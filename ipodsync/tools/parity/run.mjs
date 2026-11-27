// Parity check (plan docs/plans/one-app-overhaul.md, parts 0d + 1h): shoots every main screen on FLACie Web (Playwright) and on the Android emulator at
// every size in common.mjs, flags what does not fit, and writes ONE page: docs/parity/index.html (+ docs/parity/img/*.jpg).
//
//   node ipodsync/tools/parity/run.mjs            web and phone
//   node ipodsync/tools/parity/run.mjs --web      web only (the phone's last results stay on the page)
//   node ipodsync/tools/parity/run.mjs --phone    phone only (the web's last results stay on the page)
//   options: --sizes p360x640,pc1920x1080   only these sizes (ids in common.mjs; the phone only runs the ones marked phone)
//            --screens home,np              only these screens
//            --apk <file>                   the build installed on the emulator (default: android/app/build/outputs/apk/release/app-release.apk here, else in C:\FLACie)
//            --avd <name>                   the emulator image (default flacie_phone; retro_square needs far less free disk)
//            --server <publish dir>         a FLACie.Server publish to run instead of publishing this checkout afresh
//            --page-only                    just rebuild index.html from docs/parity/data.json
//
// Mocks only: the web runs against tests/web-smoke/mock-jellyfin.mjs (MOCK_PORT, default 8311; 8299 is often taken by other sessions) and FLACie Web on WEB_PORT 5395; the phone against
// tools/loadtest/mock.mjs (PHONE_MOCK_PORT, default 8313). Both refuse anything that does not answer the mock marker. The whole run takes the machine-wide lock
// (C:\FLACie-handoff\heavy.ps1): it re-starts itself under it, because it publishes .NET, runs Chromium and boots the emulator (emulator-5554 only;
// run-android.ps1 refuses a real phone, and wm size / density are reset when the driver ends).
import { spawn, spawnSync } from 'node:child_process';
import { createRequire } from 'node:module';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { SIZES, SCREENS, pick, shrink, writePage } from './common.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(here, '../../..');
const argv = process.argv.slice(2);
const flag = (n) => argv.includes('--' + n);
const arg = (n, d) => { const i = argv.indexOf('--' + n); return i >= 0 ? argv[i + 1] : d; };
const OUT = path.join(ROOT, 'docs', 'parity'), IMG = path.join(OUT, 'img'), DATA = path.join(OUT, 'data.json');
const doWeb = flag('web') || !flag('phone'), doPhone = flag('phone') || !flag('web');
const sizes = pick(SIZES, arg('sizes')), screens = pick(SCREENS, arg('screens'));
const MOCK_PORT = Number(process.env.MOCK_PORT || 8311), WEB_PORT = Number(process.env.WEB_PORT || 5395), PHONE_MOCK = Number(process.env.PHONE_MOCK_PORT || 8313);
const TMP = path.join(os.tmpdir(), 'flacie-parity');
const log = (...a) => console.log(`[parity ${new Date().toTimeString().slice(0, 8)}]`, ...a);

// one heavy job at a time on this machine: run again under the lock
if (!process.env.PARITY_LOCKED && !flag('page-only')) {
  // (no quotes: PowerShell 5.1 mangles quotes inside -Cmd; the paths and options here have no spaces)
  const cmd = `set PARITY_LOCKED=1&& node ${fileURLToPath(import.meta.url)} ${argv.join(' ')}`;
  const r = spawnSync('powershell', ['-NoProfile', '-File', 'C:\\FLACie-handoff\\heavy.ps1', '-Dir', ROOT, '-Cmd', cmd], { stdio: 'inherit' });
  process.exit(r.status ?? 1);
}

const require = createRequire(path.join(ROOT, 'ipodsync/tests/web-smoke/package.json'));
const { chromium } = require('@playwright/test');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const data = fs.existsSync(DATA) ? JSON.parse(fs.readFileSync(DATA, 'utf8')) : {};
const save = () => { fs.mkdirSync(OUT, { recursive: true }); fs.writeFileSync(DATA, JSON.stringify(data, null, 1)); };
const procs = [];
const killAll = () => { for (const p of procs) { try { spawnSync('taskkill', ['/F', '/T', '/PID', String(p.pid)], { stdio: 'ignore' }); } catch { } } };
process.on('exit', killAll);
async function up(url, ms) { const end = Date.now() + ms; while (Date.now() < end) { try { if ((await fetch(url)).ok) return true; } catch { } await sleep(400); } return false; }
async function isMock(url) { try { return (await (await fetch(url + '/__mock')).json()).mock === true; } catch { return false; } }

// ---------- checks on a web page (runs in the page) ----------
const DOM_FLAGS = () => {
  const vw = document.documentElement.clientWidth, vh = window.innerHeight;
  const name = (e) => (e.getAttribute('aria-label') || e.innerText || e.getAttribute('title') || e.tagName).trim().split('\n')[0].slice(0, 40);
  // what of a box is really drawn: cut by every ancestor that clips, and by the window
  const seen = (e) => {
    const r = e.getBoundingClientRect(); let l = Math.max(r.left, 0), t = Math.max(r.top, 0), rt = Math.min(r.right, vw), b = Math.min(r.bottom, vh);
    for (let p = e.parentElement; p && p !== document.body; p = p.parentElement) {
      const s = getComputedStyle(p);
      if (s.overflowX !== 'visible' || s.overflowY !== 'visible') { const q = p.getBoundingClientRect(); l = Math.max(l, q.left); t = Math.max(t, q.top); rt = Math.min(rt, q.right); b = Math.min(b, q.bottom); }
    }
    return { l, t, r: rt, b, box: r };
  };
  // the layer a control is drawn in: content scrolling under a fixed bar, or a page under the full-screen player, is not an overlap
  const layer = (e) => { for (let p = e; p && p !== document.body; p = p.parentElement) { const s = getComputedStyle(p); if (s.position === 'fixed' || s.position === 'sticky' || p.getAttribute('role') === 'dialog') return p; } return document.body; };
  // on top somewhere: another layer covering it whole (the player over the page) hides it from every check
  const onTop = (e, v) => { for (const [fx, fy] of [[.5, .5], [.2, .5], [.8, .5], [.5, .2], [.5, .8]]) { const h = document.elementFromPoint(v.l + (v.r - v.l) * fx, v.t + (v.b - v.t) * fy); if (h && (h === e || e.contains(h) || h.contains(e))) return true; } return false; };
  const scroller = (e, ax) => { for (let p = e.parentElement; p && p !== document.body && p !== document.documentElement; p = p.parentElement) { const s = getComputedStyle(p)['overflow' + ax]; if ((s === 'auto' || s === 'scroll') && (ax === 'X' ? p.scrollWidth > p.clientWidth + 1 : p.scrollHeight > p.clientHeight + 1)) return true; } return false; };
  const sideScroller = (e) => scroller(e, 'X');
  const ctl = [...document.querySelectorAll('button, a[href], input, select, textarea, [role=tab], [role=button], [role=slider]')].filter((e) => {
    const s = getComputedStyle(e); if (s.visibility === 'hidden' || s.display === 'none' || +s.opacity === 0) return false;
    const r = e.getBoundingClientRect(); return r.width >= 2 && r.height >= 2 && (e.offsetParent || s.position === 'fixed');
  }).map((e) => ({ e, n: name(e), v: seen(e) })).filter(({ e, v }) => v.r - v.l > 2 && v.b - v.t > 2 && onTop(e, v)).map((x) => ({ ...x, L: layer(x.e) }));
  const out = [];
  // cut off at a side (or, for something fixed, at the top or bottom): partly drawn, partly outside the window
  const off = ctl.filter(({ e, v, L }) => !sideScroller(e) && (v.box.left < -1 || v.box.right > vw + 1 || (L !== document.body && !scroller(e, 'Y') && (v.box.top < -1 || v.box.bottom > vh + 1))));
  if (off.length) out.push({ kind: 'off', text: 'off screen: ' + [...new Set(off.map((x) => x.n))].slice(0, 6).join(', ') });
  const over = [];
  for (let i = 0; i < ctl.length; i++) for (let j = i + 1; j < ctl.length; j++) {
    const a = ctl[i], c = ctl[j]; if (a.L !== c.L || a.e.contains(c.e) || c.e.contains(a.e)) continue;
    const w = Math.min(a.v.r, c.v.r) - Math.max(a.v.l, c.v.l), h = Math.min(a.v.b, c.v.b) - Math.max(a.v.t, c.v.t);
    if (w <= 2 || h <= 2) continue;
    // one sitting wholly on the other (a play button on a card) is meant; two boxes half over each other are not
    const inside = (p, q) => p.l >= q.l - 1 && p.r <= q.r + 1 && p.t >= q.t - 1 && p.b <= q.b + 1;
    if (inside(a.v, c.v) || inside(c.v, a.v)) continue;
    over.push(`${a.n} / ${c.n}`);
  }
  if (over.length) out.push({ kind: 'over', text: `overlapping (${over.length}): ` + over.slice(0, 4).join('; ') });
  const sw = document.documentElement.scrollWidth;
  if (sw > vw + 1) out.push({ kind: 'hscroll', text: `scrolls sideways (${sw} wide in ${vw})` });
  return out;
};

// ---------- web ----------
async function web() {
  log('web: sizes', sizes.map((s) => s.id).join(','));
  for (const port of [MOCK_PORT, WEB_PORT]) if (await fetch(`http://127.0.0.1:${port}/`).then(() => true, () => false)) throw new Error(`port ${port} is already in use: another run? (set MOCK_PORT / WEB_PORT)`);
  const smoke = path.join(ROOT, 'ipodsync/tests/web-smoke');
  procs.push(spawn('node', ['mock-jellyfin.mjs'], { cwd: smoke, env: { ...process.env, PORT: String(MOCK_PORT) }, stdio: 'ignore', windowsHide: true }));
  const MOCK = `http://127.0.0.1:${MOCK_PORT}`, WEB = `http://127.0.0.1:${WEB_PORT}`;
  if (!await up(MOCK + '/System/Info/Public', 30000) || !await isMock(MOCK)) throw new Error('REFUSING: the web mock did not come up / is not a mock');
  let pub = arg('server');
  if (!pub) {
    pub = path.join(TMP, 'pub');
    log('web: publishing FLACie.Server');
    const r = spawnSync('dotnet', ['publish', path.join(ROOT, 'ipodsync/src/FLACie.Server'), '-c', 'Release', '-o', pub, '--nologo', '-v', 'q'], { stdio: 'inherit' });
    if (r.status) throw new Error('dotnet publish failed');
  }
  const dataDir = path.join(TMP, 'web-data'); fs.rmSync(dataDir, { recursive: true, force: true });
  procs.push(spawn('dotnet', ['FLACie.Server.dll'], { cwd: pub, stdio: 'ignore', windowsHide: true, env: { ...process.env,
    ASPNETCORE_URLS: WEB, ASPNETCORE_ENVIRONMENT: 'Production', FLACIE_JELLYFIN_URL: MOCK, FLACIE_JELLYFIN_INTERNAL_URL: MOCK, FLACIE_DATA: dataDir,
    FLACIE_WEB_PLAYLISTS: 'off', FLACIE_DOWNLOADS: 'on', FLACIE_CATALOG_REDIRECT: `http://127.0.0.1:${MOCK_PORT + 2}`, FLACIE_CACHE_MIN_FREE_MB: '1',
    FLACIE_CACHE_MIN_FREE_PERCENT: '0', FLACIE_RATE_SCALE: '50' } }));
  if (!await up(WEB + '/healthz', 240000)) throw new Error('FLACie Web did not start');

  // a playlist and favourites in the account's profile (the same shape the phone keeps), so those pages have songs
  const AUTH = { authorization: 'MediaBrowser Token="mock-token"', 'content-type': 'application/json' };
  const items = (await (await fetch(`${MOCK}/Users/u1/Items?IncludeItemTypes=Audio&Recursive=true&Limit=60`, { headers: AUTH })).json()).Items;
  const song = (i) => ({ p: `${MOCK}/Audio/${items[i].Id}/stream?static=true`, t: items[i].Name, a: items[i].Artists?.[0] ?? items[i].AlbumArtist });
  const profile = { v: 2, updated: Date.now(), services: {}, favorites: Array.from({ length: 6 }, (_, i) => ({ ...song(i + 20), w: Date.now() - i * 1000 })),
    playlists: [{ id: 'parity1', n: 'Late Night Drive', m: Date.now(), jf: null, tracks: Array.from({ length: 18 }, (_, i) => song(i + 2)) }] };
  await fetch(`${MOCK}/DisplayPreferences/ipodplayer?userId=u1&client=ipodplayer`, { method: 'POST', headers: AUTH, body: JSON.stringify({ CustomPrefs: { 'ipodplayer.profile': JSON.stringify(profile) }, Client: 'ipodplayer' }) });

  const browser = await chromium.launch(process.env.PW_CHROMIUM ? { executablePath: process.env.PW_CHROMIUM } : {});
  const tool = await (await browser.newContext()).newPage();
  // signed in once; every size starts from that
  const first = await browser.newContext({ baseURL: WEB });
  { const p = await first.newPage(); await p.goto('/login'); await p.locator('#jf-form summary').click(); await p.locator('#user').fill('tester'); await p.locator('#password').fill('pw');
    await p.locator('#jf-form button[type="submit"]').click(); await p.waitForURL((u) => !u.pathname.startsWith('/login')); await p.waitForTimeout(1500);
    // the first-run welcome card goes, as the phone's first-run tip does
    await p.getByRole('button', { name: 'Got it' }).first().click({ timeout: 4000 }).catch(() => { }); await p.waitForTimeout(500); }
  const state = await first.storageState(); await first.close();
  data.web = { at: new Date().toISOString().slice(0, 16).replace('T', ' '), sizes: data.web?.sizes ?? {} };
  const pngDir = path.join(TMP, 'web-png'); fs.mkdirSync(pngDir, { recursive: true });

  for (const size of sizes) {
    log('web:', size.name);
    const ctx = await browser.newContext({ baseURL: WEB, storageState: state, viewport: { width: size.w, height: size.h }, deviceScaleFactor: size.dpr,
      isMobile: size.w < 768 && size.dpr > 1, hasTouch: size.w < 1024 && size.dpr > 1 });
    const page = await ctx.newPage();
    const res = (data.web.sizes[size.id] = {});
    const go = async (url) => { await page.goto(url); await page.waitForLoadState('load'); await page.waitForTimeout(1500); };
    const click = async (loc) => { await loc.first().click({ timeout: 8000 }); await page.waitForTimeout(1500); };
    const ls = page.locator('.ls');
    const openNp = async () => {
      if (await ls.isVisible().catch(() => false)) return;
      await go('/songs'); await click(page.locator('.tracks .track-main'));
      await click(page.locator('.player .bar-song, .player [aria-label="Full screen player"], .player [aria-label="Open Now Playing"]'));
      await ls.waitFor({ timeout: 10000 }); await page.waitForTimeout(1200);
    };
    // a phone-width player opens Lyrics / Info as a sheet with its own tabs: whichever tab of that name is showing
    // (the player's own button of that name may be under the sheet: the one on top is clicked)
    const npTab = async (n) => {
      await openNp();
      const ok = await page.evaluate((n) => {
        const top = [...document.querySelectorAll('[role=tab], button')].filter((e) => e.textContent.trim() === n).find((e) => {
          const r = e.getBoundingClientRect(); if (!r.width) return false;
          const h = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2); return h && (h === e || e.contains(h));
        });
        top?.click(); return !!top;
      }, n);
      if (!ok) throw new Error(`no "${n}" tab on top`);
      await page.waitForTimeout(1500);
    };
    const steps = {
      home: () => go('/'),
      search: async () => { await go('/search?q=Aurora'); },
      library: () => go('/library'),
      songs: () => go('/songs'),
      album: async () => { await go('/albums'); await click(page.locator('main a[href^="/album/"]')); },
      artist: () => go('/artist/Aurora%20Vale'),
      playlist: async () => { await go('/playlists'); await click(page.locator('a[href^="/playlist/"]', { hasText: 'Late Night Drive' })); },
      'playlist-edit': async () => { await go('/playlists'); await click(page.locator('a[href^="/playlist/"]', { hasText: 'Late Night Drive' })); await click(page.getByRole('link', { name: 'Edit', exact: true })); },
      favourites: () => go('/favorites'),
      devices: () => go('/devices'),
      settings: () => go('/settings'),
      downloads: () => go('/downloads'),
      account: () => go('/account'),
      np: openNp,
      'np-lyrics': () => npTab('Lyrics'),
      'np-upnext': () => npTab('Up next'),
      'np-info': () => npTab('Info'),
    };
    for (const scr of screens) {
      const png = path.join(pngDir, `${size.id}-${scr.id}.png`), img = `img/web-${size.id}-${scr.id}.jpg`;
      let error = null, flags = [];
      try { await steps[scr.id](); } catch (e) { error = 'could not reach it: ' + String(e.message).split('\n')[0].slice(0, 120); }
      try {
        flags = await page.evaluate(DOM_FLAGS);
        await page.screenshot({ path: png, scale: 'device' });
        flags.push(...await shrink(tool, png, path.join(OUT, img), size.w));
        res[scr.id] = { img, flags, ...(error ? { error } : {}) };
      } catch (e) { res[scr.id] = { img, flags: [{ kind: 'err', text: 'no shot: ' + e.message.slice(0, 100) }] }; }
      if (error) res[scr.id].flags.unshift({ kind: 'err', text: error });
    }
    await ctx.close(); save();
  }
  await browser.close();
}

// ---------- phone (the emulator part runs in phone.mjs, under run-android.ps1) ----------
async function phone() {
  const ps = sizes.filter((s) => s.phone);
  if (!ps.length) { log('phone: no phone sizes picked'); return; }
  // this checkout's release build, else the main checkout's (a worktree rarely has its own)
  const apk = arg('apk', ['C:/FLACie', ROOT].map((r) => path.join(r, 'android/app/build/outputs/apk/release/app-release.apk')).filter(fs.existsSync).at(-1));
  if (!apk || !fs.existsSync(apk)) throw new Error('no APK at ' + apk + ' (pass --apk)');
  const shots = path.join(TMP, 'phone-png'); fs.rmSync(shots, { recursive: true, force: true }); fs.mkdirSync(shots, { recursive: true });
  log('phone: sizes', ps.map((s) => s.id).join(','), 'apk', apk);
  const extra = `--sizes ${ps.map((s) => s.id).join(',')} --screens ${screens.map((s) => s.id).join(',')}`;
  const r = spawnSync('powershell', ['-NoProfile', '-File', path.join(ROOT, 'ipodsync/tools/loadtest/run-android.ps1'), '-Apk', apk, '-Label', 'parity', '-Profile', 'fast',
    '-Port', String(PHONE_MOCK), '-Shots', shots, '-Avd', arg('avd', 'flacie_phone'), '-Driver', '..\\parity\\phone.mjs', '-Extra', extra],
    { stdio: 'inherit', env: { ...process.env, ANDROID_SERIAL: 'emulator-5554' } });
  log('phone: driver exit', r.status);
  const found = fs.existsSync(path.join(shots, 'flags.json')) ? JSON.parse(fs.readFileSync(path.join(shots, 'flags.json'), 'utf8')) : {};
  const browser = await chromium.launch(process.env.PW_CHROMIUM ? { executablePath: process.env.PW_CHROMIUM } : {});
  const tool = await (await browser.newContext()).newPage();
  data.phone = { at: new Date().toISOString().slice(0, 16).replace('T', ' '), sizes: data.phone?.sizes ?? {} };
  for (const size of ps) {
    const res = (data.phone.sizes[size.id] = {});
    for (const scr of screens) {
      const png = path.join(shots, `${size.id}-${scr.id}.png`), f = found[size.id]?.[scr.id];
      if (!fs.existsSync(png)) { if (f) res[scr.id] = { img: '', flags: f.flags, error: f.error ?? 'no shot' }; continue; }
      const img = `img/phone-${size.id}-${scr.id}.jpg`;
      const flags = [...(f?.flags ?? []), ...await shrink(tool, png, path.join(OUT, img), size.w)];
      res[scr.id] = { img, flags, ...(f?.error ? { error: f.error } : {}) };
    }
  }
  await browser.close(); save();
}

try {
  if (!flag('page-only')) {
    fs.mkdirSync(IMG, { recursive: true });
    if (doWeb) { try { await web(); } finally { killAll(); procs.length = 0; } }
    if (doPhone) await phone();
  }
  const problems = writePage(OUT, data);
  log(`page: ${path.join(OUT, 'index.html')} (${problems.length} flagged)`);
  for (const [side, size, scr, f] of problems) console.log(`  ${size.id} ${side} ${scr.id}: ${f.map((x) => x.text).join('; ')}`);
  process.exit(0);
} catch (e) { console.error('[parity] failed:', e.message); process.exit(1); }
