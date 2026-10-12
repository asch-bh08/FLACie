import { test, expect, Page, BrowserContext, request as pwRequest } from '@playwright/test';
import http from 'node:http';

// "New download" labels and the download tags (beta 54): the small line under a song's artist ("Downloading · Searching Soulseek...", "Recently downloaded · 1s ago")
// in the player bar, Now Playing, the queue and every song list; the phone's POST /api/downloaded; the one hour window.
//
// A stand-in for the user's download stack runs inside this spec (port 8197): slskd (/slskd/api/v0/...) and the file mover (/fm/move, /fm/file). It has one song
// ("Lantern 12" by Brass Monkeys, a Hi-Res file) whose transfer stays "in progress" until the test releases it, so the running stage can be looked at.
// Finished downloads of known ages (20 minutes, 2 hours, a week) are written into the account's log by global-setup.ts before the server is asked anything.
// The mock Jellyfin's songs: Brass Monkeys "Brass Album 1" = Velvet 9, Orbit 10, Harbour 11, Lantern 12.

const FM = Number(process.env.FM_PORT || 8197);
const ORIGIN = `http://127.0.0.1:${FM}`;
const TOKEN = { 'X-Emby-Token': 'mock-token' };
const JSONH = { ...TOKEN, 'content-type': 'application/json' };

const state = { release: false, searchText: new Map<string, string>(), queued: [] as string[], moves: [] as { from: string; to: string }[], keys: new Set<string>(), files: 0 };
const wav = (() => {
  const rate = 8000, n = rate * 3, b = Buffer.alloc(44 + n * 2);
  b.write('RIFF', 0); b.writeUInt32LE(36 + n * 2, 4); b.write('WAVEfmt ', 8); b.writeUInt32LE(16, 16); b.writeUInt16LE(1, 20); b.writeUInt16LE(1, 22);
  b.writeUInt32LE(rate, 24); b.writeUInt32LE(rate * 2, 28); b.writeUInt16LE(2, 32); b.writeUInt16LE(16, 34); b.write('data', 36); b.writeUInt32LE(n * 2, 40);
  return b;
})();
const LANTERN = '@@share\\Music\\Brass Monkeys\\Brass Monkeys - Lantern 12 [Hi-Res].flac';

const server = http.createServer((req, res) => {
  let body = '';
  req.on('data', (c) => (body += c));
  req.on('end', () => {
    const u = new URL(req.url!, ORIGIN), p = u.pathname;
    const json = (code: number, o: unknown) => { res.writeHead(code, { 'content-type': 'application/json' }); res.end(JSON.stringify(o)); };
    if (p.startsWith('/slskd/')) {
      state.keys.add('slskd:' + req.headers['x-api-key']);
      const api = p.slice('/slskd'.length);
      if (api === '/api/v0/searches' && req.method === 'POST') { const b = JSON.parse(body || '{}'); state.searchText.set(b.id, String(b.searchText)); return json(200, {}); }
      const sm = /^\/api\/v0\/searches\/([^/]+)$/.exec(api);
      if (sm) {
        if (req.method !== 'GET') return json(200, {});
        const wanted = /lantern/i.test(state.searchText.get(sm[1]) ?? '');
        if (u.searchParams.get('includeResponses')) {
          return json(200, { responses: wanted ? [{ username: 'peer1', hasFreeUploadSlot: true, uploadSpeed: 2_000_000, queueLength: 0, files: [{ filename: LANTERN, size: 30_000_000, length: 234, bitDepth: 24, sampleRate: 96000 }] }] : [] });
        }
        return json(200, { isComplete: true, state: 'Completed, ResponseLimitReached', responseCount: wanted ? 1 : 0 });
      }
      if (api === '/api/v0/transfers/downloads/peer1') {
        if (req.method === 'POST') { for (const f of JSON.parse(body || '[]')) state.queued.push(f.filename); return json(200, {}); }
        return json(200, { directories: [{ directory: 'x', files: state.queued.map((f) => ({ filename: f, id: 't1', state: state.release ? 'Completed, Succeeded' : 'InProgress', bytesTransferred: state.release ? 30_000_000 : 0 })) }] });
      }
      if (api.startsWith('/api/v0/transfers/downloads/')) return json(200, {});
      return json(404, {});
    }
    if (p.startsWith('/fm/')) {
      state.keys.add('fm:' + req.headers['x-api-key']);
      if (p === '/fm/move' && req.method === 'POST') { state.moves.push(JSON.parse(body || '{}')); return json(200, {}); }
      if (p === '/fm/file') { state.files++; res.writeHead(200, { 'content-type': 'audio/wav', 'content-length': wav.length, 'accept-ranges': 'bytes' }); return res.end(wav); }
      return json(404, {});
    }
    json(404, {});
  });
});

const KEY_SLSKD = 'test-key-slskd', KEY_FM = 'test-key-fm';
const fmPath = (rel: string) => `${ORIGIN}/fm/file?path=${encodeURIComponent(rel)}`;
const RECENT = /^Recently downloaded · (just now|\d+s ago|\d+ min ago)$/;

let ctx: BrowserContext, page: Page;
let settingsBefore: { prefetch: boolean; chartsEnabled: boolean } = { prefetch: true, chartsEnabled: false };
let accepted = 0;     // reports the server took from this account in this spec (the limit is 200 an hour)

async function signIn(p: Page) {
  await p.goto('/login');
  await p.locator('#jf-form summary').click();
  await p.locator('#user').fill('tester');
  await p.locator('#password').fill('pw');
  await p.locator('#jf-form button[type="submit"]').click();
  await p.waitForURL((u) => !u.pathname.startsWith('/login'));
  await p.waitForLoadState('load');
  await p.waitForTimeout(1000);
}

// a page is only live once its circuit's socket is open: an event sent before that is lost without a trace
async function live(url: string) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto(url);
  await ws;
  await page.waitForTimeout(600);
}

async function report(data: unknown, headers: Record<string, string> = JSONH) {
  const r = await page.request.post('/api/downloaded', { headers, data: typeof data === 'string' ? data : JSON.stringify(data) });
  if (r.status() === 200) accepted++;
  return r;
}
const account = async () => (await (await page.request.get('/api/account', { headers: TOKEN })).json()).songs as number;
const tagText = async (loc: ReturnType<Page['locator']>) => ((await loc.first().innerText()) || '').replace(/\s+/g, ' ').trim();

async function openAlbum() {
  await live('/artist/Brass%20Monkeys');
  await page.getByRole('link', { name: /Brass Album 1/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(4, { timeout: 20_000 });
}
const row = (title: string) => page.locator('.tracks .track', { has: page.locator('.track-title', { hasText: new RegExp(`^${title}$`) }) });
const qrow = (title: string) => page.locator('.upnext li.q-item', { has: page.locator('b', { hasText: new RegExp(`^${title}$`) }) });

async function openNowPlaying() {
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await expect(page.locator('.np-titles h2')).toBeVisible();
  const tab = page.getByRole('tab', { name: 'Up next' });
  if ((await tab.getAttribute('aria-selected')) !== 'true') await tab.click();
}
async function closeNowPlaying() { await page.getByRole('button', { name: 'Close Now Playing' }).click(); await expect(page.locator('.np-titles h2')).toHaveCount(0); }
async function pause() {
  await page.getByRole('button', { name: 'Pause', exact: true }).first().click();
  await expect(page.getByRole('button', { name: 'Play', exact: true }).first()).toBeVisible();
}

test.describe.serial('download tags', () => {
  test.beforeAll(async ({ browser, baseURL }) => {
    await new Promise<void>((ok) => server.listen(FM, '127.0.0.1', ok));
    ctx = await browser.newContext({ baseURL });
    page = await ctx.newPage();
    await signIn(page);
    // the library loads on first use: wait until the mock's songs are in
    await expect(async () => { expect(await account()).toBeGreaterThan(0); }).toPass({ timeout: 30_000 });
    // no look-ahead and no daily-chart downloads while a spec is looking at what is downloading (the account's own settings are put back afterwards)
    const was = await (await page.request.get('/api/settings', { headers: TOKEN })).json();
    settingsBefore = { prefetch: was.prefetch, chartsEnabled: was.charts.enabled };
    await page.request.post('/api/settings', { headers: JSONH, data: '{"prefetch":false,"chartsEnabled":false}' });
  });
  test.afterAll(async () => {
    // put the account back as it was: no download services, look-ahead on
    try {
      await live('/dashboard');
      await expect(page.locator('#downloads')).toBeVisible({ timeout: 20_000 });
      for (const sel of ['#sl', '#fm']) { await page.locator(sel).fill(''); await page.locator(sel).dispatchEvent('change'); }
      await page.getByRole('button', { name: 'Save downloads' }).click();
      await expect(page.locator('#downloads .hint', { hasText: 'Saved.' })).toBeVisible();
      await page.request.post('/api/settings', { headers: JSONH, data: JSON.stringify(settingsBefore) });
    } catch (e) { /* the run is over; a leftover setting only matters to a second run on this server */ }
    await ctx.close();
    server.closeAllConnections?.();
    await new Promise<void>((ok) => server.close(() => ok()));
  });

  test('the dashboard takes the download services, so the Hi-Res menu and downloads work', async () => {
    await live('/dashboard');
    await expect(page.locator('#downloads')).toBeVisible({ timeout: 20_000 });
    // a bound field takes its value on the change event, which fill() alone does not send
    const set = async (sel: string, v: string) => { await page.locator(sel).fill(v); await page.locator(sel).dispatchEvent('change'); };
    await set('#sl', `${ORIGIN}/slskd`); await set('#slk', KEY_SLSKD); await set('#slp', 'downloads');
    await set('#fm', `${ORIGIN}/fm`); await set('#fmk', KEY_FM);
    await page.getByRole('button', { name: 'Save downloads' }).click();
    await expect(page.locator('#downloads .hint', { hasText: 'Saved.' })).toBeVisible();
    // kept in the account (what the Hi-Res menu and every download read), keys included: the labels say "(saved)"
    await live('/dashboard');
    await expect(page.locator('label[for="slk"]')).toContainText('(saved)', { timeout: 20_000 });
    await expect(page.locator('label[for="fmk"]')).toContainText('(saved)');
    await expect(page.locator('#sl')).toHaveValue(`${ORIGIN}/slskd`);
  });

  // ---- POST /api/downloaded ----

  test('POST /api/downloaded: no token is 401, a bad body is 400, a too big one 413', async ({ playwright, baseURL }) => {
    const anon = await playwright.request.newContext({ baseURL });
    expect((await anon.post('/api/downloaded', { headers: { 'content-type': 'application/json' }, data: '{"artist":"A","title":"T"}' })).status()).toBe(401);
    expect((await anon.post('/api/downloaded', { headers: { 'X-Emby-Token': 'bogus', 'content-type': 'application/json' }, data: '{"artist":"A","title":"T"}' })).status()).toBe(401);
    await anon.dispose();
    const bad = ['{bad', 'null', '[1,2]', '"text"', '{}', '{"artist":"A"}', '{"title":"T"}', '{"artist":"  ","title":"T"}', '{"artist":"A","title":""}', '{"artist":5,"title":"T"}', '{"artist":"A","title":["T"]}'];
    for (const body of bad) {
      const r = await report(body);
      expect(r.status(), body).toBe(400);
      expect(r.headers()['content-type'] ?? '').toContain('json');
    }
    expect((await report({ artist: 'A', title: 'T', album: 'x'.repeat(40_000) })).status()).toBe(413);
  });

  test('POST /api/downloaded: a report is accepted, never trusts a path that is not the user\'s file mover, and clamps the finish time', async () => {
    const before = await account();
    const base = { artist: 'Report Artist', album: 'Report Album', durationMs: 123000, source: 'soulseek' };
    // no path: logged and tagged, but nothing to play
    let r = await report({ ...base, title: 'No Path' });
    expect(r.status()).toBe(200);
    expect(await r.json()).toEqual({ ok: true, known: false });
    // a path that is not this user's file mover, another endpoint of it, a way out of the music folder: never added
    for (const path of ['http://evil.example/file?path=a%2Fb.flac', `${ORIGIN}/fm/move?path=a%2Fb.flac`, fmPath('../../etc/passwd'), fmPath('/etc/passwd'), `${ORIGIN}/fm/file?path=a.flac&path=b.flac`, 'file:///C:/Windows/system.ini']) {
      r = await report({ ...base, title: 'Bad Path', path });
      expect(r.status(), path).toBe(200);
      expect((await r.json()).known, path).toBe(false);
    }
    expect(await account()).toBe(before);
    // a path of the file mover: playable at once
    r = await report({ ...base, title: 'Good Path', path: fmPath('Report Artist/Report Artist - Good Path.flac') });
    expect(r.status()).toBe(200);
    expect(await r.json()).toEqual({ ok: true, known: true });
    expect(await account()).toBe(before + 1);
    // the finish time is kept within ten minutes back and never ahead, an unknown source is not echoed
    const now = Date.now();
    await report({ ...base, title: 'Clamp Old', finishedAtMs: now - 3 * 3600_000, source: 'soulseek' });
    await report({ ...base, title: 'Clamp Future', finishedAtMs: now + 3600_000, source: '<script>' });
    const log = (await (await page.request.get('/api/downloads?limit=40', { headers: TOKEN })).json()).log as { label: string; finishedAt: string; source: string | null; kind: string; outcome: string }[];
    const old = log.find((l) => l.label.startsWith('Clamp Old'))!, fut = log.find((l) => l.label.startsWith('Clamp Future'))!;
    expect(old.kind).toBe('Phone'); expect(old.outcome).toBe('done'); expect(old.source).toBe('soulseek');
    expect(Date.parse(old.finishedAt)).toBeGreaterThan(now - 10 * 60_000 - 15_000);
    expect(Date.parse(old.finishedAt)).toBeLessThan(now - 10 * 60_000 + 15_000);
    expect(Date.parse(fut.finishedAt)).toBeLessThan(Date.now() + 5_000);
    expect(fut.source).toBeNull();
  });

  // ---- the owner's story: "P" by JME downloaded on the phone ----

  test('a download the phone reported is found by Search, is "New download" and carries the tag', async () => {
    const before = await account();
    const r = await report({ artist: 'JME', title: 'P', album: 'Famous?', durationMs: 123000, path: fmPath('Music/JME/JME - P.flac'), source: 'soulseek', finishedAtMs: Date.now() - 1000 });
    expect(await r.json()).toEqual({ ok: true, known: true });
    expect(await account()).toBe(before + 1);
    await live('/search?q=' + encodeURIComponent('P JME'));
    const hit = page.locator('.tracks .track', { has: page.locator('.track-title', { hasText: /^P$/ }) }).first();
    await expect(hit).toBeVisible({ timeout: 20_000 });
    await expect(hit.locator('.badge.cloud')).toHaveText('New download');
    await expect(hit.locator('.dl-tag')).toHaveText(RECENT);
    // no second "Download" is offered for it in the same results
    await expect(page.locator('.track', { has: page.locator('.track-title', { hasText: /^P$/ }) }).getByRole('button', { name: /^Download$/ })).toHaveCount(0);
  });

  test('the player bar and Now Playing say it too, with the new wording, and the age ticks without the page being drawn again', async () => {
    await live('/search?q=' + encodeURIComponent('P JME'));
    const hit = page.locator('.tracks .track', { has: page.locator('.track-title', { hasText: /^P$/ }) }).first();
    await expect(hit).toBeVisible({ timeout: 20_000 });
    await hit.locator('.track-main').click();
    await expect(page.locator('.bar-text b')).toHaveText('P', { timeout: 20_000 });
    await pause();
    // under the artist in the player card
    await expect(page.locator('.bar-text .dl-tag')).toHaveText(RECENT);
    expect(await page.locator('.bar-text small').first().innerText()).toBe('JME');
    await openNowPlaying();
    await expect(page.locator('.np-where')).toHaveText('Streaming from new download');
    await expect(page.locator('.np-titles .pills .badge.cloud')).toHaveText('Streaming from new download');
    await expect(page.locator('.np-titles .dl-tag')).toHaveText(RECENT);
    await expect(page.locator('.upnext li.now .dl-tag')).toHaveText(RECENT);
    // one quiet line (a dot beside the words), not stacked: the queue lays its spans out in columns
    expect((await page.locator('.upnext li.now .dl-tag').boundingBox())!.height).toBeLessThan(22);
    // the Info panel names the source the same way
    await page.getByRole('tab', { name: 'Info' }).click();
    await expect(page.locator('.kv dt', { hasText: 'Source' }).locator('xpath=following-sibling::dd[1]')).toContainText('Streaming from new download', { timeout: 20_000 });
    await page.getByRole('tab', { name: 'Up next' }).click();

    // the age ticks in the browser: paused, so nothing on the server changes, yet the words move on and the element is the same one
    const age = page.locator('.np-titles .dl-tag .dl-age');
    await age.evaluate((el) => { (el as any).__same = 1; });
    const t1 = await age.innerText();
    await expect.poll(async () => age.innerText(), { timeout: 15_000 }).not.toBe(t1);
    expect(await age.evaluate((el) => (el as any).__same)).toBe(1);
    expect(await age.innerText()).toMatch(/^(just now|\d+s ago)$/);
    // it does not move the line: the tag keeps its place while it ticks
    const box1 = await page.locator('.np-titles .dl-tag').boundingBox();
    await page.waitForTimeout(2200);
    const box2 = await page.locator('.np-titles .dl-tag').boundingBox();
    expect(Math.abs(box1!.y - box2!.y)).toBeLessThan(0.5);
    expect(Math.abs(box1!.height - box2!.height)).toBeLessThan(0.5);
    await closeNowPlaying();
  });

  test('the age label and the one hour window in the browser are the shared rule', async () => {
    const r = await page.evaluate(() => {
      const f = (window as any).flacieDl.ageLabel as (ms: number) => string | null;
      return [0, 4999, 5000, 59_999, 60_000, 61_000, 10 * 60_000, 3_599_999, 3_600_001, 7 * 24 * 3600_000, -30_000, -60_000, -60_001].map(f);
    });
    expect(r).toEqual(['just now', 'just now', '5s ago', '59s ago', '1 min ago', '1 min ago', '10 min ago', '59 min ago', null, null, 'just now', 'just now', null]);
    // a tag that reaches an hour goes away by itself (no server round trip): a stand-in 1 s short of the hour
    await page.evaluate(() => {
      const t = document.createElement('span');
      t.id = 'age-probe'; t.className = 'track-dl dl-tag ok';
      t.innerHTML = 'Recently downloaded · <time class="dl-age" data-age="3599000">59 min ago</time>';
      document.body.appendChild(t);
    });
    await expect(page.locator('#age-probe')).toBeHidden({ timeout: 10_000 });
    await page.evaluate(() => document.getElementById('age-probe')?.remove());
  });

  // ---- lists: a song downloaded 20 minutes ago is tagged, one from 2 hours or a week ago and a plain library song are not ----

  test('lists: only the recent download is tagged; a download from two hours or a week ago is a normal library song', async () => {
    await openAlbum();
    await expect(row('Harbour 11').locator('.dl-tag')).toHaveText(/^Recently downloaded · \d+ min ago$/);
    // written 20 minutes before this run started, so it is 20 + the minutes the run has taken
    const mins = Number(/(\d+) min ago/.exec(await tagText(row('Harbour 11').locator('.dl-tag')))![1]);
    expect(mins).toBeGreaterThanOrEqual(19);
    expect(mins).toBeLessThan(55);
    for (const t of ['Velvet 9', 'Orbit 10', 'Lantern 12']) await expect(row(t).locator('.dl-tag'), t).toHaveCount(0);
    await expect(page.locator('.tracks .track .track-dl')).toHaveCount(1);
    // the same song in the Explore list, found by name
    await live('/search?q=' + encodeURIComponent('Harbour 11'));
    const h = page.locator('.tracks .track', { has: page.locator('.track-title', { hasText: /^Harbour 11$/ }) }).first();
    await expect(h.locator('.dl-tag')).toHaveText(/min ago$/, { timeout: 20_000 });
    await live('/songs');
    await expect(page.locator('.tracks.virtual .track').first()).toBeVisible({ timeout: 20_000 });
    const songs = page.locator('.tracks.virtual .track', { has: page.locator('.track-title', { hasText: /^Harbour 11$/ }) });
    if (await songs.count()) await expect(songs.first().locator('.dl-tag')).toHaveText(/min ago$/);
    // Home: no tag on a shelf song unless it is recent (the shelves are the library's own songs)
    await live('/');
    await expect(page.locator('.shelf').first()).toBeVisible({ timeout: 20_000 });
    for (const tag of await page.locator('.shelf .dl-tag').allInnerTexts()) expect(tag.replace(/\s+/g, ' ')).toMatch(/^Recently downloaded · /);
  });

  test('the queue: Up next tags the recent download, not the others', async () => {
    await openAlbum();
    await row('Velvet 9').locator('.track-main').click();
    await expect(page.locator('.bar-text b')).toHaveText('Velvet 9', { timeout: 20_000 });
    await pause();
    await expect(page.locator('.bar-text .dl-tag')).toHaveCount(0);       // the playing song is a plain library song
    await openNowPlaying();
    const autoplay = page.locator('.switch-row', { hasText: 'Add similar songs' }).locator('input');
    if (await autoplay.isChecked()) await autoplay.uncheck();            // an online catalog must not extend the queue while a spec looks at it
    await expect(page.locator('.upnext li.q-item')).toHaveCount(3, { timeout: 15_000 });
    await expect(qrow('Harbour 11').locator('.dl-tag')).toHaveText(/^Recently downloaded · \d+ min ago$/);
    await expect(qrow('Orbit 10').locator('.dl-tag')).toHaveCount(0);
    await expect(qrow('Lantern 12').locator('.dl-tag')).toHaveCount(0);
    await closeNowPlaying();
  });

  // ---- a download running now: its stage, in the list, the queue and the player card; then "Recently downloaded" ----

  test('a running download shows its stage live in the list, the queue and the player card, then turns into "Recently downloaded"', async () => {
    test.setTimeout(150_000);
    state.release = false; state.queued.length = 0;
    // a tall window: on a short page the last row's menu opens behind the player bar
    await page.setViewportSize({ width: 1280, height: 1000 });
    await openAlbum();
    await page.getByRole('button', { name: 'More for Lantern 12' }).click();
    await page.getByRole('menuitem', { name: /Hi-Res version/ }).click();
    await page.getByRole('button', { name: 'Download Hi-Res' }).click();
    const listTag = row('Lantern 12').locator('.dl-tag');
    await expect(listTag).toHaveText(/^Downloading · /, { timeout: 20_000 });
    // it reaches the point where it waits for the peer (the mock holds the transfer)
    await expect(listTag).toHaveText('Downloading · Waiting for a peer...', { timeout: 40_000 });
    await expect(row('Lantern 12').locator('.track-dl')).toHaveCount(1);

    // in the queue and, once it is the song playing, in the player card
    await row('Velvet 9').locator('.track-main').click();
    await expect(page.locator('.bar-text b')).toHaveText('Velvet 9', { timeout: 20_000 });
    await pause();
    await openNowPlaying();
    await expect(qrow('Lantern 12').locator('.dl-tag')).toHaveText('Downloading · Waiting for a peer...');
    await qrow('Lantern 12').locator('.q-row').click();
    await pause();                                                       // the mock's songs are 3 seconds long
    await expect(page.locator('.np-titles h2')).toHaveText('Lantern 12', { timeout: 20_000 });
    await expect(page.locator('.np-titles .dl-tag')).toHaveText('Downloading · Waiting for a peer...');
    await closeNowPlaying();
    await expect(page.locator('.bar-text b')).toHaveText('Lantern 12');
    await expect(page.locator('.bar-text .dl-tag')).toHaveText('Downloading · Waiting for a peer...');
    // under the artist, as for a Search result that says "Searching Soulseek..."
    expect(await page.locator('.bar-text small').first().innerText()).toBe('Brass Monkeys');
    const sub = await page.locator('.bar-text small').first().boundingBox(), tag = await page.locator('.bar-text .dl-tag').boundingBox();
    expect(tag!.y).toBeGreaterThanOrEqual(sub!.y + sub!.height - 1);

    // the transfer ends: the same song now says it was downloaded
    state.release = true;
    await expect(page.locator('.bar-text .dl-tag')).toHaveText(RECENT, { timeout: 60_000 });
    await expect(page.locator('.bar-text .dl-tag')).toHaveText(/^Recently downloaded · (just now|\d+s ago)$/);
    // asked of the file mover with the keys the user saved, and filed as "Artist - Title [Hi-Res].flac"
    expect(state.keys.has('slskd:' + KEY_SLSKD)).toBe(true);
    expect(state.keys.has('fm:' + KEY_FM)).toBe(true);
    expect(state.moves.some((m) => /Brass Monkeys - Lantern 12 \[Hi-Res\]\.flac$/.test(m.to))).toBe(true);
    const log = (await (await page.request.get('/api/downloads?limit=60', { headers: TOKEN })).json()).log as { label: string; outcome: string; source: string; kind: string }[];
    expect(log.some((l) => l.label.startsWith('Lantern 12') && l.outcome === 'done' && l.source === 'soulseek' && l.kind === 'Search')).toBe(true);
    // and the list row (a different component instance on another page) says the same after a fresh load
    await openAlbum();
    await expect(row('Lantern 12').locator('.dl-tag')).toHaveText(RECENT);
    await expect(row('Harbour 11').locator('.dl-tag')).toHaveText(/min ago$/);
  });

  // ---- the limit on reports (last: it uses the hour's quota up) ----

  test('POST /api/downloaded: at most 200 reports an hour per user', async () => {
    let first429 = -1, ok = 0;
    for (let i = 0; i < 230; i++) {
      const r = await report({ artist: 'Limit Test', title: 'Limit ' + i });
      if (r.status() === 429) { first429 = i; break; }
      expect(r.status()).toBe(200);
      ok++;
    }
    expect(first429).toBeGreaterThan(0);
    // every one counted, from the first test on: exactly 200 were taken in this hour (the 429 is not counted as accepted)
    expect(accepted).toBe(200);
    expect((await report({ artist: 'Limit Test', title: 'Still over' })).status()).toBe(429);
  });
});
