import * as crypto from 'node:crypto';
import * as fs from 'node:fs';
import * as path from 'node:path';

// Runs once, right after the web server is up and before any test. FLACie Web keeps a log of every download it ran per user (downloads/<user>.jsonl, read the first
// time that user's log is needed). The "recently downloaded" tag has a one hour window, so a spec needs finished downloads of known ages that it can not make by
// waiting: this writes them into the test account's log before anything asks for it (1i-download-tags.spec.ts). Songs of the mock's "Brass Album 1".
// The user id is what SessionStore keys a Jellyfin sign-in by: SHA-256 of "jf|<server>|<user id>" (the mock's only user is u1).
export default async function globalSetup() {
  const mock = `http://127.0.0.1:${process.env.MOCK_PORT || 8196}`;
  // the marker check: the Jellyfin the whole run talks to must be the mock (mock-jellyfin.mjs answers GET /__mock with {"mock":true})
  let isMock = false, why = '';
  try { const r = await fetch(`${mock}/__mock`, { signal: AbortSignal.timeout(5000) }); isMock = r.ok && (await r.json())?.mock === true; why = `HTTP ${r.status}`; } catch (e) { why = String((e as Error)?.message ?? e); }
  if (!isMock) throw new Error(`REFUSING TO RUN: the Jellyfin at ${mock} is not the mock (GET /__mock: ${why}). Tests must never talk to a real Jellyfin.`);
  // the FLACie Web the run talks to must be this checkout's build. Playwright reuses any server already listening on WEB_PORT (an earlier run, or the
  // Windows app's own copy), and an older build fails exactly the specs for the newest changes (9 Nov: five "failing alone" specs). Its page script and
  // stylesheet must be these files, byte for byte.
  const web = `http://127.0.0.1:${process.env.WEB_PORT || 5290}`;
  const hash = (b: Buffer | Uint8Array) => crypto.createHash('sha256').update(b).digest('hex');
  for (const f of ['flacie.js', 'app.css']) {
    const mine = hash(fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'FLACie.Server', 'wwwroot', f)));
    let served = '', what = '';
    try { const r = await fetch(`${web}/${f}`, { signal: AbortSignal.timeout(10_000) }); what = `HTTP ${r.status}`; if (r.ok) served = hash(new Uint8Array(await r.arrayBuffer())); } catch (e) { what = String((e as Error)?.message ?? e); }
    if (served !== mine) throw new Error(`REFUSING TO RUN: the FLACie Web at ${web} is not this checkout's build (its ${f} differs: ${what}). Playwright reuses a server already on WEB_PORT: stop it, or run on other ports (MOCK_PORT, WEB_PORT, FM_PORT).`);
  }
  const id = crypto.createHash('sha256').update(`jf|${mock}|u1`).digest('hex').toUpperCase();
  const dir = path.join(__dirname, '.pub-smoke', '.data-smoke', 'downloads');
  fs.mkdirSync(dir, { recursive: true });
  // a run must not see what an earlier run left: the MusicBrainz answers and matches, and the chosen covers (1k-musicbrainz asks the stand-in once and counts it),
  // and the listening log the recommendations learn from (every run played more Brass Monkeys, until they led Quick picks over the songs Jellyfin counted)
  const data = path.join(__dirname, '.pub-smoke', '.data-smoke');
  for (const p of ['mb-cache', 'art-override', 'listen']) fs.rmSync(path.join(data, p), { recursive: true, force: true });
  fs.rmSync(path.join(data, 'mb-overlay.json'), { force: true });
  const now = Date.now();
  const rec = (title: string, outcome: 'done' | 'failed', ageMin: number) => {
    const at = new Date(now - ageMin * 60_000).toISOString();
    return JSON.stringify({
      Id: crypto.randomUUID().replace(/-/g, ''), UserId: id, UserName: 'tester', Label: `${title} · Brass Monkeys`, Title: title, Artist: 'Brass Monkeys', Kind: 'Search',
      StartedAt: at, FinishedAt: at, Outcome: outcome, Source: outcome === 'done' ? 'soulseek' : null, Message: outcome === 'done' ? 'ready to play' : 'Not found on Soulseek',
      File: null, ArtKey: null, Album: 'Brass Album 1', DurationMs: 0, ArtUrl: null, IsAlbum: false, Trail: [],
    });
  };
  const lines = [
    rec('Velvet 9', 'done', 7 * 24 * 60),    // a week ago: a normal library song
    rec('Orbit 10', 'done', 120),            // two hours ago: no tag
    rec('Harbour 11', 'done', 5),            // five minutes before the run: "Recently downloaded · N min ago" for the whole of a long run (the window is an hour)
    rec('Lantern 12', 'failed', 150),        // failed two hours ago: no "Couldn't download"
  ];
  fs.writeFileSync(path.join(dir, `${id}.jsonl`), lines.join('\n') + '\n');
  // when these were written, so a spec can work out how old they are by the time it runs (late in a 45-minute run, not just at the start)
  fs.writeFileSync(path.join(data, 'setup-at.txt'), String(now));
}
