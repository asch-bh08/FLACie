import { test, expect, Page } from '@playwright/test';

// owner, 12 Nov: at an 800 px window the search's Top result squeezed beside Songs: the artist's name one letter per line, the songs cut to "S..."
async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(1200);
}

for (const width of [800, 1024, 1440]) {
  test(`at ${width} px the Top result and the songs are readable`, async ({ page }) => {
    test.setTimeout(60_000);
    await page.setViewportSize({ width, height: 900 });
    await signIn(page);
    await page.goto('/search?q=hires%20lab');
    const title = page.locator('.sr-hero-title').locator('visible=true').first();
    // written across, not one letter per line (measured once the results stop redrawing)
    await expect.poll(async () => { const b = await title.boundingBox().catch(() => null); return b ? b.width > b.height : null; }, { timeout: 15_000 }).toBe(true);
    const song = page.locator('.sr-section .track-main, .sr-top-songs .track-main').locator('visible=true').first();
    if (await song.count()) expect((await song.boundingBox())!.width).toBeGreaterThan(140);
    await page.screenshot({ path: `test-results/search-${width}.png` });
  });
}
