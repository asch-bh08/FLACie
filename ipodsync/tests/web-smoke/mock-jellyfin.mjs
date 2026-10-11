// A tiny stand-in for Jellyfin, just enough for FLACie Web to sign in and show a library. Account: tester / pw (an administrator).
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url));
// song 1 is an Apple Lossless .m4a (a real file), which browsers cannot play: FLACie Web has to decode it itself
const alacFile = fs.readFileSync(path.join(here, 'fixtures', 'tone-alac.m4a'));

const PORT = Number(process.env.PORT || 8196);
const artists = ['Aurora Vale', 'Brass Monkeys', 'Cobalt Sky', 'Delta Hum', 'Echo Harbour'];
const genres = ['Rock', 'Jazz', 'Electronic', 'Pop', 'Classical'];
const words = ['Sunrise', 'Harbour', 'Static', 'Velvet', 'Lantern', 'Meridian', 'Orbit', 'Paper'];
const songs = [];
for (let a = 0; a < artists.length; a++) {
  for (let al = 0; al < 2; al++) {
    for (let t = 1; t <= 4; t++) {
      const n = songs.length + 1;
      songs.push({
        Id: `s${n}`,
        Name: `${words[(n * 3) % 8]} ${n}`,
        Artists: [artists[a]],
        AlbumArtist: artists[a],
        Album: `${artists[a].split(' ')[0]} Album ${al + 1}`,
        AlbumId: `al${a}${al}`,
        IndexNumber: t,
        ParentIndexNumber: 1,
        RunTimeTicks: (150 + n * 7) * 10_000_000,
        ProductionYear: 1990 + a * 7 + al * 3,
        Path: `/music/${artists[a]}/Album ${al + 1}/${String(t).padStart(2, '0')} song ${n}.${n === 1 ? 'm4a' : 'flac'}`,
        Genres: [genres[a]],
        DateCreated: new Date(Date.UTC(2024, a, 1 + al * 10 + t)).toISOString(),
        ImageTags: { Primary: 'x' },
        MediaType: 'Audio',
      });
    }
  }
}
// ---- Hi-Res lab (1e-hires.spec.ts): exact MediaStreams, so the badge can be checked against the real facts. Deterministic, no randomness ----
// [codec, container, bitDepth, sampleRate, bitRate bps, file name, expected badge]
const labSpec = [
  ['flac', 'flac', 24, 96000, 2800000, 'Lab 1.flac'],
  ['flac', 'flac', 16, 44100, 1700000, 'Lab 2.flac'],
  ['flac', 'flac', 16, 44100, 2000000, 'Lab 3.flac'],
  ['flac', 'flac', 16, 44100, 1411000, 'Lab 4 [Hi-Res].flac'],   // named Hi-Res, really CD quality
  ['flac', 'flac', 24, 44100, 1900000, 'Lab 5.flac'],
  ['flac', 'flac', 16, 48000, 1300000, 'Lab 6.flac'],            // the one whose first Jellyfin item request fails (HTTP 500 once)
  ['mp3', 'mp3', 0, 44100, 320000, 'Lab 7.mp3'],
  ['flac', 'flac', 24, 192000, 5000000, 'Lab 8.flac'],
];
const lab = labSpec.map((s, i) => ({
  Id: `h${i + 1}`,
  Name: s[5].replace(/\.\w+$/, ''),
  Artists: ['Hires Lab'],
  AlbumArtist: 'Hires Lab',
  Album: 'Lab Album',
  AlbumId: 'alLab',
  IndexNumber: i + 1,
  ParentIndexNumber: 1,
  RunTimeTicks: 120 * 10_000_000,
  ProductionYear: 2020,
  Path: `/music/Hires Lab/Lab Album/${s[5]}`,
  Genres: ['Electronic'],
  DateCreated: new Date(Date.UTC(2025, 0, 1 + i)).toISOString(),
  ImageTags: { Primary: 'x' },
  MediaType: 'Audio',
}));
songs.push(...lab);
const labItem = (x) => {
  const s = labSpec[Number(x.Id.slice(1)) - 1];
  const stream = { Type: 'Audio', Codec: s[0], Channels: 2, BitRate: s[4], SampleRate: s[3] };
  if (s[2] > 0) stream.BitDepth = s[2];
  return { ...x, MediaSources: [{ Container: s[1], Path: x.Path, Size: 5_000_000, Bitrate: s[4], RunTimeTicks: x.RunTimeTicks, MediaStreams: [stream] }] };
};
// a tiny silent WAV for the lab songs (the browser plays it whatever the "format" claims)
const wav = (() => {
  const rate = 8000, n = rate * 3, b = Buffer.alloc(44 + n * 2);
  b.write('RIFF', 0); b.writeUInt32LE(36 + n * 2, 4); b.write('WAVEfmt ', 8); b.writeUInt32LE(16, 16); b.writeUInt16LE(1, 20); b.writeUInt16LE(1, 22);
  b.writeUInt32LE(rate, 24); b.writeUInt32LE(rate * 2, 28); b.writeUInt16LE(2, 32); b.writeUInt16LE(16, 34); b.write('data', 36); b.writeUInt32LE(n * 2, 40);
  return b;
})();
let flakyFailed = false;
const counts = { usersMe: 0, items: 0, item: 0, stream: 0, image: 0 };
// the library listing carries MediaSources/MediaStreams when asked (Fields=MediaSources,MediaStreams), like Jellyfin: plain songs are CD-quality FLAC.
// The Hi-Res lab songs are left out on purpose, so 1e-hires.spec.ts keeps exercising the per-song read (with its one HTTP 500).
const withSources = (x) => (x.Id.startsWith('s') && x.Id !== 's1'
  ? { ...x, MediaSources: [{ Container: 'flac', Path: x.Path, Size: 20_000_000, Bitrate: 1_000_000, RunTimeTicks: x.RunTimeTicks, MediaStreams: [{ Type: 'Audio', Codec: 'flac', Channels: 2, BitRate: 1_000_000, SampleRate: 44100, BitDepth: 16 }] }] }
  : x);
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==', 'base64');
const me = { Id: 'u1', Name: 'tester', Policy: { IsAdministrator: true } };
let prefs = { Id: 'ipodplayer', Client: 'ipodplayer', CustomPrefs: {} };

const json = (res, code, body) => {
  res.writeHead(code, { 'content-type': 'application/json' });
  res.end(body === undefined ? '' : JSON.stringify(body));
};
const readBody = (req) =>
  new Promise((resolve) => {
    let b = '';
    req.on('data', (c) => (b += c));
    req.on('end', () => resolve(b));
  });

http
  .createServer(async (req, res) => {
    const u = new URL(req.url, 'http://x');
    const p = u.pathname, q = u.searchParams, m = req.method;
    // request counter for the load tests: GET /__counts (totals by kind since the last POST /__reset). Not part of Jellyfin; no auth.
    if (p === '/__counts') return json(res, 200, counts);
    if (p === '/__reset') { for (const k of Object.keys(counts)) counts[k] = 0; return json(res, 200, counts); }
    if (p === '/Users/Me') counts.usersMe++;
    else if (/^\/Users\/u1\/Items\/[^/]+$/.test(p)) counts.item++;
    else if (p === '/Users/u1/Items') counts.items++;
    else if (/^\/Audio\/[^/]+\/stream/.test(p)) counts.stream++;
    else if (/^\/Items\/[^/]+\/Images\//.test(p)) counts.image++;
    if (p === '/System/Info/Public') return json(res, 200, { ServerName: 'Mock Jellyfin', Version: '10.10.0', Id: 'mock' });
    if (p === '/Users/AuthenticateByName' && m === 'POST') {
      const b = JSON.parse((await readBody(req)) || '{}');
      // 'revoked' signs in fine but its token is refused from then on (a token Jellyfin has dropped)
      if (b.Username === 'revoked' && b.Pw === 'pw') return json(res, 200, { User: { ...me, Name: 'revoked' }, AccessToken: 'dead-token' });
      return b.Username === 'tester' && b.Pw === 'pw' ? json(res, 200, { User: me, AccessToken: 'mock-token' }) : json(res, 401, { error: 'bad' });
    }
    if (!(req.headers.authorization || '').includes('Token="mock-token"')) return json(res, 401, {});
    if (p === '/QuickConnect/Initiate') return json(res, 401, {});
    if (p === '/Users/Me') return json(res, 200, me);
    if (p.startsWith('/DisplayPreferences/')) {
      if (m === 'POST') {
        prefs = JSON.parse((await readBody(req)) || '{}');
        return json(res, 204);
      }
      return json(res, 200, prefs);
    }
    // one lab song's own item (its streams): the 6th fails once with HTTP 500, like a Jellyfin that hiccups during a burst of skips
    const itemM = /^\/Users\/u1\/Items\/(h[1-8])$/.exec(p);
    if (itemM) {
      if (itemM[1] === 'h6' && !flakyFailed) { flakyFailed = true; return json(res, 500, { error: 'hiccup' }); }
      return json(res, 200, labItem(songs.find((x) => x.Id === itemM[1])));
    }
    if (/^\/Audio\/(h[1-8]|s([2-9]|[1-9]\d))\/stream/.test(p)) {
      const mm = /bytes=(\d*)-(\d*)/.exec(req.headers.range || '');
      const from = mm && mm[1] !== '' ? +mm[1] : 0, to = mm && mm[2] !== '' ? Math.min(+mm[2], wav.length - 1) : wav.length - 1;
      res.writeHead(mm ? 206 : 200, { 'content-type': 'audio/wav', 'accept-ranges': 'bytes', 'content-length': to - from + 1, ...(mm ? { 'content-range': `bytes ${from}-${to}/${wav.length}` } : {}) });
      return res.end(wav.subarray(from, to + 1));
    }
    if (p === '/Users/u1/Items') {
      // what this account has played in any Jellyfin app: a few songs by Aurora Vale, played a lot
      if (q.get('Filters') === 'IsPlayed') {
        const played = songs.filter((x) => ['s2', 's3', 's5'].includes(x.Id)).map((x, i) => ({ ...x, UserData: { PlayCount: 9 - i, LastPlayedDate: new Date(Date.now() - (20 + i) * 86400000).toISOString(), IsFavorite: false } }));
        return json(res, 200, { Items: played, TotalRecordCount: played.length });
      }
      if (q.get('Filters') === 'IsFavorite') return json(res, 200, { Items: [], TotalRecordCount: 0 });
      if (q.get('IncludeItemTypes') === 'Audio') {
        const start = Number(q.get('StartIndex') || 0), limit = Number(q.get('Limit') || 1500);
        const page = songs.slice(start, start + limit);
        return json(res, 200, { Items: (q.get('Fields') || '').includes('MediaStreams') ? page.map(withSources) : page, TotalRecordCount: songs.length, StartIndex: start });
      }
      return json(res, 200, { Items: [], TotalRecordCount: 0 });
    }
    if (p === '/Audio/s1/stream') {
      // byte ranges, like Jellyfin's static stream
      const m = /bytes=(\d*)-(\d*)/.exec(req.headers.range || '');
      const total = alacFile.length;
      const from = m && m[1] !== '' ? +m[1] : 0, to = m && m[2] !== '' ? Math.min(+m[2], total - 1) : total - 1;
      res.writeHead(m ? 206 : 200, { 'content-type': 'audio/mp4', 'accept-ranges': 'bytes', 'content-length': to - from + 1, ...(m ? { 'content-range': `bytes ${from}-${to}/${total}` } : {}) });
      return res.end(alacFile.subarray(from, to + 1));
    }
    if (/^\/Items\/[^/]+\/Images\//.test(p)) {
      res.writeHead(200, { 'content-type': 'image/png' });
      return res.end(png);
    }
    if (m === 'GET') return json(res, 200, { Items: [], TotalRecordCount: 0 });
    await readBody(req);
    return json(res, 204);
  })
  .on('upgrade', (req, sock) => sock.destroy())
  .listen(PORT, '127.0.0.1', () => console.log(`mock jellyfin on ${PORT}`));
