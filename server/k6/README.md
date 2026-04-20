# Load testing — k6

Scaffold scripts only. Not tuned for any specific environment.

## Prereqs

- [k6](https://k6.io) ≥ 0.50 installed locally.
- The API reachable at `$BASE_URL` (default `http://localhost:5175`).
- A reverse proxy or direct Kestrel with WebSocket upgrade support.

## Scenarios

| Script            | What it exercises                                            |
|-------------------|--------------------------------------------------------------|
| `baseline.js`     | 300 VUs, WebSocket connect, 1 message / 5 s, 60 s duration. Spec target: p95 delivery < 3 s, error rate < 1 %. |
| `auth.js`         | Login/logout cycle under load — validates the session/cookie path does not become a bottleneck. |
| `load_300.js`     | **Acceptance scenario for the 300-user target.** Ramps to 300 VUs, holds 3 min, each VU posts 1 msg / 5 s to a shared room; asserts `p(95) < 3000 ms` and error rate `< 1 %`. Requires `-e ROOM_ID=<guid>` and seeded `loadtest.{1..300}@example.com` users. |
| `seed_users.js`   | One-shot helper — registers 300 `loadtest.*@example.com` users (idempotent; 409s are ignored). |

Assertions (thresholds) are declared via shared `checks.js`.

## Run

```bash
docker compose -f infra/docker-compose.yml up -d --build
BASE_URL=http://localhost:8080 k6 run server/k6/scenarios/baseline.js

# 300-user acceptance run (seed users, then run the load):
BASE_URL=http://localhost:8080 k6 run server/k6/scenarios/seed_users.js
BASE_URL=http://localhost:8080 k6 run \
  -e ROOM_ID=<room-guid> \
  server/k6/scenarios/load_300.js
```

Set `BASE_URL` to point at whichever environment you want to load (the default `docker-compose.yml`
exposes nginx on `:8080`; a direct `dotnet run` exposes the API on `:5175`).

If k6 isn't installed locally, run via the official image:

```bash
docker run --rm -i \
  -e BASE_URL=http://host.docker.internal:8080 \
  -v "$PWD/server/k6:/scripts" \
  grafana/k6:latest run /scripts/scenarios/auth.js
```

On Git Bash for Windows, prefix with `MSYS_NO_PATHCONV=1` to prevent path mangling of `/scripts`.

## Notes

- Scripts assume a pre-seeded pool of user credentials at `loadtest.N@example.com / password1a`.
  Seed them ahead of time via `/api/auth/register`, or plug a setup stage into the script.
- Thresholds in `checks.js` are per-spec defaults; tune per environment before gating CI on them.
