// Android Auto's side of FLACie on the emulator (owner, 26 Nov: "do a full Android Auto maximum improvement"), without a car: the app's own BrowseProbe
// connects to the media library the way the car does and logs Home's sections, each tab's speed (the mock's 10,000 songs), the grouped search, the queue
// the car would show and the custom buttons with their states (before and after a tap on shuffle). It runs twice: the second time Home starts with Continue.
//   run-android.ps1 -Apk <apk> -Driver car-android.mjs [-Avd flacie_car]
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { requireMock } from './require-mock.mjs';
import { requireEmulator, sh, sleep, setAdb, has, find, label, tapNode, tapAt, typeText, key, waitUi, dump, shot } from './adbui.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const PORT = Number(arg('port', 8297)) + 1, ITEMS = Number(arg('items', 10000));
setAdb(arg('adb', ''));
const PKG = 'app.flacie', MOCK = `http://127.0.0.1:${PORT}`, PHONE_BASE = `http://10.0.2.2:${PORT}`;
const kids = []; const stopAll = () => { for (const k of kids) { try { k.kill('SIGKILL'); } catch { } } };
process.on('exit', stopAll);
const log = (...a) => console.error('[car]', ...a);
const checks = [];
const check = (name, pass, detail = '') => { checks.push({ name, pass: !!pass, detail: String(detail) }); log(`${pass ? 'PASS' : 'FAIL'} ${name}${detail ? ` (${detail})` : ''}`); };
async function waitOk(url, tries = 150) { for (let i = 0; i < tries; i++) { try { if ((await fetch(url)).ok) return true; } catch { } await sleep(400); } return false; }

async function probe() {
  // a fresh start each time (the activity is single-task: a second start would not reach onCreate, where the probe is read); the second start finds the saved queue
  sh(`am force-stop ${PKG}`, { quiet: true }); await sleep(1500);
  sh('logcat -c', { quiet: true });
  sh(`am start -n ${PKG}/com.ipodemu.ui.MainActivity --ez browseprobe true`, { quiet: true });
  for (let i = 0; i < 60; i++) { await sleep(2000); const out = sh('logcat -d -s FLACieBrowse', { quiet: true }) ?? ''; if (/probe (done|failed)/.test(out)) return out; }
  return sh('logcat -d -s FLACieBrowse', { quiet: true }) ?? '';
}
const line = (out, re) => (out.split('\n').find((l) => re.test(l)) ?? '').replace(/^.*?FLACieBrowse:\s*/, '').trim();

async function main() {
  kids.push(spawn('node', [path.join(here, 'mock.mjs'), '--port', String(PORT), '--host', '0.0.0.0', '--items', String(ITEMS)], { stdio: 'ignore', windowsHide: true }));
  if (!await waitOk(`${MOCK}/System/Info/Public`)) throw new Error('mock did not start');
  await requireMock(MOCK);

  requireEmulator(); sh(`am force-stop ${PKG}`); sh(`pm clear ${PKG}`);
  sh(`pm grant ${PKG} android.permission.POST_NOTIFICATIONS`, { quiet: true });
  sh(`am start -n ${PKG}/com.ipodemu.ui.MainActivity`, { quiet: true });
  let ui = await waitUi((n) => has(n, 'Sign in with Jellyfin'), 120000, 2000);
  tapNode(find(ui, 'Sign in with Jellyfin'));
  ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 30000, 1000);
  const field = ui?.find((n) => /EditText/.test(n.cls));
  // typed again until the box holds the whole address (the emulator sometimes drops keys), as smoke-android does
  for (let i = 0; i < 4; i++) {
    tapAt(field.cx, field.cy); await sleep(600); key('KEYCODE_MOVE_END'); key('--longpress KEYCODE_DEL'); await sleep(300);
    for (let k = 0; k < 3; k++) key('KEYCODE_DEL ' + 'KEYCODE_DEL '.repeat(12));
    typeText(PHONE_BASE); await sleep(1000);
    if ((dump() || []).some((n) => /EditText/.test(n.cls) && n.text === PHONE_BASE)) break;
  }
  key('KEYCODE_BACK'); await sleep(800);
  ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 20000, 1000);
  tapNode(find(ui, 'Sign in with Quick Connect'));
  const gone = await waitUi((n) => !has(n, 'Sign in with Quick Connect') && !has(n, 'Jellyfin server') && !has(n, 'Signing in'), 60000, 1500);
  check('Quick Connect sign-in completes', !!gone);
  shot(path.join(here, 'car-shot-signed-in.png'));

  // the 10,000 songs come in: the probe is run until the car's Albums tab has some
  let first = '';
  for (let i = 0; i < 3; i++) {
    // (a probe restarts the app, which stops a sync under way: long waits)
    await sleep(i === 0 ? 120000 : 90000);
    first = await probe();
    if (/timing albums: [1-9]/.test(first)) break;
    log(`library not in yet (${line(first, /timing albums:/)})`);
  }
  fs.writeFileSync(path.join(here, 'car-probe-1.txt'), first);
  check('the probe ran', /probe done/.test(first), line(first, /probe (done|failed)/));
  const sections = line(first, /home sections:/);
  check('Home has Quick picks, Your playlists and Your music in that order', /Quick picks.*Your music/.test(sections), sections);
  for (const tab of ['home', 'playlists', 'albums', 'artists']) {
    const t = line(first, new RegExp(`timing ${tab}:`)); const ms = Number((t.match(/in (\d+) ms/) ?? [])[1] ?? 99999);
    check(`${tab} answers in under 2 s with ${ITEMS} songs`, ms < 2000, t);
  }
  check('albums come with covers', /timing albums: \d+ items in \d+ ms; covers=[1-9]/.test(first), line(first, /timing albums:/));
  const search = line(first, /search 'song':/);
  check('search results are grouped (Songs heading)', /Songs/.test(search), search);
  const queue = line(first, /queue:/);
  check('the queue is headed Up next and has songs', /'Up next' [1-9]\d* items/.test(queue), queue);
  const before = line(first, /^.*buttons: /), after = line(first, /buttons after shuffle:/);
  check('every button says its state', /Shuffle: (on|off)/.test(before) && /Repeat: (off|all|one)/.test(before) && /Favourite: (on|off)/.test(before), before);
  check('a tap on shuffle changes its name', !!after && after.replace(/^buttons after shuffle: /, '') !== before.replace(/^buttons: /, '') && /Shuffle: (on|off)/.test(after), after);
  check('tapping a queue item plays it', /after tapping queue item 2: '.+'/.test(first), line(first, /after tapping queue item/));

  // the second time, the queue of last time is there to carry on with
  const second = await probe();
  fs.writeFileSync(path.join(here, 'car-probe-2.txt'), second);
  check('Home starts with Continue once something has played', /home sections: Continue/.test(second) && /home first: .* id=resume/.test(second), line(second, /home first:/));

  const failed = checks.filter((c) => !c.pass).length;
  console.log(`car: ${checks.length - failed} passed, ${failed} failed`);
  fs.writeFileSync(path.join(here, 'result-car.json'), JSON.stringify(checks, null, 2));
  stopAll(); process.exit(failed ? 1 : 0);
}

main().catch((e) => { console.error(e); try { shot(path.join(here, 'car-shot-failed.png')); } catch { } stopAll(); process.exit(1); });
