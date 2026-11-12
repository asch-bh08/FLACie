import { test, expect, Page } from '@playwright/test';

// Home (owner, 11 Nov night): the resume hero with the faded cover, the moods as colourful tiles (Home B), artists on repeat and just arrived (Home C);
// no pick card and no DJ button. And Home with the side menu folded away (owner: "when you fold the side menu away it breaks").
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

for (const size of [{ width: 1440, height: 900 }, { width: 390, height: 844 }]) {
  test(`Home at ${size.width} px: mood tiles, artists on repeat, just arrived; nothing runs off the side`, async ({ page }) => {
    test.setTimeout(120_000);
    await page.setViewportSize(size);
    await signIn(page);
    await page.goto('/');
    await expect(page.locator('.mood-tile').first()).toBeVisible({ timeout: 20_000 });
    await expect(page.locator('.home-pick')).toHaveCount(0);
    await expect(page.locator('.home-dj')).toHaveCount(0);
    // Just arrived is gone (owner, 12 Nov: "pointless"); each mood card shows covers from its songs
    await expect(page.locator('section[aria-label="Just arrived"]')).toHaveCount(0);
    await expect(page.locator('.mood-tile .mood-fan .cover').first()).toBeVisible();
    const wide = () => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1);
    expect(await wide()).toBe(true);
    await page.screenshot({ path: `test-results/home-${size.width}.png` });
    await page.locator('section[aria-label="Artists on repeat"]').scrollIntoViewIfNeeded();
    await page.screenshot({ path: `test-results/home-${size.width}-2.png` });
    // a mood tile shows its songs, the same tile again goes back
    const tile = page.locator('.mood-tile').first(); const name = (await tile.locator('.mood-name').innerText()).trim();
    await tile.click(); await expect(page.locator(`section.shelf[aria-label="${name}"]`)).toBeVisible();
    await tile.click(); await expect(page.locator(`section.shelf[aria-label="${name}"]`)).toHaveCount(0);
    if (size.width > 1000) {
      // a song played first, so the hero has its faded cover (the case that broke)
      await tile.click(); await page.locator('section.shelf .pick').first().click();
      await expect(page.getByRole('button', { name: 'Close Now Playing' })).toBeVisible({ timeout: 20_000 });
      await page.getByRole('button', { name: 'Close Now Playing' }).click(); await page.waitForTimeout(800);
      await tile.click(); await page.goto('/'); await expect(page.locator('.mood-tile').first()).toBeVisible({ timeout: 20_000 });
      await page.waitForTimeout(1200);
      await page.screenshot({ path: 'test-results/home-art.png' });
      // the side menu folded to its icons: Home still fits and lines up
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.getByRole('button', { name: 'Menu', exact: true }).click();
      await page.waitForTimeout(700);
      expect(await wide()).toBe(true);
      const rail = (await page.locator('#main-rail').boundingBox())!, hero = (await page.locator('.home-hero').boundingBox())!;
      expect(hero.x).toBeGreaterThanOrEqual(rail.x + rail.width - 2);
      await page.screenshot({ path: 'test-results/home-folded.png' });
      await page.getByRole('button', { name: 'Menu', exact: true }).click();
    }
  });
}
