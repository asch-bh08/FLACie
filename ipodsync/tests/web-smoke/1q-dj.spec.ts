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
/** Back to the defaults the other specs expect: DJ off, Often, everywhere, the DJ's own voice (not the device's), 90 % voice, 20 % music under it, captions and the cue sound on, no song choices. */
async function reset(page: Page) {
  const r = await page.request.post('/api/dj/settings', { data: { enabled: false, chatty: 'often', phone: true, auto: true, web: true, windows: true, backup: true, talkAfterSkips: true, style: 'classic', deviceVoice: false, volume: 90, duck: 20, captions: true, cue: true, songs: {} } });
  expect(r.ok()).toBe(true);
}

const dj = (page: Page) => page.locator('#dj');
const pressed = (l: ReturnType<Page['locator']>, on: boolean) => expect(l).toHaveAttribute('aria-pressed', on ? 'true' : 'false');
const seg = (page: Page, name: string) => dj(page).locator('.seg button', { hasText: new RegExp('^\\s*' + name + '\\s*$') });
const preset = (page: Page, name: string) => dj(page).locator('.dj-presets button', { hasText: name });
const row = (page: Page, text: string) => dj(page).locator('label.switch-row', { hasText: text });

test.describe.serial('the DJ', () => {
  test.afterEach(async ({ page }) => { await reset(page).catch(() => { }); });

  test('Settings has a DJ section: on or off, how chatty, where, sound, device voice; a reload keeps every choice', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await reset(page);
    await live(page, '/settings');
    await expect(page.locator('.subnav a[href="/settings#dj"]')).toHaveText('DJ');
    await expect(dj(page).getByRole('heading', { name: 'DJ', exact: true })).toBeVisible();
    // no Cloudflare on the test server: the note says who to ask, and that the DJ stays quiet unless the device voice is chosen
    await expect(dj(page).getByRole('note')).toContainText("Cloudflare isn't set up yet. Ask the admin (Dashboard → DJ).");
    const onSwitch = row(page, 'Talk between songs').locator('input[role=switch]');
    const device = row(page, 'Device voice').locator('input[role=switch]');
    const volume = dj(page).locator('input.dj-volume'), duck = dj(page).locator('input.dj-duck');
    const afterSkips = row(page, 'Talk after skips').locator('input[role=switch]');
    await expect(afterSkips).toBeChecked();   // on unless the person turns it off
    await expect(onSwitch).not.toBeChecked();
    await expect(device).not.toBeChecked();   // only ever the DJ's own voice unless the person picks the device's
    await expect(volume).toHaveValue('90'); await expect(duck).toHaveValue('20');   // the music sits lower under the voice since 2026-10-27
    await expect(row(page, 'Captions').locator('input[role=switch]')).toBeChecked();
    await expect(row(page, 'Cue sound').locator('input[role=switch]')).toBeChecked();
    await pressed(seg(page, 'Often'), true);
    await pressed(seg(page, 'Classic radio'), true);   // the style everyone starts with
    for (const w of ['Phone', 'Android Auto', 'Web', 'Windows']) await pressed(seg(page, w), true);
    await pressed(preset(page, 'Everywhere'), true);

    // change everything
    await row(page, 'Talk between songs').click();
    await expect(onSwitch).toBeChecked();
    await seg(page, 'Every song').click();   // a line before every song
    await pressed(seg(page, 'Every song'), true); await pressed(seg(page, 'Often'), false);
    expect((await (await page.request.get('/api/dj/settings')).json()).chatty).toBe('every');
    await seg(page, 'Rare').click();
    await pressed(seg(page, 'Rare'), true); await pressed(seg(page, 'Often'), false); await pressed(seg(page, 'Every song'), false);
    await seg(page, 'Chill').click();
    await pressed(seg(page, 'Chill'), true); await pressed(seg(page, 'Classic radio'), false);
    await seg(page, 'Phone').click();
    await pressed(seg(page, 'Phone'), false);
    await pressed(preset(page, 'Everywhere'), false);
    await preset(page, 'Only in the car').click();
    await pressed(seg(page, 'Android Auto'), true);
    for (const w of ['Phone', 'Web', 'Windows']) await pressed(seg(page, w), false);
    await pressed(preset(page, 'Only in the car'), true);
    await row(page, 'Device voice').click();
    await expect(device).toBeChecked();
    await volume.fill('60'); await volume.dispatchEvent('change');
    await duck.fill('15'); await duck.dispatchEvent('change');
    await expect(dj(page).locator('label.dj-level', { hasText: 'DJ volume' }).locator('output')).toHaveText('60%');
    await row(page, 'Talk after skips').click();
    await expect(afterSkips).not.toBeChecked();
    await page.screenshot({ path: 'test-results/shots/dj-settings.png', fullPage: true });

    // kept on the server: the same after a reload, and the API says the same
    await live(page, '/settings');
    await expect(row(page, 'Talk between songs').locator('input[role=switch]')).toBeChecked();
    await pressed(seg(page, 'Rare'), true);
    await pressed(seg(page, 'Chill'), true);
    await pressed(seg(page, 'Android Auto'), true);
    for (const w of ['Phone', 'Web', 'Windows']) await pressed(seg(page, w), false);
    await expect(row(page, 'Device voice').locator('input[role=switch]')).toBeChecked();
    await expect(dj(page).locator('input.dj-volume')).toHaveValue('60'); await expect(dj(page).locator('input.dj-duck')).toHaveValue('15');
    await expect(row(page, 'Talk after skips').locator('input[role=switch]')).not.toBeChecked();
    const j = await settings(page);
    expect(j).toMatchObject({ enabled: true, chatty: 'rare', phone: false, auto: true, web: false, windows: false, deviceVoice: true, volume: 60, duck: 15, talkAfterSkips: false, style: 'chill', voiceReady: false });

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
    // (both measured in one frame once Now Playing has finished sliding up: two separate reads can land on different frames of the slide)
    const rects = () => tr.evaluate((t) => [t.querySelector('button[aria-label^="Shuffle"]')!, t.querySelector('button[aria-label="DJ"]')!]
      .map((e) => { const r = e.getBoundingClientRect(); return { x: r.x, y: r.y, width: r.width, height: r.height }; }));
    let last = -1;
    await expect.poll(async () => { const y = (await rects())[0].y, still = Math.abs(y - last) < 0.5; last = y; return still; }, { intervals: [150] }).toBe(true);
    const [sh, b] = await rects();
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
    test(`on a ${w}px phone the controls (six, or seven with the mic) fit across the screen`, async ({ page }) => {
      test.setTimeout(120_000);
      await page.setViewportSize({ width: w, height: h });
      await signIn(page);
      await playLab(page);
      await page.getByRole('button', { name: 'Open Now Playing' }).click();
      const tr = page.locator('.ls-transport');
      await expect(tr.getByRole('button', { name: 'DJ', exact: true })).toBeVisible({ timeout: 20_000 });
      const boxes = await tr.evaluate((el) => [...el.children].map((c) => { const r = c.getBoundingClientRect(); return { l: r.left, r: r.right, w: r.width }; }));
      expect(boxes).toHaveLength(await tr.locator(".dj-mic").count() ? 7 : 6);
      for (const x of boxes) { expect(x.l).toBeGreaterThanOrEqual(0); expect(x.r).toBeLessThanOrEqual(w); expect(x.w).toBeGreaterThanOrEqual(38); }
      for (let i = 1; i < boxes.length; i++) expect(boxes[i].l).toBeGreaterThanOrEqual(boxes[i - 1].r - 0.5);   // none on top of another
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
      await page.screenshot({ path: `test-results/shots/dj-np-${w}.png` });
    });
  }

  test('a DJ line shows its words over the cover and in the bar, timed to it, and Captions off hides them', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await reset(page);
    // the device's voice says every line (the test server has no Cloudflare), so a line comes as soon as a 3 s song ends by itself
    const on = { enabled: true, chatty: 'every', deviceVoice: true, captions: true, cue: true };
    expect((await page.request.post('/api/dj/settings', { data: on })).ok()).toBe(true);
    await newQueue(page);
    const cap = page.locator('.ls-art .np-dj-caption.on');
    await expect(cap).toBeVisible({ timeout: 30_000 });
    await expect(cap).not.toHaveText('');
    await expect(page.locator('.player .bar-dj-caption.on')).toHaveCount(1);
    await page.waitForTimeout(400);   // (the fade in)
    await page.screenshot({ path: '../../../docs/screens-new-look/dj-caption.png' });
    // a pause (like a skip) takes the words away at once (the songs here are 3 s long, so with "Every song" one line follows another)
    await page.locator('.ls-transport').getByRole('button', { name: 'Pause' }).click();
    await expect(cap).toHaveCount(0, { timeout: 5_000 });
    // Captions off: the next line says nothing on screen
    expect((await page.request.post('/api/dj/settings', { data: { captions: false } })).ok()).toBe(true);
    await newQueue(page);
    await page.waitForTimeout(9000);
    await expect(page.locator('.np-dj-caption.on')).toHaveCount(0);
  });

  test('the DJ clip goes through a voice chain (high-pass, presence, compressor) after a short cue sound', async ({ page }) => {
    test.setTimeout(90_000);
    await signIn(page);
    await reset(page);
    await newQueue(page);
    const st = await page.evaluate(async () => {
      // a one-second tone as the clip, made here
      const rate = 8000, n = rate, b = new DataView(new ArrayBuffer(44 + n * 2));
      const w = (o: number, t: string) => { for (let i = 0; i < t.length; i++) b.setUint8(o + i, t.charCodeAt(i)); };
      w(0, 'RIFF'); b.setUint32(4, 36 + n * 2, true); w(8, 'WAVEfmt '); b.setUint32(16, 16, true); b.setUint16(20, 1, true); b.setUint16(22, 1, true);
      b.setUint32(24, rate, true); b.setUint32(28, rate * 2, true); b.setUint16(32, 2, true); b.setUint16(34, 16, true); w(36, 'data'); b.setUint32(40, n * 2, true);
      for (let i = 0; i < n; i++) b.setInt16(44 + i * 2, Math.round(Math.sin(i / rate * 2 * Math.PI * 440) * 8000), true);
      const url = URL.createObjectURL(new Blob([b.buffer], { type: 'audio/wav' }));
      const f = (window as any).flacie;
      f.djLevels(90, 20); f.djCue(true);
      const before = f.djState().swooshes;
      f.djPlay(url, '', false, 0, 0);
      await new Promise((r) => setTimeout(r, 700));
      const s = f.djState(); f.djStop();
      return { ...s, swooshed: s.swooshes - before };
    });
    expect(st.under).toBeCloseTo(0.2);
    expect(st.cue).toBe(true);
    expect(st.swooshed).toBe(1);
    // Aura clips are about -27 LUFS against -8 for mastered pop: the voice is lifted 12 dB, with a limiter so it never clips (owner, 27 Oct: "hard to hear")
    expect(st.chain).toEqual({ highpass: 'highpass', hz: 100, presence: 'peaking', presenceHz: 3000, compressor: true, liftDb: 12, limiter: true });
    // the cue sound off: no swoosh before the next clip
    const off = await page.evaluate(() => { const f = (window as any).flacie; f.djCue(false); return f.djState().cue; });
    expect(off).toBe(false);
  });

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

  // the owner asked for a warmer voice "like Spotify's DJ" and to hear every voice: Aura-2 voices in the picker, a sample button each, best ones first
  test('the Dashboard offers the Aura-2 voices, with a sample button for every voice', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await live(page, '/dashboard');
    const block = page.locator('#dj');
    await expect(block).toBeVisible({ timeout: 20_000 });
    expect(await page.locator('#djv option[value^="aura-2:"]').count()).toBe(40);
    expect(await page.locator('#djv option[value^="aura-1:"]').count()).toBe(12);
    // FLACie's own voice (Kokoro on the server, no daily limit) first, the owner's pick marked
    expect(await page.locator('#djv option[value^="kokoro:"]').count()).toBe(18);
    expect(await page.locator('#djv option').first().getAttribute('value')).toBe('kokoro:am_liam');
    await block.locator('summary', { hasText: 'Hear each voice' }).click();
    await expect(block.locator('.dj-voices-kokoro button').first()).toHaveText('Liam (American man), your pick');
    await expect(block.locator('.dj-voices-best button')).toHaveCount(11);
    await expect(block.locator('.dj-voices-best button').first()).toHaveText(/^Aries \(warm/);
    await expect(block.getByText('A sample is made once and kept')).toBeVisible();
    await block.scrollIntoViewIfNeeded();
    await page.screenshot({ path: 'test-results/shots/dj-voices.png', fullPage: false, clip: (await block.boundingBox())! });
  });
});
