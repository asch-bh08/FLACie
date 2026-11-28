import { test, expect, Page } from '@playwright/test';
// (named 1w- so it runs before 2-login, whose wrong-password tests lock this address out of signing in)

// ONE APP 1d: Settings, Account and Downloads draw the kit: buttons and choices are the small button (44, r16), the headings the kit Title / Section.

const STATE = 'test-results/settings-state.json';
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
  test(`settings family at ${w}x${h}: kit buttons, choices and headings`, async ({ page }) => {
    await page.setViewportSize({ width: w, height: h });
    await open(page, '/settings');
    const btn = await page.locator('.sp .btn').first().evaluate((e) => { const s = getComputedStyle(e); return { h: e.getBoundingClientRect().height, r: s.borderTopLeftRadius, f: s.fontWeight }; });
    expect(btn.h).toBeGreaterThanOrEqual(43.5); expect(btn.r).toBe('16px'); expect(btn.f).toBe('600');
    const seg = await page.locator('.sp .seg button').first().evaluate((e) => ({ h: e.getBoundingClientRect().height, r: getComputedStyle(e).borderTopLeftRadius }));
    expect(seg.h).toBeGreaterThanOrEqual(43.5); expect(seg.r).toBe('16px');
    expect(await page.locator('.sp .set-head h2').first().evaluate((e) => getComputedStyle(e).fontSize)).toBe('24px');
    expect(await page.locator('.topbar .frame-title').first().evaluate((e) => getComputedStyle(e).fontSize)).toBe('24px');   // the frame's title (ONE APP 1k)
    for (const url of ['/account', '/downloads']) {
      await open(page, url);
      const r = await page.locator('.sp .btn').first().evaluate((e) => getComputedStyle(e).borderTopLeftRadius);
      expect(r, url).toBe('16px');
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  });
}
