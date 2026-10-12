// Load measurement of the FLACie Android app on an emulator against the 10,000-song mock Jellyfin (the Android twin of run-web.mjs).
// Normally started by run-android.ps1, which boots the emulator and installs the APK. Standalone (emulator already up, app installed):
//
//   node run-android.mjs --label before|after --profile fast|slow|flaky|ratelimit [--window 120] [--port 8296] [--items 10000]
//                        [--out result.json] [--scenarios first-sync,scrolling,skip-burst,warm-start,kill-restore] [--adb <adb.exe>] [--shots <dir>] [--apk <file>]
//
// One run = one fresh mock (own process) + `pm clear` + the scenarios in order:
//   first-sync    cold start, first-launch screens, Jellyfin Quick Connect (the mock approves it: no password), Explore > Songs until all songs show; then idle until
//                 --window seconds after sign-in, counting what Jellyfin saw
//   scrolling     fling through the Songs list for ~40 s with `input swipe` in one on-device loop (cover requests counted)
//   skip-burst    play a song, 40 x KEYCODE_MEDIA_NEXT 60-100 ms apart in one on-device loop; streams opened / aborted
//   warm-start    am force-stop, launch again (library on disk): time to songs, requests in the first 60 s
//   kill-restore  play a song, kill the app, relaunch: does the queue come back, is the crash buffer clean
//   covers        (run after first-sync) cold starts with an EMPTY cover cache (`am start --ez clearcovers true`), --cover-runs times: how many cover slots on screen have their
//                 picture after 0.25 / 0.5 / 1 / 2 / 3 / 5 / 10 s (the app's own FLACieProbe lines), when the first frame drew Home, how many image requests the mock saw
// Result JSON has the same shape as run-web.mjs (scenarios -> first-sync / scrolling / skip-burst / warm-start), shell "android", plus kill-restore and gate lines.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { adb, sh, sleep, setAdb, anrCount, dump, has, find, label, tapNode, tapAt, typeText, key, shot, waitUi, mediaSession } from './adbui.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const LABEL = arg('label', 'run'), PROFILE = arg('profile', 'fast');
const WINDOW = Number(arg('window', 120)), ITEMS = Number(arg('items', 10000)), PORT = Number(arg('port', 8296));
const OUT = arg('out', path.join(here, `result-android-${LABEL}-${PROFILE}.json`));
const SCEN = arg('scenarios', 'first-sync,scrolling,skip-burst,warm-start,kill-restore').split(',');
const SHOTS = arg('shots', `C:\\wt\\android-shots\\${LABEL}-${PROFILE}`), APK = arg('apk', '');
const SKIP_IN_APP = process.argv.includes('--skip-in-app');
const SKIP_PARALLEL = process.argv.includes('--skip-parallel');
const SKIP_SLEEP = arg('skip-sleep', '0.01'), COVER_RUNS = Number(arg('cover-runs', 3));
const SCROLL_SECONDS = Number(arg('scroll-seconds', 40)), SKIPS = Number(arg('skips', 40));
setAdb(arg('adb', ''));
const PKG = 'com.ipodemu';
const MOCK = `http://127.0.0.1:${PORT}`, DEVICE_MOCK = `http://10.0.2.2:${PORT}`;

const jget = async (u) => (await fetch(u)).json();
const stats = () => jget(`${MOCK}/__stats`);
const reset = () => fetch(`${MOCK}/__reset`);
process.on('uncaughtException', (e) => { console.error('UNCAUGHT', e?.stack ?? e); });
process.on('unhandledRejection', (e) => { console.error('UNHANDLED', e?.stack ?? e); });
const log = (...a) => console.error(`[${LABEL}/android/${PROFILE}]`, ...a);
const pick = (s) => ({ total: s.total, byKind: s.byKind, peakInFlight: s.peakInFlight, peakPerSecond: s.peakPerSecond, throttled: s.throttled, failed: s.failed,
  images: s.byKind.image ?? 0, imageUrls: s.imageUrls, imageRepeats: s.imageRepeats, imageVariants: s.imageVariants, notModified: s.notModified,
  streamsOpened: s.streamsOpened, streamsAborted: s.streamsAborted, streamsCompleted: s.streamsCompleted, itemsPages: s.itemsPages, itemsDeltas: s.itemsDeltas,
  itemsWithStreams: s.itemsWithStreams, avgLatencyMs: s.avgLatencyMs, maxLatencyMs: s.maxLatencyMs, seconds: Math.round(s.seconds) });

// ---- the mock ----
let mock = null;
async function startMock() {
  if (process.env.MOCK_EXTERNAL) { await fetch(`${MOCK}/__profile?name=${PROFILE}`); await reset(); return; }   // a mock started by hand (e.g. with --wavsec 180)
  mock = spawn('node', [path.join(here, 'mock.mjs'), '--port', String(PORT), '--host', '127.0.0.1', '--items', String(ITEMS), '--streamms', '1500'], { stdio: 'ignore', windowsHide: true });
  for (let i = 0; i < 100; i++) { try { if ((await fetch(`${MOCK}/System/Info/Public`)).ok) break; } catch { } await sleep(200); }
  await fetch(`${MOCK}/__profile?name=${PROFILE}`);
  await reset();
}
function stopMock() { if (mock) { try { mock.kill('SIGKILL'); } catch { } try { spawn('taskkill', ['/F', '/T', '/PID', String(mock.pid)], { stdio: 'ignore' }); } catch { } mock = null; } }
process.on('exit', stopMock);

// ---- device helpers ----
const launch = () => sh(`am start -n ${PKG}/.ui.MainActivity`, { quiet: true });
const forceStop = () => sh(`am force-stop ${PKG}`);
function freshApp() {
  forceStop();
  sh(`pm clear ${PKG}`);
  // no notification prompt during the run; no music-file access, so the emulator's own sdcard files never mix into the 10,000 songs
  sh(`pm grant ${PKG} android.permission.POST_NOTIFICATIONS`, { quiet: true });
  sh(`pm revoke ${PKG} android.permission.READ_MEDIA_AUDIO`, { quiet: true });
  sh(`pm revoke ${PKG} android.permission.READ_EXTERNAL_STORAGE`, { quiet: true });
}
const songsCount = (nodes) => { if (!nodes) return -1; for (const n of nodes) { const m = /^([\d,]+) songs?$/.exec(label(n).trim()); if (m) return Number(m[1].replace(/,/g, '')); } return -1; };
const navY = () => 2000;   // the bottom navigation of the phone layout lives below this line (screen is 2400 px tall)
function gotoExploreSongs(nodes) {
  // bottom bar "Explore" (the Home screen also has an Explore shelf title, higher up), then the Songs pill
  const ex = find(nodes, 'Explore', { minY: navY() }) ?? find(nodes, 'Explore', { last: true });
  if (ex) tapNode(ex);
}
const pss = () => { const m = /TOTAL PSS:\s+(\d+)|TOTAL\s+(\d+)/.exec(sh(`dumpsys meminfo ${PKG}`, { quiet: true })); return m ? Math.round(Number(m[1] ?? m[2]) / 1024) : null; };
const gateLines = () => sh(`logcat -d -s FLACieGate`, { quiet: true }).split('\n').filter((l) => /FLACieGate/.test(l));
const crashLog = () => sh('logcat -b crash -d', { quiet: true }).split('\n').filter((l) => l.trim() && !/^-+ beginning/.test(l));

async function signInQuickConnect(res) {
  let ui = await waitUi((n) => has(n, 'Sign in with Jellyfin'), 120000, 2000);
  if (!ui) { shot(path.join(SHOTS, 'no-login.png')); throw new Error('first-launch screen "Sign in with Jellyfin" never showed'); }
  res.tFirstScreenShown = Date.now();
  tapNode(find(ui, 'Sign in with Jellyfin'));
  ui = await waitUi((n) => has(n, 'Sign in with Quick Connect') || has(n, 'Jellyfin server'), 30000, 1000);
  if (!ui) throw new Error('Jellyfin sign-in form never showed');
  const field = ui.find((n) => /EditText/.test(n.cls)) ?? find(ui, 'Jellyfin server');
  tapAt(field.cx, field.cy + (/EditText/.test(field.cls) ? 0 : 70));
  await sleep(600);
  typeText(DEVICE_MOCK);
  await sleep(600);
  key('KEYCODE_BACK');   // hides the soft keyboard (an app-level Back only goes up a page when no keyboard is showing)
  await sleep(800);
  for (let attempt = 1; attempt <= 3; attempt++) {
    ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 20000, 1000);
    if (!ui) { if (attempt > 1) break; continue; }
    tapNode(find(ui, 'Sign in with Quick Connect'));
    res.signInAttempts = attempt;
    const t = Date.now();
    // signed in = the first-launch form is gone
    const gone = await waitUi((n) => !has(n, 'Sign in with Quick Connect') && !has(n, 'Jellyfin server') && !has(n, 'Signing in'), 45000, 1500);
    if (gone) return t;
  }
  shot(path.join(SHOTS, 'signin-failed.png'));
  throw new Error('Quick Connect sign-in did not complete');
}

const result = { label: LABEL, shell: 'android', profile: PROFILE, items: ITEMS, window: WINDOW, startedAt: new Date().toISOString(), apk: APK ? path.basename(APK) : null, scenarios: {}, notes: [] };

async function scenarioFirstSync() {
  const fs1 = { timeToFirstSongsMs: null, timeToAllSongsMs: null, timeline: [], signInAttempts: 0 };
  result.scenarios['first-sync'] = fs1;
  freshApp();
  sh('logcat -c', { quiet: true });
  await reset();
  // the mock's own clock, sampled twice a second (independent of how long each uiautomator dump takes)
  const samples = [];
  const sampler = setInterval(async () => { try { const x = await stats(); samples.push({ at: Date.now(), total: x.total, pages: x.itemsPages, images: x.byKind.image ?? 0 }); } catch { } }, 500);
  launch();
  const t0 = await signInQuickConnect(fs1);   // t = the moment of the Quick Connect tap
  const tCold = Date.now();
  fs1.signInMs = tCold - t0;
  let navTapAt = 0, seenFirst = false, seenAll = false;
  for (let s = 0; (Date.now() - t0) / 1000 < WINDOW + 5; s++) {
    const ui = dump();
    const n = songsCount(ui), st = await stats(), el = Date.now() - t0;
    if (n < 0 && ui && Date.now() - navTapAt > 6000) { gotoExploreSongs(ui); navTapAt = Date.now(); }
    if (n > 0 && !seenFirst) { seenFirst = true; fs1.timeToFirstSongsMs = el; fs1.firstSongsCount = n; shot(path.join(SHOTS, 'first-sync-first-songs.png')); }
    if (n >= ITEMS && !seenAll) { seenAll = true; fs1.timeToAllSongsMs = el; fs1.atAll = pick(st); }
    fs1.songsShown = Math.max(fs1.songsShown ?? 0, n);
    fs1.timeline.push({ t: Math.round(el / 1000), requests: st.total, inFlight: st.inFlight, songs: n });
    if (el / 1000 >= WINDOW) break;
    await sleep(1500);
  }
  clearInterval(sampler);
  const firstPage = samples.find((x) => x.pages > 0), lastPage = [...samples].reverse().find((x, i, a) => i === a.length - 1 || x.pages !== a[i + 1].pages);
  const at = (sec) => { const x = samples.filter((q) => q.at - t0 <= sec * 1000).at(-1); return x ? x.total : null; };
  fs1.server = { firstItemsPageMs: firstPage ? firstPage.at - t0 : null, lastItemsPageMs: lastPage && lastPage.pages > 0 ? lastPage.at - t0 : null, itemsPagesTotal: samples.at(-1)?.pages ?? null,
    requestsAfterSec: { 5: at(5), 10: at(10), 30: at(30), 60: at(60), 120: at(120) } };
  fs1.final = pick(await stats());
  fs1.pssMb = pss();
  shot(path.join(SHOTS, 'first-sync-end.png'));
  log(`first-sync: songs ${fs1.songsShown}, first ${fs1.timeToFirstSongsMs} ms, all ${fs1.timeToAllSongsMs} ms, requests ${fs1.final.total} (${JSON.stringify(fs1.final.byKind)}), peak ${fs1.final.peakInFlight}`);
}

async function ensureSongsList() {
  const ui = dump();
  if (songsCount(ui) > 0) return ui;
  if (ui) gotoExploreSongs(ui);
  return waitUi((n) => songsCount(n) > 0, 60000, 2000);
}

async function scenarioScrolling() {
  const ui = await ensureSongsList();
  if (!ui) throw new Error('no Songs list to scroll');
  await sleep(2000);
  await reset();
  sh('logcat -c', { quiet: true });
  const t0 = Date.now();
  const out = sh(`end=$(( $(date +%s) + ${SCROLL_SECONDS} )); n=0; while [ $(date +%s) -lt $end ]; do cmd input swipe 500 1900 500 600 100; n=$((n+1)); done; echo swipes=$n`, { timeout: (SCROLL_SECONDS + 90) * 1000 });
  const ms = Date.now() - t0;
  const swipes = Number(/swipes=(\d+)/.exec(out)?.[1] ?? 0);
  await sleep(3000);   // covers still on their way
  const st = pick(await stats());
  result.scenarios.scrolling = { swipes, ms, swipesPerSecond: +(swipes / (ms / 1000)).toFixed(1), ...st };
  shot(path.join(SHOTS, 'scrolling-end.png'));
  log(`scrolling: ${swipes} swipes in ${ms} ms, image requests ${st.images} (${st.imageUrls} distinct, ${st.imageRepeats} repeats), peak ${st.peakInFlight}, total ${st.total}`);
}

async function startSong() {
  // tap the first song row on screen (any row plays the list from there)
  for (let i = 0; i < 4; i++) {
    const ui = (await ensureSongsList()) ?? dump();
    const row = ui && ui.filter((n) => /^Song \d+$/.test(label(n)) && n.cy > 700 && n.cy < navY())[0];
    if (row) { tapAt(500, row.cy); break; }   // the title sits inside a full-width row; tap the row, clear of its "more" button
    await sleep(1500);
  }
  const t = Date.now();
  for (let i = 0; i < 90; i++) {
    const m = mediaSession();
    if (m.state === 3) return { ok: true, ms: Date.now() - t, media: m };
    await sleep(500);
  }
  return { ok: false, ms: Date.now() - t, media: mediaSession() };
}

// the queue is the Songs list in Title A-Z order (the mock names its songs 'Song 1'..'Song N'), so a title tells where in the queue the player is
let azIndex = null;
const queuePos = (desc) => { const m = /^(Song \d+)/.exec(desc ?? ''); if (!m) return null; azIndex ??= new Map(Array.from({ length: ITEMS }, (_, i) => 'Song ' + (i + 1)).sort().map((t, i) => [t, i])); return azIndex.get(m[1]) ?? null; };

async function scenarioSkipBurst() {
  const sp = await startSong();
  if (!sp.ok) { shot(path.join(SHOTS, 'skip-no-play.png')); throw new Error('playback never started: ' + JSON.stringify(sp.media)); }
  await sleep(1500);
  // The media keys go to the app through the media session, like headset / lock-screen / notification buttons. With the app in the background the software-rendered
  // emulator does not have to draw a 10,000-row list on every skip, so the number measured is the player and the requests, not the emulator's frame rate.
  if (!SKIP_IN_APP) { key('KEYCODE_HOME'); await sleep(2000); }
  const before = mediaSession();
  await reset();
  sh('logcat -c', { quiet: true });
  const tb = Date.now();
  // each key is issued in the background (cmd input is ~50 ms to start, and waits for the app to take the event): the keys are SKIP_SLEEP + ~50 ms apart however slow the app is
  const out = sh(`up() { cut -d" " -f1 /proc/uptime; }; i=0; u0=$(up); while [ $i -lt ${SKIPS} ]; do cmd input keyevent 87 ${SKIP_PARALLEL ? "&" : ""}; sleep ${SKIP_SLEEP}; i=$((i+1)); done; u1=$(up); wait; u2=$(up); echo done=$i up=$u0,$u1,$u2`, { timeout: 180000 });
  const burstMs = Date.now() - tb;
  const up = /up=([0-9.]+),([0-9.]+),([0-9.]+)/.exec(out);
  const keyIssueMs = up ? Math.round((Number(up[2]) - Number(up[1])) * 1000) : null, keyAllMs = up ? Math.round((Number(up[3]) - Number(up[1])) * 1000) : null;
  // ready = PLAYING again on the song the 40 skips lead to (the player may jump there once, a moment after the last key); a build that lands elsewhere is
  // caught by the fallback: PLAYING with the same title for about 2 s
  const startPos = queuePos(before.description), target = startPos === null ? null : startPos + SKIPS;
  let playingMs = null, readyBy = null, last = null, stable = 0, media = null;
  for (let i = 0; i < 160 && playingMs === null; i++) {
    media = mediaSession();
    const pos = queuePos(media.description);
    if (media.state === 3 && target !== null && pos !== null && pos >= target) { playingMs = Date.now() - tb; readyBy = 'target'; break; }
    await sleep(250);
  }
  if (playingMs === null) {
    for (let i = 0; i < 80; i++) {
      media = mediaSession();
      if (media.state === 3) { if (media.description === last) stable++; else { stable = 0; last = media.description; } if (stable >= 8) { playingMs = Date.now() - tb; readyBy = 'stable-title'; break; } } else stable = 0;
      await sleep(250);
    }
  }
  await sleep(4000);
  const st = pick(await stats());
  if (!SKIP_IN_APP) { launch(); await sleep(3000); }
  result.scenarios['skip-burst'] = { skips: SKIPS, keysSent: Number(/done=(\d+)/.exec(out)?.[1] ?? 0), burstMs, keySpacingMs: keyIssueMs ? Math.round(keyIssueMs / SKIPS) : null, keysIssuedInMs: keyIssueMs, allKeysTakenInMs: keyAllMs, lastSongReadyMs: playingMs, readyBy, startedPlayingMs: sp.ms, appInBackground: !SKIP_IN_APP, queueStep: queuePos(media?.description) !== null && queuePos(before.description) !== null ? queuePos(media.description) - queuePos(before.description) : null, titleBefore: before.description, titleAfter: media?.description, ...st };
  shot(path.join(SHOTS, 'skip-burst-end.png'));
  log(`skip-burst: ${before.description} -> ${media?.description}, burst ${burstMs} ms, ready after ${playingMs} ms, requests ${st.total} (streams ${st.streamsOpened}, aborted ${st.streamsAborted}), peak ${st.peakInFlight}`);
}

async function scenarioWarmStart() {
  forceStop();
  await sleep(2000);
  await reset();
  sh('logcat -c', { quiet: true });
  const w0 = Date.now();
  launch();
  const warm = { signedIn: true, timeToFirstSongsMs: null, timeToAllSongsMs: null };
  let navTapAt = 0;
  while (Date.now() - w0 < 60000) {
    const ui = dump();
    const n = songsCount(ui), el = Date.now() - w0;
    if (n < 0 && ui && Date.now() - navTapAt > 5000) { if (has(ui, 'Sign in with Jellyfin')) { warm.signedIn = false; break; } gotoExploreSongs(ui); navTapAt = Date.now(); }
    if (n > 0 && warm.timeToFirstSongsMs === null) { warm.timeToFirstSongsMs = el; warm.firstSongsCount = n; shot(path.join(SHOTS, 'warm-first-songs.png')); }
    if (n >= ITEMS && warm.timeToAllSongsMs === null) warm.timeToAllSongsMs = el;
    warm.songsShown = Math.max(warm.songsShown ?? 0, n);
    await sleep(1500);
  }
  while (Date.now() - w0 < 60000) await sleep(500);   // 60 s from the launch
  Object.assign(warm, pick(await stats()));
  warm.pssMb = pss();
  result.scenarios['warm-start'] = warm;
  log(`warm-start: first songs ${warm.timeToFirstSongsMs} ms, all ${warm.timeToAllSongsMs} ms, requests in the first 60 s ${warm.total}`);
}

async function scenarioKillRestore() {
  const sp = await startSong();
  if (!sp.ok) throw new Error('playback never started: ' + JSON.stringify(sp.media));
  await sleep(2500);
  const before = mediaSession();
  sh('logcat -c', { quiet: true });
  sh(`am force-stop ${PKG}`);
  await sleep(2500);
  const afterKill = mediaSession();
  launch();
  // restored = the app is back with a queue: its media session names a song again (songs are 3 s long and the queue keeps advancing, so it need not be
  // the very song that was playing when the process was killed; sameSong / queueDelta say how far apart they are)
  let restored = false, media = null;
  for (let i = 0; i < 40 && !restored; i++) {
    await sleep(1500);
    media = mediaSession();
    if (media.found && /^Song \d+/.test(media.description ?? '')) restored = true;
  }
  const pb = queuePos(before.description), pa = queuePos(media?.description);
  const crash = crashLog();
  shot(path.join(SHOTS, 'kill-restore-end.png'));
  result.scenarios['kill-restore'] = { playedBefore: before.description, sessionAfterKill: afterKill.found, sessionAfterRelaunch: media, queueRestored: restored, sameSong: !!media?.description && media.description === before.description, queueDelta: pb !== null && pa !== null ? pa - pb : null, crashBufferLines: crash.length, crashBuffer: crash.slice(0, 20) };
  log(`kill-restore: restored ${restored}, crash buffer lines ${crash.length}`);
}

// the app logs "<ms since process start> ms  t=<n>s visible=<slots on screen> withCover=<slots with their picture> pct=..." (FLACieProbe) and "... home: shelves drawn" (FLACieBoot)
async function scenarioCovers() {
  const out = { runs: [] };
  result.scenarios.covers = out;
  for (let r = 1; r <= COVER_RUNS; r++) {
    forceStop(); await sleep(2000);
    sh('logcat -c', { quiet: true });
    await reset();
    sh(`am start -W -n ${PKG}/.ui.MainActivity --ez clearcovers true`, { quiet: true });
    await sleep(16000);
    const lines = sh('logcat -d -s FLACieProbe FLACieBoot FLACieCover', { quiet: true }).split('\n');
    const at = {};
    for (const l of lines) { const m = /t=(\d+(?:ms|s)) visible=(\d+) withCover=(\d+) pct=(\d+)/.exec(l); if (m) at[m[1]] = { visible: Number(m[2]), withCover: Number(m[3]), pct: Number(m[4]) }; }
    const shelves = lines.map((l) => /(\d+) ms\s+home: shelves drawn/.exec(l)).find(Boolean);
    const st = await stats();
    const gone = lines.filter((l) => /FLACieCover.*(failed|http \d+)/.test(l)).length;
    out.runs.push({ run: r, shelvesDrawnMs: shelves ? Number(shelves[1]) : null, coverAt: at, coverFailuresLogged: gone, imageRequests: st.byKind.image ?? 0, failed: st.failed, throttled: st.throttled });
    shot(path.join(SHOTS, `covers-run${r}.png`));
    log(`covers run ${r}: ${JSON.stringify(at['1s'])} @1s, ${JSON.stringify(at['3s'])} @3s, ${JSON.stringify(at['10s'])} @10s; image requests ${st.byKind.image ?? 0}`);
  }
  const pct = (k) => out.runs.map((x) => x.coverAt[k]?.pct).filter((v) => v !== undefined);
  out.summary = { pctAt1s: pct('1s'), pctAt3s: pct('3s'), pctAt10s: pct('10s') };
}

async function main() {
  fs.mkdirSync(SHOTS, { recursive: true });
  try { result.versionName = /versionName=(\S+)/.exec(sh(`dumpsys package ${PKG}`, { quiet: true }))?.[1]; } catch { }
  sh('logcat -G 16M', { quiet: true });   // the gate logs a lot: keep a whole scenario in the buffer
  await startMock();
  const table = { 'first-sync': scenarioFirstSync, scrolling: scenarioScrolling, 'skip-burst': scenarioSkipBurst, 'warm-start': scenarioWarmStart, 'kill-restore': scenarioKillRestore, covers: scenarioCovers };
  for (const name of SCEN) {
    if (!table[name]) continue;
    try { await table[name](); }
    catch (e) { result.scenarios[name] = { ...(result.scenarios[name] ?? {}), error: String(e?.message ?? e) }; log(`${name} FAILED: ${e?.message ?? e}`); shot(path.join(SHOTS, `${name}-failed.png`)); }
    // the crash buffer is read before the next scenario clears logcat; each entry names the process that died
    const cr = crashLog();
    result.crash = { ...(result.crash ?? {}), [name]: { lines: cr.length, processes: [...new Set(cr.map((l) => /Process: ([\w.:]+)/.exec(l)?.[1]).filter(Boolean))], head: cr.slice(0, 8) } };
    const gl = gateLines();
    try { fs.writeFileSync(path.join(SHOTS, 'gate-' + name + '.txt'), gl.join('\n')); } catch { }
    result.gate = { ...(result.gate ?? {}), [name]: { lines: gl.length, first: gl.slice(0, 15), last: gl.slice(-25) } };
    fs.writeFileSync(OUT, JSON.stringify(result, null, 1));
  }
  result.crashBufferAtEnd = crashLog().slice(0, 30);
  result.anrDialogsDismissed = anrCount;
  result.finishedAt = new Date().toISOString();
}

try { await main(); }
catch (e) { result.error = String(e?.stack ?? e); console.error('FAILED', e); }
finally {
  fs.mkdirSync(path.dirname(OUT), { recursive: true });
  fs.writeFileSync(OUT, JSON.stringify(result, null, 1));
  console.error('wrote ' + OUT);
  stopMock();
  process.exit(result.error ? 1 : 0);
}
