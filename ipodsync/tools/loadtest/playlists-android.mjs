// Playlists on the phone and in the car, end to end on the emulator (owner, 6 Nov: "clicking play all or shuffle on a playlist doesn't reload that playlist
// anymore and songs didn't even play at all"). A mock Jellyfin whose shared profile holds three playlists (one whose songs moved: the paths are stale, the
// titles right), the APK signed in with Quick Connect, then:
//   the phone: Library > Playlists > a playlist > Play, Next presses, another playlist's Shuffle, back to the first;
//   the car: CarPlaylistTest (a stand-in Android Auto, src/androidTest) run with am instrument against the same signed-in app.
// Needs the DEBUG APK and its test APK (the release build is minified, so the test can't see its classes):
//   gradlew :app:assembleDebug :app:assembleDebugAndroidTest
//   run-android.ps1 -Apk <app-debug.apk> -Driver playlists-android.mjs -Extra "--testapk <app-debug-androidTest.apk>"
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { requireMock } from './require-mock.mjs';
import { requireEmulator, adb, sh, sleep, setAdb, has, find, label, tapNode, tapAt, typeText, key, waitUi, dump, mediaSession, shot } from './adbui.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const PORT = Number(arg('port', 8297)) + 1;
setAdb(arg('adb', ''));
const TESTAPK = arg('testapk', path.resolve(here, '../../../android/app/build/outputs/apk/androidTest/debug/app-debug-androidTest.apk'));
const PKG = 'app.flacie';
const MOCK = `http://127.0.0.1:${PORT}`, PHONE_BASE = `http://10.0.2.2:${PORT}`;
const AUTH = { 'X-Emby-Token': 'mock-token', 'content-type': 'application/json' };
const kids = [];
const stopAll = () => { for (const k of kids) { try { k.kill('SIGKILL'); } catch { } } };
process.on('exit', stopAll);
const log = (...a) => console.error('[pl]', ...a);
const checks = [];
const check = (name, pass, detail = '') => { checks.push({ name, pass: !!pass, detail: String(detail) }); log(`${pass ? 'PASS' : 'FAIL'} ${name}${detail ? ` (${detail})` : ''}`); };
const titleOf = (d) => String(d ?? '').split(',')[0].trim();

async function waitOk(url, tries = 150) { for (let i = 0; i < tries; i++) { try { if ((await fetch(url)).ok) return true; } catch { } await sleep(400); } return false; }

async function main() {
  kids.push(spawn('node', [path.join(here, 'mock.mjs'), '--port', String(PORT), '--host', '0.0.0.0', '--items', '60', '--wavsec', '60'], { stdio: 'ignore', windowsHide: true }));
  if (!await waitOk(`${MOCK}/System/Info/Public`)) throw new Error('mock did not start');
  await requireMock(MOCK);
  const items = (await (await fetch(`${MOCK}/Users/u1/Items?IncludeItemTypes=Audio&Recursive=true&Limit=100`, { headers: AUTH })).json()).Items;
  const song = (i) => ({ p: `${PHONE_BASE}/Audio/${items[i].Id}/stream?static=true`, t: items[i].Name, a: items[i].Artists?.[0] ?? items[i].AlbumArtist });
  const A = Array.from({ length: 15 }, (_, i) => song(i + 2));
  const B = Array.from({ length: 10 }, (_, i) => song(i + 30));
  // songs whose files moved: the saved paths name ids the server no longer has, the titles and artists are right (the artist-name fixes renamed files)
  const S = Array.from({ length: 8 }, (_, i) => ({ ...song(i + 45), p: `${PHONE_BASE}/Audio/${'f'.repeat(31)}${i}/stream?static=true` }));
  const pl = (id, n, tracks) => ({ id, n, m: Date.now(), jf: null, tracks });
  const profile = { v: 2, updated: Date.now(), services: {}, playlists: [pl('cartesta', 'Car Test A', A), pl('cartestb', 'Car Test B', B), pl('cartests', 'Car Test Moved', S)] };
  const seed = await fetch(`${MOCK}/DisplayPreferences/ipodplayer?userId=x&client=ipodplayer`, { method: 'POST', headers: AUTH, body: JSON.stringify({ CustomPrefs: { 'ipodplayer.profile': JSON.stringify(profile) }, Client: 'ipodplayer' }) });
  if (!seed.ok) throw new Error('profile not seeded: HTTP ' + seed.status);

  requireEmulator(); sh(`am force-stop ${PKG}`); sh(`pm clear ${PKG}`);
  sh(`pm grant ${PKG} android.permission.POST_NOTIFICATIONS`, { quiet: true });
  sh(`am start -n ${PKG}/com.ipodemu.ui.MainActivity`, { quiet: true });
  let ui = await waitUi((n) => has(n, 'Sign in with Jellyfin'), 120000, 2000);
  tapNode(find(ui, 'Sign in with Jellyfin'));
  ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 30000, 1000);
  const field = ui?.find((n) => /EditText/.test(n.cls));
  tapAt(field.cx, field.cy); await sleep(600); typeText(PHONE_BASE); await sleep(600); key('KEYCODE_BACK'); await sleep(800);
  ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 20000, 1000);
  tapNode(find(ui, 'Sign in with Quick Connect'));
  await waitUi((n) => !has(n, 'Sign in with Quick Connect') && !has(n, 'Jellyfin server') && !has(n, 'Signing in'), 60000, 1500);
  await sleep(20000);
  const got = (await waitUi(() => true, 2000, 500)) ?? [];
  if (has(got, 'Got it')) tapNode(find(got, 'Got it'));

  // ---- the phone ----
  const open = async (name) => {
    const nav = find(dump() ?? [], 'Library', { minY: 2000 }); if (nav) tapNode(nav); await sleep(1500);
    const row = await waitUi((n) => has(n, 'Playlists'), 10000, 800); const r = row && find(row, 'Playlists'); if (r) tapNode(r); await sleep(1500);
    const lists = await waitUi((n) => has(n, name), 15000, 1000);
    if (!lists) return false;
    tapNode(find(lists, name)); await sleep(2000);
    return true;
  };
  const press = async (button) => {
    const u = await waitUi((n) => n.some((x) => label(x) === button), 10000, 800);
    const b = u?.find((x) => label(x) === button); if (!b) return null;
    // what played before is still PLAYING for a moment: wait for the new queue's song (a different title, or the same one from a fresh start)
    const was = mediaSession();
    tapNode(b);
    for (let i = 0; i < 40; i++) { await sleep(500); const m = mediaSession(); if (m.state === 3 && (titleOf(m.description) !== titleOf(was.description) || m.position < 1500 && was.position > 3000)) return titleOf(m.description); }
    const m = mediaSession(); return m.state === 3 ? titleOf(m.description) : null;
  };
  const nextSong = async () => { const was = titleOf(mediaSession().description); key('KEYCODE_MEDIA_NEXT'); for (let i = 0; i < 20; i++) { await sleep(400); const t = titleOf(mediaSession().description); if (t && t !== was) return t; } return titleOf(mediaSession().description); };
  const titles = (l) => l.map((s) => s.t);

  check('the phone lists "Car Test A"', await open('Car Test A'));
  shot(path.join(here, 'pl-shot-a.png'));
  let now = await press('Play');
  check('Play on Car Test A plays its first song', now === A[0].t, now);
  for (let i = 0; i < 6; i++) { const t = await nextSong(); check(`Next #${i + 1} stays in Car Test A`, titles(A).includes(t), t); }
  key('KEYCODE_BACK'); await sleep(800); key('KEYCODE_BACK'); await sleep(800);
  check('the phone lists "Car Test B"', await open('Car Test B'));
  now = await press('Shuffle');
  check('Shuffle on Car Test B plays one of its songs (the queue was switched)', titles(B).includes(now), now);
  const heard = [now];
  for (let i = 0; i < 9; i++) { const t = await nextSong(); heard.push(t); check(`Next #${i + 1} stays in Car Test B`, titles(B).includes(t), t); }
  check('shuffle plays every song of Car Test B once before any again', new Set(heard).size === B.length, heard.join(', '));
  key('KEYCODE_BACK'); await sleep(800); key('KEYCODE_BACK'); await sleep(800);
  await open('Car Test A');
  now = await press('Shuffle');
  check('Shuffle on Car Test A again switches back to it', titles(A).includes(now), now);
  key('KEYCODE_BACK'); await sleep(800); key('KEYCODE_BACK'); await sleep(800);
  const movedOpen = await open('Car Test Moved');
  shot(path.join(here, 'pl-shot-moved.png'));
  now = movedOpen ? await press('Play') : null;
  check('a playlist whose songs moved still plays them (found by title)', titles(S).includes(now), now ?? 'no Play button: none of its songs found');
  sh('input keyevent KEYCODE_MEDIA_PAUSE', { quiet: true });

  // ---- the car ----
  adb(['install', '-r', '-t', TESTAPK], { timeout: 180000 });
  const out = String(adb(['shell', `am instrument -w -e class com.ipodemu.playback.CarPlaylistTest ${PKG}.test/androidx.test.runner.AndroidJUnitRunner`], { timeout: 600000 }) ?? '');
  fs.writeFileSync(path.join(here, 'pl-car.txt'), out);
  const ok = /OK \((\d+) tests?\)/.exec(out);
  check('the car (CarPlaylistTest)', !!ok, ok ? ok[0] : out.split('\n').filter((l) => /^\d+\)|AssertionError|Exception|FAILURES|Tests run/.test(l)).slice(0, 12).join(' | '));

  const lc = String(sh('logcat -d', { quiet: true }) ?? ''); fs.writeFileSync(path.join(here, 'pl-logcat.txt'), lc);
  const failed = checks.filter((c) => !c.pass).length;
  console.log(`playlists: ${checks.length - failed} passed, ${failed} failed`);
  fs.writeFileSync(path.join(here, 'result-playlists.json'), JSON.stringify(checks, null, 2));
  stopAll();
  process.exit(failed ? 1 : 0);
}

main().catch((e) => { console.error(e); try { shot(path.join(here, 'pl-shot-failed.png')); } catch { } stopAll(); process.exit(1); });
