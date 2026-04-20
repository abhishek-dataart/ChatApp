# Client Testing Plan — Angular 19 SPA

## Context

No test runner or specs exist in `client/` today, and `angular.json` has no `test` target. This plan adds Jest for unit/component tests and Playwright for E2E smoke. Decisions: Jest (not Karma), Playwright smoke now, no CI wiring in this pass.

## Install & config

Dev deps:

```
jest, jest-preset-angular, @types/jest, ts-jest,
@testing-library/angular, @testing-library/jest-dom,
@playwright/test
```

Files:

- **`client/jest.config.ts`** — preset `jest-preset-angular`, `testEnvironment: jsdom`, `moduleNameMapper` for path aliases, `setupFilesAfterEach: ['<rootDir>/setup-jest.ts']`.
- **`client/setup-jest.ts`** — `import 'jest-preset-angular/setup-jest';` plus `@testing-library/jest-dom`.
- **`client/tsconfig.spec.json`** — `types: ["jest", "node"]`.
- **`client/package.json`** — scripts: `"test": "jest"`, `"test:watch": "jest --watch"`, `"test:coverage": "jest --coverage"`, `"e2e": "playwright test"`.
- **`client/angular.json`** — no `test` target needed; Jest is invoked directly.

## Unit / component test targets

```
client/src/app/
  core/
    auth/auth.service.spec.ts                  # login/logout/signals, error paths
    auth/auth.guard.spec.ts
    http/credentials.interceptor.spec.ts       # HttpTestingController
    http/csrf.interceptor.spec.ts              # header on non-GET, skips GET
    http/error.interceptor.spec.ts             # 401 -> logout+redirect, 403 toast
    messaging/room-messaging.service.spec.ts   # keyset paging, optimistic add
    messaging/dm.service.spec.ts
    messaging/attachments.service.spec.ts      # 2-step upload ordering
    messaging/unread.service.spec.ts
    presence/presence.service.spec.ts          # state derivation from events
    presence/activity-tracker.service.spec.ts  # fakeTimers, 20s heartbeat
    signalr/signalr.service.spec.ts            # mock @microsoft/signalr HubConnection
    rooms/rooms.service.spec.ts
    rooms/moderation.service.spec.ts
    notifications/toast.service.spec.ts
  features/
    auth/login.component.spec.ts               # @testing-library/angular
    rooms/rooms-list.component.spec.ts
    rooms/create-room-dialog.component.spec.ts
    manage-room/members-tab.component.spec.ts
    app-shell/top-bar.component.spec.ts
```

## Key points

- **SignalR mock**: a `MockHubConnection` in `src/testing/mock-hub.ts`, returned from `jest.mock('@microsoft/signalr')`. Asserts `on/invoke/start/stop`; drives incoming events.
- **HTTP**: `HttpTestingController` via `provideHttpClientTesting()` for service tests.
- **Timers**: `jest.useFakeTimers()` for activity-tracker and presence tick.
- **Signals**: read via `TestBed.runInInjectionContext(() => signal())` when needed; components prefer Testing Library queries over DOM internals.

## E2E — Playwright smoke

```
client/e2e/
  playwright.config.ts       # baseURL http://localhost:4200, webServer runs `npm start`
                             # assumes API already running via docker compose
  fixtures/test-users.ts
  tests/
    auth.spec.ts             # register -> login -> logout
    messaging.spec.ts        # create room -> send message -> peer receives
    presence.spec.ts         # two contexts, offline -> online transition visible
    attachments.spec.ts      # upload image, thumbnail renders
```

- `npx playwright install chromium` once.
- Multi-user tests use two browser contexts with separate storage state.

## Files to create / modify

- **Create** `client/jest.config.ts`, `client/setup-jest.ts`, all `*.spec.ts` above, `client/e2e/**`, `client/src/testing/mock-hub.ts`.
- **Modify** `client/package.json` (deps + scripts), `client/tsconfig.spec.json`.

## Out of scope this pass

- CI workflow.
- Coverage gates.
- Cross-browser Playwright (Chromium only).

## Load testing

k6 scaffold lives in `server/k6/` — see `server/docs/TESTING_PLAN.md`.

## Verification

1. `npm install && npm test` — Jest runs, all specs green.
2. `docker compose -f infra/docker-compose.yml up -d --build` (API up), then `npm run e2e` — Playwright smoke passes on Chromium.
