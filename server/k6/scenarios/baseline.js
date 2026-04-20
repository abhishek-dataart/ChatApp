// 300 VUs, WS + REST send, 1 message / 5 s, 60 s duration.
// Seed users first: loadtest.{1..300}@example.com / password1a.
//
// Tune numbers per environment; this script is scaffold only.

import http from 'k6/http';
import ws from 'k6/ws';
import { check, sleep } from 'k6';
import { thresholds, baseUrl, wsUrl } from '../checks.js';

export const options = {
  scenarios: {
    chat: {
      executor: 'constant-vus',
      vus: 300,
      duration: '60s',
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
  check(loginRes, { 'login ok': (r) => r.status === 200 });

  const cookie = loginRes.headers['Set-Cookie'] || '';
  const csrf = extractCsrf(cookie);

  // Connect the chat hub (negotiate is handled by SignalR; k6 targets the WS transport directly
  // in real runs you'd go through /hub/chat?access_token=... or reuse the cookie via skipNegotiation).
  ws.connect(`${wsUrl()}/hub/chat`, { headers: { Cookie: cookie } }, (socket) => {
    socket.on('open', () => {
      socket.setInterval(() => {
        const body = JSON.stringify({ body: `hello from VU ${__VU}` });
        http.post(`${baseUrl()}/api/chats/room/REPLACE_ME/messages`, body, {
          headers: {
            'Content-Type': 'application/json',
            'X-Csrf-Token': csrf,
            Cookie: cookie,
          },
        });
      }, 5000);

      socket.setTimeout(() => socket.close(), 55000);
    });
  });

  sleep(1);
}

function extractCsrf(setCookie) {
  const m = /csrf_token=([^;,\s]+)/.exec(setCookie);
  return m ? m[1] : '';
}
