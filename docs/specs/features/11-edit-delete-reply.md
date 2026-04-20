# Slice 10 — Edit / Delete / Reply

Activates the three message-mutation features that the data model already supports. `Message`
already carries `EditedAt`, `DeletedAt`, and `ReplyToId`; the send path already accepts
`replyToId` in `SendMessageRequest`. This slice adds edit/delete REST endpoints and hub
broadcasts, fixes the history queries to exclude deleted messages, denormalizes reply-parent
info into `MessageResponse`, and wires the client UI.

## Context

`docs/implementation-plan.md` slice 10; depends on slice 8 (room messaging) and slice 9
(unread markers). Authoritative references:

- **Arch doc §Message send sequence** — REST write → hub broadcast pattern; `ChatHub`
  carries `MessageEdited` and `MessageDeleted` in addition to `MessageCreated`.
- **Arch doc §Bounded contexts — Messaging** — edit/delete, replies in scope.
- **Product spec §7.3** — message author may edit or delete their own message; room
  admin/owner may delete any room message.

Outcome: A edits a message → B sees the updated body and an `(edited)` label in real-time.
A deletes a message → it disappears from all clients' lists. C clicks Reply on a message →
a quote bar appears above the input; the sent message renders the parent snippet inline.

## Design decisions

| Topic | Decision | Rationale |
|---|---|---|
| Endpoint shape | `PUT /api/messages/{id}` and `DELETE /api/messages/{id}` on a new `MessagesController` | Message ID is globally unique; controller can determine scope from the loaded entity. Matches the arch-doc endpoint table. |
| Tombstone visibility | Deleted messages removed entirely from client list | User choice. `GetHistoryAsync` / `GetRoomHistoryAsync` filter `deleted_at IS NULL`; `MessageDeleted` event splices from the client signal. |
| Edit authz | Author only; no time limit | User choice. Simpler; room admins cannot alter another user's words. |
| Delete authz | Author OR room admin/owner for room messages; author only for personal-chat messages | User choice. Keeps moderation power without changing authorship records. |
| Edit UX | In-place textarea inside the message bubble; Save / Cancel inline | User choice. |
| Reply denormalization | `replyToBody` (first 200 chars) and `replyToAuthorDisplayName` included in `MessageResponse` and `MessagePayload` | Avoids a second round-trip per message; capped at 200 chars to bound payload size. If the parent is deleted, both fields are `null` and the client shows "Original message deleted". |
| `(edited)` label | Shown when `editedAt != null` | Standard chat-app convention; conveys transparency without storing full edit history. |

### Deferred

- Edit history / audit log — not in scope.
- Undo edit (revert to original body) — not in scope.
- Sound ping on edit/delete — not in scope.
- Rate limiting on edit/delete — slice 16.
- Unread count adjustment on delete — not needed (counts are per-chat-open).

---

## Scope

### Server — files to create

| Path | Purpose |
|---|---|
| `server/ChatApp.Api/Contracts/Messages/EditMessageRequest.cs` | `public sealed record EditMessageRequest(string Body);` |
| `server/ChatApp.Domain/Services/Messaging/MessageDeletedPayload.cs` | `public sealed record MessageDeletedPayload(Guid Id, string Scope, Guid? PersonalChatId, Guid? RoomId);` — passed through `IChatBroadcaster`. |
| `server/ChatApp.Api/Contracts/Messages/MessageDeletedResponse.cs` | `public sealed record MessageDeletedResponse(Guid Id, string Scope, Guid? PersonalChatId, Guid? RoomId);` — SignalR payload shape. |
| `server/ChatApp.Api/Controllers/Messages/MessagesController.cs` | See logic below. |

#### `MessagesController` sketch

```csharp
[ApiController, Route("api/messages"), Authorize]
public sealed class MessagesController(
    MessageService messages,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MessageResponse>> Edit(
        Guid id, EditMessageRequest request, CancellationToken ct)
    {
        var me = currentUser.Id;
        var result = await messages.EditAsync(me, id, request.Body, ct);
        return result switch
        {
            { IsSuccess: true } => Ok(result.Value),
            { Error: MessagingErrors.MessageNotFound } => NotFound(),
            { Error: MessagingErrors.NotAuthor }       => Forbid(),
            { Error: MessagingErrors.BodyEmpty }       => UnprocessableEntity(result.Error),
            { Error: MessagingErrors.BodyTooLong }     => UnprocessableEntity(result.Error),
            { Error: MessagingErrors.MessageAlreadyDeleted } => Conflict(result.Error),
            _ => StatusCode(500)
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var me = currentUser.Id;
        var result = await messages.DeleteAsync(me, id, ct);
        return result switch
        {
            { IsSuccess: true }                        => NoContent(),
            { Error: MessagingErrors.MessageNotFound } => NotFound(),
            { Error: MessagingErrors.NotAuthorized }   => Forbid(),
            { Error: MessagingErrors.MessageAlreadyDeleted } => Conflict(result.Error),
            _ => StatusCode(500)
        };
    }
}
```

### Server — files to modify

| Path | Change |
|---|---|
| `server/ChatApp.Api/Contracts/Messages/MessageResponse.cs` | Add `string? ReplyToBody` and `string? ReplyToAuthorDisplayName` (nullable). |
| `server/ChatApp.Domain/Services/Messaging/MessagePayload.cs` | Add same two nullable fields. |
| `server/ChatApp.Domain/Abstractions/IChatBroadcaster.cs` | Add `Task BroadcastMessageEditedAsync(Guid scopeId, MessageScope scope, MessagePayload payload, CancellationToken ct = default);` and `Task BroadcastMessageDeletedAsync(Guid scopeId, MessageScope scope, MessageDeletedPayload payload, CancellationToken ct = default);` |
| `server/ChatApp.Api/Hubs/ChatBroadcaster.cs` | Implement both: resolve group name via `ChatGroups.PersonalChat(scopeId)` or `ChatGroups.Room(scopeId)`; send `"MessageEdited"` / `"MessageDeleted"` events. |
| `server/ChatApp.Data/Services/Messaging/MessageService.cs` | Add `EditAsync` + `DeleteAsync` (see logic below); fix `GetHistoryAsync` and `GetRoomHistoryAsync` to add `.Where(m => m.DeletedAt == null)`; add reply-parent lookup to history queries and to `SendAsync`/`SendToRoomAsync`. |
| `server/ChatApp.Domain/Services/Messaging/MessagingErrors.cs` | Add constants `MessageNotFound`, `NotAuthor`, `NotAuthorized`, `MessageAlreadyDeleted`. |

#### `MessageService.EditAsync` logic

```csharp
public async Task<Result<MessagePayload>> EditAsync(
    Guid editorId, Guid messageId, string body, CancellationToken ct)
{
    body = body.Trim();
    if (string.IsNullOrEmpty(body))    return Result.Fail(MessagingErrors.BodyEmpty);
    if (Encoding.UTF8.GetByteCount(body) > 3072) return Result.Fail(MessagingErrors.BodyTooLong);

    var msg = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId, ct);
    if (msg is null)             return Result.Fail(MessagingErrors.MessageNotFound);
    if (msg.DeletedAt is not null) return Result.Fail(MessagingErrors.MessageAlreadyDeleted);
    if (msg.AuthorId != editorId) return Result.Fail(MessagingErrors.NotAuthor);

    msg.Body = body;
    msg.EditedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);

    var payload = await BuildPayloadAsync(msg, ct);
    var scopeId = msg.Scope == MessageScope.Personal ? msg.PersonalChatId!.Value : msg.RoomId!.Value;
    await broadcaster.BroadcastMessageEditedAsync(scopeId, msg.Scope, payload, ct);
    return Result.Ok(payload);
}
```

#### `MessageService.DeleteAsync` logic

```csharp
public async Task<Result> DeleteAsync(Guid deleterId, Guid messageId, CancellationToken ct)
{
    var msg = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId, ct);
    if (msg is null)               return Result.Fail(MessagingErrors.MessageNotFound);
    if (msg.DeletedAt is not null) return Result.Fail(MessagingErrors.MessageAlreadyDeleted);

    bool authorized = msg.AuthorId == deleterId;
    if (!authorized && msg.Scope == MessageScope.Room)
        authorized = await roomPermissions.IsAdminOrOwnerAsync(msg.RoomId!.Value, deleterId, ct);
    if (!authorized) return Result.Fail(MessagingErrors.NotAuthorized);

    msg.DeletedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);

    var scopeId = msg.Scope == MessageScope.Personal ? msg.PersonalChatId!.Value : msg.RoomId!.Value;
    var scopeStr = msg.Scope == MessageScope.Personal ? "personal" : "room";
    var payload = new MessageDeletedPayload(msg.Id, scopeStr, msg.PersonalChatId, msg.RoomId);
    await broadcaster.BroadcastMessageDeletedAsync(scopeId, msg.Scope, payload, ct);
    return Result.Ok();
}
```

#### Reply-parent denormalization in `SendAsync` / `GetHistoryAsync`

For each message that has a non-null `ReplyToId`:

```csharp
// After loading message(s)
private async Task<(string? body, string? authorName)> GetReplyParentAsync(
    Guid? replyToId, CancellationToken ct)
{
    if (replyToId is null) return (null, null);
    var parent = await db.Messages
        .AsNoTracking()
        .Include(m => m.Author)
        .FirstOrDefaultAsync(m => m.Id == replyToId.Value, ct);
    if (parent is null || parent.DeletedAt is not null) return (null, null);
    var snippet = parent.Body.Length > 200 ? parent.Body[..200] : parent.Body;
    return (snippet, parent.Author.DisplayName);
}
```

History queries: use EF `Include` + `ThenInclude` or a `.Select` projection that LEFT JOINs
the parent message and its author in one query (avoids N+1).

```csharp
// Example projection in GetHistoryAsync
var messages = await db.Messages
    .AsNoTracking()
    .Where(m => m.PersonalChatId == chatId && m.DeletedAt == null)
    .OrderByDescending(m => m.CreatedAt)
    .Take(50)
    .Select(m => new
    {
        Message = m,
        Author = db.Users.First(u => u.Id == m.AuthorId),
        ReplyParent = m.ReplyToId != null
            ? db.Messages
                .Where(p => p.Id == m.ReplyToId && p.DeletedAt == null)
                .Select(p => new { p.Body, AuthorName = db.Users.First(u => u.Id == p.AuthorId).DisplayName })
                .FirstOrDefault()
            : null
    })
    .ToListAsync(ct);
```

### Client — files to modify

| Path | Change |
|---|---|
| `client/src/app/core/messaging/messaging.models.ts` | Add `replyToBody: string \| null`, `replyToAuthorDisplayName: string \| null` to `MessageResponse`; add `MessageEditedPayload { id: string; body: string; editedAt: string; }`, `MessageDeletedPayload { id: string; scope: 'personal' \| 'room'; personalChatId: string \| null; roomId: string \| null; }`, `EditMessageRequest { body: string }`. |
| `client/src/app/core/messaging/dm.service.ts` | Add `edit(id, body)` → `PUT /api/messages/{id}`; `deleteMessage(id)` → `DELETE /api/messages/{id}`; in `subscribe()` add handlers for `MessageEdited` (update matching message in `_messages`) and `MessageDeleted` (filter message out of `_messages`). Expose `replyingTo = signal<MessageResponse \| null>(null)` and `setReplyTo(msg) / clearReplyTo()` helpers; pass `replyingTo().id` as `replyToId` in `send()`. |
| `client/src/app/core/messaging/room-messaging.service.ts` | Same additions as `DmService`. |
| Message bubble component(s) | On hover / focus: show `[✎]` and `[🗑]` action buttons on own messages (show `[🗑]` to room admins too); clicking `[✎]` enters edit mode (textarea pre-filled with body, Save / Cancel buttons); clicking `[🗑]` calls `deleteMessage(id)` with confirmation; render reply-quote block above body when `replyToId` is set (show `replyToAuthorDisplayName` + truncated `replyToBody`, or "Original message deleted" if both null). Show `(edited)` label when `editedAt != null`. |
| Composer / input component(s) | When `replyingTo()` signal is non-null, render a dismissible reply bar above the textarea showing parent author + snippet; Cancel button calls `clearReplyTo()`; clicking Reply on any message calls `setReplyTo(msg)`. |

---

## Key flows

### Edit a message

1. Author clicks `[✎]` on their message → bubble switches to edit mode.
2. Author updates text, clicks Save → `DmService.edit(id, newBody)` → `PUT /api/messages/{id}`.
3. `MessageService.EditAsync`: validates body; sets `Body` + `EditedAt`; saves; broadcasts
   `MessageEdited` to `pchat:{chatId}` or `room:{roomId}` group.
4. All clients in the chat receive `MessageEdited` → `_messages` updated in place →
   bubble re-renders with new body and `(edited)` label.

### Delete a message (author)

1. Author clicks `[🗑]` → confirmation prompt → `DmService.deleteMessage(id)` →
   `DELETE /api/messages/{id}`.
2. `MessageService.DeleteAsync`: verifies author; sets `DeletedAt`; broadcasts `MessageDeleted`.
3. All clients receive `MessageDeleted` → message spliced from `_messages` → disappears.

### Delete a message (room admin)

Same as above but the request is made by a user who is admin/owner of the room.
`IsAdminOrOwnerAsync` returns `true` → proceeds to soft-delete + broadcast.

### Send a reply

1. User clicks Reply on a message → `setReplyTo(msg)` → reply bar appears above input.
2. User types and sends → `send(chatId, body, replyingTo().id)`.
3. Server persists message with `ReplyToId` set; fetches parent snippet.
4. `MessageCreated` broadcast includes `replyToBody` + `replyToAuthorDisplayName`.
5. All clients render the message with a quote block above the body.

---

## Tests — files to create

| Path | Purpose |
|---|---|
| `server/ChatApp.Tests/Unit/Messaging/MessageService_EditDelete_Tests.cs` | Unit tests with in-memory EF + mock `IChatBroadcaster` + mock `RoomPermissionService`: `EditAsync_UpdatesBodyAndBroadcasts`; `EditAsync_RejectsNonAuthor`; `EditAsync_RejectsAlreadyDeleted`; `EditAsync_RejectsEmptyBody`; `DeleteAsync_SoftDeletesAndBroadcasts`; `DeleteAsync_AllowsRoomAdminToDeleteOthersMessage`; `DeleteAsync_RejectsNonAuthorNonAdmin`; `GetHistoryAsync_ExcludesDeletedMessages`. |
| `server/ChatApp.Tests/Integration/Messaging/EditDeleteIntegrationTests.cs` | Testcontainers + `ChatApiFactory`. Flows: (1) A sends DM, edits it → GET history returns updated body and `editedAt` set. (2) A deletes their DM → GET history excludes it. (3) B (room admin) deletes A's room message → GET history excludes it. (4) C (non-author, non-admin) attempts to delete A's message → 403. |

---

## Verification

1. **Build clean.** `dotnet build server/ChatApp.sln` zero warnings; `npm run build` zero
   errors in `client/`.
2. **No new migration.** `EditedAt`, `DeletedAt`, `ReplyToId` columns already exist
   (created in slice 5/8 migrations). Verify with `dotnet ef migrations list`.
3. **Unit tests.** `dotnet test --filter "FullyQualifiedName~EditDelete"` — all pass.
4. **Integration tests.** `EditDeleteIntegrationTests` pass via Testcontainers.
5. **Compose smoke — edit.** Two browsers (A, B) in a DM. A edits a message → B sees
   updated text + `(edited)` label without refreshing.
6. **Compose smoke — delete.** A deletes a message → disappears from A's and B's view.
7. **Compose smoke — admin delete.** Room admin deletes another member's message → disappears
   for all members; non-admin receives 403.
8. **Compose smoke — reply.** A clicks Reply on B's message → sends with quote → both see
   the parent snippet above A's message. Reply to then-deleted message shows "Original message
   deleted".

---

## Critical files at a glance

**New — server:**
- `server/ChatApp.Api/Contracts/Messages/EditMessageRequest.cs`
- `server/ChatApp.Domain/Services/Messaging/MessageDeletedPayload.cs`
- `server/ChatApp.Api/Contracts/Messages/MessageDeletedResponse.cs`
- `server/ChatApp.Api/Controllers/Messages/MessagesController.cs`

**Modified — server:**
- `server/ChatApp.Api/Contracts/Messages/MessageResponse.cs` *(add reply-parent fields)*
- `server/ChatApp.Domain/Services/Messaging/MessagePayload.cs` *(add reply-parent fields)*
- `server/ChatApp.Domain/Abstractions/IChatBroadcaster.cs` *(add two broadcast methods)*
- `server/ChatApp.Api/Hubs/ChatBroadcaster.cs` *(implement `MessageEdited` + `MessageDeleted`)*
- `server/ChatApp.Data/Services/Messaging/MessageService.cs` *(EditAsync, DeleteAsync, history filter, reply denorm)*
- `server/ChatApp.Domain/Services/Messaging/MessagingErrors.cs` *(four new error constants)*

**New — tests:**
- `server/ChatApp.Tests/Unit/Messaging/MessageService_EditDelete_Tests.cs`
- `server/ChatApp.Tests/Integration/Messaging/EditDeleteIntegrationTests.cs`

**Modified — client:**
- `client/src/app/core/messaging/messaging.models.ts`
- `client/src/app/core/messaging/dm.service.ts`
- `client/src/app/core/messaging/room-messaging.service.ts`
- Message bubble component(s) in `features/dms` and `features/rooms`
- Composer / input component(s) in `features/dms` and `features/rooms`
