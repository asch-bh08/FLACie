// A tiny stand-in for Jellyfin, just enough for FLACie Web to sign in and show a library. Account: tester / pw (an administrator).
import http from 'node:http';

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
        Path: `/music/${artists[a]}/Album ${al + 1}/${String(t).padStart(2, '0')} song ${n}.flac`,
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
      if (q.get('IncludeItemTypes') === 'Audio') {
        const start = Number(q.get('StartIndex') || 0), limit = Number(q.get('Limit') || 1500);
        return json(res, 200, { Items: songs.slice(start, start + limit), TotalRecordCount: songs.length, StartIndex: start });
      }
      return json(res, 200, { Items: [], TotalRecordCount: 0 });
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
