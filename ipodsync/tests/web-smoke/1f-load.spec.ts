import { test, expect, Page, APIRequestContext } from '@playwright/test';

// Timing- and load-sensitive (stream counts, skips, clocks): late in a 45-minute run the same server is still busy with earlier specs' prefetches and
// downloads, which can tip one measurement over. One retry; a real failure fails twice.
test.describe.configure({ retries: 1 });

// Server load: what FLACie Web asks of Jellyfin. The mock Jellyfin counts requests (GET /__counts, POST-less reset at /__reset).
const MOCK = `http://127.0.0.1:${process.env.MOCK_PORT || 8196}`;   // (playwright.config.ts: MOCK_PORT moves the stand-in)
const counts = async (r: APIRequestContext) => (await r.get(`${MOCK}/__counts`)).json() as Promise<Record<string, number>>;

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
}

test.describe.serial('server load', () => {
  test('the format index is filled from a few big library pages, not one request per song', async ({ page, request }) => {
    test.setTimeout(150_000);
    await signIn(page);
    // what this test counts is what happens from here on (the specs before it have read their own songs one by one)
    await request.get(`${MOCK}/__reset`);
    const itemsBefore = (await (await page.request.get('/api/admin/load')).json()).jellyfinCallsSinceStart.items as number;
    // the background read starts about 20 s after the server; poll the admin counters until the plain songs are indexed
    let load: any = {};
    await expect.poll(async () => {
      load = await (await page.request.get('/api/admin/load')).json();
      return load.indexed;
    }, { timeout: 120_000, intervals: [2000] }).toBeGreaterThanOrEqual(39);   // the 39 plain songs (not the Apple Lossless s1); the 8 Hi-Res lab songs are not in the mock's bulk answer
    const c = await counts(request);
    // 48 songs would have been 48 single-song requests one by one; the bulk read is one library page
    expect(c.item, 'single-song reads').toBeLessThan(10);
    expect(load.jellyfinCallsSinceStart.items - itemsBefore, 'library pages and deltas asked for since this test began').toBeLessThan(12);
    // the songs the bulk read could not tell are left alone for an hour instead of five minutes
    expect(load.backingOff).toBeGreaterThanOrEqual(1);
    expect(load.desktopShell).toBe(false);
  });

  test('a burst of 30 API calls asks Jellyfin who the token is only a few times', async ({ request }) => {
    const h = { 'X-Emby-Token': 'mock-token' };
    await request.get(`${MOCK}/__reset`);
    const first = await Promise.all(Array.from({ length: 30 }, () => request.get('/api/account', { headers: h })));
    for (const r of first) expect(r.status()).toBe(200);
    for (let i = 0; i < 30; i++) expect((await request.get('/api/account', { headers: h })).status()).toBe(200);
    const c = await counts(request);
    expect(c.usersMe, 'GET /Users/Me for 60 API calls').toBeLessThanOrEqual(3);
    // a wrong token is still refused (the cache only ever holds a success)
    expect((await request.get('/api/account', { headers: { 'X-Emby-Token': 'wrong-token' } })).status()).toBe(401);
  });

  test('skipping 20 songs opens almost no streams and the last one starts within a few seconds', async ({ page, request }) => {
    test.setTimeout(90_000);
    await signIn(page);
    await page.goto('/songs');
    await expect(page.locator('.track').first()).toBeVisible({ timeout: 30_000 });
    // a song that is not the Apple Lossless one (the mock serves a short WAV for the rest)
    await page.locator('.track', { has: page.locator('.track-title', { hasText: /^Velvet 1$|^Paper 2$|^Lantern \d+$/ }) }).first().locator('button.track-main').click();
    await expect(page.locator('.player')).toBeVisible();
    await expect.poll(async () => (await page.locator('.player .seek span').first().innerText()), { timeout: 15_000 }).not.toBe('0:00');
    await request.get(`${MOCK}/__reset`);
    const next = page.getByRole('button', { name: 'Next' }).first();
    const t0 = Date.now();
    for (let i = 0; i < 20; i++) { await next.click({ noWaitAfter: true }); await page.waitForTimeout(60); }
    const title = (await page.locator('.bar-text b').innerText()).trim();
    // the last song is playing: buffered and its clock moving
    await expect.poll(() => page.evaluate(() => (window as any).flacie.ready()), { timeout: 6000 }).toBe(true);
    await expect.poll(async () => (await page.locator('.player .seek span').first().innerText()), { timeout: 6000 }).not.toBe('0:00');
    const took = Date.now() - t0;
    expect(took, `20 skips and the last song playing (${title})`).toBeLessThan(10_000);
    const c = await counts(request);
    // the songs passed over never opened a stream (the page drops them); the last one and a prefetch or two did (each prefetch is a 1 MiB read, and on a loaded
    // machine a skip takes longer, so a few more songs get their turn): far below the 20 skips
    expect(c.stream, 'Jellyfin streams opened during 20 quick skips').toBeLessThanOrEqual(10);
  });
});
