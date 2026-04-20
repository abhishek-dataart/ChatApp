# infra/

Docker and nginx deployment artifacts. Single-host Compose stack sized for **300 concurrent users on one API container**.

See the [repo-root README](../README.md) for the full stack.

---

## Files

| File                          | Purpose                                                                                 |
|-------------------------------|-----------------------------------------------------------------------------------------|
| `docker-compose.yml`          | `db`, `clamav`, `api`, `web` services on a single `chatapp` bridge network.             |
| `docker-compose.override.yml` | Dev-only overrides (auto-loaded by Compose).                                            |
| `Dockerfile.api`              | Multi-stage .NET 10 build → runtime image for the API.                                  |
| `Dockerfile.web`              | Multi-stage Angular build → nginx static serving + reverse proxy.                       |
| `nginx.conf`                  | SPA fallback, `/api/` proxy, `/hub/` WebSocket upgrade, `/healthz`, security headers, hashed-asset caching. |
| `.env.example`                | Template for `infra/.env` — copy and customise before first `compose up`.               |

---

## Services

| Service   | Image                       | Notes                                                                    |
|-----------|-----------------------------|--------------------------------------------------------------------------|
| `db`      | `postgres:16-alpine`        | Named volume `pgdata`. Sole writer is `api`.                             |
| `clamav`  | `clamav/clamav:1.4`         | Named volume `clamav-db`. Optional — disabled when `Scanner=noop`.       |
| `api`     | built from `Dockerfile.api` | Bind mount `../data/files → /var/chatapp/files`. EF migrations run on startup. |
| `web`     | built from `Dockerfile.web` | nginx. Serves SPA, reverse-proxies `/api/` and `/hub/` to `api:8080`.    |

Topology:

```
Browser ──► web (nginx :80) ──► api (kestrel :8080) ──► db (postgres :5432)
                                   │
                                   ├── /var/chatapp/files  (bind: ../data/files)
                                   └── clamav (:3310, when ChatApp:Attachments:Scanner=clamav)
```

The API is the **only** writer to Postgres and to the files volume. Do not add sidecars that mutate either.

---

## Commands

Run from the repo root — the compose file path is explicit, so `cwd` doesn't matter:

```bash
# Bring up the stack (build images if needed)
docker compose -f infra/docker-compose.yml up -d --build

# Follow API logs
docker compose -f infra/docker-compose.yml logs -f api

# Stop but keep volumes (pgdata, clamav-db, files)
docker compose -f infra/docker-compose.yml down

# Stop and WIPE pgdata + clamav-db — destructive, confirm before running
docker compose -f infra/docker-compose.yml down -v
```

Build contexts are **`..`** (repo root) — the Dockerfiles reference both `server/` and `client/` from the build context. Don't move them to use `infra/` as context without updating the `COPY` paths.

---

## Environment (`infra/.env`)

Copy `.env.example` → `.env` and adjust.

```bash
# From repo root

# macOS / Linux / Git Bash / WSL
cp infra/.env.example infra/.env

# PowerShell
Copy-Item infra/.env.example infra/.env

# Windows cmd
copy infra\.env.example infra\.env
```

Then edit `infra/.env` in your editor, or set/override a single variable inline for one run:

```bash
# Unix shells — export for the current shell
export POSTGRES_PASSWORD=s3cret
docker compose -f infra/docker-compose.yml up -d

# Or inline, scoped to the single command
POSTGRES_PASSWORD=s3cret docker compose -f infra/docker-compose.yml up -d

# PowerShell
$env:POSTGRES_PASSWORD = "s3cret"; docker compose -f infra/docker-compose.yml up -d

# Point compose at a different env file
docker compose --env-file infra/.env.prod -f infra/docker-compose.yml up -d
```

Variables:

| Variable                 | Purpose                                                   |
|--------------------------|-----------------------------------------------------------|
| `WEB_PORT`               | Host port for nginx (default `8080`)                      |
| `POSTGRES_USER`          | Postgres superuser name                                   |
| `POSTGRES_PASSWORD`      | Postgres password — **change for non-local use**          |
| `POSTGRES_DB`            | Database name                                             |
| `ASPNETCORE_ENVIRONMENT` | `Development` / `Staging` / `Production`                  |

Further API config uses `ChatApp__*` env vars (double underscore = config section separator). See [`../server/README.md`](../server/README.md#configuration).

---

## nginx specifics (`nginx.conf`)

- **Security headers** at the edge: strict CSP, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`. API-side duplicates are `proxy_hide_header`'d — if you add a new security header on the API, also hide it here (or drop the API copy) to avoid doubled values.
- **`/hub/`** upgrades to WebSocket with 1 h read/send timeouts. Don't shorten — SignalR long-running connections depend on it.
- **`/healthz`** at the edge proxies to the API's `/health` endpoint.
- **Caching**: hashed assets (`*.js`, `*.css`, fonts, images) get `Cache-Control: public, immutable; expires 1y`. `index.html` is **not** fingerprinted and is served via the SPA fallback — never cache it aggressively.
- **Forwarded headers**: nginx sets `X-Forwarded-For` / `X-Forwarded-Proto`; the API honours them via `UseForwardedHeaders` so sessions record real client IPs.

---

## Scanner toggle

`ChatApp__Attachments__Scanner` (defaults to `clamav` in compose) selects the `IAttachmentScanner`:

- `noop` — no scanning, skip the `clamav` service. **Also remove the `api` → `clamav` `depends_on` entry**, otherwise the healthcheck stalls startup.
- `clamav` — connects to the `clamav` service at `clamav:3310`.

---

## Scale-out

Everything here is single-instance. When we eventually run multiple API containers we'll need:

- A Redis service (session cache + SignalR backplane + presence store).
- `AddStackExchangeRedis()` on the API for the backplane.
- Implementations of `IPresenceStore` and `IMessageBus` that use Redis.

Until then, **do not**:
- add a second `api` replica,
- introduce sticky sessions,
- cache session state in a way that assumes process affinity.

The in-proc SignalR groups are authoritative.

---

## Backups and data

- `pgdata` — Postgres data directory (named volume).
- `clamav-db` — ClamAV signature DB (named volume, re-populated by freshclam).
- `data/files` (bind mount from repo root) — uploaded attachments + thumbnails, laid out as `/var/chatapp/files/{yyyy}/{mm}/{uuid}{.ext}`. Back this up together with the database.
