import { test, expect, Page } from '@playwright/test';

// The AI DJ on the web and in the Windows app (docs/spec/ai-dj.md): the DJ section of Settings saves on the server (a reload keeps it), the DJ button
// beside Shuffle in the full-screen player flips the DJ for this queue only, and a playlist's DJ chip goes Auto, On, Off. The test server has no
// Cloudflare, so the section says so. Every test puts the settings back as they were (DJ off), so the other specs never hear the DJ duck their music.

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

// a page is read once it is live: the copy drawn first by the server is replaced when the live connection starts
async function live(page: Page, url: string) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto(url);
  await ws; await page.waitForTimeout(800);
}

async function playLab(page: Page, row = 0) {
  await live(page, '/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  await page.locator('.tracks .track-main').nth(row).click();
  await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 20_000 });
}

/** Starts a new queue from the Songs page and opens the full-screen player on it. */
async function newQueue(page: Page, row = 0) {
  await live(page, '/songs');
  await page.getByRole('button', { name: /^Play .+ by / }).nth(row).click();
  await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 20_000 });
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await expect(page.locator('.ls-transport')).toBeVisible({ timeout: 20_000 });
}

const settings = async (page: Page) => (await page.request.get('/api/dj/settings')).json();
/** Back to the defaults the other specs expect: DJ off, Often, everywhere, backup voice on, no playlist switches. */
async function reset(page: Page) {
  const r = await page.request.post('/api/dj/settings', { data: { enabled: false, chatty: 'often', phone: true, auto: true, web: true, windows: true, backup: true } });
  expect(r.ok()).toBe(true);
}

const dj = (page: Page) => page.locator('#dj');
const pressed = (l: ReturnType<Page['locator']>, on: boolean) => expect(l).toHaveAttribute('aria-pressed', on ? 'true' : 'false');
const seg = (page: Page, name: string) => dj(page).locator('.seg button', { hasText: new RegExp('^\\s*' + name + '\\s*$') });
const preset = (page: Page, name: string) => dj(page).locator('.dj-presets button', { hasText: name });
const row = (page: Page, text: string) => dj(page).locator('label.switch-row', { hasText: text });

test.describe.serial('the DJ', () => {
  test.afterEach(async ({ page }) => { await reset(page).catch(() => { }); });

  test('Settings has a DJ section: on or off, how chatty, where, backup voice; a reload keeps every choice', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await reset(page);
    await live(page, '/settings');
    await expect(page.locator('.subnav a[href="/settings#dj"]')).toHaveText('DJ');
    await expect(dj(page).getByRole('heading', { name: 'DJ', exact: true })).toBeVisible();
    // no Cloudflare on the test server: the note says who to ask, and that the device's own voice stands in
    await expect(dj(page).getByRole('note')).toContainText("Cloudflare isn't set up yet. Ask the admin (Dashboard → DJ).");
    const onSwitch = row(page, 'Talk between songs').locator('input[role=switch]');
    const backup = row(page, 'Backup voice').locator('input[role=switch]');
    await expect(onSwitch).not.toBeChecked();
    await expect(backup).toBeChecked();
    await pressed(seg(page, 'Often'), true);
    for (const w of ['Phone', 'Android Auto', 'Web', 'Windows']) await pressed(seg(page, w), true);
    await pressed(preset(page, 'Everywhere'), true);

    // change everything
    await row(page, 'Talk between songs').click();
    await expect(onSwitch).toBeChecked();
    await seg(page, 'Rare').click();
    await pressed(seg(page, 'Rare'), true); await pressed(seg(page, 'Often'), false);
    await seg(page, 'Phone').click();
    await pressed(seg(page, 'Phone'), false);
    await pressed(preset(page, 'Everywhere'), false);
    await preset(page, 'Only in the car').click();
    await pressed(seg(page, 'Android Auto'), true);
    for (const w of ['Phone', 'Web', 'Windows']) await pressed(seg(page, w), false);
    await pressed(preset(page, 'Only in the car'), true);
    await row(page, 'Backup voice').click();
    await expect(backup).not.toBeChecked();
    await page.screenshot({ path: 'test-results/shots/dj-settings.png', fullPage: true });

    // kept on the server: the same after a reload, and the API says the same
    await live(page, '/settings');
    await expect(row(page, 'Talk between songs').locator('input[role=switch]')).toBeChecked();
    await pressed(seg(page, 'Rare'), true);
    await pressed(seg(page, 'Android Auto'), true);
    for (const w of ['Phone', 'Web', 'Windows']) await pressed(seg(page, w), false);
    await expect(row(page, 'Backup voice').locator('input[role=switch]')).not.toBeChecked();
    const j = await settings(page);
    expect(j).toMatchObject({ enabled: true, chatty: 'rare', phone: false, auto: true, web: false, windows: false, backup: false, voiceReady: false });

    // the presets: only on web, then everywhere
    await preset(page, 'Only on web').click();
    await pressed(seg(page, 'Web'), true);
    for (const w of ['Phone', 'Android Auto', 'Windows']) await pressed(seg(page, w), false);
    await preset(page, 'Everywhere').click();
    for (const w of ['Phone', 'Android Auto', 'Web', 'Windows']) await pressed(seg(page, w), true);
  });

  test('the DJ button beside Shuffle flips the DJ for this queue only, and a new queue starts on what Settings say', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await reset(page);   // DJ off in Settings
    await playLab(page);
    await page.getByRole('button', { name: 'Open Now Playing' }).click();
    const tr = page.locator('.ls-transport');
    const btn = tr.getByRole('button', { name: 'DJ', exact: true });
    await expect(btn).toBeVisible({ timeout: 20_000 });
    await expect(btn).not.toHaveClass(/\bon\b/); await pressed(btn, false);
    await expect(btn).toHaveAttribute('title', /Settings → DJ/);
    // right beside Shuffle, on the same line, in the same plain style
    const sh = (await tr.getByRole('button', { name: /^Shuffle/ }).boundingBox())!, b = (await btn.boundingBox())!;
    expect(Math.abs((sh.y + sh.height / 2) - (b.y + b.height / 2))).toBeLessThanOrEqual(2);
    expect(b.x).toBeGreaterThan(sh.x); expect(b.x - (sh.x + sh.width)).toBeLessThan(30);
    expect(await btn.evaluate((e) => getComputedStyle(e).backgroundColor)).toBe('rgba(0, 0, 0, 0)');
    await btn.click();
    await expect(btn).toHaveClass(/\bon\b/); await pressed(btn, true);
    await btn.click();
    await expect(btn).not.toHaveClass(/\bon\b/);
    await btn.click();
    await expect(btn).toHaveClass(/\bon\b/);
    expect((await settings(page)).enabled).toBe(false);   // only this queue: Settings are untouched
    // a new queue: back to what Settings say (off). (The Songs page always starts a new queue; a row of the album could be the song already playing.)
    await newQueue(page);
    await expect(tr.getByRole('button', { name: 'DJ', exact: true })).not.toHaveClass(/\bon\b/, { timeout: 10_000 });
    // with the DJ on in Settings, the button shows on for a new queue
    expect((await page.request.post('/api/dj/settings', { data: { enabled: true } })).ok()).toBe(true);
    await newQueue(page, 1);
    await expect(tr.getByRole('button', { name: 'DJ', exact: true })).toHaveClass(/\bon\b/, { timeout: 10_000 });
    await tr.getByRole('button', { name: 'DJ', exact: true }).click();   // (off again, so nothing talks over the next specs)
    await expect(tr.getByRole('button', { name: 'DJ', exact: true })).not.toHaveClass(/\bon\b/);
  });

  for (const [w, h] of [[360, 780], [390, 844]]) {
    test(`on a ${w}px phone the six controls fit across the screen`, async ({ page }) => {
      test.setTimeout(120_000);
      await page.setViewportSize({ width: w, height: h });
      await signIn(page);
      await playLab(page);
      await page.getByRole('button', { name: 'Open Now Playing' }).click();
      const tr = page.locator('.ls-transport');
      await expect(tr.getByRole('button', { name: 'DJ', exact: true })).toBeVisible({ timeout: 20_000 });
      const boxes = await tr.evaluate((el) => [...el.children].map((c) => { const r = c.getBoundingClientRect(); return { l: r.left, r: r.right, w: r.width }; }));
      expect(boxes).toHaveLength(6);
      for (const x of boxes) { expect(x.l).toBeGreaterThanOrEqual(0); expect(x.r).toBeLessThanOrEqual(w); expect(x.w).toBeGreaterThanOrEqual(38); }
      for (let i = 1; i < boxes.length; i++) expect(boxes[i].l).toBeGreaterThanOrEqual(boxes[i - 1].r - 0.5);   // none on top of another
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
      await page.screenshot({ path: `test-results/shots/dj-np-${w}.png` });
    });
  }

  test('a playlist has its own DJ chip: Auto, On, Off, and back to Auto, kept on the server', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await reset(page);
    const name = 'DJ chip test';
    await live(page, '/playlists');
    await page.getByLabel('New playlist name').fill(name);
    await page.getByRole('button', { name: 'Create' }).click();
    await page.waitForURL(/\/playlist\//);
    const url = page.url();
    await live(page, url);
    const chip = page.locator('.pl-hero').getByRole('button', { name: /^DJ: / });
    await expect(chip).toHaveText(/DJ: Auto/);
    await chip.click();
    await expect(chip).toHaveText(/DJ: On/);
    expect(Object.values((await settings(page)).playlists ?? {})).toContain(true);
    await live(page, url);
    await expect(page.locator('.pl-hero').getByRole('button', { name: /^DJ: / })).toHaveText(/DJ: On/);   // kept
    await page.locator('.pl-hero').getByRole('button', { name: /^DJ: / }).click();
    await expect(page.locator('.pl-hero').getByRole('button', { name: /^DJ: / })).toHaveText(/DJ: Off/);
    await page.locator('.pl-hero').getByRole('button', { name: /^DJ: / }).click();
    await expect(page.locator('.pl-hero').getByRole('button', { name: /^DJ: / })).toHaveText(/DJ: Auto/);
    await page.screenshot({ path: 'test-results/shots/dj-playlist-chip.png' });
    // clean up so a rerun starts clean
    await page.getByRole('button', { name: 'Delete' }).click();
    await page.getByRole('button', { name: /^Delete "/ }).click();
    await page.waitForURL(/\/playlists$/);
  });
});
