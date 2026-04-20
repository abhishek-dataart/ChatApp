/**
 * Per-run test users. Usernames and emails are suffixed with a timestamp so
 * repeat runs against a long-lived API don't collide.
 */
export interface TestUser {
  email: string;
  username: string;
  displayName: string;
  password: string;
}

function stamp(): string {
  // ~10 chars, lowercase alphanumeric — short enough to fit the server's
  // 20-char username cap even after prefixing.
  return (Date.now().toString(36) + Math.random().toString(36).slice(2, 5)).slice(-10);
}

export function createTestUser(prefix = 'e2e'): TestUser {
  const s = stamp();
  // Keep the combined `prefix_stamp` under 20 chars (server validates `^[a-z0-9_]{3,20}$`).
  const safePrefix = prefix.slice(0, 8);
  return {
    email: `${safePrefix}-${s}@example.com`,
    username: `${safePrefix}_${s}`,
    displayName: `${safePrefix} ${s}`,
    password: 'Passw0rd!Passw0rd!',
  };
}

export const USERS = {
  alice: () => createTestUser('alice'),
  bob: () => createTestUser('bob'),
};
