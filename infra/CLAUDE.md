# CLAUDE.md — infra/

Docker / nginx deployment artifacts. Single-host Compose stack targeting **300 concurrent users on one API container**.

## Files

- `docker-compose.yml` — services: `db` (postgres:16-alpine), `clamav` (clamav/clamav:1.4), `api` (built from `Dockerfile.api`), `web` (built from `Dockerfile.web`, serves SPA + reverse-proxies via nginx). Networks: single `chatapp` bridge. Named volumes: `pgdata`, `clamav-db`. Bind mount: `../data/files → /var/chatapp/files` on the API container.
- `docker-compose.override.yml` — local dev overrides (loaded automatically by compose).
- `Dockerfile.api` — multi-stage .NET 10 build.
- `Dockerfile.web` — multi-stage Angular build → nginx static serving.
- `nginx.conf` — SPA + `/api/` proxy + `/hub/` WebSocket upgrade + `/healthz`.
- `.env.example` (at repo root or alongside) — copy to `infra/.env` for `WEB_PORT`, `POSTGRES_*`, `ASPNETCORE_ENVIRONMENT` overrides.

## Commands

```bash
# From repo root (compose file is in infra/)
docker compose -f infra/docker-compose.yml up -d --build
docker compose -f infra/docker-compose.yml logs -f api
docker compose -f infra/docker-compose.yml down          # keep volumes
docker compose -f infra/docker-compose.yml down -v       # WIPES pgdata + clamav-db — confirm before running
```

Build contexts are **`..`** (repo root) — the Dockerfiles expect to see both `server/` and `client/` from the build context. Don't move them into `infra/` as context without updating COPY paths.

## nginx specifics

- Strict CSP + `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy` set at the edge; API-side duplicates are `proxy_hide_header`'d to avoid doubled values. If you add a new security header in the API, also hide it here (or drop the API copy).
- `/hub/` location upgrades to WebSocket with 1 h read/send timeouts — don't shorten; SignalR long-running connections depend on it.
- `/healthz` at the edge proxies to the API's `/health` endpoint.
- Hashed asset paths (`*.js`, `*.css`, fonts, images) get `Cache-Control: public, immutable; expires 1y`. `index.html` is not fingerprinted and is served via SPA fallback — never cache it aggressively.

## Service topology

```
Browser ──► web (nginx :80) ──► api (kestrel :8080) ──► db (postgres :5432)
                                    │
                                    └── /var/chatapp/files  (bind: ../data/files)
                                    └── clamav (:3310, when ChatApp:Attachments:Scanner=clamav)
```

The API is the only writer to Postgres and to the files volume. Do not add sidecars that mutate either.

## Scanner toggle

`ChatApp:Attachments:Scanner` env var (default `clamav` in compose) selects the `IAttachmentScanner`. Set to `noop` to run without the `clamav` service — remember to also make `api` not `depends_on` clamav, or the healthcheck will stall startup.

## Scale-out note

Everything here is single-instance. When we eventually run multiple API containers, expect to add: a Redis service (session cache + SignalR backplane + presence store) and an API-level `AddStackExchangeRedis()`. Until then, do not introduce sticky-session hacks or a second `api` replica — the in-proc SignalR groups are authoritative.
