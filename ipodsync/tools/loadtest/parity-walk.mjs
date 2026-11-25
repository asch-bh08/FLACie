import fs from 'node:fs';
// Parity walk (owner, 25 Nov: "check with the emulator"): visits each phone screen, scrolls it, and writes every label it shows to parity-labels.txt.
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import { requireEmulator, sh, sleep, setAdb, dump, has, find, tapNode, tapAt, typeText, key, waitUi, label, shot } from './adbui.mjs';
import { requireMock } from './require-mock.mjs';
const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const PORT = Number(arg('port', 8297)), SHOTS = arg('shots', 'C:/wt/android-shots/parity');
fs.mkdirSync(SHOTS, { recursive: true });
const PKG = 'app.flacie', MOCK = `http://127.0.0.1:${PORT}`;
const mock = spawn('node', [path.join(here, 'mock.mjs'), '--port', String(PORT), '--host', '127.0.0.1', '--items', '400'], { stdio: 'ignore', windowsHide: true });
process.on('exit', () => { try { mock.kill('SIGKILL'); } catch { } try { sh('wm size reset'); sh('wm density reset'); } catch { } });
for (let i = 0; i < 100; i++) { try { if ((await fetch(`${MOCK}/System/Info/Public`)).ok) break; } catch { } await sleep(200); }
await fetch(`${MOCK}/__profile?name=fast`); await fetch(`${MOCK}/__reset`);

requireEmulator(); await requireMock(MOCK); sh(`am force-stop ${PKG}`); sh(`pm clear ${PKG}`);
sh(`pm grant ${PKG} android.permission.POST_NOTIFICATIONS`, { quiet: true });
sh(`am start -n ${PKG}/com.ipodemu.ui.MainActivity`, { quiet: true });
let ui = await waitUi((n) => has(n, 'Sign in with Jellyfin'), 120000, 2000);
tapNode(find(ui, 'Sign in with Jellyfin'));
ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 30000, 1000);
const field = ui.find((n) => /EditText/.test(n.cls));
const addr = `http://10.0.2.2:${PORT}`;
for (let i = 0; i < 4; i++) {
  tapAt(field.cx, field.cy); await sleep(600); key('KEYCODE_MOVE_END'); key('--longpress KEYCODE_DEL'); await sleep(300);
  for (let k = 0; k < 3; k++) key('KEYCODE_DEL ' + 'KEYCODE_DEL '.repeat(12));
  typeText(addr); await sleep(1000);
  if ((dump() || []).some((n) => /EditText/.test(n.cls) && n.text === addr)) break;
}
key('KEYCODE_BACK'); await sleep(800);
ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 20000, 1000);
tapNode(find(ui, 'Sign in with Quick Connect'));
if (!await waitUi((n) => !has(n, 'Sign in with Quick Connect') && !has(n, 'Jellyfin server') && !has(n, 'Signing in'), 60000, 1500)) { console.log('sign-in failed'); process.exit(1); }
await sleep(6000);
{ const u = dump(); const g = u && find(u, 'Got it'); if (g) { tapNode(g); await sleep(800); } }

const out = [];
const grab = async (name, scrolls = 4) => {
  const seen = new Set();
  for (let i = 0; i <= scrolls; i++) {
    for (const n of dump() || []) { const l = label(n).trim(); if (l) seen.add(l.replace(/\s+/g, ' ')); }
    if (i === 0) shot(path.join(SHOTS, name + '.png'));
    if (i < scrolls) { sh('input swipe 540 1700 540 700 300'); await sleep(900); }
  }
  out.push(`## ${name}`, ...[...seen].map((s) => '- ' + s), '');
  fs.writeFileSync(path.join(here, 'parity-labels.txt'), out.join('\n'));
  console.log('grabbed ' + name + ' (' + seen.size + ')');
};
const tab = async (name) => { const u = dump(); const n = u && u.filter((x) => label(x) === name).sort((a, b) => b.cy - a.cy)[0]; if (n) tapNode(n); await sleep(2200); return !!n; };
const tapRe = async (re, minY = 250) => { const u = dump(); const n = u && u.find((x) => re.test(label(x)) && x.cy > minY); if (n) { tapNode(n); await sleep(2200); return true; } console.log('not found: ' + re); return false; };
const longRe = async (re, minY = 250) => { const u = dump(); const n = u && u.find((x) => re.test(label(x)) && x.cy > minY); if (n) { sh(`input swipe ${n.cx} ${n.cy} ${n.cx} ${n.cy} 900`); await sleep(1500); return true; } console.log('not found: ' + re); return false; };
const back = async () => { key('KEYCODE_BACK'); await sleep(1200); sh(`am start -n ${PKG}/com.ipodemu.ui.MainActivity`, { quiet: true }); await sleep(1000); };
const top = async () => { for (let i = 0; i < 4; i++) sh('input swipe 540 700 540 1800 200'); await sleep(600); };
const safe = async (f) => { try { await f(); } catch (e) { console.log('step failed: ' + e.message); } };

await safe(async () => { await tab('Home'); await grab('home', 5); });
await safe(async () => { await tab('Explore') || await tab('Discover'); await grab('explore', 5); });
await safe(async () => { await tab('Library'); await top(); await grab('library', 2); });
await safe(async () => { await top(); await tapRe(/^Songs$/); await grab('songs', 1); await longRe(/^Song \d+/, 400); await grab('song-menu', 3); await back(); });
await safe(async () => { await tab('Library'); await top(); await tapRe(/^Albums$/); await grab('albums', 1); await tapRe(/Album/, 500); await grab('album', 3); await back(); await back(); });
await safe(async () => { await tab('Library'); await top(); await tapRe(/^Artists$/); await grab('artists', 1); await tapRe(/Artist/, 500); await grab('artist', 4); await back(); await back(); });
await safe(async () => { await tab('Library'); await top(); await tapRe(/^Playlists$/); await grab('playlists', 1); await back(); });
await safe(async () => { await tab('Library'); await top(); await tapRe(/^Genres$/); await grab('genres', 1); await back(); });
await safe(async () => { await tab('Search'); await typeText('song'); await sleep(2500); key('KEYCODE_BACK'); await grab('search', 3); });
await safe(async () => { await tab('Library'); await top(); await tapRe(/^Songs$/); await tapRe(/^Song \d+/, 400); await sleep(3000); await grab('mini', 0);
  const u = dump(); const m = u && u.filter((x) => x.cy > 1500 && /Song \d+/.test(label(x)))[0]; if (m) { tapNode(m); await sleep(2500); }
  await grab('now-playing', 4); key('KEYCODE_BACK'); await sleep(1200); });
await safe(async () => { await tab('Settings') || (await tapRe(/Settings/, 0)); await top(); await grab('settings', 14); });
fs.writeFileSync(path.join(here, 'parity-labels.txt'), out.join('\n'));
console.log('done');
process.exit(0);
