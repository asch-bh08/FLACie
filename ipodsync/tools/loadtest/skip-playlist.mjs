// How long one Next press takes in a playlist, in the web player: from the click until the new song's title is on the player bar,
// until its audio can play, and until its cover has loaded. Run against a published FLACie.Server and the 10,000-song mock.
//   node skip-playlist.mjs --server <publish dir> --label before|after [--songs 300] [--skips 14] [--out file.json]
// Three states of the mock Jellyfin after the library is loaded: fast, slow (300 ms, 8 at once), busy (1.5 s, 2 at once: a Jellyfin
// that is working hard, as during somebody's first library sync).
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const SERVER = arg('server'), LABEL = arg('label', 'run'), SONGS = Number(arg('songs', 300)), SKIPS = Number(arg('skips', 14));
const OUT = arg('out', path.join(here, `skip-${LABEL}.json`));
const MOCK_PORT = 8297, WEB_PORT = 5391, DATA = `C:\\wt\\loadtest-data\\skip-${LABEL}`;
const require = createRequire(path.resolve(here, '../../tests/web-smoke/package.json'));
const { chromium } = require('@playwright/test');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const MOCK = `http://127.0.0.1:${MOCK_PORT}`, WEB = `http://127.0.0.1:${WEB_PORT}`;
const procs = [];
const start = (c, a, o) => { const p = spawn(c, a, { stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true, ...o }); procs.push(p); return p; };
async function waitUp(url, ms = 60000) { const end = Date.now() + ms; while (Date.now() < end) { try { if ((await fetch(url)).ok) return; } catch { } await sleep(300); } throw new Error('not up ' + url); }
const kill = () => { for (const p of procs) { try { spawn('taskkill', ['/F', '/T', '/PID', String(p.pid)], { stdio: 'ignore' }); } catch { } } };
process.on('exit', kill);
const median = (a) => { const s = [...a].sort((x, y) => x - y); return s.length ? s[Math.floor(s.length / 2)] : null; };
const stat = (a) => ({ n: a.length, median: median(a), max: a.length ? Math.max(...a) : null, min: a.length ? Math.min(...a) : null, all: a.map((x) => Math.round(x)) });

const result = { label: LABEL, songs: SONGS, skips: SKIPS, states: {} };
try {
  fs.rmSync(DATA, { recursive: true, force: true }); fs.mkdirSync(path.dirname(DATA), { recursive: true });
  start('node', [path.join(here, 'mock.mjs'), '--port', String(MOCK_PORT), '--host', '127.0.0.1', '--items', '10000', '--streamms', '0']);
  await waitUp(`${MOCK}/System/Info/Public`);
  const web = start('dotnet', ['FLACie.Server.dll'], { cwd: SERVER, env: { ...process.env, ASPNETCORE_URLS: WEB, ASPNETCORE_ENVIRONMENT: 'Production', FLACIE_JELLYFIN_URL: MOCK, FLACIE_DATA: DATA } });
  const slog = arg('serverlog', ''); if (slog) { fs.writeFileSync(slog, ''); web.stdout.on('data', (d) => fs.appendFileSync(slog, d)); web.stderr.on('data', (d) => fs.appendFileSync(slog, d)); } else { web.stdout.on('data', () => { }); web.stderr.on('data', () => { }); }
  await waitUp(`${WEB}/healthz`);
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  // the player's audio elements are never in the page: record them as they are created
  await ctx.addInitScript(() => { const A = window.Audio; window.__audios = []; const W = function (...a) { const x = new A(...a); window.__audios.push(x); return x; }; W.prototype = A.prototype; window.Audio = W; });
  const page = await ctx.newPage();
  await page.goto(WEB + '/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester'); await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 60000 });
  // the library must be complete before the playlist can be matched
  await page.goto(WEB + '/songs');
  for (let i = 0; i < 120; i++) {
    const n = await page.evaluate(() => { const m = /([\d,]+)\s+songs/.exec(document.querySelector('.page-head .sub')?.textContent ?? ''); return m ? Number(m[1].replace(/,/g, '')) : -1; });
    if (n >= 10000) break; await sleep(1000);
  }
  // a playlist of SONGS songs, imported as plain text "Artist - Title" (song i is "Song i+1" by "Artist ((i % 400) + 1)")
  const lines = Array.from({ length: SONGS }, (_, i) => `Artist ${String((i % 400) + 1).padStart(3, '0')} - Song ${i + 1}`).join('\n');
  const imp = await fetch(WEB + '/api/import?name=Skip%20test.txt', { method: 'POST', headers: { 'X-Emby-Token': 'mock-token', 'Content-Type': 'text/plain' }, body: lines });
  result.import = { status: imp.status };
  await sleep(6000);
  await page.goto(WEB + '/playlists');
  await page.getByText('Skip test').first().click();
  await page.waitForURL(/\/playlist\//);
  await page.locator('.track').first().waitFor({ timeout: 30000 });
  result.rowsInPlaylist = await page.locator('.track').count();

  const states = { fast: { latency: 0, capacity: 0 }, slow: { latency: 300, capacity: 8 }, busy: { latency: 1500, capacity: 2 } };
  const only = arg('states', '').split(',').filter(Boolean);
  for (const [name, cfg] of Object.entries(states)) {
    if (only.length && !only.includes(name)) continue;
    await fetch(`${MOCK}/__config?latency=${cfg.latency}&capacity=${cfg.capacity}&jitter=0`);
    await fetch(`${MOCK}/__reset`);
    // start at the first song of the playlist (rows are in the playlist's order)
    await page.locator('.track button.track-main').first().click();
    await page.locator('.player').waitFor({ timeout: 30000 });
    await page.evaluate(() => new Promise((r) => { const t = setInterval(() => { if (window.flacie && window.flacie.ready()) { clearInterval(t); r(); } }, 50); setTimeout(r, 20000); }));
    await sleep(1500);
    const title = [], ready = [], cover = [];
    for (let i = 0; i < SKIPS; i++) {
      const r = await page.evaluate(() => new Promise((resolve) => {
        const bar = () => document.querySelector('.player .bar-text b');
        const before = bar()?.textContent ?? '';
        const act = () => (window.__audios || []).find((a) => !a.paused && a.src && !a.src.includes('pre=1')) ?? null;
        const a0 = act(); const beforeSrc = a0?.src ?? '', beforeTime = a0?.currentTime ?? 0;
        const next = document.querySelector('.player button[aria-label="Next"]');
        const t0 = performance.now(); next.click();
        let tTitle = null, tReady = null, tCover = null;
        const tick = () => {
          const now = performance.now() - t0;
          if (tTitle === null && (bar()?.textContent ?? '') !== before) tTitle = now;
          if (tTitle !== null && tReady === null) { const a = act(); if (a && a.readyState >= 3 && a.currentTime > 0.05 && (a.src !== beforeSrc || a.currentTime < beforeTime)) tReady = now; }
          if (tTitle !== null && tCover === null) { const im = document.querySelector('.player .bar-song .cover img'); if (!im || (im.complete && im.naturalWidth > 0) || im.style.visibility === 'hidden') tCover = now; }
          if ((tTitle !== null && tReady !== null && tCover !== null) || now > 20000) return resolve({ tTitle, tReady, tCover });
          setTimeout(tick, 10);
        };
        tick();
      }));
      if (r.tTitle !== null) title.push(r.tTitle); if (r.tReady !== null) ready.push(r.tReady); if (r.tCover !== null) cover.push(r.tCover);
      await sleep(900);
    }
    const st = await (await fetch(`${MOCK}/__stats`)).json();
    result.states[name] = { title: stat(title), ready: stat(ready), cover: stat(cover), jellyfinRequests: st.total, byKind: st.byKind, streamsOpened: st.streamsOpened, peakInFlight: st.peakInFlight };
    console.error(`[${LABEL}] ${name}: title ${Math.round(median(title))} ms, ready ${Math.round(median(ready))} ms, cover ${Math.round(median(cover))} ms; requests ${st.total}`);
  }
  await browser.close();
} catch (e) { result.error = String(e?.stack ?? e); console.error('FAILED', e); }
finally { fs.writeFileSync(OUT, JSON.stringify(result, null, 1)); kill(); await sleep(500); process.exit(result.error ? 1 : 0); }
