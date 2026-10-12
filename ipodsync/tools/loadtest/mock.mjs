// A configurable stand-in for a big Jellyfin, for load tests of FLACie (web server, Windows app, phones).
//
//   node mock.mjs [--port 8196] [--host 0.0.0.0] [--items 10000] [--latency 0] [--jitter 0] [--flaky 0] [--ratelimit 0]
//
//   --latency ms   fixed delay before every answer          --jitter ms   plus a random 0..jitter
//   --flaky p      fraction (0..1) answered 503             --ratelimit n  more than n requests in one second get 429 + Retry-After: 1
//   --capacity n   the server works on at most n requests at once; the rest wait their turn (a small, busy Jellyfin: more parallel
//                  requests do not finish sooner, they only wait longer). 0 = unlimited.
//   --streamms ms  an audio answer takes this long to send (so a stream the client abandons is seen as aborted, not as complete)
//   --wavsec 3     length of every song's audio in seconds (3; use 180 to test seeking)  --nopic 1  three songs the server has no picture for at all
//   --album-art 1  tracks also carry their album's cover (AlbumId + AlbumPrimaryImageTag), as a Jellyfin with embedded art does (default 1)
// The same switches can be changed while it runs:  GET /__config?latency=300&jitter=100&flaky=0.1&ratelimit=40
// Account: tester / pw (administrator), token "mock-token". Library: user id "u1", 10,000 audio items by default.
//   GET /__stats   requests by kind, per-second peak, in flight now and peak, image variants, streams opened/aborted, since the last /__reset
//   GET /__profile?name=fast|slow|flaky|ratelimit   set the four switches at once (slow = 300 ms, flaky = 10 % 5xx, ratelimit = 20 req/s)
//   GET /__reset   zero the counters           GET /__touch?n=5   change n songs (DateLastSaved = now) and tell sockets LibraryChanged
//   GET /__delete?n=3   remove n songs and tell sockets LibraryChanged   GET /__event?type=UserDataChanged   push any socket message
// Listens on 0.0.0.0 by default so phones on the LAN can use it: point them at http://<this-pc-ip>:8196
import http from 'node:http';
import crypto from 'node:crypto';

const arg = (name, def) => { const i = process.argv.indexOf('--' + name); return i > 0 ? process.argv[i + 1] : (process.env[name.toUpperCase()] ?? def); };
const PORT = Number(arg('port', 8196)), HOST = arg('host', '0.0.0.0'), N = Number(arg('items', 10000));
const cfg = { latency: Number(arg('latency', 0)), jitter: Number(arg('jitter', 0)), flaky: Number(arg('flaky', 0)), ratelimit: Number(arg('ratelimit', 0)), capacity: Number(arg('capacity', 0)), streamms: Number(arg('streamms', 0)) };
const ALBUM_ART = Number(arg('album-art', 1)) !== 0;
const PROFILES = { fast: { latency: 0, jitter: 0, flaky: 0, ratelimit: 0, capacity: 0 }, slow: { latency: 300, jitter: 100, flaky: 0, ratelimit: 0, capacity: 8 },
  flaky: { latency: 30, jitter: 20, flaky: 0.1, ratelimit: 0, capacity: 0 }, ratelimit: { latency: 30, jitter: 20, flaky: 0, ratelimit: 20, capacity: 0 } };

// ---- the library: deterministic, realistic streams (CD FLAC, hi-res FLAC, MP3, AAC) ----
const artists = Array.from({ length: 400 }, (_, i) => `Artist ${String(i + 1).padStart(3, '0')}`);
const kinds = [
  ['flac', 'flac', 16, 44100, 950000], ['flac', 'flac', 16, 44100, 1010000], ['flac', 'flac', 16, 44100, 880000], ['flac', 'flac', 16, 44100, 1100000], ['flac', 'flac', 16, 44100, 990000],
  ['flac', 'flac', 24, 96000, 2900000], ['flac', 'flac', 24, 48000, 1700000], ['mp3', 'mp3', 0, 44100, 320000], ['mp3', 'mp3', 0, 44100, 256000], ['aac', 'm4a', 0, 44100, 256000],
];
const hex = (i) => (0xa0000000 + i).toString(16).padStart(8, '0').repeat(4);   // 32 hex chars, like Jellyfin's ids
const songs = [];
const base = Date.UTC(2024, 0, 1);
for (let i = 0; i < N; i++) {
  const k = kinds[i % kinds.length], a = artists[i % artists.length], al = Math.floor(i / 12);
  const id = hex(i), ext = k[1];
  songs.push({
    Id: id, Name: `Song ${i + 1}`, Artists: [a], AlbumArtist: a, Album: `${a} Album ${(al % 5) + 1}`, AlbumId: hex(1_000_000 + (al % (artists.length * 5))),
    IndexNumber: (i % 12) + 1, ParentIndexNumber: 1, RunTimeTicks: (140 + (i % 200)) * 10_000_000, ProductionYear: 1980 + (i % 44),
    Path: `/music/${a}/Album ${(al % 5) + 1}/${String((i % 12) + 1).padStart(2, '0')} Song ${i + 1}.${ext}`, Genres: [['Rock', 'Jazz', 'Electronic', 'Pop', 'Classical'][i % 5]],
    DateCreated: new Date(base + i * 60_000).toISOString(), DateLastSaved: new Date(base + i * 60_000).toISOString(),
    ImageTags: { Primary: 'tag' + (i % 997) }, ...(ALBUM_ART ? { AlbumPrimaryImageTag: 'alb' + (al % (artists.length * 5)) } : {}), MediaType: 'Audio', Type: 'Audio', _kind: k,
  });
}
// songs the server has no picture for at all (not for the song, not for its album): the app must look one up in the public catalog, one lookup per album (--nopic 0 turns them off)
if (Number(arg('nopic', 1)) !== 0) for (let j = 0; j < 3; j++) {
  const i = N + j, id = hex(i), k = kinds[0];
  songs.push({ Id: id, Name: ['Out of Space', 'Charly', 'Fire'][j], Artists: ['The Prodigy'], AlbumArtist: 'The Prodigy', Album: 'Experience', AlbumId: hex(2_000_000), IndexNumber: j + 1, ParentIndexNumber: 1,
    RunTimeTicks: 150 * 10_000_000, ProductionYear: 1992, Path: `/music/The Prodigy/Experience/0${j + 1} ${['Out of Space', 'Charly', 'Fire'][j]}.flac`, Genres: ['Electronic'],
    DateCreated: new Date(base + i * 60_000).toISOString(), DateLastSaved: new Date(base + i * 60_000).toISOString(), ImageTags: {}, MediaType: 'Audio', Type: 'Audio', _kind: k, _nopic: true });
}
const noPic = new Set(songs.filter((x) => x._nopic).flatMap((x) => [x.Id, x.AlbumId]));
const sources = (x) => ({ ...strip(x), MediaSources: [{ Container: x._kind[1], Path: x.Path, Size: 20_000_000, Bitrate: x._kind[4], RunTimeTicks: x.RunTimeTicks,
  MediaStreams: [{ Type: 'Audio', Codec: x._kind[0], Channels: 2, BitRate: x._kind[4], SampleRate: x._kind[3], ...(x._kind[2] ? { BitDepth: x._kind[2] } : {}) }] }] });
const strip = (x) => { const { _kind, _nopic, ...r } = x; return r; };

const wav = (() => {
  const rate = 8000, n = rate * Number(arg('wavsec', 3)), b = Buffer.alloc(44 + n * 2);
  b.write('RIFF', 0); b.writeUInt32LE(36 + n * 2, 4); b.write('WAVEfmt ', 8); b.writeUInt32LE(16, 16); b.writeUInt16LE(1, 20); b.writeUInt16LE(1, 22);
  b.writeUInt32LE(rate, 24); b.writeUInt32LE(rate * 2, 28); b.writeUInt16LE(2, 32); b.writeUInt16LE(16, 34); b.write('data', 36); b.writeUInt32LE(n * 2, 40);
  return b;
})();
// a picture Android's decoder accepts (a 1x1 PNG, sent for every size and format: clients only sniff it); the earlier hand-made 1x1 JPEG did not decode, which a cover test would show as "no cover"
const jpeg = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64');
const me = { Id: 'u1', Name: 'tester', Policy: { IsAdministrator: true } };
let prefs = { Id: 'ipodplayer', Client: 'ipodplayer', CustomPrefs: {} };

// ---- stats ----
const newStats = () => ({ total: 0, byKind: {}, inFlight: 0, peakInFlight: 0, peakPerSecond: 0, throttled: 0, failed: 0, since: Date.now(), imageVariants: {}, imageUrls: 0, imageRepeats: 0, notModified: 0,
  streamsOpened: 0, streamsAborted: 0, streamsCompleted: 0, itemsPages: 0, itemsDeltas: 0, itemsWithStreams: 0, latencyMsSum: 0, latencyN: 0, maxLatencyMs: 0 });
const stats = newStats();
const imageSeen = new Map();   // url -> times asked
const perSec = new Map();
const kindOf = (p) => (p === '/Users/Me' ? 'users/me' : /^\/Items\/[^/]+\/Images\//.test(p) ? 'image' : /^\/Audio\//.test(p) ? 'stream' : /^\/Users\/[^/]+\/Items\/[^/]+$/.test(p) ? 'info' : /^\/Users\/[^/]+\/Items$/.test(p) ? 'items' : 'other');
const resetStats = () => { Object.assign(stats, newStats(), { inFlight: stats.inFlight }); perSec.clear(); imageSeen.clear(); };
const note = (kind) => {
  stats.total++; stats.byKind[kind] = (stats.byKind[kind] ?? 0) + 1;
  const s = Math.floor(Date.now() / 1000); const n = (perSec.get(s) ?? 0) + 1; perSec.set(s, n);
  if (perSec.size > 120) perSec.delete([...perSec.keys()][0]);
  stats.peakPerSecond = Math.max(stats.peakPerSecond, n);
};

const qc = new Map();   // quick connect secrets
const waiting = []; let working = 0;
const takeSlot = (cap) => new Promise((resolve) => { if (working < cap) { working++; resolve(); } else waiting.push(resolve); });
function releaseSlot() { if (cfg.capacity <= 0) return; const next = waiting.shift(); if (next) next(); else working = Math.max(0, working - 1); }

const sockets = new Set();
const frame = (text) => { const p = Buffer.from(text); const h = p.length < 126 ? Buffer.from([0x81, p.length]) : Buffer.from([0x81, 126, p.length >> 8, p.length & 255]); return Buffer.concat([h, p]); };
const push = (type, data = {}) => { for (const s of sockets) try { s.write(frame(JSON.stringify({ MessageType: type, Data: data }))); } catch { } };

const json = (res, code, body) => { res.writeHead(code, { 'content-type': 'application/json' }); res.end(body === undefined ? '' : JSON.stringify(body)); };
const readBody = (req) => new Promise((r) => { let b = ''; req.on('data', (c) => (b += c)); req.on('end', () => r(b)); });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const authed = (req, u) => /Token="mock-token"/.test(req.headers.authorization || '') || req.headers['x-emby-token'] === 'mock-token' || u.searchParams.get('ApiKey') === 'mock-token' || u.searchParams.get('api_key') === 'mock-token';

http.createServer(async (req, res) => {
  const u = new URL(req.url, 'http://x'), p = u.pathname, q = u.searchParams, m = req.method;
  if (p === '/__stats') return json(res, 200, { ...stats, config: cfg, seconds: (Date.now() - stats.since) / 1000, sockets: sockets.size, libraryCount: songs.length,
    avgLatencyMs: stats.latencyN ? Math.round(stats.latencyMsSum / stats.latencyN) : 0 });
  if (p === '/__reset') { resetStats(); return json(res, 200, { ok: true }); }
  if (p === '/__config') { for (const k of Object.keys(cfg)) if (q.has(k)) cfg[k] = Number(q.get(k)); return json(res, 200, cfg); }
  if (p === '/__profile') { Object.assign(cfg, PROFILES[q.get('name')] ?? PROFILES.fast); return json(res, 200, cfg); }
  if (p === '/__touch') { const n = Number(q.get('n') || 1), now = new Date().toISOString(); for (let i = 0; i < n; i++) songs[(i * 7) % songs.length].DateLastSaved = now; push('LibraryChanged', { ItemsUpdated: songs.slice(0, n).map((s) => s.Id) }); return json(res, 200, { touched: n }); }
  if (p === '/__delete') { const n = Number(q.get('n') || 1); const gone = songs.splice(songs.length - n, n); push('LibraryChanged', { ItemsRemoved: gone.map((s) => s.Id) }); return json(res, 200, { removed: n, left: songs.length }); }
  if (p === '/__event') { push(q.get('type') || 'UserDataChanged'); return json(res, 200, { sockets: sockets.size }); }

  const kind = kindOf(p);
  note(kind);
  const t0 = Date.now();
  stats.inFlight++; stats.peakInFlight = Math.max(stats.peakInFlight, stats.inFlight);
  let done = false;   // the answer was fully sent
  res.on('finish', () => { done = true; const ms = Date.now() - t0; stats.latencyMsSum += ms; stats.latencyN++; stats.maxLatencyMs = Math.max(stats.maxLatencyMs, ms); });
  res.on('close', () => { stats.inFlight--; if (kind === 'stream') { if (done) stats.streamsCompleted++; else stats.streamsAborted++; } releaseSlot(); });
  if (cfg.ratelimit > 0 && (perSec.get(Math.floor(Date.now() / 1000)) ?? 0) > cfg.ratelimit) { stats.throttled++; res.writeHead(429, { 'retry-after': '1' }); done = true; return res.end(); }
  // a small busy server: only `capacity` requests are worked on at once
  if (cfg.capacity > 0) { await takeSlot(cfg.capacity); }
  if (cfg.latency || cfg.jitter) await sleep(cfg.latency + Math.random() * cfg.jitter);
  const finish = () => { };
  if (cfg.flaky > 0 && Math.random() < cfg.flaky) { stats.failed++; finish(); return json(res, 503, { error: 'flaky' }); }

  // Quick Connect: the mock approves a code as soon as it is asked about (so no password is ever typed on a test device)
  if (p === '/QuickConnect/Enabled') { finish(); res.writeHead(200, { 'content-type': 'application/json' }); return res.end('true'); }
  if (p === '/QuickConnect/Initiate') { const secret = crypto.randomBytes(16).toString('hex'); qc.set(secret, 0); finish(); return json(res, 200, { Authenticated: false, Secret: secret, Code: String(100000 + Math.floor(Math.random() * 899999)), DeviceId: 'mock', AppName: 'ipodplayer' }); }
  if (p === '/QuickConnect/Connect') { const sec = q.get('secret') ?? ''; const n = (qc.get(sec) ?? -1) + 1; if (n > 0) qc.set(sec, n); finish(); return qc.has(sec) ? json(res, 200, { Authenticated: n >= 1, Secret: sec, Code: '000000' }) : json(res, 404, {}); }
  if (p === '/Users/AuthenticateWithQuickConnect' && m === 'POST') { await readBody(req); finish(); return json(res, 200, { User: me, AccessToken: 'mock-token' }); }

  if (p === '/System/Info/Public') return json(res, 200, { ServerName: 'Mock Jellyfin', Version: '10.10.0', Id: 'mock' });
  if (p === '/Users/AuthenticateByName' && m === 'POST') {
    const b = JSON.parse((await readBody(req)) || '{}');
    return b.Username === 'tester' && b.Pw === 'pw' ? json(res, 200, { User: me, AccessToken: 'mock-token' }) : json(res, 401, { error: 'bad' });
  }
  if (!authed(req, u)) return json(res, 401, {});
  if (p === '/Users/Me') return json(res, 200, me);
  if (p.startsWith('/DisplayPreferences/')) { if (m === 'POST') { prefs = JSON.parse((await readBody(req)) || '{}'); return json(res, 204); } return json(res, 200, prefs); }
  if (p === '/Users/u1/Items') {
    if (q.get('Filters') === 'IsPlayed' || q.get('Filters') === 'IsFavorite') return json(res, 200, { Items: [], TotalRecordCount: 0 });
    if (q.get('IncludeItemTypes') !== 'Audio') return json(res, 200, { Items: [], TotalRecordCount: 0 });
    const start = Number(q.get('StartIndex') || 0), limit = Number(q.get('Limit') || 100);
    const since = q.get('MinDateLastSaved');
    const all = since ? songs.filter((s) => s.DateLastSaved > since) : songs;
    const withStreams = /MediaStreams|MediaSources/.test(q.get('Fields') || '');
    stats.itemsPages++; if (since) stats.itemsDeltas++; if (withStreams) stats.itemsWithStreams++;
    const page = all.slice(start, start + limit).map((s) => (withStreams ? sources(s) : strip(s)));
    return json(res, 200, { Items: page, TotalRecordCount: all.length, StartIndex: start });
  }
  let mm = /^\/Users\/u1\/Items\/([0-9a-f]{32})$/.exec(p);
  if (mm) { const s = songs.find((x) => x.Id === mm[1]); return s ? json(res, 200, sources(s)) : json(res, 404, {}); }
  if (/^\/Audio\/[^/]+\/stream/.test(p)) {
    const r = /bytes=(\d*)-(\d*)/.exec(req.headers.range || '');
    const from = r && r[1] !== '' ? +r[1] : 0, to = r && r[2] !== '' ? Math.min(+r[2], wav.length - 1) : wav.length - 1;
    stats.streamsOpened++;
    res.writeHead(r ? 206 : 200, { 'content-type': 'audio/wav', 'accept-ranges': 'bytes', 'content-length': to - from + 1, ...(r ? { 'content-range': `bytes ${from}-${to}/${wav.length}` } : {}) });
    const body = wav.subarray(from, to + 1);
    if (!cfg.streamms || body.length < 8) return res.end(body);
    // sent over streamms in 8 pieces: a client that gives up before the end shows as an aborted stream
    const piece = Math.ceil(body.length / 8); let off = 0;
    const timer = setInterval(() => { if (res.destroyed || res.writableEnded) return clearInterval(timer); res.write(body.subarray(off, off + piece)); off += piece; if (off >= body.length) { clearInterval(timer); res.end(); } }, cfg.streamms / 8);
    res.on('close', () => clearInterval(timer));
    return;
  }
  mm = /^\/Items\/([^/]+)\/Images\/Primary/.exec(p);
  if (mm) {
    const tag = q.get('tag') || 'tag', etag = `"${mm[1].slice(0, 8)}-${tag}"`;
    const variant = `${q.get('maxWidth') || q.get('fillWidth') || 'orig'}px-${(q.get('format') || 'default').toLowerCase()}-q${q.get('quality') || '-'}`;
    stats.imageVariants[variant] = (stats.imageVariants[variant] ?? 0) + 1;
    const seen = (imageSeen.get(req.url) ?? 0) + 1; imageSeen.set(req.url, seen);
    if (seen === 1) stats.imageUrls++; else stats.imageRepeats++;
    if (req.headers['if-none-match'] === etag) { stats.notModified++; res.writeHead(304); return res.end(); }
    res.writeHead(200, { 'content-type': 'image/png', etag, 'cache-control': 'public, max-age=31536000' });
    return res.end(jpeg);
  }
  if (m === 'GET') return json(res, 200, { Items: [], TotalRecordCount: 0 });
  await readBody(req);
  return json(res, 204);
}).on('upgrade', (req, sock) => {
  if (!/^\/socket/.test(req.url)) return sock.destroy();
  const key = req.headers['sec-websocket-key'] || '';
  const accept = crypto.createHash('sha1').update(key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
  sock.write(`HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ${accept}\r\n\r\n`);
  sockets.add(sock); note('socket');
  sock.on('data', () => { /* client frames (pings, keep-alives) are ignored */ });
  sock.on('close', () => sockets.delete(sock)); sock.on('error', () => sockets.delete(sock));
}).listen(PORT, HOST, () => console.log(`mock jellyfin on ${HOST}:${PORT} (${N} items; config ${JSON.stringify(cfg)})`));
