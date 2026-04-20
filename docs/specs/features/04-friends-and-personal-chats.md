# Slice 3 — Friends + personal chats

Third slice after Foundation (0), Identity (1), Profile/Sessions (2). Introduces the Social bounded context. Lets two users find each other by username, send a friend request with an optional note, accept/decline it, unfriend, and cancel an outgoing request. Accepting a friendship auto-creates a `PersonalChat` row for the pair; unfriending deletes the `Friendship` but keeps the `PersonalChat` so messages re-appear on re-friend (per product spec §5). No messaging, no presence, no UI ban plumbing yet — those land in slices 5/6/14.

## Context

`docs/implementation-plan.md` slice 3; depends on slice 1 (Identity — gives us `User`, `ICurrentUser`, `[Authorize]`, the cookie pipeline and session cache, and `AuthErrors`-style ProblemDetails shape). Slice 2 shipped `/api/profile/avatar/{userId}` which the Contacts page will use to render friend avatars; no other slice-2 surface is touched.

Authoritative requirements that fix this slice's shape:

- **Product spec §2** — `Friendship(user_id_low, user_id_high, state=pending|accepted, requester_id, request_note?, created_at, accepted_at?)` — one row per unordered pair. `PersonalChat(id, user_a_id, user_b_id, created_at)` auto-created on accept.
- **Product spec §5** — Request by username; optional note ≤500 chars. Recipient accepts/declines. On accept, `Friendship.state = accepted` AND a `PersonalChat` is auto-created. "Remove friend: deletes the friendship. Existing `PersonalChat` is hidden from both sidebars but messages are retained (they reappear on re-friend)."
- **Product spec §10** — Contacts sits in the right sidebar/top bar of the app shell; this slice stands up the `/app/contacts` page.
- **Arch doc §Bounded contexts — Social Graph** — `FriendshipService`, `PersonalChatService` live in `ChatApp.Domain/Services/Social/`. Accepting a friendship auto-creates the `PersonalChat`. `UserBan` consultation on friend-request send is **deferred to slice 14** (entity doesn't exist yet).
- **Arch doc §Data ownership** — `friendships`, `personal_chats` tables belong to Social. Single `ChatDbContext`, one migrations history. Cross-context reads through services only.

Outcome: user A opens `/app/contacts`, searches for user B by username, sends a request with a short note; B sees it in their Incoming list, accepts; both now see each other under "Friends" with avatar + display name; a `personal_chats` row exists for the pair and its `id` is surfaced on each friend entry so slice 5 can wire the DM view without schema churn. A unfriends B; the `friendships` row is gone but the `personal_chats` row is still there. A re-sends a request, B accepts — the same `personal_chats` row is reused.

## Decisions

Interview answers folded in; *[decided]* flags items that closed a genuinely open option.

| Topic | Decision | Rationale |
|---|---|---|
| Endpoint shape | **Action sub-routes** — `POST /api/friendships` (send by username), `POST /api/friendships/{id}/accept`, `POST /api/friendships/{id}/decline`, `DELETE /api/friendships/{id}` (unfriend when accepted; cancel when you're the requester on a pending row) | *[decided]* — matches `AuthController`'s action-verb style (`/auth/login`, `/auth/change-password`). Explicit verbs keep authz rules per-endpoint (only requester can cancel; only target can decline; either party can unfriend) instead of hiding them inside a PATCH body. |
| Row lifecycle on decline / unfriend / cancel | **Hard delete.** Decline removes the pending row; unfriend removes the accepted row; cancel removes the pending row by its requester | *[decided]* — product spec §5 says "Remove friend: deletes the friendship." No declined-state machine, no re-request cooldown. Re-request after decline is allowed immediately; if this turns out to be a harassment vector the fix is slice 14's `UserBan`, not a schema change here. |
| `PersonalChat` lifecycle | **Auto-create on accept; keep across unfriend; reuse on re-friend.** Look up by unordered `(user_a_id, user_b_id)` pair; insert only if absent | *[decided]* — product spec §5 "messages are retained (they reappear on re-friend)" requires the chat row to survive an unfriend so future messages can FK it. Uniqueness on the unordered pair is enforced by a functional unique index (see Schema). |
| List endpoint | **Single `GET /api/friendships` returning `{ friends, incoming, outgoing }`** | *[decided]* — Contacts page needs all three sections on load; one request, one invalidation after mutate. Incoming = pending rows where `requester_id != me`; outgoing = pending where `requester_id == me`; friends = accepted. |
| Pair ordering in row | `user_id_low = min(a, b)`, `user_id_high = max(a, b)` by `Guid.CompareTo` | *[decided]* — product spec §2 names the columns `user_id_low`/`user_id_high`; the low/high ordering gives a natural unique key on the unordered pair. Same trick used for `personal_chats.user_a_id`/`user_b_id`. |
| Friend-request validation | Cannot friend self (400 `cannot_friend_self`); target must exist and not be soft-deleted (404 `user_not_found`); no active friendship row for the pair (409 `friendship_exists` — covers both pending-either-direction and already-accepted) | *[decided]* — `friendship_exists` is deliberately one code; the client renders the same "already connected or pending" message regardless of direction. Prevents user-enumeration via differential error codes on pending-you-sent vs pending-they-sent. |
| Request note | Optional, stored verbatim (no HTML, no markdown), ≤500 chars after trim, empty string coerced to null | Matches spec §5 "optional note (≤500 chars)". Trim+coerce keeps `null` meaning "no note" unambiguously. |
| Sender's note visibility | Returned on the **incoming** entry (recipient sees it) and the **outgoing** entry (sender sees what they wrote). Not surfaced once the friendship is `accepted` | *[decided]* — the note is useful context during the pending phase; after accept, it's noise. Dropping it from the accepted payload keeps the friend list tight. |
| Accept / decline authz | Only the **target** (`requester_id != me`) can accept or decline | *[decided]* — accepting your own outgoing request is nonsense; enforce with 404 (treat as "no such pending row addressed to you") so the endpoint isn't an existence oracle for outgoing-only friendship ids. |
| Cancel vs decline on pending | `DELETE /api/friendships/{id}` is polymorphic: if the caller is the requester on a pending row → cancel (204); if target on pending → treat as decline (204, same side effect); if either party on an accepted row → unfriend (204) | *[decided]* — one DELETE verb, three contexts, same SQL. Simpler client code; the authz check reduces to "am I one of the two parties on this row?". |
| Sort order | Friends: `display_name ASC`, tiebreak `username ASC`. Incoming / outgoing: `created_at DESC` | Friends list is scanned visually by name; pending lists are a queue, newest first. |
| `PersonalChatId` exposure | Each **friend** entry in the list payload includes `personal_chat_id` | *[decided]* — slice 5 opens a DM view by PersonalChat id. Surfacing it now means slice 5 adds no field. Cost: one extra join in the list query, trivially indexed. |
| DI + folder layout | `server/ChatApp.Domain/Services/Social/{FriendshipService,PersonalChatService,SocialErrors}.cs`; `server/ChatApp.Api/Controllers/Social/FriendshipsController.cs`; `server/ChatApp.Api/Contracts/Social/*`. Both services `AddScoped` in `Program.cs` under a `// Social` comment block matching the existing `// Identity` grouping | Keeps context isolation visible in Program.cs; matches slice 2's layout discipline. |
| Error-result pattern | Services return `(bool Ok, string? Code, string? Message, T? Value)` tuples, same shape as `AuthService`. Controllers map `Code` → ProblemDetails via a shared `Problem(code, message, statusCode)` helper already in use | Zero new plumbing. |
| Transactional accept | `AcceptAsync` runs one `EF` transaction: update `friendship.state=accepted, accepted_at=now`, upsert `personal_chats` row (insert if the `(low, high)` pair doesn't already exist). Idempotent on retry | Failure between the two writes leaves the friendship pending with no chat, which is recoverable (retry accept); without the transaction, a half-written accept could leak a chat without a friendship or vice versa. |

### Deferred (explicit — handed to later slices)

- `UserBan` consultation on send / accept (slice 14) — entity doesn't exist yet. A comment marker in `FriendshipService.RequestAsync` (`// TODO(slice-14): reject if UserBan is active either direction`) makes the later addition a one-line insert.
- Presence dots next to contacts — slice 6 (`PresenceHub`).
- Real-time `FriendshipChanged` events over `ChatHub` / `PresenceHub` so the other party's inbox updates without a refetch — slice 4/6.
- Friend request rate limit (slice 16).
- Invite-from-room-member-list entry point — slice 12's invitation UI will reuse the same `POST /api/friendships` endpoint; no server change needed.
- `PersonalChat` list endpoint for the sidebar — slice 5; the chat id is already piggybacking on the friend entry, so the sidebar can be built from the friends payload without a new endpoint.

## Scope

### Server — files to create

| Path | Purpose |
|------|---------|
| `server/ChatApp.Data/Entities/Social/Friendship.cs` | Composite-key entity: `{ Guid Id, Guid UserIdLow, Guid UserIdHigh, FriendshipState State, Guid RequesterId, string? RequestNote, DateTimeOffset CreatedAt, DateTimeOffset? AcceptedAt }`. `Id` is the public handle for URLs; `(UserIdLow, UserIdHigh)` is the unique business key. |
| `server/ChatApp.Data/Entities/Social/FriendshipState.cs` | `enum FriendshipState { Pending = 0, Accepted = 1 }`. No `Declined` value — declined rows are deleted. |
| `server/ChatApp.Data/Entities/Social/PersonalChat.cs` | `{ Guid Id, Guid UserAId, Guid UserBId, DateTimeOffset CreatedAt }`. `UserAId < UserBId` by `Guid.CompareTo` (same low/high invariant as Friendship). |
| `server/ChatApp.Data/Configurations/Social/FriendshipConfiguration.cs` | `ToTable("friendships")`; PK on `Id`; unique index on `(UserIdLow, UserIdHigh)` named `ux_friendships_pair`; non-unique index on `RequesterId` for outgoing lookups. Enum stored as `int`. |
| `server/ChatApp.Data/Configurations/Social/PersonalChatConfiguration.cs` | `ToTable("personal_chats")`; PK on `Id`; unique index on `(UserAId, UserBId)` named `ux_personal_chats_pair`. |
| `server/ChatApp.Data/Migrations/{timestamp}_AddSocial.cs` | Generated via `dotnet ef migrations add AddSocial --project server/ChatApp.Data --startup-project server/ChatApp.Api`. Creates both tables + the two unique indexes + the requester index. |
| `server/ChatApp.Domain/Services/Social/SocialErrors.cs` | Static class mirroring `AuthErrors` — error-code constants (`cannot_friend_self`, `user_not_found`, `friendship_exists`, `friendship_not_found`, `note_too_long`) and their default messages. |
| `server/ChatApp.Domain/Services/Social/FriendshipService.cs` | `RequestAsync(Guid me, string targetUsername, string? note)`, `AcceptAsync(Guid me, Guid friendshipId)`, `DeclineAsync(Guid me, Guid friendshipId)`, `UnfriendOrCancelAsync(Guid me, Guid friendshipId)`, `ListAsync(Guid me)` returning the three grouped projections. Collaborates with `PersonalChatService.EnsureAsync` inside the accept transaction. |
| `server/ChatApp.Domain/Services/Social/PersonalChatService.cs` | `EnsureAsync(Guid a, Guid b, CancellationToken)` — normalises to `(low, high)`, `INSERT ... ON CONFLICT DO NOTHING` semantics via EF (try insert; on unique-violation catch, re-select existing). Returns the `PersonalChat.Id` either way. Exposed for slice 5 as well. |
| `server/ChatApp.Api/Controllers/Social/FriendshipsController.cs` | `[Authorize]` class. `POST /api/friendships`, `GET /api/friendships`, `POST /api/friendships/{id:guid}/accept`, `POST /api/friendships/{id:guid}/decline`, `DELETE /api/friendships/{id:guid}`. All call into `FriendshipService`; map service error codes → ProblemDetails statuses (400/404/409). |
| `server/ChatApp.Api/Contracts/Social/SendFriendRequestRequest.cs` | `{ string Username, string? Note }`. |
| `server/ChatApp.Api/Contracts/Social/FriendshipListResponse.cs` | `{ List<FriendSummary> Friends, List<PendingFriendship> Incoming, List<PendingFriendship> Outgoing }`. |
| `server/ChatApp.Api/Contracts/Social/FriendSummary.cs` | `{ Guid FriendshipId, Guid PersonalChatId, UserSummary User, DateTimeOffset AcceptedAt }`. |
| `server/ChatApp.Api/Contracts/Social/PendingFriendship.cs` | `{ Guid FriendshipId, UserSummary User, string? Note, DateTimeOffset CreatedAt }`. `User` is the *other* party in both the incoming and outgoing views. |
| `server/ChatApp.Api/Contracts/Social/UserSummary.cs` | `{ Guid Id, string Username, string DisplayName, string? AvatarUrl }`. `AvatarUrl` built the same way `AuthController.ToMe` builds it: `user.AvatarPath is null ? null : $"/api/profile/avatar/{user.Id}"`. Shared by all three list shapes. |

### Server — files to modify

| Path | Change |
|------|--------|
| `server/ChatApp.Data/ChatDbContext.cs` | Add `public DbSet<Friendship> Friendships => Set<Friendship>();` and `public DbSet<PersonalChat> PersonalChats => Set<PersonalChat>();`. `ApplyConfigurationsFromAssembly` already picks up the new configs. |
| `server/ChatApp.Api/Program.cs` | Under a new `// Social` block: `builder.Services.AddScoped<FriendshipService>();` and `builder.Services.AddScoped<PersonalChatService>();`. No other wiring — no new middleware, options, or SignalR. |

### Client — files to create

| Path | Purpose |
|------|---------|
| `client/src/app/core/social/friendships.service.ts` | Signals wrapper: `list = signal<FriendshipListResponse | null>(null)`, plus `refresh()`, `sendRequest(username, note?)`, `accept(id)`, `decline(id)`, `unfriend(id)`, `cancelOutgoing(id)` (all call `DELETE /api/friendships/{id}` — the last three are distinct methods on the client purely for readable call sites). On success, each mutator re-invokes `refresh()`. |
| `client/src/app/core/social/friendships.models.ts` | TS mirrors of `FriendshipListResponse`, `FriendSummary`, `PendingFriendship`, `UserSummary`. |
| `client/src/app/features/contacts/contacts.component.{ts,html,scss}` | Page with three sections: "Add friend" (username + optional note textarea + Send button), "Incoming requests" (list with Accept / Decline), "Outgoing requests" (list with Cancel), "Friends" (list with avatar + display name + Unfriend). Uses `FriendshipsService.list()` signal; derives the four rendered lists with `computed()`. Errors rendered inline per-section using the ProblemDetails `code` mapped to a short string. |

### Client — files to modify

| Path | Change |
|------|--------|
| `client/src/app/app.routes.ts` | Add child route under `/app`: `{ path: 'contacts', loadComponent: () => import('./features/contacts/contacts.component').then(m => m.ContactsComponent), canActivate: [authGuard] }`. |
| `client/src/app/features/app-shell/app-shell.component.html` | Add `<a routerLink="contacts">Contacts</a>` to the top-bar nav alongside Profile and Sessions. No sidebar work yet — that arrives with slice 5's DM list. |

### Out of scope (explicit — handed to later slices)

- `UserBan` entity and enforcement on send/accept — slice 14.
- Friend presence dots — slice 6.
- Real-time inbox updates when the other party accepts/declines — slice 4 (`PresenceHub`) or slice 5 (`ChatHub` user group).
- Friend-request rate limit — slice 16.
- Invite from room member list — slice 12 reuses `POST /api/friendships`.
- `/api/personal-chats` list endpoint and DM sidebar — slice 5.

## Key flows (reference)

### Send friend request

1. `POST /api/friendships { username, note? }` — `[Authorize]`.
2. Trim `username` and `note`; reject `note.Length > 500` with 400 `note_too_long`. Empty `note` → `null`.
3. Look up target by `UsernameNormalized` (same pattern as `AuthService` login). Not found or `DeletedAt != null` → 404 `user_not_found`.
4. `target.Id == me.Id` → 400 `cannot_friend_self`.
5. Compute `(low, high)` from the two ids. `SELECT 1 FROM friendships WHERE user_id_low = @low AND user_id_high = @high` — any hit (pending either way, or accepted) → 409 `friendship_exists`.
6. Insert `Friendship { State = Pending, RequesterId = me.Id, RequestNote = note, CreatedAt = now }`. Return 201 with the `PendingFriendship` projection for the *outgoing* view (so the client can splice it into its local state without a full refetch if it wants to; simple version just calls `refresh()`).

### Accept friend request

1. `POST /api/friendships/{id}/accept` — `[Authorize]`.
2. `SELECT` the friendship. Missing → 404 `friendship_not_found`. `State != Pending` → 404 `friendship_not_found` (same code — don't leak state). `RequesterId == me.Id` → 404 `friendship_not_found` (can't accept your own outgoing).
3. Begin transaction.
4. `UPDATE friendships SET state = 1, accepted_at = @now WHERE id = @id AND state = 0` (optimistic guard; rows-affected = 0 → transaction rollback + 404).
5. `PersonalChatService.EnsureAsync(low, high)` — returns the existing `PersonalChat.Id` if a row is already there (re-friend path), otherwise inserts a new one.
6. Commit. Return 200 with `FriendSummary { FriendshipId, PersonalChatId, User = theOtherParty, AcceptedAt = now }`.

### Decline friend request

1. `POST /api/friendships/{id}/decline` — `[Authorize]`.
2. Same authz + state guard as accept, **except** the caller must be the target (`RequesterId != me.Id`). Mismatch → 404 `friendship_not_found`.
3. `DELETE FROM friendships WHERE id = @id AND state = 0`. Rows-affected = 0 → 404. Otherwise 204.

### Unfriend / cancel outgoing

1. `DELETE /api/friendships/{id}` — `[Authorize]`.
2. `SELECT` the friendship. Must exist and `me.Id ∈ { UserIdLow, UserIdHigh }` — else 404 `friendship_not_found`. No state check: pending + you're the requester → cancel; pending + you're the target → same as decline; accepted → unfriend. Server doesn't distinguish.
3. `DELETE FROM friendships WHERE id = @id`. 204. `PersonalChat` row is **not** touched.

### List friendships

1. `GET /api/friendships` — `[Authorize]`.
2. Single query strategy: project all rows where `UserIdLow = @me OR UserIdHigh = @me` with a left-join to `personal_chats` on the unordered pair and an inner-join to `users` for the other-party summary. Partition in memory into `friends` (State = Accepted), `incoming` (State = Pending AND RequesterId != me), `outgoing` (State = Pending AND RequesterId == me). Expected row count per user is small (spec §11 cites ~50 contacts), so one-shot query + in-memory partition is cheaper than three separate queries.
3. Map to `FriendshipListResponse`. Sort: friends by `DisplayName ASC, Username ASC`; incoming / outgoing by `CreatedAt DESC`. Return 200.

## Verification

1. **Build clean.** `dotnet build server/ChatApp.sln` — no warnings.
2. **Migration.** `dotnet ef migrations add AddSocial --project server/ChatApp.Data --startup-project server/ChatApp.Api`. Inspect: creates `friendships` + `personal_chats` with the two unique indexes named `ux_friendships_pair` and `ux_personal_chats_pair`, plus `ix_friendships_requester_id`. Commit the migration file and model snapshot. `docker compose -f infra/docker-compose.yml up -d --build` starts clean and applies the migration on boot.
3. **Unit tests** (xUnit, no DB) in `server/ChatApp.Tests/Unit/Social/`:
   - `(low, high)` ordering helper: given `(a, b)` and `(b, a)` returns the same tuple.
   - `FriendshipService.RequestAsync` trims the note, coerces empty → null, rejects `> 500` with `note_too_long`.
   - `FriendshipService.RequestAsync` rejects self with `cannot_friend_self`.
4. **Integration tests** (Testcontainers Postgres + `WebApplicationFactory`) in `server/ChatApp.Tests/Integration/Social/`:
   - Register A, B in two cookie jars. A: `POST /api/friendships { username: "b" }` → 201. A: `GET /api/friendships` → `outgoing[0].user.username == "b"`. B: `GET /api/friendships` → `incoming[0].user.username == "a"` and `incoming[0].note` matches.
   - B: `POST /api/friendships/{id}/accept` → 200. Both jars: `GET /api/friendships` → `friends` contains the other, `personal_chat_id` is set, `incoming`/`outgoing` empty. Confirm `personal_chats` has exactly one row with `(low, high) = (min(A,B), max(A,B))`.
   - Duplicate request path: A re-sends to B while accepted → 409 `friendship_exists`. C sends to A while A↔B accepted → 201 (unrelated pair).
   - Authz: A tries to accept A's own outgoing (before B accepts) → 404 `friendship_not_found`. A tries to decline B's outgoing → 404.
   - Unfriend + re-friend keeps chat: A `DELETE /api/friendships/{id}` → 204. `personal_chats` row **still present**. A re-sends → B accepts → `GET /api/friendships` shows the same `personal_chat_id` as before (reused).
   - Cancel outgoing: A sends; A `DELETE /api/friendships/{id}` before B accepts → 204; B's `incoming` is now empty.
   - Self-request: `POST /api/friendships { username: "a" }` as A → 400 `cannot_friend_self`.
   - Unknown user: `POST /api/friendships { username: "nobody" }` → 404 `user_not_found`.
   - Note cap: 501-char note → 400 `note_too_long`.
5. **Compose smoke.** Two browsers, two users. In browser A, open `/app/contacts`, send a request to B's username with a note. In browser B, `/app/contacts` shows the incoming request with A's avatar and note; click Accept. Both browsers now show each other under Friends. In A, click Unfriend; confirm the row disappears. Re-send; B accepts; row comes back. (Manual DB peek: `personal_chats` count hasn't changed across the unfriend / re-friend cycle.)

## Follow-ups for slice 4 (Realtime backbone)

- `PresenceHub` / `ChatHub` `user:{userId}` group is the right channel to push `FriendshipChanged` so the other party's Contacts page updates without a refetch. Don't wire it in slice 3 — slice 4 establishes the hubs.

## Critical files at a glance

- `server/ChatApp.Data/Entities/Social/{Friendship,FriendshipState,PersonalChat}.cs`
- `server/ChatApp.Data/Configurations/Social/{FriendshipConfiguration,PersonalChatConfiguration}.cs`
- `server/ChatApp.Data/Migrations/{timestamp}_AddSocial.cs`
- `server/ChatApp.Data/ChatDbContext.cs` (DbSet additions)
- `server/ChatApp.Domain/Services/Social/{FriendshipService,PersonalChatService,SocialErrors}.cs`
- `server/ChatApp.Api/Controllers/Social/FriendshipsController.cs`
- `server/ChatApp.Api/Contracts/Social/{SendFriendRequestRequest,FriendshipListResponse,FriendSummary,PendingFriendship,UserSummary}.cs`
- `server/ChatApp.Api/Program.cs` (Social DI block)
- `client/src/app/core/social/{friendships.service.ts,friendships.models.ts}`
- `client/src/app/features/contacts/contacts.component.{ts,html,scss}`
- `client/src/app/app.routes.ts`
- `client/src/app/features/app-shell/app-shell.component.html`
