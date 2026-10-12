import { test, expect, Page, APIRequestContext, Browser } from '@playwright/test';

// Beta 52 load behaviour in the browser, against the mock Jellyfin's request counters (GET /__counts, /__images, /__reset, /__stream?delay=ms):
//  - a burst of 40 skips opens a bounded number of Jellyfin streams and the Hi-Res badge never claims anything wrong
//  - a stream the browser abandons after a skip is abandoned by the server too (the mock sees it cut)
//  - the next song's early read is a small Range request
//  - Settings shows the cache size and a Clear cache button
//  - a cover is fetched from Jellyfin once, at one small size, and a second view (another browser) costs Jellyfin nothing
const MOCK = 'http://127.0.0.1:8196';
const counts = async (r: APIRequestContext) => (await r.get(`${MOCK}/__counts`)).json() as Promise<Record<string, number>>;
const images = async (r: APIRequestContext) => (await r.get(`${MOCK}/__images`)).json() as Promise<{ total: number; distinct: number; repeats: number; variants: string[]; urls: Record<string, number> }>;

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(1000);
}

// the lab songs: title prefix -> the badge they must end up with (the same table as 1e-hires.spec.ts)
const LAB: Record<string, string> = { 'Lab 1': 'HI-RES', 'Lab 2': 'FLAC', 'Lab 3': 'FLAC', 'Lab 4': 'FLAC', 'Lab 5': 'HI-RES', 'Lab 6': 'HI-RES', 'Lab 7': 'MP3', 'Lab 8': 'HI-RES' };
const norm = (s: string) => (/hi-?res/i.test(s) ? 'HI-RES' : s.trim().toUpperCase());

async function playLab(page: Page) {
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  await page.locator('.tracks .track-main').first().click();
  await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 20_000 });
  await expect.poll(async () => (await page.locator('.player .seek span').first().innerText()), { timeout: 15_000 }).not.toBe('0:00');
}

test.describe.serial('beta 52 load', () => {
  test('40 quick skips open a bounded number of streams and the Hi-Res badge is never wrong', async ({ page, request }) => {
    test.setTimeout(180_000);
    await signIn(page);
    await playLab(page);
    await request.get(`${MOCK}/__reset`);
    await page.getByRole('button', { name: 'Open Now Playing' }).click();
    await expect(page.locator('.np-titles h2')).toBeVisible();
    const wrong: string[] = [];
    // the 40 presses are made inside the page, 30 ms apart: the spacing must not depend on how long the test runner takes per click
    const seen = await page.evaluate(async () => {
      const out: { title: string; badge: string }[] = [];
      for (let i = 0; i < 40; i++) {
        const label = i % 8 < 4 ? 'Next' : 'Previous';
        (document.querySelector(`.player button[aria-label="${label}"]`) as HTMLElement | null)?.click();
        await new Promise((r) => setTimeout(r, 30));
        out.push({ title: document.querySelector('.np-titles h2')?.textContent?.trim() ?? '', badge: document.querySelector('.np-meta .badge.fmt')?.textContent?.trim() ?? '' });
      }
      return out;
    });
    for (const s of seen) {
      const want = Object.entries(LAB).find(([p]) => s.title.startsWith(p))?.[1];
      if (want && s.badge && want !== 'HI-RES' && norm(s.badge) === 'HI-RES') wrong.push(`${s.title}: ${s.badge}`);
    }
    expect(wrong, 'a non-Hi-Res song claimed Hi-Res while skipping').toEqual([]);
    // settle: the song that is left plays and its badge is the right one, and stays
    await expect.poll(() => page.evaluate(() => (window as any).flacie.ready()), { timeout: 15_000 }).toBe(true);
    const title = (await page.locator('.np-titles h2').textContent())!.trim();
    const want = Object.entries(LAB).find(([p]) => title.startsWith(p))![1];
    await expect.poll(async () => norm((await page.locator('.np-meta .badge.fmt').first().textContent()) ?? ''), { timeout: 20_000 }).toBe(want);
    for (let i = 0; i < 4; i++) { await page.waitForTimeout(300); expect(norm((await page.locator('.np-meta .badge.fmt').first().textContent()) ?? ''), `${title} badge is stable`).toBe(want); }
    const c = await counts(request);
    // 40 skips: the songs passed over never open a stream (the page drops them before they start), the last one and one early read do
    expect(c.stream, 'Jellyfin streams opened by 40 quick skips').toBeLessThanOrEqual(12);
    expect(c.item, 'single-song reads').toBeLessThanOrEqual(14);
    // nothing is left running on the server
    await expect.poll(async () => (await (await page.request.get('/api/admin/load')).json()).openStreams, { timeout: 15_000 }).toBe(0);
    const load = await (await page.request.get('/api/admin/load')).json();
    expect(load.gate.peakInFlight, 'most requests at once').toBeLessThanOrEqual(8);
    expect(load.gate.inFlight).toBe(0);
    expect(load.gate.breaker).toBeDefined();
  });

  test('a stream abandoned after a skip is cut on the Jellyfin side too', async ({ page, request }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    await request.get(`${MOCK}/__stream?delay=500`);   // every song takes ~3 s to arrive
    try {
      await request.get(`${MOCK}/__reset`);
      const next = page.getByRole('button', { name: 'Next' }).first();
      for (let i = 0; i < 6; i++) { await next.click({ noWaitAfter: true }); await page.waitForTimeout(900); }   // each skip is slow enough to open its stream, too quick to finish it
      await expect.poll(async () => (await counts(request)).streamAborted, { timeout: 15_000, message: 'streams the mock saw cut' }).toBeGreaterThanOrEqual(2);
    } finally {
      await request.get(`${MOCK}/__stream?delay=0`);
    }
    // and nothing keeps running: the server has no open stream once the last one is done
    await expect.poll(async () => (await (await page.request.get('/api/admin/load')).json()).openStreams, { timeout: 20_000 }).toBeLessThanOrEqual(1);
  });

  test('the next song is read early with a small Range request', async ({ page, request }) => {
    test.setTimeout(90_000);
    await signIn(page);
    await request.get(`${MOCK}/__reset`);
    await playLab(page);
    await expect.poll(() => page.evaluate(() => (window as any).__flaciePrefetches || 0), { timeout: 20_000 }).toBeGreaterThanOrEqual(1);
    await expect.poll(async () => (await counts(request)).streamCapped, { timeout: 20_000, message: 'the early read reached Jellyfin as a 1 MiB Range' }).toBeGreaterThanOrEqual(1);
  });

  test('Settings shows how big the cache is and has a Clear cache button', async ({ page }) => {
    await signIn(page);
    await page.goto('/settings');
    const size = page.locator('#cache-size');
    await expect(size).toHaveText(/^\d+(\.\d)? ?(B|KB|MB|GB|TB)$/, { timeout: 20_000 });
    await expect(page.locator('#cache-parts')).toContainText('covers');
    const btn = page.getByRole('button', { name: 'Clear cache' });
    await expect(btn).toBeVisible();
    await btn.click();
    await expect(page.locator('#cache-note')).toContainText('Cache cleared', { timeout: 20_000 });
    const load = await (await page.request.get('/api/admin/load')).json();
    expect(load.cache.coverFiles).toBe(0);
    expect(load.cache.covers).toBe(0);
  });

  async function viewAlbums(browser: Browser, request: APIRequestContext) {
    const ctx = await browser.newContext({ baseURL: 'http://127.0.0.1:5290' });   // a browser that has never seen a cover: nothing in its own cache
    const page = await ctx.newPage();
    await signIn(page);
    await page.goto('/albums');
    await expect(page.locator('.card').first()).toBeVisible({ timeout: 20_000 });
    await page.waitForFunction(() => { const imgs = [...document.images].filter((i) => i.src.includes('/art/')); return imgs.length > 0 && imgs.every((i) => i.complete); }, undefined, { timeout: 20_000 });
    await page.waitForTimeout(1500);
    const shown = await page.evaluate(() => [...document.images].filter((i) => i.src.includes('/art/')).length);
    await ctx.close();
    return shown;
  }

  test('a cover is fetched from Jellyfin once at one small size, and a second view costs Jellyfin nothing', async ({ browser, request }) => {
    test.setTimeout(120_000);
    await request.get(`${MOCK}/__reset`);
    const shown = await viewAlbums(browser, request);
    expect(shown).toBeGreaterThan(0);
    const first = await images(request);
    expect(first.total, 'covers fetched for the first view').toBeGreaterThan(0);
    expect(first.repeats, 'a cover asked for twice').toBe(0);
    // every picture once: each song's own picture is its cover (AlbumId is not trusted to stand for an album), so the count follows the pictures shown
    expect(first.distinct).toBe(first.total);
    // one fixed size for rows and tiles: 240 px WebP at quality 80, nothing else
    for (const v of first.variants) expect(v, 'cover variant').toBe('w=240 q=80 Webp');
    // another browser (nothing cached in it) looks at the same page: the server answers from its own disk
    const second = await viewAlbums(browser, request);
    expect(second).toBeGreaterThan(0);
    const after = await images(request);
    expect(after.total, 'covers fetched by a second view').toBe(first.total);
  });

  test('only the Now Playing cover asks for the full picture, once', async ({ page, request }) => {
    test.setTimeout(90_000);
    await signIn(page);
    await request.get(`${MOCK}/__reset`);
    await playLab(page);
    await page.getByRole('button', { name: 'Open Now Playing' }).click();
    await expect(page.locator('.np-cover img').first()).toBeVisible({ timeout: 15_000 });
    await expect.poll(async () => (await images(request)).total, { timeout: 15_000 }).toBeGreaterThanOrEqual(1);
    const im = await images(request);
    expect(im.repeats).toBe(0);
    for (const v of im.variants) expect(['w=240 q=80 Webp', 'w=full q=100 '], v).toContain(v);
  });
});
