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
