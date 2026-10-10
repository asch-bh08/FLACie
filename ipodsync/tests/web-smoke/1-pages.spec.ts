import { test, expect, Page } from '@playwright/test';

// Every page of FLACie Web, signed in against the mock Jellyfin (see mock-jellyfin.mjs): each must open, show its content,
// and raise no page error or Blazor error banner.

// Expected with a fake Jellyfin that has no audio files and no live connection
const IGNORED_CONSOLE = [/WebSocket/i, /\/socket/, /Failed to load resource/, /status of (401|404)/, /ERR_/, /favicon/];

function watch(page: Page) {
  const problems: string[] = [];
  page.on('pageerror', (e) => problems.push('pageerror: ' + e.message));
  page.on('console', (m) => {
    if (m.type() === 'error' && !IGNORED_CONSOLE.some((r) => r.test(m.text()))) problems.push('console: ' + m.text());
  });
  return problems;
}

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  // let the page we land on finish loading, so leaving it for the page under test does not abort it half way
  await page.waitForLoadState('load');
  await page.waitForTimeout(1500);
}

test.beforeEach(async ({ page }) => {
  await signIn(page);
});

// route, text that proves the page rendered its content
const pages: [string, RegExp][] = [
  ['/', /Aurora Vale|Brass Monkeys|Cobalt Sky|Delta Hum|Echo Harbour|Good|Recent|Library/i],
  ['/explore', /Overview/],
  ['/songs', /Sunrise|Harbour|Static|Velvet|Lantern|Meridian|Orbit|Paper/],
  ['/albums', /Album 1/],
  ['/artists', /Aurora Vale/],
  ['/genres', /Rock|Jazz|Electronic/],
  ['/charts', /Charts|Top|Trending|Hot/i],
  ['/favorites', /Favou?rites|Liked|nothing/i],
  ['/playlists', /Playlists?/i],
  ['/search?q=aurora', /Aurora Vale/],
  ['/devices', /Devices?|This browser/i],
  ['/settings', /Settings|Account|Playback|Appearance/i],
  ['/account', /tester/],
  ['/dashboard', /Dashboard|Users?|Server|Activity/i],
  ['/downloads', /Download/i],
  ['/import', /Import/i],
  ['/jam', /Jam/i],
  ['/headphones', /Headphone test/],
  ['/artist/Aurora%20Vale', /Aurora Vale/],
  ['/genre/Rock', /Rock/],
];

for (const [route, text] of pages) {
  test(`page ${route} opens and renders`, async ({ page }) => {
    const problems = watch(page);
    const res = await page.goto(route);
    expect(res?.status(), 'http status').toBeLessThan(400);
    await expect(page).not.toHaveURL(/\/login/);
    await expect(page.locator('body')).toContainText(text);
    // let the live connection finish its first render before judging
    await page.waitForTimeout(1500);
    await expect(page.locator('#blazor-error-ui')).toBeHidden();
    await expect(page.locator('body')).not.toContainText(/An unhandled error has occurred|Unhandled exception/i);
    expect(problems).toEqual([]);
  });
}

test('the Explore filter bar narrows the results', async ({ page }) => {
  const problems = watch(page);
  await page.goto('/explore');
  const box = page.locator('input[type="search"]').first();
  // typing before the live connection is up is lost, so type again until the page reacts
  await expect(async () => {
    await box.fill('');
    await box.fill('aurora');   // one input event: typing key by key races with the page redrawing the box
    await expect(box).toHaveValue('aurora', { timeout: 3000 });
    await expect(page.locator('body')).not.toContainText('Echo Harbour', { timeout: 3000 });
    await expect(page.locator('body')).toContainText('Aurora Vale', { timeout: 3000 });
  }).toPass({ timeout: 40_000 });
  expect(problems).toEqual([]);
});

test('the headphone test plays tones, reaches 1 Hz to 44 kHz and follows the slider', async ({ page }) => {
  const problems = watch(page);
  await page.goto('/headphones');
  await expect(page.locator('#hp-root')).toBeVisible();
  await page.waitForTimeout(2000);   // the page wires itself up once the live connection is up
  await page.locator('[data-act="side"][data-side="L"]').click();
  await expect(page.locator('#hp-status')).toContainText('Left ear only');
  await page.locator('#hp-level').fill('60');
  await expect.poll(() => page.evaluate(() => (window as any).hp.peak()), { timeout: 8000 }).toBeGreaterThan(20);   // real sound is being made
  await page.locator('[data-act="tab"][data-to="tone"]').click();
  await expect(page.locator('[data-panel="tone"]')).toBeVisible();
  await expect(page.locator('[data-panel="lr"]')).toBeHidden();
  await page.locator('[data-act="preset"][data-hz="440"]').click();
  await expect(page.locator('#hp-hz-label')).toHaveText('440 Hz');
  await page.locator('[data-act="tone"]').click();
  await expect(page.locator('#hp-status')).toContainText('440 Hz');
  await page.locator('[data-act="preset"][data-hz="5"]').click();
  await expect(page.locator('#hp-hz-label')).toHaveText('5 Hz');
  await expect(page.locator('#hp-zone')).toContainText('Infrasound');
  await page.locator('[data-act="preset"][data-hz="30000"]').click();
  await expect(page.locator('#hp-zone')).toContainText('Ultrasonic');
  await page.locator('[data-act="nudge"][data-mul="0.5"]').click();
  await expect(page.locator('#hp-hz-label')).toHaveText('15 kHz');
  await page.locator('[data-act="tone"]').click();   // pressing a playing test again stops it
  await expect(page.locator('#hp-status')).toHaveText('Stopped');
  await page.locator('[data-act="tab"][data-to="space"]').click();
  await page.locator('[data-act="spin"][data-path="circle"]').click();
  await expect(page.locator('#hp-status')).toContainText('Around your head');
  await page.locator('[data-act="stop"]').click();
  await expect(page.locator('#hp-status')).toHaveText('Stopped');
  expect(problems).toEqual([]);
});

test('the player bar offers the mini player (picture in picture) where the browser can', async ({ page }) => {
  await page.goto('/songs');
  await page.waitForTimeout(2000);
  await page.locator('button.track-main').first().click();
  await expect(page.locator('.player')).toBeVisible();
  await expect(page.getByRole('button', { name: /Mini player/ })).toBeVisible({ timeout: 10_000 });
});

test('an Apple Lossless song plays: the server decodes it to WAV (browsers cannot play ALAC)', async ({ page }) => {
  const problems = watch(page);
  await page.goto('/songs');
  await page.waitForTimeout(2000);
  await expect(async () => {   // typing before the live connection is up is lost: type again until the list narrows
    await page.locator('input[type="search"]').first().fill('Velvet');
    await expect(page.locator('.track-title', { hasText: /^Velvet 1$/ })).toBeVisible({ timeout: 3000 });
  }).toPass({ timeout: 30_000 });
  const streamed = page.waitForResponse((r) => r.url().includes('/stream?p=') && r.url().includes('s1'), { timeout: 20_000 });
  await page.locator('.track', { has: page.locator('.track-title', { hasText: /^Velvet 1$/ }) }).locator('button.track-main').click();   // song 1 of the mock library
  const res = await streamed;
  expect(res.headers()['content-type']).toBe('audio/wav');
  expect([200, 206]).toContain(res.status());
  // the clock moves, so the browser really decoded and played it
  await expect.poll(async () => (await page.locator('.player .seek span').first().innerText()), { timeout: 15_000 }).not.toBe('0:00');
  const body = await page.request.get(res.url());
  const bytes = await body.body();
  expect(bytes.subarray(0, 4).toString('ascii')).toBe('RIFF');
  expect(bytes.length).toBeGreaterThan(44 + 44100 * 4);   // two seconds of 16-bit stereo at 44.1 kHz
  expect(problems).toEqual([]);
});

test('Quick picks learn from what Jellyfin says was played: songs of that artist come first', async ({ page }) => {
  const problems = watch(page);
  await page.goto('/');
  // the picks are rebuilt once Jellyfin's play counts have been read (a moment after the page opens)
  await expect(async () => {
    const shelf = page.locator('section.shelf[aria-label="Quick picks"]');
    await expect(shelf.locator('.pick').first()).toContainText('Aurora Vale', { timeout: 2500 });   // the songs Jellyfin counted lead the row
  }).toPass({ timeout: 30_000 });
  expect(problems).toEqual([]);
});

test('an unknown address shows a page, not a crash', async ({ page }) => {
  const res = await page.goto('/this-page-does-not-exist');
  expect(res?.status()).toBeLessThan(500);
});

test('signing out returns to the login page', async ({ page }) => {
  await page.request.post('/auth/logout', { maxRedirects: 0 });
  await page.goto('/songs');
  await expect(page).toHaveURL(/\/login/);
});
