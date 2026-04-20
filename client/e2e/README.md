# Playwright smoke (client E2E)

Smoke suite for the ChatApp SPA — run against a live API + Angular dev server.

## Prerequisites

1. API + Postgres up:

   ```bash
   docker compose -f ../../infra/docker-compose.yml up -d --build
   ```

2. Chromium binary installed once per machine:

   ```bash
   npx playwright install chromium
   ```

## Run

From `client/`:

```bash
npm run e2e
```

The `webServer` block in `playwright.config.ts` will start `npm start` for the
duration of the run (and reuse an existing 4200 server if one is already up).

## Scope

Chromium only. No CI wiring, no cross-browser. See
`client/docs/TESTING_PLAN.md` for the broader plan.
