import { test, expect, Page } from '@playwright/test';

// Speed, sleep timer, volume levelling and fade between songs in the full-screen player: three pills (Speed, Sleep timer, Sound) that open short menus above them,
// and skip back 10 / forward 30 for long items.
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
const pill = (page: Page, label: string) => page.locator('#speed-sleep').getByRole('button', { name: label, exact: true });
const esc = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
async function open(page: Page, label: string) { await pill(page, label).click(); await expect(page.locator('#speed-sleep .chip-pop')).toBeVisible(); }
/** Opens a pill's menu and picks the entry that starts with [option]. */
async function pick(page: Page, label: string, option: string) {
  await open(page, label);
  await page.locator('#speed-sleep .chip-pop button').filter({ hasText: new RegExp('^\\s*' + esc(option)) }).first().click();
}

test.describe.serial('speed and sleep', () => {
  test('the pills are small and tidy: three of them in one row, no form boxes', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    for (const n of ['Speed', 'Sleep timer', 'Sound']) await expect(pill(page, n)).toBeVisible();
    await expect(page.locator('#speed-sleep .dd')).toHaveCount(0);
    const boxes = await page.locator('#speed-sleep .chipbtn').evaluateAll((els) => els.map((e) => e.getBoundingClientRect()));
    expect(boxes.length).toBeGreaterThanOrEqual(3);
    for (const b of boxes) expect(b.height).toBeLessThanOrEqual(40);
    expect(new Set(boxes.map((b) => Math.round(b.top))).size, 'one row').toBe(1);
    await expect(pill(page, 'Speed')).toHaveText('1x');
    await expect(pill(page, 'Sleep timer')).toHaveText('Sleep');
    await expect(pill(page, 'Sound')).toHaveText('Sound');
    // pictures for a person to look at (test-results is not kept)
    await open(page, 'Speed'); await page.screenshot({ path: 'test-results/pills-speed.png' });
    await open(page, 'Sound'); await page.screenshot({ path: 'test-results/pills-sound.png' });
  });

  test('speed changes the audio rate with the pitch kept, is remembered per song, and shows the long-item skips', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    expect(await rate(page)).toBe(1);
    await expect(page.getByRole('button', { name: 'Back 10 seconds' })).toHaveCount(0);   // an ordinary song at 1x: no long-item buttons
    await pick(page, 'Speed', '1.5x');
    await expect.poll(() => rate(page)).toBe(1.5);
    await expect(pill(page, 'Speed')).toHaveText('1.5x');
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
    await pick(page, 'Sleep timer', '15 minutes');
    await expect(pill(page, 'Sleep timer')).toContainText(/1[45] min left/, { timeout: 10_000 });
    await pick(page, 'Sleep timer', 'Add 15 minutes');
    await expect(pill(page, 'Sleep timer')).toContainText(/(29|30) min left/, { timeout: 10_000 });
    await pick(page, 'Sleep timer', 'End of this song');
    await expect(pill(page, 'Sleep timer')).toContainText(/End of this song/, { timeout: 10_000 });
    await pick(page, 'Sleep timer', 'Turn off');
    await expect(pill(page, 'Sleep timer')).toHaveText('Sleep');
  });

  test('"end of this song" pauses at the end instead of playing the next song', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    await expect(page.getByRole('button', { name: 'Pause' }).first()).toBeVisible({ timeout: 15_000 });
    await pick(page, 'Sleep timer', 'End of this song');
    const title = await page.locator('.np-titles h2').innerText();
    // run the song to its end
    await page.evaluate(() => (window as any).flacie.seek(1e6));
    await expect(page.getByRole('button', { name: 'Play' }).first()).toBeVisible({ timeout: 20_000 });
    await expect(pill(page, 'Sleep timer')).toHaveText('Sleep');
    expect(await page.locator('.np-titles h2').innerText()).toBe(title);   // the next song did not start
  });

  test('even out volume turns a loud song down by the gain Jellyfin measured', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    const vol = () => page.evaluate(() => (window as any).flacie.loud() as number);
    const base = await page.evaluate(() => (window as any).flacie.volume() as number);
    await expect.poll(vol).toBeCloseTo(base, 3);   // off by default: untouched
    await open(page, 'Sound');
    await page.locator('.sound-row').click();
    await expect.poll(vol).toBeCloseTo(base * 0.501, 2);   // -6 dB
    await expect(pill(page, 'Sound')).toHaveText('Even out');
    await page.locator('.sound-row').click();
    await expect.poll(vol).toBeCloseTo(base, 3);
    await expect(pill(page, 'Sound')).toHaveText('Sound');
  });

  test('fade between songs fades the start of the first track of an album and is off by default', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    const gain = () => page.evaluate(() => (window as any).flacie.xfade() as number);
    await page.evaluate(() => (window as any).flacie.seek(0.4));
    await expect.poll(gain, { timeout: 5_000 }).toBe(1);   // off by default: no fade
    await open(page, 'Sound');
    await page.locator('.seg button').filter({ hasText: /^4 s$/ }).click();
    await expect(pill(page, 'Sound')).toHaveText('Fade 4 s');
    await page.evaluate(() => (window as any).flacie.seek(0.4));
    await expect.poll(gain, { timeout: 10_000 }).toBeLessThan(0.9);   // the first track of an album fades in
    await page.evaluate(() => (window as any).flacie.seek(2));
    await expect.poll(gain, { timeout: 10_000 }).toBe(1);
    await page.locator('.seg button').filter({ hasText: /^Off$/ }).click();
    await page.evaluate(() => (window as any).flacie.seek(0.4));
    await expect.poll(gain, { timeout: 10_000 }).toBe(1);
  });

  test('a click elsewhere closes a menu', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    await open(page, 'Speed');
    await page.locator('.np-top').click({ position: { x: 5, y: 5 } });
    await expect(page.locator('#speed-sleep .chip-pop')).toHaveCount(0);
  });
});
