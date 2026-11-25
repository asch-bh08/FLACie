// Devices on the phone, end to end on the emulator (owner, 25 Nov: "the Devices tab needs redoing ... and I want a way to pick where the phone was like
// 1 hour ago"): a mock Jellyfin whose shared profile names a local FLACie Web, the APK signed in with Quick Connect, a song played (the phone reports where it
// is to /api/where), a second device's earlier moment added, then Devices opened, pictured, and that moment carried on here.
// Started by run-android.ps1 -Driver devices-android.mjs (boots the emulator, installs the APK; its own mock takes --port, so this one uses the next port).
// Needs FLACie Web published to .pub-devices first: dotnet publish ../../src/FLACie.Server -c Release -o .pub-devices
//   node devices-android.mjs --port 8297 --shots <dir>
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { requireMock } from './require-mock.mjs';
import { requireEmulator, sh, sleep, setAdb, has, find, label, tapNode, tapAt, typeText, key, waitUi, dump, mediaSession, shot } from './adbui.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const PORT = Number(arg('port', 8297)) + 1, WEB = Number(arg('web', 5392));
const SHOTS = arg('shots', path.join(here, '..', '..', '..', 'docs', 'screens-devices-2026-11-25'));
setAdb(arg('adb', ''));
const PKG = 'app.flacie';
const MOCK = `http://127.0.0.1:${PORT}`, WEBURL = `http://127.0.0.1:${WEB}`;
const TOKEN = { 'X-Emby-Token': 'mock-token', 'X-Jellyfin-Server': `http://10.0.2.2:${PORT}`, 'content-type': 'application/json' };
const kids = [];
const stopAll = () => { for (const k of kids) { try { k.kill('SIGKILL'); } catch { } } };
process.on('exit', stopAll);
const log = (...a) => console.error('[devices]', ...a);
const checks = [];
const check = (name, pass, detail = '') => { checks.push({ name, pass: !!pass }); log(`${pass ? 'PASS' : 'FAIL'} ${name}${detail ? ` (${detail})` : ''}`); };
const S = (n) => path.join(SHOTS, n + '.png');

async function waitOk(url, tries = 150) { for (let i = 0; i < tries; i++) { try { if ((await fetch(url)).ok) return true; } catch { } await sleep(400); } return false; }
const labels = () => (dump() ?? []).map(label).filter(Boolean);
const tapLabel = async (re, minY = 0) => { const n = (dump() ?? []).find((x) => re.test(label(x)) && x.cy > minY); if (n) { tapNode(n); await sleep(2000); return true; } return false; };

async function main() {
  fs.mkdirSync(SHOTS, { recursive: true });
  // 1. the mock Jellyfin, its shared profile naming the local FLACie Web
  kids.push(spawn('node', [path.join(here, 'mock.mjs'), '--port', String(PORT), '--host', '0.0.0.0', '--items', '30', '--wavsec', '180'], { stdio: 'ignore', windowsHide: true }));
  if (!await waitOk(`${MOCK}/System/Info/Public`)) throw new Error('mock did not start');
  await requireMock(MOCK);
  const profile = { services: { flacieweb: { url: `http://10.0.2.2:${WEB}` } } };
  await fetch(`${MOCK}/DisplayPreferences/ipodplayer?userId=x&client=ipodplayer`, { method: 'POST', headers: { 'content-type': 'application/json', 'X-Emby-Token': 'mock-token' }, body: JSON.stringify({ CustomPrefs: { 'ipodplayer.profile': JSON.stringify(profile) }, Client: 'ipodplayer' }) });

  // 2. FLACie Web, its own data folder
  const data = path.join(here, '.data-devices'); fs.rmSync(data, { recursive: true, force: true }); fs.mkdirSync(data, { recursive: true });
  const web = spawn('dotnet', ['FLACie.Server.dll'], { cwd: path.join(here, '.pub-devices'), windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'], env: { ...process.env,
    ASPNETCORE_URLS: `http://0.0.0.0:${WEB}`, FLACIE_JELLYFIN_URL: MOCK, FLACIE_JELLYFIN_INTERNAL_URL: MOCK, FLACIE_DATA: data, FLACIE_WEB_PLAYLISTS: 'off', FLACIE_DOWNLOADS: 'off' } });
  kids.push(web);
  const webLog = []; web.stdout.on('data', (d) => webLog.push(String(d))); web.stderr.on('data', (d) => webLog.push(String(d)));
  if (!await waitOk(`${WEBURL}/healthz`)) { log(webLog.join('').slice(-3000)); throw new Error('FLACie Web did not start'); }

  // 3. the phone: signed in with Quick Connect (the profile brings FLACie Web), a song played
  requireEmulator(); sh(`am force-stop ${PKG}`); sh(`pm clear ${PKG}`);
  sh(`pm grant ${PKG} android.permission.POST_NOTIFICATIONS`, { quiet: true });
  sh(`am start -n ${PKG}/com.ipodemu.ui.MainActivity`, { quiet: true });
  let ui = await waitUi((n) => has(n, 'Sign in with Jellyfin'), 120000, 2000);
  tapNode(find(ui, 'Sign in with Jellyfin'));
  ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 30000, 1000);
  const field = ui?.find((n) => /EditText/.test(n.cls));
  tapAt(field.cx, field.cy); await sleep(600); typeText(`http://10.0.2.2:${PORT}`); await sleep(600); key('KEYCODE_BACK'); await sleep(800);
  ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 20000, 1000);
  tapNode(find(ui, 'Sign in with Quick Connect'));
  await waitUi((n) => !has(n, 'Sign in with Quick Connect') && !has(n, 'Jellyfin server') && !has(n, 'Signing in'), 60000, 1500);
  await sleep(15000);   // the library and the account sync (FLACie Web's address)
  const nav = (dump() ?? []).length && find(dump(), 'Search', { minY: 2000 }); if (nav) tapNode(nav); await sleep(2000);
  await tapLabel(/^Songs$/);
  const row = (dump() ?? []).filter((n) => /^Song \d+$/.test(label(n)) && n.cy > 700 && n.cy < 2000)[1];
  if (row) tapAt(500, row.cy);
  for (let i = 0; i < 40; i++) { if (mediaSession().state === 3) break; await sleep(500); }
  const playing = mediaSession().description;
  log('playing:', playing);
  await sleep(12000);   // the phone tells FLACie Web where it is within a few seconds

  // 4. what reached the server: the phone's own state; then a tablet's earlier moment, on a different song
  const where = await (await fetch(`${WEBURL}/api/where`, { headers: TOKEN })).json();
  const mine = where.devices?.[0];
  check('the phone told FLACie Web where it is', mine && mine.states?.length > 0, JSON.stringify(mine?.states?.[0] ?? {}).slice(0, 200));
  const one = mine ? await (await fetch(`${WEBURL}/api/where/${mine.states[0].id}`, { headers: TOKEN })).json() : null;
  const q = one?.queue ?? [];
  const pick = Math.min(q.length - 1, (one?.index ?? 0) + 3);
  const pickId = q[pick];
  const item = pickId ? await (await fetch(`${MOCK}/Users/u1/Items/${pickId}`, { headers: { 'X-Emby-Token': 'mock-token' } })).json() : null;
  const r = await fetch(`${WEBURL}/api/where`, { method: 'POST', headers: TOKEN, body: JSON.stringify({ deviceId: 'tablet-9', device: 'Galaxy Tab', kind: 'Phone', title: item?.Name ?? 'Song', artist: item?.Artists?.[0] ?? '',
    itemId: pickId, positionMs: 65_000, durationMs: 180_000, paused: false, queue: q, index: pick, context: 'Road trip' }) });
  log('tablet moment:', r.status, item?.Name);

  // 5. Devices: from Now Playing (its Devices button, else the More menu)
  const mini = (dump() ?? []).filter((n) => /^Song \d+$/.test(label(n)) && n.cy > 1800).pop();
  if (mini) { tapNode(mini); await sleep(2500); }
  if (!await tapLabel(/^Devices$/)) { await tapLabel(/^More$/); await tapLabel(/^Devices$/); }
  await sleep(4000);
  check('Devices opens with the new design', has(dump(), 'Your devices') || labels().some((l) => /Your/.test(l) && /devices/.test(l)), labels().slice(0, 20).join(' | '));
  shot(S('android-devices-top'));
  sh('input swipe 540 1700 540 500 500'); await sleep(1500);
  shot(S('android-devices-middle'));
  sh('input swipe 540 1700 540 500 500'); await sleep(1500);
  shot(S('android-devices-where'));
  const seen = labels();
  check('Where you were lists the tablet and this phone', seen.some((l) => /Galaxy Tab/.test(l)) && seen.some((l) => /this one/.test(l)), seen.slice(0, 40).join(' | '));
  check('a moment says how long ago and where in the song', seen.some((l) => /at 1:05 in Road trip/.test(l)));

  // 6. carry on from the tablet's moment: the phone plays that song
  const tabMoment = (dump() ?? []).find((n) => label(n) === item?.Name);
  const btns = (dump() ?? []).filter((n) => label(n) === 'Carry on' && (!tabMoment || n.cy > tabMoment.cy)).sort((a, b) => a.cy - b.cy);
  if (btns[0]) tapNode(btns[0]);
  for (let i = 0; i < 30; i++) { if ((mediaSession().description ?? '').includes(item?.Name ?? '@@')) break; await sleep(500); }
  check('Carry on plays the tablet\'s song here', (mediaSession().description ?? '').includes(item?.Name ?? '@@'), mediaSession().description);
  await sleep(1500);
  shot(S('android-devices-carried-on'));

  stopAll();
  const failed = checks.filter((c) => !c.pass).length;
  console.log(`devices: ${checks.length - failed} passed, ${failed} failed`);
  process.exit(failed ? 1 : 0);
}

main().catch((e) => { console.error(e); stopAll(); process.exit(1); });
