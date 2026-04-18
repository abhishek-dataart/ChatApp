# Product Spec — Online Chat Server

## Context

The repository contains `docs/high_level_requirement.txt` — a functional/non-functional brief for a classic web chat (registration, rooms, DMs, contacts, attachments, moderation) sized for 300 concurrent users. The brief leaves several load-bearing decisions open: tech stack, session model, AFK definition, admin-vs-admin moderation semantics, ban reversibility, deleted-user handling, capacity enforcement, attachment safety, and several smaller UX details. This spec closes those gaps based on the clarifying interview, so implementation can start without further back-and-forth.

---

## 1. Tech Stack and Deployment

- **Backend**: ASP.NET Core (.NET 9) Web API + SignalR hub.
- **Frontend**: Angular (latest LTS) SPA.
- **Database**: PostgreSQL 16 (EF Core with migrations).
- **Realtime**: SignalR over WebSocket (fallback to long-polling handled by SignalR itself).
- **File storage**: local filesystem, mounted volume (`/var/chatapp/files`).
- **Antivirus**: ClamAV as a sidecar container; API calls it over TCP/clamd on every upload.
- **Deployment**: `docker-compose.yml` with services `api`, `web` (nginx serving Angular bundle + reverse-proxying `/api` and `/hub`), `db`, `clamav`.
- **Configuration**: environment variables; connection strings and secrets out of source.

### Out of scope (explicit)

Password reset, typing indicators, read receipts, link unfurls, platform super-admin, forced periodic password change, email verification, ownership transfer, localization (English only), message search.

---

## 2. Data Model (authoritative)

All IDs are `uuid` unless stated.

- **User**(`id`, `email` unique CI, `username` unique CI immutable, `display_name`, `avatar_path?`, `password_hash` (Argon2id), `created_at`, `deleted_at?`).
- **Session**(`id`, `user_id`, `cookie_hash`, `user_agent`, `ip`, `created_at`, `last_seen_at`, `revoked_at?`).
- **Friendship**(`user_id_low`, `user_id_high`, `state` = `pending|accepted`, `requester_id`, `request_note?`, `created_at`, `accepted_at?`) — one row per unordered pair.
- **UserBan**(`banner_id`, `banned_id`, `created_at`, `lifted_at?`) — reversible; active when `lifted_at` is null.
- **Room**(`id`, `name` unique CI permanent-reserved, `description`, `visibility` = `public|private`, `owner_id`, `capacity` default 1000, `created_at`, `deleted_at?`).
- **RoomMember**(`room_id`, `user_id`, `role` = `owner|admin|member`, `joined_at`).
- **RoomBan**(`room_id`, `user_id`, `banned_by_id`, `created_at`, `lifted_at?`) — reversible via "Remove from ban list".
- **RoomInvitation**(`id`, `room_id`, `invitee_id`, `inviter_id`, `state` = `pending|accepted|declined`, `created_at`).
- **PersonalChat**(`id`, `user_a_id`, `user_b_id`, `created_at`) — auto-created when a friendship transitions to `accepted`.
- **Message**(`id`, `scope` = `room|personal`, `room_id?`, `personal_chat_id?`, `author_id?` nullable on user delete, `reply_to_id?`, `body`, `edited_at?`, `deleted_at?`, `created_at`).
- **Attachment**(`id`, `message_id`, `kind` = `image|file`, `original_filename`, `stored_path`, `mime`, `size_bytes`, `comment?`, `thumb_path?`).
- **UnreadMarker**(`user_id`, `scope`, `scope_id`, `last_read_message_id`, `last_read_at`).
- **ModerationAudit**(`id`, `room_id`, `actor_id`, `target_id`, `action`, `created_at`) — backs "view who banned each banned user".

### Uniqueness & case rules

- Email and username: stored case-preserving, matched case-insensitive (`CITEXT` or unique functional index on `lower(...)`).
- Room name: same rule + permanently reserved (never purged on room deletion; soft-deleted row keeps the unique constraint).

---

## 3. Authentication & Sessions

- **Registration**: email + unique username + password. Password policy: min 10 chars, must include letter and digit. Username regex `^[a-z0-9_]{3,20}$` (case-insensitive uniqueness). No email verification.
- **Login**: email + password → HttpOnly, Secure, SameSite=Lax cookie holding opaque session id (32 bytes, base64). Server stores `sha256(cookie)` only.
- **Session lifetime**: no expiry — sessions live until explicit logout or admin action; `last_seen_at` refreshed on use.
- **Sessions screen**: list of active sessions (browser/UA, IP, last-seen); user may revoke any. Revoking current session logs this browser out. Revoking others does not affect this browser.
- **Password change** (logged-in, requires current password). **No password reset flow.**
- **Account deletion**:
  - Soft-delete user row (`deleted_at` set).
  - `display_name` and `username` hidden in UI as `[deleted user]`; username remains reserved (not reusable).
  - Rooms **owned** by the user: cascade-delete rooms, their messages, and all attachments (files removed from disk).
  - Messages authored in other rooms/personal chats: kept; author shown as `[deleted user]`.
  - Attachments they uploaded in other rooms: kept (per spec 2.6.5).
  - Friendships, room memberships, pending invitations: removed.
  - All sessions revoked.
- **Rate limits**: login — 10/min/IP + 5/min/email; API — 30 messages / 10s / user; 20 uploads / min / user.

---

## 4. Presence & AFK

- **States**: `online` | `afk` | `offline`.
- Client-side: a per-tab activity watcher listens for `mousemove`, `click`, `keydown`, `scroll`. Any of those within the last 60 s ⇒ tab is "active".
- Tab heartbeats to SignalR (`PresenceHub.Heartbeat`) every 20 s carrying `isActive`. The hub aggregates all of a user's connected tabs:
  - Any tab active ⇒ `online`.
  - All connected tabs inactive >60 s ⇒ `afk`.
  - No connected tabs ⇒ `offline`.
- Presence changes broadcast only to contacts and room-mates (not globally).
- **Target latency**: state transitions visible to peers within 2 s.

---

## 5. Contacts / Friends

- Send friend request by username, or from a room's member list; optional note (≤500 chars).
- Recipient accepts or declines. On accept, `Friendship.state = accepted` and a `PersonalChat` is auto-created.
- Remove friend: deletes the friendship. Existing `PersonalChat` is hidden from both sidebars but messages are retained (they reappear on re-friend).
- **User-to-user ban** (`UserBan`):
  - Applied by either side. Active ban terminates any existing friendship.
  - While active: no DMs, no friend requests, no room invitations either direction, no ability to send a friend request from either side's UI.
  - Existing DM history remains visible to both, frozen/read-only; no edits, no new messages, no attachment changes.
  - **Reversible** any time by the banner (set `lifted_at`). Unbanning restores messaging capability but **does not** restore the prior friendship — users must re-send a friend request.

---

## 6. Chat Rooms

### 6.1 Creation & properties

Any authenticated user can create a room. Required: `name` (unique CI permanent), `description`, `visibility`. Default `capacity` = 1000.

### 6.2 Roles

- **Owner**: permanent; cannot lose admin; only role with right to delete the room or raise capacity.
- **Admin**: can moderate (see 6.4).
- **Member**: standard.

Owner cannot leave; may only delete. No ownership transfer.

### 6.3 Joining / leaving

- Public rooms: any non-banned authenticated user joins freely.
- Private rooms: join only via accepted invitation (inviter must be admin or owner).
- Leave: free for any non-owner. Leaving removes `RoomMember` row but does not ban.
- **Capacity enforcement**: server rejects join/accept-invite if `member_count >= capacity`. Owner/admin can raise `capacity` at any time (no hard ceiling). When `member_count >= 0.95 * capacity`, owner + all admins receive an in-app notification (toast + persistent banner in Manage Room).

### 6.4 Moderation

- **Admins may**: delete any message in the room; remove (= ban) any member; view `RoomBan` list with banner attribution; remove a user from the ban list; demote another admin to member; ban another admin **except the owner**.
- **Owner may**: everything an admin can; remove/demote any admin; delete the room.
- Every moderation action writes a `ModerationAudit` row.

### 6.5 Banning from room

- Removal by admin/owner is treated as a ban (`RoomBan`).
- Banned user loses access to the room's messages, files, images.
- **Sidebar UX**: the room remains in their sidebar as a disabled "Banned from #room" entry (no chat area, no member list); click shows a small "You were banned on <date> by <username>" panel. Removed from sidebar only when they explicitly dismiss or when the room is deleted.
- Unbanning (removal from `RoomBan`) lets them rejoin public rooms freely, or be re-invited to private rooms.

### 6.6 Room deletion

Hard-deletes all messages and attachments in the room (filesystem too). Name remains reserved.

### 6.7 Catalog & search

Public rooms listed with name, description, member count. Search: case-insensitive substring match on **name only**.

### 6.8 Invitations

- Direct-to-username only. No invite links.
- Inviter must be admin or owner. Target sees it in an "Invitations" inbox and must accept/decline.
- Invitation is blocked if either side has an active `UserBan` against the other, or if target is in the room's `RoomBan`.

### 6.9 Member list visibility

Visible only to current members (public and private rooms). Public catalog shows count, not names.

---

## 7. Messaging

### 7.1 Content

- Plain text (multiline, UTF-8), up to **3 KB** per message.
- Emoji via standard Unicode picker (e.g. emoji-mart) — no custom emoji set.
- Attachments (see §8).
- `reply_to_id` reference.

### 7.2 Personal chats

DMs share all message/attachment features with rooms. No admins; moderation is N/A. A DM is sendable only if friendship is `accepted` and neither side has an active `UserBan`.

### 7.3 Edit / delete

- **Edit** own message text any time; UI shows a gray "edited" indicator. Attachments are **not** editable.
- **Delete**: author always; room admins/owner for room messages. Hard-delete (set `deleted_at`; body/attachments purged on next cleanup). No recovery.
- **Reply to a deleted message**: the quoted block renders as "message deleted" stub.

### 7.4 Ordering, history, delivery

- Messages persisted, delivered via SignalR to all connected tab-clients of recipients.
- Offline messages persist; delivered when the recipient next connects and hydrates history.
- Chronological order, stable by `created_at` + tiebreak by `id`.
- **Infinite scroll**: default page 50; API `GET /api/chats/{scope}/{id}/messages?before={id}&limit=50`.
- Target p95 delivery: ≤3 s end-to-end.
- **Unread markers** per (user, scope, scope_id) — cleared when user opens the chat.

---

## 8. Attachments

- Types: **image** (png/jpeg/gif/webp, ≤3 MB) or generic **file** (any mime, ≤20 MB).
- Upload methods: button + paste.
- Server pipeline on upload:
  1. Enforce size caps (client and server).
  2. MIME sniffing via magic bytes; reject mismatch vs claimed type.
  3. ClamAV scan via clamd TCP; reject infected.
  4. For images: generate thumbnail (longest side 512 px, JPEG quality 80) stored alongside original.
  5. Persist with original filename; stored on disk under content-addressed path `files/{yyyy}/{mm}/{uuid}{.ext}`.
- Optional `comment` per attachment.
- **Downloads**: authenticated endpoint; allowed only for current room members / DM participants. Served with `Content-Disposition: attachment`. Image preview endpoint serves thumbnail inline to current authorised users.
- Loss of room access ⇒ loss of download/preview permission. Files remain on disk unless the room is deleted.

---

## 9. Notifications

- In-UI unread badges near each room and contact entry; cleared on open.
- **Sound ping** on new message when the browser tab is not focused. User can toggle in Profile settings.
- No browser push, no email.

---

## 10. UI (Angular SPA)

Top-level routes: `/login`, `/register`, `/app` (authenticated shell).

**App shell layout** (matches wireframe in the requirements):

- Top bar: logo, Public Rooms, Private Rooms, Contacts, Sessions, Profile dropdown, Sign out.
- Right sidebar (collapsible to accordion when in a chat): Rooms (Public / Private) with unread badges, Contacts with presence dots + unread badges, Create room button.
- Center: chat header (name, description), message list with infinite scroll up, replied-to message visually quoted, "edited" gray tag.
- Bottom composer: emoji picker, attach, replying-to chip, multiline input, Send.
- Right panel: Room info, Owner/Admins, Members (paginated/scrollable, 1000 max visible), Invite user, Manage room.

**Chat behaviour**

- Auto-scroll on new message only when already at bottom.
- "N new messages" jump pill when not at bottom.

**Manage Room modal** (admin/owner): tabs Members / Admins / Banned users / Invitations / Settings — matching the wireframe, plus capacity field in Settings.

**Profile page**: email (read-only), username (read-only), display name (editable), avatar upload (≤1 MB image), change password, sound-on-message toggle.

**Sessions page**: table with UA, IP, last seen, Revoke button; highlight current session.

---

## 11. Non-Functional

- **Scale**: 300 concurrent SignalR connections; 1000 default members/room (raisable). Typical 20 rooms × 50 contacts per user.
- **Performance**: message p95 <3 s; presence p95 <2 s; 10 000-message room scrolls smoothly (server supports keyset pagination; client virtualises the list).
- **Persistence**: messages retained indefinitely.
- **Security**: Argon2id hashing, HttpOnly+Secure cookies, SameSite=Lax, CSRF token on non-GET, parameterised queries via EF Core, uploads behind auth + AV, `X-Content-Type-Options: nosniff`, strict file Content-Disposition. CSP on the web container.
- **Consistency**: all permission checks server-side on every hub call and REST call; SignalR groups map 1-1 to rooms and personal chats.
- **Logging**: structured (Serilog), audit entries for all moderation actions.

---

## 12. High-Level Architecture

- **Web container** (nginx): serves Angular static bundle; reverse-proxies `/api/*` and `/hub/*` to the api container.
- **API container** (.NET): controllers (REST) + SignalR hub(s):
  - `PresenceHub` — tab heartbeats, contact/room presence broadcasting.
  - `ChatHub` — message send/edit/delete/reply events per group (room or DM).
  - `ModerationHub` (or folded into ChatHub groups) — admin-action broadcasts.
- **DB container**: Postgres with EF Core migrations applied on api startup.
- **ClamAV container**: clamd exposed only on the internal docker network; api uploads via TCP.
- **Shared volume**: `files-data` mounted into api at `/var/chatapp/files`.

---

## 13. Critical Files to Create

- `server/ChatApp.sln`, `server/ChatApp.Api/` — ASP.NET Core project.
- `server/ChatApp.Api/Hubs/{PresenceHub,ChatHub}.cs`
- `server/ChatApp.Api/Controllers/` — `Auth`, `Users`, `Sessions`, `Friends`, `Rooms`, `Messages`, `Attachments`, `Invitations`, `Moderation`.
- `server/ChatApp.Data/` — EF Core DbContext, entities, migrations.
- `server/ChatApp.Domain/` — domain services (PresenceAggregator, RoomPermissionService, AttachmentPipeline).
- `client/` — Angular workspace (`ng new`), modules per feature (auth, app-shell, rooms, dms, contacts, sessions, profile, manage-room).
- `docker-compose.yml`, `Dockerfile.api`, `Dockerfile.web`, `nginx.conf`.
- `docs/specs/00-product-spec.md` — this spec.

---

## 14. Verification Plan

1. **Unit tests** for permission logic: room role matrix (member/admin/owner/banned), user ban matrix, capacity enforcement, private invitation gating.
2. **Integration tests** (Testcontainers + Postgres): registration → login → create room → post message → infinite scroll; friendship → DM; ban → cannot DM; unban → can re-friend.
3. **End-to-end smoke** (Playwright) on docker-compose up:
   - Two browsers register, friend, DM, see presence transitions (online → afk after 60 s idle → offline on close).
   - One user creates a public room, the other joins via catalog search, sends an image (thumbnail appears), replies to it.
   - Admin bans the member; banned entry appears; admin unbans; re-join works.
   - Capacity set to 2, third user join fails with 409; 95% banner shows at capacity=3.
4. **Load check**: `bombardier`/`k6` script pushing 300 WebSocket clients, each sending 1 message/5s, asserting p95 delivery <3 s.
5. **Security smoke**: EICAR file upload is rejected; attempt to download a room attachment after being banned returns 403.
