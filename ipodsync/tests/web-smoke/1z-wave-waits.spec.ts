import { test, expect, Page } from '@playwright/test';

// Owner, 9 Nov: "songs take ages to load and buffer when playing". The page also reads a whole song (often 50-150 MB of FLAC) to work out the seek bar's
// shape; it did that at once, beside the song being started. It now waits until the song has buffered well ahead, and lets go if the song stalls.

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForTimeout(1200);
}

test('the whole-file read for the seek bar waits while the song itself is still loading', async ({ page }) => {
  test.setTimeout(90_000);
  const waveReads: number[] = [];
  let slow = true;
  const t0 = Date.now();
  page.on('request', (r) => { if (r.url().includes('/stream?wave=1')) waveReads.push(Date.now() - t0); });
  // the song's own stream answers only after 6 s (a slow start, as on the owner's line)
  await page.route(/\/stream\?p=/, async (route) => { if (slow) await new Promise((r) => setTimeout(r, 6000)); await route.continue(); });
  await signIn(page);
  await page.goto('/songs'); await page.waitForTimeout(1200);
  const clicked = Date.now() - t0;
  // a song whose shape nobody has sent yet
  await page.getByRole('button', { name: /^Play .+ by / }).nth(5).click();
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await page.waitForTimeout(4500);
  expect(waveReads.filter((at) => at > clicked), 'no whole-file read while the song is loading').toEqual([]);
  // once the song is in, the shape is still worked out
  slow = false;
  await expect.poll(() => waveReads.filter((at) => at > clicked).length, { timeout: 40_000 }).toBeGreaterThan(0);
});
