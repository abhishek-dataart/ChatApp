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

test('creator sends a message in a public room, second user receives it', async () => {
  const alice = createTestUser('alice');
  const bob = createTestUser('bob');
  const roomName = `room${Date.now().toString(36)}`;

  const baseURL = process.env['E2E_BASE_URL'] ?? 'http://localhost:8080';
  const browser = await chromium.launch();
  const aliceCtx = await browser.newContext({ baseURL });
  const bobCtx = await browser.newContext({ baseURL });
  const aPage = await aliceCtx.newPage();
  const bPage = await bobCtx.newPage();

  try {
    await register(aPage, alice);
    await register(bPage, bob);

    // Alice creates a public room via the dedicated browse page (scopes the
    // "New Room" button to the main area to avoid the sidebar duplicate).
    await aPage.goto('/app/rooms/public');
    await aPage.getByRole('main').getByRole('button', { name: /new room/i }).click();
    const dialog = aPage.getByRole('dialog');
    await dialog.getByLabel(/name/i).fill(roomName);
    await dialog.getByLabel(/description/i).fill('E2E room');
    await dialog.getByRole('button', { name: /create room/i }).click();

    // The create flow navigates alice to /app/rooms/:id — wait for that.
    await expect(aPage).toHaveURL(/\/app\/rooms\/[0-9a-f-]+/i);

    // Bob joins the same room from the public catalog.
    await bPage.goto('/app/rooms/public');
    await bPage.getByPlaceholder(/search rooms/i).fill(roomName);
    const joinBtn = bPage.getByRole('button', { name: /^join$/i }).first();
    await expect(joinBtn).toBeVisible({ timeout: 10_000 });
    await joinBtn.click();
    await expect(bPage).toHaveURL(/\/app\/rooms\/[0-9a-f-]+/i);

    // Alice sends a message; bob receives it.
    const msg = `hello-${Date.now().toString(36)}`;
    const composer = aPage.getByPlaceholder(/type a message/i);
    await composer.fill(msg);
    await composer.press('Enter');

    await expect(bPage.getByText(msg)).toBeVisible({ timeout: 15_000 });
  } finally {
    await browser.close();
  }
});
