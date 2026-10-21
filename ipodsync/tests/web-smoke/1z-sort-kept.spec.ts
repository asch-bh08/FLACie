import { test, expect, Page } from '@playwright/test';

// The sort chosen for a list is remembered (in this browser): Songs sorted by Artist is still sorted by Artist after a reload.

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

async function live(page: Page, url: string) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 }).catch(() => null);
  await page.goto(url); await ws; await page.waitForTimeout(2000);
}

test('a list keeps the sort chosen for it after a reload', async ({ page }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await live(page, '/songs');
  const sort = page.locator('.x-sort');
  await sort.locator('.dd-btn').click();
  await sort.getByRole('option', { name: 'Artist' }).click();
  await expect(sort.locator('.dd-val')).toHaveText('Artist');
  await live(page, '/songs');
  await expect(sort.locator('.dd-val')).toHaveText('Artist', { timeout: 10_000 });
  // put it back for the other specs (the browser storage is this test's own, but be tidy)
  await sort.locator('.dd-btn').click();
  await sort.getByRole('option', { name: 'Title A–Z' }).click();
});
