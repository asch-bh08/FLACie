import { test, expect, Page } from '@playwright/test';
// (named 1w- so it runs before 2-login, whose wrong-password tests lock this address out of signing in)

// ONE APP 1e: Devices and Jam take the kit headings (Title 30, Section 24) and buttons (small 44 r16, main 56).

const STATE = 'test-results/devices-kit-state.json';
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

for (const [w, h] of [[390, 844], [1366, 900]]) {
  test(`devices and jam at ${w}x${h}: kit headings and buttons`, async ({ page }) => {
    await page.setViewportSize({ width: w, height: h });
    await open(page, '/devices');
    // the title is the frame's (ONE APP 1k): serif 24 in the top bar on every page
    expect(await page.locator('.topbar .frame-title').first().evaluate((e) => getComputedStyle(e).fontSize)).toBe('24px');
    expect(await page.locator('h2.dv-h.k-section').first().evaluate((e) => getComputedStyle(e).fontSize)).toBe('24px');
    await open(page, '/jam');
    expect(await page.locator('.topbar .frame-title').first().evaluate((e) => getComputedStyle(e).fontSize)).toBe('24px');
    const b = page.locator('.sp-jam .k-btn, .sp-jam .k-btn-sm').first();
    if (await b.count()) expect(await b.evaluate((e) => getComputedStyle(e).borderTopLeftRadius)).toBe('16px');
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  });
}
