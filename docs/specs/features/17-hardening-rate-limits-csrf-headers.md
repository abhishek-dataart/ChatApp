# Slice 16 — Hardening: Rate Limits, CSRF, Security Headers

**Depends on:** Slices 5, 11  
**Demo:** Hammering POST /messages returns 429; tampered CSRF returns 403; CSP blocks injected inline scripts.

---

## Context

This slice adds the cross-cutting security hardening layer across the API, hubs, and web container. Feature slices left intentional gaps — no message rate limit, empty CSRF infrastructure, no global security headers, and no client error handling for 429/403. This slice closes all of them.

Current state going into this slice:

| Area | Status |
|---|---|
| Upload rate limit (`"uploads"`, 20/min) | Implemented in `Program.cs` |
| Login rate limit (per-IP + per-email) | Implemented in `LoginRateLimiter.cs` |
| Message rate limit | Missing |
| Hub rate limit | Missing |
| CSRF middleware | Placeholder folder only (`Infrastructure/Csrf/`) |
| `X-Content-Type-Options` | Set per-endpoint only (downloads) |
| `Content-Disposition: attachment` | Missing on download endpoint |
| CSP | nginx placeholder comment only |
| Angular CSRF header | Missing |
| Angular 429 / 403 toasts | Missing |

---

## 1 — Rate Limiting (REST)

### 1.1 Messaging policy

Add a `"messages"` policy to `AddRateLimiter` in `Program.cs`.

**Algorithm:** `TokenBucketRateLimiter`  
**Values:** 30 tokens, replenishment = 3 tokens / 1 second (matches spec: 30 msg / 10 s / user)  
**Partition key:** authenticated `userId`; fallback to `RemoteIpAddress` for unauthenticated requests  
**Queue:** 0 (no queueing — callers get 429 immediately)  
**Rejection status:** 429 with `Retry-After` header (set `RejectionStatusCode = 429` on the options)

```csharp
opts.AddPolicy("messages", context =>
{
    var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "anonymous";
    return RateLimitPartition.GetTokenBucketLimiter(userId, _ => new TokenBucketRateLimiterOptions
    {
        TokenLimit            = 30,
        TokensPerPeriod       = 3,
        ReplenishmentPeriod   = TimeSpan.FromSeconds(1),
        AutoReplenishment     = true,
        QueueLimit            = 0,
    });
});
```

Apply `[EnableRateLimiting("messages")]` to every message POST endpoint:
- `POST /api/chats/personal/{id}/messages`
- `POST /api/chats/room/{id}/messages`

### 1.2 Uploads policy

Already implemented as `"uploads"` (fixed window, 20/min/user). No change needed.

### 1.3 Login rate limit

Already handled by `LoginRateLimiter` singleton (10/min/IP + 5/min/email). No new ASP.NET middleware policy needed.

### 1.4 Global fallback

Not added — the spec defines per-endpoint limits only. A global policy would interfere with health probes and static assets.

---

## 2 — Hub Rate Limiting

**ChatHub** exposes no client→server write methods; no action needed.

**PresenceHub.Heartbeat** is the only method clients invoke. Apply an in-method token-bucket guard:

- **Limit:** 1 heartbeat per 15 s per connection (client sends every 20 s; this allows slight clock drift and one burst slot before silently dropping).
- **Implementation:** `ConcurrentDictionary<string, DateTimeOffset>` keyed by `Context.ConnectionId`, tracking the `DateTimeOffset` of the last accepted heartbeat. Store this in `PresenceAggregator` (already owns per-connection state) or a new lightweight `HubRateLimiter` helper injected into the hub.
- **Behaviour on breach:** return early (no-op), log a `Warning`. Do **not** throw — throwing in a hub method disconnects the client.

```csharp
// In PresenceHub.Heartbeat:
if (!_rateLimiter.TryConsume(Context.ConnectionId))
{
    _logger.LogWarning("Heartbeat rate-limit exceeded for connection {ConnectionId}", Context.ConnectionId);
    return;
}
await _aggregator.RecordHeartbeatAsync(Context.ConnectionId, isActive);
```

**Cleanup:** `OnDisconnectedAsync` must remove the connection's entry from the rate-limiter dictionary to prevent memory leaks.

---

## 3 — CSRF: Double-Submit Pattern

The architecture mandates double-submit token on all non-GET REST endpoints. SignalR is exempt (Origin header is checked by ASP.NET Core SignalR automatically on WebSocket upgrade).

### 3.1 Token generation (server)

**On successful login** (`AuthController.Login`):

1. Generate a 32-byte random token: `RandomNumberGenerator.GetBytes(32)`.
2. Base64url-encode it (no padding): `Base64Url.Encode(bytes)`.
3. Set a readable cookie alongside the session cookie:

```
Name:     csrf_token
HttpOnly: false          ← must be JS-readable
Secure:   true (prod) / false (dev)   ← matches CookieOptions.Secure setting
SameSite: Lax
Path:     /
Expires:  (none — browser-session lifetime, same as session cookie)
```

**On logout** (`AuthController.Logout`): expire the `csrf_token` cookie (set `Expires = DateTimeOffset.UnixEpoch`, `MaxAge = TimeSpan.Zero`) alongside the session cookie. Reuse `CookieWriter` or inline — keep parity with the session cookie expiry call.

### 3.2 Validation middleware

**File:** `server/ChatApp.Api/Infrastructure/Csrf/CsrfMiddleware.cs`  
**Registration:** `app.UseCsrf()` in `Program.cs`, placed **after** `UseAuthentication` and **before** `UseAuthorization`.

**Exemptions** (pass through without CSRF check):
- `GET`, `HEAD`, `OPTIONS` HTTP methods
- Paths starting with `/hub/` (SignalR negotiate + WebSocket upgrade)
- `/health`

**Validation logic:**
1. Read `X-Csrf-Token` request header.
2. Read `csrf_token` request cookie.
3. If either is null/empty OR they do not match: respond `403 Forbidden` with body `{ "error": "invalid_csrf_token" }` and `Content-Type: application/json`. Short-circuit — do not call `next`.
4. Otherwise: call `next`.

No cryptographic comparison needed. The guarantee comes from the browser's same-origin cookie policy: an attacker cannot read the cookie value to echo it in the header.

**Note:** Unauthenticated mutating requests (register) do not have a CSRF cookie yet. Register (`POST /api/auth/register`) must be added to the exemption list, or the CSRF check must be skipped when no `csrf_token` cookie is present **and** the user is unauthenticated. The simpler rule: skip CSRF check if `csrf_token` cookie is absent (unauthenticated users have no CSRF token yet; SameSite=Lax already protects them).

```csharp
// Simplified decision:
if (request.Method is "GET" or "HEAD" or "OPTIONS"
    || request.Path.StartsWithSegments("/hub")
    || request.Path.StartsWithSegments("/health"))
{
    await next(context);
    return;
}

var cookieToken  = request.Cookies["csrf_token"];
var headerToken  = request.Headers["X-Csrf-Token"].ToString();

if (string.IsNullOrEmpty(cookieToken))
{
    // Unauthenticated request — no CSRF token issued yet; SameSite=Lax covers this.
    await next(context);
    return;
}

if (!string.Equals(cookieToken, headerToken, StringComparison.Ordinal))
{
    context.Response.StatusCode  = StatusCodes.Status403Forbidden;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync("""{"error":"invalid_csrf_token"}""");
    return;
}

await next(context);
```

### 3.3 Client: Angular interceptor

**File:** `client/src/app/core/http/csrf.interceptor.ts` (new file, or merged into `credentials.interceptor.ts`)

Recommendation: keep it separate for clarity.

```typescript
export const csrfInterceptor: HttpInterceptorFn = (req, next) => {
  const mutating = ['POST', 'PUT', 'PATCH', 'DELETE'];
  if (!mutating.includes(req.method)) return next(req);

  const token = getCsrfToken(); // reads document.cookie
  if (!token) return next(req);

  return next(req.clone({ headers: req.headers.set('X-Csrf-Token', token) }));
};

function getCsrfToken(): string | null {
  const match = document.cookie.match(/(?:^|;\s*)csrf_token=([^;]+)/);
  return match ? decodeURIComponent(match[1]) : null;
}
```

Register in `app.config.ts` alongside `credentialsInterceptor`:
```typescript
provideHttpClient(withInterceptors([credentialsInterceptor, csrfInterceptor, errorInterceptor]))
```

---

## 4 — Error Toasts (Client)

**File:** `client/src/app/core/http/error.interceptor.ts` (new)

Handle two cases:

| Status | Condition | Toast message |
|---|---|---|
| 429 | any | "Too many requests — slow down a bit." |
| 403 | body.error === `"invalid_csrf_token"` | "Session security error — please refresh the page." |

On the CSRF 403, additionally redirect to the login page (clears the broken session state from the UI).

Wire the toast via whatever notification service the app uses (e.g., a `ToastService` or Angular Material `Snackbar`). If none exists, create a minimal `ToastService` that appends a fixed-position overlay.

---

## 5 — Security Headers (API)

Add an inline middleware in `Program.cs` (before `UseAuthentication`) that appends headers to every response:

```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"]  = "nosniff";
    context.Response.Headers["X-Frame-Options"]         = "DENY";
    context.Response.Headers["Referrer-Policy"]         = "strict-origin-when-cross-origin";
    await next();
});
```

Remove the per-endpoint `X-Content-Type-Options` header from `AttachmentsController` (the global middleware makes it redundant).

### 5.1 Content-Disposition on attachment download

`GET /api/attachments/{id}` must return `Content-Disposition: attachment; filename="{originalFilename}"`. Verify this is set in `AttachmentsController.Download()`; add it if missing. This prevents browsers from rendering untrusted file content inline.

---

## 6 — CSP on nginx (web container)

**File:** `infra/nginx.conf`

Replace the placeholder comment with:

```nginx
add_header Content-Security-Policy
    "default-src 'self'; connect-src 'self' ws: wss:; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data:; font-src 'self'; frame-ancestors 'none'; form-action 'self';"
    always;
```

Place it in the `server` block alongside the existing `X-Content-Type-Options`, `X-Frame-Options`, and `Referrer-Policy` headers.

**Directive rationale:**

| Directive | Value | Reason |
|---|---|---|
| `default-src` | `'self'` | Deny everything not explicitly allowed |
| `connect-src` | `'self' ws: wss:` | Allow XHR/fetch to API + SignalR WebSocket connections |
| `script-src` | `'self'` | Angular is compiled; no inline scripts or eval needed |
| `style-src` | `'self' 'unsafe-inline'` | Angular ViewEncapsulation injects component styles inline |
| `img-src` | `'self' blob: data:` | Thumbnail previews (blob URLs) and base64 avatars |
| `font-src` | `'self'` | Self-hosted fonts only |
| `frame-ancestors` | `'none'` | Redundant with `X-Frame-Options: DENY`; belt-and-suspenders |
| `form-action` | `'self'` | Prevent form hijacking to external origins |

`'unsafe-inline'` on `style-src` is the minimal concession for Angular. If ViewEncapsulation is later changed to `None` or nonces are added, this can be tightened.

---

## Critical files

| File | Action |
|---|---|
| `server/ChatApp.Api/Program.cs` | Add `"messages"` token-bucket policy; register security-headers middleware; register `UseCsrf()` |
| `server/ChatApp.Api/Infrastructure/Csrf/CsrfMiddleware.cs` | **New** — double-submit validation |
| `server/ChatApp.Api/Infrastructure/Csrf/CsrfExtensions.cs` | **New** — `UseCsrf()` extension |
| `server/ChatApp.Api/Controllers/Auth/AuthController.cs` | Set/expire `csrf_token` cookie on login/logout |
| `server/ChatApp.Api/Controllers/Messages/MessagesController.cs` | Add `[EnableRateLimiting("messages")]` to POST endpoints |
| `server/ChatApp.Api/Controllers/Attachments/AttachmentsController.cs` | Verify `Content-Disposition: attachment`; remove per-endpoint `X-Content-Type-Options` |
| `server/ChatApp.Api/Hubs/PresenceHub.cs` | Add token-bucket guard in `Heartbeat`; call `_rateLimiter.Remove()` in `OnDisconnectedAsync` |
| `server/ChatApp.Domain/Services/Presence/PresenceAggregator.cs` | Add `TryConsumeHeartbeat(connectionId)` or extract to new `HubRateLimiter` |
| `client/src/app/core/http/csrf.interceptor.ts` | **New** — inject `X-Csrf-Token` header |
| `client/src/app/core/http/error.interceptor.ts` | **New** — 429 and CSRF 403 toasts |
| `client/src/app/app.config.ts` | Register both new interceptors |
| `infra/nginx.conf` | Add CSP `add_header` |

---

## Verification

| Test | Expected |
|---|---|
| POST /messages 31× in 10 s (same user) | 31st → 429 with `Retry-After` |
| POST without `X-Csrf-Token` header (authenticated) | 403 `{"error":"invalid_csrf_token"}` |
| POST with wrong `X-Csrf-Token` value (authenticated) | 403 `{"error":"invalid_csrf_token"}` |
| GET without `X-Csrf-Token` | 200 — CSRF not checked on GET |
| POST /auth/register (unauthenticated, no csrf cookie) | 200 — CSRF skipped when cookie absent |
| Connect hub, send 5 heartbeats in 30 s | 4th+ are silently dropped; connection stays open |
| GET /api/attachments/{id} | `Content-Disposition: attachment` present |
| Any API response | `X-Content-Type-Options: nosniff` present |
| SPA response headers | `Content-Security-Policy` header present |
| DevTools console injection of `<script>` | CSP violation logged; script blocked |
