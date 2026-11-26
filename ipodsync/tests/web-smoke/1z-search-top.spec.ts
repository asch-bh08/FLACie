import { test, expect, Page } from '@playwright/test';
import http from 'node:http';
import path from 'node:path';

// owner, 25 Nov: "when I search heads will roll the first result is a playlist even though the 'top result' says Heads Will Roll song; it's below the
// playlists and quite small; there's wasted space and it's not clear what I have; and if Heads Will Roll downloads are irrelevant keep them irrelevant
// because we have the song". So: the song the library has is the Top result, first and big, marked "In your library"; then Songs, Artists, Albums,
// Playlists; the catalog ("More music to download") comes last, and its copies of the song we have are folded under "Other copies to download".
// owner, 26 Nov: "playlists with an equal hierarchy to the top result ... 50/50; on smaller aspect ratios or phone make the top result first; that could
// be a playlist if I search for 2026". So at 1440 px the Top result and Playlists sit side by side, half each; at 390 px the Top result comes first, full
// width; and a playlist named like the search ("2026") is the Top result, with its cover, name, song count and Play.
// The mock library has Yeah Yeah Yeahs, It's Blitz! (Heads Will Roll). A stand-in catalog runs inside this spec (Deezer's address, redirected by
// FLACIE_CATALOG_REDIRECT to FM_PORT, as in 1i-download-tags.spec.ts).

const FM = Number(process.env.FM_PORT || 8197);
// SHOTS=<tag> also saves full-page pictures under docs/screens-search-2026-11-25 (before/after the redesign)
const SHOTS = process.env.SHOTS || '';
const shotDir = path.join(__dirname, '..', '..', '..', 'docs', 'screens-search-2026-11-25');

const song = (id: number, title: string, artist: string, album: string) => ({ id, title, duration: 222, artist: { name: artist }, album: { title: album, cover_xl: '' } });
const server = http.createServer((req, res) => {
  const u = new URL(req.url!, 'http://x'), p = u.pathname, q = (u.searchParams.get('q') ?? u.searchParams.get('term') ?? '').toLowerCase();
  const json = (o: unknown) => { res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify(o)); };
  if (p === '/api.deezer.com/search' && q.includes('heads will roll')) return json({ data: [
    song(7001, 'Heads Will Roll', 'Yeah Yeah Yeahs', "It's Blitz!"),                         // the one we have: never offered
    song(7002, 'Heads Will Roll (A-Trak Remix)', 'Yeah Yeah Yeahs', 'Heads Will Roll (Remixes)'),   // a copy of it: folded away
    song(7003, 'Heads Will Roll', 'Karaoke Kings', 'Indie Karaoke Vol. 3'),                  // a copy of it: folded away
    song(7004, 'Heads Will Roll Tonight', 'Nova Lane', 'Night Roads'),                         // another song: a row of its own
  ] });
  if (p === '/api.deezer.com/search/album' && q.includes('heads will roll')) return json({ data: [{ id: 7100, title: 'Night Roads', artist: { name: 'Nova Lane' }, nb_tracks: 10, record_type: 'album', cover_xl: '' }] });
  if (p.startsWith('/api.deezer.com/')) return json({ data: [] });
  if (p.startsWith('/itunes.apple.com/')) return json({ results: [] });
  res.writeHead(404); res.end();
});
test.beforeAll(async () => { await new Promise<void>((ok) => server.listen(FM, '127.0.0.1', ok)); });
test.afterAll(async () => { await new Promise<void>((ok) => server.close(() => ok())); });

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

// the person's own playlist holding the song, so a Playlists row is there to sit under the Top result
async function playlistWithTheSong(page: Page) {
  const name = 'Blitz night ' + Date.now().toString().slice(-6);
  await page.goto('/playlists');
  await page.waitForTimeout(1500);
  await page.getByRole('textbox', { name: 'New playlist name' }).fill(name);
  await page.getByRole('button', { name: 'Create' }).click();
  await expect(page).toHaveURL(/\/playlist\//, { timeout: 15_000 });
  // added from the Top result's own menu (the library may still be loading on a fresh server: the search waits for it)
  await page.goto('/search?q=' + encodeURIComponent('heads will roll'));
  const top = page.getByRole('region', { name: 'Top result' });
  await expect(top.locator('.sr-hero-title')).toHaveText('Heads Will Roll', { timeout: 45_000 });
  await top.getByRole('button', { name: 'More for Heads Will Roll' }).click();
  await page.getByRole('menuitem', { name: 'Add to playlist' }).click();
  await page.getByRole('menuitem', { name: `Add to ${name}` }).click();
  await expect(page.locator('.toast')).toContainText('Added');
  return name;
}

const regions = (page: Page) => page.locator('main section[aria-label]').evaluateAll((els) => els.filter((e) => (e as HTMLElement).offsetParent !== null).map((e) => e.getAttribute('aria-label')));
const noSideScroll = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1);

test.describe.serial('the song asked for is the Top result', () => {
  let list = '';
  test('"heads will roll" at 1440 px: the song first and big, then the library, the catalog last with its copies folded', async ({ page }) => {
    test.setTimeout(120_000);
    await page.setViewportSize({ width: 1440, height: 900 });
    await signIn(page);
    list = await playlistWithTheSong(page);
    await page.goto('/search?q=' + encodeURIComponent('heads will roll'));
    const top = page.getByRole('region', { name: 'Top result' });
    await expect(top.locator('.sr-hero-title')).toHaveText('Heads Will Roll', { timeout: 20_000 });
    const dl = page.getByRole('region', { name: 'More music to download' });
    await expect(dl).toBeVisible({ timeout: 20_000 });
    await page.waitForTimeout(800);
    if (SHOTS) await page.screenshot({ path: path.join(shotDir, `web-1440-${SHOTS}.png`), fullPage: true });
    // the Top result is the first thing on the page, above the playlists and the catalog
    const order = await regions(page);
    expect(order[0]).toBe('Top result');
    expect(order.indexOf('Playlists')).toBeGreaterThan(order.indexOf('Songs'));
    expect(order[order.length - 1]).toBe('More music to download');
    const lists = page.getByRole('region', { name: 'Playlists' });
    await expect(lists.locator('.card', { hasText: list })).toBeVisible();
    // owner, 26 Nov: Top result and Playlists side by side, equal halves, level at the top
    const tb = (await top.boundingBox())!, lb = (await lists.boundingBox())!;
    expect(Math.abs(tb.width - lb.width)).toBeLessThanOrEqual(4);
    expect(lb.x).toBeGreaterThan(tb.x + tb.width - 2);
    expect(Math.abs(tb.y - lb.y)).toBeLessThan(40);
    // big: the cover is at least 160 px and the title is the serif heading
    const cover = (await top.locator('.sr-art').first().boundingBox())!;
    expect(cover.width).toBeGreaterThanOrEqual(160);
    // clear what you have: the card says so, and Play and More are there
    await expect(top).toContainText('In your library');
    await expect(top.getByRole('button', { name: /^(Play|Pause) Heads Will Roll$/ })).toBeVisible();
    await expect(top.getByRole('button', { name: 'More for Heads Will Roll' })).toBeVisible();
    // the catalog: the other song is a row, the copies of the song we have are folded under one small line
    await expect(dl.locator('.track-title', { hasText: 'Heads Will Roll Tonight' })).toBeVisible();
    await expect(dl.locator('.track-title', { hasText: /^Heads Will Roll( \(A-Trak Remix\))?$/ })).toHaveCount(0);
    const fold = dl.getByRole('button', { name: /Other copies to download \(2\)/ });
    await expect(fold).toBeVisible();
    await fold.click();
    await expect(dl.locator('.track-title', { hasText: 'Heads Will Roll (A-Trak Remix)' })).toBeVisible();
    expect(await noSideScroll(page)).toBe(true);
  });

  test('"heads will roll" at 390 px: the same order, no side scroll, and the Downloadable chip shows only the catalog', async ({ page }) => {
    test.setTimeout(120_000);
    await page.setViewportSize({ width: 390, height: 844 });
    await signIn(page);
    await page.goto('/search?q=' + encodeURIComponent('heads will roll'));
    const top = page.getByRole('region', { name: 'Top result' });
    await expect(top.locator('.sr-hero-title')).toHaveText('Heads Will Roll', { timeout: 20_000 });
    await expect(page.getByRole('region', { name: 'More music to download' })).toBeVisible({ timeout: 20_000 });
    await page.waitForTimeout(800);
    if (SHOTS) await page.screenshot({ path: path.join(shotDir, `web-390-${SHOTS}.png`), fullPage: true });
    const order = await regions(page);
    expect(order[0]).toBe('Top result');
    expect(order[order.length - 1]).toBe('More music to download');
    // owner, 26 Nov: narrow, the Top result is first and full width, the playlists stacked under it
    const tb = (await top.boundingBox())!, vw = page.viewportSize()!.width;
    expect(tb.width).toBeGreaterThan(vw - 60);
    const lb = await page.getByRole('region', { name: 'Playlists' }).boundingBox();
    if (lb) expect(lb.y).toBeGreaterThanOrEqual(tb.y + tb.height - 2);
    // the top card starts within the first screen, with its title readable
    const t = (await top.locator('.sr-hero-title').boundingBox())!;
    expect(t.y).toBeLessThan(500);
    expect(t.width).toBeGreaterThan(t.height);
    expect(await noSideScroll(page)).toBe(true);
    const kinds = page.getByRole('radiogroup', { name: 'Show only' });
    await kinds.getByRole('radio', { name: 'Downloadable' }).click();
    await expect.poll(() => regions(page)).toEqual(['More music to download']);
    await kinds.getByRole('radio', { name: 'All' }).click();
    await expect(top).toBeVisible();
  });

  // owner, 26 Nov: "that could be a playlist if I search for 2026"
  test('"2026": a playlist named so is the Top result with its cover, name, song count and Play, side by side with Playlists on a wide screen', async ({ page }) => {
    test.setTimeout(120_000);
    await page.setViewportSize({ width: 1440, height: 900 });
    await signIn(page);
    await page.goto('/playlists');
    await page.waitForTimeout(1500);
    await page.getByRole('textbox', { name: 'New playlist name' }).fill('2026');
    await page.getByRole('button', { name: 'Create' }).click();
    await expect(page).toHaveURL(/\/playlist\//, { timeout: 15_000 });
    await page.goto('/search?q=' + encodeURIComponent('heads will roll'));
    const song = page.getByRole('region', { name: 'Top result' });
    await expect(song.locator('.sr-hero-title')).toHaveText('Heads Will Roll', { timeout: 45_000 });
    await song.getByRole('button', { name: 'More for Heads Will Roll' }).click();
    await page.getByRole('menuitem', { name: 'Add to playlist' }).click();
    await page.getByRole('menuitem', { name: 'Add to 2026' }).click();
    await expect(page.locator('.toast')).toContainText('Added');
    await page.goto('/search?q=2026');
    const top = page.getByRole('region', { name: 'Top result' });
    await expect(top.locator('.sr-hero-title')).toHaveText('2026', { timeout: 20_000 });
    await expect(top).toContainText('Playlist');
    await expect(top).toContainText('1 song');
    await expect(top.locator('.sr-art').first()).toBeVisible();
    await expect(top.getByRole('button', { name: /^(Play|Pause) 2026$/ })).toBeVisible();
    const lists = page.getByRole('region', { name: 'Playlists' });
    await expect(lists.locator('.card', { hasText: '2026' })).toBeVisible();
    const tb = (await top.boundingBox())!, lb = (await lists.boundingBox())!;
    expect(Math.abs(tb.width - lb.width)).toBeLessThanOrEqual(4);
    // narrow: the same Top result, first
    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(400);
    expect((await regions(page))[0]).toBe('Top result');
    expect(await noSideScroll(page)).toBe(true);
  });
});
