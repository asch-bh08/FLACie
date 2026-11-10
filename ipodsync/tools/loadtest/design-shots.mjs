// Screenshots of the Android app's design I screens on an emulator, against the mock Jellyfin: sign in, play a song, then Home, the bar,
// Explore's songs and albums, an album, an artist, search, playlists, favourites and settings. Set ANDROID_SERIAL to pick the emulator.
//   node ls-shots.mjs --port 8496 --shots <dir> --adb <adb.exe>
import { spawn } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { requireEmulator, sh, sleep, setAdb, dump, has, find, label, tapNode, tapAt, typeText, key, shot, waitUi, mediaSession } from './adbui.mjs';
import { requireMock } from './require-mock.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const PORT = Number(arg('port', 8496)), SHOTS = arg('shots', '.');
setAdb(arg('adb', ''));
const PKG = 'app.flacie', MOCK = `http://127.0.0.1:${PORT}`, DEVICE_MOCK = `http://10.0.2.2:${PORT}`;
const S = (n) => path.join(SHOTS, n + '.png');

const mock = spawn('node', [path.join(here, 'mock.mjs'), '--port', String(PORT), '--host', '127.0.0.1', '--items', '200', '--streamms', '300'], { stdio: 'ignore', windowsHide: true });
process.on('exit', () => { try { mock.kill('SIGKILL'); } catch { } });
for (let i = 0; i < 100; i++) { try { if ((await fetch(`${MOCK}/System/Info/Public`)).ok) break; } catch { } await sleep(200); }
await fetch(`${MOCK}/__profile?name=fast`);

requireEmulator(); await requireMock(MOCK); sh(`am force-stop ${PKG}`); sh(`pm clear ${PKG}`);
sh(`pm grant ${PKG} android.permission.POST_NOTIFICATIONS`, { quiet: true });
sh(`am start -n ${PKG}/com.ipodemu.ui.MainActivity`, { quiet: true });
let ui = await waitUi((n) => has(n, 'Sign in with Jellyfin'), 120000, 2000);
tapNode(find(ui, 'Sign in with Jellyfin'));
ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 30000, 1000);
const field = ui?.find((n) => /EditText/.test(n.cls));
tapAt(field.cx, field.cy); await sleep(600); typeText(DEVICE_MOCK); await sleep(600); key('KEYCODE_BACK'); await sleep(800);
ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 20000, 1000);
tapNode(find(ui, 'Sign in with Quick Connect'));
await waitUi((n) => !has(n, 'Sign in with Quick Connect') && !has(n, 'Signing in'), 60000, 1500);
await sleep(4000);
for (let i = 0; i < 30; i++) {
  ui = dump();
  const row = ui && ui.filter((n) => /^Song \d+$/.test(label(n)) && n.cy > 500)[0];
  if (row) { tapAt(500, row.cy); break; }
  const ex = ui && (find(ui, 'Library', { minY: 1800 }) || find(ui, 'Explore', { minY: 1800 })); if (ex) tapNode(ex);
  const songs = ui && ui.find((n) => /^Songs$/.test(label(n)) && n.cy > 300); if (songs && ex) { await sleep(1500); tapNode(songs); }
  await sleep(2000);
}
for (let i = 0; i < 60 && mediaSession().state !== 3; i++) await sleep(500);
await sleep(2000);
// the bottom bar's places since b124: Home, Search, Library (was Explore), Settings; logs what it saw when a place is missing
const nav = async (...names) => { const u = dump(); const n = u && names.map((nm) => find(u, nm, { minY: 1900 })).find(Boolean); if (n) tapNode(n); else console.log('nav: none of', names, 'in', (u || []).map(label).filter(Boolean).slice(0, 40).join(' | ')); await sleep(2500); };
const tapLabel = async (re, minY = 300) => { const u = dump(); const n = u && u.find((x) => re.test(label(x)) && x.cy > minY); if (n) { tapNode(n); await sleep(2500); return true; } return false; };
await nav('Home'); await nav('Home');
await tapLabel(/^Got it$/);   // the first-run welcome card
shot(S('android-home'));
sh('input swipe 540 1600 540 700 400'); await sleep(1500);
shot(S('android-home-shelves'));
await nav('Library', 'Explore'); await tapLabel(/^Songs$/);
shot(S('android-browse-songs'));
await tapLabel(/^Albums$/); shot(S('android-browse-albums'));
await tapLabel(/Album \d+/, 600); await sleep(1500); shot(S('android-detail-album'));
key('KEYCODE_BACK'); await sleep(1500);
await tapLabel(/^Artists$/); await tapLabel(/Artist \d+/, 600); await sleep(1500); shot(S('android-detail-artist'));
await nav('Search'); await sleep(500); { const u = dump(); const f = u && u.find((n) => /EditText/.test(n.cls)); if (f) tapAt(f.cx, f.cy); await sleep(500); } typeText('Song 1'); await sleep(2500); key('KEYCODE_BACK'); await sleep(1200); shot(S('android-search'));
key('KEYCODE_BACK'); await sleep(800); await nav('Library', 'Playlists'); await tapLabel(/^Playlists$/); shot(S('android-playlists'));
await tapLabel(/^Favou?rites$/); shot(S('android-favourites'));
await nav('Settings'); shot(S('android-settings'));
console.log('done');
process.exit(0);
