import { test, expect, Page } from '@playwright/test';

// The owner pressed the 4th song of Home's Quick picks (Price Tag) and the 1st (I Gotta Feeling) started. A song pressed on Home is the one that plays.

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(1500);
}

test('pressing a song on Home plays that song', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  // a few plays first, so Home has shelves of songs
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  for (const n of [0, 1, 2, 3]) { await page.locator('.tracks .track-main').nth(n).click(); await page.waitForTimeout(700); }
  await page.goto('/');
  await page.waitForTimeout(4000);   // Home works its shelves out again once the whole library is in
  const picks = page.locator('button.pick, button.song-card');
  const n = await picks.count();
  expect(n).toBeGreaterThan(3);
  for (const i of [3, Math.min(5, n - 1)]) {
    const label = (await picks.nth(i).getAttribute('aria-label'))!;
    const title = label.replace(/^Play /, '').replace(/ by .*$/, '');
    await picks.nth(i).click();
    await expect(page).toHaveTitle(new RegExp('^' + title.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + ' ·'), { timeout: 8000 });
    await page.goto('/'); await page.waitForTimeout(2500);
  }
});
