import { test, expect } from '@playwright/test';

// Signed-out behaviour and the sign-in protections. The last test trips the login throttle on purpose.

test('health check', async ({ request }) => {
  const r = await request.get('/healthz');
  expect(r.ok()).toBeTruthy();
  expect(await r.text()).toContain('ok');
});

test('security headers are set', async ({ request }) => {
  const r = await request.get('/login');
  expect(r.headers()['x-content-type-options']).toBe('nosniff');
  expect(r.headers()['x-frame-options']).toBe('SAMEORIGIN');
  expect(r.headers()['referrer-policy']).toBe('same-origin');
  // a self-hosted server stays out of search results, and no other site may frame it
  expect(r.headers()['x-robots-tag']).toBe('noindex, nofollow');
  expect(r.headers()['content-security-policy']).toContain("frame-ancestors 'self'");
  expect((await request.get('/healthz')).headers()['x-robots-tag']).toBe('noindex, nofollow');
});

test('robots are told to keep out', async ({ request }) => {
  const r = await request.get('/robots.txt');
  expect(r.ok()).toBeTruthy();
  expect(await r.text()).toBe('User-agent: *\nDisallow: /\n');
});

test('an absurdly large request is refused before it is read', async ({ request }) => {
  // 3 MB of JSON at an endpoint that takes a few hundred kilobytes at most: the server stops reading at 2 MB. It answers 413 and closes the connection, so
  // a client still writing the rest sees the connection reset before it can read the answer: either way the body was refused, not read
  const outcome = await request.post('/api/listen', { data: '{"events":[' + '"x",'.repeat(800_000) + '"x"]}', headers: { 'content-type': 'application/json' } })
    .then((r) => String(r.status()), (e) => String(e));
  expect(outcome).toMatch(/^413$|ECONNRESET|EPIPE|socket hang up/);
  // and the server is fine afterwards
  expect((await request.get('/healthz')).ok()).toBeTruthy();
});

test('a signed-out visitor is sent to the login page', async ({ page }) => {
  await page.goto('/songs');
  await expect(page).toHaveURL(/\/login/);
  await expect(page.locator('#jf-form')).toBeVisible();
});

test('the sign-in page offers no NAS tab unless the host turns it on', async ({ page }) => {
  await page.goto('/login');
  await expect(page.getByRole('tab', { name: 'NAS' })).toHaveCount(0);
});

test('a wrong password is refused with a message', async ({ page }) => {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('nope');
  await page.locator('#jf-form button[type="submit"]').click();
  await expect(page.getByText(/wrong username or password/i)).toBeVisible();
});

test('NAS sign-in is refused while it is off', async ({ request }) => {
  const r = await request.post('/auth/nas', { form: { host: 'nas.local', share: 'Music' }, maxRedirects: 0 });
  expect(r.status()).toBe(302);
  expect(decodeURIComponent(r.headers()['location'])).toMatch(/turned off/i);
});

test('the admin pages are closed to a signed-out visitor', async ({ request }) => {
  for (const path of ['/api/admin/settings', '/dashboard']) {
    const r = await request.get(path, { maxRedirects: 0 });
    expect([301, 302, 307, 401, 403, 404]).toContain(r.status());
  }
});

test('guessing at a user name never locks that user out', async ({ page, request }) => {
  for (let i = 0; i < 6; i++) await request.post('/auth/jellyfin', { form: { user: 'tester', password: 'guess' + i }, maxRedirects: 0 });
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30_000 });
});

test('repeated wrong passwords are throttled', async ({ request }) => {
  let last = '';
  for (let i = 0; i < 12; i++) {
    const r = await request.post('/auth/jellyfin', { form: { user: 'someone-else', password: 'wrong' + i }, maxRedirects: 0 });
    last = decodeURIComponent(r.headers()['location'] ?? '');
  }
  expect(last).toMatch(/too many failed attempts/i);
});

// (last: it leaves "tester" guessed at from this address for ten minutes)
test('a browser that signed in before is not held up by someone guessing at its name', async ({ page, request }) => {
  const signIn = async () => {
    await page.goto('/login');
    await page.locator('#jf-form summary').click();
    await page.locator('#user').fill('tester');
    await page.locator('#password').fill('pw');
    await page.locator('#jf-form button[type="submit"]').click();
    await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30_000 });
  };
  await signIn();   // this browser is now a known device for "tester" (a cookie only /auth sees)
  await page.request.post('/auth/logout', { maxRedirects: 0 });
  // someone else, from the same address, guesses at the name until it is refused there and every other try waits 16 s
  let last = '';
  for (let i = 0; i < 12; i++) last = decodeURIComponent((await request.post('/auth/jellyfin', { form: { user: 'tester', password: 'guess' + i }, maxRedirects: 0 })).headers()['location'] ?? '');
  expect(last).toMatch(/too many failed attempts/i);
  const t0 = Date.now();
  await signIn();
  expect(Date.now() - t0).toBeLessThan(10_000);
});
