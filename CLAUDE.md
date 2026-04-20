# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Stack and envelope

ASP.NET Core API (.NET 10) + Angular 19 SPA + PostgreSQL 16, deployed as a single-container API behind nginx. Designed for **300 concurrent users on one API instance** — architecture is deliberately single-process; scale-out seams (`IPresenceStore`, `IMessageBus`, session cache) are kept clean but not implemented.

Canonical design docs live in `docs/specs/` — `00-product-spec.md` (scope) and `01-architecture.md` (bounded contexts, runtime model, decisions-vs-spec table). Read `01-architecture.md` before making structural changes; it records deliberate deviations (e.g. PBKDF2 not Argon2, ClamAV deferred behind `IAttachmentScanner`, REST-write / SignalR-broadcast split).

## Common commands

```bash
# Full stack via Docker (from repo root)
docker compose -f infra/docker-compose.yml up -d --build

# Server (from server/)
dotnet build
dotnet run --project ChatApp.Api
dotnet test
dotnet test --filter "FullyQualifiedName~RoomPermission"   # single test class
dotnet test --filter "Category=Integration"                # integration only

# Client (from client/)
npm install
npm start                                # ng serve, proxies /api and /hub to localhost:5175
npm run build
npm test                                 # Jest unit tests
npm run e2e                              # Playwright smoke (requires running stack)
```

`client/proxy.conf.json` forwards `/api` and `/hub` (WebSocket) to `http://localhost:5175` — the API must be running for `npm start` to work end-to-end.

## Architecture essentials

**Server projects** (`server/`, three-project clean-arch):
- `ChatApp.Api` — controllers, SignalR hubs, middleware (auth, CSRF, rate limiting), `Program.cs` DI wiring.
- `ChatApp.Domain` — pure services (`PresenceAggregator`, `RoomPermissionService`, `AttachmentPipeline`, `UnreadService`) and abstractions.
- `ChatApp.Data` — `ChatDbContext` (single shared context, snake_case naming), entity configurations, migrations, persistence-layer services. `db.Database.Migrate()` runs on startup (single instance → no race).

**Bounded contexts** live inside the API and are enforced at the service layer, not the DbContext: Identity & Sessions, Social Graph, Rooms, Messaging, Attachments, Presence. Cross-context reads go **through services, never direct entity queries**.

**Realtime**: exactly **two SignalR hubs** — `PresenceHub` and `ChatHub`. `ChatHub` exposes **no write methods** for messages; all writes are REST (`POST /api/chats/{scope}/{scopeId}/messages`), then the controller broadcasts `MessageCreated` / `MessageEdited` / `MessageDeleted` to the resolved group (`room:{id}` | `pchat:{id}` | `user:{id}`). This keeps auth / validation / rate-limit / logging on one path.

**Presence**: 20 s client heartbeat + 2 s server tick. States: active within 60 s → `online`; all connections idle → `afk`; none → `offline`. Transitions broadcast only to the union of the user's contacts and room-mates.

**Attachments**: two-step upload — `POST /api/attachments` returns an id with `message_id = null`; the subsequent message POST sets the FK. Pipeline: size cap → magic-byte MIME sniff → `IAttachmentScanner` (default `NoOpScanner`; ClamAV impl selectable via `ChatApp:Attachments:Scanner` config) → ImageSharp thumbnail (512 px, JPEG q80) → persist. A background service purges unlinked attachments older than 1 hour. Files live under `/var/chatapp/files/{yyyy}/{mm}/{uuid}{.ext}`.

**Auth**: PBKDF2 (`PasswordHasher<User>`), 32-byte cookie token stored server-side as `sha256(token)` in a `Session` row, `HttpOnly; Secure; SameSite=Lax`. Session lookup cached 30 s in `IMemoryCache`. SignalR uses the same cookie and re-checks on `OnConnectedAsync` so revocation is honoured on reconnect. CSRF: double-submit token on non-GET REST; SignalR relies on cookie + Origin check.

**Messaging pagination**: keyset — `WHERE (created_at, id) < (@c, @i) ORDER BY created_at DESC, id DESC LIMIT 50`. Client-side virtualisation via Angular CDK `cdk-virtual-scroll-viewport`.

## Client

Angular 19 standalone-components workspace. Layout:
- `src/app/core/` — auth (guard, service, interceptors for credentials/CSRF/errors), messaging services, presence, rooms, sessions, notifications, layout.
- `src/app/features/` — feature surfaces: `auth`, `app-shell`, `rooms`, `dms`, `contacts`, `sessions`, `profile`, `manage-room`.
- `src/app/shared/` — UI components and pipes.

State is Angular **Signals + feature services** (not NgRx). SignalR client lives in `core/`. REST calls target `/api`; WebSocket targets `/hub`.

## Build settings worth knowing

- `server/Directory.Build.props` sets `TreatWarningsAsErrors=true` and `Nullable=enable` across all server projects — warnings will break the build.
- `.NET SDK is pinned via `server/global.json` to `10.0.100` with `latestFeature` roll-forward.
- `InvariantGlobalization=true` — don't rely on culture-specific formatting server-side.
- Kestrel multipart limit is raised to 22 MB in `Program.cs`; per-endpoint `[RequestFormLimits]` attributes enforce tighter caps.

## Testing strategy (per architecture spec)

- **Unit** (xUnit, no DB): permission matrices, presence aggregation, MIME-sniff rejection.
- **Integration**: Testcontainers Postgres + `WebApplicationFactory` — exercise full HTTP pipeline.
- **E2E smoke**: Playwright against `docker compose up`.
- **Load**: k6, 300 WS clients, 1 msg/5 s, p95 delivery < 3 s. Scripts in `server/k6/`.
- **Client**: Jest (`npm test`) + Playwright (`npm run e2e`) in `client/`.
