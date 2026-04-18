# Architecture — Online Chat Server

## Context

The product spec (`00-product-spec.md`) fixes the functional scope and the top-level technology choices. This document fills in the architectural shape below that: bounded contexts, process topology, runtime model, and the decisions that deviate from the spec after an MVP-vs-over-engineering pass.

The target envelope is **300 concurrent users on a single API container**. Everything here is optimised for that envelope; the "Scale-out path" section lists the interfaces kept clean so a later multi-instance deployment (Redis backplane + distributed presence) is mechanical rather than structural.

## Decisions vs. spec

| Area | Spec said | Architecture says | Why |
|---|---|---|---|
| Projects | 3-project clean arch | 3 projects (kept) | Preserve layer discipline. |
| SignalR hubs | Presence + Chat + possibly Moderation | **2 hubs: `PresenceHub`, `ChatHub`** | Moderation events are just state changes on rooms — fold into `ChatHub` groups. |
| ClamAV | Sidecar, blocking scan per upload | **Deferred.** `IAttachmentScanner` interface with no-op impl | AV sidecar (~1 GB signatures, freshclam schedule, extra failure mode) is unjustified at MVP scale; the hook is preserved so a real scanner drops in later. |
| Password hash | Argon2id (Konscious) | **ASP.NET Core `PasswordHasher` (PBKDF2)** | Zero deps, audited, adequate at this scale. |
| Sessions | Custom table + `sha256(cookie)` | Kept | Sessions screen (list UA/IP/last-seen + revoke) requires server-side state. |
| Presence | 20s heartbeat + server aggregation | Kept | Deterministic, matches spec. |
| Image thumbs | 512px longest side, JPEG q80 | Same, via **SixLabors.ImageSharp** | Pure managed; no native Docker deps. |
| Scale-out | not stated | **Single instance**; interfaces ready for Redis | Matches load target; avoids premature infra. |
| Message send | not stated | **REST POST** then hub broadcasts | Single write path; auth / validation / rate-limit / logging land once. |
| Angular state | not stated | **Signals + feature services** | Lower ceremony; Angular 17+ idiom. |
| EF migrations | not stated | `db.Database.Migrate()` on startup | Single instance → no race. |
| Rate limits | Spec values unchanged | `AddRateLimiter` for REST; token bucket for hub | Built-in covers the important cases because message-send is REST. |

## Solution layout

```
server/
  ChatApp.sln
  ChatApp.Api/              controllers, hubs, middleware, Program.cs, appsettings
    Controllers/            Auth, Users, Sessions, Friends, Rooms, Messages,
                            Attachments, Invitations, Moderation
    Hubs/                   PresenceHub.cs, ChatHub.cs
    Infrastructure/         auth, rate-limiting, CSRF, error handling
  ChatApp.Domain/           pure services where practical
                            PresenceAggregator, RoomPermissionService,
                            AttachmentPipeline, UnreadService
  ChatApp.Data/             ChatDbContext, entity configs, migrations
  ChatApp.Tests/            xUnit + Testcontainers(Postgres) + WebApplicationFactory

client/                     Angular workspace
  src/app/
    core/                   auth guard, http interceptor, signalr client
    features/auth, app-shell, rooms, dms, contacts, sessions, profile, manage-room
    shared/                 ui components, pipes

infra/
  docker-compose.yml        services: api, web, db  (no clamav for MVP)
  Dockerfile.api
  Dockerfile.web
  nginx.conf
```

## Bounded contexts

Six bounded contexts inside the API, two cross-cutting surfaces (Identity authn, Realtime delivery), and two external dependencies (Postgres, Filesystem).

```mermaid
flowchart TB
  subgraph Client["Angular SPA"]
    UI[Feature modules<br/>auth, rooms, dms, contacts,<br/>sessions, profile, manage-room]
    RT[SignalR client]
  end

  subgraph API["ASP.NET Core API — single container"]
    direction TB

    subgraph Identity["Identity & Sessions"]
      IdSvc[AuthService<br/>SessionService<br/>ProfileService]
    end

    subgraph Social["Social Graph"]
      SocSvc[FriendshipService<br/>UserBanService<br/>PersonalChatService]
    end

    subgraph Rooms["Rooms"]
      RoomSvc[RoomService<br/>MembershipService<br/>InvitationService<br/>RoomModerationService]
    end

    subgraph Messaging["Messaging"]
      MsgSvc[MessageService<br/>UnreadService]
    end

    subgraph Attachments["Attachments"]
      AttSvc[AttachmentPipeline<br/>ImageSharp thumbnailer<br/>IAttachmentScanner<br/>&nbsp;&nbsp;NoOpScanner default]
    end

    subgraph Presence["Presence"]
      PresSvc[PresenceAggregator<br/>2s tick recompute]
    end

    subgraph Realtime["Realtime — cross-cutting"]
      PHub[PresenceHub]
      CHub[ChatHub]
    end
  end

  subgraph Infra["External"]
    DB[(PostgreSQL 16)]
    FS[/Files volume<br>/var/chatapp/files/]
  end

  UI -->|REST /api| Identity
  UI -->|REST /api| Social
  UI -->|REST /api| Rooms
  UI -->|REST /api| Messaging
  UI -->|REST /api/attachments| Attachments
  RT <-->|WebSocket /hub| Realtime

  Social -. friendship accepted .-> Messaging
  Rooms  -. room created .-> Messaging
  Messaging --> Attachments
  Messaging -->|broadcast to group| CHub
  Rooms -->|moderation events| CHub
  Presence --> PHub
  Social -->|contact fan-out list| PHub
  Rooms  -->|room-member fan-out list| PHub

  Identity -. authn .-> Social
  Identity -. authn .-> Rooms
  Identity -. authn .-> Messaging
  Identity -. authn .-> Attachments
  Identity -. authn .-> Realtime

  Identity --> DB
  Social --> DB
  Rooms --> DB
  Messaging --> DB
  Attachments --> DB
  Attachments --> FS
```

### Context responsibilities

**Identity & Sessions** — registration, login, logout, password change, session list/revoke, profile (display name, avatar, sound toggle), account soft-delete. Owns `User`, `Session`. Provides `ICurrentUser` to every other context for authz.

**Social Graph** — friend requests, friendship lifecycle, user-to-user bans, personal chats. Owns `Friendship`, `UserBan`, `PersonalChat`. Accepting a friendship auto-creates the `PersonalChat`. Active `UserBan` is consulted by Messaging on DM writes and by Rooms on invitations.

**Rooms** — room CRUD, membership, invitations, room bans, moderation, capacity enforcement, catalog/search, audit log. Owns `Room`, `RoomMember`, `RoomBan`, `RoomInvitation`, `ModerationAudit`. Authoritative source for permission checks on room-scoped messaging.

**Messaging** — room + personal messages, edit/delete, replies, pagination, unread markers. Owns `Message`, `UnreadMarker`. Scope resolution (`room` vs `personal`) delegates permission to Rooms or Social respectively.

**Attachments** — upload pipeline (size cap → magic-byte MIME sniff → `IAttachmentScanner` hook → thumbnail via ImageSharp → persist), authorised download/preview endpoints. Owns `Attachment` and filesystem layout under `/var/chatapp/files/{yyyy}/{mm}/{uuid}{.ext}`.

**Presence** — heartbeat ingestion, per-user state aggregation across tabs, fan-out list resolution. Publishes `online | afk | offline` transitions via PresenceHub, broadcast only to the target user's contacts and room-mates.

**Realtime (cross-cutting)** — two SignalR hubs:
- `PresenceHub`: client → `Heartbeat(isActive)`; server → `PresenceChanged(userId, state)`.
- `ChatHub`: server → `MessageCreated`, `MessageEdited`, `MessageDeleted`, `RoomMemberChanged`, `RoomBanned`, `ModerationAction`, `UnreadChanged`. Groups: `room:{roomId}`, `pchat:{personalChatId}`, `user:{userId}`. Hub exposes **no** write methods for messages; writes go through REST.

## Runtime model

### HTTP + WebSocket topology

```
Angular SPA ──► nginx ──► ASP.NET Core ──► Postgres
                              │
                              └── Filesystem (files volume)

WebSocket: SPA ──► nginx (upgrade) ──► ASP.NET Core (PresenceHub | ChatHub)
```

One process owns authoritative state. SignalR groups are in-process and are the source of truth for fan-out. No backplane.

### Message send sequence

1. Client `POST /api/chats/{scope}/{scopeId}/messages` with `{ body, reply_to_id?, attachment_ids? }`.
2. Controller: authz via Rooms (room scope) or Social (personal scope); rate limit; validate body ≤ 3 KB; verify attachments are owned by the caller and still unlinked.
3. Insert `Message`; link attachments (FK flip).
4. Resolve SignalR group (`room:{id}` or `pchat:{id}`); broadcast `MessageCreated` with the persisted row.
5. `UnreadService` increments `UnreadMarker` for all other recipients and emits `UnreadChanged` to each recipient's `user:{id}` group.

Edit and delete follow the same shape with `MessageEdited` / `MessageDeleted`.

### Attachment flow

1. `POST /api/attachments` (multipart). Kestrel enforces size limits.
2. Magic-byte sniff; reject if the claimed kind doesn't match bytes.
3. `IAttachmentScanner.ScanAsync(stream)` — `NoOpScanner` default; future `ClamAvScanner` impl registered via DI without changing the pipeline.
4. For images: ImageSharp resize to longest-side 512, JPEG q80, saved as `{stored_path}.thumb.jpg`.
5. Persist `Attachment` with `message_id = null` (unattached). Return id.
6. Client includes `attachment_ids` in the subsequent `POST messages` call, which sets the FK.
7. Background service purges unlinked attachments older than 1 hour.

### Presence flow

- Tab connects `PresenceHub` → `OnConnectedAsync` adds the connection to `user:{userId}` and registers it with `PresenceAggregator`.
- Tab calls `Heartbeat(isActive)` every 20 s. Aggregator updates per-connection `lastActiveAt` and `isActive`.
- A 2 s tick recomputes user state: any connection active within 60 s → `online`; connections exist but all inactive → `afk`; no connections → `offline`. Transitions broadcast to the union of the user's contacts and every room they belong to.

### Auth flow

- Login → PBKDF2 compare → generate 32-byte token → store `sha256(token)` in `Session` → set `HttpOnly; Secure; SameSite=Lax` cookie.
- Middleware on every request: hash cookie → look up `Session` (cached 30 s in `IMemoryCache`) → attach `ClaimsPrincipal`. `last_seen_at` updated fire-and-forget.
- SignalR uses the same cookie; `OnConnectedAsync` re-checks the session so revocation is honoured on next reconnect.
- CSRF: double-submit token on non-GET REST. SignalR is cookie + Origin checked.

## Data ownership

Single shared `ChatDbContext` for pragmatic reasons (one migrations history). Context boundaries are enforced at the **service layer**, not the DbContext — cross-context reads go through services, not direct entity queries.

| Tables | Owning context |
|---|---|
| `users`, `sessions` | Identity |
| `friendships`, `user_bans`, `personal_chats` | Social |
| `rooms`, `room_members`, `room_bans`, `room_invitations`, `moderation_audit` | Rooms |
| `messages`, `unread_markers` | Messaging |
| `attachments` | Attachments |

## Non-functional notes

- **Security**: PBKDF2 password hashing, HttpOnly+Secure cookies, SameSite=Lax, double-submit CSRF on non-GET, EF Core parameterised queries, authn on all upload/download endpoints, magic-byte MIME sniff, `X-Content-Type-Options: nosniff`, `Content-Disposition: attachment` on file downloads, CSP on web container, rate limits per spec.
- **Observability**: Serilog structured logging → stdout. All moderation writes a `ModerationAudit` row. SignalR connect/disconnect logged at Information.
- **Performance**: keyset pagination on messages (`WHERE (created_at, id) < (@c, @i) ORDER BY created_at DESC, id DESC LIMIT 50`). Client virtualises via Angular CDK `cdk-virtual-scroll-viewport`. Targets: message p95 < 3 s, presence p95 < 2 s.
- **Persistence**: indefinite message retention. Files removed only on room hard-delete or attachment row purge.

## Scale-out path (future, not MVP)

Interfaces kept clean so the swap is mechanical:
- `IPresenceStore` — in-proc `ConcurrentDictionary` now; Redis later.
- `IMessageBus` for SignalR fan-out — `IHubContext` now; Redis backplane (`AddStackExchangeRedis`) later.
- Session cache — `IMemoryCache` now; `IDistributedCache` (Redis) later.

With those three swapped, multiple API containers can sit behind nginx without further code changes.

## Verification

1. **Unit tests** (xUnit, no DB): room permission matrix (member / admin / owner / banned), user-ban matrix, capacity enforcement, invitation gating, presence aggregation logic, attachment MIME-sniff rejection.
2. **Integration tests** (Testcontainers Postgres + `WebApplicationFactory`): register → login → create room → send message with reply + attachment → page back; friendship accept → DM; UserBan → DM blocked; unban does **not** re-friend; room delete purges attachments from disk.
3. **E2E smoke** (Playwright on `docker compose up`): two-browser friend + DM, presence `online → afk → offline`, public-room create / catalog search / join, image thumbnail render, admin ban + unban, capacity-full returns 409, 95 % banner appears at threshold.
4. **Load** (`k6`): 300 WS clients, 1 msg / 5 s each; assert p95 delivery < 3 s.
5. **Security smoke**: attempt to download a room attachment after being banned (expect 403); register a fake `IAttachmentScanner` that always rejects, confirm upload returns 422 — proves the hook is wired.

## Critical files to create / touch

- `server/ChatApp.sln`
- `server/ChatApp.Api/Program.cs` — DI wiring, rate limiter, auth, SignalR, `IAttachmentScanner` = `NoOpScanner`
- `server/ChatApp.Api/Hubs/{PresenceHub,ChatHub}.cs`
- `server/ChatApp.Api/Controllers/*` (one per context)
- `server/ChatApp.Domain/Services/{PresenceAggregator,RoomPermissionService,AttachmentPipeline,UnreadService}.cs`
- `server/ChatApp.Data/ChatDbContext.cs`, entity configs, initial migration
- `client/src/app/core/signalr.service.ts`, `core/auth.*`, `features/**`
- `infra/docker-compose.yml` (api, web, db — **no clamav**), `Dockerfile.api`, `Dockerfile.web`, `nginx.conf`
