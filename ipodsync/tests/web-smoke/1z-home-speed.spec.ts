import { test, expect, Page } from '@playwright/test';

// The owner: Home takes 1-2 s to load. Measured from navigation to the first shelf of songs, signed in, after a first visit (the usual case).

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

test('Home shows its shelves quickly', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  for (const n of [0, 1, 2]) { await page.locator('.tracks .track-main').nth(n).click(); await page.waitForTimeout(600); }
  await page.goto('/'); await page.locator('.shelf').first().waitFor();
  const times: number[] = [];
  for (let i = 0; i < 3; i++) {
    const t0 = Date.now();
    await page.goto('/');
    await page.locator('.shelf .card, .shelf .pick').first().waitFor({ timeout: 15000 });
    times.push(Date.now() - t0);
  }
  console.log('Home to first shelf (ms): ' + times.join(', '));
  expect(Math.min(...times)).toBeLessThan(800);
});
