import { test, expect, Page } from '@playwright/test';

// The browse screens in design "I" (docs/superpowers/specs/design-2026-10-19/J-Browse-*.html): serif section titles with the last word in italics, outlined
// chips with the chosen one filled, rounded covers, DM Mono for times and format chips, and no sideways scroll on a phone or a PC.

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

const font = (l: ReturnType<Page['locator']>) => l.evaluate((e) => getComputedStyle(e).fontFamily);
const radius = (l: ReturnType<Page['locator']>) => l.evaluate((e) => parseFloat(getComputedStyle(e).borderRadius));
const noSideScroll = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1);

for (const [name, size] of [['phone', { width: 390, height: 844 }], ['pc', { width: 1440, height: 900 }]] as const) {
  test.describe(name, () => {
    test.beforeEach(async ({ page }) => { await page.setViewportSize(size); await signIn(page); });

    test('search results: serif section titles with the last word in italics, mono times, rounded covers', async ({ page }) => {
      await page.goto('/search?q=aurora');
      const songs = page.locator('section[aria-label="Songs"] h2.section');
      await expect(songs).toBeVisible();
      expect(await font(songs)).toContain('Instrument Serif');
      const words = (await songs.innerText()).trim().split(/\s+/);
      expect((await songs.locator('i').innerText()).trim()).toBe(words[words.length - 1]);
      expect(await font(page.locator('.tracks .track-time').first())).toContain('DM Mono');
      expect(await radius(page.locator('.tracks .track .cover').first())).toBeGreaterThanOrEqual(10);
      expect(await noSideScroll(page)).toBe(true);
    });

    test('search results: the top result is a rounded card with a serif title', async ({ page }) => {
      await page.goto('/search?q=aurora');
      const hero = page.locator('.sr-hero').first();
      await expect(hero).toBeVisible();
      await expect(hero.locator('.sr-hero-title')).toBeVisible(); expect(await font(hero.locator('.sr-hero-title'))).toContain('Instrument Serif');
      expect(await radius(hero)).toBeGreaterThanOrEqual(20);
    });

    test('albums: serif Explore title in italics, outlined tabs with the chosen one filled, rounded covers', async ({ page }) => {
      await page.goto('/albums');
      const h1 = page.locator('.page-head h1');
      await expect(h1.locator('i')).toHaveText('Explore');
      expect(await font(h1)).toContain('Instrument Serif');
      const on = page.locator('.mood-chips .mood.on');
      await expect(on).toHaveText('Albums');
      const off = page.locator('.mood-chips .mood:not(.on)').first();
      expect(await on.evaluate((e) => getComputedStyle(e).backgroundColor)).not.toBe(await off.evaluate((e) => getComputedStyle(e).backgroundColor));
      expect(await off.evaluate((e) => getComputedStyle(e).borderTopWidth)).toBe('1px');
      expect(await radius(page.locator('.grid .card .cover').first())).toBeGreaterThanOrEqual(14);
      expect(await font(page.locator('.page-head .sub'))).toContain('DM Mono');
      expect(await font(page.locator('.x-ftoggle'))).toContain('Archivo');
      expect(await radius(page.locator('.x-ftoggle'))).toBeLessThanOrEqual(14);
      expect(await noSideScroll(page)).toBe(true);
    });

    test('songs, artists and genres keep their tabs, filters and rows in the same look', async ({ page }) => {
      await page.goto('/songs');
      await expect(page.locator('.page-head h1 i')).toHaveText('Explore');
      await expect(page.getByRole('button', { name: /Shuffle/ })).toBeVisible();
      await expect(page.locator('.tracks .track').first()).toBeVisible();
      expect(await font(page.locator('.tracks .track-time').first())).toContain('DM Mono');
      expect(await noSideScroll(page)).toBe(true);
      for (const p of ['/artists', '/genres']) {
        await page.goto(p);
        await expect(page.locator('.page-head h1 i')).toHaveText('Explore');
        expect(await noSideScroll(page)).toBe(true);
      }
    });

    test('screenshots', async ({ page }) => {
      for (const [file, url] of [['browse-search', '/search?q=aurora'], ['browse-albums', '/albums'], ['browse-songs', '/songs']]) {
        await page.goto(url); await page.waitForTimeout(900);
        await page.screenshot({ path: `../../../docs/screens-new-look/${file}-${size.width}.png` });
      }
    });
  });
}
