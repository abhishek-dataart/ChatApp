Slice 7 — Rooms basics

Introduces the Rooms bounded context. Authenticated users can create a room (public or private), browse the public catalog with substring search, open a room they're a member of, see the member list, join a public room, and leave a room they're in. Sets up `RoomService` and `RoomPermissionService` so slice 8 (room messaging), slice 12 (invitations), and slice 13 (moderation) have a permission surface to call into without rewiring. No messaging, no invitations, no moderation, no capacity banner — all deferred by plan.

## Context

`docs/implementation-plan.md` slice 7; depends on slice 1 (Identity — gives `User`, `ICurrentUser`, `[Authorize]`, ProblemDetails `Problem(code, message, status)` helper, `UserSummary` contract shape from slice 3). None of slices 2–6 are load-bearing for this slice; in particular presence (slice 6) is independent — member presence dots land later when `PresenceHub` starts fanning out to `room:{id}` groups.

Authoritative requirements that fix this slice's shape:

- **Product spec §2** — `Room(id, name unique CI permanent-reserved, description, visibility=public|private, owner_id, capacity default 1000, created_at, deleted_at?)`, `RoomMember(room_id, user_id, role=owner|admin|member, joined_at)`.
- **Product spec §6.1** — any authenticated user can create a room. Required: `name` (unique CI permanent), `description`, `visibility`. Default `capacity` = 1000.
- **Product spec §6.2** — owner is permanent; cannot lose admin; only role that may delete the room or raise capacity. Owner cannot leave; may only delete (slice 13). No ownership transfer.
- **Product spec §6.3** — public rooms: any non-banned authenticated user joins freely. Private rooms: join only via accepted invitation. Leave: free for any non-owner. Capacity: server rejects join/accept-invite if `member_count >= capacity`.
- **Product spec §6.7** — public catalog lists name, description, member count; case-insensitive substring search on name only.
- **Product spec §6.9** — member list visible only to current members (both public and private rooms). Public catalog shows count, not names.
- **Arch doc §Bounded contexts — Rooms** — owns `rooms`, `room_members`, plus later `room_bans`, `room_invitations`, `moderation_audit`. Authoritative source for permission checks on room-scoped messaging (slice 8 will call `RoomPermissionService`).
- **Arch doc §Data ownership** — `rooms`, `room_members` belong to Rooms. Single `ChatDbContext`, one migrations history. Cross-context reads through services only.

Outcome: user A opens `/app/rooms`, clicks **Create**, submits `{ name: "general", description: "team chat", visibility: "public" }`; A is auto-joined as owner. User B opens `/app/rooms`, sees "general" in the catalog with member count 1, clicks **Join** → now a member. Both open `/app/rooms/{id}`; both see the header, the two-row member list (A as owner, B as member), and a **Leave** button (disabled for A with an "owners cannot leave" tooltip). B clicks Leave → redirected back to the catalog, member count drops to 1. A's create of a second room named "General" (different case) is rejected with 409 `room_name_taken`.

## Decisions

Interview answers folded in; *[decided]* flags items that closed a genuinely open option.

| Topic | Decision | Rationale |
|---|---|---|
| List shape | **Two endpoints.** `GET /api/rooms?q=` (public catalog with `memberCount` and `isMember`); `GET /api/rooms/mine` (rooms the caller is in, all visibilities, with `role` and `joinedAt`) | *[decided]* — catalog and my-rooms have different authz shape (catalog shows only counts per §6.9; my-rooms returns role). Splitting keeps each response tight and mirrors the slice-3 `GET /api/friendships` grouping idea without overloading a single endpoint. |
| Private rooms | **Creation allowed; join path deferred.** `POST /api/rooms` accepts `visibility=private`. Creator auto-joins as owner. Private rooms are excluded from the `GET /api/rooms` catalog. `POST /api/rooms/{id}/join` on a private room returns 403 `room_is_private` until slice 12 wires invitation acceptance | *[decided]* — schema-exercises both visibilities end-to-end now so slice 12 adds invitations without migration churn. TODO marker (`// TODO(slice-12): invitation-based join`) left at the rejection branch. |
| Catalog search | **Included.** `GET /api/rooms?q=foo` → case-insensitive substring on `name` (PostgreSQL `ILIKE '%foo%'`). Empty/missing `q` returns the full catalog | *[decided]* — cheap at 300-user envelope, unblocks the arch-doc E2E smoke which exercises catalog search. Trims leading/trailing whitespace; rejects `q.Length > 50` with 400 `search_too_long` as a cheap DoS guard. |
| Room detail shell | **Header + member list + Leave button.** Route `/app/rooms/{id}` renders name, description, visibility pill, member list (display name + avatar + role badge), Leave button (disabled for owner with tooltip). No composer, no message pane | *[decided]* — matches the acceptance criterion "both see member list" and gives slice 8 a clear seam to drop in the messages pane + composer without rebuilding the shell. |
| Endpoint shape | **Action sub-routes.** `POST /api/rooms`, `GET /api/rooms`, `GET /api/rooms/mine`, `GET /api/rooms/{id}`, `POST /api/rooms/{id}/join`, `POST /api/rooms/{id}/leave` | *[decided]* — matches `AuthController` and `FriendshipsController` style. Explicit verbs keep authz rules per-endpoint (only the owner-leave guard lives on `/leave`; only non-private gating lives on `/join`). |
| Name uniqueness | **Unique on `NameNormalized` (`name.Trim().ToLowerInvariant()`), stored in a separate non-null column.** Unique index `ux_rooms_name_normalized` survives soft-delete (filter: none) so the name stays reserved after slice 13's `deleted_at` lands | *[decided]* — mirrors the Identity slice's `UsernameNormalized` pattern (proven in slice 1). Permanent reservation is a product-spec §2 hard rule; enforcing it at the index level means slice 13's hard-delete of rooms must preserve a tombstone row (not this slice's problem — noted in Out of scope). |
| Name / description validation | `name`: required, trim, 3–40 chars, regex `^[A-Za-z0-9][A-Za-z0-9 _-]{1,38}[A-Za-z0-9]$` — printable ASCII, no leading/trailing separators. `description`: required, trim, 1–200 chars, stored verbatim | *[decided]* — name regex blocks pathological catalog entries (all-spaces, zero-width joiners) without being culture-hostile. Description intentionally left plain — no markdown, no HTML, rendered as text in slice 7's header. |
| Capacity handling | **Hard cap only.** `capacity` required on create, 2 ≤ capacity ≤ 10 000, default 1000 when absent from the request. Join rejects with 409 `room_full` when `member_count >= capacity`. **95 % banner and owner/admin toast are deferred to slice 13** (moderation slice — where the capacity field becomes editable) | *[decided]* — plan row 7 doesn't mention capacity UX. Minimum of 2 (not 1) so the acceptance demo — "two users join" — isn't blocked by a test-artifact 1-capacity. |
| Owner auto-join | `RoomService.CreateAsync` inserts the `Room` and a `RoomMember { Role = Owner }` inside one EF transaction. Failure rolls both back | Partial commit would leave an orphan room with no owner in the member table, which breaks the permission service's "owner is always a member" invariant. |
| Owner leave guard | `POST /api/rooms/{id}/leave` returns 400 `owner_cannot_leave` when the caller is the owner. Product spec §6.2: "Owner cannot leave; may only delete." Delete lands in slice 13 | Guarded in `RoomService.LeaveAsync` so the rule holds regardless of caller (controller, invitation-accept path in slice 12, or future API). |
| `RoomPermissionService` surface | `GetRoleAsync(roomId, userId) → RoomRole?` (null if not a member), `IsMemberAsync(roomId, userId)`, `IsAdminOrOwnerAsync(roomId, userId)`, `IsOwnerAsync(roomId, userId)`. Reads `room_members` via `ChatDbContext`. No caching in this slice | *[decided]* — slice 8 will call `IsMemberAsync` on every room-scoped message POST. Pure read, no state; add caching in slice 16 only if profiling shows it. Four methods rather than one because each call site wants a different yes/no — no branching on role-tier strings at the call site. |
| `RoomBan` check on join | **Deferred to slice 13.** `JoinAsync` has a `// TODO(slice-13): reject if RoomBan active` marker immediately after the public/private branch | Entity doesn't exist yet; same treatment slice 3 gave `UserBan`. |
| Catalog `isMember` flag | Catalog response includes `isMember: bool` per entry so the client can render **Open** vs **Join** without a second round-trip | *[decided]* — one extra left-join on `room_members` filtered to `user_id = @me`. Alternative (client intersects catalog with `/mine`) is more network-chatty and races on join. |
| Member list payload on detail | `GET /api/rooms/{id}` returns room fields + `members: [{ user: UserSummary, role, joinedAt }]` sorted `role` (owner → admin → member) then `displayName ASC` | Single request for the detail page. At 1000-member cap this is ≤80 KB JSON — acceptable without pagination; spec §10 mentions member-list pagination in the right panel but that's a slice-8+ UX concern. Caller must be a member; non-members get 403 `not_a_member` (no existence leak on private rooms; catalog already tells them public rooms exist). |
| Error codes | `room_name_taken` (409), `room_not_found` (404), `room_is_private` (403), `room_full` (409), `not_a_member` (403), `already_member` (409 on redundant join), `owner_cannot_leave` (400), `invalid_room_name` (400), `invalid_description` (400), `invalid_capacity` (400), `search_too_long` (400) | Short, stable, one code per distinct client action. Mirrors slice 3's `SocialErrors` shape. |
| DI + folder layout | `server/ChatApp.Data/Entities/Rooms/`, `server/ChatApp.Data/Configurations/Rooms/`, `server/ChatApp.Domain/Services/Rooms/`, `server/ChatApp.Api/Controllers/Rooms/` (folder exists, empty), `server/ChatApp.Api/Contracts/Rooms/`. Both services `AddScoped` in `Program.cs` under a new `// Rooms` block | Matches slice 3's discipline; `Controllers/Rooms/` is already reserved in the tree. |
| Error-result pattern | Services return `(bool Ok, string? Code, string? Message, T? Value)` tuples | Same shape as `AuthService` / `FriendshipService` — zero new plumbing. |

### Deferred (explicit — handed to later slices)

- Room messaging (`POST /api/chats/room/{id}/messages`, `ChatHub` broadcast to `room:{id}`) — slice 8.
- Unread markers and badges on the rooms list — slice 9.
- Image/file attachments in rooms — slice 11.
- Invitations (including the private-room join path) — slice 12.
- Moderation: kick, ban, unban, ModerationAudit, 95% capacity banner, capacity editing, room delete — slice 13.
- `RoomBan` consultation on join (`// TODO(slice-13)` marker left in `JoinAsync`) — slice 13.
- Member list pagination and the right-panel "Invite user" button — slice 12 UX.
- Presence dots on member list (fan-out to `room:{id}` from `PresenceHub`) — follow-up to slice 6.
- Catalog pagination — not needed at 300-user envelope; revisit if catalogs grow past a few hundred rooms.
- Real-time `RoomMemberChanged` event on join/leave (so the member list on other tabs updates without refetch) — slice 13 introduces the event; this slice refetches on demand.
- Rate limit on room creation — slice 16.

## Scope

### Server — files to create

| Path | Purpose |
|------|---------|
| `server/ChatApp.Data/Entities/Rooms/Room.cs` | `{ Guid Id, string Name, string NameNormalized, string Description, RoomVisibility Visibility, Guid OwnerId, int Capacity, DateTimeOffset CreatedAt, DateTimeOffset? DeletedAt }`. `DeletedAt` is declared now so slice 13 doesn't need a schema migration for soft-delete; unused in this slice. |
| `server/ChatApp.Data/Entities/Rooms/RoomVisibility.cs` | `enum RoomVisibility { Public = 0, Private = 1 }`. |
| `server/ChatApp.Data/Entities/Rooms/RoomMember.cs` | `{ Guid RoomId, Guid UserId, RoomRole Role, DateTimeOffset JoinedAt }`. Composite PK `(RoomId, UserId)`. |
| `server/ChatApp.Data/Entities/Rooms/RoomRole.cs` | `enum RoomRole { Member = 0, Admin = 1, Owner = 2 }`. Order deliberately ascending so `role DESC` sorts Owner-first in SQL without a case expression. |
| `server/ChatApp.Data/Configurations/Rooms/RoomConfiguration.cs` | `ToTable("rooms")`; PK on `Id`; unique index on `NameNormalized` named `ux_rooms_name_normalized` (no filter — reservation is permanent per product spec §2); non-unique index on `OwnerId`; non-unique index on `Visibility` to keep the public-catalog scan cheap. Enum stored as `int`. `Name` max 40, `NameNormalized` max 40, `Description` max 200. |
| `server/ChatApp.Data/Configurations/Rooms/RoomMemberConfiguration.cs` | `ToTable("room_members")`; composite PK `(RoomId, UserId)`; non-unique index on `UserId` named `ix_room_members_user_id` for the `/api/rooms/mine` query. Enum stored as `int`. FK `RoomId → rooms.id` cascade; FK `UserId → users.id` restrict (user soft-delete is handled by Identity service, not the FK). |
| `server/ChatApp.Data/Migrations/{timestamp}_AddRooms.cs` | Generated via `dotnet ef migrations add AddRooms --project server/ChatApp.Data --startup-project server/ChatApp.Api`. Creates both tables + the three indexes. |
| `server/ChatApp.Domain/Services/Rooms/RoomsErrors.cs` | Static class mirroring `SocialErrors` — error-code constants + default messages for the eleven codes listed in Decisions. |
| `server/ChatApp.Domain/Services/Rooms/RoomService.cs` | `CreateAsync(Guid me, CreateRoomInput input)`, `ListCatalogAsync(Guid me, string? q)`, `ListMineAsync(Guid me)`, `GetAsync(Guid me, Guid roomId)`, `JoinAsync(Guid me, Guid roomId)`, `LeaveAsync(Guid me, Guid roomId)`. Create runs in a transaction (room + owner RoomMember). Join uses `INSERT ... ON CONFLICT DO NOTHING` semantics via EF (catch unique-violation → `already_member`) and checks capacity **after** insert via a `COUNT` inside the same transaction with `SERIALIZABLE` isolation — rollback if overshot (prevents the classic capacity race with N concurrent joiners). |
| `server/ChatApp.Domain/Services/Rooms/RoomPermissionService.cs` | `GetRoleAsync`, `IsMemberAsync`, `IsAdminOrOwnerAsync`, `IsOwnerAsync`. Each is a single `SELECT role FROM room_members WHERE room_id = @r AND user_id = @u` (composite PK lookup — index hit). |
| `server/ChatApp.Api/Controllers/Rooms/RoomsController.cs` | `[Authorize]` class. Endpoints: `POST /api/rooms`, `GET /api/rooms`, `GET /api/rooms/mine`, `GET /api/rooms/{id:guid}`, `POST /api/rooms/{id:guid}/join`, `POST /api/rooms/{id:guid}/leave`. All call into `RoomService`; map service error codes → ProblemDetails statuses using the shared `Problem` helper. |
| `server/ChatApp.Api/Contracts/Rooms/CreateRoomRequest.cs` | `{ string Name, string Description, string Visibility, int? Capacity }`. `Visibility` accepted as `"public"` / `"private"` (case-insensitive) and parsed to the enum in the controller; unknown value → 400 `invalid_room_name` (reused — visibility is part of the create payload validation). |
| `server/ChatApp.Api/Contracts/Rooms/RoomSummary.cs` | `{ Guid Id, string Name, string Description, string Visibility, int MemberCount, int Capacity, DateTimeOffset CreatedAt }`. Used by catalog and (with extras) detail. |
| `server/ChatApp.Api/Contracts/Rooms/CatalogEntry.cs` | `RoomSummary` + `{ bool IsMember }`. Catalog-only. |
| `server/ChatApp.Api/Contracts/Rooms/MyRoomEntry.cs` | `RoomSummary` + `{ string Role, DateTimeOffset JoinedAt }`. |
| `server/ChatApp.Api/Contracts/Rooms/RoomDetailResponse.cs` | `RoomSummary` + `{ UserSummary Owner, List<RoomMemberEntry> Members, string? CurrentUserRole }`. `CurrentUserRole` is set when the caller is a member; the 403 branch runs before this is built so it's never null in a 200 response. |
| `server/ChatApp.Api/Contracts/Rooms/RoomMemberEntry.cs` | `{ UserSummary User, string Role, DateTimeOffset JoinedAt }`. `UserSummary` reused from `Contracts/Social/UserSummary.cs` — slice 3 already ships it and it fits verbatim; add a `using ChatApp.Api.Contracts.Social;` rather than duplicating the type. |

### Server — files to modify

| Path | Change |
|------|--------|
| `server/ChatApp.Data/ChatDbContext.cs` | Add `public DbSet<Room> Rooms => Set<Room>();` and `public DbSet<RoomMember> RoomMembers => Set<RoomMember>();`. `ApplyConfigurationsFromAssembly` already picks up the new configs. |
| `server/ChatApp.Api/Program.cs` | Under a new `// Rooms` block: `builder.Services.AddScoped<RoomService>();` and `builder.Services.AddScoped<RoomPermissionService>();`. No other wiring. |

### Client — files to create

| Path | Purpose |
|------|---------|
| `client/src/app/core/rooms/rooms.models.ts` | TS mirrors of `CatalogEntry`, `MyRoomEntry`, `RoomDetailResponse`, `RoomMemberEntry`, `CreateRoomRequest`, plus a `RoomVisibility` string union. |
| `client/src/app/core/rooms/rooms.service.ts` | Signals wrapper: `catalog = signal<CatalogEntry[] | null>(null)`, `mine = signal<MyRoomEntry[] | null>(null)`; methods `refreshCatalog(q?: string)`, `refreshMine()`, `create(input)`, `get(id)`, `join(id)`, `leave(id)`. On success, `create`/`join`/`leave` refresh both signals. `get` is a one-shot fetch (no caching — member list must be fresh). |
| `client/src/app/features/rooms/rooms-list/rooms-list.component.{ts,html,scss}` | Page at `/app/rooms`. Two panels: **My rooms** (from `mine` signal, each entry is a link to `/app/rooms/{id}` with role + member count) and **Catalog** (search input bound to `q` with a 200 ms debounce, then `refreshCatalog(q)`; each entry shows name, description, member count / capacity; **Open** for `isMember`, **Join** otherwise). Top-right **Create room** button opens the create dialog. |
| `client/src/app/features/rooms/create-room-dialog/create-room-dialog.component.{ts,html,scss}` | Standalone dialog component (opened via a signal-based `isOpen()` on the list component — no Angular Material dependency). Form: `name`, `description`, `visibility` (radio: public / private), `capacity` (optional number, placeholder "1000"). Submits via `RoomsService.create`; on success closes and navigates to `/app/rooms/{newId}`. Renders per-field errors from ProblemDetails `code`. |
| `client/src/app/features/rooms/room-detail/room-detail.component.{ts,html,scss}` | Page at `/app/rooms/:id`. Reads `:id` param; calls `RoomsService.get(id)` on init. Renders header (name, visibility pill, description), owner chip, member list table (avatar + display name + role badge, owner-first), **Leave** button (disabled if `currentUserRole === 'owner'`, tooltip "owners cannot leave — delete the room instead"). Leave calls `RoomsService.leave(id)` then navigates to `/app/rooms`. Not-a-member 403 redirects to `/app/rooms` with a toast. |

### Client — files to modify

| Path | Change |
|------|--------|
| `client/src/app/app.routes.ts` | Under `/app`, add `{ path: 'rooms', loadComponent: ..., canActivate: [authGuard] }` and `{ path: 'rooms/:id', loadComponent: ..., canActivate: [authGuard] }`. |
| `client/src/app/features/app-shell/app-shell.component.html` | Add `<a routerLink="/app/rooms" class="nav-link">Rooms</a>` to the top-bar nav, between `Sessions` and `Contacts` (visual grouping: account-scoped left, chat-scoped right). |

### Out of scope (explicit — handed to later slices)

- `POST /api/chats/room/{id}/messages` and `ChatHub` broadcast to `room:{id}` — slice 8.
- `RoomBan` entity, kick, ban, unban, `ModerationAudit`, room delete, capacity edit, 95 % banner — slice 13.
- `RoomInvitation`, private-room join path — slice 12.
- Unread badges on room list entries — slice 9.
- Right-side panel (Room info / Owner / Admins / Members / Invite / Manage room) — slice 12+ builds this; slice 7 renders a flat member list on the detail page instead.
- Member list pagination — slice 12+.
- `RoomMemberChanged` real-time event — slice 13.
- Rate limit on room creation — slice 16.

## Key flows (reference)

### Create room

1. `POST /api/rooms { name, description, visibility, capacity? }` — `[Authorize]`.
2. Trim `name`, `description`. Validate per the Decisions table; on failure return 400 with the matching error code.
3. Parse `visibility` to the enum; unknown → 400 `invalid_room_name`.
4. `capacity` defaults to 1000 when absent; validate 2 ≤ capacity ≤ 10 000 → 400 `invalid_capacity` otherwise.
5. Compute `nameNormalized = name.Trim().ToLowerInvariant()`. `SELECT 1 FROM rooms WHERE name_normalized = @n` → 409 `room_name_taken` on hit (even on a future soft-deleted row — reservation is permanent).
6. Begin transaction. Insert `Room`. Insert `RoomMember { RoomId = new, UserId = me, Role = Owner, JoinedAt = now }`. Commit.
7. Return 201 with `RoomDetailResponse` (members has one entry — the owner).

### List catalog

1. `GET /api/rooms?q=` — `[Authorize]`.
2. Trim `q`; reject length > 50 with 400 `search_too_long`.
3. Single query: `rooms` filtered to `Visibility = Public AND DeletedAt IS NULL` (the `DeletedAt` check is cheap and future-proofs for slice 13), optional `NameNormalized ILIKE '%' || lower(@q) || '%'`, left-joined to `room_members` grouped by room for `MemberCount`, and a second left-join to `room_members` filtered to `UserId = @me` for `IsMember`.
4. Order by `CreatedAt DESC`. Map to `CatalogEntry[]`. Return 200.

### List my rooms

1. `GET /api/rooms/mine` — `[Authorize]`.
2. `room_members` inner-joined to `rooms` where `UserId = @me AND DeletedAt IS NULL`. Project to `MyRoomEntry`.
3. Order by `Role DESC, Name ASC` (owners/admins first for visual anchoring).

### Get room detail

1. `GET /api/rooms/{id}` — `[Authorize]`.
2. `SELECT` the room. Missing / `DeletedAt != null` → 404 `room_not_found`.
3. `RoomPermissionService.GetRoleAsync(id, me)`. `null` → 403 `not_a_member` (same code for public and private — don't leak visibility to non-members; the catalog already exposes public rooms by description).
4. Project members list (join to `users`). Map to `RoomDetailResponse`. Return 200.

### Join room

1. `POST /api/rooms/{id}/join` — `[Authorize]`.
2. `SELECT` room. Missing / `DeletedAt != null` → 404 `room_not_found`.
3. `Visibility == Private` → 403 `room_is_private`. `// TODO(slice-12): invitation-based join`.
4. `// TODO(slice-13): reject if RoomBan active` — placeholder, currently falls through.
5. Begin `SERIALIZABLE` transaction. `INSERT INTO room_members ...`. Catch unique-violation (composite PK already present) → rollback, return 409 `already_member`.
6. `SELECT COUNT(*) FROM room_members WHERE room_id = @id`. If `> capacity`, rollback and return 409 `room_full`. (Doing the count *after* the insert under SERIALIZABLE is what closes the race against N concurrent joiners; the capacity check on the pre-insert count would permit overshoot under contention.)
7. Commit. Return 200 with `RoomDetailResponse`.

### Leave room

1. `POST /api/rooms/{id}/leave` — `[Authorize]`.
2. `RoomPermissionService.GetRoleAsync(id, me)`. `null` → 404 `room_not_found` (treat non-member leave the same as non-existent — no existence oracle).
3. Role `Owner` → 400 `owner_cannot_leave`.
4. `DELETE FROM room_members WHERE room_id = @id AND user_id = @me`. 204.

## Verification

1. **Build clean.** `dotnet build server/ChatApp.sln` — no warnings.
2. **Migration.** `dotnet ef migrations add AddRooms --project server/ChatApp.Data --startup-project server/ChatApp.Api`. Inspect: creates `rooms` + `room_members` with `ux_rooms_name_normalized` (no filter), `ix_rooms_owner_id`, `ix_rooms_visibility`, and `ix_room_members_user_id`. Commit the migration file and model snapshot. `docker compose -f infra/docker-compose.yml up -d --build` starts clean and applies the migration on boot.
3. **Unit tests** (xUnit, no DB) in `server/ChatApp.Tests/Unit/Rooms/`:
   - `RoomService.CreateAsync` name validation: empty, whitespace-only, 2-char, 41-char, leading-dash all rejected with `invalid_room_name`.
   - Visibility parse: `"Public"` / `"PRIVATE"` accepted (case-insensitive); `"internal"` rejected.
   - Capacity validation: missing → defaults to 1000; `1` → `invalid_capacity`; `10001` → `invalid_capacity`; `1000` accepted.
   - `RoomPermissionService.GetRoleAsync` matrix: non-member → `null`; member / admin / owner → correct enum. (Use an in-memory `DbContextOptions<ChatDbContext>` with a Postgres-shaped schema or Testcontainers if in-memory SQLite drifts — the existing test project sets the convention.)
4. **Integration tests** (Testcontainers Postgres + `WebApplicationFactory`) in `server/ChatApp.Tests/Integration/Rooms/`:
   - **Create + catalog.** A registers, `POST /api/rooms { name: "general", description: "x", visibility: "public" }` → 201. `GET /api/rooms` as B → catalog contains one entry, `memberCount = 1`, `isMember = false`. A: `GET /api/rooms/mine` → one entry, `role = "owner"`.
   - **Duplicate name.** A creates "general"; A (or B) creates "General" (different case) → 409 `room_name_taken`.
   - **Join + detail.** B `POST /api/rooms/{id}/join` → 200. `GET /api/rooms/{id}` as B → `members.length == 2`, `currentUserRole = "member"`. Re-join → 409 `already_member`.
   - **Private rejection.** A creates a private room. B `GET /api/rooms` catalog → does not contain it. B `POST /api/rooms/{id}/join` → 403 `room_is_private`. B `GET /api/rooms/{id}` → 403 `not_a_member`.
   - **Leave.** B leaves the public room → 204. A's `GET /api/rooms/{id}` → `members.length == 1`.
   - **Owner cannot leave.** A `POST /api/rooms/{owned-id}/leave` → 400 `owner_cannot_leave`.
   - **Capacity race.** Create room with `capacity = 2`. Owner is member 1. Kick off 5 concurrent `POST /join` from 5 distinct users. Exactly one succeeds; the other four return 409 `room_full` (or `already_member` for retries). Final `member_count == 2`. This is the test that proves the SERIALIZABLE post-insert count actually closes the race — worth asserting.
   - **Search.** Create rooms "general", "general-eu", "random". `GET /api/rooms?q=gen` → two entries. `GET /api/rooms?q=GEN` → same two (case-insensitive). `q` of 51 chars → 400 `search_too_long`.
   - **Not-a-member detail.** A creates public room; B (not joined) `GET /api/rooms/{id}` → 403 `not_a_member`.
5. **Compose smoke.** Two browsers. A: `/app/rooms` → **Create room** → submit "general" public, capacity 1000 → lands on `/app/rooms/{id}` with A in the member list as owner. B: `/app/rooms` → **general** appears in catalog with count 1 → click **Join** → lands on the detail page → member list shows two entries. A's detail page, after a refresh, shows B too. B clicks **Leave** → routed back to `/app/rooms`, count drops to 1. In A's browser, click **Leave** → button is disabled, tooltip reads "owners cannot leave — delete the room instead".

## Follow-ups for slice 8 (Room messaging)

- `RoomPermissionService.IsMemberAsync` is the authz hook for `POST /api/chats/room/{id}/messages`; slice 8 adds a `chats/room/{id}` route that calls it before insert.
- `ChatHub` group name convention `room:{id}` is fixed by the arch doc; slice 8 wires group join on hub connect for every room the user is a member of (read from `room_members`).
- The detail page's layout is deliberately shaped so slice 8 drops in a messages pane + composer under the header without touching the member list panel.

## Critical files at a glance

- `server/ChatApp.Data/Entities/Rooms/{Room,RoomVisibility,RoomMember,RoomRole}.cs`
- `server/ChatApp.Data/Configurations/Rooms/{RoomConfiguration,RoomMemberConfiguration}.cs`
- `server/ChatApp.Data/Migrations/{timestamp}_AddRooms.cs`
- `server/ChatApp.Data/ChatDbContext.cs` (DbSet additions)
- `server/ChatApp.Domain/Services/Rooms/{RoomService,RoomPermissionService,RoomsErrors}.cs`
- `server/ChatApp.Api/Controllers/Rooms/RoomsController.cs`
- `server/ChatApp.Api/Contracts/Rooms/{CreateRoomRequest,RoomSummary,CatalogEntry,MyRoomEntry,RoomDetailResponse,RoomMemberEntry}.cs`
- `server/ChatApp.Api/Program.cs` (Rooms DI block)
- `client/src/app/core/rooms/{rooms.service.ts,rooms.models.ts}`
- `client/src/app/features/rooms/rooms-list/rooms-list.component.{ts,html,scss}`
- `client/src/app/features/rooms/create-room-dialog/create-room-dialog.component.{ts,html,scss}`
- `client/src/app/features/rooms/room-detail/room-detail.component.{ts,html,scss}`
- `client/src/app/app.routes.ts`
- `client/src/app/features/app-shell/app-shell.component.html`
