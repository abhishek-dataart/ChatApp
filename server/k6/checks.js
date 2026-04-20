// Shared thresholds and check helpers for the ChatApp k6 scripts.
// Per the architecture spec: p95 message delivery < 3 s, error rate < 1 %.

export const thresholds = {
  http_req_failed:   ['rate<0.01'],
  http_req_duration: ['p(95)<3000'],
  ws_msgs_received:  ['count>0'],
  checks:            ['rate>0.99'],
};

export function baseUrl() {
  return __ENV.BASE_URL || 'http://localhost:5175';
}

export function wsUrl() {
  const http = baseUrl();
  return http.replace(/^http/, 'ws');
}
