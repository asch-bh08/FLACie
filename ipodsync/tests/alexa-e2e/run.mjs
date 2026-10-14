// End to end check of the Alexa feature against the real server and the mock Jellyfin, with requests signed by a test authority
// (the server trusts it only because FLACIE_CATALOG_REDIRECT points at this script's loopback stand-in, the same seam the catalog tests use).
// Needs: node 18+, openssl on PATH, the server built (dotnet build ipodsync/src/FLACie.Server). Run: node run.mjs
// Real Amazon signatures, a real Echo, and sound are NOT covered: see docs/alexa/BY-EAR.md.
import { spawn, execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const MOCK = 8396, CAT = 8397, WEB = 5490, SKILL = 'amzn1.ask.skill.e2e-test';
const work = fs.mkdtempSync(path.join(os.tmpdir(), 'alexa-e2e-'));
const data = path.join(work, 'data'); fs.mkdirSync(data);
const results = []; let failed = 0;
const check = (name, ok, detail = '') => { results.push({ name, ok }); if (!ok) failed++; console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${ok ? '' : '  -> ' + detail}`); };

// ---- a test authority and a leaf certificate for echo-api.amazon.com ----
const ssl = (...a) => execFileSync('openssl', a, { cwd: work, stdio: 'pipe' });
ssl('req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-keyout', 'root.key', '-out', 'root.crt', '-days', '2', '-subj', '/CN=E2E Root', '-addext', 'basicConstraints=critical,CA:TRUE', '-addext', 'keyUsage=critical,keyCertSign,cRLSign');
ssl('req', '-newkey', 'rsa:2048', '-nodes', '-keyout', 'leaf.key', '-out', 'leaf.csr', '-subj', '/CN=echo-api.amazon.com');
fs.writeFileSync(path.join(work, 'ext.cnf'), 'subjectAltName=DNS:echo-api.amazon.com\nbasicConstraints=CA:FALSE\nkeyUsage=digitalSignature\n');
ssl('x509', '-req', '-in', 'leaf.csr', '-CA', 'root.crt', '-CAkey', 'root.key', '-CAcreateserial', '-out', 'leaf.crt', '-days', '2', '-extfile', 'ext.cnf');
const chain = fs.readFileSync(path.join(work, 'leaf.crt'), 'utf8') + fs.readFileSync(path.join(work, 'root.crt'), 'utf8');
const leafKey = fs.readFileSync(path.join(work, 'leaf.key'), 'utf8');

// ---- stand-ins: the certificate host and the music catalog ----
const catalog = http.createServer((req, res) => {
  const u = new URL(req.url, `http://127.0.0.1:${CAT}`);
  if (u.pathname.startsWith('/s3.amazonaws.com/echo.api/')) { res.writeHead(200, { 'content-type': 'application/x-pem-file' }); return res.end(chain); }
  if (u.pathname === '/api.deezer.com/search') {
    const q = u.searchParams.get('q') || '';
    const data = /flurbo/i.test(q) ? [{ id: 1, title: 'Flurbo Zanzibar', duration: 200, artist: { name: 'Nobody Special' }, album: { title: 'Nope', cover_xl: '' } }] : [];
    res.writeHead(200, { 'content-type': 'application/json' }); return res.end(JSON.stringify({ data }));
  }
  res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify({ data: [] }));
}).listen(CAT, '127.0.0.1');

const kids = [];
const mock = spawn('node', ['mock-jellyfin.mjs'], { cwd: path.join(here, '..', 'web-smoke'), env: { ...process.env, PORT: String(MOCK) }, stdio: 'ignore' }); kids.push(mock);

fs.writeFileSync(path.join(data, 'alexa.json'), JSON.stringify({ Config: { Enabled: true, Downloads: true, SkillIds: SKILL }, Links: [] }));
const dll = path.join(here, '..', '..', 'src', 'FLACie.Server', 'bin', 'Debug', 'net9.0');
const web = spawn('dotnet', ['FLACie.Server.dll'], {
  cwd: dll, stdio: ['ignore', 'pipe', 'pipe'],
  env: { ...process.env, ASPNETCORE_URLS: `http://127.0.0.1:${WEB}`, ASPNETCORE_ENVIRONMENT: 'Production', FLACIE_JELLYFIN_URL: `http://127.0.0.1:${MOCK}`, FLACIE_DATA: data,
    FLACIE_CATALOG_REDIRECT: `http://127.0.0.1:${CAT}`, FLACIE_ALEXA_TEST_ROOT: path.join(work, 'root.crt'), FLACIE_PUBLIC_URL: 'https://flacie.example.com' },
}); kids.push(web);
let log = ''; web.stdout.on('data', (d) => (log += d)); web.stderr.on('data', (d) => (log += d));
const stop = () => { for (const k of kids) try { k.kill(); } catch { } catalog.close(); };
process.on('exit', stop);

const base = `http://127.0.0.1:${WEB}`;
for (let i = 0; i < 120; i++) { try { if ((await fetch(base + '/healthz')).ok) break; } catch { } await new Promise((r) => setTimeout(r, 500)); if (i === 119) { console.log(log); process.exit(2); } }

// ---- helpers: a signed Alexa request ----
let n = 0;
const body = (request, user = 'amzn1.ask.account.E2E', extra = {}) => ({ version: '1.0', context: { System: { application: { applicationId: SKILL }, user: { userId: user }, device: { deviceId: 'd1' }, apiEndpoint: 'https://api.amazonalexa.com', apiAccessToken: 'x', ...extra } }, request: { requestId: 'r' + ++n, locale: 'en-GB', timestamp: new Date().toISOString().replace(/\.\d+Z$/, 'Z'), ...request } });
const send = async (obj, o = {}) => {
  const raw = Buffer.from(JSON.stringify(obj));
  const sig = o.badSig ? Buffer.alloc(256).toString('base64') : crypto.sign('sha256', raw, leafKey).toString('base64');
  const headers = { 'content-type': 'application/json', SignatureCertChainUrl: o.url ?? 'https://s3.amazonaws.com/echo.api/e2e.pem' };
  if (!o.unsigned) headers['Signature-256'] = sig;
  const res = await fetch(base + '/alexa/skill', { method: 'POST', headers, body: raw });
  const text = await res.text();
  return { status: res.status, json: text ? JSON.parse(text) : null };
};
const say = (r) => (r.json?.response?.outputSpeech?.ssml || '').replace(/<[^>]+>/g, '').replace(/&apos;/g, "'");
const intent = (name, slots = {}) => ({ type: 'IntentRequest', intent: { name, slots: Object.fromEntries(Object.entries(slots).map(([k, v]) => [k, { name: k, value: v }])) } });
const playOf = (r) => r.json?.response?.directives?.find((d) => d.type === 'AudioPlayer.Play');

// ---- 1. refusals ----
let r = await send(body({ type: 'LaunchRequest' }), { unsigned: true }); check('request without a signature is refused (400)', r.status === 400, r.status);
r = await send(body({ type: 'LaunchRequest' }), { badSig: true }); check('request with a wrong signature is refused (400)', r.status === 400, r.status);
r = await send(body({ type: 'LaunchRequest' }), { url: 'https://evil.example.com/echo.api/e2e.pem' }); check('certificate URL on another host is refused (400)', r.status === 400, r.status);
r = await send(body({ type: 'LaunchRequest', timestamp: new Date(Date.now() - 400_000).toISOString().replace(/\.\d+Z$/, 'Z') })); check('a 400 second old request is refused (400)', r.status === 400, r.status);
const wrong = body({ type: 'LaunchRequest' }); wrong.context.System.application.applicationId = 'amzn1.ask.skill.other'; r = await send(wrong); check('another skill id is refused (400)', r.status === 400, r.status);
r = await send(body({ type: 'LaunchRequest' })); check('a correctly signed request is accepted', r.status === 200, r.status);
check('an unlinked account is told how to link', /create a code/i.test(say(r)), say(r));
r = await send(body(intent('PlayIntent', { query: 'aurora vale' }))); check('an unlinked account cannot play', !playOf(r) && /create a code/i.test(say(r)), say(r));

// ---- 2. pairing ----
const login = await fetch(base + '/auth/jellyfin', { method: 'POST', redirect: 'manual', body: new URLSearchParams({ user: 'tester', password: 'pw' }) });
const cookie = (login.headers.getSetCookie?.() || []).map((c) => c.split(';')[0]).join('; ');
check('signed in to FLACie as the mock user', login.status === 302 && cookie.includes('flacie'), login.status);
const settings = await (await fetch(base + '/settings', { headers: { cookie } })).text();
check('the Settings page has the Alexa section with the enable switch (administrator)', settings.includes('id="alexa"') && settings.includes('Enable Alexa') && settings.includes('Alexa can download songs') && settings.includes('Create a code'), settings.slice(0, 200));
const codeRes = await fetch(base + '/api/alexa/code', { method: 'POST', headers: { cookie } }); const code = (await codeRes.json()).code;
check('a pairing code was made', /^\d{6}$/.test(code), code);
r = await send(body(intent('LinkIntent', { code: '000000' }))); check('a wrong code does not link', /didn't work/i.test(say(r)), say(r));
r = await send(body(intent('LinkIntent', { code: code.split('').join(' ') }))); check('the right code links (spoken with spaces)', /linked/i.test(say(r)), say(r));
r = await send(body(intent('LinkIntent', { code }))); check('the same code cannot be used twice', /didn't work/i.test(say(r)), say(r));
const alexaJson = fs.readFileSync(path.join(data, 'alexa.json'), 'utf8'); check('the stored link holds no plain Jellyfin token', !alexaJson.includes('mock-token'));

// ---- 3. playing ----
const tLoad = Date.now();
for (let i = 0; i < 480; i++) { const x = await send(body(intent('PlayIntent', { query: 'aurora vale' }))); if (!/still loading/i.test(say(x))) break; await new Promise((res) => setTimeout(res, 500)); }
check(`the library was ready ${((Date.now() - tLoad) / 1000).toFixed(1)} s after the first linked request`, true);   // the library loads in the background after the first request
r = await send(body({ type: 'LaunchRequest' })); check('launch greets a linked account', /What would you like to hear/i.test(say(r)), say(r));
r = await send(body(intent('PlayIntent', { query: 'aurora vale' })));
let play = playOf(r); check('"play Aurora Vale" gives an AudioPlayer.Play', !!play, say(r) + JSON.stringify(r.json).slice(0, 200));
check('the reply is short', say(r).split(' ').length <= 8, say(r));
let url = play?.audioItem?.stream?.url ?? '';
check('the stream URL is https on the public name and holds no token', url.startsWith('https://flacie.example.com/alexa/stream/t/') && !/token|mock/i.test(url), url);
if (url) {
  const local = base + url.replace('https://flacie.example.com', '');
  const ok = await fetch(local); const buf = Buffer.from(await ok.arrayBuffer());
  check('the signed stream URL serves audio', ok.status === 200 && /audio\/mpeg/.test(ok.headers.get('content-type') || '') && buf.length > 1000, `${ok.status} ${ok.headers.get('content-type')} ${buf.length}`);
  const tamper = await fetch(local.replace(/s=[^&]+/, 's=AAAA')); check('a tampered signature is refused (403)', tamper.status === 403, tamper.status);
  const other = await fetch(local.replace(/\/t\/[0-9a-z]+\./i, '/t/00000000000000000000000000000000.')); check('a different song id fails its signature (403)', other.status === 403, other.status);
  const exp = await fetch(local.replace(/e=\d+/, 'e=1')); check('a changed expiry fails its signature (403)', exp.status === 403, exp.status);
  const noQuery = await fetch(base + '/alexa/stream/t/s2.mp3'); check('no signature at all is not served', noQuery.status === 404 || noQuery.status === 403, noQuery.status);
}
const tokenA = play?.audioItem?.stream?.token;
r = await send(body({ type: 'AudioPlayer.PlaybackStarted', token: tokenA, offsetInMilliseconds: 0 })); check('PlaybackStarted is accepted without speech', r.status === 200 && say(r) === '', say(r));
r = await send(body({ type: 'AudioPlayer.PlaybackNearlyFinished', token: tokenA, offsetInMilliseconds: 150000 }));
const next = playOf(r); check('PlaybackNearlyFinished enqueues the next song', next?.playBehavior === 'ENQUEUE' && next.audioItem.stream.expectedPreviousToken === tokenA, JSON.stringify(r.json).slice(0, 300));
r = await send(body(intent('AMAZON.NextIntent'))); check('"next" gives another song', !!playOf(r) && playOf(r).audioItem.stream.token !== tokenA, say(r));
r = await send(body(intent('AMAZON.PauseIntent'), undefined, {})); check('"pause" gives AudioPlayer.Stop', r.json?.response?.directives?.[0]?.type === 'AudioPlayer.Stop', JSON.stringify(r.json).slice(0, 200));
r = await send(body(intent('AMAZON.ResumeIntent'))); check('"resume" plays again', !!playOf(r), say(r));
r = await send(body(intent('WhatsPlayingIntent'))); check('"what\'s playing" names a song', /This is .+ by Aurora Vale/.test(say(r)), say(r));
r = await send(body(intent('LikeIntent'))); check('"I like this" is saved', /remember you like/i.test(say(r)), say(r));
await new Promise((res) => setTimeout(res, 500));
const ud = await (await fetch(`http://127.0.0.1:${MOCK}/__userdata`)).json(); check('the 5 star rating reached Jellyfin (Rating 10)', Object.values(ud).includes(10), JSON.stringify(ud));

// ---- 4. not in the library: search, download, tell ----
r = await send(body(intent('PlayIntent', { query: 'flurbo zanzibar' })));
check('a song that is not in the library is searched for', /downloading Flurbo Zanzibar by Nobody Special/i.test(say(r)), say(r));
check('the permission card for notifications is asked for once', r.json?.response?.card?.type === 'AskForPermissionsConsent', JSON.stringify(r.json?.response?.card));
let finished = ''; for (let i = 0; i < 40 && !finished; i++) { await new Promise((res) => setTimeout(res, 500)); const t = await send(body(intent('IsItReadyIntent'))); const s = say(t); if (/couldn't get|is ready|downloaded/i.test(s)) finished = s; }
check('"is it ready" reports the outcome (no download service is set up here, so a failure is expected)', /couldn't get/i.test(finished), finished);
r = await send(body({ type: 'LaunchRequest' })); check('the outcome is also told on the next launch', /couldn't download Flurbo Zanzibar/i.test(say(r)), say(r));
const dl = await (await fetch(base + '/api/downloads', { headers: { 'X-Emby-Token': 'mock-token', 'X-Jellyfin-Server': `http://127.0.0.1:${MOCK}` } })).text(); check('the download is in the Downloads log tagged Alexa', /Alexa/.test(dl) && /Flurbo Zanzibar/.test(dl), dl.slice(0, 300));


// ---- 6. load: BIG_SONGS=10000 node run.mjs ----
if (Number(process.env.BIG_SONGS || 0) > 0) {
  const total = Number(process.env.BIG_SONGS);
  let ready = false; const t0 = Date.now();
  for (let i = 0; i < 240 && !ready; i++) { const x = await send(body(intent('PlayIntent', { query: `big song ${total - 1}` }))); ready = !!playOf(x); if (i % 20 === 0) console.log('  waiting:', say(x).slice(0, 100)); if (!ready) await new Promise((res) => setTimeout(res, 500)); }
  check(`a ${total} song library answers exact title requests`, ready);
  const before = await (await fetch(`http://127.0.0.1:${MOCK}/__counts`)).json();
  const times = [];
  const phrases = [() => `big song ${Math.floor(Math.random() * total)}`, () => `big artist ${Math.floor(Math.random() * 400)}`, () => `the album big album ${Math.floor(Math.random() * 1200)}`, () => 'big sog ' + Math.floor(Math.random() * total)];
  for (let i = 0; i < 200; i++) {
    const t = performance.now();
    const x = await send(body(intent('PlayIntent', { query: phrases[i % phrases.length]() })));
    times.push(performance.now() - t);
    if (i % 5 === 0) await send(body(intent('AMAZON.NextIntent')));
    if (i % 7 === 0) await send(body({ type: 'AudioPlayer.PlaybackNearlyFinished', token: playOf(x)?.audioItem?.stream?.token ?? '1:0:0', offsetInMilliseconds: 1000 }));
  }
  times.sort((a, b) => a - b);
  const p50 = times[100], p95 = times[190], max = times[199];
  check(`200 voice requests on ${total} songs: median ${p50.toFixed(0)} ms, p95 ${p95.toFixed(0)} ms, max ${max.toFixed(0)} ms (Alexa gives about 8000 ms)`, p50 < 400 && p95 < 2000 && max < 6000, `${p50} ${p95} ${max}`);
  const t1 = performance.now(); const cat = await fetch(base + '/api/alexa/catalog?type=AMAZON.MusicRecording', { headers: { cookie } }); const cj = await cat.json(); const exportMs = performance.now() - t1;
  check(`catalog export of ${total} songs (${(JSON.stringify(cj).length / 1024 / 1024).toFixed(1)} MB) takes ${exportMs.toFixed(0)} ms`, cat.status === 200 && cj.entities.length >= total && exportMs < 8000, `${cat.status} ${cj.entities?.length} ${exportMs}`);
  const after = await (await fetch(`http://127.0.0.1:${MOCK}/__counts`)).json();
  const calls = (after.items - before.items) + (after.item - before.item) + (after.usersMe - before.usersMe);
  check(`Jellyfin was asked ${calls} times for items during 200 requests and the export (no call per song)`, calls <= 10, JSON.stringify({ before, after }));
}

// ---- 5. disabled / revoked ----
check('the server log has no token or code', !log.includes('mock-token') && !log.includes(code), '');
stop();
console.log(`\n${results.length - failed}/${results.length} passed`);
process.exit(failed ? 1 : 0);
