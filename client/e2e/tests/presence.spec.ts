import { chromium, expect, Page, test } from '@playwright/test';
import { createTestUser, TestUser } from '../fixtures/test-users';

async function register(page: Page, user: TestUser): Promise<void> {
  await page.goto('/register');
  await page.getByLabel('Email').fill(user.email);
  await page.getByLabel('Username').fill(user.username);
  await page.getByLabel('Display name').fill(user.displayName);
  await page.getByLabel('Password').fill(user.password);
  await page.getByRole('button', { name: /create account/i }).click();
  await expect(page).toHaveURL(/\/app(\/|$)/);
}

test('friends see each other as online after accepting a request', async () => {
  const alice = createTestUser('alicepres');
  const bob = createTestUser('bobpres');

  const baseURL = process.env['E2E_BASE_URL'] ?? 'http://localhost:8080';
  const browser = await chromium.launch();
  const aCtx = await browser.newContext({ baseURL });
  const bCtx = await browser.newContext({ baseURL });
  const aPage = await aCtx.newPage();
  const bPage = await bCtx.newPage();

  try {
    await register(aPage, alice);
    await register(bPage, bob);

    // Alice sends a friend request via the Contacts → Add Friend tab.
    await aPage.goto('/app/contacts');
    await aPage.getByRole('tab', { name: /add friend/i }).click();
    await aPage.getByLabel('Username').fill(bob.username);
    await aPage.getByRole('button', { name: /send request/i }).click();
    await expect(aPage.getByText(/friend request sent|request sent/i)).toBeVisible();

    // Bob accepts from the Incoming tab.
    await bPage.goto('/app/contacts');
    await bPage.getByRole('tab', { name: /incoming/i }).click();
    await bPage.getByRole('button', { name: /^accept$/i }).first().click();

    // Reload so alice's presence subscription is re-seeded with the newly
    // accepted friendship — on a fresh connect, the server's PresenceSnapshot
    // now includes bob, which is the cleanest way to observe his live state.
    await aPage.reload();
    await aPage.goto('/app/contacts');
    await aPage.getByRole('tab', { name: /^friends/i }).click();
    const main = aPage.getByRole('main');
    await expect(main.getByText(bob.displayName)).toBeVisible();

    // The friend row's avatar renders a presence dot with aria-label
    // reflecting live state ("online" | "afk" | "offline"). Bob is connected
    // via WebSocket, so alice should observe his presence as "online".
    await expect(
      main.locator('[aria-label="online"]').first(),
    ).toBeVisible({ timeout: 30_000 });
  } finally {
    await browser.close();
  }
});
