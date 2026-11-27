import { test, expect, Page } from '@playwright/test';
// (named 1w- so it runs before 2-login, whose wrong-password tests lock this address out of signing in)

// ONE APP 1h: no big empty band under a short page: on a wide screen the album / artist / Favourites header is a column the window's height beside the songs; the Library page fills the window and shares the spare room out.

const STATE = 'test-results/sizes-state.json';
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

const rect = (page: Page, sel: string) => page.locator(sel).first().evaluate((e) => { const r = e.getBoundingClientRect(); return { l: r.left, r: r.right, t: r.top, b: r.bottom, h: r.height }; });

test('1920x1080: an album is two columns, its header as tall as the window, the songs beside it', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 });
  await open(page, '/albums');
  await page.locator('.grid .card').first().click();
  await page.waitForURL(/\/album\//); await page.waitForTimeout(1200);
  const head = await rect(page, '.detail-head'), row = await rect(page, 'main.content .track, main.content [role=row], main.content li');
  expect(head.h).toBeGreaterThan(1080 * 0.8);
  expect(row.l).toBeGreaterThan(head.r);
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
});

for (const [w, h] of [[412, 1000], [1920, 1080]]) {
  test(`${w}x${h}: the Library page reaches the bottom of the window`, async ({ page }) => {
    await page.setViewportSize({ width: w, height: h });
    await open(page, '/library');
    const lib = await rect(page, '.lib-home');
    expect(lib.b).toBeGreaterThan(h * 0.85);
  });
}
