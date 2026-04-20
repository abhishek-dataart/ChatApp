# Slice 2 — Profile + active sessions

Second identity-surface slice. Gives users an editable profile (display name, avatar, sound-on-message toggle) and a real Sessions page that reflects the server-side state that slice 1 already records. Nothing here changes the auth pipeline — it only exposes and mutates the data it already keeps.

## Context

`docs/implementation-plan.md` slice 2; depends on slice 1 (Identity). Slice 1 left exactly the shape this slice needs:

- `User` already has a nullable `AvatarPath` column, `Email/Username` immutable, `DisplayName` editable at the schema level but no endpoint to edit it.
- `Session` already records `UserAgent`, `Ip`, `CreatedAt`, `LastSeenAt`, `RevokedAt` — everything the Sessions screen displays.
- `SessionLookupService.Evict(byte[] cookieHash)` is the public cache-invalidation hook; `AuthService.ChangePasswordAsync` is the reference pattern for bulk revocation (sets `revoked_at` via `ExecuteUpdateAsync`, evicts affected cookie hashes).
- `server/ChatApp.Api/Controllers/{Users,Sessions}/` are `.gitkeep` placeholders. `client/src/app/features/{profile,sessions}/` are `.gitkeep` placeholders.
- `Program.cs` has no static-file middleware, no `ForwardedHeaders`, and no `IOptions<FilesOptions>` binding — slice 1 didn't need any of them.
- `infra/docker-compose.yml` already bind-mounts `../data/files` into the api at `/var/chatapp/files` and sets `ChatApp__Files__Root` to that path. The volume exists, nothing uses it yet.

Authoritative requirements that fix this slice's shape:

- **Product spec §3** — Sessions screen lists UA/IP/last-seen and must allow revoke. Revoking current session logs *this* browser out. `display_name` is editable; `email` and `username` are not.
- **Product spec §9, §10** — Profile page includes an avatar upload (≤1 MB image) and a sound-on-message toggle.
- **Arch doc §Non-functional** — authn required on all upload/download endpoints, magic-byte MIME sniff, `X-Content-Type-Options: nosniff`, ImageSharp for image processing (pure-managed, no native Docker deps).
- **Arch doc §Auth flow** — session cache is `IMemoryCache` with 30 s TTL; revocation must evict the cached entry so the decision reaches the next request within that window.

Outcome: a user can edit their display name, upload and later remove an avatar, flip the sound toggle, see the list of active sessions from two browsers with the current session clearly marked, revoke one of them (the other browser drops to `/login` on its next API call), and bulk-revoke all non-current sessions.

## Decisions

Interview answers are folded in; *[decided]* flags items that closed a genuinely open option.

| Topic | Decision | Rationale |
|---|---|---|
| Avatar storage | Per-user path `files/avatars/{userId}.webp`; ImageSharp re-encode to 256×256 square-cover WEBP q80 | *[decided]* — one file per user means re-upload simply overwrites; no orphan cleanup and no content-addressed path needed. 256 px WEBP lands well under 50 KB for typical avatars; faster to send than an unoptimised 1 MB original on every page load. |
| Avatar serving | Authorised `GET /api/profile/avatar/{userId}` with strong ETag + `Cache-Control: private, max-age=300` | *[decided]* — arch doc §Non-functional mandates authn on file downloads. The 5-minute private cache keeps repeated loads cheap without giving a CDN a copy. Anyone authenticated can read any user's avatar in this slice; tighter scoping (only contacts/room-mates) arrives naturally with slice 3/7. |
| Avatar validation | Size cap 1 MB + magic-byte sniff (png/jpeg/gif/webp), then ImageSharp re-encode | *[decided]* — the double check (sniff + decode) means a content-type-spoofed blob is rejected twice. Pre-sniff gives us a clean 415 path before spending CPU on a broken decode. No `IAttachmentScanner` — that interface belongs to slice 11's message-attachment pipeline and shouldn't bleed into profile code. |
| Avatar delete | `DELETE /api/profile/avatar` clears column + best-effort removes file | *[decided]* — symmetric with upload. Cheap to add now; retrofitting later means teaching clients that "avatar is always set after first upload", which is wrong. |
| Profile edits | Single `PATCH /api/profile { display_name?, sound_on_message? }` | *[decided]* — partial update semantics; `null`/absent fields are left alone. Avatar stays separate because multipart doesn't mix with JSON PATCH cleanly. Change-password continues to live at `/api/auth/change-password` — the Profile *page* calls it but the endpoint isn't moving. |
| Change-password UI | Inline section on the Profile page; hits existing `/api/auth/change-password` | Reuse `AuthService.ChangePasswordAsync` verbatim. No server surface change. Matches product spec §10 wording ("Profile page: … change password …"). |
| Sound default | `true`; new users default true, migration backfills existing rows to true | *[decided]* — product spec §9 says "Sound ping on new message when the browser tab is not focused. User can toggle in Profile settings." That framing implies on-by-default; users disable if they find it annoying. |
| Sessions list | `GET /api/sessions` returns only `revoked_at IS NULL` rows; fields `{ id, user_agent, ip, created_at, last_seen_at, is_current }` | *[decided]* — revoked sessions are an audit concern, not a user-facing one. `is_current` is computed server-side by comparing each row's `id` against `ICurrentUser.SessionId`; keeps the client dumb. |
| UA parsing | None — raw `User-Agent` string stored and shown | A UA-parser (UAParser.NET etc.) adds a dep and a failure mode for a cosmetic gain. Raw string is identifiable enough: the page is for self-review, not analytics. |
| Revoke self | `DELETE /api/sessions/{currentId}` is allowed and treated as logout | *[decided]* — product spec §3: "Revoking current session logs this browser out." Controller sets `revoked_at`, evicts the cache, *and* calls `CookieWriter.Clear()` so the 204 carries a `Set-Cookie: chatapp_session=; Max-Age=0`. Client's `SessionsService` detects the current-session revoke, clears local auth state, and navigates to `/login`. |
| Revoke all others | `POST /api/sessions/revoke-others` revokes every session except the current | *[decided]* — same shape as change-password revocation (bulk `ExecuteUpdateAsync` + cache eviction for each cookie hash). Cheap now, awkward to add later once users start leaving stale sessions everywhere. |
| IP capture | `UseForwardedHeaders` middleware, always on; trusts `172.16.0.0/12` + loopback; forwards `XForwardedFor | XForwardedProto` | *[decided]* — the api sits behind nginx in compose. Without this, every session row records the nginx-container IP and the Sessions page becomes useless. `KnownNetworks` scoped to the Docker subnet prevents header spoofing from the public side. |
| `MeResponse.avatar_url` | Now populated as `"/api/profile/avatar/{id}"` when `users.avatar_path` is non-null | Slice 1 shipped the field as always-null. The URL is stable (per-user, not per-upload), so clients can cache it alongside the `Me` payload. |
| User-enumeration on `GET /api/profile/avatar/{userId}` | 401 for unauth, 404 for "no avatar set" on any known user id, 404 for unknown user id | 404 for both means the endpoint is not a user-existence oracle. |

### Deferred (explicit — handed to later slices)

- Session rate-limit — slice 16.
- Avatar upload rate-limit — slice 16.
- UA parsing, geo-IP on sessions — cosmetic polish, not MVP.
- Session revoke broadcast via `PresenceHub` so other tabs react instantly — requires slice 4.
- Attachment-table integration for avatars — avatars stay denormalised on `users.avatar_path`; slice 11's `Attachment` entity is for message uploads.
- Email change, username change, account deletion — product spec §3 defers; none of them are in this slice.

## Scope

### Server — files to create

| Path | Purpose |
|------|---------|
| `server/ChatApp.Data/Migrations/{timestamp}_AddProfileSound.cs` | Generated via `dotnet ef migrations add AddProfileSound`. Adds `sound_on_message boolean NOT NULL DEFAULT true` to `users` and backfills existing rows to `true`. `avatar_path` already exists from slice 1. |
| `server/ChatApp.Domain/Services/Identity/ProfileService.cs` | `UpdateProfileAsync(userId, displayName?, soundOnMessage?)`; `SetAvatarAsync(userId, Stream, declaredContentType)` returning the new relative path; `ClearAvatarAsync(userId)`. Owns magic-byte sniffing, delegation to `AvatarImageProcessor`, the atomic temp-file write, and the DB update. |
| `server/ChatApp.Domain/Services/Identity/SessionQueryService.cs` | `ListAsync(userId, currentSessionId)` projects `SessionView` records with the `IsCurrent` flag computed in the projection. Filters `revoked_at IS NULL`; orders by `last_seen_at DESC`. |
| `server/ChatApp.Domain/Services/Identity/SessionRevocationService.cs` | `RevokeAsync(userId, sessionId)` → returns the revoked row's `CookieHash` (or null if not found / not owned). `RevokeOthersAsync(userId, currentSessionId)` → returns the list of evicted `CookieHash`es. Controller is responsible for calling `SessionLookupService.Evict` on each and clearing the client cookie when appropriate. |
| `server/ChatApp.Api/Infrastructure/Images/AvatarImageProcessor.cs` | Thin wrapper over ImageSharp: `EncodeAsync(Stream input, Stream output)` — decodes, crop-cover 256×256 via `ResizeOptions { Mode = ResizeMode.Crop, Size = new(256,256) }`, encodes WEBP at q80. Keeps ImageSharp types out of `ProfileService` so the domain project can stay infra-light. |
| `server/ChatApp.Api/Infrastructure/Configuration/FilesOptions.cs` | `public string Root { get; set; }` — bound to `ChatApp:Files` via `Configure<FilesOptions>`. One options object beats scattering `IConfiguration["ChatApp:Files:Root"]` calls. |
| `server/ChatApp.Api/Controllers/Users/ProfileController.cs` | `PATCH /api/profile`; `POST /api/profile/avatar` (multipart, `[RequestSizeLimit(1_048_576)]`, `[RequestFormLimits(MultipartBodyLengthLimit = 1_048_576)]`); `DELETE /api/profile/avatar`; `GET /api/profile/avatar/{userId:guid}`. All `[Authorize]`. |
| `server/ChatApp.Api/Controllers/Sessions/SessionsController.cs` | `GET /api/sessions`; `DELETE /api/sessions/{id:guid}`; `POST /api/sessions/revoke-others`. All `[Authorize]`. |
| `server/ChatApp.Api/Contracts/Profile/UpdateProfileRequest.cs` | `{ string? DisplayName, bool? SoundOnMessage }`. |
| `server/ChatApp.Api/Contracts/Profile/ProfileResponse.cs` | `{ Guid Id, string Email, string Username, string DisplayName, string? AvatarUrl, bool SoundOnMessage }`. Returned from `PATCH` and `POST /avatar`. |
| `server/ChatApp.Api/Contracts/Sessions/SessionView.cs` | `{ Guid Id, string UserAgent, string Ip, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool IsCurrent }`. |

### Server — files to modify

| Path | Change |
|------|--------|
| `server/ChatApp.Data/Entities/Identity/User.cs` | Add `public bool SoundOnMessage { get; set; } = true;`. |
| `server/ChatApp.Data/Configurations/Identity/UserConfiguration.cs` | `builder.Property(u => u.SoundOnMessage).HasDefaultValue(true);`. |
| `server/ChatApp.Api/Contracts/Auth/MeResponse.cs` | Add `bool SoundOnMessage`. |
| `server/ChatApp.Api/Controllers/Auth/AuthController.cs` | `ToMe()` populates `SoundOnMessage` from `User.SoundOnMessage` and sets `AvatarUrl = user.AvatarPath is null ? null : $"/api/profile/avatar/{user.Id}"`. |
| `server/ChatApp.Api/Program.cs` | Add `builder.Services.Configure<ForwardedHeadersOptions>(o => { o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto; o.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("172.16.0.0"), 12)); o.KnownNetworks.Add(new IPNetwork(IPAddress.Loopback, 8)); });`. Add `builder.Services.Configure<FilesOptions>(builder.Configuration.GetSection("ChatApp:Files"));`. Register `ProfileService`, `SessionQueryService`, `SessionRevocationService` (scoped), `AvatarImageProcessor` (singleton). Middleware: `app.UseForwardedHeaders()` as the **first** middleware call, before `UseExceptionHandler`. |
| `server/Directory.Packages.props` | Add `PackageVersion` for `SixLabors.ImageSharp`. |
| `server/ChatApp.Api/ChatApp.Api.csproj` | Add versionless `PackageReference` for `SixLabors.ImageSharp` (Api project, not Domain — keeps Domain infra-free; the processor lives in `Infrastructure/`). |

### Client — files to create

| Path | Purpose |
|------|---------|
| `client/src/app/core/profile/profile.service.ts` | Signals wrapper over the three profile endpoints. On 200 from PATCH / avatar-upload / avatar-delete, calls `AuthService.patchLocal(partial)` so the `currentUser` signal updates in place without a `/me` refetch. |
| `client/src/app/core/sessions/sessions.service.ts` | `list()`, `revoke(id)`, `revokeOthers()`. The `revoke(id)` path compares `id` to `currentUser().currentSessionId` — if they match, calls `AuthService.clearLocalSession()` and `Router.navigate(['/login'])`. |
| `client/src/app/features/profile/profile.component.{ts,html,scss}` | Reactive-form page: read-only email + username, editable display_name (1–64), avatar preview + file input (`accept="image/png,image/jpeg,image/gif,image/webp"`) + "Clear avatar" button, sound-on-message checkbox, and an inline "Change password" card with three fields calling `AuthService.changePassword`. |
| `client/src/app/features/sessions/sessions.component.{ts,html,scss}` | Table with columns `Browser/UA`, `IP`, `Created`, `Last seen`, actions. Current row highlighted and labelled "This browser"; per-row "Revoke" button; header "Revoke all other sessions" button. |

### Client — files to modify

| Path | Change |
|------|--------|
| `client/src/app/core/auth/auth.models.ts` | Add `soundOnMessage: boolean` to `MeResponse` and `ApiMe`. Add `currentSessionId: string` to `MeResponse` *only if* we choose to surface it — alternatively, the client determines it by pattern-matching the session list. **Decision: add it to `MeResponse`.** The server already knows it (via `ICurrentUser.SessionId`); pushing it to the client removes a fragile client-side guess. Update `MeResponse` on the server accordingly. |
| `client/src/app/core/auth/auth.service.ts` | Map the two new fields. Expose `patchLocal(partial: Partial<MeResponse>): void` that mutates the `currentUser` signal. Expose `clearLocalSession(): void` that sets the signal to `null`. |
| `client/src/app/features/app-shell/app-shell.component.html` | Expose "Profile" and "Sessions" links in the existing top bar alongside "Sign out". |
| `client/src/app/app.routes.ts` | Add two child routes under `/app`: `profile` → `ProfileComponent` (authGuard), `sessions` → `SessionsComponent` (authGuard). |

### Out of scope (explicit — handed to later slices)

- Session rate-limit (slice 16).
- Avatar upload rate-limit (slice 16).
- UA parsing / geo-IP (cosmetic, deferred indefinitely).
- Real-time "your session was revoked" push via `PresenceHub` (slice 4).
- Attachment-table integration for avatars (not planned — avatars stay denormalised).
- Account deletion cascade (revisited after slice 11).

## Key flows (reference)

### Update profile

1. `PATCH /api/profile { display_name?, sound_on_message? }` — `[Authorize]`.
2. Trim `display_name`; reject empty or > 64 with 400 ProblemDetails `code: "invalid_display_name"`.
3. `ProfileService.UpdateProfileAsync` updates only the supplied fields (no-op if neither present; returns 400 `code: "empty_request"`).
4. Return 200 `ProfileResponse` reflecting post-update state.

### Upload avatar

1. `POST /api/profile/avatar` multipart with a single file field `file` — `[Authorize]`.
2. Kestrel enforces 1 MB via `[RequestSizeLimit]` + `[RequestFormLimits]`.
3. Read the first 12 bytes; magic-byte sniff against {png, jpeg, gif, webp}. Mismatch → 415 `code: "unsupported_media_type"`.
4. `AvatarImageProcessor.EncodeAsync` decodes → crop-cover 256×256 → WEBP q80. Decode failure (malformed input) → 415 `code: "invalid_image"`.
5. Write to `{Files.Root}/avatars/{userId}.webp.tmp`, then atomic rename to `{userId}.webp`. This makes the replace safe under a concurrent GET.
6. Set `users.avatar_path = "avatars/{userId}.webp"`; return 200 `ProfileResponse` with `AvatarUrl = "/api/profile/avatar/{id}"`.

### Get avatar

1. `GET /api/profile/avatar/{userId:guid}` — `[Authorize]`.
2. Look up `users.avatar_path` for `userId`. Null or user missing → 404 (no distinction — not an existence oracle).
3. Resolve to `{Files.Root}/avatars/{userId}.webp`; verify the resolved path is still rooted under `Files.Root` (defence in depth against path traversal even though the input is a `Guid`).
4. Compute strong ETag from `{userId}:{file.LastWriteTimeUtc.Ticks}`; honour `If-None-Match` with 304.
5. Return `FileStreamResult` with `ContentType = "image/webp"`, `Cache-Control: private, max-age=300`, `X-Content-Type-Options: nosniff` (already global in slice 16 but set on this response now for safety).

### Delete avatar

1. `DELETE /api/profile/avatar` — `[Authorize]`.
2. `ProfileService.ClearAvatarAsync` null-outs `users.avatar_path`, then best-effort deletes `{Files.Root}/avatars/{userId}.webp` (swallow `FileNotFoundException`; log-warn on anything else).
3. Return 204.

### List sessions

1. `GET /api/sessions` — `[Authorize]`.
2. `SessionQueryService.ListAsync(me.UserId, me.SessionId)` → `WHERE user_id = @me AND revoked_at IS NULL ORDER BY last_seen_at DESC` projecting to `SessionView` with `IsCurrent = s.Id == currentSessionId`.
3. Return 200 with the array.

### Revoke one

1. `DELETE /api/sessions/{id:guid}` — `[Authorize]`.
2. `SessionRevocationService.RevokeAsync(me.UserId, id)` — `ExecuteUpdateAsync` with `WHERE id = @id AND user_id = @me AND revoked_at IS NULL`, returning the pre-update `CookieHash`. No row matched → 404.
3. Controller calls `SessionLookupService.Evict(cookieHash)` so the 30 s cache can't keep the killed session alive on the other browser.
4. If `id == me.SessionId`, controller calls `CookieWriter.Clear(HttpContext)` (so the 204 response has `Set-Cookie: chatapp_session=; Max-Age=0; Path=/; HttpOnly; Secure; SameSite=Lax`).
5. Return 204. Other browser's next request misses the cache, hits the DB, sees `RevokedAt` set, returns 401. Current browser's client layer detects the current-session revoke and navigates to `/login`.

### Revoke all others

1. `POST /api/sessions/revoke-others` — `[Authorize]`.
2. `SessionRevocationService.RevokeOthersAsync(me.UserId, me.SessionId)` — bulk `ExecuteUpdateAsync` where `user_id = @me AND id <> @current AND revoked_at IS NULL`, returning the list of affected `CookieHash`es.
3. Controller iterates `SessionLookupService.Evict(h)` for each hash.
4. Return 204.

## Verification

1. **Build clean.** `dotnet build server/ChatApp.sln` — no warnings.
2. **Migration.** `dotnet ef migrations add AddProfileSound --project server/ChatApp.Data --startup-project server/ChatApp.Api`. Inspect: single `AddColumn` for `sound_on_message boolean NOT NULL DEFAULT true` on `users`, no other changes. Commit the migration file and model snapshot.
3. **Unit tests** (xUnit, no DB):
   - `ProfileService.UpdateProfileAsync` trims `display_name` and rejects empty / > 64 chars.
   - `ProfileService.UpdateProfileAsync` returns 400-shaped error when neither field supplied.
   - Magic-byte sniff: raw PDF bytes → 415 `unsupported_media_type`; malformed-WEBP bytes that pass the sniff → 415 `invalid_image` from the encoder.
   - `SessionQueryService.ListAsync` sets `IsCurrent` for exactly one row when the current session id matches.
   - `SessionRevocationService.RevokeOthersAsync` leaves exactly the current session active and returns the expected cookie-hash list.
4. **Integration tests** (Testcontainers Postgres + `WebApplicationFactory`):
   - Register → login from two independent cookie jars → `GET /api/sessions` in each jar returns two rows with exactly one `is_current=true` (and they differ between jars).
   - From jar 1, `POST /api/sessions/revoke-others` → jar 2's next request returns 401. (Test bypasses the `IMemoryCache` 30 s TTL by resolving it and calling `Remove` on the affected hash, same pattern as slice 1's change-password test.)
   - Upload a valid PNG → `GET /api/profile/avatar/{id}` returns 200 `image/webp`.
   - Upload a PDF with `Content-Type: image/png` → 415.
   - `DELETE /api/profile/avatar` → `GET /api/profile/avatar/{id}` returns 404 and the file is gone from disk.
   - `PATCH /api/profile { sound_on_message: false }` → `GET /api/auth/me` returns `sound_on_message: false`.
   - `DELETE /api/sessions/{currentId}` → response has `Set-Cookie: chatapp_session=; Max-Age=0`; subsequent request on the same jar returns 401.
5. **Compose smoke.** `docker compose -f infra/docker-compose.yml up -d --build`. Two browsers: log in as same user in both; open `/app/sessions` in each; confirm both rows visible, correct row marked "This browser"; revoke the other from browser A; browser B's next navigation lands on `/login`. Edit display name → top bar updates without refresh.
6. **Avatar end-to-end.** Upload a 1200×800 JPEG → avatar preview renders as a 256-px square. `curl -I` with a valid cookie on `/api/profile/avatar/{id}` shows `Content-Type: image/webp`, `ETag`, `Cache-Control: private, max-age=300`.

## Follow-ups for slice 3 (Friends + personal chats)

- `FriendshipService` will live in `ChatApp.Domain/Services/Social/`; no dependency on `ProfileService` beyond reading `users.display_name` for request display.
- Contacts page should render contact avatars via the canonical `/api/profile/avatar/{id}` URL already shipped here.
- When slice 4 adds `PresenceHub`, session-revoke should also push a `SessionRevoked` event to `user:{userId}` so other tabs react in < 2 s instead of waiting for their next REST call.

## Critical files at a glance

- `server/ChatApp.Data/Migrations/{timestamp}_AddProfileSound.cs`
- `server/ChatApp.Data/Entities/Identity/User.cs`
- `server/ChatApp.Data/Configurations/Identity/UserConfiguration.cs`
- `server/ChatApp.Domain/Services/Identity/{ProfileService,SessionQueryService,SessionRevocationService}.cs`
- `server/ChatApp.Api/Controllers/Users/ProfileController.cs`
- `server/ChatApp.Api/Controllers/Sessions/SessionsController.cs`
- `server/ChatApp.Api/Infrastructure/Images/AvatarImageProcessor.cs`
- `server/ChatApp.Api/Infrastructure/Configuration/FilesOptions.cs`
- `server/ChatApp.Api/Contracts/Profile/{UpdateProfileRequest,ProfileResponse}.cs`
- `server/ChatApp.Api/Contracts/Sessions/SessionView.cs`
- `server/ChatApp.Api/Contracts/Auth/MeResponse.cs` (field additions)
- `server/ChatApp.Api/Controllers/Auth/AuthController.cs` (`ToMe` update)
- `server/ChatApp.Api/Program.cs`
- `client/src/app/core/profile/profile.service.ts`
- `client/src/app/core/sessions/sessions.service.ts`
- `client/src/app/core/auth/{auth.models.ts,auth.service.ts}`
- `client/src/app/features/profile/profile.component.{ts,html,scss}`
- `client/src/app/features/sessions/sessions.component.{ts,html,scss}`
- `client/src/app/features/app-shell/app-shell.component.html`
- `client/src/app/app.routes.ts`
