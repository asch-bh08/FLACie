import { test, expect, Page } from '@playwright/test';

// The 1.0 budget: a FLACie tab stays under 300 MB of JavaScript memory after a session of browsing and playing (Chromium's own measure).

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

test('a tab stays under 300 MB after browsing and playing', async ({ page }) => {
  test.setTimeout(180_000);
  await signIn(page);
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await page.locator('.tracks .track-main').first().click();
  await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 20_000 });
  // walk the app twice with the music on, opening the full-screen player each time (the client-side navigation a person does)
  for (let round = 0; round < 2; round++) {
    for (const href of ['/', '/search?q=Lab', '/songs', '/albums', '/artists', '/playlists', '/settings']) {
      await page.evaluate((h) => (window as any).Blazor.navigateTo(h), href);
      await page.waitForTimeout(1200);
    }
    await page.getByRole('button', { name: 'Open Now Playing' }).click();
    await page.waitForTimeout(2500);
    await page.keyboard.press('Escape');
    await page.waitForTimeout(800);
  }
  const mb = await page.evaluate(() => ((performance as any).memory?.usedJSHeapSize ?? 0) / 1048576);
  console.log(`JS heap after the walk: ${mb.toFixed(1)} MB`);
  expect(mb).toBeGreaterThan(0);
  expect(mb).toBeLessThan(300);
});
