// 300 concurrent users, ramp-hold-ramp-down, each sending 1 message / 5 s to a shared room
// for 3 minutes. Verifies the non-functional targets from the architecture spec:
//   - p95 message send/broadcast latency < 3 s
//   - error rate < 1 %
//   - 300 simultaneous users sustained on a single API instance
//
// Seed assumption:
//   * users  loadtest.{1..300}@example.com / password1a
//   * a public room exists; pass its id via  -e ROOM_ID=<guid>
//   * base URL default http://localhost:5175 (override with BASE_URL env)
//
// Run:
//   k6 run -e ROOM_ID=<roomId> server/k6/scenarios/load_300.js

import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Trend, Rate } from 'k6/metrics';
import { baseUrl } from '../checks.js';

const messageLatency = new Trend('chat_message_send_ms', true);
const failedSends    = new Rate('chat_message_failed');

export const options = {
  scenarios: {
    chat_300: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: 300 },
        { duration: '3m',  target: 300 },
        { duration: '20s', target: 0 },
      ],
      gracefulRampDown: '10s',
    },
  },
  thresholds: {
    http_req_failed:          ['rate<0.01'],
    chat_message_failed:      ['rate<0.01'],
    chat_message_send_ms:     ['p(95)<3000'],
    'http_req_duration{scenario:chat_300}': ['p(95)<3000'],
    checks:                   ['rate>0.99'],
  },
};

const ROOM_ID = __ENV.ROOM_ID;
if (!ROOM_ID) {
  throw new Error('ROOM_ID env var is required (pass -e ROOM_ID=<guid>).');
}

export function setup() {
  if (!ROOM_ID || ROOM_ID.length < 10) {
    throw new Error('ROOM_ID must be a valid room GUID.');
  }
  return { roomId: ROOM_ID };
}

export default function (data) {
  const email = `loadtest.${(__VU % 300) + 1}@example.com`;
  const password = 'password1a';

  // Keep one active session per VU for the duration of the iteration loop.
  const auth = login(email, password);
  if (!auth) {
    failedSends.add(1);
    sleep(5);
    return;
  }

  group('send message', () => {
    const started = Date.now();
    const body = JSON.stringify({ body: `VU ${__VU} @ ${started}` });
    const res = http.post(
      `${baseUrl()}/api/chats/room/${data.roomId}/messages`,
      body,
      {
        headers: {
          'Content-Type': 'application/json',
          'X-Csrf-Token': auth.csrf,
          Cookie: auth.cookie,
        },
        tags: { scenario: 'chat_300', endpoint: 'messages_post' },
      },
    );
    const ok = check(res, { 'message 2xx': (r) => r.status >= 200 && r.status < 300 });
    messageLatency.add(Date.now() - started);
    failedSends.add(!ok);
  });

  // 1 message / 5 s pacing per VU → 60 msg/s sustained across 300 VUs.
  sleep(5);
}

function login(email, password) {
  const res = http.post(
    `${baseUrl()}/api/auth/login`,
    JSON.stringify({ email, password }),
    {
      headers: { 'Content-Type': 'application/json' },
      tags: { scenario: 'chat_300', endpoint: 'login' },
    },
  );
  if (res.status !== 200) {
    return null;
  }
  const setCookie = res.headers['Set-Cookie'] || '';
  const session = extract(setCookie, /chatapp_session=([^;,\s]+)/);
  const csrf    = extract(setCookie, /csrf_token=([^;,\s]+)/);
  if (!session || !csrf) return null;
  return { cookie: `chatapp_session=${session}; csrf_token=${csrf}`, csrf };
}

function extract(haystack, re) {
  const m = re.exec(haystack);
  return m ? m[1] : '';
}
