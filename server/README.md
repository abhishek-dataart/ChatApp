# server/

ASP.NET Core (.NET 10) API with SignalR, EF Core 10 on PostgreSQL 16. Three-project clean architecture plus an xUnit test project.

See the [repo-root README](../README.md) for the end-to-end stack, and [`docs/specs/01-architecture.md`](../docs/specs/01-architecture.md) for the authoritative design.

---

## Projects

| Project                 | Role                                                                                                                          |
|-------------------------|-------------------------------------------------------------------------------------------------------------------------------|
| `ChatApp.Api`           | HTTP/SignalR entry point. Controllers (one folder per bounded context), hubs, middleware, `Program.cs` DI, infrastructure adapters (auth, CSRF, rate limit, attachment pipeline, presence, image thumbnailing, scanning). |
| `ChatApp.Domain`        | Pure services and abstractions — `PresenceAggregator`, `RoomPermissionService`, `AttachmentPipeline`, `UnreadService`. No EF, no ASP.NET references. |
| `ChatApp.Data`          | `ChatDbContext` (single shared context, `UseSnakeCaseNamingConvention()`), entity configurations, EF migrations, persistence services. |
| `ChatApp.Tests`         | xUnit unit + integration tests. Testcontainers for Postgres, `WebApplicationFactory` for HTTP.                               |

**Dependency direction:** `Api → Domain`, `Api → Data`, `Data → Domain`. **Never** `Domain → Data` or `Domain → Api`.

---

## Common commands

```bash
# Build
dotnet build

# Run the API (http://localhost:5175 — see launchSettings.json)
dotnet run --project ChatApp.Api

# EF migrations
dotnet ef migrations add <Name> -p ChatApp.Data -s ChatApp.Api
dotnet ef database update      -p ChatApp.Data -s ChatApp.Api

# Tests
dotnet test
dotnet test --filter "FullyQualifiedName~RoomPermission"
dotnet test --filter "Category=Integration"
```

`db.Database.Migrate()` runs on startup — single instance, no race. Do not add a separate migration step to infra.

---

## Build settings (see `Directory.Build.props`)

- `TargetFramework = net10.0`, `Nullable = enable`, `ImplicitUsings = enable`.
- `TreatWarningsAsErrors = true` — warnings break the build.
- `InvariantGlobalization = true` — never rely on culture-specific formatting; use `CultureInfo.InvariantCulture` explicitly.
- SDK pinned via `global.json` to `10.0.100` with `latestFeature` roll-forward.
- Central package management in `Directory.Packages.props` — add new packages there; csproj references go without a `Version`.

---

## Patterns to preserve

- **Bounded contexts at the service layer**, not the DbContext. Shared `ChatDbContext` is pragmatic (one migrations history). Cross-context reads flow through the owning service, not direct `_db.OtherEntities` queries.
- **REST writes, hub broadcasts.** `ChatHub` has no write methods for messages. Message create/edit/delete flows through `POST /api/chats/{scope}/{scopeId}/messages` (and siblings); the controller persists, then broadcasts `MessageCreated` / `MessageEdited` / `MessageDeleted` to `room:{id}` / `pchat:{id}` / `user:{id}` groups.
- **Two hubs only** — `PresenceHub`, `ChatHub`. Moderation events ride on `ChatHub`.
- **Sessions.** Tokens stored as `sha256(token)`, never the raw value. 30 s cache in `IMemoryCache`. SignalR re-checks on `OnConnectedAsync` so revocation is honoured on reconnect.
- **Attachments.** Two-step upload: `POST /api/attachments` returns an id with `message_id = null`; the subsequent message POST sets the FK. A background service purges unlinked rows older than 1 h. `IAttachmentScanner` is DI-selected via `ChatApp:Attachments:Scanner` (`noop` | `clamav`).
- **Pagination.** Keyset only: `WHERE (created_at, id) < (@c, @i) ORDER BY created_at DESC, id DESC LIMIT 50`. Do not introduce offset pagination on messages.
- **Forwarded headers.** The API sits behind nginx; `UseForwardedHeaders` is configured so sessions record the real client IP. Don't strip it.

---

## Configuration

All keys live under the `ChatApp:` prefix in `appsettings.json` (override via env vars using `__` as the separator).

| Key                              | Purpose                                                   | Default                   |
|----------------------------------|-----------------------------------------------------------|---------------------------|
| `ConnectionStrings:Default`      | Postgres connection string                                | —                         |
| `ChatApp:Cookie:Secure`          | `Secure` attribute on session cookie                      | `true`                    |
| `ChatApp:Cookie:SameSite`        | SameSite policy                                           | `Lax`                     |
| `ChatApp:Cookie:Domain`          | Cookie domain (omit for host-only)                        | unset                     |
| `ChatApp:Files:Root`             | Filesystem root for attachments                           | `/var/chatapp/files`      |
| `ChatApp:Attachments:MaxBytes`   | Per-file cap                                              | 20 MiB                    |
| `ChatApp:Attachments:Scanner`    | `noop` or `clamav`                                        | `noop`                    |
| `ChatApp:Attachments:ClamAv:Host`| ClamAV daemon host                                        | `clamav`                  |

Kestrel's multipart limit is raised to 22 MB in `Program.cs`; individual endpoints tighten via `[RequestFormLimits]`.

---

## Testing

- **Unit** — xUnit, no DB: permission matrices, presence aggregation, MIME-sniff rejection.
- **Integration** — Testcontainers Postgres + `WebApplicationFactory`, exercising the full HTTP pipeline.
- **Load** — k6 scripts under `server/k6/`: 300 WS clients, 1 msg / 5 s, target p95 delivery < 3 s.

Representative integration flows to keep green: register → login → create room → send message (+reply +attachment) → page back; friendship accept → DM; `UserBan` blocks DM writes; room delete purges attachment files from disk.

---

## Directory layout

```
server/
├── ChatApp.Api/          # HTTP + SignalR entry point
│   ├── Controllers/      # One folder per bounded context
│   ├── Hubs/             # PresenceHub, ChatHub
│   ├── Infrastructure/   # Auth, CSRF, rate limit, attachments, presence, scanning
│   └── Program.cs
├── ChatApp.Domain/       # Pure services & abstractions
├── ChatApp.Data/         # EF Core: DbContext, configs, migrations, persistence services
├── ChatApp.Tests/        # xUnit unit + integration
├── k6/                   # Load scripts
├── Directory.Build.props # Shared MSBuild (warnings as errors, nullable, invariant globalization)
├── Directory.Packages.props # Central package versions
├── global.json           # .NET SDK pin
└── ChatApp.sln
```
