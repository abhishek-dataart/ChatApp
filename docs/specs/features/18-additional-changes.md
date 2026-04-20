# 18 — Additional changes from requirements audit

## Context

Audit of `docs/requirements/requirement.md` against the current implementation surfaced four gaps. Password reset (req 2.1.4) is intentionally out of scope for this spec. The remaining three gaps are covered here:

1. **Account deletion** (req 2.1.5) — no endpoint or UI exists.
2. **Top menu split** (req 4.1 / wireframe A.3) — single "Rooms" link; spec requires distinct "Public Rooms" and "Private Rooms" links.
3. **Manage Room dialog tab structure** (req 4.5 / wireframe A.4) — missing dedicated `Admins` and `Invitations` tabs; promote/demote is inside Members, invites are inlined in Settings.

Intended outcome: ship the Delete Account feature with full cascade, reshape the rooms navigation to match the wireframe, and refactor the Manage Room dialog tabs to match the spec.

---

## 1. Account Deletion

**Spec rules (2.1.5):** hard-delete the user; delete rooms they own (and all messages/files/images in those rooms permanently); remove memberships in other rooms; existing messages authored by the user in rooms they do NOT own remain visible, attributed to "Deleted user".

### Server changes

**Entity / config updates** (`server/ChatApp.Data/...`):

- `Entities/Messaging/Message.cs` + `Configurations/Messaging/MessageConfiguration.cs` — make `AuthorId` nullable; FK `OnDelete(DeleteBehavior.SetNull)`. Add a migration. On serialization, when `AuthorId` is null, surface author as `"Deleted user"` with a sentinel id.
- `Configurations/Rooms/RoomMemberConfiguration.cs` — change `UserId` FK from `Restrict` to `Cascade` (memberships of other rooms evaporate on user delete).
- `Configurations/Rooms/RoomConfiguration.cs` — add explicit `OwnerId` FK behavior `Restrict` (we delete owned rooms manually first; DB should refuse orphaning).
- Verify/set cascade on: `Friendship` (both sides), `UserBan` (user→user), `RoomBan.UserId` + `RoomBan.BannedById`, `RoomInvitation.InviterId`/`InviteeId`, `Session.UserId` (already Cascade), `Attachment.UploaderId` (SetNull — blob persists per req 2.6.5), `MessageRead` / unread markers if present.

**Domain service** — new `server/ChatApp.Data/Services/Identity/AccountDeletionService.cs` (`IAccountDeletionService`):

- `DeleteAccountAsync(userId, passwordConfirmation, ct)`
  1. Reverify password via existing `IPasswordHasher` pattern used in `AuthController.ChangePasswordAsync`.
  2. Transaction:
     - Load all rooms where `OwnerId == userId` and `DeletedAt == null`.
     - For each owned room, hard-purge: delete messages + attachments and remove on-disk attachment files (addresses the existing TODO in `RoomService.DeleteAsync`).
     - Attachment files live on local FS — resolve via `AttachmentsOptions.StoragePath`; delete each file after DB purge.
     - Revoke all sessions for the user.
     - Hard-delete the `User` row; DB cascades handle memberships, friendships, user-bans, invitations, sessions; `Message.AuthorId` becomes null via SetNull.
  3. Broadcast SignalR events: `RoomDeleted` for each purged room; `UserDeleted` on ChatHub so other clients can update presence/contacts.

**Controller** — extend `server/ChatApp.Api/Controllers/Users/ProfileController.cs`:

- `DELETE /api/profile` → body `{ password: string }`. Returns 204; the auth cookie for the current session is cleared.

### Client changes

- `client/src/app/core/profile/profile.service.ts` — add `deleteAccount(password)`.
- `client/src/app/features/profile/profile.component.*` — add a "Delete account" card with destructive styling; confirmation modal requiring password re-entry and typed confirmation (e.g., type the username). On success, clear auth state and navigate to `/login`.
- Message rendering — where author is null, render `"Deleted user"` with muted styling across message list, replies, quoted messages, audit log entries, members panel.

### Verification

- Unit tests (xUnit, no DB): password mismatch rejects; cascade logic for `AccountDeletionService`.
- Integration test (Testcontainers): user A owns R1 with messages + attachments; member of B's R2 with own messages; friend of C; sessions on two devices. `DELETE /api/profile`. Assert: R1 gone, R1 attachments gone from disk, R2 still exists with A's messages attributed to "Deleted user", friendship gone, both sessions revoked, cannot log in as A.
- Manual: upload an attachment in an owned room, delete account, confirm file removed from `data/attachments/`.

---

## 2. Split "Rooms" into Public Rooms and Private Rooms

### Client changes

- `client/src/app/app.routes.ts`:
  - `/app/rooms/public` → new `PublicRoomsComponent` (catalog + search).
  - `/app/rooms/private` → new `PrivateRoomsComponent` (user's private rooms + pending invitations received).
  - Redirect `/app/rooms` → `/app/rooms/public`.
- Split `client/src/app/features/rooms/rooms-list/rooms-list.component.ts` into the two components above. Reuse existing `RoomsService.catalog` (filter `visibility === 'public'`) and `RoomsService.mine` (filter `'private'`). Pending invitations via `InvitationsService.listIncoming()` (add if missing).
- `client/src/app/features/app-shell/app-shell.component.html` — replace the single "Rooms" link with two: `Public Rooms` → `/app/rooms/public`, `Private Rooms` → `/app/rooms/private`. Order per wireframe A.3: `Public Rooms | Private Rooms | Contacts | Sessions | Profile | Sign out`.
- Sidebar accordion (existing) continues to separate public/private groups — no change.

### Server changes

None required; `CatalogEntry.visibility` already ships the flag.

### Verification

- Navigate to `/app/rooms` → redirects to public; both links work; search functions on public page; private page shows only private memberships + invitations.

---

## 3. Manage Room — Admins & Invitations tabs

### Client changes

`client/src/app/features/manage-room/manage-room.component.ts`:

- Extend `Tab` type to `'members' | 'admins' | 'bans' | 'invitations' | 'audit' | 'settings'`.
- Render order per wireframe: **Members | Admins | Banned users | Invitations | Settings** (Audit kept at end as an extra).
- Add two new tab buttons + two new `<section>` blocks loading the new components.

**New** `manage-room/admins-tab/admins-tab.component.ts`:

- Lists owner (immutable) + admins; offers `Remove admin` (reuses `ModerationService.changeRole`).
- "Make admin" panel listing non-admin members.
- Promote/demote logic moves entirely out of `members-tab`.

**Update** `members-tab` — show only Ban / Remove-from-room actions (strip promote/demote bits).

**New** `manage-room/invitations-tab/invitations-tab.component.ts`:

- Extract invitation UI out of `settings-tab` (`showInviteDialog`, `pendingInvitations`, `openInviteDialog`, `onInvited`, `loadPending`).
- Reuses `InviteUserDialogComponent` and `InvitationsService.send/listOutgoing/revoke`.

**Update** `settings-tab` — trim to room name, description, visibility, delete-room (matches wireframe A.4).

### Server changes

None required; existing moderation + invitations endpoints suffice.

### Verification

- Manual: as owner, all five tabs render; promote/demote only in Admins; owner immutable; invitations only in Invitations; Settings no longer has invitation UI.

---

## Critical files

Server:
- `server/ChatApp.Data/Entities/Messaging/Message.cs`
- `server/ChatApp.Data/Configurations/Messaging/MessageConfiguration.cs`
- `server/ChatApp.Data/Configurations/Rooms/RoomMemberConfiguration.cs`
- `server/ChatApp.Data/Configurations/Rooms/RoomConfiguration.cs` (+ other FK configs above)
- `server/ChatApp.Data/Services/Identity/AccountDeletionService.cs` (new)
- `server/ChatApp.Data/Services/Rooms/RoomService.cs` (hard-purge path)
- `server/ChatApp.Api/Controllers/Users/ProfileController.cs`
- new EF migration

Client:
- `client/src/app/app.routes.ts`
- `client/src/app/features/app-shell/app-shell.component.html`
- `client/src/app/features/rooms/rooms-list/` → split into `public-rooms/` and `private-rooms/`
- `client/src/app/features/manage-room/manage-room.component.{ts,html}`
- `client/src/app/features/manage-room/admins-tab/` (new)
- `client/src/app/features/manage-room/invitations-tab/` (new)
- `client/src/app/features/manage-room/members-tab/members-tab.component.ts` (strip promote/demote)
- `client/src/app/features/manage-room/settings-tab/settings-tab.component.{ts,html}` (strip invitation UI)
- `client/src/app/features/profile/profile.component.{ts,html}` + `client/src/app/core/profile/profile.service.ts`
- `client/src/app/core/rooms/invitations.service.ts` (add `listIncoming` if absent)

## End-to-end verification

1. `docker compose -f infra/docker-compose.yml up -d --build`.
2. `dotnet test server/ChatApp.sln` — new unit + integration tests for `AccountDeletionService` and migration.
3. `npm --prefix client test` — component tests for split routes and new tabs.
4. Manual smoke:
   - Register A, create owned public room, upload image, invite B, post messages in B's room.
   - As A, delete account. Verify cookie cleared, redirect to login, cannot log back in.
   - As B: A's owned room gone (SignalR event delivered), messages in B's room persist as "Deleted user", attachment file gone from `data/attachments/`, friendship removed.
   - Top-menu shows Public Rooms / Private Rooms; both render.
   - Manage Room: 5 tabs in order Members / Admins / Banned users / Invitations / Settings; promote/demote only in Admins; invitations only in Invitations tab.
