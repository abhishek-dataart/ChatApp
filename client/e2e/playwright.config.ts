import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright smoke config. Expects the full stack to be running via
 * `docker compose -f infra/docker-compose.yml up -d --build` (nginx on :8080
 * serves the SPA and proxies /api + /hub to the API container).
 *
 * Override E2E_BASE_URL to target a different deployment (e.g. the dev server
 * on :4200 with its proxy.conf.json forwarding to a local dotnet API).
 */
const baseURL = process.env['E2E_BASE_URL'] ?? 'http://localhost:8080';

export default defineConfig({
  testDir: './tests',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  retries: 0,
  workers: 1,
  reporter: [['list']],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    video: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
