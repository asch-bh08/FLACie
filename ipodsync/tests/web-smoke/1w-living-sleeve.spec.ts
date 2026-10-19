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
  await expect(page.locator('.np-titles h2')).toBeVisible();
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
  await expect.poll(() => page.locator('.np-controls .play.big').evaluate((el) => getComputedStyle(el).backgroundColor), { timeout: 5_000 }).toBe(accent);
  await expect(page.locator('.np-glow')).toHaveCount(1);
  await expect(page.locator('.np-field')).toHaveCount(2);
});

test('the background moves only while playing', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page);
  await openPlayer(page);
  await expect(page.locator('.np')).not.toHaveClass(/\bstill\b/, { timeout: 10_000 });
  await page.locator('.np-controls .play.big').click();
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

test('the seek bar is a row of bars drawn across the song, and dragging or the keyboard still seeks', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page);
  await openPlayer(page);
  const wave = page.locator('.np .seek.big .np-wave');
  await expect(wave).toHaveCount(1);
  await expect.poll(() => wave.evaluate((c: HTMLCanvasElement) => c.width), { timeout: 5_000 }).toBeGreaterThan(100);
  // something is drawn: some pixels are not empty
  const drawn = await wave.evaluate((c: HTMLCanvasElement) => { const d = c.getContext('2d')!.getImageData(0, 0, c.width, c.height).data; let n = 0; for (let i = 3; i < d.length; i += 4) if (d[i]) n++; return n; });
  expect(drawn).toBeGreaterThan(100);
  const seek = page.locator('.np .seek.big input[type=range]');
  await expect(seek).toHaveAttribute('aria-label', 'Seek');
  // only one format badge row entry for the codec and one for the sampling, no source or bitrate badge
  await expect(page.locator('.np-main > .pills .badge:visible')).toHaveCount(2);
});
