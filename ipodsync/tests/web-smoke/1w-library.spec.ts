import { test, expect, Page } from '@playwright/test';
// (named 1w- so it runs before 2-login, whose wrong-password tests lock this address out of signing in)

// ONE APP 1a: the web's Library page is the phone's Library tab (Modern.kt LibraryHome): the kit Title, your things as tiles, your playlists
// with an "All" small button, then Browse as tiles; tiles are kit-sized (72 tall) and the page never scrolls sideways at phone width.

const STATE = 'test-results/library-state.json';
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

for (const [w, h] of [[390, 844], [1366, 768]]) {
  test(`library page at ${w}x${h}: kit title, tiles in the phone's order, no sideways scroll`, async ({ page }) => {
    await page.setViewportSize({ width: w, height: h });
    await open(page, '/library');
    await expect(page.locator('h1.k-title')).toHaveText(/Your library/);
    const names = await page.locator('.lib-tile b').allTextContents();
    expect(names).toEqual(['Favourites', 'Playlists', 'Artists', 'Albums', 'Songs', 'Genres']);
    const hrefs = await page.locator('.lib-tile').evaluateAll((as) => as.map((a) => a.getAttribute('href')));
    expect(hrefs).toEqual(['/favorites', '/playlists', '/artists', '/albums', '/songs', '/genres']);
    const tile = await page.locator('.lib-tile').first().boundingBox();
    expect(tile!.height).toBeGreaterThanOrEqual(63.5);
    await expect(page.locator('.lib-section-head .k-btn-sm')).toBeVisible();
    const sideways = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(sideways).toBeLessThanOrEqual(0);
    await page.locator('.lib-tile', { hasText: 'Albums' }).click();
    await expect(page).toHaveURL(/\/albums$/);
  });
}
