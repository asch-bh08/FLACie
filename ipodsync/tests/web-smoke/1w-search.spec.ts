import { test, expect, Page } from '@playwright/test';
// (named 1w- so it runs before 2-login, whose wrong-password tests lock this address out of signing in)

// ONE APP 1b: Search on the kit: the search field is the kit pill (48 tall, r24; 44 when the window is short), the page title is the kit Title,
// the section headings are kit Sections and the kind filters kit small buttons (one on at a time).

const STATE = 'test-results/search-state.json';
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

for (const [w, h, tall] of [[1366, 900, 48], [390, 640, 44]]) {
  test(`search at ${w}x${h}: kit field ${tall} tall, kit title and sections, filters as small buttons`, async ({ page }) => {
    await page.setViewportSize({ width: w, height: h });
    await open(page, '/search?q=aurora');
    const field = await page.locator('form.top-search').evaluate((e) => { const s = getComputedStyle(e); return { h: e.getBoundingClientRect().height, r: s.borderTopLeftRadius }; });
    expect(field.h).toBeCloseTo(tall, 0);
    expect(field.r).toBe('24px');
    await expect(page.locator('h2.section.k-section').first()).toBeVisible();
    const size = await page.locator('h2.section.k-section').first().evaluate((e) => getComputedStyle(e).fontSize);
    expect(size).toBe('24px');
    const kinds = page.getByTestId('kinds').locator('.k-btn-sm');
    expect(await kinds.count()).toBeGreaterThan(1);
    // one button shape: the filters are rounded rectangles like every other button, not full pills
    expect(await kinds.first().evaluate((e) => getComputedStyle(e).borderTopLeftRadius)).toBe('16px');
    await kinds.nth(1).click();
    await expect(page.getByTestId('kinds').locator('.k-btn-sm.on')).toHaveCount(1);
  });
}
