import { test, expect, Page } from '@playwright/test';
// (named 1w- so it runs before 2-login, whose wrong-password tests lock this address out of signing in)

// ONE APP 1f: the redesigned More (a grid of kit slots, Sign out apart) and, at phone width, every popover menu as a bottom sheet.

const STATE = 'test-results/menus-state.json';
test.beforeAll(async ({ browser }) => {
  const page = await browser.newPage();
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.context().storageState({ path: STATE });
  await page.close();
});

async function open(page: Page, url: string) {
  const state = JSON.parse(require('fs').readFileSync(STATE, 'utf8'));
  await page.context().addCookies(state.cookies);
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 }).catch(() => null);
  await page.goto(url); await ws; await page.waitForTimeout(1200);
}

const box = (page: Page, sel: string) => page.locator(sel).first().evaluate((e) => { const r = e.getBoundingClientRect(); return { l: r.left, r: r.right, t: r.top, b: r.bottom }; });

test('phone width: the song menu and More are bottom sheets the width of the window', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, '/songs');
  await page.getByRole('button', { name: /^More for / }).first().click();
  await page.waitForTimeout(600);   // the sheet rises in
  const m = await box(page, '.menu[role=menu]');
  expect(m.l).toBeCloseTo(0, 0); expect(m.r).toBeCloseTo(390, 0); expect(m.b).toBeCloseTo(844, 0);
  expect(m.t).toBeGreaterThan(100);
  await page.keyboard.press('Escape');
  await open(page, '/songs');
  await page.locator('.avatar-btn').click();
  const more = page.locator('.account-menu.am-more');
  await expect(more).toBeVisible();
  await page.waitForTimeout(600);
  const a = await box(page, '.account-menu.am-more');
  expect(a.l).toBeCloseTo(0, 0); expect(a.b).toBeCloseTo(844, 0);
  expect(await more.locator('.am-grid > .k-slot:visible').count()).toBeGreaterThanOrEqual(6);
  const slot = await more.locator('.am-grid > .k-slot').first().evaluate((e) => ({ h: e.getBoundingClientRect().height, r: getComputedStyle(e).borderTopLeftRadius }));
  expect(slot.h).toBeGreaterThanOrEqual(51.5); expect(slot.r).toBe('18px');
  await expect(more.getByRole('menuitem', { name: 'Sign out' })).toBeVisible();
  await more.getByRole('menuitem', { name: 'Devices' }).click();
  await expect(page).toHaveURL(/\/devices/);
});

test('PC: More stays a popover under the account button', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 900 });
  await open(page, '/songs');
  await page.locator('.avatar-btn').click();
  await page.waitForTimeout(600);
  const a = await box(page, '.account-menu.am-more');
  expect(a.t).toBeLessThan(120); expect(a.b).toBeLessThan(900);
  await expect(page.locator('.account-menu').getByRole('menuitem', { name: 'Settings' })).toBeVisible();
});
