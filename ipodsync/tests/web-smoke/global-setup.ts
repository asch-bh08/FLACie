import * as crypto from 'node:crypto';
import * as fs from 'node:fs';
import * as path from 'node:path';

// Runs once, right after the web server is up and before any test. FLACie Web keeps a log of every download it ran per user (downloads/<user>.jsonl, read the first
// time that user's log is needed). The "recently downloaded" tag has a one hour window, so a spec needs finished downloads of known ages that it can not make by
// waiting: this writes them into the test account's log before anything asks for it (1i-download-tags.spec.ts). Songs of the mock's "Brass Album 1".
// The user id is what SessionStore keys a Jellyfin sign-in by: SHA-256 of "jf|<server>|<user id>" (the mock's only user is u1).
export default async function globalSetup() {
  const mock = `http://127.0.0.1:${process.env.MOCK_PORT || 8196}`;
  const id = crypto.createHash('sha256').update(`jf|${mock}|u1`).digest('hex').toUpperCase();
  const dir = path.join(__dirname, '.pub-smoke', '.data-smoke', 'downloads');
  fs.mkdirSync(dir, { recursive: true });
  // a run must not see what an earlier run left: the MusicBrainz answers and matches, and the chosen covers (1k-musicbrainz asks the stand-in once and counts it)
  const data = path.join(__dirname, '.pub-smoke', '.data-smoke');
  for (const p of ['mb-cache', 'art-override']) fs.rmSync(path.join(data, p), { recursive: true, force: true });
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
