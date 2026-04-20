// One-shot helper: register the loadtest.{1..300}@example.com users that the load scenarios
// expect. Safe to re-run — existing users surface as 409 and are ignored.
//
// Run:
//   BASE_URL=http://localhost:8080 k6 run server/k6/scenarios/seed_users.js

import http from 'k6/http';
import { baseUrl } from '../checks.js';

export const options = {
  scenarios: {
    seed: { executor: 'per-vu-iterations', vus: 10, iterations: 30, maxDuration: '5m' },
  },
  thresholds: {
    checks: ['rate>0.99'],
  },
};

export default function () {
  // Each VU seeds 30 users → 10 * 30 = 300 total.
  const base = (__VU - 1) * 30 + 1;
  for (let i = 0; i < 30; i++) {
    const n = base + i;
    const email = `loadtest.${n}@example.com`;
    const username = `loadtest_${n}`;
    const body = JSON.stringify({
      email,
      username,
      displayName: `Load Test ${n}`,
      password: 'password1a',
    });
    const res = http.post(`${baseUrl()}/api/auth/register`, body, {
      headers: { 'Content-Type': 'application/json' },
      tags: { endpoint: 'register' },
    });
    if (res.status !== 201 && res.status !== 409) {
      console.error(`seed ${email} failed: ${res.status} ${res.body}`);
    }
  }
}
