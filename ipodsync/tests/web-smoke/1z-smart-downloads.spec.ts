import { test, expect, Page, BrowserContext } from '@playwright/test';
import http from 'node:http';

// Smart downloads, the phone's side (docs/superpowers/specs/2026-11-01-smart-downloads-design.md, section 4): the phone hands a download to FLACie Web
// with POST /api/download { artist, title, album, durationMs, mode } and follows it with GET /api/download/{id}
// ({ stage, message, source, done, failed, path, format }). FLACie Web runs the download itself, the same way as its own Download button.
//
// A stand-in for the user's download stack runs inside this spec (FM_PORT): slskd with two songs nobody's library has ("Smart Quick", a CD-quality
// FLAC, and "Smart Hires", only a 24-bit / 96 kHz copy), each transfer done at once, and a file mover from before smart downloads (no /health, so no
// /retire: YouTube and the open sources keep their old turn and the upgrade worker does nothing).

const FM = Number(process.env.FM_PORT || 8197);
const ORIGIN = `http://127.0.0.1:${FM}`;
const TOKEN = { 'X-Emby-Token': 'mock-token' };
const JSONH = { ...TOKEN, 'content-type': 'application/json' };
const ARTIST = 'Tide Runners';

const files: Record<string, { filename: string; size: number; length: number; bitDepth: number; sampleRate: number }> = {
  'smart quick': { filename: `@@share\\Music\\${ARTIST}\\${ARTIST} - Smart Quick.flac`, size: 30_000_000, length: 234, bitDepth: 16, sampleRate: 44100 },
  'smart hires': { filename: `@@share\\Music\\${ARTIST}\\${ARTIST} - Smart Hires.flac`, size: 90_000_000, length: 234, bitDepth: 24, sampleRate: 96000 },
};

const state = { searchText: new Map<string, string>(), queued: new Map<string, number>(), moves: [] as { from: string; to: string }[] };
const server = http.createServer((req, res) => {
  let body = '';
  req.on('data', (c) => (body += c));
  req.on('end', () => {
    const u = new URL(req.url!, ORIGIN), p = u.pathname;
    const json = (code: number, o: unknown) => { res.writeHead(code, { 'content-type': 'application/json' }); res.end(JSON.stringify(o)); };
    if (p.startsWith('/slskd/')) {
      const api = p.slice('/slskd'.length);
      if (api === '/api/v0/searches' && req.method === 'POST') { const b = JSON.parse(body || '{}'); state.searchText.set(b.id, String(b.searchText).toLowerCase()); return json(200, {}); }
      const sm = /^\/api\/v0\/searches\/([^/]+)$/.exec(api);
      if (sm) {
        if (req.method !== 'GET') return json(200, {});
        const text = state.searchText.get(sm[1]) ?? '';
        const found = Object.entries(files).filter(([k]) => text.includes(k)).map(([, f]) => f);
        if (u.searchParams.get('includeResponses')) return json(200, { responses: found.length ? [{ username: 'peer9', hasFreeUploadSlot: true, uploadSpeed: 5_000_000, queueLength: 0, files: found }] : [] });
        return json(200, { isComplete: true, state: 'Completed, ResponseLimitReached', responseCount: found.length ? 1 : 0 });
      }
      if (api === '/api/v0/transfers/downloads/peer9') {
        if (req.method === 'POST') { for (const f of JSON.parse(body || '[]')) state.queued.set(f.filename, f.size); return json(200, {}); }
        return json(200, { directories: [{ directory: 'x', files: [...state.queued].map(([f, size]) => ({ filename: f, id: 't-' + size, state: 'Completed, Succeeded', bytesTransferred: size })) }] });
      }
      if (api.startsWith('/api/v0/transfers/downloads/')) return json(200, {});
      return json(404, {});
    }
    if (p.startsWith('/fm/')) {
      if (p === '/fm/move' && req.method === 'POST') { state.moves.push(JSON.parse(body || '{}')); return json(200, {}); }
      return json(404, { error: 'not found' });   // no /health: a file mover from before smart downloads
    }
    if (p.startsWith('/api.deezer.com/')) return json(200, { data: [] });
    if (p.startsWith('/musicbrainz.org/')) return json(200, { artists: [] });
    if (p === '/itunes.apple.com/search') return json(200, { results: [] });
    json(404, {});
  });
});

let ctx: BrowserContext, page: Page;
let settingsBefore = { prefetch: true, chartsEnabled: false };

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

async function live(url: string) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto(url);
  await ws;
  await page.waitForTimeout(600);
}

async function setServices(slskd: string, fm: string) {
  await live('/dashboard');
  await expect(page.locator('#downloads')).toBeVisible({ timeout: 20_000 });
  const set = async (sel: string, v: string) => { await page.locator(sel).fill(v); await page.locator(sel).dispatchEvent('change'); };
  await set('#sl', slskd); if (slskd) { await set('#slk', 'test-key-slskd'); await set('#slp', 'downloads'); }
  await set('#fm', fm); if (fm) await set('#fmk', 'test-key-fm');
  await page.getByRole('button', { name: 'Save downloads' }).click();
  await expect(page.locator('#downloads .hint', { hasText: 'Saved.' })).toBeVisible();
}

const start = (data: unknown, headers: Record<string, string> = JSONH) => page.request.post('/api/download', { headers, data: typeof data === 'string' ? data : JSON.stringify(data) });

async function follow(id: string) {
  let last: any = null;
  await expect(async () => {
    const r = await page.request.get(`/api/download/${id}`, { headers: TOKEN });
    expect(r.status()).toBe(200);
    last = await r.json();
    expect(last.done || last.failed).toBe(true);
  }).toPass({ timeout: 45_000, intervals: [500] });
  return last;
}

test.describe.serial('smart downloads: the phone hands its downloads to FLACie Web', () => {
  test.beforeAll(async ({ browser, baseURL }) => {
    await new Promise<void>((ok) => server.listen(FM, '127.0.0.1', ok));
    ctx = await browser.newContext({ baseURL });
    page = await ctx.newPage();
    await signIn(page);
    // nothing fetched ahead or for the charts while this spec looks at its own downloads (put back afterwards)
    const was = await (await page.request.get('/api/settings', { headers: TOKEN })).json();
    settingsBefore = { prefetch: was.prefetch, chartsEnabled: was.charts.enabled };
    await page.request.post('/api/settings', { headers: JSONH, data: '{"prefetch":false,"chartsEnabled":false}' });
  });
  test.afterAll(async () => {
    try {
      await setServices('', '');
      await page.request.post('/api/settings', { headers: JSONH, data: JSON.stringify(settingsBefore) });
    } catch (e) { /* the run is over; a leftover setting only matters to a second run on this server */ }
    await ctx.close();
    server.closeAllConnections?.();
    await new Promise<void>((ok) => server.close(() => ok()));
  });

  test('no token is 401 for both calls', async ({ playwright, baseURL }) => {
    const anon = await playwright.request.newContext({ baseURL });
    expect((await anon.post('/api/download', { headers: { 'content-type': 'application/json' }, data: '{"artist":"A","title":"T"}' })).status()).toBe(401);
    expect((await anon.post('/api/download', { headers: { 'X-Emby-Token': 'bogus', 'content-type': 'application/json' }, data: '{"artist":"A","title":"T"}' })).status()).toBe(401);
    expect((await anon.get('/api/download/abc')).status()).toBe(401);
    await anon.dispose();
  });

  test('without download services set up it says so (409), never a 500', async () => {
    const r = await start({ artist: ARTIST, title: 'Smart Quick' });
    expect(r.status()).toBe(409);
    expect((await r.json()).error).toContain('set up');
  });

  test('the dashboard takes the stand-in download services', async () => {
    await setServices(`${ORIGIN}/slskd`, `${ORIGIN}/fm`);
  });

  test('a bad body or an unknown mode is 400 JSON', async () => {
    for (const body of ['{bad', 'null', '[1]', '{}', '{"artist":"A"}', '{"title":"T"}', '{"artist":5,"title":"T"}', '{"artist":"A","title":"T","mode":"slow"}', '{"artist":"A","title":"T","mode":3}']) {
      const r = await start(body);
      expect(r.status(), body).toBe(400);
      expect(r.headers()['content-type'] ?? '').toContain('json');
    }
  });

  test('an unknown id is 404', async () => {
    expect((await page.request.get('/api/download/0123456789abcdef0123456789abcdef', { headers: TOKEN })).status()).toBe(404);
  });

  test('a quick download finishes: the source, the file\'s format and where it plays from', async () => {
    const r = await start({ artist: ARTIST, title: 'Smart Quick', album: 'Tides', durationMs: 234_000, mode: 'quick' });
    expect(r.status()).toBe(200);
    const { id } = await r.json();
    expect(id).toMatch(/^[0-9a-f]{32}$/);
    const j = await follow(id);
    expect(j).toMatchObject({ stage: 'done', done: true, failed: false, source: 'soulseek', format: 'flac', hiRes: false });
    // relative to the file mover's root, as the phone files its own downloads
    expect(j.path).toBe(`${ARTIST}/${ARTIST} - Smart Quick.flac`);
    expect(typeof j.message).toBe('string');
    // the same job is on the Downloads page with its label and source, as a download from the web's own button is
    const log = (await (await page.request.get('/api/downloads', { headers: TOKEN })).json()).log as any[];
    expect(log.find((x) => x.id === id)).toMatchObject({ label: `Smart Quick · ${ARTIST}`, outcome: 'done', source: 'soulseek' });
  });

  test('a Hi-Res download takes the Hi-Res copy and files it as [Hi-Res]', async () => {
    const r = await start({ artist: ARTIST, title: 'Smart Hires', durationMs: 234_000, mode: 'hires' });
    expect(r.status()).toBe(200);
    const j = await follow((await r.json()).id);
    expect(j).toMatchObject({ done: true, source: 'soulseek', format: 'flac', hiRes: true });
    expect(j.path).toBe(`${ARTIST}/${ARTIST} - Smart Hires [Hi-Res].flac`);
  });

  test('asking twice for a song on its way gives the same job', async () => {
    const a = await (await start({ artist: ARTIST, title: 'Not Anywhere', mode: 'quick' })).json();
    const b = await (await start({ artist: ARTIST, title: 'Not Anywhere', mode: 'quick' })).json();
    expect(b.id).toBe(a.id);
    const j = await follow(a.id);
    expect(j.failed).toBe(true);
    expect(j.done).toBe(false);
    expect(j.path).toBeNull();
  });
});
