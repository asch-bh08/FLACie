import { test, expect, Page } from '@playwright/test';

// Settings from the full-screen player (owner, 12 Nov: "on that page where is the settings"): a button beside Retro on a PC, an item in More everywhere
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
async function openPlayer(page: Page) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto('/artist/Hires%20Lab');
  await ws; await page.waitForTimeout(600);
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await page.locator('.tracks .track-main').first().click();
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await expect(page.locator('.ls-names h2')).toBeVisible();
  await page.waitForTimeout(700);
}

test('a PC: Settings beside Retro closes the player and opens Settings', async ({ page }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page); await openPlayer(page);
  await page.locator('.ls-tools .np-settings').click();
  await page.waitForURL(/\/settings/);
  await expect(page.locator('.np')).toHaveCount(0);
});

test('a phone: Settings is in More', async ({ page }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 390, height: 844 });
  await signIn(page); await openPlayer(page);
  await expect(page.locator('.ls-tools .np-settings')).toBeHidden();
  await page.getByRole('button', { name: 'More options' }).locator('visible=true').first().click();
  await page.getByRole('menuitem', { name: 'Settings' }).first().click();
  await page.waitForURL(/\/settings/);
});
