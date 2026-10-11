// Load measurement of FLACie Web (and, with --shell desktop, of the Windows app's embedded copy) against the 10,000-song mock Jellyfin.
//
//   node run-web.mjs --server <dir with FLACie.Server.dll> --label before|after --profile fast|slow|flaky|ratelimit
//                    [--shell web|desktop] [--out results.json] [--window 150] [--items 10000]
//
// One run = one fresh mock (own port) + one fresh FLACie Web data folder + one Chromium. Scenarios, in order:
//   first-sync   sign in on an empty server, open Explore, wait for all songs; then idle until --window seconds after sign-in, counting what Jellyfin saw
//   scrolling    fling through the whole Explore list (about 40 s) with covers lazily loading
//   skip-burst   play a song, press Next 40 times, 60 ms apart; count the streams Jellyfin saw
//   warm-start   stop the server, start it again on the same data folder, open Explore: time to songs, requests in the first 60 s
// Numbers come from the mock's /__stats (requests, peak concurrent, per-second peak, image variants, streams opened/aborted) and from the browser.
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const SERVER = arg('server'), LABEL = arg('label', 'run'), PROFILE = arg('profile', 'fast'), SHELL = arg('shell', 'web');
const WINDOW = Number(arg('window', 150)), ITEMS = Number(arg('items', 10000));
const OUT = arg('out', path.join(here, `result-${LABEL}-${SHELL}-${PROFILE}.json`));
const MOCK_PORT = Number(arg('mock-port', 8296)), WEB_PORT = Number(arg('web-port', 5390));
const DATA = arg('data', `C:\\wt\\loadtest-data\\${LABEL}-${SHELL}-${PROFILE}`);
if (!SERVER) { console.error('--server <publish dir> is required'); process.exit(2); }

const require = createRequire(path.resolve(here, '../../tests/web-smoke/package.json'));
const { chromium } = require('@playwright/test');

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const MOCK = `http://127.0.0.1:${MOCK_PORT}`, WEB = `http://127.0.0.1:${WEB_PORT}`;
const jget = async (u) => (await fetch(u)).json();
const stats = () => jget(`${MOCK}/__stats`);
const reset = () => fetch(`${MOCK}/__reset`);

const procs = [];
function start(cmd, args, opts) { const p = spawn(cmd, args, { stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true, ...opts }); procs.push(p); return p; }
async function waitUp(url, ms = 60000) { const end = Date.now() + ms; while (Date.now() < end) { try { if ((await fetch(url)).ok) return; } catch { } await sleep(300); } throw new Error('not up: ' + url); }
function killAll() { for (const p of procs) { try { p.kill('SIGKILL'); } catch { } } try { spawn('taskkill', ['/F', '/T', '/PID', String(procs.at(-1)?.pid)], { stdio: 'ignore' }); } catch { } }
process.on('exit', killAll);

let web;
function startWeb() {
  const env = { ...process.env, ASPNETCORE_URLS: WEB, ASPNETCORE_ENVIRONMENT: 'Production', FLACIE_JELLYFIN_URL: MOCK, FLACIE_DATA: DATA, DOTNET_CLI_TELEMETRY_OPTOUT: '1' };
  if (SHELL === 'desktop') env.FLACIE_PARENT_PID = String(process.pid);   // what the Windows app sets: background jobs that belong to an always-on server stay off
  web = start('dotnet', ['FLACie.Server.dll'], { cwd: SERVER, env });
  web.stdout.on('data', (d) => fs.appendFileSync(DATA + '.server.log', d));
  web.stderr.on('data', (d) => fs.appendFileSync(DATA + '.server.log', d));
  return waitUp(`${WEB}/healthz`);
}
async function stopWeb() {
  if (!web) return;
  await new Promise((r) => { web.once('exit', r); spawn('taskkill', ['/F', '/T', '/PID', String(web.pid)], { stdio: 'ignore' }); setTimeout(r, 8000); });
  web = null; await sleep(500);
}

const pick = (s) => ({ total: s.total, byKind: s.byKind, peakInFlight: s.peakInFlight, peakPerSecond: s.peakPerSecond, throttled: s.throttled, failed: s.failed,
  images: s.byKind.image ?? 0, imageUrls: s.imageUrls, imageRepeats: s.imageRepeats, imageVariants: s.imageVariants, notModified: s.notModified,
  streamsOpened: s.streamsOpened, streamsAborted: s.streamsAborted, streamsCompleted: s.streamsCompleted, itemsPages: s.itemsPages, itemsDeltas: s.itemsDeltas, avgLatencyMs: s.avgLatencyMs, maxLatencyMs: s.maxLatencyMs, seconds: Math.round(s.seconds) });

async function songCount(page) {
  return page.evaluate(() => { const t = document.querySelector('.page-head .sub')?.textContent ?? ''; const m = /([\d,]+)\s+songs/.exec(t); return m ? Number(m[1].replace(/,/g, '')) : -1; }).catch(() => -1);
}

async function signIn(page) {
  await page.goto(WEB + '/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 60000 });
}

const result = { label: LABEL, shell: SHELL, profile: PROFILE, items: ITEMS, window: WINDOW, startedAt: new Date().toISOString(), scenarios: {}, notes: [] };

async function main() {
  fs.rmSync(DATA, { recursive: true, force: true }); fs.rmSync(DATA + '.server.log', { force: true }); fs.mkdirSync(path.dirname(DATA), { recursive: true });
  const mock = start('node', [path.join(here, 'mock.mjs'), '--port', String(MOCK_PORT), '--host', '127.0.0.1', '--items', String(ITEMS), '--streamms', '1500']);
  mock.stderr.on('data', (d) => process.stderr.write(d));
  await waitUp(`${MOCK}/System/Info/Public`);
  await fetch(`${MOCK}/__profile?name=${PROFILE}`);
  await reset();
  await startWeb();
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await ctx.newPage();

  // ---- first sync (cold start: nothing saved anywhere) ----
  const t0 = Date.now();
  await signIn(page);
  await page.goto(WEB + '/songs');
  const fs1 = { timeToFirstSongsMs: null, timeToAllSongsMs: null, timeline: [] };
  const seen = { firstSongs: false, all: false };
  for (let s = 0; (Date.now() - t0) / 1000 < WINDOW + 5; s++) {
    const n = await songCount(page), st = await stats(), el = Date.now() - t0;
    if (n > 0 && !seen.firstSongs) { seen.firstSongs = true; fs1.timeToFirstSongsMs = el; }
    if (n >= ITEMS && !seen.all) { seen.all = true; fs1.timeToAllSongsMs = el; fs1.atAll = pick(st); }
    if (s % 5 === 0) fs1.timeline.push({ t: Math.round(el / 1000), requests: st.total, inFlight: st.inFlight, songs: n });
    if (el / 1000 >= WINDOW) break;
    await sleep(2000);
  }
  fs1.songsShown = await songCount(page);
  fs1.final = pick(await stats());
  result.scenarios['first-sync'] = fs1;
  console.error(`[${LABEL}/${SHELL}/${PROFILE}] first-sync: songs ${fs1.songsShown}, first ${fs1.timeToFirstSongsMs} ms, all ${fs1.timeToAllSongsMs} ms, requests ${fs1.final.total}, peak ${fs1.final.peakInFlight}`);

  // ---- scrolling the whole list ----
  if (fs1.songsShown > 0) {
    await page.goto(WEB + '/songs');
    await page.locator('.track').first().waitFor({ timeout: 60000 });
    await sleep(1500); await reset();
    const sc = await page.evaluate(async () => {
      const el = document.scrollingElement; const total = el.scrollHeight; let steps = 0; const t0 = performance.now();
      const stepPx = Math.max(900, Math.ceil(total / 330));
      while (el.scrollTop + innerHeight < el.scrollHeight - 5 && steps < 400) { el.scrollBy(0, stepPx); steps++; await new Promise((r) => setTimeout(r, 120)); }
      return { steps, px: el.scrollTop, scrollHeight: total, ms: Math.round(performance.now() - t0) };
    });
    // stopped at the bottom: how long until the covers on screen are in?
    const tStop = Date.now(); let okCovers = 0, allCovers = 0;
    for (let i = 0; i < 40; i++) {
      const r = await page.evaluate(() => { const imgs = [...document.querySelectorAll('.track .cover img')]; return { n: imgs.length, ok: imgs.filter((i) => i.complete && i.naturalWidth > 0).length }; });
      okCovers = r.ok; allCovers = r.n; if (r.n > 0 && r.ok === r.n) break; await sleep(250);
    }
    const afterScroll = pick(await stats());
    result.scenarios.scrolling = { ...sc, coversOnScreen: allCovers, coversLoaded: okCovers, settleMs: Date.now() - tStop, ...afterScroll };
    console.error(`  scrolling: ${sc.steps} steps in ${sc.ms} ms, image requests ${afterScroll.images} (${afterScroll.imageUrls} distinct, ${afterScroll.imageRepeats} repeats), peak ${afterScroll.peakInFlight}, covers on screen ${okCovers}/${allCovers} after ${Date.now() - tStop} ms`);

    // ---- 40-skip burst ----
    await page.goto(WEB + '/songs');
    await page.locator('.track').first().waitFor({ timeout: 60000 });
    await sleep(1000);
    await page.locator('.track button.track-main').first().click();
    await page.locator('.player').waitFor({ timeout: 20000 });
    await sleep(2500);
    await reset();
    const next = page.getByRole('button', { name: 'Next' }).first();
    const tb = Date.now();
    for (let i = 0; i < 40; i++) { await next.click({ noWaitAfter: true, timeout: 5000 }).catch(() => { }); await sleep(60); }
    const burstMs = Date.now() - tb;
    let playingMs = null;
    for (let i = 0; i < 60; i++) {
      const ready = await page.evaluate(() => (window.flacie && window.flacie.ready ? window.flacie.ready() : null)).catch(() => null);
      if (ready === true) { playingMs = Date.now() - tb; break; }
      await sleep(250);
    }
    await sleep(4000);   // let anything that was going to happen happen
    const sb = pick(await stats());
    result.scenarios['skip-burst'] = { skips: 40, burstMs, lastSongReadyMs: playingMs, ...sb };
    console.error(`  skip-burst: ready after ${playingMs} ms, requests ${sb.total} (streams ${sb.streamsOpened}, aborted ${sb.streamsAborted}), peak ${sb.peakInFlight}`);
  }

  await browser.close();
  // ---- warm start: same data folder, server restarted ----
  await stopWeb();
  await sleep(1000); await reset();
  const w0 = Date.now();
  await startWeb();
  const b2 = await chromium.launch();
  const p2 = await (await b2.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
  let warm = { signedIn: false };
  try {
    await signIn(p2);   // the browser is new (no cookie); the library on the server's disk is what makes the start warm
    await p2.goto(WEB + '/songs');
    let first = null, all = null;
    for (let i = 0; i < 240; i++) {
      const n = await songCount(p2); const el = Date.now() - w0;
      if (n > 0 && first === null) first = el;
      if (n >= ITEMS && all === null) { all = el; break; }
      await sleep(250);
    }
    warm = { signedIn: true, timeToFirstSongsMs: first, timeToAllSongsMs: all };
    // 60 s from the start of the server, whatever it asked Jellyfin meanwhile
    while (Date.now() - w0 < 60000) await sleep(1000);
    warm = { ...warm, ...pick(await stats()) };
  } catch (e) { warm.error = String(e); }
  result.scenarios['warm-start'] = warm;
  console.error(`  warm-start: first songs ${warm.timeToFirstSongsMs} ms, all ${warm.timeToAllSongsMs} ms, requests in first 60 s ${warm.total}`);
  await b2.close();
  result.finishedAt = new Date().toISOString();
}

try { await main(); }
catch (e) { result.error = String(e?.stack ?? e); console.error('FAILED', e); }
finally {
  fs.writeFileSync(OUT, JSON.stringify(result, null, 1));
  console.error('wrote ' + OUT);
  await stopWeb().catch(() => { });
  killAll();
  process.exit(result.error ? 1 : 0);
}
