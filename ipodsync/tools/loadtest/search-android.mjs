// Search on the phone for a song the library lacks (owner, 6 Nov: "only download when I click Download"; "it should filter out the 30 versions like Spotify"):
// the catalogue's versions of one song are one row ("+N versions"), a tap opens the song's page (title, artist, Download, Other versions) and nothing is
// fetched until Download. The catalogue is Deezer's public search, so the emulator needs the internet; the library is the mock's.
//   run-android.ps1 -Apk <apk> -Driver search-android.mjs
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { requireMock } from './require-mock.mjs';
import { requireEmulator, sh, sleep, setAdb, has, find, label, tapNode, tapAt, typeText, key, waitUi, dump, shot } from './adbui.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const PORT = Number(arg('port', 8297)) + 1;
setAdb(arg('adb', ''));
const PKG = 'app.flacie', MOCK = `http://127.0.0.1:${PORT}`, PHONE_BASE = `http://10.0.2.2:${PORT}`;
const kids = []; const stopAll = () => { for (const k of kids) { try { k.kill('SIGKILL'); } catch { } } };
process.on('exit', stopAll);
const log = (...a) => console.error('[search]', ...a);
const checks = [];
const check = (name, pass, detail = '') => { checks.push({ name, pass: !!pass, detail: String(detail) }); log(`${pass ? 'PASS' : 'FAIL'} ${name}${detail ? ` (${detail})` : ''}`); };
async function waitOk(url, tries = 150) { for (let i = 0; i < tries; i++) { try { if ((await fetch(url)).ok) return true; } catch { } await sleep(400); } return false; }

async function main() {
  kids.push(spawn('node', [path.join(here, 'mock.mjs'), '--port', String(PORT), '--host', '0.0.0.0', '--items', '30'], { stdio: 'ignore', windowsHide: true }));
  if (!await waitOk(`${MOCK}/System/Info/Public`)) throw new Error('mock did not start');
  await requireMock(MOCK);

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
  await sleep(10000);
  const got = dump() ?? []; if (has(got, 'Got it')) tapNode(find(got, 'Got it'));

  const nav = find(dump() ?? [], 'Search', { minY: 2000 }); if (nav) tapNode(nav); await sleep(1500);
  const sb = await waitUi((n) => n.some((x) => /^Songs, albums/.test(label(x)) || /EditText/.test(x.cls)), 10000, 800);
  const box = (sb ?? []).find((x) => /^Songs, albums/.test(label(x))) ?? (sb ?? []).find((x) => /EditText/.test(x.cls) && x.cy < 800);
  if (box) tapAt(box.cx, box.cy); await sleep(800);
  typeText('you got the love florence'); await sleep(800); key('KEYCODE_BACK');   // the keyboard down (adb's Enter is not the keyboard's Search key)
  const res = await waitUi((n) => n.some((x) => /version/.test(label(x))), 30000, 1500);
  shot(path.join(here, 'search-shot-results.png'));
  const grouped = (res ?? []).filter((x) => /\+\d+ versions?/.test(label(x)));
  check('versions of one song are grouped into one row ("+N versions")', grouped.length > 0, grouped.map(label).slice(0, 3).join(' | '));
  const titles = (res ?? []).map(label).filter((l) => /^you('ve)? got the love$/i.test(l));
  check('the same song is not listed again and again', titles.length <= 3, `${titles.length} rows titled You Got the Love`);

  // a tap on the row (its title, not its Download button) opens the song's page
  // a song row's title (not the search box, which holds the same words, nor an album card)
  const songsAt = Math.max(0, ...(res ?? []).filter((x) => label(x) === 'Songs' && x.cy > 700).map((x) => x.cy));
  const row = (res ?? []).find((x) => /^you('ve)? got the love$/i.test(label(x)) && !/EditText/.test(x.cls) && x.cy > songsAt && songsAt > 0) ?? (res ?? []).find((x) => grouped.includes(x));
  if (row) tapNode(row);
  const page = await waitUi((n) => has(n, 'Other versions') || n.some((x) => label(x) === 'Download'), 15000, 1000);
  shot(path.join(here, 'search-shot-page.png'));
  check('a tap opens the song page with a Download button', !!page && page.some((x) => label(x) === 'Download'), (page ?? []).map(label).filter(Boolean).slice(0, 10).join(' | '));
  check('the page names the song and its artist', !!page && page.some((x) => /got the love/i.test(label(x))) && page.some((x) => /florence/i.test(label(x))));
  check('the page lists the other versions', !!page && has(page, 'Other versions'));
  check('opening the page downloaded nothing', !!page && !page.some((x) => /Getting|Downloading|Searching|Queued/i.test(label(x))), (page ?? []).map(label).filter((l) => /Getting|Download/i.test(l)).join(' | '));

  // an album the library lacks: a tap opens its page (its songs, Download album), it does not start downloading
  key('KEYCODE_BACK'); await sleep(1500);
  const res2 = await waitUi((n) => has(n, 'Albums and EPs'), 15000, 1000);
  const card = (res2 ?? []).find((x) => /, (Album|EP|Single) by /.test(label(x)) && !/In your library/.test(label(x)));
  if (card) tapAt(card.x1 + 60, card.y1 + 60);
  const albumPage = await waitUi((n) => n.some((x) => /^Download (album|ep|single)$/i.test(label(x))), 15000, 1000);
  shot(path.join(here, 'search-shot-album.png'));
  check('a tap on an album card opens its page with Download album', !!albumPage, card ? label(card) : 'no album card');
  check('the album page lists its songs', !!albumPage && (has(albumPage, 'Songs') || has(albumPage, 'Loading the songs')));
  check('opening the album downloaded nothing', !!albumPage && !albumPage.some((x) => /Requested|Getting|Downloading/i.test(label(x))));

  const failed = checks.filter((c) => !c.pass).length;
  console.log(`search: ${checks.length - failed} passed, ${failed} failed`);
  fs.writeFileSync(path.join(here, 'result-search.json'), JSON.stringify(checks, null, 2));
  stopAll(); process.exit(failed ? 1 : 0);
}

main().catch((e) => { console.error(e); try { shot(path.join(here, 'search-shot-failed.png')); } catch { } stopAll(); process.exit(1); });
