# Slice 0 — Foundation

Unblocks every subsequent slice. Turns the current 15-line `Program.cs` scaffold into a runnable API with a real DB connection, structured logging, standardised error responses, and a DB-probing health check.

## Context

`docs/implementation-plan.md` lists this as Slice 0: "no other slice can land without `ChatDbContext` and migration-on-startup wiring". Today:

- `server/ChatApp.Api/Program.cs` wires nothing — no DI, no DbContext, no logging, a literal `Results.Ok` at `/health`.
- `ChatApp.Data` has `Configurations/` and `Migrations/` folders but no `ChatDbContext`, no entity configs, no migrations.
- `server/Directory.Packages.props` explicitly defers Central Package Management (CPM) until real `PackageReference`s land — which is now.
- `infra/docker-compose.yml` already provisions Postgres 16, wires `ConnectionStrings__Default` into the api container, and gives the api a `/health` healthcheck. The foundation slice is what makes that healthcheck meaningful.

Outcome: `docker compose up` starts the api healthy, with the DB connection logged, and an empty `InitialCreate` migration applied cleanly.

## Decisions

| Topic                    | Decision                                                                  | Rationale |
|--------------------------|---------------------------------------------------------------------------|-----------|
| DbContext shape          | Empty `ChatDbContext` + empty `InitialCreate` migration                   | Satisfies acceptance criterion ("empty migration applies cleanly"); creates `__ef_migrations_history` so slice 1's first real migration has something to follow. |
| Snake_case naming        | `EFCore.NamingConventions` + `UseSnakeCaseNamingConvention()`             | Matches table names in `01-architecture.md` (`users`, `sessions`, `room_members`, …) without per-entity `ToTable/HasColumnName` boilerplate. |
| Error handling           | `AddProblemDetails()` + `UseExceptionHandler()`                           | RFC 7807 responses out of the box, zero custom code, correlates unhandled exceptions into Serilog. Custom `IExceptionHandler`s can be registered later without refactor. |
| Health check             | `AddHealthChecks().AddDbContextCheck<ChatDbContext>()` → `MapHealthChecks("/health")` | Plays directly with the compose `wget /health` probe; extensible (room for disk/queue checks later); standard pattern. |
| Central Package Mgmt     | Enable now in `Directory.Packages.props`                                  | First PackageReferences land in this slice; centralising from the start avoids version drift later. |
| Serilog output           | Plain-text console sink (`Serilog.AspNetCore` + `Serilog.Sinks.Console`)  | Readable in `docker compose logs`. Switch to JSON / add aggregator later with a one-line config change. |
| Request logging          | `UseSerilogRequestLogging()`                                              | Baseline the arch doc implies ("connect/disconnect logged at Information"). One Information line per request with elapsed ms. |
| Design-time factory      | `ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>` in `ChatApp.Data` | `DbContext` lives in a non-startup project; the factory lets `dotnet ef migrations add` work without the `--startup-project` dance. |

## Scope

### Files to create

| Path | Purpose |
|------|---------|
| `server/ChatApp.Data/ChatDbContext.cs` | Empty `DbContext` with ctor `(DbContextOptions<ChatDbContext>)`. No `DbSet<T>` yet. |
| `server/ChatApp.Data/ChatDbContextFactory.cs` | `IDesignTimeDbContextFactory<ChatDbContext>`. Reads `ConnectionStrings:Default` from env var or falls back to a local dev connection string. Configures `UseNpgsql(...).UseSnakeCaseNamingConvention()` — same wiring as Program.cs so generated SQL matches runtime. |
| `server/ChatApp.Data/Migrations/{timestamp}_InitialCreate.cs` | Generated via `dotnet ef migrations add InitialCreate`. Up/Down bodies empty. Committed. |
| `server/ChatApp.Data/Migrations/ChatDbContextModelSnapshot.cs` | Generated alongside the migration. Committed. |

### Files to modify

| Path | Change |
|------|--------|
| `server/Directory.Packages.props` | Flip `ManagePackageVersionsCentrally` to `true`. Add `PackageVersion` entries for all packages below. Drop the placeholder comment. |
| `server/ChatApp.Data/ChatApp.Data.csproj` | Add `PackageReference` (no `Version`) for `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`, `EFCore.NamingConventions`. |
| `server/ChatApp.Api/ChatApp.Api.csproj` | Add `PackageReference` for `Serilog.AspNetCore`, `Serilog.Sinks.Console`, `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`. Existing `Microsoft.AspNetCore.OpenApi` moves to versionless form under CPM. |
| `server/ChatApp.Api/Program.cs` | Full rewrite — see shape below. |

### `Program.cs` shape (reference)

```csharp
using ChatApp.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace ChatApp.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((ctx, cfg) => cfg
            .ReadFrom.Configuration(ctx.Configuration)
            .WriteTo.Console());

        var conn = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

        builder.Services.AddDbContext<ChatDbContext>(o => o
            .UseNpgsql(conn)
            .UseSnakeCaseNamingConvention());

        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks()
            .AddDbContextCheck<ChatDbContext>();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ChatDbContext>()
                .Database.Migrate();
        }

        app.UseExceptionHandler();
        app.UseSerilogRequestLogging();
        app.MapHealthChecks("/health");

        app.Run();
    }
}
```

Per-context controller folders (`Auth/`, `Users/`, …) are not created yet — they arrive with the slice that populates them.

### Out of scope (defer to later slices)

- Entities and DbSets (slice 1: `User`, `Session`).
- Cookie auth, PBKDF2, CSRF, `ICurrentUser` (slice 1).
- Rate limiting, security headers, CSP (slice 16).
- SignalR hubs (slice 4).
- `IAttachmentScanner` wiring (slice 11).
- Appsettings changes beyond what already exists.

## Verification

1. **Build clean.** `dotnet build server/ChatApp.sln` — no warnings (CPM + `TreatWarningsAsErrors`).
2. **Migration generates empty.** `dotnet ef migrations add InitialCreate --project server/ChatApp.Data`. Inspect: `Up` and `Down` bodies are empty (or only `MigrationBuilder` header). Commit the file and the model snapshot.
3. **Compose boot-up.** `docker compose -f infra/docker-compose.yml up -d --build`.
   - `db` healthy within ~5 s.
   - `api` logs (`docker compose logs api`) show a Serilog line indicating the migration applied (EF Core emits `Applied migration 'InitialCreate'` or similar) and one `HTTP GET /health responded 200` line per healthcheck tick.
   - `docker compose ps` shows `api` healthy within `start_period` (30 s).
4. **End-to-end health.** `curl http://localhost:8080/healthz` (via nginx) and `curl http://localhost:5175/health` (direct via `docker-compose.override.yml`) both return `200` with HealthChecks payload.
5. **DB-outage signal.** `docker compose stop db` — next `/health` poll returns `503`. `docker compose start db` — returns to `200` within a poll cycle. Proves the probe is real, not a stub.
6. **Local iteration.** `dotnet run --project server/ChatApp.Api` against the exposed dev Postgres on `localhost:5432` works identically without a rebuild.

## Follow-ups for slice 1 (Identity)

Hand-off notes so slice 1 does not re-plan this ground:

- `ChatApp.Data/Configurations/Identity/` — where `UserConfiguration`, `SessionConfiguration` will live (context folder, not project).
- First real migration is `AddIdentity` — adds `users`, `sessions` tables. Because of `UseSnakeCaseNamingConvention()`, column names land snake_case automatically; only override via `HasColumnName` where the arch doc specifies a non-derivable name.
- `ChatDbContext` gains `DbSet<User>` and `DbSet<Session>`.
- `Program.cs` additions in slice 1: `AddAuthentication(...).AddCookie(...)`, session cache (`AddMemoryCache`), `ICurrentUser` accessor registration, `AuthController`, and the cookie-hash middleware per `01-architecture.md §Auth flow`.
- ProblemDetails already wired — auth failures should return problem-details responses out of the box.

## Critical files at a glance

- `server/ChatApp.Api/Program.cs`
- `server/ChatApp.Data/ChatDbContext.cs`
- `server/ChatApp.Data/ChatDbContextFactory.cs`
- `server/ChatApp.Data/Migrations/*`
- `server/Directory.Packages.props`
- `server/ChatApp.{Api,Data}/*.csproj`
