// How long a song takes to start in FLACie Web, against the mock Jellyfin (owner, 25 Nov: "songs are not loading cleanly: sometimes about 10 s to
// play, sometimes not at all"). Clicks a run of different songs in the Songs list, one after another, and times each from the click to the audio
// element's first "playing" (window.__flaciePlayingAt); a song that has not started after --give-up seconds counts as never started.
//
//   node song-starts-web.mjs --server <dir with FLACie.Server.dll> [--profile fast|slow|flaky|ratelimit] [--songs 10] [--gap 2500] [--give-up 20]
//                            [--first 0] [--label run] [--out file.json] [--mock-port 8396] [--web-port 5490]
//
//   --hover ms   rest the pointer on each row this long before clicking it (the page then reads the song's start early, as for a person)
//   --outage ms  before each song, Jellyfin fails everything (503) for this long while another song is pressed, then comes back: the song pressed
//                after that must start at once (owner, 25 Nov: the circuit breaker, opened by the outage, failed every song for 15 s and they were skipped)
//   --first ms   the mock waits this long before a song's first byte (a real Jellyfin opening a big FLAC from disk)
// Prints one line per song and a summary (median, worst, never started), and writes the numbers to --out.
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { requireMock } from './require-mock.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const SERVER = arg('server'), PROFILE = arg('profile', 'fast'), LABEL = arg('label', 'run');
const SONGS = Number(arg('songs', 10)), GAP = Number(arg('gap', 2500)), GIVE_UP = Number(arg('give-up', 20)) * 1000, FIRST = Number(arg('first', 0)), OUTAGE = Number(arg('outage', 0)), HOVER = Number(arg('hover', 0));
const MOCK_PORT = Number(arg('mock-port', 8396)), WEB_PORT = Number(arg('web-port', 5490));
const OUT = arg('out', `C:\\wt\\loadtest-data\\starts-${LABEL}-web-${PROFILE}.json`);   // (not in the repo)
const DATA = arg('data', `C:\\wt\\loadtest-data\\starts-${LABEL}-${PROFILE}`);
if (!SERVER) { console.error('--server <publish dir> is required'); process.exit(2); }

const require = createRequire(path.resolve(here, '../../tests/web-smoke/package.json'));
const { chromium } = require('@playwright/test');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const MOCK = `http://127.0.0.1:${MOCK_PORT}`, WEB = `http://127.0.0.1:${WEB_PORT}`;

const procs = [];
const start = (cmd, args, opts) => { const p = spawn(cmd, args, { stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true, ...opts }); procs.push(p); return p; };
async function waitUp(url, ms = 90000) { const end = Date.now() + ms; while (Date.now() < end) { try { if ((await fetch(url)).ok) return; } catch { } await sleep(300); } throw new Error('not up: ' + url); }
function killAll() { for (const p of procs) { try { spawn('taskkill', ['/F', '/T', '/PID', String(p.pid)], { stdio: 'ignore' }); } catch { } try { p.kill('SIGKILL'); } catch { } } }
process.on('exit', killAll);

const result = { label: LABEL, profile: PROFILE, firstByteMs: FIRST, songs: [], startedAt: new Date().toISOString() };
try {
  fs.rmSync(DATA, { recursive: true, force: true }); fs.rmSync(DATA + '.server.log', { force: true }); fs.mkdirSync(path.dirname(DATA), { recursive: true });
  start('node', [path.join(here, 'mock.mjs'), '--port', String(MOCK_PORT), '--host', '127.0.0.1', '--items', '400', '--wavsec', '60', '--first', String(FIRST)]);
  await waitUp(`${MOCK}/System/Info/Public`);
  await requireMock(MOCK);
  await fetch(`${MOCK}/__profile?name=${PROFILE}`);
  const web = start('dotnet', ['FLACie.Server.dll'], { cwd: SERVER, env: { ...process.env, ASPNETCORE_URLS: WEB, ASPNETCORE_ENVIRONMENT: 'Production', FLACIE_JELLYFIN_URL: MOCK,
    FLACIE_JELLYFIN_INTERNAL_URL: MOCK, FLACIE_DATA: DATA, FLACIE_RATE_SCALE: '50', FLACIE_CATALOG_REDIRECT: 'http://127.0.0.1:9', DOTNET_CLI_TELEMETRY_OPTOUT: '1' } });
  web.stdout.on('data', (d) => fs.appendFileSync(DATA + '.server.log', d)); web.stderr.on('data', (d) => fs.appendFileSync(DATA + '.server.log', d));
  await waitUp(`${WEB}/healthz`);
  const browser = await chromium.launch({ args: ['--autoplay-policy=no-user-gesture-required'] });
  const page = await (await browser.newContext({ viewport: { width: 1280, height: Number(arg('height', 2400)) } })).newPage();   // tall: every row clicked is on screen (the list is virtual: scrolling re-renders rows under the pointer)
  // the player's own audio element (the first one made), so a song that never starts can say what state it was left in
  await page.addInitScript(() => { const A = window.Audio; window.Audio = function (...a) { const el = new A(...a); (window.__audios ||= []).push(el); if (window.__audios.length === 1) { window.__ev = []; for (const e of ['loadstart', 'loadedmetadata', 'canplay', 'play', 'playing', 'pause', 'waiting', 'error', 'emptied']) el.addEventListener(e, () => window.__ev.push(e + '@' + Math.round(performance.now()))); } return el; }; window.Audio.prototype = A.prototype;
    if (window.AudioContext) { const C = window.AudioContext; window.AudioContext = function (...a) { (window.__ev ||= []).push('AudioContext@' + Math.round(performance.now())); return new C(...a); }; window.AudioContext.prototype = C.prototype; }
    const pl = HTMLMediaElement.prototype.play; HTMLMediaElement.prototype.play = function () { if (this !== (window.__audios || [])[0]) (window.__ev ||= []).push('otherplay:' + String(this.src).slice(-30) + '@' + Math.round(performance.now())); return pl.call(this); };
    try { new PerformanceObserver((l) => { for (const e of l.getEntries()) (window.__long ||= []).push(Math.round(e.startTime) + ':' + Math.round(e.duration)); }).observe({ type: 'longtask', buffered: true }); } catch { } });
  const streams = [];
  const sent = [];
  page.on('request', (r) => { const u = new URL(r.url()); if (u.pathname === '/stream' && !u.searchParams.has('pre') && !u.searchParams.has('wave')) sent.push(Date.now()); });
  page.on('requestfinished', (r) => { const u = new URL(r.url()); if (process.env.EV && u.pathname === '/stream') { const t = r.timing(); console.error(`    finished ${u.searchParams.has('pre') ? 'pre' : u.searchParams.has('wave') ? 'wave' : 'song'} ${r.headers()['range'] || ''}: start ${Math.round(t.startTime % 100000)} ttfb ${Math.round(t.responseStart)} end ${Math.round(t.responseEnd)}`); } });
  page.on('response', (r) => { const u = new URL(r.url()); if (u.pathname === '/stream') streams.push({ t: Date.now(), status: r.status(), pre: u.searchParams.has('pre'), wave: u.searchParams.has('wave') }); });
  await page.goto(WEB + '/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester'); await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 60000 });
  await page.goto(WEB + '/songs');
  await page.locator('.track button.track-main').nth(SONGS + 2).waitFor({ timeout: 90000 });
  await sleep(2000);
  await fetch(`${MOCK}/__reset`);
  if (process.env.RAWFIRST) console.error('  raw audio element in the page: ' + await page.evaluate(async (m) => { const a = new Audio(); const t = performance.now(); a.src = '/stream?p=' + encodeURIComponent(m + '/Audio/a0000063a0000063a0000063a0000063/stream?static=true'); a.play().catch(() => { }); const ok = await new Promise((r) => { a.addEventListener('playing', () => r(true), { once: true }); setTimeout(() => r(false), 10000); }); if (!ok) return 'none'; const ms = Math.round(performance.now() - t); a.pause(); a.removeAttribute('src'); a.load(); return ms; }, MOCK) + ' ms');
  for (let i = 0; i < SONGS; i++) {
    const row = page.locator('.track button.track-main').nth(i * Number(arg('stride', 2)) + 1);   // (not neighbours: the next song's early read would make every start look instant)
    const before = await page.evaluate(() => window.__flaciePlayingAt || 0), loadsBefore = await page.evaluate(() => window.flacie.loadsStarted());
    const box = await row.boundingBox(), label = await row.getAttribute('aria-label');
    if (OUTAGE > 0) {
      await fetch(`${MOCK}/__config?flaky=1`);
      await page.locator('.track button.track-main').nth(i * Number(arg('stride', 2))).click({ noWaitAfter: true });
      await sleep(OUTAGE);
      await fetch(`${MOCK}/__config?flaky=0`);
    }
    if (HOVER > 0) { await row.hover(); await sleep(HOVER); }   // a person's pointer resting on the row before the click (the page reads its start early)
    const t0 = Date.now(), s0 = streams.length, q0 = sent.length;
    await row.click({ noWaitAfter: true });
    const clickAt = await page.evaluate(() => performance.now());
    let ms = null;
    while (Date.now() - t0 < GIVE_UP) {
      const at = await page.evaluate(() => window.__flaciePlayingAt || 0);
      if (at > before) { ms = Math.max(0, Math.round(at - clickAt + (Date.now() - t0 > 0 ? 0 : 0))); break; }
      await sleep(40);
    }
    const audioState = ms !== null ? null : await page.evaluate(() => { const a = (window.__audios || [])[0]; return a && { src: a.src.slice(-60), readyState: a.readyState, networkState: a.networkState, paused: a.paused, error: a.error && a.error.code, t: a.currentTime, loads: window.flacie.loadsStarted() }; });
    if (audioState) { audioState.loadsBefore = loadsBefore; audioState.box = box; audioState.label = label; audioState.url = page.url(); await page.screenshot({ path: path.join(path.dirname(OUT), `never-${LABEL}-${i}.png`) }).catch(() => { }); }
    if (audioState) console.error('    audio: ' + JSON.stringify(audioState));
    const toast = await page.locator('.toast').last().textContent({ timeout: 200 }).catch(() => '');
    const mine = streams.slice(s0).filter((s) => !s.pre && !s.wave).map((s) => s.status);
    const ev = await page.evaluate((c) => (window.__ev || []).splice(0).map((x) => { const [e, t] = x.split('@'); return e + '+' + Math.round(t - c); }).join(' '), clickAt);
    const longs = await page.evaluate((c) => (window.__long || []).splice(0).map((x) => { const [t, d] = x.split(':'); return '+' + Math.round(t - c) + '/' + d + 'ms'; }).join(' '), clickAt);
    if (process.env.EV) console.error('    events: ' + ev + '   long tasks: ' + longs);
    const askMs = sent.length > q0 ? sent[q0] - t0 : null, answerMs = streams.slice(s0).find((x) => !x.pre && !x.wave)?.t - t0;
    result.songs.push({ i, ms, askMs, answerMs, statuses: mine, audioState, toast: (toast || '').trim().slice(0, 120) });
    console.error(`  song ${i + 1}: ${ms === null ? 'NEVER STARTED' : ms + ' ms'} (asked ${askMs}, answered ${answerMs})  streams ${mine.join(',')}${toast ? '  toast: ' + toast.trim().slice(0, 80) : ''}`);
    await sleep(GAP);
  }
  const got = result.songs.map((s) => s.ms).filter((x) => x !== null).sort((a, b) => a - b);
  result.summary = { started: got.length, never: SONGS - got.length, medianMs: got[Math.floor(got.length / 2)] ?? null, worstMs: got.at(-1) ?? null, mock: await (await fetch(`${MOCK}/__stats`)).json() };
  console.error(`[${LABEL}/${PROFILE}] started ${got.length}/${SONGS}, median ${result.summary.medianMs} ms, worst ${result.summary.worstMs} ms`);
  await browser.close();
} catch (e) { result.error = String(e?.stack ?? e); console.error('FAILED', e); }
finally {
  fs.writeFileSync(OUT, JSON.stringify(result, null, 1));
  killAll();
  process.exit(result.error ? 1 : 0);
}
