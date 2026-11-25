// What the Info tab's live graphs cost the page per frame in FLACie Web (owner, 25 Nov: "the waveform graph is very laggy"). Plays a song from the mock,
// opens a graph on a canvas#viz the size of the full-screen one, slows the CPU down (--cpu 4: about a phone) and reads Chrome's own counters over
// --secs seconds: frames drawn, and the main thread's script, style and layout time per frame.
//
//   node graph-perf-web.mjs --server <dir with FLACie.Server.dll> [--styles scope,curve,leds,wall] [--cpu 4] [--secs 5] [--label run]
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { requireMock } from './require-mock.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const SERVER = arg('server'), LABEL = arg('label', 'run'), CPU = Number(arg('cpu', 4)), SECS = Number(arg('secs', 5));
const STYLES = arg('styles', 'scope,curve,leds,wall').split(',');
const MOCK_PORT = Number(arg('mock-port', 8397)), WEB_PORT = Number(arg('web-port', 5491));
const DATA = `C:\\wt\\loadtest-data\\graph-${LABEL}`;
if (!SERVER) { console.error('--server <publish dir> is required'); process.exit(2); }
const require = createRequire(path.resolve(here, '../../tests/web-smoke/package.json'));
const { chromium } = require('@playwright/test');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const MOCK = `http://127.0.0.1:${MOCK_PORT}`, WEB = `http://127.0.0.1:${WEB_PORT}`;
const procs = [];
const start = (cmd, args, opts) => { const p = spawn(cmd, args, { stdio: 'ignore', windowsHide: true, ...opts }); procs.push(p); return p; };
async function waitUp(url, ms = 90000) { const end = Date.now() + ms; while (Date.now() < end) { try { if ((await fetch(url)).ok) return; } catch { } await sleep(300); } throw new Error('not up: ' + url); }
function killAll() { for (const p of procs) { try { spawn('taskkill', ['/F', '/T', '/PID', String(p.pid)], { stdio: 'ignore' }); } catch { } } }
process.on('exit', killAll);

let code = 0;
try {
  fs.rmSync(DATA, { recursive: true, force: true });
  start('node', [path.join(here, 'mock.mjs'), '--port', String(MOCK_PORT), '--host', '127.0.0.1', '--items', '200', '--wavsec', '120']);
  await waitUp(`${MOCK}/System/Info/Public`); await requireMock(MOCK);
  start('dotnet', ['FLACie.Server.dll'], { cwd: SERVER, env: { ...process.env, ASPNETCORE_URLS: WEB, ASPNETCORE_ENVIRONMENT: 'Production', FLACIE_JELLYFIN_URL: MOCK,
    FLACIE_JELLYFIN_INTERNAL_URL: MOCK, FLACIE_DATA: DATA, FLACIE_RATE_SCALE: '50', FLACIE_CATALOG_REDIRECT: 'http://127.0.0.1:9' } });
  await waitUp(`${WEB}/healthz`);
  const browser = await chromium.launch({ args: ['--autoplay-policy=no-user-gesture-required'] });
  const page = await (await browser.newContext({ viewport: { width: Number(arg('w', 1280)), height: Number(arg('h', 900)) }, deviceScaleFactor: Number(arg('dpr', 2)) })).newPage();
  await page.goto(WEB + '/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester'); await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 60000 });
  await page.goto(WEB + '/songs');
  await page.locator('.track button.track-main').nth(3).click();
  for (let i = 0; i < 150 && !(await page.evaluate(() => !!(window.flacie && window.flacie.ready()))); i++) await sleep(200);   // (waitForFunction is refused by the page's CSP)
  // a canvas the size of the full-screen graph (the whole window), in the page as the Info tab puts it
  // --real 1: the graph where people see it: Now Playing, Info, the graph's own tab (with everything else that page does each second)
  const REAL = arg('real', '0') === '1';
  if (REAL) {
    await page.locator('.player').click({ position: { x: 300, y: 40 } });
    await sleep(1500);
    // (the Info button is a slot under the cover on a PC and a tab on a phone: whichever is shown)
    for (let i = 0; i < 20; i++) { if (await page.evaluate(() => { const b = [...document.querySelectorAll('.ls-slot, [role=tab]')].find((x) => /^\s*Info\s*$/.test(x.textContent) && x.offsetParent); if (b) b.click(); return !!b; })) break; await sleep(500); }
    await page.locator('#viz').waitFor({ timeout: 20000 });
  } else await page.evaluate(() => { const c = document.createElement('canvas'); c.id = 'viz'; c.style.cssText = 'position:fixed;left:0;top:0;width:100vw;height:100vh;z-index:9999'; document.body.appendChild(c); });
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Performance.enable');
  await cdp.send('Emulation.setCPUThrottlingRate', { rate: CPU });
  const m = async () => Object.fromEntries((await cdp.send('Performance.getMetrics')).metrics.map((x) => [x.name, x.value]));
  const out = { label: LABEL, cpu: CPU, styles: {} };
  for (const st of STYLES) {
    if (REAL) await page.locator('.gtab', { hasText: new RegExp('^' + ({ scope: 'Waveform', curve: 'Spectrum', leds: 'Visualizer', wall: 'Spectrogram' }[st] || st) + '$') }).first().click();
    else await page.evaluate(([s, RATE]) => window.flacie.viz('viz', s, RATE, 0, ''), [st, Number(arg('rate', 44100))]);
    await sleep(1500);
    await page.evaluate(() => { window.__frames = 0; window.__gaps = []; let last = performance.now(); const f = (t) => { window.__frames++; window.__gaps.push(t - last); last = t; if (window.__frames < 100000) requestAnimationFrame(f); }; requestAnimationFrame(f); });
    const a = await m(); await sleep(SECS * 1000); const b = await m();
    if (process.env.SHOT) await page.screenshot({ path: `C:\\wt\\loadtest-data\\graph-${LABEL}-${st}.png` });
    const r = await page.evaluate(() => { const g = window.__gaps.slice(1).sort((x, y) => x - y); return { frames: window.__frames, p95: g[Math.floor(g.length * 0.95)], worst: g.at(-1) }; });
    const per = (k) => +(((b[k] - a[k]) * 1000) / Math.max(1, r.frames)).toFixed(2);
    out.styles[st] = { fps: +(r.frames / SECS).toFixed(1), p95FrameMs: Math.round(r.p95), worstFrameMs: Math.round(r.worst), scriptMsPerFrame: per('ScriptDuration'), styleMsPerFrame: per('RecalcStyleDuration'),
      layoutMsPerFrame: per('LayoutDuration'), taskMsPerFrame: per('TaskDuration'), styleRecalcs: b.RecalcStyleCount - a.RecalcStyleCount, layouts: b.LayoutCount - a.LayoutCount };
    console.error(`[${LABEL}] ${st}: ${JSON.stringify(out.styles[st])}`);
    if (!REAL) await page.evaluate(() => window.flacie.vizStop());
    await page.evaluate(() => { window.__frames = 100000; });
  }
  fs.writeFileSync(`C:\\wt\\loadtest-data\\graph-${LABEL}.json`, JSON.stringify(out, null, 1));
  await browser.close();
} catch (e) { console.error('FAILED', e); code = 1; }
finally { killAll(); process.exit(code); }
