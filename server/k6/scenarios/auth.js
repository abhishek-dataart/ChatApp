// Login/logout cycle under load. Validates the auth/session path does not bottleneck
// under the ramp-up phase that the baseline scenario hits in a single burst.

import http from 'k6/http';
import { check } from 'k6';
import { thresholds, baseUrl } from '../checks.js';

export const options = {
  scenarios: {
    auth: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { target: 100, duration: '20s' },
        { target: 100, duration: '30s' },
        { target: 0,   duration: '10s' },
      ],
    },
  },
  thresholds,
};

export default function () {
  const email = `loadtest.${(__VU % 300) + 1}@example.com`;
  const password = 'password1a';

  const loginRes = http.post(
    `${baseUrl()}/api/auth/login`,
    JSON.stringify({ email, password }),
    { headers: { 'Content-Type': 'application/json' } },
  );
  if (!check(loginRes, { 'login ok': (r) => r.status === 200 })) {
    return; // skip logout if login failed (user pool not seeded, etc.)
  }

  const cookie = loginRes.headers['Set-Cookie'] || '';
  const csrf = /csrf_token=([^;,\s]+)/.exec(cookie)?.[1] || '';

  const logoutRes = http.post(`${baseUrl()}/api/auth/logout`, null, {
    headers: { Cookie: cookie, 'X-Csrf-Token': csrf },
  });
  check(logoutRes, { 'logout ok': (r) => r.status === 204 });
}
