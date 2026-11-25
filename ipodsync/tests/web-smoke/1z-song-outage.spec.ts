import { test, expect, Page } from '@playwright/test';

// Owner, 25 Nov: "songs are not loading cleanly: sometimes about 10 s to play, sometimes not at all". A few seconds of Jellyfin failing (a restart, a
// hiccup) opened the server's circuit breaker, and for its 15 s cool-down every song pressed failed at once: the player tried each one four times and
// skipped it without a word, running down the queue in silence (11-14 s measured against the mock). Now the song someone presses goes to Jellyfin as the
// breaker's probe, and a passing error is asked again at once on the server: the song pressed after the outage starts straight away.

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

test.afterEach(async ({ request }) => { await request.get(`${MOCK}/__outage?ms=0`); });

test('a song pressed just after Jellyfin failed for a few seconds starts at once', async ({ page, request }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await page.goto('/artist/Brass%20Monkeys');
  await page.getByRole('link', { name: /Brass Album/ }).first().click();
  const rows = page.locator('.tracks .track-main');
  await rows.nth(2).waitFor();
  // Jellyfin fails everything for 3 s while a song is pressed: that song can't play, and the failures open the breaker
  await request.get(`${MOCK}/__outage?ms=3000`);
  await rows.first().click();
  await page.waitForTimeout(3500);
  // Jellyfin is back; the next song pressed must start well inside the breaker's 15 s cool-down
  const ms = await page.evaluate(async () => {
    const w = window as any; const before = w.__flaciePlayingAt || 0; const t0 = performance.now();
    (document.querySelectorAll('.tracks .track-main')[2] as HTMLElement).click();
    for (let i = 0; i < 200; i++) { await new Promise((r) => setTimeout(r, 50)); if ((w.__flaciePlayingAt || 0) > before) return w.__flaciePlayingAt - t0; }
    return 99999;
  });
  console.log(`song after the outage started after ${Math.round(ms)} ms`);
  expect(ms).toBeLessThan(1500);
  // and it is the song pressed, not one further down the queue reached by silent skips
  await expect(page.locator('.player')).toContainText(await rows.nth(2).locator('.track-title').innerText());
});
