import { test, expect, Page } from '@playwright/test';

// Home and the menu in design I (docs/superpowers/specs/design-2026-10-19/J-Home-pc.html and J-Home-phone.html): the cover of what played last fading into the
// ground, a mono greeting over a serif headline, Shuffle all, the mood chips, serif shelf titles with the last word in italics, and a shorter menu: Explore is part
// of Search, Import lives on Playlists, Downloads is in the Dashboard (administrators only).

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

test.describe('the menu', () => {
  test.beforeEach(async ({ page }) => { await page.setViewportSize({ width: 1440, height: 900 }); await signIn(page); });

  test('is short: Home, Search, Favourites, Playlists, Devices, Jam, Headphone test and (for an administrator) Dashboard', async ({ page }) => {
    const links = (await page.locator('#main-rail a').allInnerTexts()).map((t) => t.trim());
    expect(links).toEqual(['Home', 'Search', 'Favourites', 'Playlists', 'Devices', 'Jam', 'Headphone test', 'Dashboard']);
  });

  test('the logo reads "FLACie" with no gap before the "ie"', async ({ page }) => {
    const gap = await page.locator('.rail .wordmark, .topbar .wordmark').first().evaluate((el) => {
      const r = document.createRange(); r.selectNodeContents(el.firstChild!); const a = r.getBoundingClientRect();
      const b = el.querySelector('i')!.getBoundingClientRect(); return b.left - a.right;
    });
    expect(Math.abs(gap)).toBeLessThan(2);
  });

  test('Search with nothing typed is where Explore was: its tabs and the overview', async ({ page }) => {
    await page.goto('/search');
    await expect(page.getByRole('tablist', { name: 'Explore' })).toBeVisible({ timeout: 15_000 });
    await expect(page.locator('#main-rail a', { hasText: 'Search' })).toHaveClass(/active/);
    // the old address still works
    await page.goto('/explore');
    await expect(page.getByRole('tablist', { name: 'Explore' })).toBeVisible();
  });

  test('Import is on the Playlists page; Downloads is in the Dashboard', async ({ page }) => {
    await page.goto('/playlists');
    await expect(page.getByRole('link', { name: 'Import a playlist' })).toHaveAttribute('href', '/import');
    await page.goto('/dashboard');
    await expect(page.locator('main').getByRole('link', { name: 'Downloads', exact: true })).toHaveAttribute('href', '/downloads');
  });
});

for (const [w, h] of [[390, 844], [1440, 900]]) {
  test.describe(`Home at ${w} x ${h}`, () => {
    test.beforeEach(async ({ page }) => { await page.setViewportSize({ width: w, height: h }); await signIn(page); });

    test('a cover hero with a mono greeting, a serif headline and Shuffle all; the mood chips; serif shelf titles', async ({ page }) => {
      // the page is read once it is live (the first copy shown on load is replaced when the live connection starts)
      const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
      await page.goto('/');
      await ws; await page.waitForTimeout(800);
      const hero = page.locator('.home-hero');
      await expect(hero).toBeVisible({ timeout: 15_000 });
      expect(await hero.locator('.home-greet').evaluate((e) => getComputedStyle(e).fontFamily)).toContain('DM Mono');
      const h1 = hero.locator('h1');
      expect(await h1.evaluate((e) => getComputedStyle(e).fontFamily)).toContain('Instrument Serif');
      await expect(h1.locator('i')).toHaveCount(1);
      await expect(page.getByRole('button', { name: /Shuffle/ }).first()).toBeVisible();
      await expect(page.getByRole('group', { name: 'Moods' })).toBeVisible();
      const shelf = page.locator('.shelf-head h2').first();
      expect(await shelf.evaluate((e) => getComputedStyle(e).fontFamily)).toContain('Instrument Serif');
      await expect(shelf.locator('i')).toHaveCount(1);
      // the page fills the width with no sideways scroll
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(w);
    });
  });
}
