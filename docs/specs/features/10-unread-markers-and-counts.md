# Slice 9 — Unread Markers + Counts

Activates the `UnreadMarker` infrastructure that slices 5 and 8 explicitly deferred. Every message send increments markers for non-sender recipients and emits per-recipient `UnreadChanged` SignalR events on their `user:{id}` group. The client renders badges on the DM contact list and room list; opening a chat calls a REST mark-as-read endpoint that zeroes the count and re-broadcasts to all of the user's tabs.

## Context

`docs/implementation-plan.md` slice 9; depends on slice 5 (`UnreadMarker` entity + DB table in `AddMessaging` migration, `MessageService.SendAsync`, `ChatHub` `user:{id}` group join on connect) and slice 8 (`MessageService.SendToRoomAsync`, `RoomMessagesController`).

Authoritative requirements:

- **Arch doc §Message send sequence step 5** — `UnreadService` increments `UnreadMarker` for all other recipients and emits `UnreadChanged` to each recipient's `user:{id}` group.
- **Arch doc §Realtime** — `ChatHub` sends `UnreadChanged` event; `user:{userId}` group joined on `OnConnectedAsync`.
- **Product spec §7.4** — `UnreadMarker` per `(user, scope, scope_id)` — cleared when user opens the chat.
- **Product spec §9** — In-UI unread badges near each room and contact entry; cleared on open.
- **Slice 5 follow-ups** — `UnreadService` increments markers on personal-chat send.
- **Slice 8 follow-ups** — `UnreadService` increments markers for room members on room send; room list entries get unread badges.

Outcome: A sends a DM to B while B has the Contacts page open. A badge appears on B's Message link in real-time. B opens the DM; the badge disappears immediately on B's screen and also disappears on B's second browser tab showing the Contacts page. The same flow works for room messages.

## Decisions

| Topic | Decision | Rationale |
|---|---|---|
| `UnreadService` location | `server/ChatApp.Data/Services/Messaging/UnreadService.cs` | Needs DB access + `IChatBroadcaster`; same placement pattern as `MessageService` and `RoomPermissionService` |
| Upsert strategy | Raw SQL via `db.Database.ExecuteSqlAsync` with `INSERT … ON CONFLICT (user_id, scope, scope_id) DO UPDATE SET unread_count = unread_markers.unread_count + 1` | Prevents the read-modify-write race condition when messages arrive concurrently; avoids a SELECT before the increment |
| Room fan-out | Single batched raw-SQL upsert for all room members minus sender; one SELECT to fetch updated counts; one `BroadcastUnreadChangedAsync` call per recipient | One DB round-trip per room send regardless of member count; acceptable for the 300-user / 1000-member envelope |
| `UnreadChanged` payload | `{ scope, scopeId, unreadCount }` mirroring `UnreadMarker` key | Client uses `scope:scopeId` as a flat map key; no nesting needed |
| Mark-as-read endpoints | `POST /api/chats/personal/{chatId}/read` and `POST /api/chats/room/{roomId}/read` added to existing controllers | Consistent with the existing controller layout; auth/permission already wired there |
| Bulk unread load | `GET /api/chats/unread` on a new `UnreadController` | Client needs all non-zero counts at startup in a single round-trip |
| Client state | `signal<Record<string, number>>({})` keyed `"personal:{id}"` / `"room:{id}"` | Flat, fast keyed reads; helpers in components convert scope+id to key |
| Optimistic mark-as-read | Zero the local signal before POST resolves | Badge clears immediately on the active tab; server broadcast syncs other tabs |
| Reconnect sync | `chatConn.onreconnected(() => this.loadAll())` in `UnreadService` constructor | Badges accurate after hub reconnect without a page reload |
| Sound ping | **Deferred.** Not in slice 9 client scope per implementation plan | `User.SoundOnMessage` entity field already exists; add in a later slice |
| New migration | **None.** `unread_markers` table created in slice 5 `AddMessaging` migration | Verify table exists; no schema changes required |

### Deferred (explicit — handed to later slices)

- Sound ping on `MessageCreated` when tab is not focused — later slice.
- Unread count adjustments on message delete — not needed (counts are per-chat-open, not per-message).
- Pagination and virtual scroll — slice 15.
- Rate limiting — slice 16.

## Scope

### Server — files to create

| Path | Purpose |
|------|---------|
| `server/ChatApp.Data/Services/Messaging/UnreadService.cs` | Constructor: `ChatDbContext db`, `IChatBroadcaster broadcaster`. Three public async methods: `IncrementAsync`, `MarkReadAsync`, `GetAllAsync`. See logic below. |
| `server/ChatApp.Domain/Services/Messaging/UnreadChangedPayload.cs` | `public record UnreadChangedPayload(string Scope, Guid ScopeId, int UnreadCount);` — passed through `IChatBroadcaster`. |
| `server/ChatApp.Api/Contracts/Messages/UnreadResponse.cs` | `public sealed record UnreadResponse(string Scope, Guid ScopeId, int UnreadCount);` — HTTP response DTO and SignalR event payload. |
| `server/ChatApp.Api/Controllers/Messages/UnreadController.cs` | `[ApiController, Route("api/chats/unread"), Authorize]`. Single `GET` action: resolves `ICurrentUser`, calls `UnreadService.GetAllAsync(me, ct)`, returns `200 List<UnreadResponse>`. |

### Server — files to modify

| Path | Change |
|------|--------|
| `server/ChatApp.Domain/Abstractions/IChatBroadcaster.cs` | Add `Task BroadcastUnreadChangedAsync(Guid userId, UnreadChangedPayload payload, CancellationToken ct = default);` |
| `server/ChatApp.Api/Hubs/ChatBroadcaster.cs` | Implement `BroadcastUnreadChangedAsync`: `hub.Clients.Group($"user:{userId}").SendAsync("UnreadChanged", new UnreadResponse(payload.Scope, payload.ScopeId, payload.UnreadCount), ct)`. The `user:{userId}` group is already joined on connect. |
| `server/ChatApp.Data/Services/Messaging/MessageService.cs` | Inject `UnreadService` via constructor. In `SendAsync`, after `BroadcastMessageCreatedToPersonalChatAsync`: `await unreadService.IncrementAsync(me, MessageScope.Personal, personalChatId, ct)`. In `SendToRoomAsync`, after `BroadcastMessageCreatedToRoomAsync`: `await unreadService.IncrementAsync(me, MessageScope.Room, roomId, ct)`. |
| `server/ChatApp.Api/Controllers/Messages/PersonalMessagesController.cs` | Inject `UnreadService`. Add `[HttpPost("{chatId:guid}/read")]` action: participant guard (same check as GET history); `await unreadService.MarkReadAsync(me, MessageScope.Personal, chatId, ct)`; return `204 No Content`. |
| `server/ChatApp.Api/Controllers/Messages/RoomMessagesController.cs` | Inject `UnreadService`. Add `[HttpPost("{roomId:guid}/read")]` action: `RoomPermissionService.IsMemberAsync(roomId, me, ct)` → 403 if false; `await unreadService.MarkReadAsync(me, MessageScope.Room, roomId, ct)`; return `204 No Content`. |
| `server/ChatApp.Api/Program.cs` | Add `builder.Services.AddScoped<UnreadService>();` after `AddScoped<MessageService>()`. |

#### `UnreadService` method logic

```csharp
// Recipient resolution + batch upsert + broadcast
public async Task IncrementAsync(Guid senderId, MessageScope scope, Guid scopeId, CancellationToken ct)
{
    List<Guid> recipientIds;
    if (scope == MessageScope.Personal)
    {
        var chat = await db.PersonalChats.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == scopeId, ct);
        if (chat is null) return;
        recipientIds = [chat.UserAId == senderId ? chat.UserBId : chat.UserAId];
    }
    else
    {
        recipientIds = await db.RoomMembers.AsNoTracking()
            .Where(m => m.RoomId == scopeId && m.UserId != senderId)
            .Select(m => m.UserId)
            .ToListAsync(ct);
    }

    if (recipientIds.Count == 0) return;

    // Batch upsert — one round-trip
    // Use FormattableString / parameters to avoid SQL injection
    foreach (var uid in recipientIds)
    {
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO unread_markers (user_id, scope, scope_id, unread_count, last_read_at)
            VALUES ({uid}, {(int)scope}, {scopeId}, 1, null)
            ON CONFLICT (user_id, scope, scope_id)
            DO UPDATE SET unread_count = unread_markers.unread_count + 1
            """, ct);
    }

    // Fetch updated counts then broadcast
    var updated = await db.UnreadMarkers.AsNoTracking()
        .Where(m => recipientIds.Contains(m.UserId) && m.Scope == scope && m.ScopeId == scopeId)
        .Select(m => new { m.UserId, m.UnreadCount })
        .ToListAsync(ct);

    foreach (var row in updated)
    {
        var payload = new UnreadChangedPayload(
            scope == MessageScope.Personal ? "personal" : "room",
            scopeId,
            row.UnreadCount);
        await broadcaster.BroadcastUnreadChangedAsync(row.UserId, payload, ct);
    }
}

// Zero count + broadcast to sync all tabs
public async Task MarkReadAsync(Guid me, MessageScope scope, Guid scopeId, CancellationToken ct)
{
    var now = DateTimeOffset.UtcNow;
    await db.Database.ExecuteSqlAsync(
        $"""
        INSERT INTO unread_markers (user_id, scope, scope_id, unread_count, last_read_at)
        VALUES ({me}, {(int)scope}, {scopeId}, 0, {now})
        ON CONFLICT (user_id, scope, scope_id)
        DO UPDATE SET unread_count = 0, last_read_at = {now}
        """, ct);

    var payload = new UnreadChangedPayload(
        scope == MessageScope.Personal ? "personal" : "room",
        scopeId,
        0);
    await broadcaster.BroadcastUnreadChangedAsync(me, payload, ct);
}

// Used by GET /api/chats/unread
public async Task<List<UnreadResponse>> GetAllAsync(Guid me, CancellationToken ct)
{
    return await db.UnreadMarkers.AsNoTracking()
        .Where(m => m.UserId == me && m.UnreadCount > 0)
        .Select(m => new UnreadResponse(
            m.Scope == MessageScope.Personal ? "personal" : "room",
            m.ScopeId,
            m.UnreadCount))
        .ToListAsync(ct);
}
```

### Client — files to create

| Path | Purpose |
|------|---------|
| `client/src/app/core/messaging/unread.service.ts` | `@Injectable({ providedIn: 'root' })`. See shape below. |

```typescript
@Injectable({ providedIn: 'root' })
export class UnreadService {
  private readonly http = inject(HttpClient);
  private readonly signalr = inject(SignalrService);

  private readonly _counts = signal<Record<string, number>>({});
  readonly counts = this._counts.asReadonly();

  private readonly handler = (payload: UnreadChangedPayload) => {
    const key = `${payload.scope}:${payload.scopeId}`;
    this._counts.update(c => ({ ...c, [key]: payload.unreadCount }));
  };

  constructor() {
    this.signalr.chat.onreconnected(() => this.loadAll());
  }

  async loadAll(): Promise<void> {
    const data = await firstValueFrom(
      this.http.get<UnreadResponse[]>(`${environment.apiBase}/chats/unread`),
    );
    const map: Record<string, number> = {};
    for (const item of data) {
      map[`${item.scope}:${item.scopeId}`] = item.unreadCount;
    }
    this._counts.set(map);
  }

  subscribe(): void {
    this.signalr.chat.on('UnreadChanged', this.handler);
  }

  unsubscribe(): void {
    this.signalr.chat.off('UnreadChanged', this.handler);
  }

  countFor(scope: 'personal' | 'room', scopeId: string): number {
    return this._counts()[`${scope}:${scopeId}`] ?? 0;
  }

  async markRead(scope: 'personal' | 'room', scopeId: string): Promise<void> {
    // Optimistic clear — badge disappears immediately on this tab
    this._counts.update(c => ({ ...c, [`${scope}:${scopeId}`]: 0 }));
    await firstValueFrom(
      this.http.post(`${environment.apiBase}/chats/${scope}/${scopeId}/read`, null),
    );
    // Server will broadcast UnreadChanged { count: 0 } to sync other tabs
  }
}
```

### Client — files to modify

| Path | Change |
|------|--------|
| `client/src/app/core/messaging/messaging.models.ts` | Add `export interface UnreadChangedPayload { scope: 'personal' \| 'room'; scopeId: string; unreadCount: number; }` and `export interface UnreadResponse { scope: 'personal' \| 'room'; scopeId: string; unreadCount: number; }` |
| `client/src/app/features/app-shell/app-shell.component.ts` | Inject `UnreadService`. In `ngOnInit` after `this.signalr.start()`: `this.unread.subscribe(); await this.unread.loadAll();`. In `ngOnDestroy`: `this.unread.unsubscribe();` |
| `client/src/app/features/rooms/rooms-list/rooms-list.component.ts` | Inject `UnreadService`. Add `unreadFor(id: string): number { return this.unread.countFor('room', id); }` |
| `client/src/app/features/rooms/rooms-list/rooms-list.component.html` | Inside the My Rooms `room-card` link after `room-card__name`, add `@if (unreadFor(room.id) > 0) { <span class="unread-badge">{{ unreadFor(room.id) }}</span> }` |
| `client/src/app/features/contacts/contacts.component.ts` | Inject `UnreadService`. Add `dmUnreadFor(chatId: string): number { return this.unread.countFor('personal', chatId); }` |
| `client/src/app/features/contacts/contacts.component.html` | On the friend-row `btn-message` anchor, add `@if (dmUnreadFor(item.personalChatId) > 0) { <span class="unread-badge">{{ dmUnreadFor(item.personalChatId) }}</span> }` adjacent to or inside the link. |
| `client/src/app/features/dms/dms.component.ts` | Inject `UnreadService`. In `ngOnInit` after `dmService.loadHistory(chatId)`: `this.unread.markRead('personal', chatId);` |
| `client/src/app/features/rooms/room-detail/room-detail.component.ts` | Inject `UnreadService`. In `ngOnInit` after `roomMessaging.loadHistory(roomId)`: `this.unread.markRead('room', roomId);` |

### Tests — files to create

| Path | Purpose |
|------|---------|
| `server/ChatApp.Tests/Unit/Messaging/UnreadService_Tests.cs` | Unit tests using in-memory EF provider + mock `IChatBroadcaster`: `IncrementAsync_PersonalChat_IncrementsRecipientNotSender`; `IncrementAsync_Room_IncrementsAllMembersExceptSender`; `IncrementAsync_Accumulates_MultipleSends`; `MarkReadAsync_SetsCountToZeroAndBroadcasts`; `GetAllAsync_ReturnsOnlyNonZeroCounts`. Mock verifies `BroadcastUnreadChangedAsync` is called with correct userId and payload. |
| `server/ChatApp.Tests/Integration/Messaging/UnreadIntegrationTests.cs` | Testcontainers + `ChatApiFactory`. Flows: (1) A sends DM to B → `GET /api/chats/unread` as B returns `[{ scope:"personal", unreadCount:1 }]`; B `POST .../personal/{chatId}/read` → GET returns `[]`. (2) A sends room message → each other member has `unreadCount:1`; one member marks read → only theirs drops to 0. (3) A sends to their own DM does not create A's own unread entry. |

---

## Key flows

### Send DM — unread increment

1. A `POST /api/chats/personal/{chatId}/messages`.
2. `MessageService.SendAsync` persists, broadcasts `MessageCreated` to `pchat:{chatId}`.
3. `UnreadService.IncrementAsync(A, Personal, chatId)`: determines recipient is B; upserts B's marker (+1); fetches updated count; `BroadcastUnreadChangedAsync(B, { scope:"personal", scopeId:chatId, unreadCount:N })` → `user:{B}` group.
4. B's `UnreadService` (client) `handler` fires: `_counts["personal:{chatId}"] = N`.
5. Badge appears on B's Message link in the Contacts section without any page refresh.

### Open DM — mark as read + cross-tab sync

1. B navigates to `/app/dms/{chatId}`.
2. `DmsComponent.ngOnInit` calls `dmService.loadHistory(chatId)` then `unread.markRead('personal', chatId)`.
3. Client optimistically zeroes `_counts["personal:{chatId}"]` → badge disappears immediately.
4. Client `POST /api/chats/personal/{chatId}/read` → server upserts `unread_count=0, last_read_at=now`; broadcasts `UnreadChanged { count:0 }` to `user:{B}`.
5. B's other tab receives the event and updates its own `_counts` → badge clears there too.

### App-shell startup — load all unread

1. `AppShellComponent.ngOnInit`: `signalr.start()` → `unread.subscribe()` → `unread.loadAll()`.
2. `GET /api/chats/unread` returns all non-zero markers for the current user.
3. `_counts` signal populated; all badges render before the user navigates anywhere.

### Hub reconnect — counts stay accurate

1. API restarts; client reconnects via exponential backoff.
2. `chatConn.onreconnected` fires → `unreadService.loadAll()` re-fetches the current state.
3. Badges reflect actual DB state, not a stale in-memory snapshot.

---

## Implementation notes

### `ExecuteSqlAsync` with interpolated string

EF Core 7+ `ExecuteSqlAsync(FormattableString)` parameterises the interpolated values automatically, preventing SQL injection:

```csharp
await db.Database.ExecuteSqlAsync(
    $"""
    INSERT INTO unread_markers (user_id, scope, scope_id, unread_count, last_read_at)
    VALUES ({uid}, {(int)scope}, {scopeId}, 1, null)
    ON CONFLICT (user_id, scope, scope_id)
    DO UPDATE SET unread_count = unread_markers.unread_count + 1
    """, ct);
```

Each call is one round-trip. For rooms with many members this is N individual upserts — acceptable for MVP; a future optimisation could batch values into a single multi-row INSERT.

### Enum serialisation in `UnreadResponse`

`Scope` is serialised as the lowercase string `"personal"` or `"room"` (not the int). The `ChatBroadcaster` constructs `new UnreadResponse(payload.Scope, ...)` where `payload.Scope` is already the correct string from `UnreadService`. Client models use the same string union type.

### `markRead` POST URL shape

The URL `POST /api/chats/{scope}/{scopeId}/read` resolves to either `personal/{chatId}/read` or `room/{roomId}/read`, matching the existing controller route patterns `api/chats/personal/{chatId:guid}/...` and `api/chats/room/{roomId:guid}/...`.

---

## Verification

1. **Build clean.** `dotnet build server/ChatApp.sln` zero warnings; `cd client && npm run build` zero errors.
2. **Migration check.** `docker compose up -d --build`. `unread_markers` table exists in Postgres (from slice 5). No new migration should appear.
3. **Unit tests.** `dotnet test --filter "FullyQualifiedName~UnreadService"` — all pass.
4. **Integration tests.** `UnreadIntegrationTests` pass via Testcontainers.
5. **Compose smoke — DM flow.** Two browsers (A, B). B is on Contacts. A sends DM → badge appears on B's Message link. B opens DM → badge clears. Refresh → still no badge.
6. **Compose smoke — cross-tab.** B opens a second browser tab on Contacts. A sends DM → badge appears on **both** B tabs simultaneously. B opens DM in tab 1 → badge clears on both tabs.
7. **Compose smoke — room flow.** A sends room message in room R. All other members see a badge on R's card in My Rooms. One member opens room R → their badge clears; others' badges unchanged.
8. **Unread API smoke.** After all clears, `GET /api/chats/unread` (B's session cookie) returns `[]`.

---

## Critical files at a glance

**New — server:**
- `server/ChatApp.Data/Services/Messaging/UnreadService.cs`
- `server/ChatApp.Domain/Services/Messaging/UnreadChangedPayload.cs`
- `server/ChatApp.Api/Contracts/Messages/UnreadResponse.cs`
- `server/ChatApp.Api/Controllers/Messages/UnreadController.cs`

**Modified — server:**
- `server/ChatApp.Domain/Abstractions/IChatBroadcaster.cs` *(add `BroadcastUnreadChangedAsync`)*
- `server/ChatApp.Api/Hubs/ChatBroadcaster.cs` *(implement `BroadcastUnreadChangedAsync`)*
- `server/ChatApp.Data/Services/Messaging/MessageService.cs` *(inject `UnreadService`; call `IncrementAsync` after each send)*
- `server/ChatApp.Api/Controllers/Messages/PersonalMessagesController.cs` *(add `/read` action)*
- `server/ChatApp.Api/Controllers/Messages/RoomMessagesController.cs` *(add `/read` action)*
- `server/ChatApp.Api/Program.cs` *(register `UnreadService`)*

**New — client:**
- `client/src/app/core/messaging/unread.service.ts`

**Modified — client:**
- `client/src/app/core/messaging/messaging.models.ts` *(add `UnreadChangedPayload` + `UnreadResponse` types)*
- `client/src/app/features/app-shell/app-shell.component.ts` *(subscribe + loadAll on init)*
- `client/src/app/features/rooms/rooms-list/rooms-list.component.{ts,html}` *(unread badges on My Rooms cards)*
- `client/src/app/features/contacts/contacts.component.{ts,html}` *(unread badges on friend Message links)*
- `client/src/app/features/dms/dms.component.ts` *(`markRead` on open)*
- `client/src/app/features/rooms/room-detail/room-detail.component.ts` *(`markRead` on open)*

**New — tests:**
- `server/ChatApp.Tests/Unit/Messaging/UnreadService_Tests.cs`
- `server/ChatApp.Tests/Integration/Messaging/UnreadIntegrationTests.cs`

---

## Follow-ups for slice 10 (Edit / delete / reply)

- Message deletes and edits do not require unread count adjustments (counts are per-chat-open, not per-message).
- Sound ping (`User.SoundOnMessage` field already in entity, API, and client `MeResponse`) — wire in a later slice as a `NotificationService` that listens to `MessageCreated` events on the chat hub and plays a sound when `document.hidden && currentUser.soundOnMessage`.
