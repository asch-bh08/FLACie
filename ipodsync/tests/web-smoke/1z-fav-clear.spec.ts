import { test, expect, Page } from '@playwright/test';

// Favourites: Clear all asks first, takes them all away, and Undo brings them back.

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

test('Clear all favourites, then Undo', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page);
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  for (const n of [5, 6]) {
    const row = page.locator('.tracks .track').nth(n - 1);
    const heart = row.getByRole('button', { name: 'Add to favourites' });
    if (await heart.count()) await heart.click();
  }
  await page.goto('/favorites');
  const titles = () => page.locator('.tracks .track-title').allInnerTexts().then((a) => a.map((x) => x.trim()));
  await expect.poll(titles, { timeout: 20_000 }).toEqual(expect.arrayContaining(['Lab 5', 'Lab 6']));
  const before = (await titles()).length;
  await page.getByRole('button', { name: 'Clear all', exact: true }).click();
  await page.getByRole('button', { name: new RegExp('^Clear all ' + before + '$') }).click();
  await expect.poll(titles).toEqual([]);
  await expect(page.locator('.toast')).toContainText(`Cleared ${before} favourites`);
  await page.locator('.toast').getByRole('button', { name: 'Undo' }).click();
  await expect.poll(async () => (await titles()).length).toBe(before);
});
