# client/

Angular 19 SPA — standalone components, Signals + feature services for state, SCSS with component-scoped styles, strict TypeScript.

See the [repo-root README](../README.md) for the full stack and [`../docs/specs/01-architecture.md`](../docs/specs/01-architecture.md) for runtime semantics.

---

## Commands

```bash
npm install

npm start            # ng serve — proxies /api and /hub to http://localhost:5175
npm run build        # production build to dist/
npm run watch        # dev build with --watch

npm test             # Jest unit tests
npm run test:watch
npm run test:coverage

npm run e2e          # Playwright against a running stack
```

The API **must** be running on `:5175` for `/api` REST and `/hub` (SignalR WebSocket) to work under `ng serve`. `proxy.conf.json` handles both, including WebSocket upgrade for the hub.

---

## Layout

```
src/app/
├── core/                    # Cross-feature plumbing
│   ├── auth/                # Guard, service, models
│   ├── http/                # Interceptors: credentials, csrf, error
│   ├── signalr/             # SignalR client setup
│   ├── messaging/           # Rooms/DMs messaging, attachments, unread
│   ├── presence/            # Presence service + activity-tracker (20 s heartbeat)
│   ├── rooms/  social/  sessions/  profile/  users/
│   ├── notifications/  context/  layout/
│   └── ...
├── features/                # Feature surfaces — lazy-loaded routes
│   ├── auth/
│   ├── app-shell/
│   ├── rooms/  dms/  contacts/
│   ├── sessions/  profile/  manage-room/
└── shared/                  # Reusable UI components and pipes
```

Routes live in `app.routes.ts`; feature routes are lazy-loaded. HTTP interceptors are wired in `app.config.ts`.

---

## Conventions

- **State** — Angular **Signals + feature services**. No NgRx. Keep signal state inside the feature service; components read via `computed()`.
- **Standalone components** throughout. Component selector prefix is `app` (see `angular.json`).
- **Styles** — SCSS, component-scoped. Global tokens/theme in `src/styles/`.
- **Icons** — `lucide-angular`.
- **SignalR** — `@microsoft/signalr` v8. `ChatHub` is **read-only** from the client: do **not** invoke hub methods to send messages. POST to `/api/chats/{scope}/{scopeId}/messages` and wait for the `MessageCreated` broadcast.
- **CSRF** — `csrf.interceptor` adds the double-submit token to non-GET REST. Don't bypass it.
- **Virtualised message list** — CDK `cdk-virtual-scroll-viewport` with keyset pagination. New messages prepend without resetting the scroll anchor.
- **Presence** — `activity-tracker.service` drives the 20 s `Heartbeat(isActive)` call on `PresenceHub`. Don't add additional heartbeats.

---

## Build settings

- Angular CLI **application builder** (`@angular-devkit/build-angular:application`).
- Zone.js change detection (zoneless is not enabled).
- Strict TS (`tsconfig.json`) — keep `strict`, `noImplicitAny`, and friends on. Don't loosen to silence errors.

---

## Testing

| Layer | Stack                                                       | Command               |
|-------|-------------------------------------------------------------|-----------------------|
| Unit  | Jest + `jest-preset-angular` + `@testing-library/jest-dom`  | `npm test`            |
| E2E   | Playwright (`e2e/playwright.config.ts`) against a live stack | `npm run e2e`         |

Jest is configured via `jest.config.js` + `setup-jest.ts`. Playwright fixtures live in `e2e/fixtures/`; specs in `e2e/tests/`.

See [`docs/TESTING_PLAN.md`](docs/TESTING_PLAN.md) for the coverage plan.

---

## Proxy

`proxy.conf.json`:

```json
{
  "/api":  { "target": "http://localhost:5175", "secure": false, "changeOrigin": true },
  "/hub":  { "target": "http://localhost:5175", "secure": false, "ws": true, "changeOrigin": true }
}
```

---

## Production build

`npm run build` emits to `client/dist/`. In the compose stack, `infra/Dockerfile.web` does the Angular build and serves the artifacts via nginx, which also reverse-proxies `/api` and `/hub` to the API container.
