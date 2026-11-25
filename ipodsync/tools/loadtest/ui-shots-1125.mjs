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

// Screens changed on 25 Nov: Library tiles, Home playlists, Now Playing's Settings button, Find a setting.
const tab = async (name) => { const u = dump(); const n = u && u.filter((x) => label(x) === name).sort((a, b) => b.cy - a.cy)[0]; if (n) tapNode(n); await sleep(2200); return !!n; };
const S = (n) => path.join(SHOTS, n + '.png');
// a playlist so the shelves show
await tab('Library'); await sleep(1000); shot(S('library'));
{ const u = dump(); const n = u.find((x) => /^Your playlists$/.test(label(x))); if (n) { tapNode(n); await sleep(2000); } shot(S('playlists-from-library')); key('KEYCODE_BACK'); await sleep(1500); }
await tab('Home'); await sleep(1500); shot(S('home'));
sh('input swipe 540 1700 540 900 300'); await sleep(1200); shot(S('home-2'));
await tab('Library'); { const u = dump(); const n = u.find((x) => label(x) === 'Songs'); if (n) tapNode(n); await sleep(2500); }
{ const u = dump(); const n = u.find((x) => /^Song \d+/.test(label(x)) && x.cy > 400); if (n) tapNode(n); await sleep(3500); }
{ const u = dump(); const p = u.filter((x) => label(x) === 'Pause' || label(x) === 'Play').sort((a, b) => b.cy - a.cy)[0]; if (p) { tapAt(300, p.cy); await sleep(2500); } }
shot(S('now-playing'));
{ const u = dump(); console.log('NP top has Settings:', !!u.find((x) => label(x) === 'Settings' && x.cy < 400)); const n = u.find((x) => label(x) === 'Settings' && x.cy < 400); if (n) { tapNode(n); await sleep(2500); } }
shot(S('settings'));
{ const u = dump(); const f = u.find((x) => /Find a setting/.test(label(x)) || /EditText/.test(x.cls)); if (f) { tapNode(f); await sleep(800); typeText('fade'); await sleep(1500); key('KEYCODE_BACK'); await sleep(800); } }
shot(S('settings-find-fade'));
console.log('done');
process.exit(0);
