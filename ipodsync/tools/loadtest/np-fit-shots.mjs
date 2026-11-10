// (from design-shots.mjs) Screenshots of the Android app's design I screens on an emulator, against the mock Jellyfin: sign in, play a song, then Home, the bar,
// Explore's songs and albums, an album, an artist, search, playlists, favourites and settings. Set ANDROID_SERIAL to pick the emulator.
//   node ls-shots.mjs --port 8496 --shots <dir> --adb <adb.exe>
import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
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

// (from flight-shots.mjs) a song plays from the Songs list, then a tap on its row (the song playing) opens Now Playing and a
// screen recording catches the cover on its way there and back (flight.mp4 in the shots folder).
//   run-android.ps1 -Apk <apk> -Driver flight-shots.mjs   (shots in the run's -Shots folder)

// Now Playing must fit every screen with nothing to scroll (owner, 10 Nov: his dad had to scroll to reach the controls and the graph). Opens Now
// Playing, then at each size (a big-text phone, a small phone, on its side, a tablet, the unfolded Fold) takes a screenshot and checks the controls are
// inside the screen. Sizes and text scale go back to normal at the end.
//   run-android.ps1 -Apk <apk> -Driver np-fit-shots.mjs   (shots + np-fit.json in the run's -Shots folder)
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
await sleep(2000);
ui = dump();
const mini = ui && ui.filter((n) => /^Song \d+$/.test(label(n))).sort((a, b) => b.cy - a.cy)[0];
tapAt(mini.cx, mini.cy); await sleep(3000);
const reset = () => { sh('wm size reset', { quiet: true }); sh('wm density reset', { quiet: true }); sh('settings put system font_scale 1.0', { quiet: true });
  sh('settings put system accelerometer_rotation 0', { quiet: true }); sh('settings put system user_rotation 0', { quiet: true }); sh('wm user-rotation lock 0', { quiet: true }); };
process.on('exit', () => { try { reset(); } catch { } });
const sizes = [
  { name: 'phone', size: null, density: null, font: '1.0', rot: 0 },
  { name: 'phone-bigtext', size: null, density: null, font: '1.5', rot: 0 },
  { name: 'small-bigtext', size: '720x1280', density: '320', font: '1.5', rot: 0 },
  { name: 'phone-sideways', size: null, density: null, font: '1.0', rot: 1 },
  { name: 'tablet', size: '1600x2560', density: '320', font: '1.0', rot: 0 },
  { name: 'fold-open', size: '1840x2208', density: '420', font: '1.0', rot: 0 },
  { name: 'fold-open-bigtext', size: '1840x2208', density: '420', font: '1.5', rot: 0 },
];
const must = [/^(Play|Pause)$/, /^Next/, /^Previous/, /^Lyrics$/, /^Up next$/];
const report = [];
const only = arg('only', ''); for (const z of sizes.filter((x) => !only || only.split(',').includes(x.name))) {
  reset();
  if (z.size) sh(`wm size ${z.size}`); if (z.density) sh(`wm density ${z.density}`);
  sh(`settings put system font_scale ${z.font}`);
  if (z.rot) { sh('settings put system accelerometer_rotation 0'); sh(`settings put system user_rotation ${z.rot}`); sh(`wm user-rotation lock ${z.rot}`, { quiet: true }); }
  await sleep(4500);
  // Now Playing should still be open (a text-size change used to restart the screen and drop to Home, 10 Nov): note it, then reopen it to measure
  let u = dump() || [];
  // the app itself gone (a crash, or the system dropping it on a big size change): say which, then start it again
  const front = sh('dumpsys activity activities', { quiet: true });
  if (!new RegExp(`(topResumedActivity|mResumedActivity).*${PKG}`).test(front)) {
    const crash = sh('logcat -b crash -d', { quiet: true }).trim();
    console.log(`${z.name}: APP NOT IN FRONT; crash buffer: ${crash ? crash.split(String.fromCharCode(10)).slice(0, 12).join(' | ') : 'empty'}`);
    sh('logcat -b crash -c', { quiet: true });
    sh(`am start -n ${PKG}/com.ipodemu.ui.MainActivity`, { quiet: true }); await sleep(5000); u = dump() || [];
  }
  const stillOpen = u.some((x) => /^(Lyrics|Up next)$/.test(label(x)));
  if (!stillOpen) { const mp = u.filter((x) => /^Song \d+$/.test(label(x))).sort((a, b) => b.cy - a.cy)[0]; if (mp) { tapAt(mp.cx, mp.cy); await sleep(3000); u = dump() || []; } }
  shot(S('np-' + z.name));
  const m = /(\d+)x(\d+)/.exec(sh('wm size', { quiet: true }).split('\n').filter(Boolean).pop() || '');
  let W = m ? Number(m[1]) : 1080, H = m ? Number(m[2]) : 2400; if (z.rot % 2) [W, H] = [H, W];
  const missing = [];
  for (const re of must) {
    const n = u.find((x) => re.test(label(x)));
    if (!n) missing.push(String(re) + ' not found');
    else if (n.cy > H - 10 || n.cy < 0 || n.cx > W || n.cx < 0) missing.push(`${label(n)} off screen at ${n.cx},${n.cy} (screen ${W}x${H})`);
  }
  report.push({ size: z.name, ok: missing.length === 0 && stillOpen, stayedOpen: stillOpen, missing });
  console.log(`${z.name}: ${stillOpen ? '' : 'NOW PLAYING CLOSED BY THE CHANGE; '}${missing.length ? 'PROBLEM ' + missing.join('; ') : 'all controls on screen'}`);
}
fs.writeFileSync(path.join(SHOTS, 'np-fit.json'), JSON.stringify(report, null, 2));
console.log('done');
process.exit(0);
