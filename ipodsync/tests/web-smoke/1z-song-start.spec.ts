import { test, expect, Page } from '@playwright/test';

// The owner hears 3-4 s of nothing between songs. A real Jellyfin takes a while to send a big FLAC's first byte; the page reads the start of the next song
// early, but the server threw those bytes away, so Next waited for the first byte all over again. With a slow first byte (1.5 s here) the next song must
// start well before that: its start was kept.

const MOCK = `http://127.0.0.1:${process.env.MOCK_PORT || 8196}`;

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(1500);
}

test.afterEach(async ({ request }) => { await request.get(`${MOCK}/__stream?first=0`); });

test('Next starts the next song without waiting for its first byte again', async ({ page, request }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await request.get(`${MOCK}/__stream?first=1500`);
  await page.goto('/artist/Brass%20Monkeys');
  await page.getByRole('link', { name: /Brass Album/ }).first().click();
  await page.locator('.tracks .track-main').first().click();
  await expect.poll(() => page.evaluate(() => (window as any).__flaciePlayingAt || 0), { timeout: 15000 }).toBeGreaterThan(0);
  // the early read of the next song has gone out (started once this one can play) and had time to come back
  await expect.poll(() => page.evaluate(() => (window as any).__flaciePrefetches || 0), { timeout: 10000 }).toBeGreaterThan(0);
  await page.waitForTimeout(2500);
  const ms = await page.evaluate(async () => {
    const w = window as any; const before = w.__flaciePlayingAt; const t0 = performance.now();
    (document.querySelector('.player [aria-label="Next"]') as HTMLElement).click();
    for (let i = 0; i < 100; i++) { await new Promise((r) => setTimeout(r, 50)); if (w.__flaciePlayingAt > before) return w.__flaciePlayingAt - t0; }
    return 99999;
  });
  console.log(`next song started after ${Math.round(ms)} ms`);
  expect(ms).toBeLessThan(900);
});

// The owner: a song from Listen again took about 3 s. Nothing of it was fetched before it was pressed; pointing at it now starts its start.
test('a Home song pointed at before it is pressed starts without waiting for its first byte', async ({ page, request }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  for (const n of [0, 1, 2]) { await page.locator('.tracks .track-main').nth(n).click(); await page.waitForTimeout(600); }
  await page.locator('.player [aria-label="Stop playing and close the player"]').click().catch(() => {});
  await request.get(`${MOCK}/__stream?first=1500`);
  await page.goto('/');
  const card = page.locator('button.pick').nth(3);   // a Quick pick: not one of the songs just played (their starts are kept already)
  await card.hover();
  await page.waitForTimeout(2200);   // a person's pointer resting on it before the click
  const ms = await page.evaluate(async () => {
    const w = window as any; const before = w.__flaciePlayingAt || 0; const t0 = performance.now();
    (document.querySelectorAll('button.pick')[3] as HTMLElement).click();
    for (let i = 0; i < 100; i++) { await new Promise((r) => setTimeout(r, 50)); if ((w.__flaciePlayingAt || 0) > before) return w.__flaciePlayingAt - t0; }
    return 99999;
  });
  console.log(`Home song started after ${Math.round(ms)} ms`);
  expect(ms).toBeLessThan(900);
});
