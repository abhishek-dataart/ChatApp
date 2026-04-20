# Server Testing Plan — ChatApp.Api

## Context

The repo has zero tests today. `CLAUDE.md` prescribes xUnit + Testcontainers but `ChatApp.Tests` is missing. This plan scaffolds unit and integration test layers so future changes have a safety net.

Decisions: xUnit, Testcontainers Postgres (real DB, matches snake_case + keyset pagination), no CI wiring in this pass.

## Project layout — `server/ChatApp.Tests`

Single xUnit project, added to `server/ChatApp.sln`. Inherits `Directory.Build.props` (nullable on, warnings-as-errors).

```
server/ChatApp.Tests/
  ChatApp.Tests.csproj          # xUnit, FluentAssertions, NSubstitute,
                                # Testcontainers.PostgreSql,
                                # Microsoft.AspNetCore.Mvc.Testing,
                                # Microsoft.Extensions.TimeProvider.Testing,
                                # Respawn
  Unit/
    AuthValidatorTests.cs
    SessionTokensTests.cs
    LoginRateLimiterTests.cs
    MagicBytesTests.cs              # accept PNG/JPEG/PDF, reject mismatched ext
    ImageMagicBytesTests.cs
    ModerationActionsTests.cs       # role x action matrix
    PresenceAggregatorTests.cs      # active<60s -> online, idle -> afk, none -> offline
    RoomPermissionMatrixTests.cs    # owner/admin/member/banned x action
  Integration/
    Infrastructure/
      PostgresFixture.cs            # IAsyncLifetime, Testcontainers.PostgreSql 16
      ChatAppFactory.cs             # WebApplicationFactory<Program>, swaps conn-string,
                                    # registers NoOpScanner, FakeTimeProvider
      AuthenticatedClient.cs        # helper: register + login, returns HttpClient w/ cookies+CSRF
      DbSeeder.cs                   # minimal fixtures (users, rooms, memberships)
    Auth/
      RegisterLoginLogoutTests.cs
      SessionRevocationTests.cs     # revoked session -> 401 after 30s cache TTL bypass
      CsrfTests.cs                  # POST without token -> 403
    Rooms/
      RoomLifecycleTests.cs         # create, invite, accept, leave
      RoomPermissionTests.cs        # non-admin cannot kick; banned cannot post
      ModerationAuditTests.cs
    Messaging/
      RoomMessageCrudTests.cs
      DmMessageCrudTests.cs
      KeysetPaginationTests.cs      # seed 120 msgs, page with (created_at,id) cursor
      UnreadMarkerTests.cs
    Attachments/
      UploadAttachTests.cs          # 2-step: POST /attachments -> POST message
      MimeSniffRejectionTests.cs
      OrphanPurgeTests.cs           # advance clock, run purge, assert deletion
      ClamAvScannerTests.cs         # unit: NSubstitute mock of nClamAv client; infected -> reject, clean -> pass
    Social/
      FriendshipFlowTests.cs
      UserBanTests.cs
    Signalr/
      ChatHubBroadcastTests.cs      # REST POST -> hub client receives MessageCreated
      PresenceHubHeartbeatTests.cs
```

## Key implementation points

- **`PostgresFixture`** — `PostgreSqlBuilder().WithImage("postgres:16").Build()` as a collection fixture; container reused across tests, each class gets a clean DB via `Respawn`.
- **`ChatAppFactory`** — overrides `ConfigureWebHost`: sets `ConnectionStrings:Default` to the container, `ASPNETCORE_ENVIRONMENT=Testing`, replaces `IAttachmentScanner` with a NSubstitute mock (default: returns clean; individual tests configure infected responses), swaps the real clock for `FakeTimeProvider` (used by orphan-purge and rate-limit tests).
- **SignalR tests** — `HubConnectionBuilder` against `factory.Server` with `CreateHandler()` for HTTP + `WebSocketClient`; cookie copied from REST login for auth.
- **No mocking of DbContext.** Pure-service unit tests construct the service with NSubstitute fakes; integration tests use the real EF pipeline.
- **Reuse, don't re-implement**: unit tests target existing classes under `ChatApp.Domain` (`AuthValidator`, `SessionTokens`, `LoginRateLimiter`, `MagicBytes`, `ModerationActions`) and `RoomPermissionService` in `ChatApp.Data`.

## Commands

```bash
cd server
dotnet test                                                   # all
dotnet test --filter "FullyQualifiedName~Unit"                # fast loop, no Docker
dotnet test --filter "FullyQualifiedName~RoomPermission"      # single class
```

Integration tests require Docker running.

## Files to create / modify

- **Create** `server/ChatApp.Tests/` (project + all files above).
- **Modify** `server/ChatApp.sln` — add the test project.

## Load testing scaffold — `server/k6/`

```
server/k6/
  README.md
  scenarios/
    baseline.js     # 300 virtual users, WS connect + send 1 msg/5s, run 60s
    auth.js         # login/logout cycle under load
  checks.js         # shared: p95 delivery < 3s, error rate < 1%
```

Run after `docker compose up`:

```bash
k6 run server/k6/scenarios/baseline.js
```

This is scaffold only — scripts define the structure and assertions but are not tuned for a specific environment.

## Out of scope this pass

- Tuning k6 thresholds for specific infra.
- CI workflow.
- Coverage gates.

## Verification

1. `dotnet build` — no warnings (warnings-as-errors still green).
2. `dotnet test --filter "FullyQualifiedName~Unit"` — passes in seconds, no Docker needed.
3. With Docker up, `dotnet test` — full suite passes; Postgres container starts, `WebApplicationFactory` boots, SignalR round-trip asserted.
4. Sanity: temporarily break `RoomPermissionService.IsAdminOrOwnerAsync` to return `true` — `RoomPermissionTests` must go red.
