# Slice 6 — Presence States

Sixth slice. First feature that exercises the `PresenceHub` end-to-end: tabs heartbeat, server aggregates per-user state from N connections, transitions fan out to the union of the user's contacts (room-mates land in slice 7). Delivers the "A goes idle → AFK → closes tab → offline; B sees it live" demo from the implementation plan.

## Context

`docs/implementation-plan.md` slice 6; depends on slice 3 (Friendship + PersonalChat entities; gives us the fan-out target list) and slice 4 (PresenceHub wired, client `SignalrService` exposes the raw `HubConnection`).

Authoritative requirements that fix this slice's shape:

- **Product spec §4** — states `online | afk | offline`; client watches `mousemove / click / keydown / scroll` with a 60 s active window; heartbeat every 20 s to `PresenceHub.Heartbeat(isActive)`; p95 transition visible < 2 s; broadcast only to contacts + room-mates.
- **Arch doc §Presence flow** — per-connection `lastActiveAt` + `isActive`; 2 s server tick recomputes user state; aggregator is the owner of the state machine.
- **Arch doc §Scale-out path** — `IPresenceStore` listed as a clean seam; **decision below: introduce the interface now** rather than inline, so slice 6 already looks like the future Redis swap.
- **Arch doc §Decisions vs. spec** — presence is kept in-proc; no backplane at MVP.
- **Slice 4 decisions** — `PresenceHub.OnConnectedAsync` already adds to `user:{userId}`; that group is the fan-out target for broadcasts.
- **Slice 5 decisions** — `ChatHub.OnConnectedAsync` already joins `pchat:{id}` groups. Not reused here; presence broadcasts target `user:{id}` groups, not `pchat:{id}`.

Outcome: two browsers logged in as A and B (friended). A stops touching the tab → after 60 s B's contacts list shows A's dot flip from green to yellow (`afk`). A closes the tab → within ~2 s B sees it flip to grey (`offline`). A opens a second tab while already present in the first → state stays `online`; closing only one tab keeps state `online` until both are gone.

## Decisions

| Topic | Decision | Rationale |
|---|---|---|
| State machine thresholds | `online` = any connection active within last 60 s; `afk` = ≥1 connection but all inactive > 60 s; `offline` = no connections | Matches spec §4 verbatim. |
| Heartbeat method signature | `Task Heartbeat(bool isActive)` on `PresenceHub`; returns `Task` (fire-and-forget from client). | Spec: client sends `isActive` flag derived from its 60 s activity window. Server also treats the *arrival* of the heartbeat as implicit `lastActiveAt = UtcNow` when `isActive = true`. |
| Server tick | `IHostedService` (`PresenceTickService`) with `PeriodicTimer(TimeSpan.FromSeconds(2))`; calls `PresenceAggregator.RecomputeAsync(ct)` on each tick | 2 s cadence matches arch doc; single shared timer avoids per-user timers. |
| Store abstraction | `IPresenceStore` interface + `InMemoryPresenceStore` (singleton, `ConcurrentDictionary<Guid userId, ConcurrentDictionary<string connId, ConnectionState>>`) | User chose the seam now. Mirrors arch doc §Scale-out. `InMemoryPresenceStore` owns the raw connection map; `PresenceAggregator` owns the state-machine + broadcast logic on top. |
| Fan-out target resolver | New `IPresenceFanoutResolver` with one method `Task<IReadOnlyCollection<Guid>> ResolveTargetsAsync(Guid userId, CancellationToken ct)`. Slice 6 impl returns **accepted friends** only, via `FriendshipService.ListAsync`. Slice 7 extends to `UNION(friends, roommates)` without touching the aggregator. | User chose "contacts only, room-mates deferred" — cleanest seam, no stub code. |
| Initial snapshot on connect | In `PresenceHub.OnConnectedAsync`, after registering the connection, send `PresenceSnapshot` event **to the caller only** with `{ entries: [{ userId, state }, ...] }` for every non-offline contact | User chose push-on-connect (fastest, one round-trip, no extra REST). |
| Broadcast channel | `IHubContext<PresenceHub>.Clients.Group($"user:{targetId}").SendAsync("PresenceChanged", { userId, state })` for each target in the fan-out set | `user:{id}` group already populated by `PresenceHub.OnConnectedAsync` (slice 4). |
| Self-notification | Do **not** fan out a user's own state changes to themselves. | Client derives self-state locally; avoids noise. |
| Idle detection on client | New `ActivityTrackerService`: listens `mousemove`, `click`, `keydown`, `scroll` on `document`; debounced last-activity timestamp; `isActiveNow()` returns `Date.now() - lastActivity < 60_000`. Uses `fromEvent(document, ...)` with `Subject`/signal. | Pure RxJS/signal; no timers leak across logout because service is scoped-destroyed with `AppShellComponent`. |
| Heartbeat cadence | `setInterval(() => presenceConn.invoke('Heartbeat', activityTracker.isActiveNow()), 20_000)` | 20 s per spec. Also sends a heartbeat immediately on hub `onreconnected` so state is not stuck at offline after a blip. |
| Presence state shape on client | Extend `UserSummary` (or wrap `FriendSummary` with a new `presence` signal map keyed by `userId`) — plan uses **separate `PresenceService`** with `presenceByUserId = signal<Map<string, 'online'\|'afk'\|'offline'>>(new Map())` to keep `FriendshipsService` pure. | Presence is ephemeral; mixing it into the friend DTO would mean re-fetching friends just to mutate presence. A separate store keyed by `userId` is cleaner and room-mate presence will merge into the same map in slice 7. |
| `PresenceChanged` payload | `{ userId: string (guid), state: 'online' \| 'afk' \| 'offline' }`. `PresenceSnapshot` payload: `{ entries: { userId: string, state: 'online' \| 'afk' }[] }` (offline omitted — default assumption). | Small, forward-compatible. |

### Deferred (explicit — handed to later slices)

- Room-mate fan-out (`RoomMember` enumeration) — **slice 7** extends `IPresenceFanoutResolver`.
- Token-bucket rate limit on `Heartbeat` — slice 16.
- UI indicator in app-shell / profile badge — cosmetic; slice 9 unread-badges pass can absorb it. Not blocking.
- Redis `DistributedPresenceStore` impl — scale-out; not MVP.
- Self-view of "I'm AFK" banner — not in spec.

## Scope

### Server — files to create

| Path | Purpose |
|------|---------|
| `server/ChatApp.Domain/Abstractions/IPresenceStore.cs` | Interface. Methods: `void Register(Guid userId, string connectionId)`, `void Unregister(Guid userId, string connectionId)`, `void Touch(Guid userId, string connectionId, bool isActive, DateTime nowUtc)`, `IReadOnlyDictionary<Guid, PresenceState> SnapshotStates(DateTime nowUtc)`, `PresenceState? GetState(Guid userId, DateTime nowUtc)`. |
| `server/ChatApp.Domain/Presence/PresenceState.cs` | Enum `Online, Afk, Offline`. Lives in Domain (no EF refs). |
| `server/ChatApp.Domain/Presence/ConnectionState.cs` | Record: `DateTime LastActiveAt, bool IsActive`. Internal to store impl but expressible via the interface. |
| `server/ChatApp.Domain/Services/Presence/IPresenceFanoutResolver.cs` | Interface. `Task<IReadOnlyCollection<Guid>> ResolveTargetsAsync(Guid userId, CancellationToken ct)`. |
| `server/ChatApp.Data/Services/Presence/FriendshipFanoutResolver.cs` | Implements `IPresenceFanoutResolver`. Scoped. Calls `FriendshipService.ListAsync` and projects `FriendOutcome.User.Id`. Room-mates will merge in slice 7 via a second resolver or extension to this class. |
| `server/ChatApp.Api/Infrastructure/Presence/InMemoryPresenceStore.cs` | Singleton impl of `IPresenceStore`. Backed by `ConcurrentDictionary<Guid, ConcurrentDictionary<string, ConnectionState>>`. `SnapshotStates(now)` walks the dictionary: any conn with `LastActiveAt >= now - 60s` → `Online`; else if dict non-empty → `Afk`; else absent (treated as `Offline`). |
| `server/ChatApp.Api/Infrastructure/Presence/PresenceAggregator.cs` | Singleton. Injects `IPresenceStore`, `IHubContext<PresenceHub>`, `IServiceScopeFactory` (for per-tick resolver scope), `ILogger<PresenceAggregator>`. Maintains `ConcurrentDictionary<Guid, PresenceState> _lastBroadcastState`. Public methods: `Task OnConnectedAsync(Guid userId, string connId, CancellationToken ct)`, `Task OnDisconnectedAsync(Guid userId, string connId, CancellationToken ct)`, `Task HeartbeatAsync(Guid userId, string connId, bool isActive, CancellationToken ct)`, `Task RecomputeAsync(CancellationToken ct)`, `Task<IReadOnlyCollection<(Guid UserId, PresenceState State)>> GetContactSnapshotAsync(Guid viewerId, CancellationToken ct)` (used by `OnConnectedAsync` push). Broadcast only on state *change*. |
| `server/ChatApp.Api/Infrastructure/Presence/PresenceTickService.cs` | `BackgroundService`. `PeriodicTimer(TimeSpan.FromSeconds(2))`. Loop: `await aggregator.RecomputeAsync(stoppingToken)`; swallow + log exceptions per tick so one failure doesn't kill the loop. |
| `server/ChatApp.Api/Contracts/Presence/PresenceChangedEvent.cs` | `{ Guid UserId, string State }` (state serialised as lowercase string). |
| `server/ChatApp.Api/Contracts/Presence/PresenceSnapshotEvent.cs` | `{ IReadOnlyCollection<PresenceSnapshotEntry> Entries }`; entry: `{ Guid UserId, string State }`. |

### Server — files to modify

| Path | Change |
|------|--------|
| `server/ChatApp.Api/Hubs/PresenceHub.cs` | Inject `PresenceAggregator`. `OnConnectedAsync`: keep existing `user:{userId}` group-add + log line, then `await aggregator.OnConnectedAsync(userId, Context.ConnectionId, ct)`, then call `aggregator.GetContactSnapshotAsync(userId, ct)` and send `PresenceSnapshot` via `Clients.Caller.SendAsync("PresenceSnapshot", new PresenceSnapshotEvent(...))`. `OnDisconnectedAsync`: `await aggregator.OnDisconnectedAsync(userId, Context.ConnectionId, ct)`, then existing log line. Add new hub method `public Task Heartbeat(bool isActive) => aggregator.HeartbeatAsync(userId, Context.ConnectionId, isActive, Context.ConnectionAborted);`. |
| `server/ChatApp.Api/Program.cs` | Register: `builder.Services.AddSingleton<IPresenceStore, InMemoryPresenceStore>();`, `builder.Services.AddSingleton<PresenceAggregator>();`, `builder.Services.AddScoped<IPresenceFanoutResolver, FriendshipFanoutResolver>();`, `builder.Services.AddHostedService<PresenceTickService>();`. Place with other service registrations (near `ChatBroadcaster` singleton + `FriendshipService` scoped). |

### Client — files to create

| Path | Purpose |
|------|---------|
| `client/src/app/core/presence/presence.models.ts` | `export type PresenceState = 'online' \| 'afk' \| 'offline'; export interface PresenceChangedEvent { userId: string; state: PresenceState; } export interface PresenceSnapshotEvent { entries: { userId: string; state: PresenceState; }[]; }` |
| `client/src/app/core/presence/activity-tracker.service.ts` | Root-scoped service. Listens `document` `mousemove`/`click`/`keydown`/`scroll` (with `{ passive: true }`); updates a private `lastActivityAt = performance.now()`; exposes `isActiveNow(): boolean` → `performance.now() - lastActivityAt < 60_000`. Uses `DestroyRef` + `takeUntilDestroyed` so teardown is automatic. |
| `client/src/app/core/presence/presence.service.ts` | Root-scoped service. Injects `SignalrService`, `ActivityTrackerService`. Holds `_stateByUserId = signal<ReadonlyMap<string, PresenceState>>(new Map())`. Exposes `stateOf(userId: string) = computed<PresenceState>(...)` returning `'offline'` default. Method `start()`: registers `presenceConn.on('PresenceChanged', ...)` and `presenceConn.on('PresenceSnapshot', ...)` handlers (updating the signal), starts a `setInterval(20_000)` heartbeat loop invoking `presenceConn.invoke('Heartbeat', activity.isActiveNow())`, and sends one immediate heartbeat on `presenceConn.onreconnected`. Method `stop()`: clears the interval + removes handlers. |

### Client — files to modify

| Path | Change |
|------|--------|
| `client/src/app/features/app-shell/app-shell.component.ts` | `inject(PresenceService)`. After `signalr.start()` resolves in `ngOnInit`, call `presence.start()`. In `ngOnDestroy`, call `presence.stop()` before `signalr.stop()` so outstanding handlers detach cleanly. |
| `client/src/app/features/contacts/contacts.component.ts` | `inject(PresenceService)`. Expose helper `presenceOf(userId: string) = presence.stateOf(userId)` (or inline in template). |
| `client/src/app/features/contacts/contacts.component.html` | Inside each `.friend-row`, next to the avatar, add `<span class="presence-dot" [class.online]="presenceOf(friend.user.id)() === 'online'" [class.afk]="presenceOf(friend.user.id)() === 'afk'" [class.offline]="presenceOf(friend.user.id)() === 'offline'" [attr.title]="presenceOf(friend.user.id)()"></span>`. |
| `client/src/app/features/contacts/contacts.component.scss` | `.presence-dot` base: `8px` circle, inline-block; `&.online { background: #3BA55D; } &.afk { background: #FAA61A; } &.offline { background: #747F8D; }`. Positioning: overlap the avatar bottom-right corner (`position: absolute; bottom: 0; right: 0;` inside a relative avatar wrapper). |

### Out of scope (explicit — handed to later slices)

- DM view presence indicator — trivial add in slice 9/10 once `DmsComponent` renders the peer's header; not blocking this slice's demo.
- Room member list presence dots — slice 7 joins the rooms feature and extends the resolver; presence service will pick room-mates up automatically via new `PresenceChanged` events.
- Server-authoritative presence in `PersonalChat` header (peer name + dot) — slice 9 polish.
- Rate-limiting `Heartbeat` (abusive client spamming invoke) — slice 16.

## Key flows (reference)

### User connects, sees live peers

1. User A logs in → `AppShellComponent.ngOnInit` → `signalr.start()` → `presence.start()`.
2. `PresenceHub.OnConnectedAsync` on server: add to `user:{A}` group; `aggregator.OnConnectedAsync(A, connId)` → store registers a `ConnectionState { LastActiveAt = now, IsActive = true }`; if A's prior state was not `Online`, recompute locally and broadcast `PresenceChanged` to A's fan-out targets.
3. Server calls `aggregator.GetContactSnapshotAsync(A)`: resolver returns A's friend IDs; for each, read `store.GetState(friendId, now)`; filter out `Offline`; send `PresenceSnapshot` to `Clients.Caller`.
4. Client `presence.service` `on('PresenceSnapshot')` handler overwrites the signal map with the snapshot entries.
5. Contacts view re-renders dots from the signal.

### User goes idle → AFK → closes tab

1. A stops interacting. `ActivityTrackerService.lastActivityAt` stops updating.
2. Every 20 s, `presence.service` sends `Heartbeat(false)` (since `now - lastActivityAt > 60_000` after the first minute).
3. Server: aggregator updates the connection's `IsActive = false` but does **not** touch `LastActiveAt` (since `isActive=false`); `LastActiveAt` stays at the last active heartbeat.
4. `PresenceTickService` tick: `store.SnapshotStates(now)` sees A's connection with `LastActiveAt < now - 60s` → state = `Afk`. Aggregator compares with `_lastBroadcastState[A]` (`Online`) → they differ → broadcast `PresenceChanged(A, 'afk')` to all targets resolved via `IPresenceFanoutResolver` for A.
5. B (who is A's friend) receives the event on its `user:{B}` group → `presence.service` updates `stateByUserId` → contacts dot flips yellow.
6. A closes the tab → `PresenceHub.OnDisconnectedAsync` → `aggregator.OnDisconnectedAsync` → store removes the connection → next tick (or immediate recompute on disconnect, see note) sees zero connections → state = `Offline` → broadcast.

**Note:** `OnDisconnectedAsync` triggers an immediate `RecomputeSingleAsync(A)` in addition to the periodic tick, so offline propagates in ~200 ms rather than up to 2 s. This is a tiny optimisation for the most common transition; the hot path is still the 2 s tick for AFK.

### Multi-tab semantics

1. A has tabs T1 and T2. T1 is focused and active; T2 is minimised (inactive).
2. T1 sends `Heartbeat(true)` every 20 s; T2 sends `Heartbeat(false)` every 20 s.
3. Aggregator: T1's `LastActiveAt` stays current → `Online` for A (any active connection wins).
4. A closes T1. Only T2 remains. Next tick: T2's `LastActiveAt` is already > 60 s stale → state = `Afk`.
5. A closes T2 → `Offline`.

## Implementation notes

### State-machine core (pseudocode)

```csharp
public PresenceState? SnapshotState(Guid userId, DateTime nowUtc) {
    if (!_map.TryGetValue(userId, out var conns) || conns.IsEmpty) return null; // offline
    var threshold = nowUtc.AddSeconds(-60);
    foreach (var (_, s) in conns) if (s.LastActiveAt >= threshold) return PresenceState.Online;
    return PresenceState.Afk;
}
```

`null` is treated as `Offline` by the aggregator so the store itself never carries offline entries (keeps memory bounded).

### Touch semantics

- `Register(userId, connId)` sets `LastActiveAt = UtcNow, IsActive = true` (assume a just-connected tab is active).
- `Touch(userId, connId, isActive, nowUtc)`:
  - if `isActive == true`: set `LastActiveAt = nowUtc, IsActive = true`.
  - if `isActive == false`: leave `LastActiveAt` alone, set `IsActive = false`. The 60 s staleness of `LastActiveAt` is what flips the user to AFK.

### Why `IServiceScopeFactory` in the singleton aggregator

`IPresenceFanoutResolver` and `FriendshipService` are scoped (they hold `ChatDbContext`). The aggregator is singleton. On each recompute / each connect, the aggregator opens an `await using var scope = _scopeFactory.CreateAsyncScope()` and resolves `IPresenceFanoutResolver` from `scope.ServiceProvider`. Standard pattern, identical to how hosted services consume EF services.

### Broadcast dedup

`PresenceAggregator` keeps `ConcurrentDictionary<Guid, PresenceState> _lastBroadcastState`. On recompute, only users whose computed state differs from `_lastBroadcastState[userId]` generate a broadcast; the dictionary is updated in the same pass. Offline transitions remove the entry from both `_lastBroadcastState` and (implicitly) the store.

## Verification

1. **Build clean.** `dotnet build server/ChatApp.sln` — zero warnings. `cd client && npm run build` — zero errors.
2. **Happy path (two browsers).** `docker compose -f infra/docker-compose.yml up -d --build`. Register A, B in two browsers; A friend-requests B; B accepts.
   - Both tabs focused → each sees the other's dot green within < 2 s of login (snapshot on connect).
   - A stops touching the tab. After ~60 s, B's view of A flips to yellow (AFK).
   - A closes the tab. Within ~2 s, B's view of A flips to grey (offline).
3. **Multi-tab.** A opens a second tab (T2). Close T1 → still green on B (T2 is active). Stop touching T2 for 60 s → yellow. Close T2 → grey.
4. **Heartbeat visible.** Browser devtools → WebSocket frames on `/hub/presence` show `{"target":"Heartbeat","arguments":[true|false]}` every 20 s.
5. **Unit tests** (`ChatApp.Tests/Unit/Presence/`):
   - `PresenceStore_NoConnections_ReturnsNull` — offline.
   - `PresenceStore_OneActiveConnection_ReturnsOnline`.
   - `PresenceStore_OneStaleInactiveConnection_ReturnsAfk` — `LastActiveAt = now - 61s`.
   - `PresenceStore_OneActiveOneStale_ReturnsOnline` — any active wins.
   - `PresenceAggregator_RecomputeAsync_BroadcastsOnlyOnChange` — fake `IHubContext`, verify a second recompute with the same state calls `SendAsync` zero additional times.
   - `PresenceAggregator_DisconnectLast_BroadcastsOffline`.
   - `FriendshipFanoutResolver_ReturnsOnlyAcceptedFriends` — in-memory DB seed, pending friendships excluded.
6. **Integration test** (`ChatApp.Tests/Integration/Presence/PresenceHubIntegrationTests.cs`):
   - Two `HubConnection` clients against `ChatApiFactory` as users A (friends with B) and B.
   - Assert B receives `PresenceSnapshot` with A=`online` upon B's connect.
   - Assert both receive `PresenceChanged` within 3 s of disconnecting A.
7. **Manual nginx timing.** `docker compose logs api | grep PresenceHub` — connect/disconnect log lines appear; no exceptions from the tick service.

## Critical files at a glance

- `server/ChatApp.Domain/Abstractions/IPresenceStore.cs` *(new)*
- `server/ChatApp.Domain/Presence/PresenceState.cs` *(new)*
- `server/ChatApp.Domain/Services/Presence/IPresenceFanoutResolver.cs` *(new)*
- `server/ChatApp.Data/Services/Presence/FriendshipFanoutResolver.cs` *(new)*
- `server/ChatApp.Api/Infrastructure/Presence/InMemoryPresenceStore.cs` *(new)*
- `server/ChatApp.Api/Infrastructure/Presence/PresenceAggregator.cs` *(new)*
- `server/ChatApp.Api/Infrastructure/Presence/PresenceTickService.cs` *(new)*
- `server/ChatApp.Api/Hubs/PresenceHub.cs` *(Heartbeat + connect/disconnect aggregator calls + snapshot push)*
- `server/ChatApp.Api/Program.cs` *(DI: store singleton, aggregator singleton, resolver scoped, hosted service)*
- `client/src/app/core/presence/presence.service.ts` *(new)*
- `client/src/app/core/presence/activity-tracker.service.ts` *(new)*
- `client/src/app/core/presence/presence.models.ts` *(new)*
- `client/src/app/features/app-shell/app-shell.component.ts` *(start/stop presence.service)*
- `client/src/app/features/contacts/contacts.component.{ts,html,scss}` *(presence dot)*

## Output location

This plan should be written to `docs/specs/features/07-presence-states.md` once approved (the features folder uses 1-indexed filenames that lag slice numbers by one because `01-foundation.md` = slice 0; slice 6 → `07-presence-states.md`).
