import { test, expect, Page } from '@playwright/test';

// Home's "Pick up where you left off" has a button that does it: Carry on, with the song's name, plays it again.

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

test('Carry on plays what was playing last', async ({ page }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 1280, height: 800 });
  await signIn(page);
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  await page.locator('.tracks .track-main').nth(2).click();
  await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 20_000 });
  await page.getByRole('button', { name: 'Pause', exact: true }).first().click();
  await page.waitForTimeout(4000);   // the play is written to the history
  await page.goto('/');
  const carry = page.getByRole('button', { name: /^Carry on/ });
  await expect(carry).toBeVisible({ timeout: 20_000 });
  await expect(carry).toContainText('Lab');
  await carry.click();
  await expect(page.getByRole('button', { name: 'Pause', exact: true }).first()).toBeVisible({ timeout: 15_000 });
});
