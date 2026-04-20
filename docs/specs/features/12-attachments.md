# Slice 11 — Attachments (images + generic files)

Adds the attachment pipeline the data model has always hinted at: multipart upload → magic-byte MIME sniff → pluggable scanner → image thumbnail → row returned, unlinked; message-send then flips the FK. Authorised download and thumbnail endpoints. Hourly purge of orphans.

## Context

`docs/implementation-plan.md` slice 11; depends on slice 5 (DM messaging write path) and slice 8 (room messaging). Authoritative references:

- **Arch doc §Attachment flow** — two-step flow: `POST /api/attachments` returns an unlinked row; `POST messages` carries `attachment_ids` and flips the FK. Background purge of unlinked > 1 h.
- **Arch doc §Bounded contexts — Attachments** — owns `Attachment` entity and filesystem layout under `/var/chatapp/files/{yyyy}/{mm}/{uuid}{.ext}`. `IAttachmentScanner` with `NoOpScanner` default (ClamAV deferred).
- **Arch doc §Decisions vs. spec — ClamAV/ImageSharp** — no clamav service; ImageSharp does 512-px longest-side JPEG q80 thumbs.
- **Product spec §8** — images (png/jpeg/gif/webp, ≤3 MB) and generic files (any mime, ≤20 MB); button + paste upload; per-attachment comment; authorised download with `Content-Disposition: attachment`.

Outcome: A drags or pastes an image into the DM composer → thumb preview appears beside the textarea with an optional comment field → sending posts the message with `attachmentIds`; B's client receives `MessageCreated` with the attachment payload and renders the thumbnail inline; clicking opens the original (authenticated). Same flow in a room. A third user who is not a member gets 403 on download.

## Design decisions

| Topic | Decision | Rationale |
|---|---|---|
| Upload shape | Two-step: `POST /api/attachments` (multipart, one file) returns id; message send lists `attachmentIds` | Matches arch doc; keeps upload independent of message validation; lets the client show a preview before send and cancel cleanly. |
| Multiple per message | Up to 10 attachments per message, validated server-side | User choice. Matches gallery-style UI in the product-spec wireframe. |
| Attachment kinds | `image` (png/jpeg/gif/webp, ≤3 MB) **and** `file` (any mime, ≤20 MB) in this slice | User choice (include generic files now). Unified pipeline; only the image branch runs ImageSharp + produces a thumb. |
| Per-attachment comment | Included now, nullable, ≤500 chars | User choice. Set on `POST /api/attachments` as a form field; returned in `MessageResponse`. |
| Stored layout | `{filesRoot}/attachments/{yyyy}/{MM}/{uuid}{.ext}`; thumbs as `{same}.thumb.jpg` | Matches arch doc §Attachments. Extension preserved from sniffed MIME, not the client-supplied filename. |
| MIME sniffing | Magic-byte check against the claimed MIME; reject on mismatch (`422`) for the image kind. For the `file` kind: store as `application/octet-stream` if unknown; only reject when a **claimed** image MIME disagrees with bytes | Arch doc + spec §8. |
| Scanner hook | `IAttachmentScanner` with `NoOpScanner` default wired in DI; stream rewound after scan | Arch doc requirement; later swap-in works without pipeline edits. |
| Thumbnail | Image only. ImageSharp: longest side 512 px, `JpegEncoder { Quality = 80 }` | Spec §8 and arch doc. `file` kind gets `thumbPath = null`. |
| FK flip | `POST /api/chats/.../messages` validates every id in `attachmentIds`: exists, unlinked, same uploader = caller, not expired. Sets `MessageId` in the same transaction as the `Message` insert | Arch doc §Message send sequence step 3. Prevents a user from attaching another user's pending upload. |
| Unlinked TTL | 1 h. Uploads older than that fail `attachmentIds` validation (409) and are swept by the purger | Arch doc requirement. |
| Background purge | `AttachmentPurger : BackgroundService` ticks every 10 min, deletes rows with `MessageId IS NULL AND CreatedAt < now() - 1h`, removes files from disk | Matches arch doc; interval small enough that disk doesn't bloat. |
| Authz — download | Caller must be participant in the owning scope (DM: one of the two users; Room: current member, **not** in `RoomBan`). Unlinked rows: only the uploader may read | Spec §8. Reuses `RoomPermissionService.IsMemberAsync` (existing). |
| Response headers | Original download: `Content-Disposition: attachment; filename="<original>"`, `X-Content-Type-Options: nosniff`. Thumb: `Content-Disposition: inline`, `Content-Type: image/jpeg`, `Cache-Control: private, max-age=3600` | Spec §11 security; keeps thumbs usable in `<img>` tags. |
| Kestrel limits | Set `FormOptions.MultipartBodyLengthLimit = 20 MB + slack` in `Program.cs`; per-endpoint `[RequestSizeLimit(20_971_520)]` | Server-side cap independent of proxy. |
| Rate limit | 20 uploads / min / user (spec §3); enforced via `AddRateLimiter` policy `uploads` on the attachments controller | Matches spec; attribute-annotate now with `[EnableRateLimiting("uploads")]` and add the policy in Program.cs. |

### Deferred

- ClamAV — `NoOpScanner` stays the default; arch doc §Decisions.
- Virus-signature updates, upload progress UI, resumable uploads, chunked upload.
- Cross-chat repost (attachment reuse across messages).
- EXIF strip / image re-encode for non-thumb originals (originals stored as-uploaded).
- CSRF token on multipart (slice 16).
- Avatar migration to attachments pipeline — stays separate.

---

## Scope

### Server — files to create

| Path | Purpose |
|---|---|
| `server/ChatApp.Data/Entities/Messaging/Attachment.cs` | `Id, MessageId?, UploaderId, Kind, OriginalFilename, StoredPath, Mime, SizeBytes, ThumbPath?, Comment?, CreatedAt, ScannedAt?`. `Kind` enum (`Image=0, File=1`). |
| `server/ChatApp.Data/Configurations/Messaging/AttachmentConfiguration.cs` | Table `attachments`; indexes on `(message_id)` and a filtered partial index on unlinked rows `(created_at) WHERE message_id IS NULL`. FK `message_id → messages.id ON DELETE CASCADE`. |
| `server/ChatApp.Data/Migrations/{timestamp}_AddAttachments.cs` | New migration; one table; filtered partial index on unlinked rows. |
| `server/ChatApp.Domain/Entities/AttachmentKind.cs` | Enum shared with API. |
| `server/ChatApp.Domain/Abstractions/IAttachmentScanner.cs` | `Task<AttachmentScanResult> ScanAsync(Stream content, string claimedMime, CancellationToken ct);` result = `Clean \| Infected(reason)`. |
| `server/ChatApp.Domain/Services/Attachments/NoOpScanner.cs` | Returns `Clean`. |
| `server/ChatApp.Domain/Abstractions/IAttachmentImageProcessor.cs` | `Task<byte[]> CreateThumbAsync(Stream source, CancellationToken ct);` — 512 px longest side, JPEG q80. |
| `server/ChatApp.Data/Services/Attachments/AttachmentImageProcessor.cs` | ImageSharp impl (mirrors existing `AvatarImageProcessor`). |
| `server/ChatApp.Domain/Services/Attachments/AttachmentsErrors.cs` | Constants: `FileRequired`, `UnsupportedKind`, `SizeExceeded`, `MimeMismatch`, `ScanFailed`, `ScannerRejected`, `AttachmentNotFound`, `AttachmentAlreadyLinked`, `AttachmentExpired`, `NotUploader`, `NotAuthorized`, `TooManyAttachments`, `CommentTooLong`. |
| `server/ChatApp.Domain/Services/Attachments/MagicBytes.cs` | Magic-byte detector; see format list below. |
| `server/ChatApp.Data/Services/Attachments/AttachmentService.cs` | See logic below — upload, link, resolve-for-read, purge. |
| `server/ChatApp.Api/Contracts/Attachments/UploadAttachmentResponse.cs` | `(Guid Id, string Kind, string OriginalFilename, string Mime, long SizeBytes, string? Comment, string? ThumbUrl, string DownloadUrl, DateTimeOffset CreatedAt)`. |
| `server/ChatApp.Api/Contracts/Attachments/AttachmentSummary.cs` | Embedded in `MessageResponse.Attachments` — same shape minus unlinked-only fields. |
| `server/ChatApp.Api/Controllers/Attachments/AttachmentsController.cs` | `POST /api/attachments` (multipart), `GET /api/attachments/{id}` (download), `GET /api/attachments/{id}/thumb` (inline). |
| `server/ChatApp.Api/Infrastructure/Attachments/AttachmentPurger.cs` | `BackgroundService` — 10-min tick; deletes unlinked > 1 h and their files. |
| `server/ChatApp.Api/Infrastructure/Attachments/AttachmentsOptions.cs` | Binds `ChatApp:Attachments` section: `MaxImageBytes=3145728`, `MaxFileBytes=20971520`, `MaxPerMessage=10`, `UnlinkedTtlMinutes=60`, `PurgeIntervalMinutes=10`. |

#### `AttachmentsController` sketch

```csharp
[ApiController, Route("api/attachments"), Authorize]
public sealed class AttachmentsController(
    AttachmentService attachments,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 20 * 1024 * 1024)]
    [EnableRateLimiting("uploads")]
    public async Task<ActionResult<UploadAttachmentResponse>> Upload(
        [FromForm] IFormFile file,
        [FromForm] string kind,        // "image" | "file"
        [FromForm] string? comment,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0) return UnprocessableEntity(AttachmentsErrors.FileRequired);
        await using var stream = file.OpenReadStream();
        var result = await attachments.UploadAsync(
            currentUser.Id, kind, file.ContentType, file.FileName, file.Length, comment, stream, ct);
        return result switch
        {
            { Ok: true }                                       => Ok(result.Value),
            { Code: AttachmentsErrors.UnsupportedKind }        => UnprocessableEntity(result.Code),
            { Code: AttachmentsErrors.SizeExceeded }           => UnprocessableEntity(result.Code),
            { Code: AttachmentsErrors.MimeMismatch }           => UnprocessableEntity(result.Code),
            { Code: AttachmentsErrors.CommentTooLong }         => UnprocessableEntity(result.Code),
            { Code: AttachmentsErrors.ScannerRejected }        => UnprocessableEntity(result.Code),
            _                                                  => StatusCode(500)
        };
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var result = await attachments.OpenOriginalAsync(currentUser.Id, id, ct);
        if (!result.Ok) return MapReadError(result.Code);
        var (stream, mime, filename) = result.Value!;
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stream, mime, filename);  // Content-Disposition: attachment set by filename overload
    }

    [HttpGet("{id:guid}/thumb")]
    public async Task<IActionResult> Thumb(Guid id, CancellationToken ct)
    {
        var result = await attachments.OpenThumbAsync(currentUser.Id, id, ct);
        if (!result.Ok) return MapReadError(result.Code);
        Response.Headers["Cache-Control"] = "private, max-age=3600";
        return File(result.Value!, "image/jpeg");  // inline
    }
}
```

#### `AttachmentService` — core logic

```csharp
public async Task<(bool Ok, string? Code, string? Message, UploadAttachmentResponse? Value)>
    UploadAsync(Guid uploaderId, string kindRaw, string claimedMime, string originalFilename,
                long size, string? comment, Stream content, CancellationToken ct)
{
    // 1. Parse kind + size cap
    if (!Enum.TryParse<AttachmentKind>(kindRaw, ignoreCase: true, out var kind))
        return Fail(AttachmentsErrors.UnsupportedKind);
    var cap = kind == AttachmentKind.Image ? opts.MaxImageBytes : opts.MaxFileBytes;
    if (size > cap) return Fail(AttachmentsErrors.SizeExceeded);
    if (comment is { Length: > 500 }) return Fail(AttachmentsErrors.CommentTooLong);

    // 2. Buffer to a seekable temp file — sniff + scan + (thumb) all need rewinds
    var tmpPath = Path.Combine(Path.GetTempPath(), $"upload-{Guid.NewGuid():N}");
    await using (var tmp = File.Create(tmpPath)) await content.CopyToAsync(tmp, ct);
    try
    {
        // 3. Magic-byte sniff. For image kind: require match + map to canonical mime.
        await using var sniffStream = File.OpenRead(tmpPath);
        var sniffed = MagicBytes.Detect(sniffStream);            // returns ("image/png", ".png") etc.
        sniffStream.Position = 0;
        if (kind == AttachmentKind.Image)
        {
            if (sniffed is null || !ImageMimes.Contains(sniffed.Mime) ||
                !MimeAgreesWith(claimedMime, sniffed.Mime))
                return Fail(AttachmentsErrors.MimeMismatch);
        }
        var finalMime = sniffed?.Mime ?? "application/octet-stream";
        var finalExt  = sniffed?.Extension ?? SafeExtFrom(originalFilename);

        // 4. Scanner hook
        await using var scanStream = File.OpenRead(tmpPath);
        var scan = await scanner.ScanAsync(scanStream, finalMime, ct);
        if (scan is AttachmentScanResult.Infected) return Fail(AttachmentsErrors.ScannerRejected);

        // 5. Disk layout + copy
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var relDir = Path.Combine("attachments", now.ToString("yyyy"), now.ToString("MM"));
        Directory.CreateDirectory(Path.Combine(filesRoot, relDir));
        var relPath = Path.Combine(relDir, $"{id:N}{finalExt}");
        File.Move(tmpPath, Path.Combine(filesRoot, relPath));
        tmpPath = null;  // moved

        // 6. Image thumb
        string? thumbRelPath = null;
        if (kind == AttachmentKind.Image)
        {
            await using var src = File.OpenRead(Path.Combine(filesRoot, relPath));
            var thumbBytes = await imageProcessor.CreateThumbAsync(src, ct);
            thumbRelPath = relPath + ".thumb.jpg";
            await File.WriteAllBytesAsync(Path.Combine(filesRoot, thumbRelPath), thumbBytes, ct);
        }

        // 7. Persist row (MessageId null)
        var row = new Attachment {
            Id = id, UploaderId = uploaderId, Kind = kind,
            OriginalFilename = SafeName(originalFilename), StoredPath = relPath,
            ThumbPath = thumbRelPath, Mime = finalMime, SizeBytes = size,
            Comment = comment, CreatedAt = now, ScannedAt = now
        };
        db.Attachments.Add(row);
        await db.SaveChangesAsync(ct);

        return Ok(new UploadAttachmentResponse(
            row.Id, kind.ToString().ToLowerInvariant(), row.OriginalFilename, row.Mime,
            row.SizeBytes, row.Comment,
            thumbRelPath is null ? null : $"/api/attachments/{row.Id}/thumb",
            $"/api/attachments/{row.Id}", row.CreatedAt));
    }
    finally
    {
        if (tmpPath is not null && File.Exists(tmpPath)) File.Delete(tmpPath);
    }
}

public async Task<(bool Ok, string? Code, List<Attachment>? Rows)>
    ValidateForLinkAsync(Guid uploaderId, IReadOnlyList<Guid> ids, CancellationToken ct)
{
    if (ids.Count == 0) return (true, null, new List<Attachment>());
    if (ids.Count > opts.MaxPerMessage) return (false, AttachmentsErrors.TooManyAttachments, null);
    var rows = await db.Attachments.Where(a => ids.Contains(a.Id)).ToListAsync(ct);
    if (rows.Count != ids.Count)                   return (false, AttachmentsErrors.AttachmentNotFound, null);
    if (rows.Any(a => a.UploaderId != uploaderId)) return (false, AttachmentsErrors.NotUploader, null);
    if (rows.Any(a => a.MessageId is not null))    return (false, AttachmentsErrors.AttachmentAlreadyLinked, null);
    var cutoff = DateTimeOffset.UtcNow.AddMinutes(-opts.UnlinkedTtlMinutes);
    if (rows.Any(a => a.CreatedAt < cutoff))       return (false, AttachmentsErrors.AttachmentExpired, null);
    return (true, null, rows);
}
```

Read-path (`OpenOriginalAsync` / `OpenThumbAsync`): load the attachment; if `MessageId IS NULL` allow only the uploader; otherwise load the parent message and delegate:

- `scope == personal` → caller must be `user_a_id` or `user_b_id` on the personal chat.
- `scope == room` → `RoomPermissionService.IsMemberAsync(roomId, callerId)` AND not present in `RoomBan`.

### Server — files to modify

| Path | Change |
|---|---|
| `server/ChatApp.Data/ChatDbContext.cs` | Add `DbSet<Attachment> Attachments`; apply `AttachmentConfiguration`. |
| `server/ChatApp.Api/Contracts/Messages/SendMessageRequest.cs` | Add `List<Guid>? AttachmentIds`. |
| `server/ChatApp.Api/Contracts/Messages/MessageResponse.cs` | Add `IReadOnlyList<AttachmentSummary> Attachments` (empty list when none). |
| `server/ChatApp.Domain/Services/Messaging/MessagePayload.cs` | Same addition. |
| `server/ChatApp.Data/Services/Messaging/MessageService.cs` | In `SendAsync` + `SendToRoomAsync`: call `AttachmentService.ValidateForLinkAsync`; after inserting the message, set `MessageId` on the returned rows and `SaveChangesAsync` in the same transaction. Surface new error codes. Update `BuildPayloadAsync` to include attachment summaries (`LEFT JOIN` via `.Select` projection to avoid N+1, matching the reply-parent approach in slice 10). History queries hydrate attachments the same way. |
| `server/ChatApp.Domain/Services/Messaging/MessagingErrors.cs` | Add `AttachmentValidationFailed` passthrough constants or reuse attachments errors directly from the send controller. |
| `server/ChatApp.Api/Controllers/Messaging/*MessagesController.cs` (DM + room send) | Map attachment error codes to `422`/`409`/`404` appropriately. |
| `server/ChatApp.Api/Program.cs` | Register `AttachmentService`, `IAttachmentScanner → NoOpScanner`, `IAttachmentImageProcessor → AttachmentImageProcessor` (Scoped / Singleton as appropriate); bind `AttachmentsOptions`; register `AttachmentPurger` as a `HostedService`; configure `FormOptions.MultipartBodyLengthLimit`; add rate-limit policy `uploads` (fixed window 20/min keyed on user id). |
| `server/ChatApp.Api/appsettings.json` | Add `ChatApp:Attachments` section with defaults above. |

### Magic-byte detector

New helper `server/ChatApp.Domain/Services/Attachments/MagicBytes.cs`:

- PNG: `89 50 4E 47 0D 0A 1A 0A` → `image/png`, `.png`
- JPEG: `FF D8 FF` → `image/jpeg`, `.jpg`
- GIF: `47 49 46 38 {37|39} 61` → `image/gif`, `.gif`
- WEBP: `52 49 46 46 .. .. .. .. 57 45 42 50` → `image/webp`, `.webp`
- Returns `null` for anything else (fine for generic files — they keep `application/octet-stream`).

### Client — files to create / modify

| Path | Change |
|---|---|
| `client/src/app/core/messaging/messaging.models.ts` | Add `AttachmentSummary { id; kind: 'image' \| 'file'; originalFilename; mime; sizeBytes; comment: string \| null; thumbUrl: string \| null; downloadUrl: string; createdAt; }`. Add `attachments: AttachmentSummary[]` to `MessageResponse`. Add `attachmentIds?: string[]` to `SendMessageRequest`. New `UploadAttachmentResponse` mirroring server. New `PendingAttachment` view-model for composer state (localId, file, previewUrl, status: `uploading\|ready\|failed`, uploadId?, comment). |
| `client/src/app/core/messaging/attachments.service.ts` *(new)* | `upload(file: File, comment?: string): Promise<UploadAttachmentResponse>` — POST multipart to `/api/attachments`. Exposes a small helper `pickKind(file: File): 'image' \| 'file'` based on `file.type`. |
| `client/src/app/core/messaging/dm.service.ts` + `room-messaging.service.ts` | `send()` takes optional `attachmentIds: string[]`; include in the JSON body. Signal handlers already get `attachments` through the extended `MessageResponse` — no hub-level changes. |
| `client/src/app/shared/messaging/message-composer.component.ts` + `.html` + `.scss` | Add: (1) paperclip button opening `<input type="file" multiple accept="image/*,*/*">`; (2) drag-and-drop overlay on the composer (dragover/dragleave/drop handlers); (3) paste handler on the textarea (`(paste)="onPaste($event)"` reading `clipboardData.items`). Maintain a `pending = signal<PendingAttachment[]>([])`. Show a horizontal strip of previews above the textarea (thumbnail for images, generic icon + filename for files), each with a small X to remove and an editable comment field (≤500 chars). Disable Send while any attachment is `uploading`. Emit `{ body, replyToId, attachmentIds }` where `attachmentIds = pending().filter(p => p.status === 'ready').map(p => p.uploadId)`. |
| `client/src/app/shared/messaging/message-list.component.html` + `.ts` | After the body, render `message.attachments`: images as `<img [src]="a.thumbUrl" (click)="open(a.downloadUrl)">` with the comment below; files as a row with filename + size + download link. Clicking the image opens the original in a new tab (`window.open(a.downloadUrl, '_blank')`). |
| `client/src/app/core/http/credentials.interceptor.ts` | No change — `withCredentials` already covers multipart. |

#### Composer paste + drop UX

```typescript
onPaste(event: ClipboardEvent) {
  const items = event.clipboardData?.items ?? [];
  for (const item of items) {
    if (item.kind === 'file') {
      const file = item.getAsFile();
      if (file) this.queue(file);
      event.preventDefault();   // don't paste image binary into textarea
    }
  }
}
onDrop(event: DragEvent) {
  event.preventDefault();
  for (const file of Array.from(event.dataTransfer?.files ?? [])) this.queue(file);
}
private async queue(file: File) {
  const kind = file.type.startsWith('image/') ? 'image' : 'file';
  const previewUrl = kind === 'image' ? URL.createObjectURL(file) : null;
  const local = { localId: crypto.randomUUID(), file, previewUrl, status: 'uploading' as const, comment: '' };
  this.pending.update(ps => [...ps, local]);
  try {
    const result = await this.attachments.upload(file, local.comment);
    this.pending.update(ps => ps.map(p => p.localId === local.localId
      ? { ...p, uploadId: result.id, status: 'ready' } : p));
  } catch (e) {
    this.pending.update(ps => ps.map(p => p.localId === local.localId
      ? { ...p, status: 'failed' } : p));
  }
}
```

---

## Key flows

### Image upload + send (DM)

1. User drops a PNG onto the composer → `onDrop` queues a `PendingAttachment`; `AttachmentsService.upload(file, '')` POSTs multipart to `/api/attachments`.
2. Server: Kestrel enforces 20 MB; controller receives `IFormFile`; `AttachmentService.UploadAsync` runs size cap (3 MB for image), magic-byte sniff, `NoOpScanner.ScanAsync`, ImageSharp thumb, moves file to `files/attachments/{yyyy}/{MM}/{uuid}.png`, writes `.thumb.jpg`, inserts row with `MessageId = null`.
3. Response returns `{ id, thumbUrl, downloadUrl, ... }`. Client flips pending entry to `ready`; thumb preview visible.
4. User types, hits Send → `send(chatId, body, replyToId, [pending[0].uploadId])`.
5. `MessageService.SendAsync` authorises DM, validates body, calls `AttachmentService.ValidateForLinkAsync` (uploader match, unlinked, within TTL, count ≤ 10), inserts the `Message`, sets `MessageId` on each attachment, `SaveChangesAsync` in one transaction.
6. `BuildPayloadAsync` projects `MessagePayload.Attachments`; `ChatBroadcaster.BroadcastMessageCreatedToPersonalChatAsync` pushes `MessageCreated` to `pchat:{id}` — recipients render the inline thumb.

### Download authz

1. `GET /api/attachments/{id}` from a user who was banned from the owning room.
2. `AttachmentService.OpenOriginalAsync` loads the attachment → loads the parent `Message` (`scope=room`, `roomId=R`) → `RoomPermissionService.IsMemberAsync(R, caller)` returns `false` (membership row removed when banned) → returns `NotAuthorized`.
3. Controller maps to `403`.

### Orphan purge

1. Every 10 min, `AttachmentPurger.ExecuteAsync` tick queries `Attachments.Where(a => a.MessageId == null && a.CreatedAt < now - 1h)`.
2. For each, delete the disk file (+ thumb if present), then remove the row. Transactional per-attachment so a single disk failure doesn't block the batch.
3. On service shutdown, the loop exits cleanly via the cancellation token.

---

## Tests — files to create

| Path | Purpose |
|---|---|
| `server/ChatApp.Tests/Unit/Attachments/MagicBytes_Tests.cs` | Each supported format detected; EICAR/zip rejected for image kind; truncated streams return null. |
| `server/ChatApp.Tests/Unit/Attachments/AttachmentService_Tests.cs` | In-memory EF + fake scanner + fake image processor + temp filesRoot: `Upload_RejectsOversizeImage`; `Upload_RejectsMimeMismatch`; `Upload_RejectsScannerInfected` (scanner returns `Infected`); `Upload_WritesOriginalAndThumb`; `Upload_FileKindSkipsThumb`; `ValidateForLink_RejectsOtherUploader`; `ValidateForLink_RejectsAlreadyLinked`; `ValidateForLink_RejectsExpired`; `ValidateForLink_RejectsTooMany`. |
| `server/ChatApp.Tests/Integration/Attachments/AttachmentsIntegrationTests.cs` | `ChatApiFactory` + Testcontainers: (1) A uploads PNG via multipart, GETs thumb (200), GETs original (200, `Content-Disposition: attachment`). (2) A attaches to DM → B receives `MessageCreated` with attachment populated. (3) C (non-participant) `GET /api/attachments/{id}` → 403. (4) A uploads image then does NOT send within TTL → after purger tick the row + files are gone (test drives the purger loop directly via `AttachmentPurger.PurgeOnceAsync` seam). (5) Registering an always-reject `IAttachmentScanner` via DI override → upload returns 422 (proves the hook wires end-to-end, per arch-doc verification §5). |
| `server/ChatApp.Tests/Integration/Attachments/RoomAttachmentsTests.cs` | A posts image in room `R`; admin bans A; A tries `GET /api/attachments/{id}` → 403. Room hard-delete removes attachment rows and files from disk. |

---

## Verification

1. **Build clean.** `dotnet build server/ChatApp.sln` zero warnings; `npm run build` zero errors in `client/`.
2. **Migration applies.** `dotnet ef migrations add AddAttachments` produces one up-script; `dotnet ef database update` on a dev DB is a no-op on rerun; `db.Database.Migrate()` on startup applies cleanly on a fresh volume.
3. **Unit tests.** `dotnet test --filter "FullyQualifiedName~Attachments"` — all pass.
4. **Integration tests.** All four groups above green via Testcontainers.
5. **Compose smoke — image DM.** Two browsers (A, B) in a DM. A drops a JPEG into the composer → thumb preview appears → hits Send. B sees the message with an inline thumb; clicking opens the original in a new tab (authenticated download).
6. **Compose smoke — paste + file.** A pastes a screenshot from clipboard into the composer → same upload flow. A attaches a `.pdf` (kind `file`) → B sees a filename + size + download link (no thumb).
7. **Compose smoke — room authz.** A posts an image in room `R`. Admin bans A. A hits the download URL from a stale tab → 403.
8. **Compose smoke — orphan purge.** Upload an image and close the tab without sending; `ls data/files/attachments` shows the file; after ~1 h (or manually triggering the purger via a debug endpoint in `Development` only) the file and row disappear.
9. **Security smoke.** Upload an `.exe` renamed to `image.png` with `Content-Type: image/png` → 422 on mime mismatch. Register a fake `IAttachmentScanner` that returns `Infected` → upload returns 422 (proves the hook).

---

## Critical files at a glance

**New — server:**
- `server/ChatApp.Data/Entities/Messaging/Attachment.cs`
- `server/ChatApp.Data/Configurations/Messaging/AttachmentConfiguration.cs`
- `server/ChatApp.Data/Migrations/{timestamp}_AddAttachments.cs`
- `server/ChatApp.Domain/Entities/AttachmentKind.cs`
- `server/ChatApp.Domain/Abstractions/IAttachmentScanner.cs`
- `server/ChatApp.Domain/Abstractions/IAttachmentImageProcessor.cs`
- `server/ChatApp.Domain/Services/Attachments/{NoOpScanner,MagicBytes,AttachmentsErrors}.cs`
- `server/ChatApp.Data/Services/Attachments/{AttachmentService,AttachmentImageProcessor}.cs`
- `server/ChatApp.Api/Contracts/Attachments/{UploadAttachmentResponse,AttachmentSummary}.cs`
- `server/ChatApp.Api/Controllers/Attachments/AttachmentsController.cs`
- `server/ChatApp.Api/Infrastructure/Attachments/{AttachmentPurger,AttachmentsOptions}.cs`

**Modified — server:**
- `server/ChatApp.Data/ChatDbContext.cs` *(DbSet + config)*
- `server/ChatApp.Api/Contracts/Messages/{SendMessageRequest,MessageResponse}.cs`
- `server/ChatApp.Domain/Services/Messaging/MessagePayload.cs`
- `server/ChatApp.Data/Services/Messaging/MessageService.cs` *(validate + link + project)*
- `server/ChatApp.Api/Program.cs` *(DI, hosted service, form limits, rate-limit policy)*
- `server/ChatApp.Api/appsettings.json` *(ChatApp:Attachments)*

**New — tests:**
- `server/ChatApp.Tests/Unit/Attachments/MagicBytes_Tests.cs`
- `server/ChatApp.Tests/Unit/Attachments/AttachmentService_Tests.cs`
- `server/ChatApp.Tests/Integration/Attachments/AttachmentsIntegrationTests.cs`
- `server/ChatApp.Tests/Integration/Attachments/RoomAttachmentsTests.cs`

**Modified — client:**
- `client/src/app/core/messaging/messaging.models.ts`
- `client/src/app/core/messaging/{dm.service.ts,room-messaging.service.ts}`
- `client/src/app/shared/messaging/message-composer.{ts,html,scss}`
- `client/src/app/shared/messaging/message-list.{ts,html}`

**New — client:**
- `client/src/app/core/messaging/attachments.service.ts`
