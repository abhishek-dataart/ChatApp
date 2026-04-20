# CLAUDE.md — server/

ASP.NET Core (.NET 10) API. See repo-root `CLAUDE.md` for the cross-cutting architecture. This file covers conventions specific to the server projects.

## Projects

- `ChatApp.Api` — HTTP entry point. Controllers (one folder per bounded context), SignalR hubs, `Program.cs` DI wiring, `Infrastructure/` (auth, CSRF, rate limiting, attachment pipeline, presence adapters, image thumbnailing, scanning).
- `ChatApp.Domain` — pure services and abstractions. No EF, no ASP.NET. Examples: `PresenceAggregator`, `RoomPermissionService`, `AttachmentPipeline`, `UnreadService`. Do not take a dependency on `ChatApp.Data` from here.
- `ChatApp.Data` — `ChatDbContext`, entity configurations, migrations, persistence services (`Services/{Identity,Messaging,Presence,Rooms,Social,Attachments}`). Snake-case naming convention via `UseSnakeCaseNamingConvention()`.

Dependency direction: `Api → Domain`, `Api → Data`, `Data → Domain`. **Never** `Domain → Data` or `Domain → Api`.

## Common commands

```bash
dotnet build                                       # from server/
dotnet run --project ChatApp.Api                   # listens on http://localhost:5175 (see launchSettings)
dotnet ef migrations add <Name> -p ChatApp.Data -s ChatApp.Api
dotnet ef database update   -p ChatApp.Data -s ChatApp.Api
dotnet test
dotnet test --filter "FullyQualifiedName~RoomPermission"
dotnet test --filter "Category=Integration"
```

On startup the API runs `db.Database.Migrate()` — single instance, no race. Do not add a separate migration step to infra.

## Build settings (from `Directory.Build.props`)

- `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`.
- `TreatWarningsAsErrors=true` — warnings **will** break the build.
- `InvariantGlobalization=true` — don't rely on culture-specific formatting (parse/format with `CultureInfo.InvariantCulture` where explicit).
- SDK pinned via `global.json` to `10.0.100` with `latestFeature` roll-forward.
- Central package management via `Directory.Packages.props` — add new packages there, reference without version in csproj.

## Patterns worth preserving

- **Bounded contexts enforced at the service layer**, not the DbContext. Shared `ChatDbContext` is pragmatic (one migrations history). Cross-context reads go through the owning service, not direct `_db.OtherEntities` queries.
- **REST writes, hub broadcasts.** `ChatHub` has no write methods for messages. Message create/edit/delete flows through `POST /api/chats/{scope}/{scopeId}/messages` (and siblings); the controller persists, then broadcasts `MessageCreated` / `MessageEdited` / `MessageDeleted` to `room:{id}` | `pchat:{id}` | `user:{id}` groups. Do not add hub-side writes.
- **Two hubs only**: `PresenceHub`, `ChatHub`. Moderation events ride on `ChatHub`.
- **Sessions**: token stored as `sha256(token)`, never the raw token. Lookup is cached 30 s in `IMemoryCache`. SignalR re-checks the session on `OnConnectedAsync` so revocations take effect on reconnect.
- **Attachments**: two-step upload — `POST /api/attachments` returns an id with `message_id = null`; the subsequent message POST sets the FK. Unlinked rows older than 1 h are reaped by a background service. `IAttachmentScanner` is DI-selected (`ChatApp:Attachments:Scanner` = `noop` | `clamav`).
- **Pagination**: keyset (`(created_at, id) < (@c, @i)`). Do not add offset pagination on messages.
- **Forwarded headers**: API sits behind nginx; `UseForwardedHeaders` is configured so session rows record the real client IP, not the nginx-container IP. Don't strip it.

## Configuration keys

All live under the `ChatApp:` prefix in `appsettings.json`:
- `ChatApp:Cookie:*` — cookie policy (Secure, SameSite, Domain).
- `ChatApp:Files:Root` — filesystem root (default `/var/chatapp/files`).
- `ChatApp:Attachments:*` — size caps, scanner kind, `FilesRoot` override.
- `ConnectionStrings:Default` — Postgres connection string.

## Testing

`ChatApp.Tests` is wired (`Unit/` + `Integration/` folders). Stack: xUnit + Testcontainers (Postgres) + `WebApplicationFactory`.

- **Unit** — permission matrices, presence aggregation, MIME-sniff rejection. No DB.
- **Integration** — full HTTP pipeline. Keep these flows green: register → login → create room → send message (+reply +attachment) → page back; friendship accept → DM; `UserBan` blocks DM writes; room delete purges attachment files from disk.
- Mark integration tests with `[Trait("Category", "Integration")]` so `--filter Category=Integration` isolates them.
