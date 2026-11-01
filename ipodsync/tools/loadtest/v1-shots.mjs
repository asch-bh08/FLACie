// Screenshots of the Android app's main screens for a look-over before 1.0 (owner, 1 Nov: "no spacing bugs", "fits any phone"): Home, Library,
// Songs, an album, Playlists, Offline music, Search with the keyboard up and after, the full-screen player with its Lyrics and Info cards, and
// Settings. Started like the smoke test, which brings the emulator up:
//   run-android.ps1 -Apk <apk> -Port 8297 -Driver v1-shots.mjs [-Shots <dir>]
// Set SIZE=WxH (and DENSITY) in -Extra to see the same screens on another phone size (adb shell wm size / wm density, put back at the end).
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import { sh, sleep, setAdb, dump, has, find, tapNode, tapAt, typeText, key, waitUi, label, shot, mediaSession } from './adbui.mjs';
const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const PORT = Number(arg('port', 8297)), SHOTS = arg('shots', '.');
setAdb(arg('adb', ''));
const PKG = 'app.flacie', MOCK = `http://127.0.0.1:${PORT}`;
const S = (n) => path.join(SHOTS, n + '.png');
const sizes = (process.env.SIZES || 'native').split(',');

const mock = spawn('node', [path.join(here, 'mock.mjs'), '--port', String(PORT), '--host', '127.0.0.1', '--items', '400'], { stdio: 'ignore', windowsHide: true });
process.on('exit', () => { try { mock.kill('SIGKILL'); } catch { } try { sh('wm size reset'); sh('wm density reset'); } catch { } });
for (let i = 0; i < 100; i++) { try { if ((await fetch(`${MOCK}/System/Info/Public`)).ok) break; } catch { } await sleep(200); }
await fetch(`${MOCK}/__profile?name=fast`); await fetch(`${MOCK}/__reset`);

sh(`am force-stop ${PKG}`); sh(`pm clear ${PKG}`);
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

const h = () => Number((sh('wm size', { quiet: true }) || '').match(/(\d+)x(\d+)\s*$/m)?.[2] || 2400);
const tab = async (name) => { const u = dump(); const n = u && u.filter((x) => label(x) === name).sort((a, b) => b.cy - a.cy)[0]; if (n) tapNode(n); await sleep(2200); return !!n; };
const tapLabel = async (re, minY = 250) => { const u = dump(); const n = u && u.find((x) => re.test(label(x)) && x.cy > minY); if (n) { tapNode(n); await sleep(2200); return true; } console.log('not found: ' + re); return false; };
// Back, then FLACie to the front again in case that Back left it (singleTask: the same screen comes back, nothing restarts)
const back = async () => { key('KEYCODE_BACK'); await sleep(1200); sh(`am start -n ${PKG}/com.ipodemu.ui.MainActivity`, { quiet: true }); await sleep(1200); };

for (const size of sizes) {
  const tag = size === 'native' ? '' : '-' + size.replace(/[^0-9x@]/g, '');
  if (size !== 'native') { const [wh, dpi] = size.split('@'); sh(`wm size ${wh}`); if (dpi) sh(`wm density ${dpi}`); await sleep(4000); }
  await tab('Home'); await tab('Home'); shot(S('home' + tag));
  await tab('Library'); shot(S('library' + tag));
  await tapLabel(/^Songs$/); await sleep(1000); shot(S('songs' + tag));
  // a song plays: the mini player
  await tapLabel(/^Song \d+/, 400); await sleep(3000); await back();
  shot(S('mini-player' + tag));
  await back(); await tab('Library'); await tab('Library'); await tapLabel(/^Albums$/); await tapLabel(/Album \d+/, 400); await sleep(1200); shot(S('album' + tag));
  await tab('Library'); await tab('Library'); await tapLabel(/^Playlists$/); shot(S('playlists' + tag));
  await tab('Library'); await tab('Library'); await tapLabel(/^Offline music$/); shot(S('offline-music' + tag));
  await back();
  // search: with the keyboard up (no band between the results and the keyboard), then after
  await tab('Search'); { const u = dump(); const f = u && u.find((n) => /EditText/.test(n.cls)); if (f) tapNode(f); } await sleep(800);
  typeText('Song 1'); await sleep(3000); shot(S('search-keyboard' + tag));
  await back(); await sleep(800); shot(S('search' + tag));
  // the full-screen player: open from the mini player (the song's title near the bottom), then its Lyrics and Info cards
  await tab('Home');
  { const u = dump(); const t = u && u.filter((n) => /^Song \d+/.test(label(n)) && n.cy > h() * 0.75).sort((a, b) => b.cy - a.cy)[0]; if (t) tapNode(t); }
  await sleep(2500); shot(S('player' + tag));
  if (await tapLabel(/^Lyrics$/, h() * 0.6)) { shot(S('player-lyrics' + tag)); await tapLabel(/^Lyrics$/, h() * 0.6); }
  if (await tapLabel(/^(Song info|Info)$/, h() * 0.6)) { await sleep(1500); shot(S('player-info' + tag)); await tapLabel(/^(Song info|Info)$/, h() * 0.6); }
  await back();
  await tab('Settings'); shot(S('settings' + tag));
  sh('input swipe 540 1700 540 600 400'); await sleep(1200); shot(S('settings-2' + tag));
  if (size !== 'native') { sh('wm size reset'); sh('wm density reset'); await sleep(3000); }
}
console.log('done; playing: ' + mediaSession().state);
process.exit(0);
