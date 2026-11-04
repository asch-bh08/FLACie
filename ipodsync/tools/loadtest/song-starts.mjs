// Plays one real song file served by the mock (--audio FILE, set by AUDIO=...) on the emulator and reports whether it starts: for a song that stays silent and
// "loading" for ever on the phone (owner, 2 Nov: Wild Ones, Flo Rida) while it plays fine on the server. Started like the smoke test:
//   run-android.ps1 -Apk <apk> -Port 8297 -Driver song-starts.mjs   (with AUDIO=<file> set)
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import { requireEmulator, sh, sleep, setAdb, dump, has, find, tapNode, tapAt, typeText, key, waitUi, label, shot, mediaSession } from './adbui.mjs';
import { requireMock } from './require-mock.mjs';
const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const PORT = Number(arg('port', 8297)), SHOTS = arg('shots', '.');
setAdb(arg('adb', ''));
const PKG = 'app.flacie', MOCK = `http://127.0.0.1:${PORT}`;
const S = (n) => path.join(SHOTS, n + '.png');
const sizes = (process.env.SIZES || 'native').split(',');

const mock = spawn('node', [path.join(here, 'mock.mjs'), '--port', String(PORT), '--host', '127.0.0.1', '--items', '400', ...(process.env.AUDIO ? ['--audio', process.env.AUDIO] : [])], { stdio: 'ignore', windowsHide: true });
process.on('exit', () => { try { mock.kill('SIGKILL'); } catch { } try { sh('wm size reset'); sh('wm density reset'); } catch { } });
for (let i = 0; i < 100; i++) { try { if ((await fetch(`${MOCK}/System/Info/Public`)).ok) break; } catch { } await sleep(200); }
await fetch(`${MOCK}/__profile?name=fast`); await fetch(`${MOCK}/__reset`);

requireEmulator(); await requireMock(MOCK); sh(`am force-stop ${PKG}`); sh(`pm clear ${PKG}`);
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


const tapLabel = async (re, minY = 250) => { const u = dump(); const n = u && u.find((x) => re.test(label(x)) && x.cy > minY); if (n) { tapNode(n); await sleep(2200); return true; } console.log('not found: ' + re); return false; };
const tab = async (name) => { const u = dump(); const n = u && u.filter((x) => label(x) === name).sort((a, b) => b.cy - a.cy)[0]; if (n) tapNode(n); await sleep(2200); return !!n; };
sh('logcat -c', { quiet: true });
await tab('Library'); await tapLabel(/^Songs$/); await sleep(1000);
await tapLabel(/^Song \d+/, 400);
// how it goes for 25 s: the session's state (3 = playing, 6 = buffering) and position each 2.5 s
const seen = [];
for (let i = 0; i < 10; i++) { await sleep(2500); const m = mediaSession(); seen.push(`${m.state}@${m.position}`); }
console.log('states: ' + seen.join(' '));
const m = mediaSession();
const log = (sh('logcat -d -s FLACie:* ExoPlayerImplInternal:* MediaCodecAudioRenderer:* FlacExtractor:* AndroidRuntime:E', { quiet: true, timeout: 30000 }) || '').split('\n').filter((l) => /FLACie|Exo|Flac|Codec|Runtime/.test(l)).slice(-25).join('\n');
console.log(log);
console.log('RESULT ' + (m.state === 3 && (m.position ?? 0) > 2000 ? 'PLAYS' : 'STUCK') + ' ' + JSON.stringify(m));
process.exit(0);
