import { test, expect, Page } from '@playwright/test';

// Several songs at once: Select in a song's menu starts choosing (a tap then ticks a row), and the bar acts on all of them.

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

test('select three songs and add them to the queue in one go', async ({ page }) => {
  test.setTimeout(90_000);
  await signIn(page);
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  await page.waitForTimeout(1500);
  // something playing first, so the queue has a head
  await page.locator('.tracks .track-main').first().click();
  await page.getByRole('button', { name: 'Pause', exact: true }).first().click();

  await page.getByRole('button', { name: 'More for Lab 3' }).click();
  await page.getByRole('menuitem', { name: 'Select' }).click();
  const bar = page.getByRole('region', { name: 'Selected songs' });
  await expect(bar).toContainText('1 selected');
  await page.locator('.track-pick[aria-label="Select Lab 5"]').click();
  await page.locator('.track-pick[aria-label="Select Lab 7"]').click();
  await expect(bar).toContainText('3 selected');
  await bar.getByRole('button', { name: 'Add to queue' }).click();
  await expect(page.locator('.toast')).toContainText('Added 3 songs to the queue');
  await expect(bar).toBeHidden();
});
