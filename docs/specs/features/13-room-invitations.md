# Slice 12 — Room invitations

Introduces the invitation surface to the Rooms bounded context. Admins and owners can invite another user to a room they manage by username (both public and private rooms). The invitee sees pending invitations in their `/app/contacts` page and can accept (auto-joins the room) or decline. The inviter can revoke a pending invitation before it's accepted. For **private** rooms, this slice also finally opens the join path that slice 7 deliberately left behind a `403 room_is_private` TODO — accepting an invitation is now the only way in. No UserBan check yet (slice 14), no `RoomMemberChanged` realtime event (slice 13), no hub push of invitation state changes (invitee refetches the inbox after mutate — matches slice 7's discipline).

## Context

`docs/implementation-plan.md` slice 12; depends on slice 3 (Social — gives `UserSummary`, `/api/profile/avatar/{userId}`, the `features/contacts` page as the host for the invitations inbox) and slice 7 (Rooms — gives `Room`, `RoomMember`, `RoomRole`, `RoomPermissionService`, `RoomService.JoinAsync`'s capacity guard and the `// TODO(slice-12): invitation-based join` seam at the private-room branch).

Authoritative requirements that fix this slice's shape:

- **Product spec §2** — `RoomInvitation(id, room_id, invitee_id, inviter_id, state = pending|accepted|declined, created_at)`. (This slice narrows the stored lifecycle to "pending only" — see Decisions.)
- **Product spec §6.3** — "Private rooms: join only via accepted invitation." "Server rejects join/accept-invite if `member_count >= capacity`."
- **Product spec §6.8** — Direct-to-username only; no invite links. Inviter must be admin or owner. Target sees it in an "Invitations" inbox and must accept or decline. Invitation is blocked if either side has an active `UserBan`, or if the target is in the room's `RoomBan`.
- **Product spec §10** — `manage-room` right-panel has an "Invite user" affordance; this slice places that affordance on the room detail page and leaves `features/manage-room/` empty for slice 13.
- **Arch doc §Bounded contexts — Rooms** — Rooms owns `room_invitations` (plus the later `room_bans` / `moderation_audit`). `InvitationService` is explicitly listed in the Rooms subgraph.
- **Arch doc §Data ownership** — `room_invitations` belongs to Rooms. Single `ChatDbContext`, one migrations history.
- **Implementation-plan row 12** — "`RoomInvitation` entity; `InvitationService` (send / accept / decline / revoke); blocked by active `UserBan` (consulted from Social)." Client scope: `features/contacts` invitations inbox + "invite" button in `manage-room`. Demo: "A invites B to a private room; B accepts → joined; revoking before accept removes it."

Outcome: user A (owner of a private room "engineers") opens `/app/rooms/{id}`, clicks **Invite**, enters "b", submits — B's `/app/contacts` shows one incoming room invitation with the room name/description and A's avatar. B clicks **Accept** → B is now in the room's member list and lands on `/app/rooms/{id}` as a member. Alternative path: A sends the invite, then clicks **Revoke** on the outgoing list next to B's name before B acts — B's inbox no longer shows the invite. Alternative path: B clicks **Decline** → A's outgoing list loses the row; A may re-send later. Demo-aligned.

## Decisions

Interview answers folded in; *[decided]* flags items that closed a genuinely open option.

| Topic | Decision | Rationale |
|---|---|---|
| Invited room visibility | **Both public and private.** Admin/owner may invite to either. For public rooms the invitation is a "nudge" — invitee can still self-join via catalog without accepting. Accept path still checks capacity and duplicate-member the same way | *[decided]* — spec §6.8 doesn't restrict by visibility; only §6.3 restricts the *join* path. Allowing admin invites to public rooms is consistent with the spec and avoids a special-case in `SendAsync`. The only visibility-gated surface is `RoomService.JoinAsync`, which still returns 403 `room_is_private` for direct joins; private-room access only opens through `AcceptAsync`. |
| Invitation lifecycle | **Pending-only row; hard-delete on decline, accept, revoke.** No state column, no `accepted_at`. Accept performs `DELETE invitation + INSERT room_member` atomically; decline and revoke are `DELETE` | *[decided]* — mirrors slice 3's `Friendship` decline/unfriend path, which has been proved in integration. An accepted invitation carries no post-accept information the client needs (the row's "value" is the resulting `RoomMember`). Deferring spec §2's `state` column avoids a dead enum. A later audit requirement (slice 13's `ModerationAudit`) would track accept/decline events there if ever needed — not in `room_invitations`. |
| Re-invite after decline | **Allowed immediately.** Decline deletes the row; a fresh `POST` is permitted. Same door slice 3 left open for re-sending a friend request after decline | *[decided]* — harassment mitigation is a `UserBan` concern (slice 14), not an invitation-layer gate. Matches slice 3 explicitly. |
| Endpoint shape | **Split send vs inbox vs action.** `POST /api/rooms/{roomId}/invitations` (send), `GET /api/rooms/{roomId}/invitations` (outgoing for a room — admin/owner only), `GET /api/invitations` (inbox — caller's incoming), `POST /api/invitations/{id}/accept`, `POST /api/invitations/{id}/decline`, `DELETE /api/invitations/{id}` (polymorphic revoke/decline) | *[decided]* — `POST /rooms/{roomId}/invitations` groups creation under the room it belongs to (mirrors slice 7's `/rooms/{id}/join`). Reading inbox is caller-scoped, so `GET /api/invitations` — no room id in the URL — matches slice 3's `GET /api/friendships` shape. Action sub-routes on `/api/invitations/{id}/...` mirror `FriendshipsController`. Polymorphic `DELETE` (revoke if inviter, decline if invitee) mirrors slice 3's `DELETE /api/friendships/{id}`. |
| Inbox UI placement | **Section on `/app/contacts`.** Add an "Incoming room invitations" panel between "Incoming requests" and "Friends" | *[decided]* — implementation-plan row 12 literally says `features/contacts invitations inbox`. Invitations and friend requests are the two inbound social queues; keeping them on one page is less navigation and matches the wireframe hinted at in spec §10 (Contacts is the social-state hub). |
| Invite-button placement | **Room-detail page, admin/owner only.** Adds an **Invite** button in the room-actions row next to **Leave**; opens a dialog (username + optional note, ≤200 chars). Also renders a collapsible "Pending invitations" list beneath the member list, again admin/owner only, with **Revoke** buttons | *[decided]* — `features/manage-room/` stays empty until slice 13 reshapes it into a full admin shell (Members / Bans / Audit / Settings tabs). Pulling a manage-room scaffold forward just for invitations wastes the work slice 13 will redo. The dialog/list live on `room-detail` for the slice; slice 13 can later lift them into the manage-room shell unchanged (both are self-contained components). |
| Inviter authz | Caller must be `Admin` or `Owner` of the room — `RoomPermissionService.IsAdminOrOwnerAsync`. Members and non-members → 403 `not_admin_or_owner` | Matches spec §6.8 "Inviter must be admin or owner." Existing permission helper; zero new plumbing. |
| Dup-invite uniqueness | Unique index on `(RoomId, InviteeId)` named `ux_room_invitations_room_invitee`. A second send while a pending row exists → 409 `invitation_exists`. Index has no filter — a row only exists while pending, so no historical rows to sidestep | *[decided]* — one pending invite per pair is the natural rule once the row means "pending". The unique index makes the controller's check race-free (catch unique-violation on insert). |
| Invitee validity | Target must exist (`DeletedAt is null`); `InviteeId != InviterId` (400 `cannot_invite_self`); invitee must not already be a `RoomMember` (409 `already_member`). RoomBan check is **deferred to slice 13**; UserBan check is **deferred to slice 14** | Soft-deleted users yield 404 `user_not_found` (same code slice 3 uses). Self-invite is a cheap 400 at the top of `SendAsync`. Already-member is a pre-check against `room_members` — saves the invitee from an embarrassing "accept" that then 409s. |
| Optional note on invite | `note?` on `POST /api/rooms/{id}/invitations` — trimmed, coerced-empty → `null`, max 200 chars (400 `note_too_long` beyond that). Shown on the invitee's inbox entry. Not shown once accepted (the row is gone) | Mirrors slice 3's note handling but with a tighter cap (invitations are operational, not introductions — 500 chars would be gratuitous). |
| Accept flow | **`AcceptAsync` performs the join directly** under a `SERIALIZABLE` transaction: re-read invite, re-check invitee `== me`, re-check capacity, `INSERT room_member`, `DELETE room_invitation`, commit. Does **not** call `RoomService.JoinAsync` — `JoinAsync` would reject private rooms with `room_is_private`. Capacity race is closed the same way `JoinAsync` does it: post-insert `COUNT`, rollback on overshoot | *[decided]* — duplicating ~15 lines is cheaper than either (a) adding a `bypassVisibility` flag to `JoinAsync` (leaky), or (b) refactoring `JoinAsync` into a `JoinMemberCoreAsync` helper that both call (could be done but risks touching a proved slice-7 code path for no functional gain). Slice 13 can extract the helper when room-ban adds a third call site. |
| Decline flow | `DELETE FROM room_invitations WHERE id = @id AND invitee_id = @me`. Rows-affected = 0 → 404 `invitation_not_found` | Same hard-delete the friendship `decline` does. No side effects. |
| Revoke flow | `DELETE FROM room_invitations WHERE id = @id AND inviter_id = @me`. Rows-affected = 0 → 404 `invitation_not_found` | Same SQL shape as decline; differing WHERE clause keeps the authz check at the row level. |
| Polymorphic DELETE | `DELETE /api/invitations/{id}` → the row must exist and the caller must be either `inviter_id` or `invitee_id`, otherwise 404. Service method `RevokeOrDeclineAsync` handles both with a single SQL `DELETE` filtered to `(inviter_id = @me OR invitee_id = @me)` | Mirrors `FriendshipService.UnfriendOrCancelAsync`. Keeps client call sites readable (`revoke(id)` vs `decline(id)`) while the server does one thing. |
| `JoinAsync` private-room branch | **Leave as-is.** Private-room direct-join still 403s with `room_is_private`. The TODO marker is removed and replaced with a short comment ("Private rooms: join via `InvitationService.AcceptAsync`.") | *[decided]* — the accept path is the single entry to private rooms; `JoinAsync` does not need to route through it. The old TODO is now resolved by the existence of `AcceptAsync`. |
| Inbox response | `GET /api/invitations` → `{ incoming: List<InvitationEntry> }` ordered `CreatedAt DESC`. Each entry: `{ invitationId, room: RoomSummary, inviter: UserSummary, note?, createdAt }` | Single endpoint, single payload. Separate `outgoing` list for a room is a different query (admin-scoped), so it's a different endpoint. |
| Outgoing (per-room) response | `GET /api/rooms/{roomId}/invitations` (admin/owner only) → `{ invitations: List<OutgoingInvitationEntry> }` ordered `CreatedAt DESC`. Each entry: `{ invitationId, invitee: UserSummary, inviter: UserSummary, note?, createdAt }` | Admin/owner needs to see who's been invited and who did the inviting (for the audit feel that slice 13 formalises). 403 `not_admin_or_owner` for non-admins. |
| Realtime event | **Deferred.** No `RoomInvitationChanged` push in this slice. Invitee's inbox updates by refetch after mutate (consistent with slice 7's discipline on `RoomMemberChanged`) | *[decided]* — implementation-plan row 12 doesn't list any realtime event. Slice 13 introduces `RoomMemberChanged` and can bolt an invitation-changed ping on at the same time. Tolerable UX gap: an invitee sitting on `/app/contacts` when a new invite arrives doesn't see it until they navigate away and back. |
| Error codes | `invitation_not_found` (404), `invitation_exists` (409), `cannot_invite_self` (400), `not_admin_or_owner` (403), `already_member` (409), `note_too_long` (400). Reuses from slice 7: `room_not_found` (404), `room_full` (409), `user_not_found` (404) | Stable, short, one code per distinct client action. Extends the existing `RoomsErrors` static class. |
| DI + folder layout | `server/ChatApp.Data/Entities/Rooms/RoomInvitation.cs`, `server/ChatApp.Data/Configurations/Rooms/RoomInvitationConfiguration.cs`, `server/ChatApp.Domain/Services/Rooms/InvitationService.cs`, `server/ChatApp.Api/Controllers/Rooms/InvitationsController.cs`, `server/ChatApp.Api/Contracts/Rooms/*` extended. `AddScoped<InvitationService>()` in `Program.cs` under the existing `// Rooms` block | Matches slice 7's discipline; no new folders. |
| Error-result pattern | `InvitationService` methods return `(bool Ok, string? Code, string? Message, T? Value)` tuples | Same shape the rest of the codebase uses. |

### Deferred (explicit — handed to later slices)

- `UserBan` check on send / accept (slice 14) — entity doesn't exist. A `// TODO(slice-14): reject if UserBan is active either direction` marker goes in `InvitationService.SendAsync` right after the invitee lookup.
- `RoomBan` check on send / accept (slice 13) — entity doesn't exist. A `// TODO(slice-13): reject if RoomBan active for invitee on this room` marker goes in `SendAsync` and a mirrored one in `AcceptAsync` (the banned user could be holding a pending invite from before the ban).
- Realtime `RoomInvitationChanged` event (fan-out to invitee and inviter user groups) — slice 13 (when `ChatHub` starts broadcasting room-member state changes).
- `manage-room` admin shell (tabs for Members / Bans / Audit / Settings) — slice 13. The invite dialog and pending-invites list live on `room-detail` in this slice and migrate cleanly when slice 13 ships.
- Invitation rate limit — slice 16.
- "Invite from member list" entry point on existing member rows (useful if a member has left and an admin wants to re-invite them) — slice 13 rolls member-list affordances into `manage-room`.
- Invite by clicking a friend in `/app/contacts` (context-menu "Invite to room → choose room") — nice UX, but requires rooms-the-inviter-admins-list-of, which doesn't exist yet as an endpoint. Defer.

## Scope

### Server — files to create

| Path | Purpose |
|------|---------|
| `server/ChatApp.Data/Entities/Rooms/RoomInvitation.cs` | `{ Guid Id, Guid RoomId, Guid InviterId, Guid InviteeId, string? Note, DateTimeOffset CreatedAt }`. No state enum — pending is implied by row existence. |
| `server/ChatApp.Data/Configurations/Rooms/RoomInvitationConfiguration.cs` | `ToTable("room_invitations")`; PK on `Id`; **unique** index on `(RoomId, InviteeId)` named `ux_room_invitations_room_invitee` (no filter); non-unique index on `InviteeId` named `ix_room_invitations_invitee_id` for the inbox query; non-unique index on `InviterId` named `ix_room_invitations_inviter_id` for a per-room outgoing query that filters on inviter (useful for slice 13 audit). `Note` max 200. FK `RoomId → rooms.id` cascade (room delete wipes invitations); FKs `InviterId`, `InviteeId → users.id` restrict (user soft-delete doesn't cascade — spec §3 says pending invitations are removed on account delete, but that's an Identity-side cleanup, not an FK cascade). |
| `server/ChatApp.Data/Migrations/{timestamp}_AddRoomInvitations.cs` | Generated via `dotnet ef migrations add AddRoomInvitations --project server/ChatApp.Data --startup-project server/ChatApp.Api`. Creates the table + three indexes. |
| `server/ChatApp.Domain/Services/Rooms/InvitationService.cs` | `SendAsync(Guid me, Guid roomId, string inviteeUsername, string? note) → (Ok, Code, Message, OutgoingInvitationEntry?)`, `ListIncomingAsync(Guid me) → List<InvitationEntry>`, `ListOutgoingForRoomAsync(Guid me, Guid roomId) → (Ok, Code, Message, List<OutgoingInvitationEntry>?)`, `AcceptAsync(Guid me, Guid invitationId) → (Ok, Code, Message, RoomDetailResponse?)`, `DeclineAsync(Guid me, Guid invitationId) → (Ok, Code, Message)`, `RevokeAsync(Guid me, Guid invitationId) → (Ok, Code, Message)`, `RevokeOrDeclineAsync(Guid me, Guid invitationId) → (Ok, Code, Message)` (the polymorphic version). `AcceptAsync` runs SERIALIZABLE with post-insert count check (same pattern as `RoomService.JoinAsync`). |

### Server — files to modify

| Path | Change |
|------|--------|
| `server/ChatApp.Data/ChatDbContext.cs` | Add `public DbSet<RoomInvitation> RoomInvitations => Set<RoomInvitation>();`. `ApplyConfigurationsFromAssembly` picks up the new config. |
| `server/ChatApp.Domain/Services/Rooms/RoomsErrors.cs` | Add constants + messages for `InvitationNotFound`, `InvitationExists`, `CannotInviteSelf`, `NotAdminOrOwner`. |
| `server/ChatApp.Domain/Services/Rooms/RoomService.cs` | `JoinAsync`: remove the `// TODO(slice-12): invitation-based join` marker; replace with a one-line comment: `// Private rooms: join via InvitationService.AcceptAsync.` No behaviour change. |
| `server/ChatApp.Api/Controllers/Rooms/InvitationsController.cs` *(new file in existing folder)* | `[Authorize]` class. Endpoints: `POST /api/rooms/{roomId:guid}/invitations`, `GET /api/rooms/{roomId:guid}/invitations`, `GET /api/invitations`, `POST /api/invitations/{id:guid}/accept`, `POST /api/invitations/{id:guid}/decline`, `DELETE /api/invitations/{id:guid}`. Reuses the existing `FromError`/`Ext` helpers already in `RoomsController` — lift them into a `RoomsControllerBase` or a shared private helper (see below). |
| `server/ChatApp.Api/Controllers/Rooms/RoomsController.cs` | Lift the `FromError(string, string?)` + `Ext(string)` private helpers into a small `internal static class RoomsErrorMapper` under `Controllers/Rooms/`; both controllers call into it. Two-line refactor; no behaviour change. |
| `server/ChatApp.Api/Program.cs` | Under the existing `// Rooms` block: `builder.Services.AddScoped<InvitationService>();`. No other wiring — no new middleware, no new SignalR event. |
| `server/ChatApp.Api/Contracts/Rooms/SendInvitationRequest.cs` *(new)* | `{ string Username, string? Note }`. |
| `server/ChatApp.Api/Contracts/Rooms/InvitationEntry.cs` *(new)* | `{ Guid InvitationId, RoomSummary Room, UserSummary Inviter, string? Note, DateTimeOffset CreatedAt }`. Invitee's inbox shape. |
| `server/ChatApp.Api/Contracts/Rooms/OutgoingInvitationEntry.cs` *(new)* | `{ Guid InvitationId, UserSummary Invitee, UserSummary Inviter, string? Note, DateTimeOffset CreatedAt }`. Per-room outgoing shape (admin/owner view). |
| `server/ChatApp.Api/Contracts/Rooms/IncomingInvitationsResponse.cs` *(new)* | `{ List<InvitationEntry> Incoming }`. |
| `server/ChatApp.Api/Contracts/Rooms/RoomInvitationsResponse.cs` *(new)* | `{ List<OutgoingInvitationEntry> Invitations }`. |

### Client — files to create

| Path | Purpose |
|------|---------|
| `client/src/app/core/rooms/invitations.models.ts` | TS mirrors of `InvitationEntry`, `OutgoingInvitationEntry`, `IncomingInvitationsResponse`, `RoomInvitationsResponse`, `SendInvitationRequest`. |
| `client/src/app/core/rooms/invitations.service.ts` | Signals wrapper: `incoming = signal<InvitationEntry[] \| null>(null)`; per-room outgoing kept local to whoever calls it (not global state — it's an admin detail inside one room-detail view). Methods: `refreshIncoming()`, `listOutgoing(roomId)` (one-shot), `send(roomId, username, note?)`, `accept(id)`, `decline(id)`, `revoke(id)`. On success, `accept`/`decline` refresh `incoming`; `send`/`revoke` return the fresh outgoing list for the caller to render; `accept` additionally calls `RoomsService.refreshMine()` so the newly-joined room appears in the sidebar immediately. |
| `client/src/app/features/rooms/invite-user-dialog/invite-user-dialog.component.{ts,html,scss}` | Standalone dialog (same signal-open/close pattern as `create-room-dialog`). Inputs: `roomId`, `roomName` (for title). Form: username, optional note (≤200 chars). Submit calls `InvitationsService.send(roomId, ...)`; on success closes and re-fetches the outgoing list on the parent. Renders per-field errors from ProblemDetails codes. |

### Client — files to modify

| Path | Change |
|------|--------|
| `client/src/app/features/contacts/contacts.component.{ts,html}` | Call `InvitationsService.refreshIncoming()` alongside `FriendshipsService.refresh()` on init. Add a third section between "Incoming requests" and "Friends" titled "Room invitations": renders each entry as `{room.name} · invited by @{inviter.username}` with optional note, **Accept** / **Decline** buttons. Accept navigates to `/app/rooms/{entry.room.id}`; decline removes the row inline (service refreshes). Empty state hidden when the list is empty to avoid a third always-present heading. |
| `client/src/app/features/rooms/room-detail/room-detail.component.{ts,html,scss}` | Add **Invite** button in the room-actions row next to **Leave**; visible only when `currentUserRole` is `'admin'` or `'owner'`. Opens the invite dialog. Add a collapsible "Pending invitations" panel below the member list (admin/owner only); panel loads `InvitationsService.listOutgoing(roomId)` on expand. Each row: `{invitee.displayName}`, `invited by {inviter.displayName}`, **Revoke** button. After any mutate, refetch. |

### Out of scope (explicit — handed to later slices)

- `RoomBan` consultation on send / accept — slice 13.
- `UserBan` consultation on send / accept — slice 14.
- `RoomMemberChanged` realtime event on accept (so the inviter sees the member list grow live without refetch) — slice 13.
- `RoomInvitationChanged` realtime event — slice 13 (reuses the same `ChatHub` group fan-out).
- `ModerationAudit` row on revoke / accept / decline — slice 13 (audit is the moderation slice's entity).
- Invitation rate limit — slice 16.
- `manage-room` shell with tabs — slice 13 hoists the invite dialog + pending list into it.
- Invite-by-friend-click from `/app/contacts` — future UX.
- Pagination on outgoing invitations (at capacity 1000 a room can have at most ~1000 pending invites; JSON is small — skip).

## Key flows (reference)

### Send invitation

1. `POST /api/rooms/{roomId}/invitations { username, note? }` — `[Authorize]`.
2. Trim `username`, `note`; reject `note.Length > 200` with 400 `note_too_long`; empty `note` → `null`.
3. `SELECT` room. Missing / `DeletedAt != null` → 404 `room_not_found`.
4. `RoomPermissionService.IsAdminOrOwnerAsync(roomId, me)` → false → 403 `not_admin_or_owner`.
5. `SELECT` user by `UsernameNormalized`. Missing / `DeletedAt != null` → 404 `user_not_found`.
6. `inviteeId == me.Id` → 400 `cannot_invite_self`.
7. `// TODO(slice-13): reject if RoomBan active for invitee on this room`.
8. `// TODO(slice-14): reject if UserBan active either direction between me and invitee`.
9. `SELECT 1 FROM room_members WHERE room_id = @r AND user_id = @i` → hit → 409 `already_member`.
10. `INSERT INTO room_invitations ...`. Catch unique-violation on `(RoomId, InviteeId)` → 409 `invitation_exists`.
11. Return 201 with `OutgoingInvitationEntry` for the caller to splice into their local outgoing list.

### List incoming (inbox)

1. `GET /api/invitations` — `[Authorize]`.
2. `SELECT ri.*, r.*, inv.* FROM room_invitations ri JOIN rooms r ON ri.room_id = r.id JOIN users inv ON ri.inviter_id = inv.id WHERE ri.invitee_id = @me AND r.deleted_at IS NULL` — the soft-delete guard protects the edge case where a room was deleted with pending invitations (FK cascade already handles hard deletes, but a slice-13 soft-delete would strand pending rows).
3. Project to `List<InvitationEntry>`. Order `CreatedAt DESC`. Return 200 with `IncomingInvitationsResponse`.

### List outgoing (per-room, admin view)

1. `GET /api/rooms/{roomId}/invitations` — `[Authorize]`.
2. `SELECT` room. 404 `room_not_found` on miss.
3. `RoomPermissionService.IsAdminOrOwnerAsync(roomId, me)` → false → 403 `not_admin_or_owner`.
4. `SELECT ri.*, invitee.*, inviter.* FROM room_invitations ri JOIN users invitee ON ri.invitee_id = invitee.id JOIN users inviter ON ri.inviter_id = inviter.id WHERE ri.room_id = @r` ordered `CreatedAt DESC`. Map to `List<OutgoingInvitationEntry>`.

### Accept invitation

1. `POST /api/invitations/{id}/accept` — `[Authorize]`.
2. Begin `SERIALIZABLE` transaction.
3. `SELECT` invitation. Missing → 404 `invitation_not_found`. `InviteeId != me.Id` → 404 `invitation_not_found` (same code — don't leak the existence of invitations addressed to other users).
4. `SELECT` room. `DeletedAt != null` → 404 `room_not_found` and rollback.
5. `// TODO(slice-13): reject if RoomBan for me on room` (possible if a ban landed between send and accept).
6. `// TODO(slice-14): reject if UserBan active` (same caveat).
7. Already-member guard: `SELECT 1 FROM room_members WHERE room_id = @r AND user_id = @me` — hit → delete the dangling invitation and return 409 `already_member` (covers the race where the user joined publicly in another tab between send and accept).
8. `INSERT INTO room_members { room_id, user_id = me, role = Member, joined_at = now }`.
9. `SELECT COUNT(*) FROM room_members WHERE room_id = @r`. If `> capacity`, rollback and return 409 `room_full`.
10. `DELETE FROM room_invitations WHERE id = @id`.
11. Commit. Return 200 with the `RoomDetailResponse` for the room (reuse `RoomService.GetAsync` projection — caller is now a member, so the 403 branch doesn't fire).

### Decline invitation

1. `POST /api/invitations/{id}/decline` — `[Authorize]`.
2. `DELETE FROM room_invitations WHERE id = @id AND invitee_id = @me`. Rows-affected = 0 → 404 `invitation_not_found`. Otherwise 204.

### Revoke or decline (polymorphic)

1. `DELETE /api/invitations/{id}` — `[Authorize]`.
2. `DELETE FROM room_invitations WHERE id = @id AND (inviter_id = @me OR invitee_id = @me)`. Rows-affected = 0 → 404 `invitation_not_found`. Otherwise 204.
3. `POST .../decline` above is a typed alias of this for readable client call sites — points at the same service method with `invitee_id = @me` filter.

## Verification

1. **Build clean.** `dotnet build server/ChatApp.sln` — no warnings.
2. **Migration.** `dotnet ef migrations add AddRoomInvitations --project server/ChatApp.Data --startup-project server/ChatApp.Api`. Inspect: creates `room_invitations` with `ux_room_invitations_room_invitee`, `ix_room_invitations_invitee_id`, `ix_room_invitations_inviter_id`. Commit the migration + snapshot. `docker compose -f infra/docker-compose.yml up -d --build` starts clean and applies.
3. **Unit tests** (xUnit, no DB) in `server/ChatApp.Tests/Unit/Rooms/`:
   - `InvitationService.SendAsync` note validation: 201-char note → `note_too_long`; empty note → coerced to `null`; 200-char note → accepted.
   - `InvitationService.SendAsync` self-invite: invitee lookup returns `me` → `cannot_invite_self`.
   - `RoomPermissionService.IsAdminOrOwnerAsync` matrix already covered by slice 7's tests — add one dedicated test that `SendAsync` refuses the `Member` role with `not_admin_or_owner`.
4. **Integration tests** (Testcontainers Postgres + `WebApplicationFactory`) in `server/ChatApp.Tests/Integration/Rooms/`:
   - **Private room happy path.** A (owner) `POST /api/rooms` `{ visibility: "private" }`. A `POST /api/rooms/{r}/invitations { username: "b" }` → 201 with `OutgoingInvitationEntry`. B `GET /api/invitations` → incoming length 1, `room.name`, `inviter.username == "a"`. B `POST /api/invitations/{i}/accept` → 200 with `RoomDetailResponse` containing B. `GET /api/rooms/{r}` as B → members length 2, `currentUserRole == "member"`.
   - **Public room nudge.** A (owner of public room) invites B. B's inbox has the entry. B can either accept (joins as usual) or call the existing `POST /api/rooms/{r}/join` (also joins — the invite row is left orphaned). On B's next accept of a different stale invite, the already-member guard returns `already_member` (with the dangling invite cleaned up by the accept path).
   - **Revoke before accept.** A invites B. A `DELETE /api/invitations/{i}` → 204. B `GET /api/invitations` → incoming empty. B `POST /api/invitations/{i}/accept` → 404 `invitation_not_found`.
   - **Decline.** A invites B. B `POST /api/invitations/{i}/decline` → 204. A's `GET /api/rooms/{r}/invitations` → outgoing empty. A re-sends → 201 (re-invite allowed immediately).
   - **Duplicate invite.** A invites B; A invites B again → 409 `invitation_exists`.
   - **Self-invite.** A `POST /api/rooms/{r}/invitations { username: "a" }` → 400 `cannot_invite_self`.
   - **Unknown user.** A invites "nobody" → 404 `user_not_found`.
   - **Non-admin invite.** B is a member of A's public room. B `POST /api/rooms/{r}/invitations { username: "c" }` → 403 `not_admin_or_owner`.
   - **Already-member on send.** A invites B; B self-joins via the public-room path in another tab; A re-invites B → 409 `already_member`. (Stale invite must be handled: `AcceptAsync` above includes the guard that cleans the dangling row and returns `already_member`.)
   - **Private room direct-join still rejected.** `POST /api/rooms/{private-r}/join` → 403 `room_is_private`, unchanged from slice 7.
   - **Capacity on accept.** Create a private room with `capacity = 2`. Owner is member 1. Invite B and C concurrently; both accept concurrently. One accept returns 200, the other returns 409 `room_full`. Final `member_count == 2`. This is the test that proves the SERIALIZABLE post-insert count in `AcceptAsync` closes the race — worth asserting.
   - **Admin-only outgoing list.** A invites B. Non-member C `GET /api/rooms/{r}/invitations` → 403 `not_admin_or_owner`. Member D (after joining) `GET .../invitations` → still 403 (admin/owner only). A gets 200 with the list.
   - **Polymorphic DELETE.** Invitee `DELETE /api/invitations/{i}` → 204 (decline). Inviter `DELETE /api/invitations/{i}` (on a different pending invite) → 204 (revoke). Unrelated user `DELETE /api/invitations/{i}` → 404.
   - **Room deletion cascade.** (Setup-only at this slice — slice 13 adds room delete.) Not asserted here; FK cascade is declared in the config so when slice 13 lands its integration can add an assertion.
5. **Compose smoke.** Two browsers. A: create a private room "engineers". A: open `/app/rooms/{id}`, click **Invite**, submit "b", note "join us". B: `/app/contacts` shows the "Room invitations" section with the entry and note. B: click **Accept** → routed to `/app/rooms/{id}`, member list shows A (owner) + B (member). A's browser: refresh `/app/rooms/{id}` → member list shows both, the Pending invitations panel is empty. Repeat the flow but have A click **Revoke** before B accepts → B's inbox row disappears on refresh.

## Follow-ups for later slices

- **Slice 13 (Room moderation)** — lift the invite dialog + pending-invitations panel out of `room-detail` into the new `manage-room` shell (Invitations tab). Wire `RoomInvitationChanged` to the `ChatHub` user-group fan-out so the inbox updates live. Uncomment the RoomBan checks at the two TODO markers. Write `ModerationAudit` rows on revoke / accept.
- **Slice 14 (User-to-user bans)** — uncomment the UserBan checks at the two TODO markers; add 403 `user_ban_active` (or reuse slice-14's code, TBD there).
- **Slice 16 (Hardening)** — rate-limit `POST /api/rooms/{id}/invitations` (per-inviter token bucket, e.g. 20/min) alongside the existing message rate limiters.

## Critical files at a glance

- `server/ChatApp.Data/Entities/Rooms/RoomInvitation.cs`
- `server/ChatApp.Data/Configurations/Rooms/RoomInvitationConfiguration.cs`
- `server/ChatApp.Data/Migrations/{timestamp}_AddRoomInvitations.cs`
- `server/ChatApp.Data/ChatDbContext.cs` (DbSet addition)
- `server/ChatApp.Domain/Services/Rooms/InvitationService.cs`
- `server/ChatApp.Domain/Services/Rooms/RoomsErrors.cs` (four new constants)
- `server/ChatApp.Domain/Services/Rooms/RoomService.cs` (remove slice-12 TODO marker, no behaviour change)
- `server/ChatApp.Api/Controllers/Rooms/InvitationsController.cs`
- `server/ChatApp.Api/Controllers/Rooms/RoomsController.cs` (lift `FromError`/`Ext` helpers)
- `server/ChatApp.Api/Contracts/Rooms/{SendInvitationRequest,InvitationEntry,OutgoingInvitationEntry,IncomingInvitationsResponse,RoomInvitationsResponse}.cs`
- `server/ChatApp.Api/Program.cs` (Rooms DI block: `AddScoped<InvitationService>`)
- `client/src/app/core/rooms/{invitations.service.ts,invitations.models.ts}`
- `client/src/app/features/rooms/invite-user-dialog/invite-user-dialog.component.{ts,html,scss}`
- `client/src/app/features/rooms/room-detail/room-detail.component.{ts,html,scss}` (Invite button + pending panel)
- `client/src/app/features/contacts/contacts.component.{ts,html}` (Room invitations section)
