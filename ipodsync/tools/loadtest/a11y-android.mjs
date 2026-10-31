// Android accessibility check: signs in to the mock and lists, per main screen (Home, Search, Library, Devices, Songs, the player), every
// clickable element a screen reader cannot name (no text or description of its own or inside it). Started like the smoke test, which brings the emulator up:
//   run-android.ps1 -Apk <apk> -Port 8297 -Driver a11y-android.mjs      (all should say 0 unnamed; exit code 1 when any screen has one or can't be reached)
// It starts its own mock Jellyfin on --port (run-android.ps1 doesn't), as smoke-android.mjs does.
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import { sh, sleep, setAdb, dump, has, find, tapNode, tapAt, typeText, key, waitUi, label } from './adbui.mjs';
const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const PORT = Number(arg('port', 8297)), ITEMS = Number(arg('items', 10000));
setAdb(arg('adb', ''));
const PKG = 'com.ipodemu';
const MOCK = `http://127.0.0.1:${PORT}`;

const mock = spawn('node', [path.join(here, 'mock.mjs'), '--port', String(PORT), '--host', '127.0.0.1', '--items', String(ITEMS)], { stdio: 'ignore', windowsHide: true });
process.on('exit', () => { try { mock.kill('SIGKILL'); } catch { } });
for (let i = 0; i < 100; i++) { try { if ((await fetch(`${MOCK}/System/Info/Public`)).ok) break; } catch { } await sleep(200); }
await fetch(`${MOCK}/__profile?name=fast`); await fetch(`${MOCK}/__reset`);

sh(`am force-stop ${PKG}`); sh(`pm clear ${PKG}`);
sh(`pm grant ${PKG} android.permission.POST_NOTIFICATIONS`, { quiet: true });
sh(`pm revoke ${PKG} android.permission.READ_MEDIA_AUDIO`, { quiet: true });
sh(`am start -n ${PKG}/.ui.MainActivity`, { quiet: true });
let ui = await waitUi((n) => has(n, 'Sign in with Jellyfin'), 120000, 2000);
tapNode(find(ui, 'Sign in with Jellyfin'));
ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 30000, 1000);
const field = ui.find((n) => /EditText/.test(n.cls));
tapAt(field.cx, field.cy); await sleep(600); typeText(`http://10.0.2.2:${PORT}`); await sleep(600); key('KEYCODE_BACK'); await sleep(800);
ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 20000, 1000);
tapNode(find(ui, 'Sign in with Quick Connect'));
const signedIn = await waitUi((n) => !has(n, 'Sign in with Quick Connect') && !has(n, 'Jellyfin server') && !has(n, 'Signing in'), 60000, 1500);
if (!signedIn) { console.log('sign-in did not complete'); process.exit(1); }
await sleep(8000);

let failed = false;
const report = (name) => {
  const nodes = dump() || [];
  // a clickable node with no words of its own and none inside it is a button a screen reader can't name
  const bad = nodes.filter((n) => n.clickable && !label(n) && !nodes.some((c) => c !== n && c.x1 >= n.x1 && c.x2 <= n.x2 && c.y1 >= n.y1 && c.y2 <= n.y2 && label(c)));
  if (bad.length) failed = true;
  console.log(name + ': ' + bad.length + ' unnamed' + bad.map((b) => ` [${b.x1},${b.y1}][${b.x2},${b.y2}] ${b.cls.split('.').pop()}`).join(''));
};
// a tab: the lowest thing on screen named exactly so (the tab bar)
const tab = (name) => { const n = (dump() || []).filter((x) => label(x) === name && x.w > 0 && x.h > 0).sort((a, b) => b.cy - a.cy)[0]; if (n) tapNode(n); else { failed = true; console.log(name + ': tab not found'); } return !!n; };

report('Home');
for (const t of ['Search', 'Library']) { if (tab(t)) { await sleep(3500); report(t); } }
// Devices is a tab only while another device can play (Modern.kt tabsFor)
if ((dump() || []).some((x) => label(x) === 'Devices')) { tab('Devices'); await sleep(3500); report('Devices'); key('KEYCODE_BACK'); await sleep(1500); }
// design I: the song list is Search's Songs chip
if (tab('Search')) { await sleep(1500); const chip = find(dump(), /^Songs$/); if (chip) { tapNode(chip); await sleep(3500); } report('Songs'); }
const row = (dump() || []).find((n) => /^Song \d+/.test(label(n))); if (row) { tapAt(500, row.cy); await sleep(5000); }
// the mini player is named by its song: open it by its left end, beside its Pause button
const pause = (dump() || []).filter((n) => label(n) === 'Pause').sort((a, b) => b.cy - a.cy)[0];
if (pause) { tapAt(200, pause.cy); await sleep(4000); report('Player'); } else { failed = true; console.log('Player: nothing playing (no Pause in the mini player)'); }
key('KEYCODE_BACK');
process.exit(failed ? 1 : 0);
