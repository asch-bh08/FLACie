// The emulator half of the parity check: run by run.mjs through tools/loadtest/run-android.ps1 (-Driver ..\parity\phone.mjs), which boots the AVD,
// installs the APK and passes --port --shots --adb (+ --sizes --screens via -Extra). Signs in to a fresh tools/loadtest/mock.mjs, seeds a playlist and
// favourites in the account's profile, then per size: `wm size` + `wm density 320`, every screen shot to <shots>/<size>-<screen>.png, and the
// layout flags read from uiautomator to <shots>/flags.json. Size and density are reset at the end (also on a crash).
import fs from 'node:fs';
import zlib from 'node:zlib';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import { requireEmulator, sh, adb, sleep, setAdb, dump, has, tapNode, tapAt, typeText, key, waitUi, label, shot } from '../loadtest/adbui.mjs';
import { requireMock } from '../loadtest/require-mock.mjs';
import { SIZES, SCREENS, pick } from './common.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
setAdb(arg('adb'));
const PORT = Number(arg('port', 8313)), SHOTS = arg('shots', path.join(here, '.shots'));
const sizes = pick(SIZES, arg('sizes')).filter((s) => s.phone), screens = pick(SCREENS, arg('screens'));
fs.mkdirSync(SHOTS, { recursive: true });
const PKG = 'app.flacie', MOCK = `http://127.0.0.1:${PORT}`, ACT = `${PKG}/com.ipodemu.ui.MainActivity`;
const log = (...a) => console.log(`[phone ${new Date().toTimeString().slice(0, 8)}]`, ...a);

// emulator-5554 and nothing else (run-android.ps1 and adbui already refuse a phone; this also refuses a second emulator)
requireEmulator();
{ const s = adb(['get-serialno']).trim(); if (s !== 'emulator-5554') { console.error('REFUSING: the target is ' + s + ', not emulator-5554'); process.exit(3); } }

const mock = spawn('node', [path.join(here, '../loadtest/mock.mjs'), '--port', String(PORT), '--host', '127.0.0.1', '--items', '400'], { stdio: 'ignore', windowsHide: true });
const resetScreen = () => { try { sh('wm size reset', { quiet: true }); sh('wm density reset', { quiet: true }); } catch { } };
process.on('exit', () => { try { mock.kill('SIGKILL'); } catch { } resetScreen(); });
process.on('SIGINT', () => process.exit(130));
for (let i = 0; i < 100; i++) { try { if ((await fetch(`${MOCK}/System/Info/Public`)).ok) break; } catch { } await sleep(200); }
await requireMock(MOCK);
await fetch(`${MOCK}/__profile?name=fast`); await fetch(`${MOCK}/__reset`);

// the same playlist and favourites as the web gets (as look-1126.mjs seeds them)
{ const AUTH = { 'X-Emby-Token': 'mock-token', 'content-type': 'application/json' }, BASE = `http://10.0.2.2:${PORT}`;
  const items = (await (await fetch(`${MOCK}/Users/u1/Items?IncludeItemTypes=Audio&Recursive=true&Limit=60`, { headers: AUTH })).json()).Items;
  const song = (i) => ({ p: `${BASE}/Audio/${items[i].Id}/stream?static=true`, t: items[i].Name, a: items[i].Artists?.[0] ?? items[i].AlbumArtist });
  const profile = { v: 2, updated: Date.now(), services: {}, favorites: Array.from({ length: 6 }, (_, i) => ({ ...song(i + 20), w: Date.now() - i * 1000 })),
    playlists: [{ id: 'parity1', n: 'Late Night Drive', m: Date.now(), jf: null, tracks: Array.from({ length: 18 }, (_, i) => song(i + 2)) }] };
  await fetch(`${MOCK}/DisplayPreferences/ipodplayer?userId=x&client=ipodplayer`, { method: 'POST', headers: AUTH, body: JSON.stringify({ CustomPrefs: { 'ipodplayer.profile': JSON.stringify(profile) }, Client: 'ipodplayer' }) }); }

// ---- sign in once (as look-1126.mjs does) ----
// (at a plain phone size whatever the AVD is: on a small square one the keyboard covers the address field)
sh("wm size 1080x2400"); sh("wm density 420"); await sleep(2000);
sh(`am force-stop ${PKG}`); sh(`pm clear ${PKG}`);
sh(`pm grant ${PKG} android.permission.POST_NOTIFICATIONS`, { quiet: true });
sh(`pm revoke ${PKG} android.permission.READ_MEDIA_AUDIO`, { quiet: true });
sh(`am start -n ${ACT}`, { quiet: true });
let ui = await waitUi((n) => has(n, 'Sign in with Jellyfin'), 120000, 2000);
if (!ui) { shot(path.join(SHOTS, 'signin-failed.png')); console.log('no sign-in screen'); process.exit(1); }
tapNode(ui.find((n) => label(n).includes('Sign in with Jellyfin')));
ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 30000, 1000);
const field = ui.find((n) => /EditText/.test(n.cls)), addr = `http://10.0.2.2:${PORT}`;
for (let i = 0; i < 4; i++) {
  tapAt(field.cx, field.cy); await sleep(600); key('KEYCODE_MOVE_END'); key('--longpress KEYCODE_DEL'); await sleep(300);
  for (let k = 0; k < 3; k++) key('KEYCODE_DEL ' + 'KEYCODE_DEL '.repeat(12));
  // a few characters at a time: on a slow emulator one long `input text` loses or swaps characters ("8313" came out "3813")
  for (const part of addr.match(/.{1,4}/g)) { typeText(part); await sleep(350); }
  await sleep(1000);
  if ((dump() || []).some((n) => /EditText/.test(n.cls) && n.text === addr)) break;
}
key('KEYCODE_BACK'); await sleep(800);
ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 20000, 1000);
tapNode(ui.find((n) => label(n).includes('Sign in with Quick Connect')));
if (!await waitUi((n) => !has(n, 'Sign in with Quick Connect') && !has(n, 'Jellyfin server') && !has(n, 'Signing in'), 60000, 1500)) {
  shot(path.join(SHOTS, 'signin-failed.png')); console.log('sign-in failed'); process.exit(1);
}
await sleep(6000);
{ const u = dump(); const g = u && u.find((n) => label(n) === 'Got it'); if (g) { tapNode(g); await sleep(800); } }
log('signed in');

// ---- the layout flags, from uiautomator's tree (the flat dump() has no parents) ----
function tree() {
  const r = adb(['shell', 'uiautomator dump /sdcard/ui.xml'], { quiet: true, timeout: 40000 });
  if (!/dumped to/i.test(r)) return null;
  const xml = adb(['shell', 'cat /sdcard/ui.xml'], { quiet: true });
  const nodes = [], stack = [];
  for (const m of xml.matchAll(/<node\b([^>]*?)(\/?)>|<\/node>/g)) {
    if (m[0] === '</node>') { stack.pop(); continue; }
    const a = {}; for (const x of m[1].matchAll(/([\w:-]+)="([^"]*)"/g)) a[x[1]] = x[2];
    const b = /\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]/.exec(a.bounds || ''); if (!b) continue;
    const [x1, y1, x2, y2] = b.slice(1).map(Number);
    const n = { label: (a.text || a['content-desc'] || '').slice(0, 40), words: a.text || '', clickable: a.clickable === 'true', scrollable: a.scrollable === 'true', x1, y1, x2, y2, parent: stack.at(-1) ?? null, kids: [] };
    n.parent?.kids.push(n); nodes.push(n); if (m[2] !== '/') stack.push(n);
  }
  // a clickable Compose row has no words itself: its first labelled child names it
  const named = (n) => n.label || n.kids.map(named).find(Boolean) || '';
  for (const n of nodes) if (!n.label) n.label = named(n);
  return nodes;
}
const above = (n, f) => { for (let p = n.parent; p; p = p.parent) if (f(p)) return true; return false; };
// the screenshot, decoded (8-bit RGB/RGBA as screencap writes it; no npm package for one PNG)
function readPng(file) {
  try {
    const b = fs.readFileSync(file); let p = 8, w = 0, h = 0, ch = 4; const idat = [];
    while (p < b.length) {
      const len = b.readUInt32BE(p), type = b.toString('ascii', p + 4, p + 8), d = b.subarray(p + 8, p + 8 + len);
      if (type === 'IHDR') { w = d.readUInt32BE(0); h = d.readUInt32BE(4); ch = d[9] === 6 ? 4 : 3; if (d[8] !== 8 || d[12]) return null; }
      else if (type === 'IDAT') idat.push(d); else if (type === 'IEND') break;
      p += 12 + len;
    }
    const raw = zlib.inflateSync(Buffer.concat(idat)), px = Buffer.alloc(w * h * ch), st = w * ch;
    for (let y = 0; y < h; y++) {
      const ft = raw[y * (st + 1)], src = raw.subarray(y * (st + 1) + 1, (y + 1) * (st + 1)), o = y * st;
      for (let x = 0; x < st; x++) {
        const a = x >= ch ? px[o + x - ch] : 0, up = y ? px[o - st + x] : 0, c = y && x >= ch ? px[o - st + x - ch] : 0;
        let pr = 0;
        if (ft === 4) { const q = a + up - c, pa = Math.abs(q - a), pb = Math.abs(q - up), pc = Math.abs(q - c); pr = pa <= pb && pa <= pc ? a : pb <= pc ? up : c; }
        px[o + x] = (src[x] + [0, a, up, (a + up) >> 1, pr][ft]) & 255;
      }
    }
    return { w, h, ch, px };
  } catch { return null; }
}
// words that are not drawn (no edges where uiautomator puts them) are under something: the tab bar under the full-screen player is still in
// the tree, but hidden, not overlapped
const allKids = (k) => [k, ...k.kids.flatMap(allKids)];
function wordsShow(img, n) {
  const t = allKids(n).find((k) => k.words);
  if (!img || !t) return true;
  let edges = 0, all = 0;
  for (let y = Math.max(0, t.y1); y < Math.min(img.h, t.y2); y++) for (let x = Math.max(1, t.x1); x < Math.min(img.w, t.x2); x++) {
    const i = (y * img.w + x) * img.ch, j = i - img.ch; all++;
    if (Math.abs(img.px[i] - img.px[j]) + Math.abs(img.px[i + 1] - img.px[j + 1]) + Math.abs(img.px[i + 2] - img.px[j + 2]) > 60) edges++;
  }
  return !all || edges / all > 0.01;
}
function flagsFrom(nodes, W, H, img) {
  if (!nodes) return [{ kind: 'err', text: 'no uiautomator dump (screen busy)' }];
  const out = [], inScroller = (n) => above(n, (p) => p.scrollable);
  const scroller = (n) => { for (let p = n.parent; p; p = p.parent) if (p.scrollable) return p; return null; };
  const ctl = nodes.filter((n) => n.clickable && n.x2 - n.x1 > 2 && n.y2 - n.y1 > 2 && wordsShow(img, n));
  const off = ctl.filter((n) => !inScroller(n) && (n.x1 < -1 || n.x2 > W + 1 || n.y1 < -1 || n.y2 > H + 1));
  if (off.length) out.push({ kind: 'off', text: 'off screen: ' + [...new Set(off.map((n) => n.label || '(unnamed)'))].slice(0, 6).join(', ') });
  const over = [];
  for (let i = 0; i < ctl.length; i++) for (let j = i + 1; j < ctl.length; j++) {
    const a = ctl[i], c = ctl[j];
    if (above(a, (p) => p === c) || above(c, (p) => p === a)) continue;
    // a row of a scrolling list passing under a fixed bar is not an overlap: only things in the same scroller (or both outside any) count
    if (scroller(a) !== scroller(c)) continue;
    const w = Math.min(a.x2, c.x2) - Math.max(a.x1, c.x1), h = Math.min(a.y2, c.y2) - Math.max(a.y1, c.y1);
    if (w <= 4 || h <= 4) continue;
    // one wholly on the other (a button on a card) is meant; half over each other is not
    const inside = (p, q) => p.x1 >= q.x1 - 2 && p.x2 <= q.x2 + 2 && p.y1 >= q.y1 - 2 && p.y2 <= q.y2 + 2;
    if (inside(a, c) || inside(c, a)) continue;
    over.push(`${a.label || '(unnamed)'} / ${c.label || '(unnamed)'}`);
  }
  if (over.length) out.push({ kind: 'over', text: `overlapping (${over.length}): ` + over.slice(0, 4).join('; ') });
  // anything with words reaching past the sides outside a scrolling row: the screen is wider than the phone
  const wide = nodes.filter((n) => n.label && !inScroller(n) && (n.x1 < -1 || n.x2 > W + 1));
  if (wide.length) out.push({ kind: 'hscroll', text: 'wider than the screen: ' + [...new Set(wide.map((n) => n.label))].slice(0, 4).join(', ') });
  return out;
}

// ---- getting to each screen ----
let W = 1080, H = 2400;
const nodesNow = () => dump() || [];
const tab = async (name) => {
  const u = nodesNow(); const n = u.filter((x) => label(x) === name).sort((a, b) => b.cy - a.cy || a.cx - b.cx)[0];
  if (!n) throw new Error(`no "${name}" tab`); tapNode(n); await sleep(2500);
};
// finds a row by its words, scrolling down a little at a time on a short screen
const tapRe = async (re, minY = 0.12) => {
  for (let i = 0; i < 4; i++) {
    const n = nodesNow().find((x) => re.test(label(x)) && x.cy > H * minY && x.cy < H * 0.92 && x.w > 0);
    if (n) { tapNode(n); await sleep(2500); return; }
    sh(`input swipe ${W / 2} ${H * 0.7} ${W / 2} ${H * 0.35} 400`); await sleep(1200);
  }
  throw new Error('not found: ' + re);
};
const fresh = async () => {
  sh(`am force-stop ${PKG}`); sh(`am start -n ${ACT}`, { quiet: true });
  if (!await waitUi((n) => n.some((x) => label(x) === 'Library'), 60000, 1500)) throw new Error('the app did not open');
  await sleep(1500);
  const g = nodesNow().find((n) => label(n) === 'Got it'); if (g) { tapNode(g); await sleep(800); }
};
const lib = async (re) => { await fresh(); await tab('Library'); await tapRe(re); };
let npOpen = false;
const openNp = async () => {
  if (npOpen) return;
  await lib(/^Songs$/); await tapRe(/^Song \d+$/, 0.25); await sleep(3500);
  // the mini bar: tap its words, left of its buttons
  const p = nodesNow().filter((x) => /^(Pause|Play)$/.test(label(x))).sort((a, b) => b.cy - a.cy)[0];
  if (!p) throw new Error('no mini bar');
  tapAt(Math.round(W * 0.3), p.cy); await sleep(3500); npOpen = true;
};
const npTab = async (re) => { await openNp(); await tapRe(re, 0); };
const steps = {
  home: async () => { await fresh(); await tab('Home'); },
  search: async () => {
    await fresh(); await tab('Search');
    const f = nodesNow().find((n) => /EditText/.test(n.cls)); if (f) { tapNode(f); await sleep(600); }
    typeText('Song 1'); await sleep(3000); key('KEYCODE_BACK'); await sleep(1500);
  },
  library: async () => { await fresh(); await tab('Library'); },
  songs: () => lib(/^Songs$/),
  album: async () => { await lib(/^Albums$/); await tapRe(/Album \d+/, 0.25); },
  artist: async () => { await lib(/^Artists$/); await tapRe(/^Artist \d+$/, 0.25); },
  playlist: () => lib(/Late Night Drive/),
  'playlist-edit': async () => { await lib(/Late Night Drive/); await tapRe(/^Edit$/, 0.1); },
  favourites: () => lib(/^Favourites/),
  downloads: () => lib(/^Offline/),
  // no Devices tab on a phone (26 Nov): it opens from the player's More
  devices: async () => { npOpen = false; await openNp(); await tapRe(/^More$/, 0.5); await tapRe(/^Devices$/, 0.2); npOpen = false; },
  settings: async () => { await fresh(); await tab('Settings'); },
  account: async () => { await fresh(); await tapRe(/^Account: /, 0); await tapRe(/^Account$/, 0); },
  np: async () => { npOpen = false; await openNp(); },
  'np-lyrics': () => npTab(/^Lyrics$/),
  'np-upnext': () => npTab(/^Up next$/),
  'np-info': () => npTab(/^(Info|Song info)$/),
};

const result = {};
const saveFlags = () => fs.writeFileSync(path.join(SHOTS, 'flags.json'), JSON.stringify(result, null, 1));
for (const size of sizes) {
  W = size.w * 2; H = size.h * 2;
  log(`size ${size.id}: wm size ${W}x${H}, density 320`);
  sh(`wm size ${W}x${H}`); sh('wm density 320'); await sleep(2500);
  // Android caps the override's aspect ratio by the AVD's own screen (a square AVD gives 1080x2160 for 1080x2400): say so on every shot
  const got = /Override size: (\d+)x(\d+)/.exec(sh('wm size', { quiet: true }));
  const note = got && (+got[1] !== W || +got[2] !== H) ? { kind: 'err', text: `emulator gave ${got[1] / 2}×${got[2] / 2} dp, not ${size.w}×${size.h}` } : null;
  if (got) { W = +got[1]; H = +got[2]; }
  const res = (result[size.id] = {});
  npOpen = false;
  for (const scr of screens) {
    let error = null;
    try { await steps[scr.id](); } catch (e) { error = 'could not reach it: ' + String(e.message).split('\n')[0].slice(0, 120); npOpen = false; }
    await sleep(800);
    const png = path.join(SHOTS, `${size.id}-${scr.id}.png`);
    shot(png);
    const flags = flagsFrom(tree(), W, H, readPng(png));
    if (error) flags.unshift({ kind: 'err', text: error });
    if (note) flags.unshift(note);
    res[scr.id] = { flags, ...(error ? { error } : {}) };
    log(`  ${scr.id}${error ? ' (' + error + ')' : ''}: ${flags.map((f) => f.kind).join(',') || 'ok'}`);
    saveFlags();
  }
}
resetScreen();
log('done');
process.exit(0);
