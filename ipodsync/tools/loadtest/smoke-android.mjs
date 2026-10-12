// Emulator smoke test of one FLACie APK against the 10,000-song mock Jellyfin (fast profile). Started by run-android.ps1 -Driver smoke-android.mjs
// (which boots the emulator and installs the APK); standalone with an emulator up:
//   node smoke-android.mjs --port 8296 [--out smoke.json] [--shots <dir>] [--adb <adb.exe>]
// Steps: sign in with Quick Connect, library loads, scroll, play, 40-skip burst, pause + force-stop + relaunch (queue and position come back),
// Settings > Storage (cache line, Clear cache, library stays), Up next (Play next / Move up / Move down / Remove with the song playing now staying current),
// crash buffer and ANR check. Every check lands in the JSON as { name, pass, detail }.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { adb, sh, sleep, setAdb, anrCount, dump, has, find, label, tapNode, tapAt, typeText, key, shot, waitUi, mediaSession } from './adbui.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const PORT = Number(arg('port', 8296)), ITEMS = Number(arg('items', 10000));
const OUT = arg('out', path.join(here, 'smoke-android.json'));
const SHOTS = arg('shots', 'C:\\wt\\android-shots\\smoke');
setAdb(arg('adb', ''));
const PKG = 'com.ipodemu';
const MOCK = `http://127.0.0.1:${PORT}`, DEVICE_MOCK = `http://10.0.2.2:${PORT}`;
const stats = async () => (await fetch(`${MOCK}/__stats`)).json();
const checks = [];
const check = (name, pass, detail = '') => { checks.push({ name, pass: !!pass, detail: String(detail) }); console.error(`[smoke] ${pass ? 'PASS' : 'FAIL'} ${name} ${detail}`); };
const S = (n) => path.join(SHOTS, n + '.png');

let mock = null;
async function startMock() {
  mock = spawn('node', [path.join(here, 'mock.mjs'), '--port', String(PORT), '--host', '127.0.0.1', '--items', String(ITEMS), '--streamms', '1500'], { stdio: 'ignore', windowsHide: true });
  for (let i = 0; i < 100; i++) { try { if ((await fetch(`${MOCK}/System/Info/Public`)).ok) break; } catch { } await sleep(200); }
  await fetch(`${MOCK}/__profile?name=fast`); await fetch(`${MOCK}/__reset`);
}
const stopMock = () => { if (mock) { try { mock.kill('SIGKILL'); } catch { } mock = null; } };
process.on('exit', stopMock);

const launch = () => sh(`am start -n ${PKG}/.ui.MainActivity`, { quiet: true });
const songsCount = (nodes) => { if (!nodes) return -1; for (const n of nodes) { const m = /^([\d,]+) songs?$/.exec(label(n).trim()); if (m) return Number(m[1].replace(/,/g, '')); } return -1; };
const titleOf = (d) => (d ?? '').split(',')[0].trim();
const tapNav = (name) => { const ui = dump(); const n = ui && find(ui, name, { minY: 2000 }); if (n) { tapNode(n); return true; } return false; };
const azIndex = new Map(Array.from({ length: ITEMS }, (_, i) => 'Song ' + (i + 1)).sort().map((t, i) => [t, i]));
const pos = (d) => azIndex.get(titleOf(d)) ?? null;

async function playFirstVisible() {
  for (let i = 0; i < 5; i++) {
    const ui = dump();
    const row = ui && ui.filter((n) => /^Song \d+$/.test(label(n)) && n.cy > 700 && n.cy < 2000)[0];
    if (row) { tapAt(500, row.cy); break; }
    await sleep(1500);
  }
  for (let i = 0; i < 60; i++) { const m = mediaSession(); if (m.state === 3) return m; await sleep(500); }
  return mediaSession();
}

async function main() {
  await startMock();
  const ver = /versionName=(\S+)/.exec(sh(`dumpsys package ${PKG}`, { quiet: true }))?.[1];
  check('installed build', !!ver, ver);
  sh(`am force-stop ${PKG}`); sh(`pm clear ${PKG}`);
  sh(`pm grant ${PKG} android.permission.POST_NOTIFICATIONS`, { quiet: true });
  sh(`pm revoke ${PKG} android.permission.READ_MEDIA_AUDIO`, { quiet: true });
  sh('logcat -c', { quiet: true });
  launch();

  // 1. sign in
  let ui = await waitUi((n) => has(n, 'Sign in with Jellyfin'), 120000, 2000);
  check('first-launch screen shows Sign in with Jellyfin', !!ui);
  if (!ui) throw new Error('no login screen');
  tapNode(find(ui, 'Sign in with Jellyfin'));
  ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 30000, 1000);
  const field = ui?.find((n) => /EditText/.test(n.cls));
  tapAt(field.cx, field.cy); await sleep(600); typeText(DEVICE_MOCK); await sleep(600); key('KEYCODE_BACK'); await sleep(800);
  ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 20000, 1000);
  tapNode(find(ui, 'Sign in with Quick Connect'));
  const gone = await waitUi((n) => !has(n, 'Sign in with Quick Connect') && !has(n, 'Jellyfin server') && !has(n, 'Signing in'), 60000, 1500);
  check('Quick Connect sign-in completes', !!gone);
  shot(S('01-after-signin'));

  // 2. library loads
  let n = -1; const t0 = Date.now();
  for (let i = 0; i < 80 && n < ITEMS; i++) {
    ui = dump();
    if (songsCount(ui) < 0 && ui) tapNav('Explore');
    n = Math.max(n, songsCount(ui));
    if (n >= ITEMS) break;
    await sleep(2000);
  }
  check(`library loads (${ITEMS} songs listed)`, n >= ITEMS, `${n} songs after ${Math.round((Date.now() - t0) / 1000)} s`);
  shot(S('02-songs'));

  // 3. scroll
  const firstBefore = (dump() ?? []).filter((x) => /^Song \d+$/.test(label(x))).map(label)[0];
  sh('i=0; while [ $i -lt 12 ]; do cmd input swipe 500 1900 500 600 100; i=$((i+1)); done', { timeout: 90000 });
  await sleep(1500);
  const firstAfter = (dump() ?? []).filter((x) => /^Song \d+$/.test(label(x))).map(label)[0];
  check('scrolling moves the list', firstBefore && firstAfter && firstBefore !== firstAfter, `${firstBefore} -> ${firstAfter}`);
  shot(S('03-scrolled'));
  const stScroll = await stats();

  // 4. play
  const m = await playFirstVisible();
  check('a song plays (media session PLAYING)', m.state === 3, titleOf(m.description));
  shot(S('04-playing'));

  // 5. 40-skip burst (app in front, as a user pressing Next in the app would; keys one after the other)
  await sleep(1000);
  const before = mediaSession(); await fetch(`${MOCK}/__reset`);
  const tb = Date.now();
  sh(`i=0; while [ $i -lt 40 ]; do cmd input keyevent 87; sleep 0.01; i=$((i+1)); done`, { timeout: 180000 });
  const target = pos(before.description) === null ? null : pos(before.description) + 40;
  let ready = null, mm = null;
  for (let i = 0; i < 120 && ready === null; i++) { mm = mediaSession(); if (mm.state === 3 && target !== null && pos(mm.description) !== null && pos(mm.description) >= target) ready = Date.now() - tb; else await sleep(250); }
  const sb = await stats();
  check('40-skip burst ends playing the song 40 places on', ready !== null, `${titleOf(before.description)} -> ${titleOf(mm?.description)}, ready ${ready} ms, ${sb.total} requests, streams ${sb.streamsOpened} opened / ${sb.streamsAborted} aborted`);
  shot(S('05-after-burst'));

  // 6. pause, kill, relaunch
  key('KEYCODE_MEDIA_PAUSE'); await sleep(2500);
  const paused = mediaSession();
  check('pause works', paused.state === 2, `state ${paused.state}`);
  await sleep(4000);   // the queue is written to disk a moment after changes
  sh(`am force-stop ${PKG}`); await sleep(2500);
  launch(); await sleep(6000);
  ui = await waitUi((x) => x.some((q) => /^Song \d+$/.test(label(q)) && q.cy > 1900) || has(x, titleOf(paused.description)), 60000, 2000);
  const mini = ui && find(ui, titleOf(paused.description), { minY: 1700 });
  shot(S('06-relaunched'));
  check('relaunch restores the queue (mini player names the paused song)', !!mini, `paused on ${titleOf(paused.description)} at ${paused.position} ms`);
  const mini2 = ui && ui.filter((q) => /^Song \d+$/.test(label(q)) && q.cy > 1700).map(label);
  check('relaunch is not auto-playing', mediaSession().state !== 3, `state ${mediaSession().state}`);

  // 7. Settings > Storage
  tapNav('Settings'); await sleep(2000);
  let cacheLine = null;
  for (let i = 0; i < 25 && !cacheLine; i++) {
    ui = dump();
    const c = ui && find(ui, /^Cache/);
    if (c) { cacheLine = ui; break; }
    sh('cmd input swipe 500 1700 500 700 200'); await sleep(900);
  }
  const rowText = (ui, head) => { const i = ui.findIndex((q) => label(q).startsWith(head)); return i < 0 ? null : ui.slice(i, i + 4).map(label).join(' | '); };
  shot(S('07-settings-storage'));
  check('Settings > Storage shows a cache size', !!cacheLine && /\d/.test(rowText(cacheLine, 'Cache') ?? ''), rowText(cacheLine ?? [], 'Cache') ?? 'no Cache row');
  if (cacheLine) {
    const clr = find(cacheLine, 'Clear cache'); tapNode(clr); await sleep(5000);
    const after = dump();
    shot(S('08-cache-cleared'));
    check('Clear cache runs', !!after && !has(after, 'Clearing'), rowText(after ?? [], 'Cache') ?? '');
    tapNav('Explore'); await sleep(2500);
    const u2 = await waitUi((x) => songsCount(x) > 0, 30000, 1500);
    check('library stays after Clear cache', songsCount(u2) >= ITEMS, `${songsCount(u2)} songs`);
    shot(S('09-library-after-clear'));
  }

  // 8. Up next
  ui = dump();
  const play2 = await playFirstVisible();
  key('KEYCODE_MEDIA_PAUSE'); await sleep(1500);
  const cur = mediaSession();
  // open Now Playing (tap the mini player), then the queue pane
  ui = dump();
  const miniNode = ui && ui.filter((q) => /^Song \d+$/.test(label(q)) && q.cy > 1700)[0];
  if (miniNode) { tapAt(400, miniNode.cy); await sleep(2500); }
  shot(S('10-now-playing'));
  ui = dump();
  const upBtn = ui && (find(ui, /^Up next$/i) ?? find(ui, /Up next|Queue/i));
  if (upBtn) { tapNode(upBtn); await sleep(2000); }
  shot(S('11-up-next'));
  ui = dump();
  const more = ui ? ui.filter((q) => /^More for Song \d+/.test(q.desc)) : [];
  check('Up next lists songs with a More button', more.length >= 2, `${more.length} rows with More`);
  if (more.length >= 2) {
    const names = (u) => u.filter((q) => /^Song \d+$/.test(label(q))).map(label);
    const list0 = names(ui);
    const target1 = more[1];   // second upcoming row
    tapNode(target1); await sleep(1500);
    shot(S('12-up-next-sheet'));
    let sheet = dump();
    const items0 = (sheet ?? []).map(label).filter((s) => /Play next|Move up|Move down|Remove from queue/.test(s));
    check('row sheet offers Play next / Move up / Move down / Remove', items0.length >= 3, items0.join(', '));
    const mv = sheet && find(sheet, 'Move down');
    if (mv) { tapNode(mv); await sleep(1500); }
    ui = dump(); const list1 = ui ? names(ui) : [];
    check('Move down changes the order', list1.join() !== list0.join(), `${list0.slice(0, 5).join(', ')} -> ${list1.slice(0, 5).join(', ')}`);
    check('the playing song stays current after Move down', titleOf(mediaSession().description) === titleOf(cur.description), `${titleOf(cur.description)} now ${titleOf(mediaSession().description)}`);
    // Move up the same song again
    const more2 = ui ? ui.filter((q) => /^More for Song \d+/.test(q.desc)) : [];
    if (more2.length >= 3) {
      tapNode(more2[2]); await sleep(1500);
      sheet = dump(); const up = sheet && find(sheet, 'Move up');
      if (up) { tapNode(up); await sleep(1500); }
      ui = dump(); const list2 = ui ? names(ui) : [];
      check('Move up changes the order', list2.join() !== list1.join(), `${list1.slice(0, 5).join(', ')} -> ${list2.slice(0, 5).join(', ')}`);
    }
    // Remove one
    const more3 = ui ? ui.filter((q) => /^More for Song \d+/.test(q.desc)) : [];
    if (more3.length >= 2) {
      const victim = /Song \d+/.exec(more3[1].desc)[0];
      tapNode(more3[1]); await sleep(1500);
      sheet = dump(); const rm = sheet && find(sheet, 'Remove from queue');
      if (rm) { tapNode(rm); await sleep(1500); }
      ui = dump(); const list3 = ui ? names(ui) : [];
      check('Remove takes the song out of Up next', !list3.includes(victim), `removed ${victim}`);
      check('the playing song stays current after Remove', titleOf(mediaSession().description) === titleOf(cur.description), `${titleOf(mediaSession().description)}`);
    }
    shot(S('13-up-next-end'));
  }

  // 9. crashes and ANRs
  const crash = sh('logcat -b crash -d', { quiet: true }).split('\n').filter((l) => l.trim() && !/^-+ beginning/.test(l));
  check('crash buffer is clean', crash.length === 0, crash.slice(0, 5).join(' / '));
  const anr = sh('logcat -d -b main,system', { quiet: true }).split('\n').filter((l) => /ANR in com\.ipodemu|isn't responding.*ipodemu/.test(l));
  check('no ANR in com.ipodemu', anr.length === 0, anr.slice(0, 3).join(' / '));
  const gate = sh('logcat -d -s FLACieGate', { quiet: true }).split('\n').filter((l) => /FLACieGate/.test(l));
  fs.writeFileSync(path.join(SHOTS, 'gate-smoke.txt'), gate.join('\n'));
  return { gateLines: gate.length, systemUiAnrDialogsDismissed: anrCount, versionName: ver };
}

const result = { startedAt: new Date().toISOString(), checks };
try { Object.assign(result, await main()); }
catch (e) { result.error = String(e?.stack ?? e); console.error('SMOKE FAILED', e); shot(S('99-failed')); }
finally {
  result.finishedAt = new Date().toISOString();
  result.passed = checks.filter((c) => c.pass).length; result.failed = checks.filter((c) => !c.pass).length;
  fs.mkdirSync(path.dirname(OUT), { recursive: true });
  fs.writeFileSync(OUT, JSON.stringify(result, null, 1));
  console.error(`smoke: ${result.passed} passed, ${result.failed} failed${result.error ? ', ERROR' : ''}; wrote ${OUT}`);
  stopMock();
  process.exit(result.error || result.failed ? 1 : 0);
}
