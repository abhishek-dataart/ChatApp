# Slice 13 — Room moderation (kick / ban / unban)

Closes the Rooms bounded context's admin surface. Admin/owner can remove a member (treated as a ban per spec §6.5), unban, promote member→admin, demote admin→member, edit room capacity, and delete the room. Every moderation action writes a `ModerationAudit` row. `ChatHub` gains three new server→client events: `RoomMemberChanged` (member added / removed / role-changed → `room:{id}` group), `RoomBanned` (kicked user only → `user:{userId}` group → toast + redirect), `RoomDeleted` (`room:{id}` → all members redirected). Room-detail gains a 95 % capacity banner. Slice 12's invite dialog and pending-invitations panel are lifted out of `room-detail` into the new `features/manage-room` admin shell (Members / Bans / Audit / Settings tabs). The two `// TODO(slice-13): RoomBan` markers in `InvitationService` are uncommented and routed through `ModerationService.IsBannedAsync`. No `ModerationAction` broadcast event — the Audit tab refetches on open. UserBan stays deferred to slice 14.

## Context

`docs/implementation-plan.md` slice 13; depends on slice 7 (Rooms — gives `Room`, `RoomMember`, `RoomRole`, `RoomService`, `RoomPermissionService`, capacity guard) and slice 8 (Room messaging — gives the `room:{id}` ChatHub group). Slice 12 (invitations) shipped two TODO markers (`InvitationService.SendAsync` and `AcceptAsync`) that this slice closes, plus a `features/manage-room/.gitkeep` placeholder this slice fills.

Authoritative requirements that fix this slice's shape:

- **Product spec §2** — `RoomBan(room_id, user_id, banned_by_id, created_at, lifted_at?)`; `ModerationAudit(id, room_id, actor_id, target_id, action, created_at)`.
- **Product spec §6.4** — Admins may delete any room message, ban any member, view `RoomBan` list, remove from ban list, demote another admin to member, ban another admin **except the owner**. Owner adds: remove/demote any admin, delete the room. Every moderation action writes `ModerationAudit`.
- **Product spec §6.5** — Removal by admin/owner *is* a ban (single action). Banned user keeps the room as a disabled "Banned from #room" entry in the sidebar (not implemented client-side this slice — see Out of scope: this slice routes the kicked user away from the room and emits `RoomBanned`, the disabled-sidebar visualisation lives in a future polish pass).
- **Product spec §6.6** — Room delete hard-deletes all messages and attachments; name remains reserved (the soft-delete row keeps the unique constraint, per slice 7).
- **Product spec §6.3** — capacity raisable any time by admin/owner (no hard ceiling); 95 % threshold notifies admins.
- **Product spec §10** — `manage-room` modal with tabs Members / Admins / Banned users / Invitations / Settings. (We collapse "Members" + "Admins" into one tab keyed off the existing role badge to avoid two near-identical tables; the Invitations tab is the new home for slice 12's invite dialog and pending list.)
- **Arch doc §Bounded contexts — Rooms** — Rooms owns `room_bans` and `moderation_audit`. `RoomModerationService` is explicitly listed in the Rooms subgraph.
- **Arch doc §Realtime** — `ChatHub` server-to-client events include `RoomMemberChanged`, `RoomBanned`, `ModerationAction`. We ship the first two (plus `RoomDeleted` which is implied by §6.6); `ModerationAction` is deferred since the Audit tab refetches on open and audit doesn't need live push at the 300-user envelope.
- **Implementation-plan row 13** — server scope: `RoomBan`, `ModerationAudit`, admin endpoints, ChatHub events, capacity editing + 95 % banner, room delete. Client scope: `features/manage-room` admin tab (members, bans, audit log); kicked user gets toast + redirect.

Outcome: A is owner of "engineers" with admin B and members C, D. A opens `/app/rooms/{id}/manage` → Members tab lists all four. A clicks **Ban** on D → confirmation → D's tab toasts "You were banned from engineers" and redirects to `/app/rooms`; D no longer sees engineers in the sidebar (refetched). A's Bans tab shows D with "banned by A" timestamp; **Unban** removes the row. A's Audit tab lists `ban`/`unban`/`role_change`/`capacity_change` rows in reverse chronological order. A clicks **Promote** on C → C is now admin (C's room-detail header reflects new role on next refresh; live via `RoomMemberChanged`). A bumps capacity from 1000 to 1500 in Settings tab. A deletes the room → B and C see "Room deleted" toast and are redirected. Banned D attempts to re-join via catalog → 403 `room_banned`. Banned D attempts to accept a stale invitation → 403 `room_banned`. New invitations targeting D → 403 `room_banned`.

## Decisions

Interview answers folded in; *[decided]* flags items that closed a genuinely open option.

| Topic | Decision | Rationale |
|---|---|---|
| Kick vs ban | **Single action — remove == ban**, per spec §6.5. One endpoint `POST /api/rooms/{roomId}/bans { userId }`. No separate "kick" endpoint or button | *[decided]* — matches spec verbatim. Two-action UX is more flexible but the spec ruled it out, and a single action keeps the audit log readable (one `ban` row, one `unban` row, no parallel `kick` action). Re-joining a public room after an unban is the existing self-join path (slice 7 `JoinAsync`) — already free for non-banned users |
| Ban storage | `RoomBan` row with `LiftedAt? IS NULL` indicating active ban; unban sets `LiftedAt = now` rather than hard-delete. Re-ban after unban inserts a fresh row (not "reactivates"). Spec §2 says "reversible via lifted_at" — so we keep history | History matters for the Audit tab and the spec literally names a `lifted_at` column. Hard-deleting on unban would lose the audit trail of "X was previously banned" |
| `RoomBan` uniqueness | **Partial unique index** `ux_room_bans_room_user_active` on `(RoomId, UserId)` `WHERE LiftedAt IS NULL` (Npgsql `HasFilter`) — guarantees at most one active ban per (room, user) pair while leaving lifted history rows untouched. Race-free `already_banned` check by catching the unique violation on insert, mirroring slice-12's `ux_room_invitations_room_invitee` discipline | *[decided]* — application-side guards alone race on concurrent ban attempts; the partial index closes the race in the DB |
| Member-row removal | Banning **deletes** the `RoomMember` row in the same transaction as the `RoomBan` insert. Unbanning does **not** auto-rejoin — the user re-joins via the existing public-room join path or a fresh invitation | Matches spec §6.5: "Removed from sidebar only when they explicitly dismiss." The membership row is gone; the ban row is the source of truth for "blocked from this room" |
| Self-ban / owner-ban / admin-bans-admin | Reject `targetId == me.Id` → 400 `cannot_ban_self`. Reject if `target.Role == Owner` → 403 `cannot_ban_owner` (no exceptions, owner is permanent per spec §6.2). Reject if `actor.Role == Admin && target.Role == Admin` → 403 `cannot_ban_peer_admin` (only owner can ban an admin per spec §6.4). Owner banning admin: allowed | *[decided]* — spec §6.4 says admins can ban "another admin **except the owner**" — but reading with §6.2 ("Owner cannot lose admin"), the **owner** is the only role that's permanently un-bannable. Admins banning each other invites mutual-ouster races; restricting to owner-only matches the conservative reading and keeps audit clean. Members can ban members (no — only admin/owner can ban at all); owner can ban anyone non-owner |
| Promote / demote | `PATCH /api/rooms/{roomId}/members/{userId}/role { role: "admin" \| "member" }`. Owner-only when target is currently Admin (only owner can demote an admin per spec §6.4); admins can promote members. Owner role itself cannot be set or removed via this endpoint (400 `cannot_change_owner_role`). Self-demote allowed for an admin (admins may step down to member); self-promote rejected (400 `cannot_promote_self`) | *[decided]* — symmetric with the ban matrix. Self-demote is harmless and lets an admin "step down" without owner intervention. Self-promote would let any admin promote themselves to owner — out of scope and against §6.2 |
| Capacity edit | `PATCH /api/rooms/{roomId}/capacity { capacity }`. Admin/owner only. Validate `capacity >= max(member_count, 1)` (can't shrink below current population) → 400 `capacity_below_population`. No upper bound (spec §6.3 says raisable, no ceiling). Writes a `capacity_change` audit row with the old + new values encoded in a `Detail` JSON column | *[decided]* — the audit table needs an optional payload column; we add `string? Detail` (jsonb in Postgres) so capacity changes carry `{ "from": 1000, "to": 1500 }`. Other actions leave `Detail = null` |
| Room delete | `DELETE /api/rooms/{roomId}`. Owner-only → 403 `not_owner`. Soft-delete: set `Room.DeletedAt = now`, do **not** physically drop the row (matches slice 7 — keeps `Name` reserved per spec §6.6). `RoomBan`/`ModerationAudit`/`RoomMember`/`RoomInvitation` cascades remain as configured by earlier slices but logically sit unreachable via room reads (we filter by `DeletedAt IS NULL` everywhere). Messages + attachment files hard-delete is **deferred to slice 15 cleanup** — this slice writes a `// TODO(slice-15): purge messages + attachment files for soft-deleted rooms via background job` marker. Spec §6.6 says hard-delete; we ship soft-delete + a documented gap because the messages/attachments cleanup is its own non-trivial work item (file system traversal, transaction sizing, background scheduling) | *[decided]* — same compromise slice 7 made for `Room` itself. Documented gap is preferable to half-baked file deletion that could lose data |
| Audit log | `ModerationAudit { Id, RoomId, ActorId, TargetId?, Action, Detail?, CreatedAt }`. `Action` is a string enum constant: `ban`, `unban`, `role_change`, `capacity_change`, `room_delete`. `TargetId` is null for `capacity_change` and `room_delete` (room-scoped). `Detail` is jsonb (`string?`) carrying action-specific extras: `role_change` → `{ "from": "member", "to": "admin" }`; `capacity_change` → `{ "from": 1000, "to": 1500 }`. Audit rows are never edited or deleted. View endpoint `GET /api/rooms/{roomId}/audit` admin/owner only | *[decided]* — schema is forward-compatible with future actions (e.g. `invite_revoked`) without column changes. Rejecting "one row per action type with typed columns" — too many columns sit null per row |
| Audit pagination | `GET /api/rooms/{roomId}/audit?limit=50&before={id}` — keyset pagination on `(CreatedAt, Id)` DESC, mirroring the message-pagination shape that slice 15 will introduce. For this slice, server defaults `limit=50`, accepts `1..200`, returns `{ items, nextBefore? }`. Simple enough that we don't punt to slice 15 | One audit row per moderation action × 300 users × heavy moderation = under 100k rows / room / year. Limit alone is fine; keyset is cheap to add now and avoids a polish pass later |
| Endpoint shape | Group moderation endpoints under `ModerationController`: `POST /api/rooms/{roomId}/bans`, `GET /api/rooms/{roomId}/bans`, `DELETE /api/rooms/{roomId}/bans/{userId}`, `PATCH /api/rooms/{roomId}/members/{userId}/role`, `GET /api/rooms/{roomId}/audit`. Capacity edit (`PATCH /api/rooms/{roomId}/capacity`) and room delete (`DELETE /api/rooms/{roomId}`) belong on `RoomsController` since they mutate the room itself, not its moderation surface | *[decided]* — Capacity and delete are room-CRUD; bans/roles/audit are moderation. Keeps controller responsibilities crisp |
| Hub events | Three events on `ChatHub`: (1) `RoomMemberChanged` to `room:{roomId}` carrying `{ roomId, userId, change: "added" \| "removed" \| "role_changed", role? }`. (2) `RoomBanned` to `user:{bannedUserId}` carrying `{ roomId, roomName, bannedBy: UserSummary, createdAt }` — only the banned user receives this. (3) `RoomDeleted` to `room:{roomId}` carrying `{ roomId }`. No `ModerationAction` event in this slice | *[decided]* — `RoomMemberChanged` is the live update for member-list views (handles ban-as-removed too — banned user sees `RoomBanned` for the toast, others see them disappear via `RoomMemberChanged`). `RoomDeleted` lets non-owner members redirect off the deleted room |
| Hub group cleanup | After ban: server calls `IHubContext<ChatHub>.Groups.RemoveFromGroupAsync(connectionId, "room:{id}")` for **all** of the banned user's connections so the message stream stops reaching them. Tracking which connections belong to a user requires a connection registry — slice 6's `PresenceAggregator` already maintains `ConcurrentDictionary<Guid, HashSet<string>>`. Inject it (read-only API: `IEnumerable<string> GetConnectionIds(Guid userId)`) and iterate. Same for `RoomDeleted` (remove all room members from `room:{id}`) | *[decided]* — without this, banned users' open tabs would keep receiving `MessageCreated` until they reconnect. Reusing PresenceAggregator's connection registry avoids inventing a parallel store |
| `JoinAsync` ban check | Add an active-ban check at the top of `RoomService.JoinAsync` (after the room-exists / not-private guards): `SELECT 1 FROM room_bans WHERE room_id = @r AND user_id = @me AND lifted_at IS NULL` → 403 `room_banned`. Same check inside `InvitationService.AcceptAsync` (where the slice-12 TODO sits) | Closes the spec §6.3 gap: "any non-banned authenticated user joins" |
| `InvitationService.SendAsync` ban check | Uncomment slice-12's `// TODO(slice-13): RoomBan` marker. Reject if invitee has active `RoomBan` on this room → 403 `invitee_room_banned`. Distinct error code (not the inviter's `room_banned`) so the inviter UI can show "User is banned from this room — unban first" | Two different actors trigger near-identical errors; distinct codes keep client UX honest |
| Toast / redirect on `RoomBanned` | Client receives the event; calls `toast.show({ severity: "warn", message: "You were banned from #engineers" })`; if currently routed to `/app/rooms/{bannedRoomId}` or any sub-route, navigate to `/app/rooms`; refetch `RoomsService.refreshMine()` to drop the room from the sidebar | The disabled-sidebar UX from spec §6.5 is deferred (see Out of scope); for this slice the room simply disappears |
| 95 % capacity banner | Computed signal in `room-detail.component.ts`: `capacityNearFull = computed(() => room()!.memberCount / room()!.capacity >= 0.95)`. Renders a yellow banner above the message list visible to admin/owner only ("Room is at {{ pct }}% capacity. Consider raising capacity in Settings."). On 100 % the banner switches to red ("Room is full. New joins will be rejected.") | Spec §6.3 says "owner + admins receive in-app notification (toast + persistent banner in Manage Room)". We render the banner on room-detail (where it's actually useful while chatting) and a toast is fired once when crossing the threshold (debounced via signal-effect comparing previous value) |
| Manage-room shell | New route `/app/rooms/:id/manage` outside `RoomDetailComponent` (sibling under app-shell). Lazy-loaded standalone component with four child tab components: `members-tab`, `bans-tab`, `audit-tab`, `settings-tab`. Top of the page: room header (name, member-count / capacity, role badge), tab nav. Settings tab hosts: capacity editor, room-delete button (owner-only), and the Invitations sub-section (lifted from slice 12 — invite dialog + pending invitations list). Caller must be admin or owner — guard via `RoomModerationGuard` that calls `RoomsService.get(id)` and 403s if `currentUserRole == "member"`. Non-admin / non-owner is redirected to `/app/rooms/:id` | *[decided]* — matches spec §10 wireframe verbatim; lifts slice-12 affordances into their final home; a route (not modal) gives clean back-navigation and deep links |
| `room-detail` cleanup | Remove the slice-12 **Invite** button and **Pending invitations** panel from `room-detail.component.html`; both move to the Manage tab's Settings section. Add a single **Manage room** button (admin/owner only) that navigates to `/manage`. The capacity stat + 95 % banner stay on `room-detail` (they're useful mid-chat) | Slice 12 deliberately put those affordances on `room-detail` "until slice 13 lifts them". This is that lift |
| Toast service | New `client/src/app/core/notifications/toast.service.ts` — signal-based queue (`toasts = signal<Toast[]>([])`), public `show({severity, message, durationMs?})`, auto-dismiss via `setTimeout`. New `client/src/app/shared/toast/toast-outlet.component.ts` — renders the queue as fixed bottom-right pill stack. Mounted once in `app-shell.component.html` | First general toast surface in the app; needed for the kicked-user UX. Other slices (rate-limit 429, errors) can hook into the same service later |
| SignalR plumbing | Extend `signalr.service.ts` with three Subjects: `roomMemberChanged$`, `roomBanned$`, `roomDeleted$`. Wire on `chatConn.start()` via `chatConn.on("RoomMemberChanged", ...)` etc. Components subscribe via signals (`toSignal(roomMemberChanged$, { initialValue: null })`) or via the raw subject in `effect()` blocks | Mirrors the slice-5/8 pattern for `MessageCreated` |
| Error codes | New: `RoomBanned` (403, "User is banned from this room"), `InviteeRoomBanned` (403), `AlreadyBanned` (409), `BanNotFound` (404), `CannotBanSelf` (400), `CannotBanOwner` (403), `CannotBanPeerAdmin` (403), `CannotChangeOwnerRole` (400), `CannotPromoteSelf` (400), `NotOwner` (403), `CapacityBelowPopulation` (400), `InvalidCapacity` (400), `MemberNotFound` (404), `InvalidRole` (400) | Stable, short, one code per distinct client action |
| DI + folder layout | New service: `server/ChatApp.Domain/Services/Rooms/ModerationService.cs`. New controller: `server/ChatApp.Api/Controllers/Rooms/ModerationController.cs`. Entities under `server/ChatApp.Data/Entities/Rooms/`, configurations under `server/ChatApp.Data/Configurations/Rooms/`. `AddScoped<ModerationService>()` in `Program.cs` under the existing `// Rooms` block, after `AddScoped<InvitationService>()` | Matches the slice-12 layout exactly |
| Error-result pattern | `ModerationService` methods return `(bool Ok, string? Code, string? Message, T? Value)` tuples — same as `RoomService` and `InvitationService` | Codebase convention |

### Deferred (explicit — handed to later slices)

- **Disabled "Banned from #room" sidebar entry** (spec §6.5) — requires a sidebar redesign that knows about banned rooms separately from joined rooms. Slice 13 ships the toast + redirect + drop-from-sidebar; the banned-but-still-visible affordance is a polish pass (call it slice 13.5 if needed; not blocking demo).
- **Hard-delete of messages + attachment files on room delete** — soft-delete the room row and document via `// TODO(slice-15): purge messages + attachment files for soft-deleted rooms`. Slice 15 (or a dedicated cleanup slice) implements the background sweep.
- **`ModerationAction` ChatHub broadcast** — Audit tab refetches on open; no live push. If a future demo wants live audit, bolt on a `ModerationActionAdded` event that mirrors `RoomMemberChanged`'s fan-out.
- **`UserBan`** — slice 14. The `// TODO(slice-14)` markers in `InvitationService` stay.
- **Ownership transfer** — out of scope per spec §1.
- **Invite-from-banned-list "Re-invite" button** — nice UX, requires invitation context inside Bans tab. Can be added in a polish pass.
- **Audit log filtering / search** — out of scope; chronological list + pagination is enough for the spec.
- **Rate-limit on moderation endpoints** — slice 16 (general hardening pass).

## Scope

### Server — files to create

| Path | Purpose |
|------|---------|
| `server/ChatApp.Data/Entities/Rooms/RoomBan.cs` | `{ Guid Id, Guid RoomId, Guid UserId, Guid BannedById, DateTimeOffset CreatedAt, DateTimeOffset? LiftedAt }`. |
| `server/ChatApp.Data/Entities/Rooms/ModerationAudit.cs` | `{ Guid Id, Guid RoomId, Guid ActorId, Guid? TargetId, string Action, string? Detail, DateTimeOffset CreatedAt }`. `Action` is a constant from `ModerationActions` static class (`ban`, `unban`, `role_change`, `capacity_change`, `room_delete`). `Detail` stored as `jsonb` (Npgsql column type `"jsonb"`). |
| `server/ChatApp.Data/Configurations/Rooms/RoomBanConfiguration.cs` | `ToTable("room_bans")`. PK on `Id`. **Partial unique** index `ux_room_bans_room_user_active` on `(RoomId, UserId)` `WHERE LiftedAt IS NULL` (Npgsql `HasFilter("\"LiftedAt\" IS NULL")`). Non-unique index `ix_room_bans_room_id` on `RoomId` for the bans-list query. FKs: `RoomId → rooms.id` cascade; `UserId`, `BannedById → users.id` restrict. |
| `server/ChatApp.Data/Configurations/Rooms/ModerationAuditConfiguration.cs` | `ToTable("moderation_audit")`. PK on `Id`. Index `ix_moderation_audit_room_created` on `(RoomId, CreatedAt DESC, Id DESC)` for the keyset audit query. `Action` max 32. `Detail` column type `"jsonb"`. FKs: `RoomId → rooms.id` cascade; `ActorId`, `TargetId → users.id` restrict (audit history outlives users in other rooms; soft-delete already protects display). |
| `server/ChatApp.Data/Migrations/{timestamp}_AddRoomModeration.cs` | Generated via `dotnet ef migrations add AddRoomModeration --project server/ChatApp.Data --startup-project server/ChatApp.Api`. Creates both tables and indexes. |
| `server/ChatApp.Domain/Services/Rooms/ModerationService.cs` | Methods: `BanAsync(Guid actorId, Guid roomId, Guid targetId)`, `UnbanAsync(Guid actorId, Guid roomId, Guid targetId)`, `ListBansAsync(Guid actorId, Guid roomId)`, `ChangeRoleAsync(Guid actorId, Guid roomId, Guid targetId, RoomRole newRole)`, `ListAuditAsync(Guid actorId, Guid roomId, int limit, Guid? before)`, `IsBannedAsync(Guid roomId, Guid userId, CancellationToken ct)` (called from `RoomService.JoinAsync` and `InvitationService.SendAsync`/`AcceptAsync` — internal helper). All mutating methods write a `ModerationAudit` row in the same transaction. `BanAsync` deletes the `RoomMember` row, inserts the `RoomBan` row, calls `ChatHub.Groups.RemoveFromGroupAsync` for each of the target's connection IDs, broadcasts `RoomMemberChanged(removed)` to `room:{id}` and `RoomBanned` to `user:{targetId}`. `UnbanAsync` sets `LiftedAt = now`. `ChangeRoleAsync` runs the matrix in §Decisions and broadcasts `RoomMemberChanged(role_changed)`. |
| `server/ChatApp.Domain/Services/Rooms/ModerationActions.cs` | `internal static class ModerationActions { public const string Ban = "ban"; ... }`. |
| `server/ChatApp.Api/Controllers/Rooms/ModerationController.cs` | `[Authorize]` class. Routes: `POST /api/rooms/{roomId:guid}/bans`, `GET /api/rooms/{roomId:guid}/bans`, `DELETE /api/rooms/{roomId:guid}/bans/{userId:guid}`, `PATCH /api/rooms/{roomId:guid}/members/{userId:guid}/role`, `GET /api/rooms/{roomId:guid}/audit`. Uses `RoomsErrorMapper.FromError`. |
| `server/ChatApp.Api/Contracts/Rooms/BanUserRequest.cs` | `{ Guid UserId }`. |
| `server/ChatApp.Api/Contracts/Rooms/RoomBanEntry.cs` | `{ Guid BanId, UserSummary User, UserSummary BannedBy, DateTimeOffset CreatedAt }`. |
| `server/ChatApp.Api/Contracts/Rooms/RoomBansResponse.cs` | `{ List<RoomBanEntry> Bans }`. |
| `server/ChatApp.Api/Contracts/Rooms/ChangeRoleRequest.cs` | `{ string Role }` (`admin` or `member`; anything else → 400 `invalid_role`). |
| `server/ChatApp.Api/Contracts/Rooms/AuditEntry.cs` | `{ Guid Id, UserSummary Actor, UserSummary? Target, string Action, string? Detail, DateTimeOffset CreatedAt }`. |
| `server/ChatApp.Api/Contracts/Rooms/AuditResponse.cs` | `{ List<AuditEntry> Items, Guid? NextBefore }`. |
| `server/ChatApp.Api/Contracts/Rooms/UpdateCapacityRequest.cs` | `{ int Capacity }`. |
| `server/ChatApp.Api/Contracts/Rooms/RoomMemberChangedPayload.cs` | `{ Guid RoomId, Guid UserId, string Change, string? Role }`. |
| `server/ChatApp.Api/Contracts/Rooms/RoomBannedPayload.cs` | `{ Guid RoomId, string RoomName, UserSummary BannedBy, DateTimeOffset CreatedAt }`. |
| `server/ChatApp.Api/Contracts/Rooms/RoomDeletedPayload.cs` | `{ Guid RoomId }`. |

### Server — files to modify

| Path | Change |
|------|--------|
| `server/ChatApp.Data/ChatDbContext.cs` | `public DbSet<RoomBan> RoomBans => Set<RoomBan>();` and `public DbSet<ModerationAudit> ModerationAudits => Set<ModerationAudit>();`. |
| `server/ChatApp.Domain/Services/Rooms/RoomsErrors.cs` | Add: `RoomBanned`, `InviteeRoomBanned`, `AlreadyBanned`, `BanNotFound`, `CannotBanSelf`, `CannotBanOwner`, `CannotBanPeerAdmin`, `CannotChangeOwnerRole`, `CannotPromoteSelf`, `NotOwner`, `CapacityBelowPopulation`, `InvalidCapacity`, `MemberNotFound`, `InvalidRole`. |
| `server/ChatApp.Domain/Services/Rooms/RoomService.cs` | (a) `JoinAsync`: insert active-ban guard after the not-private check via `ModerationService.IsBannedAsync` (or a thin reader to avoid circular DI). (b) Add `DeleteAsync(Guid actorId, Guid roomId)` — owner-only (403 `not_owner`); soft-deletes `Room` (set `DeletedAt`); writes `ModerationAudit` `room_delete`; broadcasts `RoomDeleted` to `room:{id}` group; calls `ChatHub.Groups.RemoveFromGroupAsync` for every member's connections. (c) Add `UpdateCapacityAsync(Guid actorId, Guid roomId, int capacity)` — admin/owner; validates `capacity >= memberCount`; writes `ModerationAudit` `capacity_change` with `Detail` JSON `{from, to}`. |
| `server/ChatApp.Domain/Services/Rooms/InvitationService.cs` | Replace the two `// TODO(slice-13): reject if RoomBan` markers with active-ban guards via `ModerationService.IsBannedAsync` (or the shared reader). On `SendAsync` → 403 `invitee_room_banned`. On `AcceptAsync` → 403 `room_banned`. |
| `server/ChatApp.Domain/Abstractions/IChatBroadcaster.cs` | Add `BroadcastRoomMemberChangedAsync(Guid roomId, RoomMemberChangedPayload payload, CancellationToken)`, `BroadcastRoomBannedToUserAsync(Guid userId, RoomBannedPayload, CancellationToken)`, `BroadcastRoomDeletedAsync(Guid roomId, RoomDeletedPayload, CancellationToken)`. |
| `server/ChatApp.Domain/Services/Realtime/ChatBroadcaster.cs` | Implement the three new methods using `hub.Clients.Group(...)` — same pattern as `BroadcastMessageCreatedToRoomAsync`. |
| `server/ChatApp.Api/Controllers/Rooms/RoomsController.cs` | Add `DELETE /api/rooms/{id:guid}` → `RoomService.DeleteAsync`. Add `PATCH /api/rooms/{id:guid}/capacity` → `RoomService.UpdateCapacityAsync`. |
| `server/ChatApp.Api/Controllers/Rooms/RoomsErrorMapper.cs` | Map all new error codes to HTTP status + title. |
| `server/ChatApp.Api/Program.cs` | Under `// Rooms`: `builder.Services.AddScoped<ModerationService>();`. If `PresenceAggregator` is not already injectable for connection enumeration, expose `IEnumerable<string> GetConnectionIds(Guid userId)` on it (read-only) and reuse it. |
| `server/ChatApp.Domain/Services/Presence/PresenceAggregator.cs` | If not already exposing connection IDs by user, add `IEnumerable<string> GetConnectionIds(Guid userId)` (read-only over the existing dictionary). |

### Client — files to create

| Path | Purpose |
|------|---------|
| `client/src/app/core/rooms/moderation.models.ts` | TS mirrors of `RoomBanEntry`, `RoomBansResponse`, `AuditEntry`, `AuditResponse`, `RoomMemberChangedPayload`, `RoomBannedPayload`, `RoomDeletedPayload`. Plus `BanUserRequest`, `ChangeRoleRequest`, `UpdateCapacityRequest`. |
| `client/src/app/core/rooms/moderation.service.ts` | Methods: `listBans(roomId)`, `ban(roomId, userId)`, `unban(roomId, userId)`, `changeRole(roomId, userId, role)`, `listAudit(roomId, before?, limit?)`. State: `bans = signal<RoomBanEntry[] \| null>(null)`, audit kept local to the audit-tab component. After `ban`/`unban`/`changeRole`, re-fetches `bans` and the parent room detail. |
| `client/src/app/core/notifications/toast.service.ts` | `Toast { id, severity: "info"\|"warn"\|"error", message, durationMs }`. `toasts = signal<Toast[]>([])`. `show(...)`, internal `dismiss(id)`. Auto-dismiss via `setTimeout`. |
| `client/src/app/shared/toast/toast-outlet.component.ts` | Standalone component; renders `toastService.toasts()` as fixed bottom-right pill stack. |
| `client/src/app/features/manage-room/manage-room.component.{ts,html,scss}` | Shell: header (room name, member-count / capacity, role badge), four-tab nav (Members / Bans / Audit / Settings). Loads `RoomsService.get(id)` on init; redirects to `/app/rooms/:id` if `currentUserRole == 'member'`. Hosts the four tab components via signal-controlled tab switch (simpler than child routes). |
| `client/src/app/features/manage-room/members-tab/members-tab.component.{ts,html,scss}` | Reuses `RoomDetailResponse.members`. Per row: avatar, displayName, @username, role badge. Action affordances based on `currentUserRole` + target role: **Ban**, **Promote** (member → admin), **Demote** (admin → member, owner-only or self-demote). Confirmation dialog before destructive actions. After mutate, refetches room detail. |
| `client/src/app/features/manage-room/bans-tab/bans-tab.component.{ts,html,scss}` | Calls `ModerationService.listBans(roomId)` on init. Renders banned-user list with banner attribution, banned-on date, **Unban** button. After unban, refreshes the list. |
| `client/src/app/features/manage-room/audit-tab/audit-tab.component.{ts,html,scss}` | Calls `ModerationService.listAudit(roomId)` on init. Renders rows: `{actor.displayName} · {action} · {target?.displayName ?? '—'} · {detail (rendered per action)} · {createdAt}`. Detail rendering: `role_change` → "{from} → {to}"; `capacity_change` → "{from} → {to}"; others omit. **Load older** button at bottom drives keyset pagination via `nextBefore`. |
| `client/src/app/features/manage-room/settings-tab/settings-tab.component.{ts,html,scss}` | Capacity field with **Save** (admin/owner). Owner-only **Delete room** button with type-room-name confirmation dialog. Below: "Invitations" sub-section that lifts the slice-12 invite dialog + pending-invitations panel verbatim (move the existing components, don't rewrite them). |

### Client — files to modify

| Path | Change |
|------|--------|
| `client/src/app/app.routes.ts` | Add `{ path: 'rooms/:id/manage', loadComponent: () => import('./features/manage-room/manage-room.component').then(m => m.ManageRoomComponent) }` under the `/app` shell. |
| `client/src/app/features/app-shell/app-shell.component.html` | Mount `<app-toast-outlet/>` once near the root element. |
| `client/src/app/features/rooms/room-detail/room-detail.component.{ts,html}` | (a) **Remove** the slice-12 Invite button + Pending invitations panel (they migrate to settings-tab). (b) **Add** "Manage room" button (admin/owner only) navigating to `/app/rooms/{id}/manage`. (c) **Add** 95 % capacity banner (computed signal). (d) Subscribe to `signalr.roomMemberChanged$`, `signalr.roomDeleted$`: on `removed`/`role_changed` for current user, refetch room detail (or redirect on remove); on `RoomDeleted` for current room, toast "Room was deleted" and navigate to `/app/rooms`. (e) Subscribe to `signalr.roomBanned$` (this can also live in `app.component.ts` so it works regardless of route): toast "You were banned from #{roomName}", call `RoomsService.refreshMine()`, redirect to `/app/rooms` if currently inside the banned room. |
| `client/src/app/core/signalr/signalr.service.ts` | Add three Subjects: `roomMemberChanged$`, `roomBanned$`, `roomDeleted$`. Wire `chatConn.on("RoomMemberChanged", payload => roomMemberChangedSubject.next(payload))` etc. inside the existing chat-connect setup. |
| `client/src/app/core/rooms/rooms.service.ts` | Add `delete(roomId)` (DELETE) → on success calls `refreshMine()`. Add `updateCapacity(roomId, capacity)` (PATCH) → on success refetches the room detail. |

### Out of scope (explicit — handed to later slices)

- Disabled "Banned from #room" sidebar entry (spec §6.5 polish) — slice 13.5 / future polish.
- Hard-delete of messages + attachment files for soft-deleted rooms — slice 15 background sweep.
- `ModerationAction` live broadcast — bolt on if a future demo needs live audit.
- `UserBan` consultation on send / accept — slice 14.
- Rate limits on moderation endpoints — slice 16.
- Audit search / filtering — out of scope.
- Ownership transfer — out of scope per spec §1.

## Key flows (reference)

### Ban a member

1. `POST /api/rooms/{roomId}/bans { userId }` — `[Authorize]`.
2. `SELECT` room. Missing / `DeletedAt != null` → 404 `room_not_found`.
3. `RoomPermissionService.GetRoleAsync(roomId, me)` → null/Member → 403 `not_admin_or_owner`.
4. `userId == me.Id` → 400 `cannot_ban_self`.
5. `SELECT` target `RoomMember`. Missing → 404 `member_not_found`.
6. `target.Role == Owner` → 403 `cannot_ban_owner`.
7. `actor.Role == Admin && target.Role == Admin` → 403 `cannot_ban_peer_admin`.
8. Begin transaction.
9. `INSERT INTO room_bans { id, room_id, user_id = target, banned_by_id = me, created_at = now, lifted_at = null }`. Catch unique-violation on partial index → 409 `already_banned`.
10. `DELETE FROM room_members WHERE room_id = @r AND user_id = @target`.
11. `INSERT INTO moderation_audit { id, room_id, actor_id = me, target_id = target, action = "ban", detail = null, created_at = now }`.
12. Commit.
13. For each connection id in `presenceAggregator.GetConnectionIds(target)`: `hub.Groups.RemoveFromGroupAsync(connId, "room:{roomId}")`.
14. Broadcast `RoomMemberChanged { roomId, userId = target, change = "removed" }` to `room:{roomId}`.
15. Broadcast `RoomBanned { roomId, roomName, bannedBy: UserSummary(me), createdAt }` to `user:{target}`.
16. Return 204.

### Unban

1. `DELETE /api/rooms/{roomId}/bans/{userId}` — `[Authorize]`.
2. Room + admin/owner check (same as above).
3. `UPDATE room_bans SET lifted_at = now WHERE room_id = @r AND user_id = @u AND lifted_at IS NULL` — rows-affected = 0 → 404 `ban_not_found`.
4. `INSERT INTO moderation_audit { action = "unban", target_id = userId }`.
5. Commit. Return 204. (No hub event — unbanned user re-joins via the normal join path which produces its own `RoomMemberChanged(added)`.)

### List bans

1. `GET /api/rooms/{roomId}/bans` — `[Authorize]`.
2. Room + admin/owner check.
3. `SELECT rb.*, u.*, banner.* FROM room_bans rb JOIN users u ON rb.user_id = u.id JOIN users banner ON rb.banned_by_id = banner.id WHERE rb.room_id = @r AND rb.lifted_at IS NULL` ordered `CreatedAt DESC`.
4. Return `RoomBansResponse`.

### Change role

1. `PATCH /api/rooms/{roomId}/members/{userId}/role { role }` — `[Authorize]`.
2. Room + admin/owner check.
3. Validate `role IN ("admin", "member")` → else 400 `invalid_role`.
4. `SELECT` target `RoomMember`. Missing → 404 `member_not_found`.
5. `target.Role == Owner` → 400 `cannot_change_owner_role`.
6. `targetRole == Admin && newRole == Member && actor.Role != Owner` → 403 (only owner can demote admins).
7. `userId == me.Id && newRole == Admin` → 400 `cannot_promote_self`.
8. `target.Role == newRole` → 204 (no-op, no audit row).
9. Begin transaction. `UPDATE room_members SET role = @newRole`. `INSERT moderation_audit { action = "role_change", target_id = userId, detail = '{"from":"...","to":"..."}' }`. Commit.
10. Broadcast `RoomMemberChanged { roomId, userId, change = "role_changed", role = newRole }` to `room:{roomId}`.
11. Return 204.

### Update capacity

1. `PATCH /api/rooms/{roomId}/capacity { capacity }` — `[Authorize]`.
2. Room + admin/owner check.
3. `capacity < 1` → 400 `invalid_capacity`.
4. `SELECT COUNT(*) FROM room_members WHERE room_id = @r`. `capacity < count` → 400 `capacity_below_population`.
5. Begin transaction. `UPDATE rooms SET capacity = @capacity`. `INSERT moderation_audit { action = "capacity_change", detail = '{"from":N,"to":M}' }`. Commit.
6. Return 200 with the updated `RoomDetailResponse` (consistent with slice 12's accept). (No hub event needed — admins reading capacity see it on next refresh; banner update is local to the editor.)

### Delete room

1. `DELETE /api/rooms/{roomId}` — `[Authorize]`.
2. `SELECT` room. Missing / soft-deleted → 404 `room_not_found`.
3. `me.Id != room.OwnerId` → 403 `not_owner`.
4. Snapshot member list (for hub group cleanup).
5. Begin transaction. `UPDATE rooms SET deleted_at = now`. `INSERT moderation_audit { action = "room_delete" }`. Commit.
6. Broadcast `RoomDeleted { roomId }` to `room:{roomId}`.
7. For each member's connections: `hub.Groups.RemoveFromGroupAsync(connId, "room:{id}")`.
8. `// TODO(slice-15): purge messages + attachment files for soft-deleted rooms via background job.`
9. Return 204.

### List audit

1. `GET /api/rooms/{roomId}/audit?limit=50&before={id}` — `[Authorize]`.
2. Room + admin/owner check.
3. `limit` clamped `1..200`; default `50`.
4. `SELECT` `moderation_audit` joined to `users` for actor + target. `WHERE room_id = @r AND (created_at, id) < (@beforeCreated, @beforeId)` if `before` supplied. `ORDER BY created_at DESC, id DESC LIMIT @limit + 1`.
5. If returned `> limit`, slice to `limit` and set `nextBefore = lastReturned.Id`.
6. Map to `AuditResponse`.

### Join with active ban

1. `POST /api/rooms/{id}/join` — same as slice 7 up to the not-private check.
2. **New:** `SELECT 1 FROM room_bans WHERE room_id = @r AND user_id = @me AND lifted_at IS NULL` → hit → 403 `room_banned`.
3. Continue with the existing capacity-guarded insert.

## Verification

1. **Build clean.** `dotnet build server/ChatApp.sln` — no warnings.
2. **Migration.** `dotnet ef migrations add AddRoomModeration --project server/ChatApp.Data --startup-project server/ChatApp.Api`. Inspect: creates `room_bans` (with `ux_room_bans_room_user_active` partial unique index, `ix_room_bans_room_id`) and `moderation_audit` (with `ix_moderation_audit_room_created`). Commit migration + snapshot. `docker compose -f infra/docker-compose.yml up -d --build` starts clean.
3. **Unit tests** (xUnit, no DB) in `server/ChatApp.Tests/Unit/Rooms/`:
   - `ModerationService.BanAsync`: actor=Member → `not_admin_or_owner`; target=Owner → `cannot_ban_owner`; actor=Admin & target=Admin → `cannot_ban_peer_admin`; self → `cannot_ban_self`.
   - `ChangeRoleAsync` matrix: admin promoting member → ok; admin demoting admin → 403 (only owner); owner demoting admin → ok; admin self-demoting → ok; member self-promoting → 400.
   - `RoomService.UpdateCapacityAsync`: capacity below current member count → `capacity_below_population`; capacity 0 → `invalid_capacity`.
4. **Integration tests** (Testcontainers Postgres + `WebApplicationFactory`) in `server/ChatApp.Tests/Integration/Rooms/`:
   - **Ban + redirect.** Owner A creates room, B joins. A `POST /api/rooms/{r}/bans { userId: B }` → 204. `GET /api/rooms/{r}/bans` → length 1. B's `GET /api/rooms/mine` → no longer contains room. B `POST /api/rooms/{r}/join` → 403 `room_banned`.
   - **Unban.** A `DELETE /api/rooms/{r}/bans/{B}` → 204. `GET /api/rooms/{r}/bans` → empty. B `POST /api/rooms/{r}/join` → 200 (re-joined).
   - **Already banned.** Ban B; ban B again → 409 `already_banned`. After unban, ban B again → 204 (new row, history preserved).
   - **Owner-protected.** Member M `POST /bans { userId: Owner }` → 403 `not_admin_or_owner` (M isn't admin); admin C `POST /bans { userId: Owner }` → 403 `cannot_ban_owner`.
   - **Admin can't ban admin.** Admins C and D both promoted by owner. C tries to ban D → 403 `cannot_ban_peer_admin`. Owner bans D → 204.
   - **Role change.** Owner promotes member B → admin → 204; B sees role on `GET /api/rooms/{r}` after refetch. Admin C tries to demote admin D → 403; owner demotes D → 204.
   - **Capacity edit.** Room with 3 members. Admin sets capacity to 2 → 400 `capacity_below_population`. Set to 5 → 200, audit row written.
   - **Room delete.** Owner `DELETE /api/rooms/{r}` → 204. `GET /api/rooms/{r}` → 404. `GET /api/rooms/mine` for any member → empty. Audit row recorded (visible via direct DB query — admin endpoint is no longer reachable since room is gone).
   - **Audit pagination.** Generate 75 audit rows. `GET /api/rooms/{r}/audit` → 50 items + `nextBefore`. Follow-up with `?before={id}` → 25 items + `nextBefore = null`.
   - **Invitation gating by RoomBan.** Ban B. A `POST /api/rooms/{r}/invitations { username: "b" }` → 403 `invitee_room_banned`. After unban → 201.
   - **Accept gating by RoomBan.** A invites B; A bans B; B `POST /api/invitations/{i}/accept` → 403 `room_banned`.
   - **Hub group eviction.** Ban B mid-session. A sends a message. SignalR test client (B's connection) does **not** receive `MessageCreated` (group removed). B's connection still receives `RoomBanned` on `user:{B}` group.
5. **Compose smoke.** Two browsers. A (owner) and B (member) in private room "engineers". A: navigate `/app/rooms/{id}/manage`, Members tab, click **Ban** on B, confirm → B's tab toasts "You were banned from engineers" and lands on `/app/rooms`; B's sidebar no longer shows engineers. A's Bans tab shows B with timestamp. A clicks **Unban** → B can re-join via catalog (visible since A also has a public test room). A promotes member C to admin → C's role badge updates on next room-detail view. A bumps capacity from 1000 → 1500 in Settings tab. A deletes the room → B (still online elsewhere) sees the room disappear from the sidebar after the `RoomDeleted` toast.

## Follow-ups for later slices

- **Slice 13.5 / polish** — implement the disabled "Banned from #room" sidebar entry (spec §6.5).
- **Slice 14 (UserBan)** — uncomment the slice-14 markers in `InvitationService` and consider whether `UserBan` should also block `ModerationService.BanAsync` (likely no — room ban is independent).
- **Slice 15 (Pagination + cleanup)** — background sweep that hard-deletes messages and attachment files for soft-deleted rooms (close the `// TODO(slice-15)` from `RoomService.DeleteAsync`).
- **Slice 16 (Hardening)** — rate-limit `POST /api/rooms/{id}/bans` and the role-change endpoint per actor.

## Critical files at a glance

- `server/ChatApp.Data/Entities/Rooms/{RoomBan,ModerationAudit}.cs`
- `server/ChatApp.Data/Configurations/Rooms/{RoomBanConfiguration,ModerationAuditConfiguration}.cs`
- `server/ChatApp.Data/Migrations/{timestamp}_AddRoomModeration.cs`
- `server/ChatApp.Data/ChatDbContext.cs` (two DbSets)
- `server/ChatApp.Domain/Services/Rooms/ModerationService.cs` (new)
- `server/ChatApp.Domain/Services/Rooms/ModerationActions.cs` (new)
- `server/ChatApp.Domain/Services/Rooms/RoomService.cs` (Join ban guard, DeleteAsync, UpdateCapacityAsync)
- `server/ChatApp.Domain/Services/Rooms/InvitationService.cs` (uncomment two RoomBan TODOs)
- `server/ChatApp.Domain/Services/Rooms/RoomsErrors.cs` (new codes)
- `server/ChatApp.Domain/Abstractions/IChatBroadcaster.cs` + `Realtime/ChatBroadcaster.cs` (three new methods)
- `server/ChatApp.Api/Controllers/Rooms/ModerationController.cs` (new)
- `server/ChatApp.Api/Controllers/Rooms/RoomsController.cs` (DELETE + PATCH capacity)
- `server/ChatApp.Api/Controllers/Rooms/RoomsErrorMapper.cs` (new mappings)
- `server/ChatApp.Api/Contracts/Rooms/{BanUserRequest, RoomBanEntry, RoomBansResponse, ChangeRoleRequest, AuditEntry, AuditResponse, UpdateCapacityRequest, RoomMemberChangedPayload, RoomBannedPayload, RoomDeletedPayload}.cs`
- `server/ChatApp.Api/Program.cs` (register `ModerationService`)
- `client/src/app/core/rooms/{moderation.service.ts, moderation.models.ts}`
- `client/src/app/core/notifications/toast.service.ts` + `client/src/app/shared/toast/toast-outlet.component.{ts,html,scss}`
- `client/src/app/features/manage-room/manage-room.component.{ts,html,scss}` plus four tab subcomponents
- `client/src/app/features/rooms/room-detail/room-detail.component.{ts,html}` (remove invite affordances; add Manage button + 95% banner + hub subscriptions)
- `client/src/app/core/signalr/signalr.service.ts` (three subjects)
- `client/src/app/core/rooms/rooms.service.ts` (delete + updateCapacity)
- `client/src/app/app.routes.ts` (manage route)
- `client/src/app/features/app-shell/app-shell.component.html` (toast outlet mount)
