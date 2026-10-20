import { test, expect, Page } from '@playwright/test';

// After a server restart the page reloads by itself (flacie.autoRecover). A song that was playing then carries on from the second it had reached,
// instead of coming back paused as an ordinary reload leaves it.

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(1000);
}

test('a reload after a server restart carries on playing; an ordinary reload comes back paused', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page);
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  await page.locator('.tracks .track-main').first().click();
  await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 20_000 });
  await page.getByRole('button', { name: 'Pause', exact: true }).first().click();
  await page.waitForTimeout(1500);

  // an ordinary reload: paused
  await page.reload();
  await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 40_000 });
  await expect(page.getByRole('button', { name: 'Play', exact: true }).first()).toBeVisible();

  // the reload autoRecover does when the server is back, with the song playing: it plays on
  await page.evaluate(() => (window as any).flacie.recoverReload(true, 1));
  await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 40_000 });
  await expect(page.getByRole('button', { name: 'Pause', exact: true }).first()).toBeVisible({ timeout: 20_000 });
});
