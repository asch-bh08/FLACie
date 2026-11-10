import { test, expect, Page, BrowserContext } from '@playwright/test';

// A big library's Albums and Artists pages draw only the tiles on or near the screen: with 20,000 songs (1,200 more albums, 400 more artists) they used to
// send 2.6 MB and build 12,300 elements (Albums; Artists 0.87 MB and 4,250). The grid looks and behaves as before: the same columns, every tile reachable by scrolling, the last
// album at the very end, and Back returns to the same tiles.
//
// The size checks need the mock's big library, on a fresh pair of servers (stop any running ones first):
//   BIG_SONGS=20000 npx playwright test 1z-big-grid --trace off --reporter=line
// With the normal small mock (every other spec) they are skipped, and only the checks that every tile is there run.

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

// how many the page's header says ("1,210 albums", "12 of 1,210 albums")
const headCount = async (page: Page) => Number(((await page.locator('.page-head .sub').textContent()) || '').replace(/[^\d ]/g, ' ').trim().split(/\s+/).pop());
const elements = (page: Page) => page.evaluate(() => document.getElementsByTagName('*').length);

// opens a page from inside the app (as the tabs do) and counts what the server sent for it over the live connection
async function openCounting(page: Page, context: BrowserContext, path: string) {
  const cdp = await context.newCDPSession(page);
  await cdp.send('Network.enable');
  let bytes = 0;
  cdp.on('Network.webSocketFrameReceived', (e: any) => { bytes += e.response.payloadData.length; });
  await page.evaluate((p) => (window as any).Blazor.navigateTo(p), path);
  await expect(page.locator('.grid .card').first()).toBeVisible();
  await page.waitForTimeout(2500);
  await cdp.detach();
  return bytes;
}

// the grid's rows as drawn: how many tiles each row holds, and the columns the page's own layout makes
const rowsOf = (page: Page) => page.evaluate(() => {
  const g = document.querySelector('.grid') as HTMLElement;
  const cols = getComputedStyle(g).gridTemplateColumns.split(' ').filter(Boolean).length;
  const tops = new Map<number, number>();
  for (const c of g.querySelectorAll(':scope > .card')) { const t = Math.round(c.getBoundingClientRect().top + scrollY); tops.set(t, (tops.get(t) || 0) + 1); }
  return { cols, rows: [...tops.entries()].sort((a, b) => a[0] - b[0]).map((e) => e[1]) };
});

test.describe('small library', () => {
  for (const path of ['/albums', '/artists']) {
    test(`${path}: every tile is there, in full rows`, async ({ page }) => {
      await page.setViewportSize({ width: 1440, height: 900 });
      await signIn(page);
      await page.evaluate((p) => (window as any).Blazor.navigateTo(p), path);
      await expect(page.locator('.grid .card').first()).toBeVisible();
      const n = await headCount(page);
      test.skip(n > 200, 'a big library: the checks below are for the small mock');
      await expect(page.locator('.grid > .card')).toHaveCount(n);
      const { cols, rows } = await rowsOf(page);
      for (const r of rows.slice(0, -1)) expect(r).toBe(cols);
    });
  }
});

test.describe('big library (BIG_SONGS=20000)', () => {
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await signIn(page);
    await page.evaluate(() => (window as any).Blazor.navigateTo('/albums'));
    await expect(page.locator('.grid .card').first()).toBeVisible();
    test.skip((await headCount(page)) < 1000, 'needs the big mock library: BIG_SONGS=20000 on a fresh server (see the top of this file)');
    await page.evaluate(() => (window as any).Blazor.navigateTo('/'));
    await page.waitForTimeout(1500);
  });

  // the last tile in A-Z order (the mock's library, with the big one added): the same as when every tile was drawn
  for (const [path, last] of [['/albums', '/album/prodigy%7Cwhatevillurksep'], ['/artists', '/artist/The%20Prodigy']]) {
    test(`${path}: small to send and to build, and the last tile is at the end`, async ({ page, context }) => {
      test.setTimeout(120_000);
      const bytes = await openCounting(page, context, path);
      const all = await elements(page);
      const cards = await page.locator('.grid > .card').count();
      const firstText = (await page.locator('.grid > .card').first().textContent()) || '';
      console.log(`${path}: ${Math.round(bytes / 1024)} KB sent, ${all} elements, ${cards} tiles drawn of ${await headCount(page)}`);
      // before: 2.6 MB and 12,300 elements for Albums, 0.87 MB and 4,250 for Artists
      expect(bytes).toBeLessThan(300_000);
      expect(all).toBeLessThan(3000);
      // a full page load is small too (the page's HTML comes with the first tiles only)
      const html = await (await page.request.get(path)).text();
      expect(html.length).toBeLessThan(300_000);
      // the page is as tall as every tile would make it, so the scroll bar and Back's place are the same
      const n = await headCount(page);
      const geo = await page.evaluate(() => {
        const g = document.querySelector('.grid') as HTMLElement, cs = getComputedStyle(g);
        const cards = [...g.querySelectorAll(':scope > .card')] as HTMLElement[];
        const cols = cs.gridTemplateColumns.split(' ').filter(Boolean).length;
        const pitch = cards[cols].getBoundingClientRect().top - cards[0].getBoundingClientRect().top;
        return { cols, pitch, gap: parseFloat(cs.rowGap), height: g.getBoundingClientRect().height };
      });
      expect(Math.abs(geo.height - (Math.ceil(n / geo.cols) * geo.pitch - geo.gap))).toBeLessThan(4);
      // scrolling to the end shows the last tile, in its own column, and the rows before it are full
      await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
      await expect(page.locator('.grid > .card').last()).toHaveAttribute('href', last);
      await expect(page.locator('.grid > .card').last()).toBeInViewport();
      const { cols, rows } = await rowsOf(page);
      expect(rows.length).toBeGreaterThan(2);
      for (const r of rows.slice(0, -1)) expect(r).toBe(cols);
      expect(rows[rows.length - 1]).toBe(n % cols || cols);
      expect(await elements(page)).toBeLessThan(3000);
      // and back at the top, the first tiles again
      await page.evaluate(() => window.scrollTo(0, 0));
      await expect(page.locator('.grid > .card').first()).toHaveText(firstText);
      await expect(page.locator('.grid > .card').first()).toBeInViewport();
    });
  }

  test('the columns are the same at every width, and mid-way down every row is full', async ({ page }) => {
    test.setTimeout(120_000);
    await page.evaluate(() => (window as any).Blazor.navigateTo('/albums'));
    await expect(page.locator('.grid .card').first()).toBeVisible();
    // 168 px tiles (134 px on a phone) with 18 px (14 px) between them, as many as fit
    for (const [w, want] of [[1440, 0], [1100, 0], [761, 0], [390, 2]] as const) {
      await page.setViewportSize({ width: w, height: 900 });
      await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight * 0.4));
      await page.waitForTimeout(800);
      const { cols, rows } = await rowsOf(page);
      if (want) expect(cols).toBe(want);
      expect(cols).toBeGreaterThan(1);
      expect(rows.length).toBeGreaterThan(2);
      for (const r of rows.slice(0, -1)) expect(r, `${w} px`).toBe(cols);
      // the tiles under the middle of the screen are drawn
      const hit = await page.evaluate(() => { const g = document.querySelector('.grid')!.getBoundingClientRect(); const e = document.elementFromPoint(g.left + 40, innerHeight / 2); return !!e?.closest('.card'); });
      expect(hit, `${w} px`).toBe(true);
    }
  });

  test('Back to Albums comes back to the same tiles', async ({ page }) => {
    test.setTimeout(120_000);
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.evaluate(() => (window as any).Blazor.navigateTo('/albums'));
    await expect(page.locator('.grid .card').first()).toBeVisible();
    await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight * 0.5));
    await page.waitForTimeout(800);
    const before = await page.evaluate(() => window.scrollY);
    // the first tile wholly on screen
    const name = await page.evaluate(() => { const c = [...document.querySelectorAll('.grid > .card')].find((c) => c.getBoundingClientRect().top >= 60)!; return c.querySelector('.card-title')!.textContent; });
    await page.locator('.grid > .card', { has: page.locator('.card-title', { hasText: new RegExp(`^${name}$`) }) }).click();
    await page.waitForURL(/\/album\//);
    await page.waitForTimeout(1000);
    await page.goBack();
    await expect.poll(() => page.evaluate(() => window.scrollY), { timeout: 8000 }).toBeGreaterThan(before - 150);
    expect(await page.evaluate(() => window.scrollY)).toBeLessThan(before + 150);
    await expect(page.locator('.grid > .card', { has: page.locator('.card-title', { hasText: new RegExp(`^${name}$`) }) })).toBeInViewport();
  });
});
