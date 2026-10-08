import { test, expect } from '@playwright/test';

// A browser whose Jellyfin token has been dropped must be sent back to the sign-in page, not left half signed in (no playlists, no admin).
test('a cookie whose Jellyfin token was revoked is signed out', async ({ page }) => {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('revoked');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  // the account load fails with 401; the next request must land on the login page
  await expect(async () => {
    await page.goto('/songs');
    await expect(page).toHaveURL(/\/login/, { timeout: 3000 });
  }).toPass({ timeout: 30_000 });
});
