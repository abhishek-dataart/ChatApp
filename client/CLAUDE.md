# CLAUDE.md — client/

Angular 19 workspace (standalone components, SCSS, strict TS). See repo-root `CLAUDE.md` for cross-cutting architecture.

## Commands

```bash
npm install
npm start                # ng serve — proxies /api and /hub to http://localhost:5175 (see proxy.conf.json)
npm run build
npm run watch            # ng build --watch --configuration development
npm test                 # Jest unit tests
npm run test:watch
npm run test:coverage
npm run e2e              # Playwright (requires a running API stack)
```

The API **must** be running on `:5175` for `/api` REST and `/hub` (SignalR WebSocket) to work from the dev server.

## Layout

- `src/app/core/` — cross-feature plumbing. Subfolders by concern:
  - `auth/` — guard, service, models.
  - `http/` — HTTP interceptors: `credentials.interceptor`, `csrf.interceptor`, `error.interceptor`. Wire them in `app.config.ts`.
  - `signalr/` — SignalR client setup.
  - `messaging/`, `presence/`, `rooms/`, `social/`, `sessions/`, `profile/`, `users/`, `notifications/`, `context/`, `layout/`, `theme/`.
- `src/app/features/` — feature surfaces (one folder each): `auth`, `app-shell`, `rooms`, `dms`, `contacts`, `sessions`, `profile`, `manage-room`.
- `src/app/shared/` — reusable UI components and pipes.

## Conventions

- **State**: Angular **Signals + feature services**. No NgRx / no Redux. Keep signal state inside the feature service; components read via computed signals.
- **Standalone components** throughout. Routes live in `app.routes.ts`; feature routes are lazy-loaded.
- **Style**: SCSS, component-scoped. Component selector prefix is `app` (see `angular.json`).
- **Icons**: `lucide-angular`.
- **SignalR**: `@microsoft/signalr` v8 client. `ChatHub` is read-only from the client — **do not call hub methods to send messages**; POST to `/api/chats/...` and wait for the `MessageCreated` broadcast.
- **CSRF**: `csrf.interceptor` adds the double-submit token to non-GET REST. Don't bypass it.
- **Virtualised message list**: CDK `cdk-virtual-scroll-viewport` + keyset pagination. New entries prepend without resetting scroll anchor.
- **Presence**: `activity-tracker.service` drives the 20 s `Heartbeat(isActive)` call on `PresenceHub`. Don't add extra heartbeats.

## Build settings

- Angular CLI application builder (`@angular-devkit/build-angular:application`).
- Zone.js-based change detection (zoneless not enabled).
- Strict TS (`tsconfig.json`). Keep it strict — don't loosen `strict` / `noImplicitAny` to silence errors.

## Testing

- **Unit**: Jest + `jest-preset-angular` + `@testing-library/jest-dom`. Config in `jest.config.js` / `setup-jest.ts`. Don't reintroduce Karma.
- **E2E**: Playwright (Chromium only) under `e2e/` with `playwright.config.ts`. Fixtures in `e2e/fixtures/`; specs in `e2e/tests/`. Requires the API stack running — the config's `webServer` block spins up `ng serve` for the run.
- Coverage plan: `docs/TESTING_PLAN.md`.
