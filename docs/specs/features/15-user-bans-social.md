# Slice 14 — User-to-user bans (social)

Closes the Social Graph bounded context's blocking surface. Either user can block another; an active block terminates any existing friendship in the same transaction, prevents new DMs from either side, blocks friend requests, and blocks room invitations in both directions. Existing DM history remains visible to both parties, frozen read-only. The block is reversible by the banner (sets `lifted_at`); unblocking restores messaging capability but does **not** restore the prior friendship. Three pre-placed `// TODO(slice-14)` markers in `FriendshipService` and `InvitationService` are filled. `MessageService.SendAsync` (personal chat path) gains a ban guard that has no pre-existing marker. No SignalR event is emitted on ban/unban — the DM composer discovers the frozen state via a ban-status endpoint on page load, and a failed-send 403 reinforces it inline.

## Context

`docs/implementation-plan.md` slice 14; depends on slice 3 (Friends + personal chats — gives `Friendship`, `PersonalChat`, `FriendshipService`), slice 5 (DM messaging — gives `MessageService.SendAsync`, `PersonalMessagesController`), and slice 12 (Room invitations — gives `InvitationService` with two `// TODO(slice-14)` markers).

Authoritative requirements that fix this slice's shape:

- **Product spec §2** — `UserBan(banner_id, banned_id, created_at, lifted_at?)` — reversible; active when `lifted_at` is null.
- **Product spec §5** — "Applied by either side. Active ban terminates any existing friendship. While active: no DMs, no friend requests, no room invitations either direction, no ability to send a friend request from either side's UI. Existing DM history remains visible to both, frozen/read-only. Reversible any time by the banner. Unbanning restores messaging capability but does not restore the prior friendship."
- **Product spec §6.8** — "Invitation is blocked if either side has an active `UserBan` against the other."
- **Product spec §7.2** — "A DM is sendable only if friendship is `accepted` and neither side has an active `UserBan`."
- **Arch doc §Social Graph** — `UserBanService` is explicitly listed in the Social subgraph. `Active UserBan` is consulted by Messaging on DM writes and by Rooms on invitations.
- **Arch doc §Verification** — integration test: `UserBan → DM blocked; unban does not re-friend`.
- **Implementation-plan row 14** — server scope: `UserBan` entity; block / unblock; consulted by Messaging on DM writes and Rooms on invites; unban does not re-friend. Client scope: Block / unblock from contacts and DM header.

Outcome: A and B are friends with a DM history. A blocks B. A's contacts list no longer shows B. B opens the DM — composer is hidden, banner reads "You cannot message this user." A opens the same DM — composer is hidden, banner reads "You have blocked this user" with an **Unblock** button. B attempts to re-send a friend request to A — gets a 404 (same as user-not-found, not revealing the block). B tries to invite A to a private room — 403. A clicks Unblock — A's DM composer is restored; A must re-send a friend request to B to become friends again; B's view updates on next load (no realtime push). DM history is intact and readable throughout.

## Decisions

Interview answers folded in; *[decided]* flags items that closed a genuinely open option.

| Topic | Decision | Rationale |
|---|---|---|
| Entity schema | `{ Guid Id, Guid BannerId, Guid BannedId, DateTimeOffset CreatedAt, DateTimeOffset? LiftedAt }` — matches spec §2 verbatim. Active = `LiftedAt IS NULL`. | Mirrors the `RoomBan` shape already in the codebase. One consistent pattern across both ban types. |
| Partial unique index | `ux_user_bans_banner_banned_active` on `(BannerId, BannedId)` `WHERE LiftedAt IS NULL`. Re-ban after unban inserts a fresh row (history preserved). | Same discipline as `ux_room_bans_room_user_active`. Closes the concurrent-ban race in the DB, not just in application code. |
| Enforcement direction | Ban is placed by **one** user (`BannerId → BannedId`), but all enforcement checks **both directions**: `WHERE (banner_id = @a AND banned_id = @b) OR (banner_id = @b AND banned_id = @a)`. | Spec §5: "no DMs … either direction". A single row per directional ban; the OR query makes it bilateral without storing two rows. |
| Friendship termination | `BanAsync` deletes the `Friendship` row (if any) in the **same transaction** as the `UserBan` insert. | Spec: "Active ban terminates any existing friendship." Must be atomic — a gap between insert and delete would leave a window where both a friendship and an active ban exist. |
| Unban + friendship | `UnbanAsync` sets `LiftedAt = now`. Does **not** re-create `Friendship`. | Spec: "Unbanning restores messaging capability but does not restore the prior friendship." Re-friending is an explicit user action via the existing friend-request flow. |
| DM history on ban | `GET /api/chats/personal/{chatId}/messages` continues to return history for both participants. `PersonalChat` row is never deleted. New sends are blocked by the `IsActiveAnyDirectionAsync` guard in `MessageService.SendAsync`. | Spec: "Existing DM history remains visible to both, frozen/read-only." Keeping the `PersonalChat` row is the simplest way to honour that. |
| DM sidebar after ban | The DM entry remains in the sidebar list (the `PersonalChat` row survives). The composer is replaced by a frozen banner. | Hiding the DM entirely would prevent viewing the history that the spec explicitly says should be visible. |
| Friend request rejection HTTP code | `FriendshipsController` maps `SocialErrors.UserBanned → 404 NotFound` (same response as `UserNotFound`). | *[decided]* — Does not reveal to the requester whether they are banned or they have banned the other; preserves privacy symmetry. A dedicated 403 would tell B that A exists and has blocked them. |
| Realtime event on ban/unban | **No hub event.** Blocker's UI refreshes its own contacts list after the ban call succeeds (local mutation). Blocked user's DM discovers the state via the ban-status endpoint on load; a failed-send 403 reinforces it inline. | Adding a `UserBanned` push to `user:{id}` would expose "you were blocked" as a real-time notification, which the spec does not mandate and raises a privacy concern. The REST-only approach matches how `RoomBan` works in slice 13 (room-ban eviction uses a hub event because the banned user is actively inside a room — DM is not an equivalent context). |
| Ban-status endpoint | `GET /api/users/{id}/ban-status` → `{ bannedByMe: bool, bannedByThem: bool }`. Called by the DM page on load. | Client needs explicit state to render the correct frozen-state banner on page load, not only after a failed send attempt. Two booleans let the client show asymmetric copy ("You blocked" vs "You cannot message"). |
| Block list endpoint | `GET /api/users/bans` → list of users the **current user** has actively banned. Does not expose who has banned the current user. | Privacy: users can only observe bans they placed. |
| API surface | `POST /api/users/{id}/ban`, `DELETE /api/users/{id}/ban`, `GET /api/users/bans`, `GET /api/users/{id}/ban-status` — all under `BansController` at route prefix `api/users`. | Consistent with `FriendshipsController` style. Routes sit on the `users` resource because bans are a user-to-user relationship, not scoped to rooms or chats. |
| Error codes | New in `SocialErrors`: `UserBanned` (blocks an action), `CannotBanSelf` (400), `AlreadyBanned` (409), `BanNotFound` (404). New in `MessagingErrors`: `UserBanned` (403 on DM send). | Follow the existing static-constant pattern in each error class. Separate namespace constants (`SocialErrors.UserBanned` vs `MessagingErrors.UserBanned`) keep controller error-mapping switches unambiguous. |
| `IsActiveAnyDirectionAsync` visibility | Declared `internal` on `UserBanService`; `FriendshipService`, `MessageService`, and `InvitationService` each receive `UserBanService` via constructor injection and call the helper directly. | Three callers; all in `ChatApp.Data`. No need for a domain-layer interface — the service references `ChatDbContext` directly, same as the other Data-layer services. |
| DI layout | `UserBanService` in `server/ChatApp.Data/Services/Social/`. `BansController` in `server/ChatApp.Api/Controllers/Social/`. Registered as `AddScoped<UserBanService>()` under the `// Social` block in `Program.cs`. | Exactly mirrors `FriendshipService` / `FriendshipsController` layout. |
| Block/unblock UX placement | "Block" action on the friend row in `features/contacts` (alongside the existing "Unfriend" button). "Block" / "Unblock" button in the DM header. Both show a confirmation dialog before acting. | Two natural surfaces where the user has context about who they are blocking. |
| DM frozen-state copy | `bannedByMe` → "You have blocked this user. Unblock to resume messaging." + **Unblock** button. `bannedByThem` → "You cannot message this user." (no detail, no Unblock). | Asymmetric phrasing preserves who-blocked-whom privacy for the blocked side. The blocked user never learns definitively that they were blocked (vs. some other restriction). |

### Deferred (explicit)

- **Attachment download restriction on frozen DMs** — banned users can still download attachments from the frozen DM history. Spec says history is "visible", implying accessible; restricting would require augmenting the attachment auth check with a ban query on every download. Deferred to a future polish pass if required.
- **Rate limiting on ban endpoints** — slice 16 (general hardening pass).
- **Unit and integration tests** — slice 17 sweep, except that the ban-matrix unit test (`user_ban_matrix`) is listed explicitly in arch doc §Verification under the unit-test suite; add it to `ChatApp.Tests/Unit/Social/` in this slice (the service logic is trivially testable without a DB).

## Scope

### Server — files to create

| Path | Purpose |
|---|---|
| `server/ChatApp.Data/Entities/Social/UserBan.cs` | `{ Guid Id, Guid BannerId, Guid BannedId, DateTimeOffset CreatedAt, DateTimeOffset? LiftedAt }` |
| `server/ChatApp.Data/Configurations/Social/UserBanConfiguration.cs` | `ToTable("user_bans")`. PK on `Id`. Partial unique index `ux_user_bans_banner_banned_active` on `(BannerId, BannedId)` `WHERE "lifted_at" IS NULL` (Npgsql `HasFilter`). Non-unique index `ix_user_bans_banner_id` on `BannerId` for the my-bans list query. FKs: `BannerId`, `BannedId → users.id` (Restrict — ban history should outlive neither user). |
| `server/ChatApp.Data/Migrations/{timestamp}_AddUserBans.cs` | Generated via `dotnet ef migrations add AddUserBans --project server/ChatApp.Data --startup-project server/ChatApp.Api`. Creates `user_bans` table and both indexes. |
| `server/ChatApp.Data/Services/Social/UserBanService.cs` | Public methods (all `async Task<(bool Ok, string? Code, string? Message, T? Value)>`): `BanAsync(Guid me, Guid targetId, CancellationToken)`, `UnbanAsync(Guid me, Guid targetId, CancellationToken)`, `ListMyBansAsync(Guid me, CancellationToken)`, `GetBanStatusAsync(Guid me, Guid otherId, CancellationToken)`. Internal helper: `IsActiveAnyDirectionAsync(Guid a, Guid b, CancellationToken) → bool`. `BanAsync` runs the self-check, duplicate-check, then opens a transaction: `INSERT user_bans` + `DELETE friendships` (if row exists). `UnbanAsync` does a single `UPDATE … SET lifted_at = now … WHERE … lifted_at IS NULL`, checks rows-affected > 0. |
| `server/ChatApp.Api/Controllers/Social/BansController.cs` | `[ApiController, Route("api/users"), Authorize]`. Four actions: `POST {id:guid}/ban`, `DELETE {id:guid}/ban`, `GET bans`, `GET {id:guid}/ban-status`. Error mapping via local `FromError` switch (pattern from `FriendshipsController`). |
| `server/ChatApp.Api/Contracts/Social/BannedUserEntry.cs` | `record BannedUserEntry(Guid BanId, UserSummary User, DateTimeOffset CreatedAt)` |
| `server/ChatApp.Api/Contracts/Social/BanListResponse.cs` | `record BanListResponse(List<BannedUserEntry> Bans)` |
| `server/ChatApp.Api/Contracts/Social/BanStatusResponse.cs` | `record BanStatusResponse(bool BannedByMe, bool BannedByThem)` |
| `client/src/app/core/social/bans.models.ts` | TS mirrors: `BannedUserEntry`, `BanListResponse`, `BanStatusResponse`. Reuses the existing `UserSummary` interface from `friendships.models.ts`. |
| `client/src/app/core/social/bans.service.ts` | `@Injectable({ providedIn: 'root' })`. Methods: `async block(userId)`, `async unblock(userId)`, `async listMyBans(): Promise<BannedUserEntry[]>`, `async getBanStatus(userId): Promise<BanStatusResponse>`. Signal `myBans = signal<BannedUserEntry[]>([])` populated after `block`/`unblock`/`listMyBans`. |

### Server — files to modify

| Path | Change |
|---|---|
| `server/ChatApp.Data/ChatDbContext.cs` | Add `public DbSet<UserBan> UserBans => Set<UserBan>();` |
| `server/ChatApp.Domain/Services/Social/SocialErrors.cs` | Add `public const string UserBanned = "user_banned";`, `CannotBanSelf = "cannot_ban_self"`, `AlreadyBanned = "already_banned"`, `BanNotFound = "ban_not_found"` |
| `server/ChatApp.Domain/Services/Messaging/MessagingErrors.cs` | Add `public const string UserBanned = "user_banned";` |
| `server/ChatApp.Data/Services/Social/FriendshipService.cs` | In `RequestAsync`, replace `// TODO(slice-14): reject if UserBan is active either direction` with: `if (await _userBans.IsActiveAnyDirectionAsync(requesterId, target.Id, ct)) return (false, SocialErrors.UserBanned, "Cannot send a friend request to this user.", null);`. Add `UserBanService _userBans` to constructor. |
| `server/ChatApp.Data/Services/Messaging/MessageService.cs` | In `SendAsync` (personal chat path), after the participant-check block and before the attachment-validation block: resolve the partner id (`partner = chat.UserAId == me ? chat.UserBId : chat.UserAId`), then `if (await _userBans.IsActiveAnyDirectionAsync(me, partner, ct)) return (false, MessagingErrors.UserBanned, "You cannot message this user.", null);`. Add `UserBanService _userBans` to constructor. |
| `server/ChatApp.Data/Services/Rooms/InvitationService.cs` | **`SendAsync` (line 71):** replace the TODO with `if (await _userBans.IsActiveAnyDirectionAsync(inviterId, inviteeId, ct)) return (false, SocialErrors.UserBanned, "Cannot invite this user.", null);`. **`AcceptAsync` (line 207):** replace the TODO with the same check using the inviter and accepter ids. Add `UserBanService _userBans` to constructor. |
| `server/ChatApp.Api/Controllers/Social/FriendshipsController.cs` | In `FromError`: add `SocialErrors.UserBanned => Problem(statusCode: 404, title: "User not found.", extensions: Ext(code))` — maps to 404 intentionally (see Decisions). |
| `server/ChatApp.Api/Controllers/Messages/PersonalMessagesController.cs` | In `FromError`: add `MessagingErrors.UserBanned => Problem(statusCode: 403, title: "You cannot message this user.", extensions: Ext(code))` |
| `server/ChatApp.Api/Program.cs` | Under the `// Social` services block: `builder.Services.AddScoped<UserBanService>();` |

### Client — files to modify

| Path | Change |
|---|---|
| `client/src/app/features/contacts/contacts.component.ts` | Inject `BansService`. Add `async block(userId: string)` — confirm dialog → `bans.block(userId)` → `friendships.refresh()`. Add `async unblock(userId: string)` (for the unblock-from-contacts case, if a previously-blocked user appears in a "blocked users" section; optional for MVP — see note below). |
| `client/src/app/features/contacts/contacts.component.html` | Add **Block** button (danger/secondary style) alongside the existing **Unfriend** button on each friend row. Show a `confirm()` or inline confirmation before calling `block()`. |
| `client/src/app/features/dms/dm-detail.component.ts` | Inject `BansService`. After resolving `partnerId` from the route/chat, call `await bans.getBanStatus(partnerId)` and store result in a `banStatus = signal<BanStatusResponse | null>(null)`. Re-fetch after a successful `unblock()` call from this view. |
| `client/src/app/features/dms/dm-detail.component.html` | Above (or in place of) the message composer: `@if (banStatus()?.bannedByMe) { <div class="ban-banner">You have blocked this user. <button (click)="unblock()">Unblock</button></div> } @else if (banStatus()?.bannedByThem) { <div class="ban-banner">You cannot message this user.</div> }`. Hide the composer (`@if (!banStatus()?.bannedByMe && !banStatus()?.bannedByThem)`) so no new input is possible in either banned state. |

> **MVP note on "blocked users" list:** `GET /api/users/bans` and `BansService.listMyBans()` are wired server- and service-side in this slice. A dedicated "Blocked users" settings page is not required by the implementation plan but the data is available. Unblocking from the DM header covers the primary UX path. A blocked-users tab can be added in a polish pass without server changes.

## Key flows

### Ban a user

1. `POST /api/users/{targetId}/ban` — `[Authorize]`.
2. `targetId == me.Id` → 400 `cannot_ban_self`.
3. `SELECT` user by id (`deleted_at IS NULL`) → 404 if not found.
4. `SELECT 1 FROM user_bans WHERE banner_id = @me AND banned_id = @target AND lifted_at IS NULL` → hit → 409 `already_banned`.
5. Begin transaction.
6. `INSERT INTO user_bans (id, banner_id, banned_id, created_at) VALUES (…, @me, @target, now())`.
7. `DELETE FROM friendships WHERE user_id_low = @low AND user_id_high = @high` (where `low = min(me, target)`, `high = max(me, target)`). Rows-affected = 0 is fine (may not have been friends).
8. Commit.
9. Return 204.

### Unban a user

1. `DELETE /api/users/{targetId}/ban` — `[Authorize]`.
2. `UPDATE user_bans SET lifted_at = now() WHERE banner_id = @me AND banned_id = @target AND lifted_at IS NULL`. Rows-affected = 0 → 404 `ban_not_found`.
3. Return 204. Friendship is **not** restored; the user must re-send a friend request via the existing flow.

### List my bans

1. `GET /api/users/bans` — `[Authorize]`.
2. `SELECT ub.id, u.id, u.username, u.display_name, u.avatar_path, ub.created_at FROM user_bans ub JOIN users u ON ub.banned_id = u.id WHERE ub.banner_id = @me AND ub.lifted_at IS NULL ORDER BY ub.created_at DESC`.
3. Return `BanListResponse`.

### Ban-status check

1. `GET /api/users/{otherId}/ban-status` — `[Authorize]`.
2. Two EXISTS queries (or one with CASE): `bannedByMe = SELECT 1 … WHERE banner_id = @me AND banned_id = @other AND lifted_at IS NULL`; `bannedByThem = SELECT 1 … WHERE banner_id = @other AND banned_id = @me AND lifted_at IS NULL`.
3. Return `BanStatusResponse { bannedByMe, bannedByThem }`.

### Send DM while a ban is active (either direction)

1. `POST /api/chats/personal/{chatId}/messages` — existing flow through `MessageService.SendAsync`.
2. After participant check: `partner = chat.UserAId == me ? chat.UserBId : chat.UserAId`. Call `IsActiveAnyDirectionAsync(me, partner, ct)`.
3. Active ban found → return `(false, MessagingErrors.UserBanned, "You cannot message this user.", null)` → controller → 403.
4. Client receives `{ code: "user_banned" }` → disables composer, shows frozen banner (or refreshes ban-status to get the correct copy).

### Friend request while a ban is active

1. `POST /api/friendships { username }` — resolves to target user.
2. `FriendshipService.RequestAsync` calls `IsActiveAnyDirectionAsync(requesterId, target.Id, ct)` → active → `(false, SocialErrors.UserBanned, …)`.
3. `FriendshipsController.FromError`: `SocialErrors.UserBanned → 404 NotFound`. Caller cannot distinguish "user doesn't exist" from "a ban is in place" — intentional.

### Room invitation while a ban is active

1. `InvitationService.SendAsync` — after the existing `RoomBan` check at line 69.
2. `IsActiveAnyDirectionAsync(inviterId, inviteeId, ct)` → active → `(false, SocialErrors.UserBanned, "Cannot invite this user.", null)` → controller → 403 `user_banned`.
3. Same guard in `AcceptAsync` (inviter id + accepter id).

## Verification

1. **Build clean.** `dotnet build server/ChatApp.sln` — zero warnings (nullable enabled, `TreatWarningsAsErrors`).
2. **Migration.** `dotnet ef migrations add AddUserBans --project server/ChatApp.Data --startup-project server/ChatApp.Api`. Inspect generated SQL: `CREATE TABLE user_bans (…)`, `CREATE UNIQUE INDEX ux_user_bans_banner_banned_active … WHERE lifted_at IS NULL`, `CREATE INDEX ix_user_bans_banner_id …`. Commit migration + snapshot. `docker compose -f infra/docker-compose.yml up -d --build` → api starts clean with migration applied.
3. **Unit tests** (xUnit, no DB) — `server/ChatApp.Tests/Unit/Social/UserBanServiceTests.cs` (mock `ChatDbContext` or use an in-memory store):
   - `BanAsync(me, me)` → `cannot_ban_self`.
   - `BanAsync` with unknown target → `user_not_found`.
   - `BanAsync` when active ban already exists → `already_banned`.
   - `BanAsync` (valid) → ok; confirm friendship row deleted atomically.
   - `UnbanAsync` with no active ban → `ban_not_found`.
   - `UnbanAsync` (valid) → ok; `LiftedAt` set; friendship NOT restored.
   - `FriendshipService.RequestAsync` with active ban in either direction → `user_banned`.
4. **Integration tests** (Testcontainers Postgres + `WebApplicationFactory`) — `server/ChatApp.Tests/Integration/Social/UserBanIntegrationTests.cs`:
   - A blocks B (friends) → A's `GET /api/friendships` no longer includes B; friendship row gone.
   - B's `POST /api/chats/personal/{chat}/messages` → 403 `user_banned`.
   - A's `POST /api/chats/personal/{chat}/messages` → 403 `user_banned`.
   - Both sides: `GET /api/chats/personal/{chat}/messages` (history) → 200, history intact.
   - B's `POST /api/friendships { username: "a" }` → 404 (mapped from `user_banned`).
   - A's `POST /api/friendships { username: "b" }` → 404 (same mapping).
   - B tries to invite A to a private room → 403 `user_banned`.
   - A unblocks B → both `POST /api/chats/personal/{chat}/messages` → 201 (messaging restored).
   - After unblock: `GET /api/friendships` for A → B not in list (no auto-re-friend).
   - A re-sends friend request to B → `POST /api/friendships { username: "b" }` → 201 (accepted by B → friendship restored).
   - Double-ban attempt → 409 `already_banned`.
   - Unban a non-existent ban → 404 `ban_not_found`.
5. **Compose smoke.** Two browsers (A and B), already friends with DM history.
   - A: contacts page → Block on B's row → confirmation → B disappears from A's contacts.
   - B: opens DM with A → composer hidden, banner "You cannot message this user."
   - A: opens DM with B → banner "You have blocked this user." + **Unblock** button; composer hidden.
   - B: tries to send a message in the DM → (if any composer remains) → 403 toasted.
   - B: tries to invite A to a private room → 403, toast appears.
   - A: clicks **Unblock** → composer restored for A; B's view updates on next load.
   - A: re-sends friend request to B → accepted → both see each other in contacts again.

## Follow-ups for later slices

- **Slice 15 (Pagination + virtual scroll)** — the `GET /api/users/bans` list has no pagination; add keyset pagination in the same pass as message pagination if the blocked-users list grows large (unlikely at 300-user scale).
- **Slice 16 (Hardening)** — add rate limits to `POST /api/users/{id}/ban` and `DELETE /api/users/{id}/ban` per actor.
- **Slice 17 (Test sweep)** — confirm the `user_ban_matrix` unit test and the Testcontainers integration test are present (arch doc §Verification explicitly lists "UserBan → DM blocked; unban does not re-friend").
- **Polish** — "Blocked users" settings page using `GET /api/users/bans`; attachment download restriction on frozen DMs (augment attachment auth); expose `GET /api/users/{id}/ban-status` on DM list entries so the sidebar can pre-render frozen state without an extra round-trip on open.

## Critical files at a glance

- `server/ChatApp.Data/Entities/Social/UserBan.cs` (new)
- `server/ChatApp.Data/Configurations/Social/UserBanConfiguration.cs` (new)
- `server/ChatApp.Data/Migrations/{timestamp}_AddUserBans.cs` (generated)
- `server/ChatApp.Data/ChatDbContext.cs` (add DbSet)
- `server/ChatApp.Data/Services/Social/UserBanService.cs` (new — core of this slice)
- `server/ChatApp.Domain/Services/Social/SocialErrors.cs` (add 4 constants)
- `server/ChatApp.Domain/Services/Messaging/MessagingErrors.cs` (add `UserBanned`)
- `server/ChatApp.Data/Services/Social/FriendshipService.cs` (fill TODO, inject `UserBanService`)
- `server/ChatApp.Data/Services/Messaging/MessageService.cs` (add ban guard in `SendAsync`, inject `UserBanService`)
- `server/ChatApp.Data/Services/Rooms/InvitationService.cs` (fill two TODO markers, inject `UserBanService`)
- `server/ChatApp.Api/Controllers/Social/BansController.cs` (new)
- `server/ChatApp.Api/Contracts/Social/BannedUserEntry.cs`, `BanListResponse.cs`, `BanStatusResponse.cs` (new)
- `server/ChatApp.Api/Controllers/Social/FriendshipsController.cs` (add `UserBanned → 404` mapping)
- `server/ChatApp.Api/Controllers/Messages/PersonalMessagesController.cs` (add `UserBanned → 403` mapping)
- `server/ChatApp.Api/Program.cs` (register `UserBanService`)
- `client/src/app/core/social/bans.models.ts` (new)
- `client/src/app/core/social/bans.service.ts` (new)
- `client/src/app/features/contacts/contacts.component.{ts,html}` (block action)
- `client/src/app/features/dms/dm-detail.component.{ts,html}` (frozen-state banner + unblock)
