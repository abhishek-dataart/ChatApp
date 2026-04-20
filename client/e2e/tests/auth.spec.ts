import { expect, Page, test } from '@playwright/test';
import { createTestUser, TestUser } from '../fixtures/test-users';

async function registerAndLandOnApp(page: Page, user: TestUser): Promise<void> {
  await page.goto('/register');
  await page.getByLabel('Email').fill(user.email);
  await page.getByLabel('Username').fill(user.username);
  await page.getByLabel('Display name').fill(user.displayName);
  await page.getByLabel('Password').fill(user.password);
  await page.getByRole('button', { name: /create account/i }).click();
  await expect(page).toHaveURL(/\/app(\/|$)/);
}

test('register → login → logout round-trip', async ({ page }) => {
  const user = createTestUser('auth');

  await registerAndLandOnApp(page, user);

  // Logout via the top-bar user menu.
  await page.getByRole('button', { name: 'User menu' }).click();
  await page.getByRole('menuitem', { name: /sign out|log out/i }).click();
  await expect(page).toHaveURL(/\/login/);

  // Login with the same credentials.
  await page.getByLabel('Email').fill(user.email);
  await page.getByLabel('Password').fill(user.password);
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page).toHaveURL(/\/app(\/|$)/);
});

test('invalid login shows an error', async ({ page }) => {
  await page.goto('/login');
  await page.getByLabel('Email').fill('nobody@example.com');
  await page.getByLabel('Password').fill('wrongpassword');
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page.getByRole('alert')).toContainText(/invalid|incorrect/i);
});
