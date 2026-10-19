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
/** The icon button of a menu (its accessible name is "Label: value"). */
const pill = (page: Page, label: string) => page.locator('#speed-sleep button.chip-ico[aria-label^="' + label + ':"]');
const says = (page: Page, label: string, value: string | RegExp) => expect(pill(page, label)).toHaveAttribute('aria-label', typeof value === 'string' ? label + ': ' + value : new RegExp('^' + label + ': ' + value.source));
const esc = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
async function open(page: Page, label: string) { await pill(page, label).click(); await expect(page.locator('#speed-sleep .chip-pop')).toBeVisible(); }
/** Opens a pill's menu and picks the entry that starts with [option]. */
async function pick(page: Page, label: string, option: string) {
  await open(page, label);
  await page.locator('#speed-sleep .chip-pop button').filter({ hasText: new RegExp('^\\s*' + esc(option)) }).first().click();
}

test.describe.serial('speed and sleep', () => {
  test('the controls are icon buttons in the player top bar, beside Retro, in the same style, not a row of their own', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    for (const n of ['Speed', 'Sleep timer', 'Sound']) await expect(pill(page, n)).toBeVisible();
    await expect(page.locator('#speed-sleep .dd')).toHaveCount(0);
    await page.waitForTimeout(1500);   // the format badges arrive and move the row: measure after they have
    // all four rectangles read in one go (the row can still move between separate reads)
    const r = await page.evaluate(() => {
      const rect = (e: Element | null) => { const b = e!.getBoundingClientRect(); return { y: b.y, h: b.height }; };
      // (the Living Sleeve player keeps them in its top bar, out of the way of the cover and the controls; measured against the Retro button there)
      const heart = document.querySelector('.ls-tools .np-retro');
      return { heart: rect(heart!), others: ['Speed', 'Sleep timer', 'Sound'].map((n) => rect(document.querySelector('#speed-sleep button.chip-ico[aria-label^="' + n + ':"]'))) };
    });
    for (const o of r.others) {
      expect(o.h, 'as big as the Retro button').toBeLessThanOrEqual(r.heart.h + 4);
      expect(Math.abs(o.y - r.heart.y), 'on the top bar').toBeLessThanOrEqual(6);
    }
    await says(page, 'Speed', '1x'); await says(page, 'Sleep timer', 'off'); await says(page, 'Sound', 'default');
    // pictures for a person to look at (test-results is not kept)
    await page.screenshot({ path: 'test-results/icons.png' });
    await open(page, 'Speed'); await page.screenshot({ path: 'test-results/icons-speed.png' });
    await open(page, 'Sound'); await page.screenshot({ path: 'test-results/icons-sound.png' });
  });

  test('speed changes the audio rate with the pitch kept, is remembered per song, and shows the long-item skips', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    expect(await rate(page)).toBe(1);
    await expect(page.getByRole('button', { name: 'Back 10 seconds' })).toHaveCount(0);   // an ordinary song at 1x: no long-item buttons
    await pick(page, 'Speed', '1.5x');
    await expect.poll(() => rate(page)).toBe(1.5);
    await says(page, 'Speed', '1.5x');
    expect(await page.evaluate(() => { const a = new Audio(); return 'preservesPitch' in a; })).toBe(true);
    await expect(page.getByRole('button', { name: 'Back 10 seconds' })).toBeVisible();      // a changed speed shows them
    await expect(page.getByRole('button', { name: 'Forward 30 seconds' })).toBeVisible();
    // next song: back to 1x; back to the first song: its 1.5x is remembered
    await page.locator('.ls-transport').getByRole('button', { name: 'Next' }).click();
    await expect.poll(() => rate(page), { timeout: 15_000 }).toBe(1);
    // (Previous within the first 3 s goes to the previous song)
    await page.locator('.ls-transport').getByRole('button', { name: 'Previous' }).click();
    await expect.poll(() => rate(page), { timeout: 15_000 }).toBe(1.5);
    await pick(page, 'Speed', '1x');
    await expect.poll(() => rate(page)).toBe(1);
  });

  test('the sleep timer can be set, shows what is left, extends and turns off', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    await pick(page, 'Sleep timer', '15 minutes');
    await says(page, 'Sleep timer', /1[45] min left/);
    await pick(page, 'Sleep timer', 'Add 15 minutes');
    await says(page, 'Sleep timer', /(29|30) min left/);
    await pick(page, 'Sleep timer', 'Turn off');
    await says(page, 'Sleep timer', 'off');
    // (last, because the mock's songs are 3 s long and this one then ends by itself)
    await pick(page, 'Sleep timer', 'End of this song');
    await expect.poll(async () => (await pill(page, 'Sleep timer').getAttribute('aria-label')) ?? '', { timeout: 10_000 }).toMatch(/^Sleep timer: (End of this song|off)/);
  });

  test('"end of this song" pauses at the end instead of playing the next song', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    await expect(page.getByRole('button', { name: 'Pause' }).first()).toBeVisible({ timeout: 15_000 });
    await pick(page, 'Sleep timer', 'End of this song');
    const title = await page.locator('.ls-names h2').innerText();
    // run the song to its end
    await page.evaluate(() => (window as any).flacie.seek(1e6));
    await expect(page.getByRole('button', { name: 'Play' }).first()).toBeVisible({ timeout: 20_000 });
    await says(page, 'Sleep timer', 'off');
    expect(await page.locator('.ls-names h2').innerText()).toBe(title);   // the next song did not start
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
    await says(page, 'Sound', 'even out');
    await page.locator('.sound-row').click();
    await expect.poll(vol).toBeCloseTo(base, 3);
    await says(page, 'Sound', 'default');
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
    await says(page, 'Sound', 'fade 4 s');
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
    await page.locator('.ls-top').click({ position: { x: 5, y: 5 } });
    await expect(page.locator('#speed-sleep .chip-pop')).toHaveCount(0);
  });
});
