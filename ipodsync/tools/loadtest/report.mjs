// Merges the result JSON files of run-web.mjs / run-android.ps1 into the before/after tables of docs/test-run/I-load.md.
//   node report.mjs <dir with before-*.json, after-*.json, android-before-*.json, android-after-*.json> > tables.md
import fs from 'node:fs';
import path from 'node:path';

const dir = process.argv[2] ?? path.resolve('../../../docs/test-run/load-data');
const read = (f) => { try { return JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8')); } catch { return null; } };
const profiles = ['fast', 'slow', 'flaky', 'ratelimit'];
const profileText = { fast: 'fast (no delay)', slow: 'slow (about 300 ms per answer, 8 at once at most)', flaky: 'flaky (10 % of answers are 503)', ratelimit: 'rate-limiting (more than 20 requests in a second get 429)' };
const n = (v) => (v === null || v === undefined || Number.isNaN(v) ? 'n/a' : typeof v === 'number' ? v.toLocaleString('en-GB') : String(v));
const s = (ms) => (ms === null || ms === undefined ? 'not reached' : ms < 10000 ? `${(ms / 1000).toFixed(1)} s` : `${Math.round(ms / 1000)} s`);

function rows(b, a) {
  const g = (r, ...p) => p.reduce((o, k) => (o === undefined || o === null ? undefined : o[k]), r);
  const line = (label, path, fmt = n) => `| ${label} | ${b ? fmt(g(b, ...path)) : 'n/a'} | ${a ? fmt(g(a, ...path)) : 'n/a'} |`;
  const sc = (x) => x?.scenarios ?? {};
  return [
    `| | before (beta 51) | after (beta 52) |`, `|---|---:|---:|`,
    `| **First sync, empty start** | | |`,
    `| songs shown at the end of the 120 s window | ${n(g(sc(b), 'first-sync', 'songsShown'))} | ${n(g(sc(a), 'first-sync', 'songsShown'))} |`,
    `| time until the first songs are listed | ${s(g(sc(b), 'first-sync', 'timeToFirstSongsMs'))} | ${s(g(sc(a), 'first-sync', 'timeToFirstSongsMs'))} |`,
    `| time until all songs are listed | ${s(g(sc(b), 'first-sync', 'timeToAllSongsMs'))} | ${s(g(sc(a), 'first-sync', 'timeToAllSongsMs'))} |`,
    `| Jellyfin requests in the 120 s after sign-in | ${n(g(sc(b), 'first-sync', 'final', 'total'))} | ${n(g(sc(a), 'first-sync', 'final', 'total'))} |`,
    `| &nbsp;&nbsp;of which one-song info reads | ${n(g(sc(b), 'first-sync', 'final', 'byKind', 'info'))} | ${n(g(sc(a), 'first-sync', 'final', 'byKind', 'info'))} |`,
    `| &nbsp;&nbsp;of which library pages | ${n(g(sc(b), 'first-sync', 'final', 'byKind', 'items'))} | ${n(g(sc(a), 'first-sync', 'final', 'byKind', 'items'))} |`,
    `| peak requests at the same moment | ${n(g(sc(b), 'first-sync', 'final', 'peakInFlight'))} | ${n(g(sc(a), 'first-sync', 'final', 'peakInFlight'))} |`,
    `| peak requests in one second | ${n(g(sc(b), 'first-sync', 'final', 'peakPerSecond'))} | ${n(g(sc(a), 'first-sync', 'final', 'peakPerSecond'))} |`,
    `| answers that were 429 / 5xx | ${n(g(sc(b), 'first-sync', 'final', 'throttled'))} / ${n(g(sc(b), 'first-sync', 'final', 'failed'))} | ${n(g(sc(a), 'first-sync', 'final', 'throttled'))} / ${n(g(sc(a), 'first-sync', 'final', 'failed'))} |`,
    `| **Scrolling the whole list (about 40 s)** | | |`,
    `| cover requests | ${n(g(sc(b), 'scrolling', 'images'))} | ${n(g(sc(a), 'scrolling', 'images'))} |`,
    `| &nbsp;&nbsp;distinct pictures / repeated asks | ${n(g(sc(b), 'scrolling', 'imageUrls'))} / ${n(g(sc(b), 'scrolling', 'imageRepeats'))} | ${n(g(sc(a), 'scrolling', 'imageUrls'))} / ${n(g(sc(a), 'scrolling', 'imageRepeats'))} |`,
    `| &nbsp;&nbsp;picture sizes asked for | ${Object.keys(g(sc(b), 'scrolling', 'imageVariants') ?? {}).join(', ') || 'n/a'} | ${Object.keys(g(sc(a), 'scrolling', 'imageVariants') ?? {}).join(', ') || 'n/a'} |`,
    `| peak requests at the same moment | ${n(g(sc(b), 'scrolling', 'peakInFlight'))} | ${n(g(sc(a), 'scrolling', 'peakInFlight'))} |`,
    `| covers on screen that had loaded when scrolling stopped | ${n(g(sc(b), 'scrolling', 'coversLoaded'))} of ${n(g(sc(b), 'scrolling', 'coversOnScreen'))} | ${n(g(sc(a), 'scrolling', 'coversLoaded'))} of ${n(g(sc(a), 'scrolling', 'coversOnScreen'))} |`,
    `| **40 quick skips** | | |`,
    `| Jellyfin requests | ${n(g(sc(b), 'skip-burst', 'total'))} | ${n(g(sc(a), 'skip-burst', 'total'))} |`,
    `| audio streams opened / abandoned before the end | ${n(g(sc(b), 'skip-burst', 'streamsOpened'))} / ${n(g(sc(b), 'skip-burst', 'streamsAborted'))} | ${n(g(sc(a), 'skip-burst', 'streamsOpened'))} / ${n(g(sc(a), 'skip-burst', 'streamsAborted'))} |`,
    `| peak requests at the same moment | ${n(g(sc(b), 'skip-burst', 'peakInFlight'))} | ${n(g(sc(a), 'skip-burst', 'peakInFlight'))} |`,
    `| last song ready after (from the first skip) | ${s(g(sc(b), 'skip-burst', 'lastSongReadyMs'))} | ${s(g(sc(a), 'skip-burst', 'lastSongReadyMs'))} |`,
    `| **Warm start (library already on disk)** | | |`,
    `| songs listed after | ${s(g(sc(b), 'warm-start', 'timeToAllSongsMs'))} | ${s(g(sc(a), 'warm-start', 'timeToAllSongsMs'))} |`,
    `| Jellyfin requests in the first 60 s | ${n(g(sc(b), 'warm-start', 'total'))} | ${n(g(sc(a), 'warm-start', 'total'))} |`,
  ].join('\n');
}

const out = [];
for (const shell of ['web', 'desktop']) {
  out.push(`### ${shell === 'web' ? 'FLACie Web (server, browser client)' : 'Windows app mode (the same server code started the way the desktop app starts it)'}`);
  for (const p of profiles) {
    const b = read(`before-${shell}-${p}.json`), a = read(`after-${shell}-${p}.json`);
    if (!b && !a) continue;
    out.push(`#### Server: ${profileText[p]}`, rows(b, a), '');
    if (b?.error || a?.error) out.push(`_Run problems:_ ${b?.error ? 'before: ' + String(b.error).split('\n')[0] : ''} ${a?.error ? 'after: ' + String(a.error).split('\n')[0] : ''}`, '');
  }
}
for (const p of profiles) {
  const b = read(`android-before-${p}.json`), a = read(`android-after-${p}.json`);
  if (!b && !a) continue;
  if (!out.includes('### Android (emulator)')) out.push('### Android (emulator, software rendered; request counts only)');
  out.push(`#### Server: ${profileText[p]}`, rows(b, a), '');
}
console.log(out.join('\n'));
