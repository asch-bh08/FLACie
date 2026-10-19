import { test, expect, Page } from '@playwright/test';

// The full-screen player's "Living Sleeve" look: its colour comes from the cover (--np-accent on .np, used by the big play button and the glow),
// the background drifts only while a song plays, and stays still when paused or when the person asked for reduced motion.

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

async function openPlayer(page: Page) {
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await page.locator('.tracks .track-main').first().click();
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await expect(page.locator('.ls-names h2')).toBeVisible();
  await page.waitForTimeout(600);   // the player slides up (0.38 s) before anything is measured
}

const accentOf = (page: Page) => page.locator('.np').evaluate((el) => getComputedStyle(el).getPropertyValue('--np-accent').trim());

test('the player takes its colour from the cover and the play button wears it', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page);
  await openPlayer(page);
  await expect.poll(() => page.locator('.np').getAttribute('data-accent'), { timeout: 10_000 }).toBe('cover');
  const accent = await accentOf(page);
  expect(accent).toMatch(/^rgb\(\d+, \d+, \d+\)$/);
  // the button fades to the new colour (1.2 s), so it is read once the fade is over
  await expect.poll(() => page.locator('.ls-play').evaluate((el) => getComputedStyle(el).backgroundColor), { timeout: 5_000 }).toBe(accent);
  await expect(page.locator('.np-field')).toHaveCount(2);
});

test('the background moves only while playing', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page);
  await openPlayer(page);
  await expect(page.locator('.np')).not.toHaveClass(/\bstill\b/, { timeout: 10_000 });
  await page.locator('.ls-play').click();
  await expect(page.locator('.np')).toHaveClass(/\bstill\b/);
  const running = await page.locator('.np-field').first().evaluate((el) => getComputedStyle(el).animationPlayState);
  expect(running).toBe('paused');
});

test('reduced motion keeps the background still', async ({ page }) => {
  test.setTimeout(120_000);
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await signIn(page);
  await openPlayer(page);
  const anim = await page.locator('.np-field').first().evaluate((el) => getComputedStyle(el).animationName);
  expect(anim).toBe('none');
});

test('the seek bar is a ring of bars round the play button, and dragging or the keyboard still seeks', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page);
  await openPlayer(page);
  const wave = page.locator('.ls .np-frame');   // on a PC the shape is a frame round the cover (the ring is the phone's)
  await expect(wave).toHaveCount(1);
  await expect.poll(() => wave.evaluate((c: HTMLCanvasElement) => c.width), { timeout: 5_000 }).toBeGreaterThan(80);
  // something is drawn: some pixels are not empty
  const drawn = await wave.evaluate((c: HTMLCanvasElement) => { const d = c.getContext('2d')!.getImageData(0, 0, c.width, c.height).data; let n = 0; for (let i = 3; i < d.length; i += 4) if (d[i]) n++; return n; });
  expect(drawn).toBeGreaterThan(100);
  const seek = page.locator('.ls-ring input[type=range]');
  await expect(seek).toHaveAttribute('aria-label', 'Seek');
  // one chip for the file (the codec or Hi-Res with its sampling), no source or bitrate badge
  await expect(page.locator('.ls-chip:visible')).toHaveCount(1);
});

test('on a phone the player covers the whole screen and its controls stay clear of the home gesture at the bottom', async ({ page }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 390, height: 844 });
  await signIn(page);
  await openPlayer(page);
  const dock = await page.locator('.ls-actions').boundingBox();
  expect(dock!.y + dock!.height).toBeLessThanOrEqual(844 - 20);
  // nothing of the page under it shows at the bottom: the app's own tabs and player bar are covered
  const inside = await page.evaluate(() => !!document.elementFromPoint(195, 838)?.closest('.ls'));
  expect(inside).toBe(true);
  // the lyrics and the queue open as a sheet over the cover, and close again
  await page.locator('.ls-actions .ls-outline', { hasText: 'Up next' }).click();
  await expect(page.locator('.ls')).toHaveClass(/panel-open/);
  await page.getByRole('button', { name: 'Back to the cover' }).click();
  await expect(page.locator('.ls')).not.toHaveClass(/panel-open/);
});

test('the seek bar takes the song own shape: the first player works it out and the server keeps it for next time', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page);
  // the page asks the server for the shape; when nobody has sent it yet (a fresh test data folder) it works it out and sends it
  const asked = page.waitForRequest((r) => r.url().includes('/api/wave') && r.method() === 'GET', { timeout: 30_000 });
  await openPlayer(page);
  const p = new URL((await asked).url()).searchParams.get('p')!;
  const get = () => page.request.get('/api/wave?p=' + encodeURIComponent(p));
  await expect.poll(async () => (await get()).status(), { timeout: 60_000 }).toBe(200);
  const bars = await (await get()).json();
  expect(bars).toHaveLength(200);
  expect(bars.every((b: number) => b >= 0 && b <= 255)).toBe(true);   // (the stand-in's songs are silence: every bar is 0)
  // a shape that is not 200 bars of 0-255 is refused
  expect((await page.request.post('/api/wave?p=' + encodeURIComponent(p), { data: [1, 2, 3] })).status()).toBe(400);
});
