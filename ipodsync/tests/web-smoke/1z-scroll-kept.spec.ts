import { test, expect, Page } from '@playwright/test';

// Back to a list returns to the place in it: scroll down Songs, open an album, press Back, and the list is where it was.

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

test('Back to a list comes back to the same place in it', async ({ page }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 1280, height: 800 });
  await signIn(page);
  await page.evaluate(() => (window as any).Blazor.navigateTo('/songs'));
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollHeight), { timeout: 20_000 }).toBeGreaterThan(2500);
  await page.evaluate(() => window.scrollTo(0, 1500));
  await page.waitForTimeout(600);
  const before = await page.evaluate(() => window.scrollY);
  expect(before).toBeGreaterThan(1000);
  await page.evaluate(() => (window as any).Blazor.navigateTo('/albums'));
  await page.waitForTimeout(1500);
  await page.goBack();
  await expect.poll(() => page.evaluate(() => window.scrollY), { timeout: 8000 }).toBeGreaterThan(before - 150);
});
