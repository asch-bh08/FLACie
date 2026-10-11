// Small adb + uiautomator helper used by run-android.mjs (also a CLI for poking at the emulator by hand):
//   node adbui.mjs dump                 list what uiautomator sees (text / content-desc / bounds)
//   node adbui.mjs tap "Sign in"        tap the first node whose text or content-desc contains the string
//   node adbui.mjs shot out.png         screenshot
//   node adbui.mjs text "http://10.0.2.2:8296"
import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let ADB = process.env.ADB || path.join(process.env.LOCALAPPDATA || '', 'Android', 'Sdk', 'platform-tools', 'adb.exe');
export const setAdb = (p) => { if (p) ADB = p; };

export function adb(args, { buffer = false, timeout = 90000, quiet = false } = {}) {
  try { return execFileSync(ADB, args, { encoding: buffer ? 'buffer' : 'utf8', maxBuffer: 256 * 1024 * 1024, timeout, stdio: ['ignore', 'pipe', quiet ? 'ignore' : 'pipe'] }); }
  catch (e) { const out = e.stdout ? e.stdout.toString() : ''; if (quiet) return out; throw new Error(`adb ${args.join(' ')} failed: ${e.message}\n${out}`); }
}
export const sh = (cmd, o) => adb(['shell', cmd], o);

const unesc = (s) => s.replace(/&#10;/g, '\n').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, '&');

export function parseDump(xml) {
  const nodes = [];
  for (const m of xml.matchAll(/<node\b([^>]*?)\/?>/g)) {
    const a = {};
    for (const x of m[1].matchAll(/([\w:-]+)="([^"]*)"/g)) a[x[1]] = unesc(x[2]);
    const b = /\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]/.exec(a.bounds || '');
    if (!b) continue;
    const [x1, y1, x2, y2] = b.slice(1).map(Number);
    nodes.push({ text: a.text || '', desc: a['content-desc'] || '', cls: a.class || '', id: a['resource-id'] || '', clickable: a.clickable === 'true', scrollable: a.scrollable === 'true',
      x1, y1, x2, y2, cx: Math.round((x1 + x2) / 2), cy: Math.round((y1 + y2) / 2), w: x2 - x1, h: y2 - y1 });
  }
  return nodes;
}

/** uiautomator dump (null when it could not get an idle state, which happens while music plays or a spinner runs). */
export function dump() {
  for (let i = 0; i < 3; i++) {
    const r = adb(['shell', 'uiautomator dump /sdcard/ui.xml'], { quiet: true, timeout: 40000 });
    if (/dumped to/i.test(r)) {
      const x = adb(['shell', 'cat /sdcard/ui.xml'], { quiet: true });
      if (x.startsWith('<?xml')) {
        const nodes = parseDump(x);
        // a slow software-rendered emulator sometimes shows "<app> isn't responding": press Wait, and look again
        const anr = nodes.find((n) => /isn't responding|isn.t responding/i.test(label(n)));
        if (anr) { const w = nodes.find((n) => n.text === 'Wait'); if (w) { tapAt(w.cx, w.cy); anrCount++; } continue; }
        return nodes;
      }
    }
  }
  return null;
}
export let anrCount = 0;
export const label = (n) => (n.text || n.desc || '');
export const has = (nodes, s) => !!nodes && nodes.some((n) => label(n).includes(s));
export function find(nodes, s, opts = {}) {
  if (!nodes) return null;
  const re = s instanceof RegExp ? s : null;
  const hit = nodes.filter((n) => (re ? re.test(label(n)) : label(n).includes(s)) && (!opts.minY || n.cy >= opts.minY) && n.w > 0 && n.h > 0);
  return hit[opts.last ? hit.length - 1 : 0] ?? null;
}
export const tapAt = (x, y) => sh(`input tap ${x} ${y}`);
export function tapNode(n) { tapAt(n.cx, n.cy); }
export function tapText(s, opts) { const n = find(dump(), s, opts); if (!n) return false; tapNode(n); return true; }
export function typeText(s) { sh(`input text '${s.replace(/ /g, '%s')}'`); }
export const key = (k) => sh(`input keyevent ${k}`);
export function shot(file) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  try { fs.writeFileSync(file, adb(['exec-out', 'screencap', '-p'], { buffer: true, timeout: 30000 })); } catch (e) { /* screenshots are best effort */ }
}
/** Wait until fn(nodes) is truthy (polling uiautomator); returns the nodes or null on timeout. */
export async function waitUi(fn, timeoutMs = 60000, every = 1500) {
  const end = Date.now() + timeoutMs;
  while (Date.now() < end) { const n = dump(); if (n && fn(n)) return n; await sleep(every); }
  return null;
}

// ---- playback state, from the media session (works while music plays: uiautomator does not) ----
export function mediaSession() {
  const t = sh('dumpsys media_session', { quiet: true, timeout: 30000 });
  const pkg = /package=com\.ipodemu[\s\S]{0,2500}/.exec(t)?.[0] ?? '';
  const st = /PlaybackState \{state=(?:\w+\()?(\d+)/.exec(pkg), md = /metadata:.*?description=([^\n]*)/.exec(pkg), q = /queueTitle=([^\n]*)/.exec(pkg);
  const pos = /position=(\d+)/.exec(pkg);
  return { found: !!pkg, state: st ? Number(st[1]) : null, description: md ? md[1].trim() : null, position: pos ? Number(pos[1]) : null, queueTitle: q ? q[1].trim() : null };
}

if (process.argv[1] && path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url))) {
  const [cmd, ...rest] = process.argv.slice(2);
  if (cmd === 'dump') { const n = dump(); if (!n) console.log('(no dump)'); else for (const x of n) if (x.text || x.desc || x.clickable) console.log(`${x.clickable ? 'C' : ' '} [${x.x1},${x.y1}][${x.x2},${x.y2}] ${JSON.stringify(x.text)} ${x.desc ? 'desc=' + JSON.stringify(x.desc) : ''} ${x.cls.split('.').pop()}`); }
  else if (cmd === 'tap') console.log(tapText(rest.join(' ')) ? 'tapped' : 'not found');
  else if (cmd === 'shot') shot(rest[0] || 'shot.png');
  else if (cmd === 'text') typeText(rest.join(' '));
  else if (cmd === 'media') console.log(JSON.stringify(mediaSession()));
  else console.log('dump | tap <text> | shot <file> | text <str> | media');
}
