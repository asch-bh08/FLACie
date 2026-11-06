// The DJ on the phone, end to end on the emulator (owner, 6 Nov: "the DJ still just isn't talking, no subtitles, nothing"): a mock Jellyfin whose
// shared profile names a local FLACie Web, that FLACie Web with the DJ on (its voice a stand-in for Kokoro answering with a real clip), the APK signed in
// with Quick Connect, a song played, and the songs after it heard through. Prints the phone's DJ log (/api/diag) and the server's DJ lines.
// Started by run-android.ps1 -Driver dj-android.mjs (boots the emulator, installs the APK; its own mock takes --port, so this one uses the next port); standalone:
//   node dj-android.mjs --port 8297
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { requireMock } from './require-mock.mjs';
import { requireEmulator, sh, sleep, setAdb, has, find, label, tapNode, tapAt, typeText, key, waitUi, dump, mediaSession, shot } from './adbui.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const PORT = Number(arg('port', 8297)) + 1, WEB = Number(arg('web', 5391)), VOICE = Number(arg('voice', 8093));
setAdb(arg('adb', ''));
const PKG = 'app.flacie';
const MOCK = `http://127.0.0.1:${PORT}`, WEBURL = `http://127.0.0.1:${WEB}`;
const TOKEN = { 'X-Emby-Token': 'mock-token', 'X-Jellyfin-Server': `http://10.0.2.2:${PORT}`, 'content-type': 'application/json' };
const kids = [];
const stopAll = () => { for (const k of kids) { try { k.kill('SIGKILL'); } catch { } } };
process.on('exit', stopAll);
const log = (...a) => console.error('[dj]', ...a);

async function waitOk(url, tries = 150) { for (let i = 0; i < tries; i++) { try { if ((await fetch(url)).ok) return true; } catch { } await sleep(400); } return false; }

async function main() {
  // 1. the mock Jellyfin: 30 songs of 45 s, its shared profile naming the local FLACie Web
  kids.push(spawn('node', [path.join(here, 'mock.mjs'), '--port', String(PORT), '--host', '0.0.0.0', '--items', '30', '--wavsec', '45'], { stdio: 'ignore', windowsHide: true }));
  if (!await waitOk(`${MOCK}/System/Info/Public`)) throw new Error('mock did not start');
  await requireMock(MOCK);
  const profile = { services: { flacieweb: { url: `http://10.0.2.2:${WEB}` } } };
  await fetch(`${MOCK}/DisplayPreferences/ipodplayer?userId=x&client=ipodplayer`, { method: 'POST', headers: { 'content-type': 'application/json', 'X-Emby-Token': 'mock-token' }, body: JSON.stringify({ CustomPrefs: { 'ipodplayer.profile': JSON.stringify(profile) }, Client: 'ipodplayer' }) });

  // 2. Kokoro's stand-in: every line is the same real clip
  const clip = fs.readFileSync(path.join(here, 'dj-voice-sample.mp3'));
  let said = 0;
  const voice = http.createServer((req, res) => { req.resume(); req.on('end', () => { said++; res.writeHead(200, { 'content-type': 'audio/mpeg', 'content-length': clip.length }); res.end(clip); }); }).listen(VOICE, '127.0.0.1');

  // 3. FLACie Web with the DJ: Kokoro's voice (no Cloudflare), its own data folder
  const data = path.join(here, '.data-dj'); fs.rmSync(data, { recursive: true, force: true }); fs.mkdirSync(data, { recursive: true });
  fs.writeFileSync(path.join(data, 'dj.json'), JSON.stringify({ Voice: 'kokoro:am_liam', AiLines: false, CapPercent: 50, Version: 2 }));
  const web = spawn('dotnet', ['FLACie.Server.dll'], { cwd: path.join(here, '.pub-dj'), windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'], env: { ...process.env,
    ASPNETCORE_URLS: `http://0.0.0.0:${WEB}`, FLACIE_JELLYFIN_URL: MOCK, FLACIE_JELLYFIN_INTERNAL_URL: MOCK, FLACIE_DATA: data, FLACIE_WEB_PLAYLISTS: 'off',
    FLACIE_DOWNLOADS: 'off', FLACIE_KOKORO_URL: `http://127.0.0.1:${VOICE}`, Logging__LogLevel__Default: 'Information', 'Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics': 'Information' } });
  kids.push(web);
  const webLog = []; web.stdout.on('data', (d) => webLog.push(String(d))); web.stderr.on('data', (d) => webLog.push(String(d)));
  if (!await waitOk(`${WEBURL}/healthz`)) { log(webLog.join('').slice(-3000)); throw new Error('FLACie Web did not start'); }
  // the DJ on for the phone, at every song
  for (let i = 0; i < 30; i++) {
    const r = await fetch(`${WEBURL}/api/dj/settings`, { method: 'POST', headers: TOKEN, body: JSON.stringify({ enabled: true, phone: true, auto: true, chatty: 'every', captions: true }) });
    if (r.ok) { log('DJ settings:', (await r.text()).slice(0, 300)); break; }
    await sleep(1000);
  }

  // 4. the phone: signed in with Quick Connect (the profile brings FLACie Web), a song played
  requireEmulator(); sh(`am force-stop ${PKG}`); sh(`pm clear ${PKG}`);
  sh(`pm grant ${PKG} android.permission.POST_NOTIFICATIONS`, { quiet: true });
  sh('logcat -c', { quiet: true });
  sh(`am start -n ${PKG}/com.ipodemu.ui.MainActivity`, { quiet: true });
  let ui = await waitUi((n) => has(n, 'Sign in with Jellyfin'), 120000, 2000);
  tapNode(find(ui, 'Sign in with Jellyfin'));
  ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 30000, 1000);
  const field = ui?.find((n) => /EditText/.test(n.cls));
  tapAt(field.cx, field.cy); await sleep(600); typeText(`http://10.0.2.2:${PORT}`); await sleep(600); key('KEYCODE_BACK'); await sleep(800);
  ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 20000, 1000);
  tapNode(find(ui, 'Sign in with Quick Connect'));
  await waitUi((n) => !has(n, 'Sign in with Quick Connect') && !has(n, 'Jellyfin server') && !has(n, 'Signing in'), 60000, 1500);
  const step = (n) => { try { shot(path.join(here, `dj-shot-${n}.png`)); } catch { } log('screen', n + ':', (dump() ?? []).map(label).filter(Boolean).slice(0, 25).join(' | ')); };
  step('signed-in');
  await sleep(15000);   // the library, the account sync (FLACie Web's address) and the DJ settings
  // what the phone wrote back to the shared profile: its services show whether it took FLACie Web's address
  try { const back = JSON.parse((await (await fetch(`${MOCK}/DisplayPreferences/ipodplayer`, { headers: { 'X-Emby-Token': 'mock-token' } })).json()).CustomPrefs?.['ipodplayer.profile'] ?? '{}'); log('profile after sign-in: updated', back.updated ?? '(never pushed)', 'services', JSON.stringify(back.services ?? {}).slice(0, 300)); } catch (e) { log('profile unreadable:', e.message); }
  // play the first song in Search's song list
  const ui2 = dump(); const nav = ui2 && find(ui2, 'Search', { minY: 2000 }); if (nav) tapNode(nav); await sleep(2000);
  const chip = (dump() ?? []).find((n) => label(n) === 'Songs'); if (chip) tapNode(chip); await sleep(2000);
  const row = (dump() ?? []).filter((n) => /^Song \d+$/.test(label(n)) && n.cy > 700 && n.cy < 2000)[0];
  step('songs');
  if (row) tapAt(500, row.cy);
  for (let i = 0; i < 40; i++) { if (mediaSession().state === 3) break; await sleep(500); }
  log('playing:', mediaSession().description);
  step('playing');
  // three songs heard through (45 s each), so the DJ asks about the next and says it
  await sleep(150000);

  // 5. what happened
  const diag = path.join(data, 'diag'); const files = fs.existsSync(diag) ? fs.readdirSync(diag) : [];
  log('phone log entries:'); for (const f of files) for (const l of fs.readFileSync(path.join(diag, f), 'utf8').trim().split('\n')) { try { const o = JSON.parse(l); console.log(`  ${o.Kind}: ${o.Text}`); } catch { } }
  log('voice clips made:', said);
  const asked = webLog.join('').split('\n').filter((l) => /Request (starting|finished)/.test(l) && /\/api\//.test(l));
  log('phone requests to FLACie Web:', asked.length); console.log(asked.slice(-40).join('\n'));
  const lc = String(sh('logcat -d', { quiet: true }) ?? '');
  fs.writeFileSync(path.join(here, 'dj-logcat.txt'), lc);
  fs.writeFileSync(path.join(here, 'dj-weblog.txt'), webLog.join(''));
  log('logcat lines:', lc.split('\n').length, '(dj-logcat.txt)');
  voice.close(); stopAll();
}

main().catch((e) => { console.error(e); stopAll(); process.exit(1); });
