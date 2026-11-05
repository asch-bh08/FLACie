import { test, expect, Page } from '@playwright/test';

// F25.2 (owner, 5 Nov: "this page is ugly"): the Import page wraps at every width (no sideways scroll at 375 px or 1600 px, the side menu whole),
// Replace is a switch you can see, the service's mark shows as a link is recognised, and a past import is one compact row.

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

const noSideScroll = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1);

for (const [name, size] of [['phone', { width: 375, height: 812 }], ['pc', { width: 1600, height: 900 }]] as const) {
  test(`the Import page fits at ${size.width} px, with a visible Replace switch and the service's mark`, async ({ page }) => {
    await page.setViewportSize(size);
    await signIn(page);
    await page.goto('/import');
    await expect(page.getByRole('heading', { name: 'Import playlists' })).toBeVisible({ timeout: 20_000 });
    await page.waitForTimeout(1200);
    expect(await noSideScroll(page), 'no sideways scroll').toBe(true);
    // the explanations wrap inside their card and are not in the monospace font
    const text = page.locator('.imp-text').first();
    const card = page.locator('.imp-card').first();
    const tb = (await text.boundingBox())!, cb = (await card.boundingBox())!;
    expect(tb.x + tb.width).toBeLessThanOrEqual(cb.x + cb.width + 1);
    expect(await text.evaluate((e) => getComputedStyle(e).fontFamily)).not.toMatch(/mono|consolas/i);
    if (size.width >= 1024) {
      const nav = page.locator('nav').first();
      if (await nav.count()) { const nb = (await nav.boundingBox())!; expect(nb.x).toBeGreaterThanOrEqual(0); }
    }
    // Replace: a switch with a size you can see, and pressing it turns it on
    const sw = page.locator('.imp-switch .switch');
    const sb = (await sw.boundingBox())!;
    expect(sb.width).toBeGreaterThanOrEqual(30); expect(sb.height).toBeGreaterThanOrEqual(18);
    await page.locator('.imp-switch').click();
    await expect(page.getByTestId('import-replace')).toBeChecked();
    // the service's mark appears as the link is recognised
    const box = page.getByRole('textbox', { name: 'Playlist link' });
    await box.fill('https://music.apple.com/us/playlist/todays-hits/pl.f4d106fed2bd41149aaacabb233eb5eb');
    await expect(page.getByTestId('import-service')).toHaveText('Apple Music');
    await box.fill('https://soundcloud.com/the-prodigy/sets/the-fat-of-the-land-6');
    await expect(page.getByTestId('import-service')).toHaveText('SoundCloud');
    await box.fill('https://tidal.com/browse/playlist/1');
    await expect(page.getByTestId('import-service')).toHaveText('');
    expect(await noSideScroll(page), 'no sideways scroll with a link typed').toBe(true);
    await page.screenshot({ path: `test-results/shots/import-${name}.png`, fullPage: true });
  });
}

test('a file import shows as one compact row: name, source, how many added, date', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await signIn(page);
  await page.goto('/import');
  await expect(page.getByRole('heading', { name: 'Import playlists' })).toBeVisible({ timeout: 20_000 });
  await page.waitForTimeout(1200);
  await page.getByLabel('Choose a playlist file').setInputFiles({ name: 'mine.txt', mimeType: 'text/plain', buffer: Buffer.from('## Mine\nAurora Vale - Harbour 3\nNobody - Not Here\n') });
  const row = page.getByTestId('import-job').filter({ hasText: 'mine.txt' }).first();
  await expect(row).toBeVisible({ timeout: 20_000 });
  await expect(row.locator('.imp-mark')).toHaveText('File');
  await expect(row.locator('small')).toContainText(/\d+ of 2 added · \d+ \w{3}/);
  expect(await noSideScroll(page)).toBe(true);
});
