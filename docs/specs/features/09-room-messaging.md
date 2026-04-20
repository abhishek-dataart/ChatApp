# Slice 8 — Room Messaging (Write Path + Broadcast)

Extends the Messaging context to room scope. Members of a room exchange messages in real-time via `POST /api/chats/room/{id}/messages` → `ChatHub` broadcast to `room:{id}`. Authorization delegates to `RoomPermissionService.IsMemberAsync`. Client extracts a reusable message list + composer so DM and Room views share one implementation; a parallel `RoomMessagingService` mirrors `DmService` for room scope.

## Context

`docs/implementation-plan.md` slice 8; depends on slice 5 (Messaging write path, `Message`/`UnreadMarker` entities, `MessageService`, `IChatBroadcaster`, `ChatHub.OnConnectedAsync` joining `pchat:{id}` groups) and slice 7 (`Room`/`RoomMember` entities, `RoomPermissionService.IsMemberAsync`, room detail page shell).

Authoritative requirements:

- **Arch doc §Message send sequence** — `POST /api/chats/{scope}/{scopeId}/messages`; authz via Rooms for room scope; body ≤ 3 KB; broadcast `MessageCreated` to `room:{id}`.
- **Arch doc §Realtime** — `ChatHub` groups `room:{roomId}`; hub has no write methods for messages; group membership resolved on `OnConnectedAsync`.
- **Arch doc §Bounded contexts — Messaging** — scope resolution delegates permission to Rooms (room scope) or Social (personal scope).
- **Product spec §7** — message body ≤ 3 KB UTF-8.
- **Slice 5 follow-ups for slice 8** (06-dm-messaging.md §Follow-ups) — extend `MessageService` with room-scope send, broadcast to `room:{roomId}`, new controller, `ChatHub.OnConnectedAsync` joins `room:{id}` groups from `RoomMember`.
- **Slice 7 follow-ups for slice 8** (08-rooms-basics.md §Follow-ups) — `RoomPermissionService.IsMemberAsync` is the authz hook; `ChatHub` group `room:{id}`; detail page laid out for messages pane under header.

Outcome: A and B are both members of room R. A opens `/app/rooms/{R}`, types "hello", hits Send. B (already on `/app/rooms/{R}` in another browser) sees the message appear within a second without refreshing. Reloading either page fetches the last 50 messages via `GET /api/chats/room/{R}/messages`. A non-member C calling `POST /api/chats/room/{R}/messages` gets 403 `not_member`.

## Decisions

Interview-confirmed choices folded in; *[decided]* marks genuinely open options that closed.

| Topic | Decision | Rationale |
|---|---|---|
| UI reuse | **Extract shared components.** Pull the message-list and composer out of `DmsComponent` into two standalone components under `client/src/app/shared/messaging/`: `MessageListComponent` (input: `messages: MessageResponse[]`, `currentUserId: string`) and `MessageComposerComponent` (output: `(send) => { body, replyToId? }`, input: `disabled`). Both `DmsComponent` and `RoomDetailComponent` consume them | *[decided]* — one-time refactor cost; future slices 10 (edit/delete controls), 11 (attachment thumbs), 15 (virtual scroll) edit one place. The alternative — duplicating the markup into the room view — doubles the surface for every later slice. |
| Client service shape | **New `RoomMessagingService`** parallel to `DmService`. Same shape: `messages: Signal<MessageResponse[]>`, `loadHistory(roomId)`, `send(roomId, body, replyToId?)`, `subscribe(roomId)`, `unsubscribe()` | *[decided]* — two tiny services are easier to reason about than one generalised `ScopedMessagingService` that would force refactoring `DmsComponent` in the same slice. Slice 15's pagination can unify them later if duplication hurts. |
| `MessageResponse` contract | **Nullable both IDs + `scope` discriminator.** Change to `{ Scope: "personal" \| "room", PersonalChatId: Guid?, RoomId: Guid?, AuthorId, AuthorUsername, AuthorDisplayName, AuthorAvatarUrl?, Body, ReplyToId?, CreatedAt, EditedAt? }`. Exactly one ID set per payload | *[decided]* — clean discrimination on the client (narrowed by `scope` string) without brittle "infer from which ID is present" logic. Small break to DM contract: `PersonalChatId` becomes nullable in the TS model, existing DM client code already reads by name so no functional impact. |
| `MessagePayload` (domain record) | Mirror the contract: add `Scope`, make `PersonalChatId` nullable, add `RoomId`. Single projection for both scopes — `MessageService` fills the appropriate ID, leaves the other null | One payload type, one client type. Alternatives (separate `RoomMessagePayload`) double the projection code. |
| `MessageService` extension | Add `SendToRoomAsync(Guid me, Guid roomId, string body, Guid? replyToId, CancellationToken ct)` and `GetRoomHistoryAsync(Guid me, Guid roomId, CancellationToken ct)`. Existing `SendAsync` / `GetHistoryAsync` keep their DM-only signatures (no overload confusion at call sites) | *[decided]* — distinct methods make the authz contract explicit (`IsMemberAsync` for rooms vs `PersonalChatParticipants` for DM). Keeps slice 5's tests unchanged. |
| Permission check | `RoomPermissionService.IsMemberAsync(roomId, me, ct)` on both send and history. No role distinction — any member reads/writes. Admin/owner checks come in slice 10 (edit/delete) | Matches arch doc: Rooms is the authoritative authz source for room-scoped messaging; IsMemberAsync is the right grain. |
| Broadcaster extension | `IChatBroadcaster.BroadcastMessageCreatedToRoomAsync(Guid roomId, MessagePayload payload, CancellationToken ct)` sending to `room:{roomId}` group. Existing `BroadcastMessageCreatedAsync(personalChatId, ...)` renamed to `BroadcastMessageCreatedToPersonalChatAsync` for symmetry | Two named methods read clearer than one group-name-taking method; compiler enforces the group naming convention centrally. Rename is internal — `IChatBroadcaster` is a server-only DI interface. |
| Hub group join on connect | **Eager join of `room:{id}` groups on `ChatHub.OnConnectedAsync`.** After the existing `pchat:{id}` loop, query `RoomMembers.Where(m => m.UserId == userId).Select(m => m.RoomId)` and `AddToGroupAsync` for each. New rooms joined after hub connect need a reconnect — same policy as slice 5 for new friends | *[decided]* — preserves "hub has no write methods" rule (per arch doc §Realtime "Hub exposes no write methods for messages"). Slice 13's `RoomMemberChanged` event is the natural place to trigger a client-side reconnect/resubscribe if that becomes a UX problem. |
| DM contract impact | `MessageResponse.cs` and `MessagePayload` refactor touches slice 5 code. DM history and broadcast payloads now carry `Scope = "personal"`, `RoomId = null`. Existing DM integration tests updated to assert the new fields | Unavoidable given the single-payload-shape decision; low risk because the field set is additive plus one nullability change. |
| Error codes | Add to `MessagingErrors`: `RoomNotFound = "room_not_found"` (404), `NotMember = "not_member"` (403). Reuse existing `BodyEmpty`, `BodyTooLong` | Mirrors `NotParticipant` from DM; distinct code so clients can differentiate "room gone" from "you're not in it" for telemetry, even though both map to the same user-facing outcome. |
| History ordering & limit | Same as DM: last 50 messages `ORDER BY created_at ASC, id ASC LIMIT 50`. No pagination param | Consistent with slice 5; slice 15 unifies keyset pagination across both scopes. |
| UnreadMarker on room send | **Not touched.** No increments on room send in this slice — slice 9 introduces `UnreadService` which handles both scopes uniformly | Plan row 8 and 9 draw the line here explicitly. |
| Reply-to | Accepted and stored; not rendered. Same treatment as DM in slice 5 | Rendering → slice 10. |
| Route shape | `POST /api/chats/room/{roomId:guid}/messages`, `GET /api/chats/room/{roomId:guid}/messages`. Mirrors `PersonalMessagesController` | Arch doc §Message send sequence names this path. |
| SignalR group naming | Continue using `$"room:{roomId}"` literal via a shared helper. Add `ChatGroups.Room(Guid id)` and `ChatGroups.PersonalChat(Guid id)` in `ChatApp.Api/Hubs/ChatGroups.cs` to stop the string literal drift | Small hygiene win — three places form the group name (hub, broadcaster, tests). Prevents a typo being a silent delivery failure. |
| Folder layout | `server/ChatApp.Api/Controllers/Messages/RoomMessagesController.cs`; `client/src/app/core/messaging/room-messaging.service.ts`; `client/src/app/shared/messaging/{message-list,message-composer}.component.ts` | Parallels existing slice-5 placement. |

### Deferred (explicit — handed to later slices)

- Unread marker increment on room send and `UnreadChanged` event — slice 9.
- Edit / delete / reply rendering in room view — slice 10.
- Image attachments in rooms — slice 11.
- `RoomMemberChanged` event driving client-side group resubscribe — slice 13.
- Keyset pagination and virtual scroll on room history — slice 15.
- Rate limiting on room message send (30 msg / 10 s / user) — slice 16.
- Admin/owner moderation on room messages — slice 13.

## Scope

### Server — files to create

| Path | Purpose |
|------|---------|
| `server/ChatApp.Api/Hubs/ChatGroups.cs` | Static helper: `public static string Room(Guid id) => $"room:{id}";` and `public static string PersonalChat(Guid id) => $"pchat:{id}";`. Replaces literal interpolation across hub, broadcaster, controllers. |
| `server/ChatApp.Api/Controllers/Messages/RoomMessagesController.cs` | `[ApiController, Route("api/chats/room/{roomId:guid}/messages"), Authorize]`. `POST` → `MessageService.SendToRoomAsync` → 201 `MessageResponse`. `GET` → `MessageService.GetRoomHistoryAsync` → 200 `List<MessageResponse>`. Same `FromError` helper shape as `PersonalMessagesController`: maps `RoomNotFound → 404`, `NotMember → 403`, `BodyTooLong / BodyEmpty → 400`. |

### Server — files to modify

| Path | Change |
|------|--------|
| `server/ChatApp.Data/Services/Messaging/MessageService.cs` | Add `SendToRoomAsync(Guid me, Guid roomId, string body, Guid? replyToId, CancellationToken ct)` returning `(bool, string?, string?, MessagePayload?)`. Body: validate `body` non-empty, UTF-8 byte count ≤ 3000; `SELECT` room (404 `room_not_found` if missing or soft-deleted); `RoomPermissionService.IsMemberAsync(roomId, me, ct)` → 403 `not_member` if false; insert `Message { Scope = Room, RoomId = roomId, PersonalChatId = null, AuthorId = me, Body, ReplyToId, CreatedAt = UtcNow }`; project to `MessagePayload` with author-user join; `broadcaster.BroadcastMessageCreatedToRoomAsync(roomId, payload, ct)`; return payload. Add `GetRoomHistoryAsync(Guid me, Guid roomId, CancellationToken ct)`: member-check then `SELECT ... WHERE RoomId = @r AND DeletedAt IS NULL ORDER BY CreatedAt ASC, Id ASC LIMIT 50` projected to `MessagePayload[]`. Inject `RoomPermissionService` via constructor. |
| `server/ChatApp.Data/Services/Messaging/MessagePayload.cs` (or wherever the record lives) | Add `MessageScope Scope` and `Guid? RoomId`; make `PersonalChatId` nullable. Adjust all constructors / factory mappers. |
| `server/ChatApp.Api/Contracts/Messages/MessageResponse.cs` | Mirror `MessagePayload`: `{ Guid Id, string Scope, Guid? PersonalChatId, Guid? RoomId, Guid AuthorId, string AuthorUsername, string AuthorDisplayName, string? AuthorAvatarUrl, string Body, Guid? ReplyToId, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt }`. `Scope` serialises as lowercase `"personal"` or `"room"` (use `[JsonConverter(typeof(JsonStringEnumConverter))]` with naming policy, or explicit string). Update `From(payload)` factory to copy the new fields. |
| `server/ChatApp.Domain/Services/Messaging/MessagingErrors.cs` | Add `public const string RoomNotFound = "room_not_found";` and `public const string NotMember = "not_member";`. |
| `server/ChatApp.Api/Hubs/IChatBroadcaster.cs` + `ChatBroadcaster.cs` | Rename existing `BroadcastMessageCreatedAsync(Guid personalChatId, ...)` → `BroadcastMessageCreatedToPersonalChatAsync(...)`. Add `BroadcastMessageCreatedToRoomAsync(Guid roomId, MessagePayload payload, CancellationToken ct)` sending to `ChatGroups.Room(roomId)` with event name `"MessageCreated"` and the projected `MessageResponse` payload. Update `MessageService.SendAsync` (DM path) to use the renamed method. |
| `server/ChatApp.Api/Hubs/ChatHub.cs` | In `OnConnectedAsync`, after the existing `pchat:{id}` group-join loop (~line 29), add: query `db.RoomMembers.Where(m => m.UserId == userId).Select(m => m.RoomId).ToListAsync(ct)`; loop `await Groups.AddToGroupAsync(Context.ConnectionId, ChatGroups.Room(rid), ct)`. Log `room group count` at Information alongside the existing pchat count. Replace any literal `$"pchat:{id}"` with `ChatGroups.PersonalChat(id)` for consistency. |
| `server/ChatApp.Api/Program.cs` | No new DI registrations needed (`MessageService` and `RoomPermissionService` are already `AddScoped`). Verify `MessageService` constructor still resolves after injecting `RoomPermissionService`. |

### Client — files to create

| Path | Purpose |
|------|---------|
| `client/src/app/shared/messaging/message-list.component.ts` + `.html` + `.scss` | Standalone. `@Input() messages: MessageResponse[] = []`; `@Input({ required: true }) currentUserId!: string`. Template: flex-column scrollable list; each row shows author display name (or "You" when `message.authorId === currentUserId`), avatar, body (preserving newlines via `white-space: pre-wrap`), timestamp via Angular `DatePipe`. Scroll-to-bottom on new message using `AfterViewChecked` + a private `lastCount` guard. Skeleton handled by caller (component receives a loaded array). |
| `client/src/app/shared/messaging/message-composer.component.ts` + `.html` + `.scss` | Standalone. `@Input() disabled = false`; `@Output() send = new EventEmitter<{ body: string; replyToId?: string \| null }>()`. Template: textarea (auto-grow to 4 lines, enter-sends, shift+enter for newline) + send button. Local `body` signal, client-side guard `body.trim().length > 0` and UTF-8 byte length ≤ 3000 (preview counter shown on overflow). Clears input on emit. |
| `client/src/app/core/messaging/room-messaging.service.ts` | `@Injectable({ providedIn: 'root' })`. Parallels `DmService`: `private _messages = signal<MessageResponse[]>([])`, `readonly messages = this._messages.asReadonly()`, `loadHistory(roomId)` (GET), `send(roomId, body, replyToId?)` (POST, server response appended via hub handler — no optimistic append), `subscribe(roomId)` registers a `MessageCreated` handler on `SignalrService` chat hub that filters `payload.scope === 'room' && payload.roomId === roomId` before appending, `unsubscribe()` clears handler and state. |
| `client/src/app/core/messaging/messaging.models.ts` (new if absent, else modify) | Export `MessageResponse` TS interface with `scope: 'personal' \| 'room'`, `personalChatId: string \| null`, `roomId: string \| null`, and the author/body/reply fields. Shared by `DmService` and `RoomMessagingService`. |

### Client — files to modify

| Path | Change |
|------|--------|
| `client/src/app/features/dms/dms.component.ts` + `.html` + `.scss` | Replace inline message-list and composer markup with `<app-message-list [messages]="messages()" [currentUserId]="currentUserId()" />` and `<app-message-composer [disabled]="sending()" (send)="onSend($event)" />`. Remove duplicated scroll / send logic now housed in the shared components. `DmService` untouched except importing the moved `MessageResponse` type from `messaging.models.ts`. |
| `client/src/app/core/messaging/dm.service.ts` | Update hub handler filter to `payload.scope === 'personal' && payload.personalChatId === chatId` (now that `scope` exists). Import `MessageResponse` from `messaging.models.ts` instead of declaring it locally. |
| `client/src/app/features/rooms/room-detail/room-detail.component.ts` + `.html` + `.scss` | Inject `RoomMessagingService`. In `ngOnInit` (after successful `RoomsService.get(id)`): `await roomMessaging.loadHistory(id); roomMessaging.subscribe(id);`. In `ngOnDestroy`: `roomMessaging.unsubscribe()`. Template: add a `<section class="room-messages">` below the existing room header and above/beside the member list (see layout note), containing `<app-message-list [messages]="roomMessaging.messages()" [currentUserId]="currentUserId()" />` and `<app-message-composer [disabled]="sending()" (send)="onSend($event)" />`. Layout: two-column flex on desktop (messages left 2fr, members right 1fr), stacked on narrow viewports. The Leave button stays in the header. Error from `onSend` surfaces via the existing `error` signal. |
| `client/src/app/core/signalr/signalr.service.ts` | Verify it exposes a way to register multiple named handlers on the chat hub without overwriting (the DM handler must coexist with the room handler). If it currently uses `hub.on("MessageCreated", handler)` that's additive by design — only concern is that `unsubscribe()` calls `hub.off("MessageCreated", handler)` with the specific handler reference. Adjust `DmService` and `RoomMessagingService` to store their handler refs accordingly. |

### Tests — files to create

| Path | Purpose |
|------|---------|
| `server/ChatApp.Tests/Unit/Messaging/MessageService_SendToRoomAsync_Tests.cs` | `BodyEmpty → body_empty`; `BodyTooLong → body_too_long` (3001-byte UTF-8 string); `RoomDoesNotExist → room_not_found`; `NotMember → not_member`; `HappyPath_PersistsWithScopeRoomAndBroadcasts` — uses a test double for `IChatBroadcaster` asserting `BroadcastMessageCreatedToRoomAsync(roomId, …)` was called once with the persisted payload. In-memory DB shape follows the convention already set by slice-5 unit tests. |
| `server/ChatApp.Tests/Unit/Messaging/MessageService_GetRoomHistoryAsync_Tests.cs` | `NotMember → 403`; `ReturnsLast50Ascending`. |
| `server/ChatApp.Tests/Integration/Messaging/RoomMessagingIntegrationTests.cs` | Uses `ChatApiFactory` + `PostgresFixture`. Flows: register A & B; A creates public room; B joins; A posts `POST /api/chats/room/{id}/messages { body: "hi" }` → 201 response has `scope = "room"`, `roomId` set, `personalChatId = null`. B `GET /api/chats/room/{id}/messages` → list contains it. C (non-member) `POST` → 403 `not_member`. C `GET` → 403 `not_member`. Post body > 3000 bytes → 400 `body_too_long`. Post to non-existent roomId → 404 `room_not_found`. |
| `server/ChatApp.Tests/Integration/Messaging/ChatHubRoomGroupsTests.cs` | Spin up `WebApplicationFactory`, connect a SignalR client as user A (member of rooms R1 and R2), verify that a `MessageCreated` fired via `IHubContext<ChatHub>.Clients.Group(ChatGroups.Room(R1))` reaches the test client. This is the test that proves `OnConnectedAsync` joined the right groups; without it the broadcast wiring is untested. |

### Tests — files to modify

| Path | Change |
|------|--------|
| Existing slice-5 DM unit + integration tests | Update assertions for new `MessageResponse` fields: `scope == "personal"`, `roomId == null`. No behaviour change. |

### Out of scope (explicit — handed to later slices)

- `UnreadMarker` increment on room send, `UnreadChanged` SignalR event — slice 9.
- Room message edit / delete / reply rendering — slice 10.
- Image attachments in room messages — slice 11.
- Keyset pagination and Angular CDK virtual scroll on room history — slice 15.
- Rate-limit token bucket per user on message POST — slice 16.
- `RoomMemberChanged` event → client resubscribes hub groups live — slice 13.
- Presence dots on room member list — follow-up to slice 6.

## Key flows (reference)

### Send a room message (happy path)

1. A is on `/app/rooms/{R}`. Types "hi", presses Enter.
2. `MessageComposerComponent` emits `send` → `RoomDetailComponent.onSend({ body: "hi" })`.
3. `RoomMessagingService.send(R, "hi")` → `POST /api/chats/room/{R}/messages { body: "hi" }` with credentials.
4. **Server:** `RoomMessagesController.Post` → `MessageService.SendToRoomAsync(me=A, roomId=R, body="hi", replyToId=null, ct)`.
5. Validate body non-empty + ≤ 3000 UTF-8 bytes.
6. `SELECT Room WHERE Id = R` — 404 `room_not_found` if missing/soft-deleted.
7. `RoomPermissionService.IsMemberAsync(R, A, ct)` — 403 `not_member` if false.
8. Insert `Message { Scope = Room, RoomId = R, PersonalChatId = null, AuthorId = A, Body, CreatedAt = UtcNow }`; `SaveChangesAsync`.
9. Project to `MessagePayload` (joining `Users` for author username / display name / avatar).
10. `broadcaster.BroadcastMessageCreatedToRoomAsync(R, payload, ct)` → `Clients.Group(ChatGroups.Room(R)).SendAsync("MessageCreated", MessageResponse.From(payload), ct)`.
11. Return `201 Created` with `MessageResponse`.
12. **Client (A):** POST resolves. No explicit append — the hub handler receives the same `MessageCreated` event (A is in the `room:{R}` group) and appends via the filter `scope === 'room' && roomId === R`.
13. **Client (B):** same handler fires; message appears live.

### Load room history

1. On `RoomDetailComponent.ngOnInit`, after `RoomsService.get(id)` succeeds, call `roomMessaging.loadHistory(id)` and `roomMessaging.subscribe(id)`.
2. `GET /api/chats/room/{id}/messages` → server member-checks then returns last 50 ordered ASC.
3. Client sets `_messages` signal; `MessageListComponent` renders.

### Hub reconnect

1. API restarts. Client `SignalrService` reconnects with backoff.
2. `ChatHub.OnConnectedAsync` re-queries `RoomMembers` for the user and re-joins every `room:{id}` group (plus `pchat:{id}` groups unchanged from slice 5).
3. The open room view resumes receiving `MessageCreated` without any client-side re-subscription.

### New room joined mid-session

1. A joins room R2 while the hub is already connected.
2. A does not receive `room:{R2}` events until reconnect — documented limitation (slice 5 has the same behaviour for new DMs). Slice 13 adds `RoomMemberChanged` + client-side resubscribe; the slice-7 `Join` flow currently redirects A to `/app/rooms/{R2}`, so it's only a problem if A keeps another tab open on a different room — acceptable for the envelope.

## Implementation notes

### MessageService permission check ordering

```csharp
// Validate body first — cheap, no DB hit.
var bytes = Encoding.UTF8.GetByteCount(body);
if (bytes == 0) return (false, MessagingErrors.BodyEmpty, ...);
if (bytes > 3000) return (false, MessagingErrors.BodyTooLong, ...);

var roomExists = await db.Rooms.AnyAsync(r => r.Id == roomId && r.DeletedAt == null, ct);
if (!roomExists) return (false, MessagingErrors.RoomNotFound, ...);

var isMember = await roomPermissions.IsMemberAsync(roomId, me, ct);
if (!isMember) return (false, MessagingErrors.NotMember, ...);

// persist + broadcast
```

### Broadcaster rename

The rename of `BroadcastMessageCreatedAsync` → `BroadcastMessageCreatedToPersonalChatAsync` is a find-and-replace limited to `MessageService.SendAsync` (the only current caller). `IChatBroadcaster` lives server-side only so there's no client break.

### Client hub handler registration

`SignalrService.chatHub.on("MessageCreated", handler)` must accept multiple handlers — SignalR's JS client calls them all in registration order. `DmService` and `RoomMessagingService` each register their own filter-guarded handler. Each stores a handler reference for `unsubscribe()` → `chatHub.off("MessageCreated", handler)`.

## Verification

1. **Build clean.** `dotnet build server/ChatApp.sln` zero warnings; `cd client && npm run build` zero errors.
2. **Migration.** No new migration — `Message` and `RoomMember` schemas unchanged.
3. **Unit tests.** `dotnet test server/ChatApp.Tests --filter "FullyQualifiedName~Messaging"` — new tests pass, slice-5 DM tests pass with updated assertions.
4. **Integration tests.** Run `server/ChatApp.Tests` Testcontainers suite. `RoomMessagingIntegrationTests` covers send/history/authz/body; `ChatHubRoomGroupsTests` covers group delivery.
5. **Compose smoke.** `docker compose -f infra/docker-compose.yml up -d --build`. Two browsers:
   - User A and B both registered, become friends (slice 3), A creates public room R (slice 7), B joins (slice 7).
   - Both navigate to `/app/rooms/{R}`. A types "hello" → B sees it within ~1 s without refresh. B replies → A sees it. Refresh either browser — history loads.
   - C (a third user, not a member) opens DevTools and `curl -b cookie.txt -X POST .../api/chats/room/{R}/messages -H "Content-Type: application/json" -d '{"body":"nope"}'` → 403 `not_member`.
   - Paste a 4 KB string in the composer → send disabled and counter red (or POST → 400 `body_too_long` if guard bypassed).
6. **DM regression.** Go to `/app/dms/{chatId}`, exchange DMs A↔B — still works, history loads, payloads now include `"scope":"personal"` per DevTools network tab.
7. **Hub group proof.** Tail api logs. On `ChatHub connected`, Information log line shows `... joined N pchat groups, M room groups`.

## Critical files at a glance

- `server/ChatApp.Api/Controllers/Messages/RoomMessagesController.cs` *(new)*
- `server/ChatApp.Api/Hubs/ChatGroups.cs` *(new)*
- `server/ChatApp.Api/Hubs/ChatHub.cs` *(OnConnectedAsync room-group loop)*
- `server/ChatApp.Api/Hubs/ChatBroadcaster.cs` + `IChatBroadcaster.cs` *(rename + add room method)*
- `server/ChatApp.Data/Services/Messaging/MessageService.cs` *(add SendToRoomAsync, GetRoomHistoryAsync)*
- `server/ChatApp.Data/Services/Messaging/MessagePayload.cs` *(add Scope + RoomId, nullable PersonalChatId)*
- `server/ChatApp.Api/Contracts/Messages/MessageResponse.cs` *(add Scope + RoomId, nullable PersonalChatId)*
- `server/ChatApp.Domain/Services/Messaging/MessagingErrors.cs` *(RoomNotFound, NotMember)*
- `client/src/app/shared/messaging/message-list.component.{ts,html,scss}` *(new, extracted)*
- `client/src/app/shared/messaging/message-composer.component.{ts,html,scss}` *(new, extracted)*
- `client/src/app/core/messaging/room-messaging.service.ts` *(new)*
- `client/src/app/core/messaging/messaging.models.ts` *(shared MessageResponse type)*
- `client/src/app/core/messaging/dm.service.ts` *(use shared type, scope-aware filter)*
- `client/src/app/features/dms/dms.component.{ts,html}` *(consume shared components)*
- `client/src/app/features/rooms/room-detail/room-detail.component.{ts,html,scss}` *(messages pane + composer)*
- `server/ChatApp.Tests/Unit/Messaging/MessageService_SendToRoomAsync_Tests.cs` *(new)*
- `server/ChatApp.Tests/Unit/Messaging/MessageService_GetRoomHistoryAsync_Tests.cs` *(new)*
- `server/ChatApp.Tests/Integration/Messaging/RoomMessagingIntegrationTests.cs` *(new)*
- `server/ChatApp.Tests/Integration/Messaging/ChatHubRoomGroupsTests.cs` *(new)*

## Follow-ups for slice 9 (Unread markers + counts)

- `UnreadService` increments markers for all room members except the sender on every `SendToRoomAsync` success — hook in `MessageService` right after the broadcast.
- `UnreadChanged` event to each recipient's `user:{userId}` group.
- Room list entries in `/app/rooms` get unread badges sourced from `UnreadMarker` where `scope = 'room'`.
