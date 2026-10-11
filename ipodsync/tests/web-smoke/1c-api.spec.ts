import { test, expect, request as pwRequest, APIRequestContext } from '@playwright/test';

// The HTTP API and the media endpoints, probed directly (no browser): who may call what, malformed input, ranges, path tricks, headers.
// Runs before 2-login.spec.ts, which trips the sign-in throttle for this address on purpose. Makes no failed sign-ins itself.
// The mock Jellyfin (mock-jellyfin.mjs) has one account, tester / pw (an administrator), and song s1 is an Apple Lossless file.

const MOCK = Number(process.env.MOCK_PORT || 8196);
const TOKEN = { 'X-Emby-Token': 'mock-token' };
const S1 = `http://127.0.0.1:${MOCK}/Audio/s1/stream?static=true`;

let web: APIRequestContext;
test.beforeAll(async ({ baseURL }) => {
  web = await pwRequest.newContext({ baseURL });
  const r = await web.post('/auth/jellyfin', { form: { user: 'tester', password: 'pw' }, maxRedirects: 0 });
  expect(r.status()).toBe(302);
  // the library loads on first use: ask once, then wait until the 40 mock songs are in
  await expect(async () => {
    const a = await web.get('/api/account', { headers: TOKEN });
    expect((await a.json()).songs).toBeGreaterThan(0);
  }).toPass({ timeout: 30_000 });
});
test.afterAll(async () => { await web.dispose(); });

test.describe('anonymous callers', () => {
  const calls: [string, string][] = [
    ['GET', '/api/account'], ['GET', '/api/import'], ['POST', '/api/import'], ['GET', '/api/settings'], ['POST', '/api/settings'], ['POST', '/api/charts/run'],
    ['GET', '/api/charts/0'], ['POST', '/api/played'], ['GET', '/api/alac?id=00000000000000000000000000000000'], ['GET', '/api/audiofacts'], ['GET', '/api/downloads'],
    ['POST', '/api/import/abc/cancel'],
  ];
  for (const [m, p] of calls) {
    test(`${m} ${p} is 401 without a token`, async ({ playwright, baseURL }) => {
      const anon = await playwright.request.newContext({ baseURL });
      const r = await anon.fetch(p, { method: m, maxRedirects: 0, data: m === 'POST' ? '{}' : undefined, headers: { 'content-type': 'application/json' } });
      expect(r.status()).toBe(401);
      await anon.dispose();
    });
  }

  test('a made-up token is refused', async ({ playwright, baseURL }) => {
    const anon = await playwright.request.newContext({ baseURL });
    expect((await anon.get('/api/account', { headers: { 'X-Emby-Token': 'bogus' } })).status()).toBe(401);
    await anon.dispose();
  });

  for (const p of ['/stream?p=x', '/art/jfs1', '/api/whoami']) {
    test(`${p} sends a signed-out visitor to the login page`, async ({ playwright, baseURL }) => {
      const anon = await playwright.request.newContext({ baseURL });
      const r = await anon.get(p, { maxRedirects: 0 });
      expect(r.status()).toBe(302);
      expect(r.headers()['location']).toContain('/login');
      await anon.dispose();
    });
  }

  test('the debug endpoints do not exist unless FLACIE_DEBUG is on', async ({ playwright, baseURL }) => {
    const anon = await playwright.request.newContext({ baseURL });
    for (const p of ['/debug/login', '/debug/token', '/debug/library', '/debug/jf?path=/System/Info']) expect([401, 404, 302]).toContain((await anon.get(p, { maxRedirects: 0 })).status());
    await anon.dispose();
  });

  test('wrong methods are 405 and carry no error page', async ({ request }) => {
    expect((await request.put('/api/ping')).status()).toBe(405);
    expect((await request.get('/auth/jellyfin')).status()).toBe(405);
    expect((await request.delete('/api/import')).status()).toBe(405);
    expect((await request.get('/nope-not-here')).status()).toBe(404);
  });

  test('ping says what the server is and nothing else', async ({ request }) => {
    const r = await request.get('/api/ping');
    expect(await r.json()).toEqual({ name: 'FLACie Web', import: true });
  });
});

test.describe('signed in', () => {
  test('the sign-in cookie is HttpOnly and SameSite=Lax', async ({ playwright, baseURL }) => {
    const c = await playwright.request.newContext({ baseURL });
    const r = await c.post('/auth/jellyfin', { form: { user: 'tester', password: 'pw' }, maxRedirects: 0 });
    const cookie = r.headersArray().filter((h) => h.name.toLowerCase() === 'set-cookie').map((h) => h.value).join('\n');
    expect(cookie).toMatch(/^flacie=/m);
    expect(cookie).toMatch(/httponly/i);
    expect(cookie).toMatch(/samesite=lax/i);
    expect(r.headers()['location']).toBe('/');
    await c.dispose();
  });

  test('an external ReturnUrl is never followed after sign-in', async ({ playwright, baseURL }) => {
    const c = await playwright.request.newContext({ baseURL });
    const r = await c.post('/auth/jellyfin?ReturnUrl=https://evil.example/', { form: { user: 'tester', password: 'pw', ReturnUrl: 'https://evil.example/' }, maxRedirects: 0 });
    expect(r.headers()['location']).toBe('/');
    await c.dispose();
  });

  test('security headers are on API answers too', async () => {
    const r = await web.get('/api/account', { headers: TOKEN });
    expect(r.headers()['x-content-type-options']).toBe('nosniff');
    expect(r.headers()['x-frame-options']).toBe('SAMEORIGIN');
  });

  test('the phone-style token header and a Bearer token both work', async ({ playwright, baseURL }) => {
    const anon = await playwright.request.newContext({ baseURL });
    expect((await anon.get('/api/account', { headers: { Authorization: 'Bearer mock-token' } })).status()).toBe(200);
    await anon.dispose();
  });

  test('a token with quotes or commas is refused, it cannot add parameters to the Jellyfin header', async ({ playwright, baseURL }) => {
    const anon = await playwright.request.newContext({ baseURL });
    for (const t of ['a", Token="mock-token', 'x,y', 'a b']) expect((await anon.get('/api/account', { headers: { 'X-Emby-Token': t } })).status()).toBe(401);
    await anon.dispose();
  });

  test('/api/settings: a malformed body or a wrong type is 400 JSON, never a 500 page', async () => {
    for (const body of ['{bad', 'null', '[1,2]', '"text"', '{"prefetch":"x"}', '{"lists":[null]}', '{"lists":"a"}', '{"minFreeGb":1.5}', '{"perList":{}}']) {
      const r = await web.post('/api/settings', { headers: { ...TOKEN, 'content-type': 'application/json' }, data: body });
      expect(r.status(), body).toBe(400);
      expect(r.headers()['content-type'] ?? '').toContain('json');
    }
  });

  test('/api/settings: values are clamped and a good body is applied', async () => {
    const r = await web.post('/api/settings', { headers: { ...TOKEN, 'content-type': 'application/json' }, data: '{"minFreeGb":99999999,"perList":500,"lists":[0,0,999999]}' });
    expect(r.status()).toBe(200);
    const j = await r.json();
    expect(j.minFreeGb).toBe(2000);
    expect(j.charts.perList).toBe(50);
    expect(j.charts.lists).toEqual([0]);
    await web.post('/api/settings', { headers: { ...TOKEN, 'content-type': 'application/json' }, data: '{"minFreeGb":10,"perList":10}' });
  });

  test('/api/downloads clamps its limit and rejects text', async () => {
    expect((await web.get('/api/downloads?limit=-5', { headers: TOKEN })).status()).toBe(200);
    expect((await web.get('/api/downloads?limit=999999', { headers: TOKEN })).status()).toBe(200);
    expect((await web.get('/api/downloads?limit=abc', { headers: TOKEN })).status()).toBe(400);
  });

  test('/api/alac wants a 32 character hex id', async () => {
    for (const id of ['', 'zz', 'g'.repeat(32), '0'.repeat(31), '0'.repeat(33)]) expect((await web.get('/api/alac?id=' + id, { headers: TOKEN })).status()).toBe(400);
  });

  test('/api/charts/{id} only takes a number', async () => {
    expect((await web.get('/api/charts/abc', { headers: TOKEN })).status()).toBe(404);
  });

  test.describe('/api/played', () => {
    const post = (data: unknown) => web.post('/api/played', { headers: { ...TOKEN, 'content-type': 'application/json' }, data });
    test('accepts a start, a progress and a stop', async () => {
      for (const event of ['start', 'progress', 'stop']) expect((await post({ event, key: 'k1', title: 'T', artist: 'A', via: 'NAS', device: 'phone' })).status()).toBe(204);
    });
    test('refuses an unknown event, an unknown via, a missing or over-long key, and a body that is not JSON', async () => {
      expect((await post({ event: 'bogus', key: 'k', via: 'NAS' })).status()).toBe(400);
      expect((await post({ event: 'start', key: 'k', via: 'evil' })).status()).toBe(400);
      expect((await post({ event: 'start', via: 'NAS' })).status()).toBe(400);
      expect((await post({ event: 'start', key: 'k'.repeat(601), via: 'NAS' })).status()).toBe(400);
      expect((await post({ key: 'k', via: 'NAS' })).status()).toBe(400);
      const r = await web.post('/api/played', { headers: { ...TOKEN, 'content-type': 'application/json' }, data: 'notjson' });
      expect(r.status()).toBe(400);
    });
    test('very long titles, artists and device names are accepted and cut, not rejected', async () => {
      expect((await post({ event: 'start', key: 'k2', title: 'T'.repeat(5000), artist: 'A'.repeat(5000), via: 'direct', device: 'D'.repeat(5000) })).status()).toBe(204);
    });
  });

  test('/api/import: a path in the name is reduced to the file name; empty and unreadable bodies are 400', async () => {
    const ok = await web.post('/api/import?name=' + encodeURIComponent('../../etc/evil.m3u'), { headers: { ...TOKEN, 'content-type': 'text/plain' }, data: 'Nobody Known - Nothing Here One\n' });
    expect(ok.status()).toBe(200);
    const j = await ok.json();
    expect(j.file).toBe('evil.m3u');
    expect((await web.post('/api/import', { headers: { ...TOKEN, 'content-type': 'text/plain' }, data: '' })).status()).toBe(400);
    expect((await web.post('/api/import', { headers: { ...TOKEN, 'content-type': 'text/plain' }, data: '\u0000\u0001\u0002' })).status()).toBe(400);
    // an unknown import id is a harmless no-op, not an error and not somebody else's import
    expect((await web.post('/api/import/not-a-real-id/cancel', { headers: TOKEN })).status()).toBe(200);
  });

  test('/api/import: a body over 20 MB is refused with 413/400, with and without a length', async () => {
    const big = Buffer.alloc(21 * 1024 * 1024, 'a');
    const r = await web.post('/api/import?name=big.txt', { headers: { ...TOKEN, 'content-type': 'text/plain' }, data: big });
    expect(r.status()).toBe(400);
  });
});

test.describe('/stream', () => {
  const stream = (range?: string) => web.get('/stream?p=' + encodeURIComponent(S1), { headers: range ? { range } : {} });

  test('a song of this library plays, whole and by range', async () => {
    const whole = await stream();
    expect(whole.status()).toBe(200);
    expect(whole.headers()['content-type']).toBe('audio/wav');
    expect(whole.headers()['accept-ranges']).toBe('bytes');
    const total = Number(whole.headers()['content-length']);
    const part = await stream('bytes=0-9');
    expect(part.status()).toBe(206);
    expect(part.headers()['content-range']).toBe(`bytes 0-9/${total}`);
    expect((await part.body()).length).toBe(10);
    const tail = await stream('bytes=-5');
    expect(tail.status()).toBe(206);
    expect(tail.headers()['content-range']).toBe(`bytes ${total - 5}-${total - 1}/${total}`);
  });

  test('a range past the end is 416 with the size, an inverted or unknown range is the whole song', async () => {
    const past = await stream('bytes=999999999-');
    expect(past.status()).toBe(416);
    expect(past.headers()['content-range']).toMatch(/^bytes \*\/\d+$/);
    expect((await stream('bytes=-0')).status()).toBe(416);
    expect((await stream('bytes=5-3')).status()).toBe(200);
    expect((await stream('garbage')).status()).toBe(200);
    expect((await stream('items=0-1')).status()).toBe(200);
  });

  test('only songs of the signed-in library are served: paths, schemes and other servers are 404', async () => {
    for (const p of ['file:///etc/passwd', '../../etc/passwd', '/etc/passwd', 'http://127.0.0.1:1/x', `http://127.0.0.1:${MOCK}/Audio/s1/stream`, '']) {
      const r = await web.get('/stream?p=' + encodeURIComponent(p));
      expect(r.status(), p).toBe(404);
    }
    expect((await web.get('/stream')).status()).toBe(400);
  });
});

test.describe('/art', () => {
  test('a Jellyfin cover is served and cached', async () => {
    const r = await web.get('/art/jfs1');
    expect(r.status()).toBe(200);
    expect(r.headers()['content-type']).toBe('image/jpeg');
    expect(r.headers()['cache-control']).toContain('max-age=604800');
  });

  test('keys with separators, dots or absurd length are 400', async () => {
    for (const k of ['..%2f..%2fetc%2fpasswd', '..', 'a%5Cb', 'a'.repeat(401)]) expect((await web.get('/art/' + k)).status(), k).toBe(400);
  });

  test('keys that are not real covers are a quick 404, never a 500', async () => {
    for (const k of ['nf!!!', 'it!!', 'ex!!', 'jf', 'zz', 'exaHR0cDovLzEyNy4wLjAuMTo4MTk2L3g']) {
      const r = await web.get('/art/' + k);
      expect([404, 200], k).toContain(r.status());
      expect(r.status(), k).not.toBe(500);
    }
  });

  test('an external cover address outside the public cover hosts is never fetched', async () => {
    const key = 'ex' + Buffer.from('http://169.254.169.254/latest/meta-data').toString('base64url');
    const r = await web.get('/art/' + key);
    expect(r.status()).toBe(404);
  });
});

test.describe('sign-in form edge cases', () => {
  test('missing fields go back to the form with a message, not an error page', async ({ request }) => {
    const r = await request.post('/auth/jellyfin', { form: { user: '' }, maxRedirects: 0 });
    expect(r.status()).toBe(302);
    expect(r.headers()['location']).toContain('/login?error=');
  });

  test('logging out without a session is a redirect to the login page', async ({ request }) => {
    const r = await request.post('/auth/logout', { maxRedirects: 0 });
    expect(r.status()).toBe(302);
    expect(r.headers()['location']).toBe('/login');
  });

  test('the error page tells nothing about the server', async ({ request }) => {
    const r = await request.get('/error');
    const t = await r.text();
    expect(t).not.toMatch(/Exception|StackTrace|at FLACie|\.cs:line|Microsoft\./);
  });
});
