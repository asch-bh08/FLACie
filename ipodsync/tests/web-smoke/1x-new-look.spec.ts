import { test, expect, Page } from '@playwright/test';

// The full-screen player in design "I" (docs/superpowers/specs/design-2026-10-19): the cover runs full-bleed across the top and fades into the cover's
// ground colour, a serif title with its last word in italics, a RING seek bar (the song's loudness as bars round a rounded-square play button), tinted pill
// previous / next, nothing important in the bottom 100px of a phone, and on a PC a cover card, a wide panel and a bar along the bottom.

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
  await page.waitForTimeout(700);
}

test.describe('phone (390 x 844)', () => {
  test.beforeEach(async ({ page }) => { await page.setViewportSize({ width: 390, height: 844 }); await signIn(page); await openPlayer(page); });

  test('the cover is full-bleed across the top and fades into the ground', async ({ page }) => {
    const cover = (await page.locator('.ls-hero').boundingBox())!;
    expect(cover.x).toBe(0); expect(cover.y).toBe(0); expect(cover.width).toBe(390);
    expect(cover.height).toBeGreaterThan(480); expect(cover.height).toBeLessThan(600);
    await expect(page.locator('.ls-hero .ls-fade')).toHaveCount(1);
    const ground = await page.locator('.ls').evaluate((el) => getComputedStyle(el).getPropertyValue('--ground').trim());
    expect(ground).toMatch(/^rgb\(\d+, \d+, \d+\)$/);
  });

  test('the title is serif with its last word in italics, the artist is bold, and the file is a mono chip', async ({ page }) => {
    const h2 = page.locator('.ls-names h2');
    expect(await h2.evaluate((e) => getComputedStyle(e).fontFamily)).toContain('Instrument Serif');
    const words = (await h2.innerText()).trim().split(/\s+/);
    expect((await h2.locator('i').innerText()).trim()).toBe(words[words.length - 1]);
    expect(await page.locator('.ls-artist').evaluate((e) => getComputedStyle(e).fontWeight)).toBe('700');
    expect(await page.locator('.ls-chip').first().evaluate((e) => getComputedStyle(e).fontFamily)).toContain('DM Mono');
    // the top pill names the album and the track
    await expect(page.locator('.ls-where')).toHaveText(/·\s*TRACK \d+$/);
  });

  test('the play button is a rounded square in the cover colour inside a ring that is the seek bar', async ({ page }) => {
    const ring = (await page.locator('.ls-ring').boundingBox())!;
    expect(ring.width).toBeGreaterThan(150); expect(Math.abs(ring.width - ring.height)).toBeLessThan(1);
    const play = (await page.locator('.ls-play').boundingBox())!;
    expect(play.x).toBeGreaterThan(ring.x + 20); expect(play.x + play.width).toBeLessThan(ring.x + ring.width - 20);
    expect(await page.locator('.ls-play').evaluate((e) => getComputedStyle(e).borderRadius)).not.toBe('50%');
    const wave = page.locator('.ls-ring canvas.np-wave');
    await expect.poll(() => wave.evaluate((c: HTMLCanvasElement) => c.width), { timeout: 5_000 }).toBeGreaterThan(100);
    const drawn = await wave.evaluate((c: HTMLCanvasElement) => { const d = c.getContext('2d')!.getImageData(0, 0, c.width, c.height).data; let n = 0; for (let i = 3; i < d.length; i += 4) if (d[i]) n++; return n; });
    expect(drawn).toBeGreaterThan(100);
  });

  test('a tap on the ring seeks to that point of the circle (3 o\'clock is a quarter of the song)', async ({ page }) => {
    const ring = (await page.locator('.ls-ring').boundingBox())!;
    const input = page.locator('.ls-ring input[type=range]');
    const max = Number(await input.getAttribute('max'));
    await page.mouse.click(ring.x + ring.width - 8, ring.y + ring.height / 2);
    // (the stand-in's songs are a few seconds long, so the value is whole seconds: allow one second either way)
    await expect.poll(async () => Math.abs(Number(await input.inputValue()) - max / 4), { timeout: 5_000 }).toBeLessThanOrEqual(1);
  });

  test('previous and next are tinted pills, shuffle and repeat are plain', async ({ page }) => {
    const bg = (n: string) => page.locator('.ls-transport').getByRole('button', { name: n }).evaluate((e) => getComputedStyle(e).backgroundColor);
    expect(await bg('Previous')).not.toBe('rgba(0, 0, 0, 0)');
    expect(await bg('Next')).not.toBe('rgba(0, 0, 0, 0)');
    expect(await bg('Shuffle off')).toBe('rgba(0, 0, 0, 0)');
    expect(await bg('Repeat off')).toBe('rgba(0, 0, 0, 0)');
  });

  test('no main control is in the bottom 100px and there are no lyrics on the main screen', async ({ page }) => {
    for (const sel of ['.ls-transport', '.ls-actions', '.ls-times']) {
      const b = (await page.locator(sel).boundingBox())!;
      expect(b.y + b.height, sel).toBeLessThanOrEqual(844 - 90);
    }
    await expect(page.locator('.ls-lyrics')).toHaveCount(0);
    // the time is easy to read and sits right under the ring
    const ring = (await page.locator('.ls-ring').boundingBox())!, time = (await page.locator('.ls-times').boundingBox())!;
    expect(time.y - (ring.y + ring.height)).toBeLessThan(8);
    expect(parseFloat(await page.locator('.ls-times').evaluate((e) => getComputedStyle(e).fontSize))).toBeGreaterThanOrEqual(14);
    await page.locator('.ls-actions').getByRole('button', { name: 'Lyrics' }).click();
    await expect(page.locator('.ls')).toHaveClass(/panel-open/);
  });

  test('rating, speed, sleep and sound stay reachable behind one Options button', async ({ page }) => {
    await expect(page.locator('.ls-toolset')).toBeHidden();
    await page.getByRole('button', { name: 'Options', exact: true }).click();
    await expect(page.locator('.ls-toolset button[aria-label^="Rating:"]')).toBeVisible();
    await expect(page.locator('.ls-toolset button[aria-label^="Speed:"]')).toBeVisible();
    await expect(page.locator('.ls-toolset button[aria-label^="Sleep timer:"]')).toBeVisible();
    await expect(page.locator('.ls-toolset button[aria-label^="Sound:"]')).toBeVisible();
  });
});

for (const [w, h] of [[1280, 720], [1440, 900]]) {
  test.describe(`PC (${w} x ${h})`, () => {
    test.beforeEach(async ({ page }) => { await page.setViewportSize({ width: w, height: h }); await signIn(page); await openPlayer(page); });

    test('two halves: the cover, title and controls on the left, the panel on the right, the time right under the ring', async ({ page }) => {
      const box = async (s: string) => (await page.locator(s).first().boundingBox())!;
      const cover = await box('.ls-cover'), panel = await box('.ls-side'), title = await box('.ls-title'), tr = await box('.ls-transport'), frame = await box('.ls-frame'), time = await box('.ls-times');
      expect(panel.width / w).toBeGreaterThan(0.4); expect(panel.width / w).toBeLessThan(0.55);
      expect(panel.x).toBeGreaterThanOrEqual(cover.x + cover.width + 20);
      expect(tr.x + tr.width).toBeLessThanOrEqual(panel.x);
      expect(cover.y + cover.height).toBeLessThanOrEqual(title.y + 1);
      expect(title.y + title.height).toBeLessThanOrEqual(tr.y + 1);
      // the seek bar is a frame round the cover, and the time sits right under it
      expect(frame.x).toBeLessThan(cover.x); expect(frame.x + frame.width).toBeGreaterThan(cover.x + cover.width);
      expect(time.y).toBeGreaterThanOrEqual(cover.y + cover.height); expect(time.y - (frame.y + frame.height)).toBeLessThan(8);
      // no empty band above the player
      expect((await box('.ls-top')).y).toBeLessThan(30);
      expect(time.y + time.height).toBeLessThanOrEqual(h);
      expect(panel.y + panel.height).toBeLessThanOrEqual(h);
    });

    test('the Info tab: the file at a glance, its numbers as tiles, graph tabs and a live graph', async ({ page }) => {
      await page.locator('.ls-seg [role=tab]', { hasText: 'Info' }).click();
      await expect(page.locator('.info .fmt-big')).toBeVisible({ timeout: 20_000 });
      expect(await page.locator('.info-stats .stat').count()).toBeGreaterThanOrEqual(2);
      // where it plays from is a small chip beside the format, not a card in Info; the graph comes first
      await expect(page.locator('.info-from')).toHaveCount(0);
      await expect(page.locator('.ls-chips .ls-src')).not.toBeEmpty();
      expect(await page.locator('.info > *').first().getAttribute('class')).toContain('graph-box');
      await expect(page.locator('.info-stats .stat', { hasText: 'kHz rate' })).toBeVisible();
      const tabs = (await page.locator('.graph-tabs [role=tab]').allInnerTexts()).map((t) => t.trim());
      expect(tabs.slice(0, 3)).toEqual(['Spectrum', 'Visualizer', 'Spectrogram']);
      await page.locator('.graph-tabs [role=tab]', { hasText: 'Spectrogram' }).click();
      await expect(page.locator('.graph-tabs [role=tab]', { hasText: 'Spectrogram' })).toHaveAttribute('aria-selected', 'true');
      await expect(page.locator('#viz.wall')).toBeVisible();
    });

    test('a press on the frame seeks: the middle of its right side is a quarter of the song', async ({ page }) => {
      const fr = (await page.locator('.ls-frame').boundingBox())!, input = page.locator('.ls-ring input[type=range]'), max = Number(await input.getAttribute('max'));
      const drawn = await page.locator('.ls-frame canvas').evaluate((c: HTMLCanvasElement) => { const d = c.getContext('2d')!.getImageData(0, 0, c.width, c.height).data; let n = 0; for (let i = 3; i < d.length; i += 4) if (d[i]) n++; return n; });
      expect(drawn).toBeGreaterThan(200);
      await page.mouse.click(fr.x + fr.width - 14, fr.y + fr.height / 2);
      await expect.poll(async () => Math.abs(Number(await input.inputValue()) - max / 4), { timeout: 5_000 }).toBeLessThanOrEqual(1);
    });

    test('Up next is the first tab; Lyrics and Info are there too; the options show inline', async ({ page }) => {
      const tabs = await page.locator('.ls-seg [role=tab]').allInnerTexts();
      expect(tabs.map((t) => t.trim())).toEqual(['Up next', 'Lyrics', 'Info']);
      await expect(page.locator('.ls-seg [role=tab]').first()).toHaveAttribute('aria-selected', 'true');
      await expect(page.locator('.ls-toolset button[aria-label^="Rating:"]')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Options', exact: true })).toBeHidden();
      // the lyrics are in the app's own bold sans, not the title serif
      expect(await page.locator('.ls').evaluate((el) => { const o = document.createElement('ol'); o.className = 'ls-lyrics'; el.appendChild(o); const f = getComputedStyle(o).fontFamily; o.remove(); return f; })).not.toContain('Instrument Serif');
    });
  });
}
