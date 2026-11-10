import { test, expect, Page } from '@playwright/test';

// A Jellyfin that takes a song request and never answers left the song buffering for good (no time limit on the first reply). The server now gives
// Jellyfin 15 s for its first reply; the player then says "Couldn't reach Jellyfin", tries the song once more, and skips it when that fails too.
// The mock is told to go quiet for the next song asked for (GET /__stream?hang=next): only that song, every request for it.

const MOCK = `http://127.0.0.1:${process.env.MOCK_PORT || 8196}`;

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

test.afterEach(async ({ request }) => { await request.get(`${MOCK}/__stream?hang=`); });

test('a song Jellyfin never answers for says so, is tried once more, then skipped', async ({ page, request }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  // the songs the player asks for (not the early read of the next song, not the seek bar's whole-file read)
  const asked: string[] = [];
  page.on('request', (r) => {
    const u = new URL(r.url());
    if (u.pathname === '/stream' && !u.searchParams.has('pre') && !u.searchParams.has('wave')) asked.push(u.searchParams.get('p') || '');
  });
  await page.goto('/artist/Brass%20Monkeys');
  await page.getByRole('link', { name: /Brass Album/ }).first().click();
  await request.get(`${MOCK}/__stream?hang=next`);
  await page.locator('.tracks .track-main').first().click();
  await expect.poll(() => asked.length, { timeout: 5000 }).toBeGreaterThan(0);
  const first = asked[0];
  // after Jellyfin's 15 s, the player says why and tries the song again
  const toast = page.locator('.toast');
  await expect(toast).toContainText("Couldn't reach Jellyfin", { timeout: 25_000 });
  await expect.poll(() => asked.filter((p) => p === first).length, { timeout: 5000 }).toBeGreaterThanOrEqual(2);
  // that fails the same way: the song is skipped, as other playback errors are, and the next song plays
  await expect.poll(() => asked.some((p) => p !== first), { timeout: 25_000 }).toBe(true);
  expect(asked.filter((p) => p === first).length, 'tried once more, not over and over').toBe(2);
  await expect(toast).toContainText(/Couldn't reach Jellyfin.*skipped/i);
  await expect.poll(() => page.evaluate(() => (window as any).__flaciePlayingAt || 0), { timeout: 15_000 }).toBeGreaterThan(0);
  const counts = await (await request.get(`${MOCK}/__counts`)).json();
  expect(counts.streamHung).toBeGreaterThanOrEqual(2);
});
