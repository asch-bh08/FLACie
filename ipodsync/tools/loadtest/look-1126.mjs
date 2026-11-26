import fs from 'node:fs';
// Look at the 26 Nov screens (owner: "lets have a look"): Now Playing like the web, Devices, Settings > Playback (favourites switch), a playlist.
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import { requireEmulator, sh, sleep, setAdb, dump, has, find, tapNode, tapAt, typeText, key, waitUi, label, shot } from './adbui.mjs';
import { requireMock } from './require-mock.mjs';
const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const PORT = Number(arg('port', 8297)), SHOTS = arg('shots', 'C:/wt/android-shots/look-1126');
fs.mkdirSync(SHOTS, { recursive: true });
const PKG = 'app.flacie', MOCK = `http://127.0.0.1:${PORT}`;
const mock = spawn('node', [path.join(here, 'mock.mjs'), '--port', String(PORT), '--host', '127.0.0.1', '--items', '400'], { stdio: 'ignore', windowsHide: true });
process.on('exit', () => { try { mock.kill('SIGKILL'); } catch { } try { sh('wm size reset'); sh('wm density reset'); } catch { } });
for (let i = 0; i < 100; i++) { try { if ((await fetch(`${MOCK}/System/Info/Public`)).ok) break; } catch { } await sleep(200); }
await fetch(`${MOCK}/__profile?name=fast`); await fetch(`${MOCK}/__reset`);

// a playlist and favourites in the account, so the playlist page has songs (as playlists-android.mjs seeds them)
{ const AUTH = { 'X-Emby-Token': 'mock-token', 'content-type': 'application/json' }, BASE = `http://10.0.2.2:${PORT}`;
  const items = (await (await fetch(`${MOCK}/Users/u1/Items?IncludeItemTypes=Audio&Recursive=true&Limit=60`, { headers: AUTH })).json()).Items;
  const song = (i) => ({ p: `${BASE}/Audio/${items[i].Id}/stream?static=true`, t: items[i].Name, a: items[i].Artists?.[0] ?? items[i].AlbumArtist });
  const profile = { v: 2, updated: Date.now(), services: {}, favorites: Array.from({ length: 6 }, (_, i) => ({ ...song(i + 20), w: Date.now() - i * 1000 })),
    playlists: [{ id: 'look1', n: 'Late Night Drive', m: Date.now(), jf: null, tracks: Array.from({ length: 18 }, (_, i) => song(i + 2)) }] };
  const r = await fetch(`${MOCK}/DisplayPreferences/ipodplayer?userId=x&client=ipodplayer`, { method: 'POST', headers: AUTH, body: JSON.stringify({ CustomPrefs: { 'ipodplayer.profile': JSON.stringify(profile) }, Client: 'ipodplayer' }) });
  console.log('seeded', r.status); }
requireEmulator(); await requireMock(MOCK); sh(`am force-stop ${PKG}`); sh(`pm clear ${PKG}`);
sh(`pm grant ${PKG} android.permission.POST_NOTIFICATIONS`, { quiet: true });
sh(`pm revoke ${PKG} android.permission.READ_MEDIA_AUDIO`, { quiet: true });
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
if (!await waitUi((n) => !has(n, 'Sign in with Quick Connect') && !has(n, 'Jellyfin server') && !has(n, 'Signing in'), 60000, 1500)) { shot(path.join(SHOTS, 'signin-failed.png')); console.log('sign-in failed; on screen:', (dump() || []).map(label).filter(Boolean).slice(0, 30).join(' | ')); process.exit(1); }
await sleep(6000);
{ const u = dump(); const g = u && find(u, 'Got it'); if (g) { tapNode(g); await sleep(800); } }

const tab = async (name) => { const u = dump(); const n = u && u.filter((x) => label(x) === name).sort((a, b) => b.cy - a.cy)[0]; if (n) tapNode(n); await sleep(2200); return !!n; };
const S = (n) => path.join(SHOTS, n + '.png');
await tab('Library'); { const u = dump(); const n = u.find((x) => label(x) === 'Songs'); if (n) tapNode(n); await sleep(2500); }
{ const u = dump(); const n = u.find((x) => /^Song \d+/.test(label(x)) && x.cy > 400); if (n) tapNode(n); await sleep(4000); }
shot(S('1-bar'));
{ const u = dump(); const p = u.filter((x) => label(x) === 'Pause' || label(x) === 'Play').sort((a, b) => b.cy - a.cy)[0]; if (p) { tapAt(300, p.cy); await sleep(3000); } }
shot(S('2-now-playing'));
{ const u = dump(); const h = u.find((x) => /favourites/i.test(label(x)) && x.cy > 1400); if (h) { tapNode(h); await sleep(1200); } shot(S('2b-hearted')); }
key('KEYCODE_BACK'); await sleep(1500);
await tab('Library'); { const u = dump(); const n = u.find((x) => /playlists/i.test(label(x))); if (n) { tapNode(n); await sleep(2500); } }
{ const u = dump(); console.log('playlists screen:', u.map(label).filter(Boolean).slice(0, 40).join(' | '));
  const n = u.find((x) => x.cy > 400 && /Late Night Drive/.test(label(x))); console.log('playlist node:', n && label(n)); if (n) { tapNode(n); await sleep(3000); } }
shot(S('5-playlist'));
sh('input swipe 540 1700 540 800 300'); await sleep(1200); shot(S('5b-playlist-lower'));
key('KEYCODE_BACK'); await sleep(1500);
{ const u = dump(); const n = u.find((x) => /^Favourites/.test(label(x))); if (n) { tapNode(n); await sleep(2500); } shot(S('5c-favourites')); }
await tab('Settings'); await sleep(1500); shot(S('6-settings'));
console.log('done');
process.exit(0);
