# Slice 4 — Realtime backbone

Fourth slice after Foundation (0), Identity (1), Profile/Sessions (2), Friends (3). Establishes the two SignalR hubs that every subsequent real-time feature routes through. No messages, no presence aggregation yet — those land in slices 5/6. This slice wires `PresenceHub` and `ChatHub`, verifies cookie auth survives the WebSocket upgrade, and gives the Angular client a single `SignalrService` with reconnect-with-backoff that later feature services consume.

## Context

`docs/implementation-plan.md` slice 4; depends on slice 1 (Identity — gives us `SessionAuthenticationHandler`, which populates `ClaimTypes.NameIdentifier` with the user's GUID; that's what SignalR's default `IUserIdProvider` reads for `Context.UserIdentifier`).

Authoritative requirements that fix this slice's shape:

- **Arch doc §Realtime** — two hubs: `PresenceHub` on `/hub/presence`, `ChatHub` on `/hub/chat`. Hub auth uses the same cookie as REST. `OnConnectedAsync` re-checks the session so revocation is honoured on next reconnect. Connect/disconnect logged at Information.
- **Arch doc §Decisions vs. spec** — rate limiting on hubs is a token bucket; that is slice 16. SignalR groups are in-process (no Redis backplane at MVP).
- **Arch doc §Scale-out path** — `IMessageBus` abstraction deferred; `IHubContext` is the implementation today.
- **Infra** — `nginx.conf` already proxies `/hub/` with `Upgrade`/`Connection: upgrade` headers and 1-hour timeouts. No nginx changes needed.
- **Client** — SignalR lives in `core/signalr/`. Features consume events via services, never instantiate `HubConnection` directly. Environment config provides `hubBase`.

Outcome: both browsers log in; API logs show `PresenceHub connected` and `ChatHub connected` at Information level for each session, each carrying the user's GUID and SignalR connection ID. Logout logs disconnect lines. Restarting the API container causes the Angular client to reconnect with exponential backoff; once the API is healthy the `connected` log lines reappear.

## Decisions

| Topic | Decision | Rationale |
|---|---|---|
| SignalR NuGet | No extra package — `Microsoft.AspNetCore.SignalR` is in the shared framework | Zero deps, already in `Microsoft.AspNetCore.App`. |
| Hub auth | `[Authorize]` on both hubs; no custom `IUserIdProvider` | `SessionAuthenticationHandler` sets `ClaimTypes.NameIdentifier = userId.ToString()`. SignalR's default `IUserIdProvider` reads exactly that claim for `Context.UserIdentifier`. `[Authorize]` uses the sole registered scheme ("Session") by convention. |
| Groups in `OnConnectedAsync` | Both hubs add connection to `user:{userId}` | *[decided]* — `PresenceHub` needs it for slice 6 presence fan-out to contacts; `ChatHub` needs it for slice 9 `UnreadChanged` per-user events. Room and pchat groups join in slices 5/7 when those entities exist. |
| Disconnect group cleanup | Not called explicitly in `OnDisconnectedAsync` | SignalR removes all connections from groups automatically on disconnect. Explicit `RemoveFromGroupAsync` would be redundant; log only. |
| Reconnect policy | Custom `IRetryPolicy` — unlimited retries, exponential backoff capped at 30 s | Default `withAutomaticReconnect()` stops after 4 attempts (~42 s). A chat client should keep trying indefinitely. The policy returns `Math.min(1000 * 2^attempt, 30_000)` ms. |
| Hub connection lifetime | `AppShellComponent.ngOnInit` → `start()`; `ngOnDestroy` → `stop()` | *[decided]* — app-shell renders only when authenticated; its Angular lifetime exactly matches the logged-in session. Logout navigates away, `ngOnDestroy` fires, `stop()` cleans up both connections cleanly. |
| No DB migration | No new entities | Presence aggregation state is in-proc; that's slice 6. |

### Deferred (explicit — handed to later slices)

- `PresenceAggregator` singleton and `Heartbeat(isActive)` hub method — slice 6.
- `pchat:{personalChatId}` group membership on ChatHub connect — slice 5.
- `room:{roomId}` group membership on ChatHub connect — slice 7.
- Hub token-bucket rate limit — slice 16.
- UI connection-state indicator in app-shell nav bar — future; the state signal is exposed now for later wiring.

## Scope

### Server — files to create

| Path | Purpose |
|------|---------|
| `server/ChatApp.Api/Hubs/PresenceHub.cs` | `[Authorize]` hub. `OnConnectedAsync`: add connection to `user:{userId}` group, log Information with `UserId` and `ConnectionId`. `OnDisconnectedAsync`: log Information. No hub methods yet (Heartbeat is slice 6). |
| `server/ChatApp.Api/Hubs/ChatHub.cs` | `[Authorize]` hub. Same connect/disconnect logging pattern. `OnConnectedAsync` also adds to `user:{userId}` for future `UnreadChanged` delivery. Exposes **no** write methods — message send goes through REST. |

### Server — files to modify

| Path | Change |
|------|--------|
| `server/ChatApp.Api/Program.cs` | Add `builder.Services.AddSignalR();` after `builder.Services.AddAuthorization();`. Add `app.MapHub<PresenceHub>("/hub/presence");` and `app.MapHub<ChatHub>("/hub/chat");` after `app.MapControllers();`. Add `using ChatApp.Api.Hubs;`. |

### Client — files to create

| Path | Purpose |
|------|---------|
| `client/src/app/core/signalr/signalr.service.ts` | Root-scoped service (`providedIn: 'root'`). Builds two `HubConnection` instances: `presence` at `${environment.hubBase}/presence` and `chat` at `${environment.hubBase}/chat`, both with `withCredentials: true` and the unlimited-retry policy. Exposes `presenceState = signal<HubConnectionState>(HubConnectionState.Disconnected)` and `chatState = signal<HubConnectionState>(HubConnectionState.Disconnected)`. Wires each connection's `onreconnecting`, `onreconnected`, and `onclose` callbacks to update the corresponding signal. `start(): Promise<void>` starts both; `stop(): Promise<void>` stops both. |

### Client — files to modify

| Path | Change |
|------|--------|
| `client/package.json` | Add `"@microsoft/signalr": "^8.0.7"` to `dependencies`. Run `npm install` to materialise `package-lock.json`. |
| `client/src/app/features/app-shell/app-shell.component.ts` | `inject(SignalrService)`. Implement `OnInit` / `OnDestroy`. `ngOnInit`: call `signalrService.start()`. `ngOnDestroy`: call `signalrService.stop()`. |

### Out of scope (explicit — handed to later slices)

- Any hub methods invokable by the client (`Heartbeat`, etc.) — slice 6.
- Joining `pchat:*` or `room:*` groups — slices 5/7.
- Broadcasting `MessageCreated` or any other event — slices 5+.
- Token-bucket rate limit on hub connections — slice 16.

## Key flows (reference)

### Hub connect on login

1. User logs in via REST (`POST /api/auth/login`); API sets `chatapp_session` cookie.
2. Angular `AppShellComponent.ngOnInit` calls `signalrService.start()`.
3. `SignalrService` calls `presenceConn.start()` and `chatConn.start()` in parallel.
4. Browser sends WebSocket upgrade requests to `/hub/presence` and `/hub/chat`. nginx proxies with `Upgrade`/`Connection` headers.
5. ASP.NET Core middleware pipeline runs: `UseAuthentication` → `SessionAuthenticationHandler` reads the cookie, hashes it, validates against the `sessions` table (30 s cached), sets `ClaimsPrincipal` with `NameIdentifier = userId`.
6. `[Authorize]` on the hub passes. `OnConnectedAsync` fires: adds connection to `user:{userId}` group, logs Information.
7. `SignalrService` `onreconnected` callback updates the `presenceState` / `chatState` signal to `Connected`.

### Hub disconnect on logout

1. User clicks Sign Out → `AppShellComponent.signOut()` calls `auth.logout()` then navigates to `/login`.
2. Angular destroys `AppShellComponent` → `ngOnDestroy` calls `signalrService.stop()`.
3. Both connections close gracefully. `OnDisconnectedAsync` fires on the server and logs Information.

### Reconnect after API restart

1. API container restarts. Both hub connections receive a close event.
2. `@microsoft/signalr` retries at 0 s, 1 s, 2 s, 4 s… up to 30 s cap, indefinitely.
3. `onreconnecting` callback sets the signal to `Reconnecting`.
4. Once the API is back, the WebSocket upgrade succeeds; `onreconnected` sets the signal to `Connected`. New `OnConnectedAsync` log lines appear in the API.

## Implementation

### PresenceHub.cs

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Api.Hubs;

[Authorize]
public class PresenceHub(ILogger<PresenceHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier!;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        logger.LogInformation(
            "PresenceHub connected userId={UserId} connectionId={ConnectionId}",
            userId, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation(
            "PresenceHub disconnected userId={UserId} connectionId={ConnectionId}",
            Context.UserIdentifier, Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
```

### ChatHub.cs

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Api.Hubs;

[Authorize]
public class ChatHub(ILogger<ChatHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier!;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        logger.LogInformation(
            "ChatHub connected userId={UserId} connectionId={ConnectionId}",
            userId, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation(
            "ChatHub disconnected userId={UserId} connectionId={ConnectionId}",
            Context.UserIdentifier, Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
```

### Program.cs additions

```csharp
// after AddAuthorization():
builder.Services.AddSignalR();

// after app.MapControllers():
app.MapHub<PresenceHub>("/hub/presence");
app.MapHub<ChatHub>("/hub/chat");
```

### signalr.service.ts

```typescript
import { Injectable, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  IRetryPolicy,
  RetryContext,
} from '@microsoft/signalr';
import { environment } from '../../../environments/environment';

const retryPolicy: IRetryPolicy = {
  nextRetryDelayInMilliseconds(ctx: RetryContext): number {
    return Math.min(1000 * Math.pow(2, ctx.previousRetryCount), 30_000);
  },
};

@Injectable({ providedIn: 'root' })
export class SignalrService {
  private readonly presenceConn: HubConnection = new HubConnectionBuilder()
    .withUrl(`${environment.hubBase}/presence`, { withCredentials: true })
    .withAutomaticReconnect(retryPolicy)
    .build();

  private readonly chatConn: HubConnection = new HubConnectionBuilder()
    .withUrl(`${environment.hubBase}/chat`, { withCredentials: true })
    .withAutomaticReconnect(retryPolicy)
    .build();

  readonly presenceState = signal<HubConnectionState>(this.presenceConn.state);
  readonly chatState = signal<HubConnectionState>(this.chatConn.state);

  constructor() {
    this.presenceConn.onreconnecting(() =>
      this.presenceState.set(HubConnectionState.Reconnecting));
    this.presenceConn.onreconnected(() =>
      this.presenceState.set(HubConnectionState.Connected));
    this.presenceConn.onclose(() =>
      this.presenceState.set(HubConnectionState.Disconnected));

    this.chatConn.onreconnecting(() =>
      this.chatState.set(HubConnectionState.Reconnecting));
    this.chatConn.onreconnected(() =>
      this.chatState.set(HubConnectionState.Connected));
    this.chatConn.onclose(() =>
      this.chatState.set(HubConnectionState.Disconnected));
  }

  async start(): Promise<void> {
    await Promise.all([
      this.presenceConn.start().then(() =>
        this.presenceState.set(HubConnectionState.Connected)),
      this.chatConn.start().then(() =>
        this.chatState.set(HubConnectionState.Connected)),
    ]);
  }

  async stop(): Promise<void> {
    await Promise.all([this.presenceConn.stop(), this.chatConn.stop()]);
  }

  get presence(): HubConnection { return this.presenceConn; }
  get chat(): HubConnection { return this.chatConn; }
}
```

The `presence` and `chat` getters expose the raw `HubConnection` objects so later services (slice 5 `DmService`, slice 6 `PresenceService`, etc.) can register event handlers with `.on('EventName', handler)` without importing `HubConnectionBuilder` themselves.

### app-shell.component.ts additions

```typescript
import { OnDestroy, OnInit } from '@angular/core';
import { SignalrService } from '../../core/signalr/signalr.service';

// class AppShellComponent implements OnInit, OnDestroy {
//   private readonly signalr = inject(SignalrService);
//
//   ngOnInit(): void { this.signalr.start(); }
//   ngOnDestroy(): void { this.signalr.stop(); }
// }
```

(Merge with the existing `AppShellComponent` class; add `OnInit` / `OnDestroy` to the `implements` list.)

## Verification

1. **Build clean.** `dotnet build server/ChatApp.sln` — zero warnings. `cd client && npm install && npm run build` — zero errors.
2. **API hub logs.** `docker compose -f infra/docker-compose.yml up -d --build`. Open the SPA, log in. Tail API logs: confirm `PresenceHub connected userId=<guid> connectionId=<id>` and `ChatHub connected userId=<guid> connectionId=<id>` appear at Information level.
3. **Disconnect log.** Log out (or navigate to `/login`). Confirm `PresenceHub disconnected` and `ChatHub disconnected` lines appear.
4. **Auth enforcement.** In an incognito window (no session cookie), open devtools console and run:
   ```js
   const { HubConnectionBuilder } = await import('/hub/presence');
   // or manually: fetch('/hub/presence/negotiate', {method:'POST'})
   ```
   A `POST /hub/presence/negotiate` without a session cookie must return `401`. Hub connection attempt must fail, not silently succeed.
5. **Reconnect with backoff.** Log in. Restart only the API: `docker compose restart api`. Watch browser network tab — reconnect attempts should appear with increasing intervals (0 s, 1 s, 2 s, 4 s…). Once API is healthy, confirm `connected` log lines reappear.
6. **Connection-state signal.** Add a temporary `console.log` in `AppShellComponent.ngOnInit` after `start()`: `console.log(this.signalr.presenceState())`. Value should be `Connected`. Remove before committing.

## Follow-ups for slice 5 (DM messaging)

- `DmService` registers `chatConn.on('MessageCreated', handler)` via `SignalrService.chat`.
- `POST /api/chats/personal/{id}/messages` server handler resolves the `pchat:{id}` group and calls `IHubContext<ChatHub>.Clients.Group(...)`.
- `AppShellComponent` (or `DmComponent.ngOnInit`) calls `chatConn.invoke('JoinPersonalChat', chatId)` if we decide to add a join method, or the server joins the group on connect once it can enumerate the user's personal chats from the DB.

## Critical files at a glance

- `server/ChatApp.Api/Hubs/PresenceHub.cs` *(new)*
- `server/ChatApp.Api/Hubs/ChatHub.cs` *(new)*
- `server/ChatApp.Api/Program.cs` *(AddSignalR + MapHub)*
- `client/src/app/core/signalr/signalr.service.ts` *(new)*
- `client/src/app/features/app-shell/app-shell.component.ts` *(ngOnInit/ngOnDestroy)*
- `client/package.json` *(add @microsoft/signalr)*
