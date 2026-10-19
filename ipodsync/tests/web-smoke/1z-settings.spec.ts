import { test, expect, Page } from '@playwright/test';

// Settings and the server pages in design I: serif page titles with the last word in italics, soft rounded cards, outlined chip sub-navs,
// and no sideways scroll. The login page gets the same feel.

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

// A page is read once it is live (the first copy shown on load is replaced when the live connection starts).
async function openLive(page: Page, path: string) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto(path);
  await ws; await page.waitForTimeout(800);
}

const pages: [string, string][] = [
  ['/settings', 'Settings'], ['/account', 'Account'], ['/dashboard', 'Dashboard'], ['/downloads', 'Downloads'],
  ['/import', 'Import playlists'], ['/devices', 'Devices'], ['/jam', 'Jam'],
];

for (const [w, h] of [[390, 844], [1440, 900]]) {
  test.describe(`settings pages at ${w} x ${h}`, () => {
    test.beforeEach(async ({ page }) => { await page.setViewportSize({ width: w, height: h }); await signIn(page); });

    for (const [path, name] of pages) {
      test(`${path}: serif title with an italic last word, no sideways scroll`, async ({ page }) => {
        await openLive(page, path);
        const h1 = page.locator('.sp h1').first();
        await expect(h1).toBeVisible({ timeout: 15_000 });
        expect(await h1.evaluate((e) => getComputedStyle(e).fontFamily)).toContain('Instrument Serif');
        await expect(h1.locator('i')).toHaveCount(1);
        // Account's title is the signed-in name, not a fixed word.
        if (path !== '/account') expect((await h1.innerText()).replace(/\s+/g, ' ').trim().toLowerCase()).toBe(name.toLowerCase());
        expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(w);
      });
    }

    test('Settings: rounded cards and outlined chip sub-nav', async ({ page }) => {
      await openLive(page, '/settings');
      const card = page.locator('.sp .panel.set').first();
      await expect(card).toBeVisible({ timeout: 15_000 });
      expect(parseFloat(await card.evaluate((e) => getComputedStyle(e).borderTopLeftRadius))).toBeGreaterThanOrEqual(18);
      const chip = page.locator('.sp .subnav a').first();
      expect(await chip.evaluate((e) => getComputedStyle(e).fontFamily)).toContain('DM Mono');
      expect(await chip.evaluate((e) => getComputedStyle(e).backgroundColor)).toMatch(/rgba\(0, 0, 0, 0\)|transparent/);
      expect(await page.locator('.sp .set-head h2').first().evaluate((e) => getComputedStyle(e).fontFamily)).toContain('Instrument Serif');
    });

    test('Dashboard: chip sub-nav in the mono face', async ({ page }) => {
      await openLive(page, '/dashboard');
      await expect(page.locator('.sp h1').first()).toBeVisible({ timeout: 15_000 });
      expect(await page.locator('.sp .subnav a').first().evaluate((e) => getComputedStyle(e).fontFamily)).toContain('DM Mono');
    });

    test('Login: serif lede, rounded card, no sideways scroll', async ({ browser }) => {
      const ctx = await browser.newContext({ viewport: { width: w, height: h } });
      const page = await ctx.newPage();
      await page.goto('/login');
      const card = page.locator('.login-card');
      expect(parseFloat(await card.evaluate((e) => getComputedStyle(e).borderTopLeftRadius))).toBeGreaterThanOrEqual(18);
      expect(await page.locator('.login-card .lede').evaluate((e) => getComputedStyle(e).fontFamily)).toContain('Instrument Serif');
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(w);
      await ctx.close();
    });
  });
}
