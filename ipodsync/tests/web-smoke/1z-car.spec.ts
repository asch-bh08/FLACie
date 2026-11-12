import { test, expect, Page } from '@playwright/test';

// Car mode (owner, 12 Nov: "easy on/off, keeps the screen on, less clutter, nice visualisers and buttons"): /car from the side menu, the song big,
// a live visualiser a click changes, big previous / play / next, Exit back to Home. It fits a phone and a wide window with nothing off the side.
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

for (const size of [{ width: 1440, height: 900 }, { width: 390, height: 844 }, { width: 844, height: 390 }]) {
  test(`car mode at ${size.width}x${size.height}: big buttons, a visualiser, Exit`, async ({ page }) => {
    test.setTimeout(120_000);
    await page.setViewportSize(size);
    await signIn(page);
    // a song playing first
    await page.goto('/');
    await page.locator('.mood-tile').first().click();
    await page.locator('section.shelf .pick').first().click();
    await expect(page.getByRole('button', { name: 'Close Now Playing' })).toBeVisible({ timeout: 20_000 });
    await page.getByRole('button', { name: 'Close Now Playing' }).click(); await page.waitForTimeout(800);
    if (size.width > 1000) await page.getByRole('link', { name: 'Car mode' }).click(); else await page.goto('/car');
    await expect(page.locator('.car')).toBeVisible({ timeout: 20_000 });
    await expect(page.locator('.car-title')).not.toHaveText('Nothing playing');
    for (const name of ['Previous', 'Next', 'Exit car mode']) await expect(page.locator('.car').getByRole('button', { name, exact: true })).toBeVisible();
    // the player bar's other controls too (owner, 12 Nov): shuffle, repeat and a seek bar
    await expect(page.locator('.car').getByRole('button', { name: /^Shuffle (on|off)$/ })).toBeVisible();
    await expect(page.locator('.car').getByRole('button', { name: /^Repeat/ })).toBeVisible();
    await expect(page.locator('.car-seek input[aria-label="Seek"]')).toBeVisible();
    const play = page.locator('.car-play');
    const box = await play.boundingBox();
    expect(box!.height).toBeGreaterThanOrEqual(72);
    // every button inside the window, nothing runs off the side
    expect(await page.evaluate(() => [...document.querySelectorAll('.car button')].every((b) => { const r = b.getBoundingClientRect(); return r.right <= innerWidth + 1 && r.bottom <= innerHeight + 1 && r.left >= -1; }))).toBe(true);
    // a click on the visualiser moves to the next one
    const viz = page.locator('.car-viz');
    await expect(viz).toHaveAttribute('aria-label', /Visualizer/);
    await viz.click(); await expect(viz).toHaveAttribute('aria-label', /Spectrum/);
    // every graph is there: the arrows go forward and back (owner, 12 Nov)
    await page.getByRole('button', { name: 'Previous graph' }).click(); await expect(viz).toHaveAttribute('aria-label', /Visualizer/);
    await page.getByRole('button', { name: 'Previous graph' }).click(); await expect(viz).toHaveAttribute('aria-label', /Stereo/);
    await page.screenshot({ path: `test-results/car-${size.width}x${size.height}.png` });
    await page.locator('.car').getByRole('button', { name: 'Exit car mode' }).click();
    await expect(page.locator('.car')).toHaveCount(0);
  });
}
