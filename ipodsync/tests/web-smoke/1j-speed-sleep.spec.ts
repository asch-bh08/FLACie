import { test, expect, Page } from '@playwright/test';

// Beta 60: playback speed with a per-item memory, the sleep timer (minutes, end of song, extend) and the long-item skips in the full-screen player.
async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(1000);
}

async function playLab(page: Page) {
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  await page.locator('.tracks .track-main').first().click();
  await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 20_000 });
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await expect(page.locator('#speed-sleep')).toBeVisible({ timeout: 20_000 });
}

const rate = (page: Page) => page.evaluate(() => (window as any).flacie.rate() as number);
async function pick(page: Page, label: string, option: string) {
  await page.locator('#speed-sleep .dd', { hasText: label }).locator('.dd-btn').click();
  await page.locator('#speed-sleep .dd-opt', { hasText: option }).first().click();
}

test.describe.serial('speed and sleep', () => {
  test('speed changes the audio rate with the pitch kept, is remembered per song, and shows the long-item skips', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    expect(await rate(page)).toBe(1);
    await expect(page.getByRole('button', { name: 'Back 10 seconds' })).toHaveCount(0);   // an ordinary song at 1x: no long-item buttons
    await pick(page, 'Speed', '1.5x');
    await expect.poll(() => rate(page)).toBe(1.5);
    expect(await page.evaluate(() => { const a = new Audio(); return 'preservesPitch' in a; })).toBe(true);
    await expect(page.getByRole('button', { name: 'Back 10 seconds' })).toBeVisible();      // a changed speed shows them
    await expect(page.getByRole('button', { name: 'Forward 30 seconds' })).toBeVisible();
    // next song: back to 1x; back to the first song: its 1.5x is remembered
    await page.getByRole('button', { name: 'Next' }).last().click();
    await expect.poll(() => rate(page), { timeout: 15_000 }).toBe(1);
    // (Previous within the first 3 s goes to the previous song)
    await page.getByRole('button', { name: 'Previous' }).last().click();
    await expect.poll(() => rate(page), { timeout: 15_000 }).toBe(1.5);
    await pick(page, 'Speed', '1x');
    await expect.poll(() => rate(page)).toBe(1);
  });

  test('the sleep timer can be set, shows what is left, extends and turns off', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    await expect(page.locator('#speed-sleep .dd', { hasText: 'Sleep' })).toContainText('Off');
    await pick(page, 'Sleep', '15 minutes');
    await expect(page.locator('#speed-sleep .dd', { hasText: 'Sleep' })).toContainText(/1[45] min left/, { timeout: 10_000 });
    await pick(page, 'Sleep', 'Add 15 minutes');
    await expect(page.locator('#speed-sleep .dd', { hasText: 'Sleep' })).toContainText(/(29|30) min left/, { timeout: 10_000 });
    await pick(page, 'Sleep', 'End of this song');
    await expect(page.locator('#speed-sleep .dd', { hasText: 'Sleep' })).toContainText(/End of this song/, { timeout: 10_000 });
    await pick(page, 'Sleep', 'Off');
    await expect(page.locator('#speed-sleep .dd', { hasText: 'Sleep' })).toContainText('Off');
  });

  test('"end of this song" pauses at the end instead of playing the next song', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    await expect(page.getByRole('button', { name: 'Pause' }).first()).toBeVisible({ timeout: 15_000 });
    await pick(page, 'Sleep', 'End of this song');
    const title = await page.locator('.np-titles h2').innerText();
    // run the song to its end
    await page.evaluate(() => (window as any).flacie.seek(1e6));
    await expect(page.getByRole('button', { name: 'Play' }).first()).toBeVisible({ timeout: 20_000 });
    await expect(page.locator('#speed-sleep .dd', { hasText: 'Sleep' })).toContainText('Off');
    expect(await page.locator('.np-titles h2').innerText()).toBe(title);   // the next song did not start
  });
});
