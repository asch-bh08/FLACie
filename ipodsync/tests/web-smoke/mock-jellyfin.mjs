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
    if (p === '/Users/u1/Items') {
      // what this account has played in any Jellyfin app: a few songs by Aurora Vale, played a lot
      if (q.get('Filters') === 'IsPlayed') {
        const played = songs.filter((x) => ['s2', 's3', 's5'].includes(x.Id)).map((x, i) => ({ ...x, UserData: { PlayCount: 9 - i, LastPlayedDate: new Date(Date.now() - (20 + i) * 86400000).toISOString(), IsFavorite: false } }));
        return json(res, 200, { Items: played, TotalRecordCount: played.length });
      }
      if (q.get('Filters') === 'IsFavorite') return json(res, 200, { Items: [], TotalRecordCount: 0 });
      if (q.get('IncludeItemTypes') === 'Audio') {
        const start = Number(q.get('StartIndex') || 0), limit = Number(q.get('Limit') || 1500);
        return json(res, 200, { Items: songs.slice(start, start + limit), TotalRecordCount: songs.length, StartIndex: start });
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
