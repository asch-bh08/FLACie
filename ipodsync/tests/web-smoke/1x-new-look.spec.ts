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
  // clicks only once the page is live: a click on the copy the server drew first goes nowhere when it is replaced
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto('/artist/Hires%20Lab');
  await ws; await page.waitForTimeout(600);
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

  test('the seek bar is a frame of the song shape round the cover card (as on a PC), with the time right under it; play is a rounded square', async ({ page }) => {
    const frame = (await page.locator('.ls-frame').boundingBox())!, cover = (await page.locator('.ls-cover').boundingBox())!, time = (await page.locator('.ls-times').boundingBox())!;
    expect(frame.width).toBeGreaterThan(250); expect(Math.abs(frame.width - frame.height)).toBeLessThan(1);
    expect(frame.x).toBeLessThan(cover.x); expect(frame.x + frame.width).toBeGreaterThan(cover.x + cover.width);
    // the time's row starts right under the frame (1g-2: the time has the row to itself)
    expect(time.y - (frame.y + frame.height)).toBeLessThan(10);
    expect(await page.locator('.ls-play').evaluate((e) => getComputedStyle(e).borderRadius)).not.toBe('50%');
    const wave = page.locator('.ls-frame canvas');
    await expect.poll(() => wave.evaluate((c: HTMLCanvasElement) => c.width), { timeout: 5_000 }).toBeGreaterThan(100);
    // (the canvas is sized before the shape arrives from the server, or from the browser working it out, so wait for the bars to be drawn)
    await expect.poll(() => wave.evaluate((c: HTMLCanvasElement) => { const d = c.getContext('2d')!.getImageData(0, 0, c.width, c.height).data; let n = 0; for (let i = 3; i < d.length; i += 4) if (d[i]) n++; return n; }), { timeout: 20_000 }).toBeGreaterThan(200);
  });

  test('a tap on the frame seeks (the middle of its right side is a quarter of the song)', async ({ page }) => {
    const fr = (await page.locator('.ls-frame').boundingBox())!;
    const input = page.locator('.ls-ring input[type=range]');
    const max = Number(await input.getAttribute('max'));
    await page.mouse.click(fr.x + fr.width - 10, fr.y + fr.height / 2);
    // (the stand-in's songs are a few seconds long, so the value is whole seconds: allow one second either way)
    await expect.poll(async () => Math.abs(Number(await input.inputValue()) - max / 4), { timeout: 5_000 }).toBeLessThanOrEqual(1);
  });

  test('previous and next are tinted rounded rectangles; Shuffle and Repeat are slots in the row of quiet actions, ink when on (1g-2, layout B)', async ({ page }) => {
    const css = (n: string) => page.locator('.ls-transport').getByRole('button', { name: n }).evaluate((e) => { const s = getComputedStyle(e); return [s.backgroundColor, s.borderTopLeftRadius]; });
    for (const n of ['Previous', 'Next']) {
      const [bg, r] = await css(n);
      expect(bg, n).not.toBe('rgba(0, 0, 0, 0)'); expect(['16px', '18px'], n).toContain(r);
    }
    await expect(page.locator('.ls-transport .ls-shuf')).toBeHidden();
    const shuf = page.locator('.ls-actions .ls-slot-shuf'), rep = page.locator('.ls-actions .ls-slot-rep');
    await expect(shuf).toBeVisible(); await expect(rep).toBeVisible();
    const words = await page.locator('.ls-actions > .ls-slot, .ls-actions .ls-more-btn').allInnerTexts();
    expect(words.map((w) => w.trim())).toEqual(['Shuffle', 'Repeat', 'Lyrics', 'More']);
    const before = await shuf.evaluate((e) => getComputedStyle(e).backgroundColor);
    await shuf.click(); await page.waitForTimeout(400);
    expect(await shuf.evaluate((e) => getComputedStyle(e).backgroundColor)).not.toBe(before);
    await shuf.click();
  });

  test('no main control is in the bottom 100px and there are no lyrics on the main screen', async ({ page }) => {
    for (const sel of ['.ls-prev', '.ls-ring', '.ls-next', '.ls-actions', '.ls-times']) {
      const b = (await page.locator(sel).boundingBox())!;
      expect(b.y + b.height, sel).toBeLessThanOrEqual(844 - 90);
    }
    await expect(page.locator('.ls-lyrics')).toHaveCount(0);
    // the time is easy to read and sits above the transport (shuffle and repeat beside it, previous / play / next below)
    const ring = (await page.locator('.ls-ring').boundingBox())!, time = (await page.locator('.ls-times').boundingBox())!;
    expect(time.y + time.height).toBeLessThanOrEqual(ring.y);
    expect(parseFloat(await page.locator('.ls-times').evaluate((e) => getComputedStyle(e).fontSize))).toBeGreaterThanOrEqual(14);
    // Info is two taps away on a phone: More, then Song info (owner, 28 Nov: the row is Shuffle, Repeat, Lyrics, More)
    await page.locator('.ls-actions .ls-more-btn').click();
    await page.getByRole('menuitem', { name: 'Song info' }).click();
    await expect(page.locator('.ls')).toHaveClass(/panel-open/);
    await expect(page.locator('.ls-seg [role=tab]', { hasText: 'Info' })).toHaveAttribute('aria-selected', 'true');
    await page.getByRole('button', { name: 'Back to the cover' }).click();
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
      // the transport is in the now-playing bar along the bottom, below both halves
      expect(tr.y).toBeGreaterThanOrEqual(panel.y + panel.height - 1);
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
      await expect(page.locator('.ls-chips .ls-from')).not.toBeEmpty();
      expect(await page.locator('.info > *').first().getAttribute('class')).toContain('graph-box');
      await expect(page.locator('.info-stats .stat', { hasText: 'kHz rate' })).toBeVisible();
      const tabs = (await page.locator('.graph-tabs [role=tab]').allInnerTexts()).map((t) => t.trim());
      expect(tabs.slice(0, 3)).toEqual(['Spectrum', 'Visualizer', 'Spectrogram']);
      await page.locator('.graph-tabs [role=tab]', { hasText: 'Spectrogram' }).click();
      await expect(page.locator('.graph-tabs [role=tab]', { hasText: 'Spectrogram' })).toHaveAttribute('aria-selected', 'true');
      await expect(page.locator('#viz.wall')).toBeVisible();
      // the graph on the whole screen (owner, 8 Nov): fills the screen, the tabs still switch it, and its ✕ closes it (Esc too, in a real browser)
      await page.locator('.graph-full-btn').click();
      await expect.poll(() => page.locator('#viz').evaluate((c) => c.getBoundingClientRect().height)).toBeGreaterThan(page.viewportSize()!.height * .5);
      await page.screenshot({ path: `test-results/graph-full-${page.viewportSize()!.width}.png` });
      // the tabs are never cut off at the bottom (owner, 12 Nov)
      const tr = (await page.locator('.graph-box .graph-tabs').boundingBox())!, tb = (await page.locator('.graph-box .gtab').first().boundingBox())!;
      expect(tb.y + tb.height).toBeLessThanOrEqual(tr.y + tr.height + 0.5);
      await page.locator('.graph-tabs [role=tab]', { hasText: 'Spectrum' }).click();
      await expect(page.locator('#viz.curve')).toBeVisible();
      // play / pause without leaving the graph
      await page.locator('.graph-play').click();
      await expect(page.locator('.graph-play')).toHaveAttribute('aria-label', 'Play');
      await page.locator('.graph-play').click();
      await expect(page.locator('.graph-play')).toHaveAttribute('aria-label', 'Pause');
      await expect(page.locator('.graph-play')).toBeInViewport();
      // the song and a seek bar in full screen (owner, 11 Nov), and the two new graphs
      await expect(page.locator('.graph-now .graph-song b')).toHaveText(/\S/);
      await expect(page.locator('.graph-seek input')).toBeInViewport();
      // shuffle and repeat beside the transport, as in the player bar (owner, 12 Nov)
      await expect(page.locator('.graph-transport button[aria-label^="Shuffle"]')).toBeInViewport();
      await expect(page.locator('.graph-transport button[aria-label^="Repeat"]')).toBeInViewport();
      for (const [tab, cls] of [['Circle', 'ring'], ['Waveform', 'scope'], ['Orbit', 'orbit'], ['Skyline', 'skyline'], ['Bars', 'bars'], ['Aura', 'aura']]) {
        await page.locator('.graph-tabs [role=tab]', { hasText: tab }).click();
        await expect(page.locator('#viz.' + cls)).toBeVisible();
        await page.waitForTimeout(600);
        await page.screenshot({ path: `test-results/graph-${cls}-${page.viewportSize()!.width}.png` });
      }
      await page.locator('.graph-full-btn').click();
      await expect.poll(() => page.locator('#viz').evaluate((c) => c.getBoundingClientRect().height)).toBeLessThan(page.viewportSize()!.height * .5);
      // the arrow reaches the graphs off the end of the row (owner, 12 Nov: "I can't reach the last graph")
      const row = page.locator('.graph-tabs').first();
      if (await row.evaluate((e) => e.scrollWidth > e.clientWidth + 2)) {
        await page.locator('.graph-tabs-arrow.next').first().click();
        await expect.poll(() => row.evaluate((e) => e.scrollLeft)).toBeGreaterThan(0);
        await page.locator('.graph-tabs-arrow.prev').first().click();
        await expect.poll(() => row.evaluate((e) => e.scrollLeft)).toBe(0);
      }
    });

    test('a press on the frame seeks: the middle of its right side is a quarter of the song', async ({ page }) => {
      const fr = (await page.locator('.ls-frame').boundingBox())!, input = page.locator('.ls-ring input[type=range]'), max = Number(await input.getAttribute('max'));
      // (the shape arrives after the canvas is sized, so wait for the bars to be drawn)
      await expect.poll(() => page.locator('.ls-frame canvas').evaluate((c: HTMLCanvasElement) => { const d = c.getContext('2d')!.getImageData(0, 0, c.width, c.height).data; let n = 0; for (let i = 3; i < d.length; i += 4) if (d[i]) n++; return n; }), { timeout: 20_000 }).toBeGreaterThan(200);
      await page.mouse.click(fr.x + fr.width - 14, fr.y + fr.height / 2);
      await expect.poll(async () => Math.abs(Number(await input.inputValue()) - max / 4), { timeout: 5_000 }).toBeLessThanOrEqual(1);
    });

    test('Up next is the first tab; Lyrics and Info are there too; the options show inline', async ({ page }) => {
      const tabs = await page.locator('.ls-seg [role=tab]').allInnerTexts();
      expect(tabs.map((t) => t.trim())).toEqual(['Up next', 'Lyrics', 'Info']);
      await expect(page.locator('.ls-seg [role=tab]').first()).toHaveAttribute('aria-selected', 'true');
      await expect(page.locator('.ls-tiles .eq-menu .chipbtn')).toBeVisible();   // Web A: the options are the bar's tiles (rating, speed and sleep in More)
      await expect(page.getByRole('button', { name: 'Options', exact: true })).toBeHidden();
      // the lyrics are in the app's own bold sans, not the title serif
      expect(await page.locator('.ls').evaluate((el) => { const o = document.createElement('ol'); o.className = 'ls-lyrics'; el.appendChild(o); const f = getComputedStyle(o).fontFamily; o.remove(); return f; })).not.toContain('Instrument Serif');
    });
  });
}

// Task 2: the seek bar's shape is there the moment a song starts. While one song plays (player open or not), the next two songs' shapes are fetched (or
// worked out) in the background and kept, so the next song draws at once without asking again. Not on mobile data.
test.describe('the shape is ready instantly', () => {
  const waveP = (u: string) => new URL(u).searchParams.get('p') ?? '';
  async function playAlbum(page: Page) {
    // clicks only once the page is live: a click on the copy the server drew first goes nowhere when it is replaced
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto('/artist/Hires%20Lab');
  await ws; await page.waitForTimeout(600);
    await page.getByRole('link', { name: /Lab Album/ }).first().click();
    await page.locator('.tracks .track-main').first().click();
    await expect(page.locator('.bar-text b')).toBeVisible({ timeout: 20_000 });
  }

  test('the next two songs get their shapes while one plays, and the next one is drawn without asking again', async ({ page }) => {
    test.setTimeout(120_000);
    const asked: string[] = [];
    page.on('request', (r) => { if (r.url().includes('/api/wave') && r.method() === 'GET') asked.push(waveP(r.url())); });
    await signIn(page);
    // the server already keeps the album's shapes, as it does once anyone has played them (whether it does depends on which tests ran before: the page only
    // looks ahead for shapes the server has, and asks for the rest when a song starts)
    for (let i = 1; i <= 8; i++) {
      const p = `http://127.0.0.1:${process.env.MOCK_PORT || 8196}/Audio/h${i}/stream?static=true`;
      await expect.poll(async () => (await page.request.post('/api/wave?p=' + encodeURIComponent(p), { data: Array.from({ length: 200 }, (_, b) => 40 + (b % 50)) })).status(), { timeout: 20_000 }).toBe(204);
    }
    await playAlbum(page);
    // the player is not even open: two upcoming songs are asked about
    await expect.poll(() => new Set(asked).size, { timeout: 30_000 }).toBeGreaterThanOrEqual(2);
    const early = [...new Set(asked)];
    // the next song: its shape is already here, so opening the player on it asks nothing new for it
    await page.getByRole('button', { name: 'Next' }).first().click();
    await page.getByRole('button', { name: 'Open Now Playing' }).click();
    await expect(page.locator('.ls-names h2')).toBeVisible();
    await page.waitForTimeout(1500);
    const now = await page.evaluate(() => (window as any).flacie.npWaveFor?.() ?? '');
    expect(early).toContain(now);
    expect(asked.filter((p) => p === now)).toHaveLength(1);
  });

  test('on mobile data nothing is fetched ahead', async ({ page }) => {
    test.setTimeout(120_000);
    await page.addInitScript(() => Object.defineProperty(navigator, 'connection', { value: { type: 'cellular', saveData: false, addEventListener() { }, removeEventListener() { } }, configurable: true }));
    const asked: string[] = [];
    page.on('request', (r) => { if (r.url().includes('/api/wave')) asked.push(r.url()); });
    await signIn(page);
    await playAlbum(page);
    await page.waitForTimeout(6000);
    expect(asked).toHaveLength(0);
  });
});

test.describe('the full-screen player bar on a PC', () => {
  test('every button sits in one bar along the bottom with no seek bar in it, all one size, and its menus open upwards on screen', async ({ page }) => {
    test.setTimeout(120_000);
    await page.setViewportSize({ width: 1440, height: 900 });
    await signIn(page); await openPlayer(page);
    const bar = (await page.locator('.ls-dock').boundingBox())!;
    expect(bar.y + bar.height).toBeGreaterThanOrEqual(898);
    // Web A (owner, 8 Nov): Add, Lyrics, More on the left; Fav, EQ, Sound on the right
    for (const sel of ['.ls-transport', '.ls-dock-l button[aria-label="Add to playlist"]', '.ls-dock-l button[aria-label="Share"]', '.ls-dock .np-more', '.ls-tiles .ls-dock-heart', '.ls-tiles .eq-menu .chipbtn', '.ls-tiles .ls-volpill']) {
      const b = (await page.locator(sel).first().boundingBox())!;
      expect(b.y, sel).toBeGreaterThanOrEqual(bar.y - 1); expect(b.y + b.height, sel).toBeLessThanOrEqual(bar.y + bar.height + 1);
    }
    // like the player bar on Home (owner, 12 Nov), the buttons on both sides one size, the DJ on the left and the mic on the right so they balance;
    // no seek bar in the bar ("remove the mini seek bar": the frame round the cover is the seek bar)
    await expect(page.locator('.ls-dock input[type=range][aria-label="Seek"]')).toHaveCount(0);
    await expect(page.locator('.ls-dock-l .ls-dj')).toBeVisible(); await expect(page.locator('.ls-dock-r .ls-dock-mic button')).toBeVisible();
    // the even dock (owner, 27 Nov: "make it even"): the left (5 slots) and the right (3 slots and the volume, two slots wide) are the same width
    // (measured over the buttons themselves: the groups' boxes can be a few px wider than what is drawn in them)
    const extent = (sel: string) => page.evaluate((s) => { const rs = [...document.querySelectorAll(s)].map((e) => e.getBoundingClientRect()).filter((r) => r.width > 0);
      return { l: Math.min(...rs.map((r) => r.left)), r: Math.max(...rs.map((r) => r.right)) }; }, sel);
    const lg = await extent('.ls-dock-l > *, .ls-dock-l .devpick'), rg = await extent('.ls-tiles > *, .ls-tiles .eq-menu, .ls-tiles .dj-mic');
    expect(Math.abs((lg.r - lg.l) - (rg.r - rg.l))).toBeLessThanOrEqual(2);
    // every dock button is the same slot: one width, the kit's slot height (60) and corner (18), its word under the icon; shuffle and repeat in the
    // transport are slot-tall squares, previous / play / next slot-tall too
    const slots = await page.locator('.ls-dock-l > button, .ls-dock-l .devpick button, .ls-dock-mic button, .ls-dock .np-more.d .ls-more-btn, .ls-tiles > button, .ls-tiles .eq-menu .chipbtn').evaluateAll((els) => els.filter((e) => (e as HTMLElement).offsetParent).map((e) => {
      const r = e.getBoundingClientRect(), ic = e.querySelector('svg')!.getBoundingClientRect(), word = [...e.querySelectorAll('span')].find((s) => (s as HTMLElement).offsetParent && s.textContent!.trim());
      return { n: e.getAttribute('aria-label'), w: Math.round(r.width), h: Math.round(r.height), r: getComputedStyle(e).borderTopLeftRadius, x: r.left, right: r.right, word: word ? word.getBoundingClientRect().top >= ic.bottom - 1 : false };
    }));
    expect(slots.length).toBe(8);
    for (const s of slots) { expect(s.w, s.n!).toBe(slots[0].w); expect(s.h, s.n!).toBe(60); expect(s.r, s.n!).toBe('18px'); expect(s.word, `${s.n} has its word under the icon`).toBe(true); }
    const vol = (await page.locator('.ls-tiles .ls-volpill').boundingBox())!;
    expect(Math.round(vol.height)).toBe(60);
    // the Volume slot's slider has the slot's width (owner, 28 Nov: it was squeezed between the mute and options buttons, under a word)
    expect((await page.locator('.ls-tiles .ls-volpill-bar').boundingBox())!.width).toBeGreaterThan(60);
    // and no text in the slot (owner, 28 Nov: "just remove the text")
    expect(await page.locator('.ls-tiles .ls-volpill').evaluate((e) => [...e.querySelectorAll('*')].filter((x) => x.childNodes.length && [...x.childNodes].some((n) => n.nodeType === 3 && n.textContent!.trim()) && getComputedStyle(x).display !== 'none' && (x as HTMLElement).offsetParent !== null).length)).toBe(0);
    // the gaps are all the same, on both sides
    const lefts = slots.filter((s) => s.right <= vol.x).sort((a, b) => a.x - b.x);
    const gaps = lefts.slice(1).map((s, i) => Math.round(s.x - lefts[i].right));
    gaps.push(Math.round(vol.x - lefts[lefts.length - 1].right));
    const inGroups = gaps.filter((g) => g < 100);   // (not the one across the transport)
    expect(inGroups).toHaveLength(gaps.length - 1);
    for (const g of inGroups) expect(Math.abs(g - gaps[0])).toBeLessThanOrEqual(1);
    expect(Math.abs(vol.width - (2 * slots[0].w + gaps[0]))).toBeLessThanOrEqual(1);
    // balanced round the transport: as far from its left as the right group is from its right
    const tr = (await page.locator('.ls-transport').boundingBox())!;
    expect(Math.abs((tr.x - lg.r) - (rg.l - (tr.x + tr.width)))).toBeLessThanOrEqual(3);
    const transport = await page.locator('.ls-transport .ls-shuf, .ls-transport .ls-rep, .ls-transport .ls-pillbtn, .ls-transport .ls-play').evaluateAll((els) => els.map((e) => { const r = e.getBoundingClientRect(); return [Math.round(r.width), Math.round(r.height)]; }));
    expect(transport).toHaveLength(5);
    for (const [, h] of transport) expect(h).toBe(60);
    expect(transport[0][0]).toBe(60); expect(transport[4][0]).toBe(60);   // shuffle and repeat are squares
    // the top bar holds only close, where it plays from and Retro
    await expect(page.locator('.ls-top .ls-toolset')).toHaveCount(0);
    // More opens upwards, fully on screen
    const more = page.locator('.ls-dock .np-more button').first();
    const mb = (await more.boundingBox())!;
    await more.click();
    const menu = (await page.locator('.ls-dock .np-menu').boundingBox())!;
    // above its button, fully on screen
    expect(menu.y).toBeGreaterThanOrEqual(0); expect(menu.y + menu.height).toBeLessThanOrEqual(mb.y + 1);
  });

  test('Web A: the left and right tiles sit evenly round the transport; the rest is in More; Sound pops up on hover with the volume', async ({ page }) => {
    test.setTimeout(120_000);
    await page.setViewportSize({ width: 1440, height: 900 });
    await signIn(page); await openPlayer(page);
    await expect(page.locator('.ls-toolset')).toBeHidden();
    await expect(page.locator('.ls-dock .vol')).toBeHidden();
    const tr = (await page.locator('.ls-transport').boundingBox())!;
    const left = (await page.locator('.ls-dock-l').boundingBox())!, right = (await page.locator('.ls-tiles').boundingBox())!;
    expect(left.x + left.width).toBeLessThan(tr.x); expect(right.x).toBeGreaterThan(tr.x + tr.width);
    // the left tiles line up with the title above them; the right ones are as far in from the right edge
    const title = (await page.locator('.ls-names h2').boundingBox())!;
    expect(Math.abs(left.x - title.x)).toBeLessThanOrEqual(3);
    expect(Math.abs((1440 - (right.x + right.width)) - left.x)).toBeLessThanOrEqual(3);
    await page.screenshot({ path: 'test-results/web-a-bar.png' });
    // the volume is in the bar itself (owner, 11 Nov: "quick to reach"): no pop-up to open first. Set, clicked, dragged, nudged with the keys and the wheel
    const bar = page.locator('.ls-volpill-bar');
    await expect(bar).toBeVisible();
    const vol = () => page.evaluate(() => Math.round((window as any).flacie.volume() * 100));
    await bar.fill('40');
    await expect.poll(vol).toBe(40);
    await expect(page.locator('.ls-volpill-bar')).toHaveAttribute('aria-valuetext', '40%');
    const tb = (await bar.boundingBox())!;
    await page.mouse.click(tb.x + tb.width * .9, tb.y + tb.height / 2);
    await expect.poll(vol).toBeGreaterThan(80);
    await page.mouse.move(tb.x + tb.width * .85, tb.y + tb.height / 2); await page.mouse.down();
    await page.mouse.move(tb.x + tb.width * .2, tb.y + tb.height / 2, { steps: 6 }); await page.mouse.up();
    await expect.poll(vol).toBeLessThan(35);
    const set = Number(await bar.inputValue());
    await bar.focus(); await page.keyboard.press('ArrowUp');
    await expect.poll(() => bar.inputValue()).toBe(String(set + 1));
    await page.mouse.move(tb.x + tb.width / 2, tb.y + tb.height / 2); await page.mouse.wheel(0, -100);
    await expect.poll(() => bar.inputValue()).toBe(String(set + 6));
    await expect.poll(vol).toBe(set + 6);
    // mute and back
    await page.locator('.ls-volpill-mute').click();
    await expect.poll(vol).toBe(0);
    await page.locator('.ls-volpill-mute').click();
    await expect.poll(vol).toBeGreaterThan(0);
    // the sound options open on a click, everything inside the pop-up
    await page.getByRole('button', { name: 'Sound options' }).click();
    const pop = page.locator('.ls-volpill-pop');
    await expect(pop.getByRole('switch', { name: 'Even out volume' })).toBeVisible();
    const pb = (await pop.boundingBox())!;
    expect(pb.y).toBeGreaterThanOrEqual(0);
    for (const el of await pop.locator('.switch, .seg').all()) {
      const b = (await el.boundingBox())!;
      expect(b.x).toBeGreaterThanOrEqual(pb.x); expect(b.x + b.width).toBeLessThanOrEqual(pb.x + pb.width);
    }
    await page.screenshot({ path: 'test-results/web-a-sound.png' });
    await pop.getByRole('button', { name: 'Done' }).click();
    await expect(pop).toHaveCount(0);
    // a click outside it closes it too (owner, 12 Nov: "make this one clickable to exit it")
    await page.getByRole('button', { name: 'Sound options' }).click();
    await expect(pop).toBeVisible();
    await page.mouse.click(700, 300);
    await expect(pop).toHaveCount(0);
    // More holds the rating, speed and the sleep timer (paused first: the test songs are 3 s long and a new music track starts at 1x)
    await page.mouse.move(10, 10);
    await page.locator('.ls-play[aria-label="Pause"]').click();
    await page.locator('.ls-dock .np-more button').first().click();
    const menu = page.locator('.ls-dock .np-menu');
    await menu.getByRole('menuitem', { name: /^Speed/ }).click();
    await menu.getByRole('menuitemradio', { name: '1.5x' }).click();
    await page.locator('.ls-dock .np-more button').first().click();
    await expect(menu.getByRole('menuitem', { name: /^Speed/ })).toContainText('1.5x');
    await expect(menu.getByRole('menuitem', { name: /^Sleep timer/ })).toBeVisible();
    await expect(menu.getByRole('menuitem', { name: /^Rate…/ })).toBeVisible();
    await page.screenshot({ path: 'test-results/web-a-more.png' });
  });
});
