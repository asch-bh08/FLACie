// (from design-shots.mjs) Screenshots of the Android app's design I screens on an emulator, against the mock Jellyfin: sign in, play a song, then Home, the bar,
// Explore's songs and albums, an album, an artist, search, playlists, favourites and settings. Set ANDROID_SERIAL to pick the emulator.
//   node ls-shots.mjs --port 8496 --shots <dir> --adb <adb.exe>
import { spawn, spawnSync } from 'node:child_process';
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

requireEmulator(); await requireMock(MOCK);
// run-android.ps1 turns the emulator's animations off for steady timings; the app follows the phone's setting (Tweaks.motionOn), so they go back on
// before the app starts, and off again at the end for the next run
const anim = (v) => { for (const k of ['window_animation_scale', 'transition_animation_scale', 'animator_duration_scale']) sh(`settings put global ${k} ${v}`, { quiet: true }); };
anim(1); process.on('exit', () => { try { anim(0); } catch { } });
sh(`am force-stop ${PKG}`); sh(`pm clear ${PKG}`);
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

// The cover flight into Now Playing, recorded: a song plays from the Songs list, then a tap on its row (the song playing) opens Now Playing and a
// screen recording catches the cover on its way there and back (flight.mp4 in the shots folder).
//   run-android.ps1 -Apk <apk> -Driver flight-shots.mjs   (shots in the run's -Shots folder)
let row = null;
for (let i = 0; i < 30 && !row; i++) {
  ui = dump();
  row = ui && ui.filter((n) => /^Song \d+$/.test(label(n)) && n.cy > 500)[0];
  if (!row) { const ex = ui && (find(ui, 'Library', { minY: 1800 }) || find(ui, 'Explore', { minY: 1800 })); if (ex) tapNode(ex); await sleep(1500);
    const u2 = dump(); const songs = u2 && u2.find((n) => /^Songs$/.test(label(n)) && n.cy > 300); if (songs) tapNode(songs); await sleep(2000); }
}
if (!row) { console.log('no song row'); process.exit(1); }
tapAt(500, row.cy);
for (let i = 0; i < 60 && mediaSession().state !== 3; i++) await sleep(500);
await sleep(2500);
// the test songs last seconds: pause, so the row tapped is still the song playing (a tap on it opens Now Playing; on another row it plays that one)
key('KEYCODE_MEDIA_PAUSE'); await sleep(1500);
ui = dump();
const now = ui && ui.filter((n) => /^Song \d+$/.test(label(n))).sort((a, b) => b.cy - a.cy)[0];   // the mini player's title is the lowest "Song N"
const cur = now && ui.filter((n) => label(n) === label(now) && n.cy > 300 && n.cy < now.cy - 50)[0];
if (!cur) { console.log('playing row not on screen:', now && label(now)); process.exit(1); }
console.log('playing:', label(now));
// a screen recording through the tap and the Back press (screenshots take about a second each here, longer than the flight); frames come out of
// it afterwards (ffmpeg -i flight.mp4 -vf fps=15 ...)
const adbExe = arg('adb', 'adb');
const rec = spawn(adbExe, ['shell', 'screenrecord', '--time-limit', '7', '--bit-rate', '8000000', '/sdcard/flight.mp4'], { stdio: 'ignore', windowsHide: true });
await sleep(1500);
tapAt(500, cur.cy);                     // the playing song's row: opens Now Playing, its cover flies
await sleep(2500);
key('KEYCODE_BACK');                    // closes Now Playing: the cover flies back to the row
await new Promise((r) => rec.on('exit', r));
await sleep(1000);
spawnSync(adbExe, ['pull', '/sdcard/flight.mp4', path.join(SHOTS, 'flight.mp4')], { stdio: 'ignore', windowsHide: true });
console.log('done');
process.exit(0);
