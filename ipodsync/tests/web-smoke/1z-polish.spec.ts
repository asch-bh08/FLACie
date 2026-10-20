import { test, expect, Page } from '@playwright/test';

// Loose ends from the earlier phases: /charts in design "I", the artist page and song rows on a phone, the device picker on the phone bar.

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

// a page is read once it is live (the server-drawn copy is replaced when the live connection starts)
async function live(page: Page, url: string) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto(url);
  await ws; await page.waitForTimeout(800);
}

const font = (l: ReturnType<Page['locator']>) => l.evaluate((e) => getComputedStyle(e).fontFamily);
const radius = (l: ReturnType<Page['locator']>) => l.evaluate((e) => parseFloat(getComputedStyle(e).borderRadius));
const noSideScroll = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1);

for (const [name, size] of [['phone', { width: 390, height: 844 }], ['pc', { width: 1440, height: 900 }]] as const) {
  test.describe(name, () => {
    test.beforeEach(async ({ page }) => { await page.setViewportSize(size); await signIn(page); });

    test('charts: serif Explore title, song-style rows, rounded covers', async ({ page }) => {
      await live(page, '/charts');
      const h1 = page.locator('.page-head h1');
      await expect(h1.locator('i')).toHaveText('Explore');
      expect(await font(h1)).toContain('Instrument Serif');
      // the mock has no internet chart: draw one row in the page's own markup so the styles can be read
      await page.evaluate(() => {
        const ol = document.createElement('ol'); ol.className = 'chart-list';
        ol.innerHTML = '<li class="chart-row"><span class="rank">1</span><div class="cover" style="width:48px;height:48px"><img alt="" src="data:image/gif;base64,R0lGODlhAQABAAAAACw="></div><span class="chart-text"><b>Song</b><small>Artist</small></span><button class="btn">Play</button></li>';
        document.querySelector('.mood-chips')!.after(ol);
      });
      const row = page.locator('.chart-row').first();
      await expect(row).toBeVisible();
      expect(await radius(row)).toBeGreaterThanOrEqual(14);
      expect(await radius(row.locator('.cover').first())).toBeGreaterThanOrEqual(10);
      expect(await font(row.locator('.chart-text b'))).toContain('Archivo');
      expect(await font(row.locator('.rank'))).toContain('DM Mono');
      expect(await noSideScroll(page)).toBe(true);
      await page.screenshot({ path: `test-results/shots/polish-charts-${size.width}.png` });
    });

    test('artist: round picture, no big gap under Play / Shuffle', async ({ page }) => {
      await live(page, '/artist/Hires%20Lab');
      const cover = page.locator('.detail-head .cover.detail-cover');
      await expect(cover).toHaveClass(/round/);
      await expect(cover).toBeVisible();
      const box = (await cover.boundingBox())!;
      expect(Math.abs(box.width - box.height)).toBeLessThan(2);
      expect(await cover.evaluate((e) => parseFloat(getComputedStyle(e).borderTopLeftRadius))).toBe(50);
      const actions = (await page.locator('.detail-actions').boundingBox())!;
      const albums = (await page.locator('h2.section').first().boundingBox())!;
      expect(albums.y - (actions.y + actions.height)).toBeLessThan(60);
      expect(await noSideScroll(page)).toBe(true);
      await page.screenshot({ path: `test-results/shots/polish-artist-${size.width}.png` });
    });
  });
}

test.describe('phone only', () => {
  test.beforeEach(async ({ page }) => { await page.setViewportSize({ width: 390, height: 844 }); await signIn(page); });

  test('song rows: the source badge sits under the title, titles keep the full width', async ({ page }) => {
    await live(page, '/songs');
    const row = page.locator('.tracks .track').first();
    await expect(row).toBeVisible();
    const src = row.locator('.track-text .track-src');
    await expect(src).toBeVisible();
    expect((await src.innerText()).trim().length).toBeGreaterThan(0);
    await expect(row.locator('.pills .badge:not(.fmt)')).toBeHidden();
    const title = (await row.locator('.track-title').boundingBox())!;
    const time = (await row.locator('.track-time').boundingBox())!;
    expect(time.x - (title.x + title.width)).toBeLessThan(30);
    await page.screenshot({ path: 'test-results/shots/polish-songs-390.png' });
  });

  test('bar: the device picker is back at 360px and nothing overflows', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 740 });
    await page.goto('/artist/Hires%20Lab');
    await page.getByRole('link', { name: /Lab Album/ }).first().click();
    await page.locator('.tracks .track-main').first().click();
    await expect(page.locator('.player')).toBeVisible();
    await page.waitForTimeout(1500);
    const dev = page.locator('.player .devpick button');
    await expect(dev).toBeVisible();
    const p = (await page.locator('.player').boundingBox())!;
    const d = (await dev.boundingBox())!;
    expect(d.x).toBeGreaterThanOrEqual(p.x); expect(d.x + d.width).toBeLessThanOrEqual(p.x + p.width + 1);
    expect(await noSideScroll(page)).toBe(true);
    const title = (await page.locator('.player .bar-text b').boundingBox())!;
    expect(title.width).toBeGreaterThan(60);
    await page.screenshot({ path: 'test-results/shots/polish-bar-360.png' });
  });
});
