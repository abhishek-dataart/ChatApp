import { expect, test } from '@playwright/test';
import path from 'node:path';
import fs from 'node:fs';
import os from 'node:os';
import { createTestUser } from '../fixtures/test-users';

/**
 * A real PNG from a dev dep — big enough for the server's image validation
 * to accept (a 1×1 placeholder gets rejected with 422).
 */
const SAMPLE_PNG = path.resolve(
  __dirname,
  '..',
  '..',
  'node_modules',
  '@jest',
  'reporters',
  'assets',
  'jest_logo.png',
);

test('image upload renders a thumbnail in the conversation', async ({ page }) => {
  const user = createTestUser('attach');
  await page.goto('/register');
  await page.getByLabel('Email').fill(user.email);
  await page.getByLabel('Username').fill(user.username);
  await page.getByLabel('Display name').fill(user.displayName);
  await page.getByLabel('Password').fill(user.password);
  await page.getByRole('button', { name: /create account/i }).click();
  await expect(page).toHaveURL(/\/app(\/|$)/);

  // Create a public room so we have a chat surface.
  const roomName = `att${Date.now().toString(36)}`;
  await page.goto('/app/rooms/public');
  await page.getByRole('main').getByRole('button', { name: /new room/i }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel(/name/i).fill(roomName);
  await dialog.getByLabel(/description/i).fill('Attachments smoke');
  await dialog.getByRole('button', { name: /create room/i }).click();
  await expect(page).toHaveURL(/\/app\/rooms\/[0-9a-f-]+/i);

  // Copy a real PNG to a temp path so uploads don't collide on repeat runs.
  const pngPath = path.join(os.tmpdir(), `e2e-${Date.now()}.png`);
  fs.copyFileSync(SAMPLE_PNG, pngPath);

  try {
    // Target the composer's own file input (the first hidden one inside the composer).
    await page
      .locator('input[type="file"].composer__file-input')
      .setInputFiles(pngPath);

    // Type a body (server rejects attachment-only messages with 400) and
    // wait for the upload to finish — the Send button stays disabled while
    // any attachment is still uploading. Once enabled, fire the message.
    const composer = page.getByPlaceholder(/type a message/i);
    await composer.fill('image test');
    const sendBtn = page.getByRole('button', { name: 'Send' });
    await expect(sendBtn).toBeEnabled({ timeout: 20_000 });
    await sendBtn.click();

    // The message list renders image attachments with class `attachment__thumb`.
    await expect(page.locator('img.attachment__thumb').first()).toBeVisible({
      timeout: 20_000,
    });
  } finally {
    fs.unlinkSync(pngPath);
  }
});
