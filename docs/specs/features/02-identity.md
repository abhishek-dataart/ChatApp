# Slice 1 — Identity: register / login / logout

First slice with real domain behaviour. Introduces the `User` and `Session` entities, the cookie-based auth pipeline described in `01-architecture.md §Auth flow`, and the minimum client surface needed to prove round-trip auth: register, log in, stay authed across a refresh, change password, and log out.

## Context

`docs/implementation-plan.md` slice 1; depends on slice 0 (Foundation), which already wired `ChatDbContext`, ProblemDetails, Serilog, and `/health`. Today:

- `ChatDbContext` is empty — no `DbSet<T>`, one empty `InitialCreate` migration applied on startup.
- `ChatApp.Api/Program.cs` has no authentication, no authorization, no `ICurrentUser`.
- `ChatApp.Api/Controllers/Auth/` exists as an empty `.gitkeep` placeholder from the arch-doc scaffold.
- Client `src/app/features/auth/` and `src/app/core/{auth,http}/` are empty `.gitkeep` placeholders.

Authoritative requirements that fix this slice's shape:

- **Product spec §3** — registration fields, password policy (≥10 chars, letter + digit), username regex `^[a-z0-9_]{3,20}$` case-insensitive, sessions never expire, password-change requires current password, rate limits 10/min/IP + 5/min/email on login.
- **Arch doc §Auth flow** — 32-byte token, `sha256(cookie)` stored server-side, `HttpOnly; Secure; SameSite=Lax`, `IMemoryCache` 30 s session cache, fire-and-forget `last_seen_at` update, `ICurrentUser` abstraction consumed by every other context.
- **Arch doc "Decisions vs. spec"** — PBKDF2 via ASP.NET `PasswordHasher<T>` (not Argon2id as the product spec suggests).

Outcome: two browsers can register separate accounts, log in, refresh the tab and stay authed, change their password (which kicks other browsers out on their next request), and log out. `users` and `sessions` tables exist; an `AddIdentity` migration applies cleanly on top of `InitialCreate`.

## Decisions

All user-input answers from the planning interview are folded in here; flagged *[decided]* where they close an option that was genuinely open.

| Topic | Decision | Rationale |
|---|---|---|
| Auth handler | `AddAuthentication().AddCookie(...)` **plus** a custom `SessionAuthenticationMiddleware` that re-validates the cookie's `sha256` against the `sessions` table | `AddCookie` gets us the `ClaimsPrincipal` pipeline and `[Authorize]` plumbing for free; the middleware layer is where server-side revocation (Sessions screen, password-change) takes effect on the next request. Matches arch doc §Auth flow. |
| Cookie format | Opaque 32-byte token, base64url-encoded, named `chatapp_session`; `HttpOnly; Secure; SameSite=Lax; Path=/` | Product spec + arch doc. Base64url keeps it URL/header-safe; `chatapp_` prefix makes it greppable in dev tools. |
| Session cache | `IMemoryCache`, 30 s TTL, keyed by `cookie_hash` | Arch doc. Cache holds `(userId, sessionId, revoked)`. Revocation is honoured within 30 s without flooding the DB. |
| Password hashing | `PasswordHasher<User>` with defaults (PBKDF2-HMAC-SHA256, 100k iterations in .NET 9) | Arch doc decision. Zero deps, audited, format-versioned so upgrades are mechanical. |
| Registration fields | `email`, `username`, `display_name`, `password` | *[decided]* — capture `display_name` at signup so the profile isn't empty on first login; Profile page (slice 2) will make it editable. |
| Login error messaging | Generic `401 invalid_credentials` for both unknown email and wrong password | *[decided]* — anti-enumeration; the login form must not act as an email-existence oracle. |
| Login rate limit | `AddRateLimiter` with **one** named policy `login` on `POST /api/auth/login`: fixed-window, 10/min/IP **and** 5/min/email | *[decided]* — only public-unauth endpoint in this slice; pulling the `RateLimiter` package in here is cheaper than leaving a brute-force window open until slice 16. Slice 16 adds the remaining policies (messages, uploads, register) on the same service. |
| Register rate limit | Deferred to slice 16 | Register needs an IP limit too, but slice 16 already owns the broader policy sweep. A full signup is noisier than a password guess; one missed window is acceptable. |
| Change-password side effect | Revokes **all other** sessions (current session stays live) | *[decided]* — standard hygiene; if the old password was compromised, stolen cookies lose value within 30 s (cache TTL). Current session stays so the user isn't bounced back to login on the success path. |
| Account deletion | Deferred — not in this slice | *[decided]* — product spec §3 cascades deletion into rooms, messages, attachments, friendships, invitations; none exist yet. Stub implementation would mislead. Revisited once slice 11 lands. |
| CSRF | Deferred to slice 16 | *[decided]* — `SameSite=Lax` blocks the cross-site POSTs that matter in this slice's threat model; slice 16 wires double-submit tokens uniformly across all non-GET endpoints. Login throttle is a different trade: it's scoped to one endpoint that ships here. |
| `ICurrentUser` | Scoped service registered in DI; reads claims from `HttpContext.User`; throws `UnauthorizedAccessException` if accessed on an unauth request | Every other context's services depend on it. Keeping the "must be authed" invariant at the accessor level means controllers don't re-check it. |
| User-enumeration on registration | `409 username_taken` / `409 email_taken` are **distinct** and explicit | Registration is the correct surface for this feedback; anti-enumeration on login is what protects against harvesting. |
| `/api/auth/me` | Returns `{ id, email, username, display_name, avatar_url: null }` | SPA bootstrap needs this to hydrate auth state on page load. `avatar_url` always null in this slice (Profile slice adds it); shipping the field now keeps the client contract stable. |
| Client shell for redirect | Minimal `AppShellComponent` at `/app` — top bar (username + Sign out), empty body `"Pick a chat"` | *[decided]* — login must redirect somewhere; leaving `/app` as 404 makes the demo ("refresh → still authed") awkward. Later slices extend this component rather than replacing it. |

## Scope

### Server — files to create

| Path | Purpose |
|------|---------|
| `server/ChatApp.Data/Entities/Identity/User.cs` | Entity: `Id`, `Email`, `EmailNormalized`, `Username`, `UsernameNormalized`, `DisplayName`, `AvatarPath` (null), `PasswordHash`, `CreatedAt`, `DeletedAt` (null). |
| `server/ChatApp.Data/Entities/Identity/Session.cs` | Entity: `Id`, `UserId`, `CookieHash`, `UserAgent`, `Ip`, `CreatedAt`, `LastSeenAt`, `RevokedAt`. |
| `server/ChatApp.Data/Configurations/Identity/UserConfiguration.cs` | `IEntityTypeConfiguration<User>`: unique index on `EmailNormalized` and `UsernameNormalized`; `Email`/`Username` stored case-preserving; `DisplayName` max 64. |
| `server/ChatApp.Data/Configurations/Identity/SessionConfiguration.cs` | Unique index on `CookieHash`; index on `(UserId, RevokedAt)` for the Sessions screen in slice 2. |
| `server/ChatApp.Data/Migrations/{timestamp}_AddIdentity.cs` | Generated via `dotnet ef migrations add AddIdentity`. Creates `users` + `sessions`. |
| `server/ChatApp.Domain/Abstractions/ICurrentUser.cs` | `Guid Id { get; }`, `string Username { get; }`, `bool IsAuthenticated { get; }`. |
| `server/ChatApp.Domain/Services/Identity/AuthService.cs` | `RegisterAsync`, `LoginAsync` (returns token + `Session`), `LogoutAsync`, `ChangePasswordAsync`. Owns PBKDF2 verify, session row insert, bulk-revoke on password change. |
| `server/ChatApp.Domain/Services/Identity/SessionLookupService.cs` | Cookie-hash → `(userId, sessionId)` lookup with `IMemoryCache` 30 s TTL; fire-and-forget `last_seen_at` update. |
| `server/ChatApp.Api/Infrastructure/Auth/CurrentUser.cs` | `ICurrentUser` implementation reading `HttpContext.User` claims via `IHttpContextAccessor`. |
| `server/ChatApp.Api/Infrastructure/Auth/SessionAuthenticationMiddleware.cs` | Reads `chatapp_session` cookie → `SessionLookupService` → populates `HttpContext.User` with `ClaimsPrincipal`. Sits **after** `UseAuthentication` so `[Authorize]` sees the principal. |
| `server/ChatApp.Api/Controllers/Auth/AuthController.cs` | `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/logout`, `POST /api/auth/change-password`, `GET /api/auth/me`. |
| `server/ChatApp.Api/Contracts/Auth/*.cs` | Request/response DTOs: `RegisterRequest`, `LoginRequest`, `ChangePasswordRequest`, `MeResponse`. |

### Server — files to modify

| Path | Change |
|------|--------|
| `server/Directory.Packages.props` | Add `PackageVersion` for `Microsoft.AspNetCore.Authentication.Cookies` (transitively available, but pinned), `Microsoft.AspNetCore.Identity` (for `PasswordHasher<T>`), `Microsoft.AspNetCore.RateLimiting` (transitively in `Microsoft.AspNetCore.App` — no extra package needed; confirm on first build). |
| `server/ChatApp.Data/ChatDbContext.cs` | Add `DbSet<User> Users`, `DbSet<Session> Sessions`. Apply configurations from `Configurations/Identity`. |
| `server/ChatApp.Api/Program.cs` | Add: `AddHttpContextAccessor`, `AddMemoryCache`, `AddAuthentication().AddCookie(...)`, `AddAuthorization`, `AddRateLimiter(o => o.AddPolicy("login", ...))`, DI for `AuthService`, `SessionLookupService`, `ICurrentUser`, `IPasswordHasher<User>`. Middleware order: `UseExceptionHandler` → `UseSerilogRequestLogging` → `UseAuthentication` → `SessionAuthenticationMiddleware` → `UseRateLimiter` → `UseAuthorization` → `MapControllers` → `MapHealthChecks`. |

### Client — files to create

| Path | Purpose |
|------|---------|
| `client/src/app/core/auth/auth.service.ts` | Signals-based: `currentUser: Signal<Me \| null>`, `isAuthenticated: computed`. Methods: `login`, `register`, `logout`, `changePassword`, `bootstrap()` (calls `/api/auth/me` on app start). |
| `client/src/app/core/auth/auth.guard.ts` | `CanActivateFn`; redirects to `/login` when `!isAuthenticated`. |
| `client/src/app/core/auth/anon.guard.ts` | Inverse guard; bounces authed users away from `/login`, `/register` to `/app`. |
| `client/src/app/core/http/credentials.interceptor.ts` | Sets `withCredentials: true` on every outgoing request. |
| `client/src/app/features/auth/login/login.component.ts` | Standalone component; reactive form; surfaces 401 as "Invalid email or password". |
| `client/src/app/features/auth/register/register.component.ts` | Reactive form mirroring server validation: email, username (`^[a-z0-9_]{3,20}$`), display_name (1–64), password (min 10, letter + digit). 409 surfaces which field collided. |
| `client/src/app/features/app-shell/app-shell.component.ts` | Minimal top bar (username + Sign out) + `<router-outlet>`; body placeholder "Pick a chat". |

### Client — files to modify

| Path | Change |
|------|--------|
| `client/src/app/app.routes.ts` | `/login` → LoginComponent (anonGuard), `/register` → RegisterComponent (anonGuard), `/app` → AppShellComponent (authGuard), `''` → redirect to `/app`. |
| `client/src/app/app.config.ts` | Provide `HttpClient` with the credentials interceptor; `provideAppInitializer(() => inject(AuthService).bootstrap())`. |
| `client/src/app/app.component.html` / `.ts` | Strip scaffold content; just render `<router-outlet>`. |

### Out of scope (explicit — handed to later slices)

- Sessions list + revoke UI (slice 2 — Profile + active sessions).
- Avatar upload (slice 2).
- Account deletion cascade (revisited after slice 11).
- Full CSRF double-submit wiring (slice 16).
- Rate limits beyond the single `login` policy (slice 16).
- Security headers (`nosniff`, CSP) beyond what slice 0 already provides (slice 16).

## Key flows (reference)

### Register
1. `POST /api/auth/register { email, username, display_name, password }`.
2. Validate format; reject via ProblemDetails 400.
3. Normalize email and username to lowercase; insert — UNIQUE violations surface as 409 with `code: "email_taken"` or `"username_taken"`.
4. Hash password via `PasswordHasher<User>`; persist `User`.
5. Auto-login: generate token → insert `Session` → set cookie → return 201 with `MeResponse` body.

### Login
1. `POST /api/auth/login { email, password }` with `RateLimiter("login")` — 10/min/IP and 5/min/email.
2. Lookup user by `EmailNormalized`. If missing, run a dummy hash verify (constant-time) then return 401.
3. `VerifyHashedPassword`; 401 on mismatch. Both failure branches return identical 401 `{ code: "invalid_credentials" }`.
4. On `PasswordVerificationResult.SuccessRehashNeeded`, re-hash and update.
5. Generate 32-byte token; insert `Session` with UA + client IP; set cookie; return 200 with `MeResponse`.

### Logout
1. `POST /api/auth/logout` — `[Authorize]`.
2. Set `revoked_at` on the current session row; evict cache entry for this `cookie_hash`.
3. Clear the cookie. Return 204.

### Change password
1. `POST /api/auth/change-password { current_password, new_password }` — `[Authorize]`.
2. Verify `current_password`; 400 `invalid_current_password` on mismatch.
3. Hash new; update `password_hash`.
4. Bulk-update `sessions SET revoked_at = now() WHERE user_id = @me AND id <> @current AND revoked_at IS NULL`.
5. Return 204. Other browsers clear themselves on next request (30 s worst case via cache TTL).

### Bootstrap / `/api/auth/me`
1. `GET /api/auth/me` — `[Authorize]`. Returns `MeResponse` for the current principal.
2. Client's `APP_INITIALIZER` calls it once; a 401 puts the SPA in the unauthenticated state and navigation to `/app` is blocked by the guard.

## Verification

1. **Build clean.** `dotnet build server/ChatApp.sln` — no warnings.
2. **Migration.** `dotnet ef migrations add AddIdentity --project server/ChatApp.Data --startup-project server/ChatApp.Api`. Inspect: creates `users` and `sessions` with the expected indexes. Commit file + snapshot.
3. **Unit tests** (xUnit, no DB):
   - `AuthService.RegisterAsync` rejects password missing digit / letter / under 10 chars.
   - `AuthService.RegisterAsync` rejects username `"AB"`, `"abc def"`, `"ok-name"`, `"TOOLONG" * 5`.
   - `AuthService.LoginAsync` returns the same `401 invalid_credentials` shape for unknown email and wrong password (anti-enumeration).
   - `AuthService.ChangePasswordAsync` revokes every other live session and leaves the current one untouched.
4. **Integration tests** (Testcontainers Postgres + `WebApplicationFactory`):
   - Register → login → `GET /api/auth/me` returns the new user.
   - Duplicate email / username registration returns 409 with the right `code`.
   - Login rate limit: 11th attempt from the same IP within a minute returns 429; 6th attempt for the same email within a minute returns 429.
   - Change-password in browser A → browser B's next request returns 401 (after cache TTL is bypassed in tests by clearing `IMemoryCache`).
   - Logout in browser A → subsequent request with the same cookie returns 401.
5. **Compose smoke.** `docker compose -f infra/docker-compose.yml up -d --build`. Open two browsers, register two accounts, log in, refresh — still authed. Log out — cookie gone, `/app` redirects to `/login`.
6. **Cookie flags smoke.** In dev-tools Network tab, confirm `Set-Cookie: chatapp_session=...; Path=/; HttpOnly; Secure; SameSite=Lax`.

## Follow-ups for slice 2 (Profile + active sessions)

Hand-off notes so slice 2 does not re-plan this ground:

- `UserService` / `ProfileService` in `ChatApp.Domain/Services/Identity/` for display-name + avatar.
- `SessionsController` reads from `sessions`; revoke endpoint sets `revoked_at` and evicts the cache entry by `cookie_hash` (reuse `SessionLookupService.EvictAsync`).
- Avatar upload lives on `POST /api/profile/avatar` multipart → saved under `files/avatars/{userId}{.ext}`; `Attachment` table does **not** yet exist, so store the path directly on `users.avatar_path`.
- Sound-on-message toggle is a nullable boolean on `users`; add the column in slice 2's migration.
- `MeResponse.avatar_url` already shipped — slice 2 just needs to populate it when the column is non-null.

## Critical files at a glance

- `server/ChatApp.Api/Program.cs`
- `server/ChatApp.Api/Controllers/Auth/AuthController.cs`
- `server/ChatApp.Api/Infrastructure/Auth/SessionAuthenticationMiddleware.cs`
- `server/ChatApp.Domain/Services/Identity/{AuthService,SessionLookupService}.cs`
- `server/ChatApp.Domain/Abstractions/ICurrentUser.cs`
- `server/ChatApp.Data/Entities/Identity/{User,Session}.cs`
- `server/ChatApp.Data/Configurations/Identity/{User,Session}Configuration.cs`
- `server/ChatApp.Data/Migrations/{timestamp}_AddIdentity.cs`
- `client/src/app/core/auth/*`
- `client/src/app/core/http/credentials.interceptor.ts`
- `client/src/app/features/auth/{login,register}/*`
- `client/src/app/features/app-shell/app-shell.component.ts`
- `client/src/app/app.{routes,config}.ts`
