# ChatApp

A real-time team-chat application built for **300 concurrent users on a single API instance**. ASP.NET Core (.NET 10) API with SignalR, Angular 19 SPA, PostgreSQL 16, deployed as a Docker Compose stack behind nginx.

Delivered against the hackathon brief in [`docs/requirements/requirement.md`](docs/requirements/requirement.md).

> **Authoritative design docs:** [`docs/specs/00-product-spec.md`](docs/specs/00-product-spec.md) (scope) and [`docs/specs/01-architecture.md`](docs/specs/01-architecture.md) (runtime model, bounded contexts, deliberate deviations). Read the architecture doc before making structural changes.

---

## Features

- **Identity** — self-registration (email + unique username + password), sign-in/out, password reset + change, account deletion with owned-room cascade. Unique email & username; immutable username.
- **Sessions** — persistent cookie login across browser restarts; per-session listing with browser/IP and selective logout.
- **Contacts / friends** — friend requests (by username or from a room's member list), accept/decline, unfriend, user-to-user ban freezing DM history read-only.
- **Rooms & DMs** — public/private rooms, unique names, catalog with search; owner/admin roles, member & ban management, invites for private rooms; 1:1 DMs gated by friendship + ban state.
- **Messaging** — UTF-8 text (3 KB cap) with multiline, emoji, replies, edit (shows "edited"), delete (author or admin); infinite scroll over persistent history; unread indicators cleared on open.
- **Attachments** — images + arbitrary files (upload button or paste), 20 MB / 3 MB caps, original filename + optional comment preserved; access tied to current room membership.
- **Realtime** — SignalR messaging with keyset-paginated history, CDK virtual scrolling; `online` / `afk` / `offline` presence from 20 s heartbeats + 2 s server tick, fanned out only to contacts and room-mates.
- **Security** — PBKDF2 hashing, `HttpOnly; Secure; SameSite=Lax` cookie, server-side session table keyed by `sha256(token)`, per-session revocation; double-submit CSRF on non-GET REST; SignalR cookie + `Origin` check; magic-byte MIME sniff and pluggable scanner (`NoOp` / ClamAV) on uploads.

---

## Architecture at a glance

```
Browser ──► web (nginx :80) ──► api (kestrel :8080) ──► db (postgres :5432)
                                   │
                                   ├─ /var/chatapp/files   (bind mount: data/files)
                                   └─ clamav (:3310, optional)
```

- **REST writes, hub broadcasts.** `ChatHub` exposes no write methods — all message create/edit/delete flows through `POST /api/chats/{scope}/{scopeId}/messages`; the controller then broadcasts to `room:{id}` / `pchat:{id}` / `user:{id}` groups. One path for auth, validation, rate-limiting, and logging.
- **Two hubs only** — `PresenceHub` and `ChatHub`.
- **Bounded contexts** (Identity & Sessions, Social Graph, Rooms, Messaging, Attachments, Presence) are enforced at the service layer, not the DbContext. Cross-context reads go through services.
- **Single-process by design.** Scale-out seams (`IPresenceStore`, `IMessageBus`, session cache) are abstracted but not implemented. See [`infra/README.md`](infra/README.md#scale-out).

---

## Repository layout

| Path        | What lives there                                                                     |
|-------------|--------------------------------------------------------------------------------------|
| `server/`   | ASP.NET Core API + Domain + Data projects and xUnit tests. See [`server/README.md`](server/README.md). |
| `client/`   | Angular 19 SPA (standalone components, Signals). See [`client/README.md`](client/README.md).             |
| `infra/`    | Dockerfiles, `docker-compose.yml`, nginx config. See [`infra/README.md`](infra/README.md).               |
| `docs/`     | Product spec, architecture spec, design notes, bug log. See [`docs/README.md`](docs/README.md).          |
| `data/`     | Host-mounted volumes for dev (Postgres, uploaded files). Git-ignored.                |

---

## Quick start — Docker Compose (recommended)

```bash
# From repo root
cp infra/.env.example infra/.env   # adjust POSTGRES_PASSWORD, WEB_PORT, etc.
docker compose -f infra/docker-compose.yml up -d --build

# Follow API logs
docker compose -f infra/docker-compose.yml logs -f api

# Teardown (keeps volumes)
docker compose -f infra/docker-compose.yml down
```

Open <http://localhost:${WEB_PORT:-8080}>. The API auto-runs EF migrations on startup.

## Quick start — local development

Two terminals:

```bash
# Terminal 1 — API on :5175
cd server
dotnet run --project ChatApp.Api

# Terminal 2 — SPA on :4200, proxies /api and /hub to :5175
cd client
npm install
npm start
```

You'll need a local Postgres reachable via `ConnectionStrings:Default`. The easiest route is `docker compose -f infra/docker-compose.yml up -d db` and point the API's user-secrets or `appsettings.Development.json` at `localhost:5432`.

---

## Prerequisites

| Tool             | Version                                                        |
|------------------|----------------------------------------------------------------|
| .NET SDK         | **10.0.100** (pinned in `server/global.json`, `latestFeature`) |
| Node.js          | 20 LTS or newer                                                |
| Docker + Compose | 24+                                                            |
| PostgreSQL       | 16 (containerised by default)                                  |

---

## Testing

| Layer        | Stack                                                                 | Command                                   |
|--------------|-----------------------------------------------------------------------|-------------------------------------------|
| Unit         | xUnit (no DB) — permissions, presence, MIME sniff                     | `dotnet test` *(from `server/`)*          |
| Integration  | Testcontainers Postgres + `WebApplicationFactory`                     | `dotnet test --filter Category=Integration` |
| Client unit  | Jest + `jest-preset-angular`                                          | `npm test` *(from `client/`)*             |
| E2E smoke    | Playwright against `docker compose up`                                | `npm run e2e` *(from `client/`)*          |
| Load         | k6 — 300 WS clients, 1 msg / 5 s, p95 delivery < 3 s                  | See `server/k6/`                          |

---

## Configuration

All server settings sit under the `ChatApp:` prefix in `appsettings.json` and can be overridden by env vars (`ChatApp__Cookie__Secure=true`):

| Key                              | Purpose                                                  |
|----------------------------------|----------------------------------------------------------|
| `ConnectionStrings:Default`      | Postgres connection string                               |
| `ChatApp:Cookie:*`               | Cookie policy — `Secure`, `SameSite`, `Domain`           |
| `ChatApp:Files:Root`             | Attachment root (default `/var/chatapp/files`)           |
| `ChatApp:Attachments:Scanner`    | `noop` or `clamav`                                       |
| `ChatApp:Attachments:MaxBytes`   | Per-file upload cap                                      |

See [`infra/README.md`](infra/README.md) for Compose-level overrides and [`server/README.md`](server/README.md) for the full list.

---

## Security model (short version)

- Passwords: PBKDF2 via `PasswordHasher<User>`.
- Sessions: 32-byte cookie; only `sha256(token)` is persisted. 30 s session cache in `IMemoryCache`.
- CSRF: double-submit on non-GET REST; SignalR validates cookie + `Origin`.
- Headers: strict CSP, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy` — set at the nginx edge; API duplicates are hidden to avoid doubling.
- Attachments: size cap → magic-byte sniff → scanner → thumbnail → persist. Files outside the declared MIME/byte signature set are rejected before touching disk.

Full threat model and decisions-vs-spec table in [`docs/specs/01-architecture.md`](docs/specs/01-architecture.md).

---

## Contributing

1. Read `docs/specs/01-architecture.md` and the `CLAUDE.md` in the folder you're touching.
2. `server/Directory.Build.props` sets `TreatWarningsAsErrors=true` — warnings break the build.
3. `client/tsconfig.json` is strict — don't loosen it to silence errors.
4. Add migrations via `dotnet ef migrations add <Name> -p ChatApp.Data -s ChatApp.Api`.
5. One PR per bounded-context change; keep cross-context leaks out of the DbContext.

---

## Requirements traceability

The hackathon brief in [`docs/requirements/requirement.md`](docs/requirements/requirement.md) maps onto the eighteen feature slices under [`docs/specs/features/`](docs/specs/features/) — see the table in [`docs/README.md`](docs/README.md) for the index. Deliberate deviations (e.g. PBKDF2 rather than Argon2, ClamAV deferred behind `IAttachmentScanner`) are recorded in the "decisions vs spec" table in [`docs/specs/01-architecture.md`](docs/specs/01-architecture.md).
