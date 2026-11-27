import { test, expect, Page } from '@playwright/test';

// The now-playing bar in design "I" (docs/superpowers/specs/design-2026-10-19/I-Bar.dc.html and J-Home-phone.html): a serif title with its last word in italics,
// the format as a mono chip, a rounded-square play button in the cover colour between tinted pill previous / next, a thin progress line along the top that is
// still the seek control, and on a phone a floating card clear of the tab bar.

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

async function play(page: Page) {
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await page.locator('.tracks .track-main').first().click();
  await expect(page.locator('.player')).toBeVisible();
  await page.waitForTimeout(1200);
}

const noSideScroll = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth);

for (const [name, w, h] of [['phone', 390, 844], ['desktop', 1440, 900]] as const) {
  test.describe(`${name} (${w} x ${h})`, () => {
    test.beforeEach(async ({ page }) => { await page.setViewportSize({ width: w, height: h }); await signIn(page); await play(page); });

    test('the title is serif with its last word in italics', async ({ page }) => {
      const b = page.locator('.bar-text b');
      expect(await b.evaluate((e) => getComputedStyle(e).fontFamily)).toContain('Instrument Serif');
      const words = (await b.innerText()).trim().split(/\s+/);
      expect((await b.locator('i').innerText()).trim()).toBe(words[words.length - 1]);
    });

    test('the play button is a rounded square in the cover colour, previous and next are tinted pills', async ({ page }) => {
      const accent = await page.locator('.player').evaluate((e) => getComputedStyle(e).getPropertyValue('--np-accent').trim());
      expect(accent).toMatch(/^rgb\(/);
      const play = page.locator('.player .play');
      expect(await play.evaluate((e) => getComputedStyle(e).backgroundColor)).toBe(accent);
      const r = parseFloat(await play.evaluate((e) => getComputedStyle(e).borderTopLeftRadius));
      const box = (await play.boundingBox())!;
      expect(r).toBeGreaterThan(8); expect(r).toBeLessThan(Math.min(box.width, box.height) / 2);
      for (const label of ['Previous', 'Next']) {
        const b = page.locator(`.player button[aria-label="${label}"]`);
        const cs = await b.evaluate((e) => ({ bg: getComputedStyle(e).backgroundColor, r: parseFloat(getComputedStyle(e).borderTopLeftRadius) }));
        expect(cs.bg).not.toBe('rgba(0, 0, 0, 0)'); expect(cs.bg).not.toBe(accent);
        expect(cs.r).toBeGreaterThanOrEqual(16);   // the kit's one button shape (owner, 26 Nov)
      }
    });

    test('the format is a mono chip', async ({ page }) => {
      const chip = page.locator('.player .bar-chip');
      await expect(chip).toBeVisible({ timeout: 20_000 });
      expect(await chip.evaluate((e) => getComputedStyle(e).fontFamily)).toContain('DM Mono');
    });

    test('the progress line is along the top and still seeks', async ({ page }) => {
      const player = (await page.locator('.player').boundingBox())!;
      const line = page.locator('.player > .bar-progress');
      const lb = (await line.boundingBox())!;
      // along the top edge on a PC, along the foot of the floating card on a phone
      const c = lb.y + lb.height / 2;
      expect(Math.min(Math.abs(c - player.y), Math.abs(c - (player.y + player.height)))).toBeLessThan(12);
      expect(lb.width).toBeGreaterThan(w * 0.6);
      await expect(line).toHaveAttribute('aria-label', 'Seek');
      await line.focus(); await page.keyboard.press('End');
      await expect.poll(async () => Number(await line.inputValue()), { timeout: 10_000 }).toBeGreaterThan(0);
    });

    test('nothing scrolls sideways', async ({ page }) => { expect(await noSideScroll(page)).toBe(true); });
  });
}

test.describe('on a phone', () => {
  test.beforeEach(async ({ page }) => { await page.setViewportSize({ width: 390, height: 844 }); await signIn(page); await play(page); });

  test('the bar floats as a rounded card clear of the 84px tab bar', async ({ page }) => {
    const p = (await page.locator('.player').boundingBox())!;
    expect(p.y + p.height).toBeLessThanOrEqual(844 - 62);
    expect(p.x).toBeGreaterThanOrEqual(8); expect(p.x + p.width).toBeLessThanOrEqual(382);
    expect(parseFloat(await page.locator('.player').evaluate((e) => getComputedStyle(e).borderTopLeftRadius))).toBeGreaterThanOrEqual(16);
    for (const sel of ['.player .play', '.player button[aria-label="Next"]', '.player button[aria-label="Stop playing and close the player"]']) {
      const b = (await page.locator(sel).first().boundingBox())!;
      expect(b.y + b.height, sel).toBeLessThanOrEqual(844 - 62);
      expect(b.x + b.width, sel).toBeLessThanOrEqual(390);
    }
  });
});

test.describe('on a PC', () => {
  test.beforeEach(async ({ page }) => { await page.setViewportSize({ width: 1440, height: 900 }); await signIn(page); await play(page); });

  test('every control of today\'s bar is still there', async ({ page }) => {
    // (Retro and the mini player are in the bar's More menu, in words: owner, 7 Nov)
    await page.locator('.player').getByRole('button', { name: 'More player options' }).click();
    for (const label of [/^Shuffle/, 'Previous', /^(Pause|Play)$/, 'Next', /^Repeat/, 'Retro', 'Mute', 'Full screen player', 'Stop playing and close the player', 'Open Now Playing'])
      await expect(page.locator('.player').getByRole(label === 'Retro' ? 'menuitem' : 'button', { name: label }).first(), String(label)).toBeVisible();
    await expect(page.locator('.player .seek input')).toBeVisible();
    await expect(page.locator('.player input[aria-label="Volume"]')).toBeVisible();
  });

  test('the revamp (owner, 27 Nov): play in the middle, the right-hand buttons one size at one gap, shuffle and repeat lit when on with no dot', async ({ page }) => {
    const play = (await page.locator('.player .play').boundingBox())!;
    expect(Math.abs(play.x + play.width / 2 - 720)).toBeLessThanOrEqual(12);
    const right = await page.locator('.player .bar-right').evaluate((el) => [...el.querySelectorAll('button')].filter((b) => (b as HTMLElement).offsetParent && !b.closest('.menu, .devpop'))
      .map((b) => { const r = b.getBoundingClientRect(); return { n: b.getAttribute('aria-label'), w: Math.round(r.width), h: Math.round(r.height), x: r.left, right: r.right, r: getComputedStyle(b).borderTopLeftRadius }; })
      .sort((a, b) => a.x - b.x));
    expect(right.length).toBeGreaterThanOrEqual(7);
    for (const b of right) { expect(b.w, b.n!).toBe(44); expect(b.h, b.n!).toBe(44); expect(b.r, b.n!).toBe('16px'); }
    const shuf = page.locator('.player .buttons [data-pip="shuffle"]');
    const look = () => shuf.evaluate((e) => ({ bg: getComputedStyle(e).backgroundColor, after: getComputedStyle(e, '::after').content, w: e.getBoundingClientRect().width, h: e.getBoundingClientRect().height }));
    if (/ on$/.test((await shuf.getAttribute('aria-label'))!)) { await shuf.click(); await expect(shuf).toHaveAttribute('aria-pressed', 'false'); }
    await page.mouse.move(0, 0);   // (no hover tint in either picture)
    const off = await look();
    await shuf.click();
    await page.mouse.move(0, 0);
    await expect(shuf).toHaveAttribute('aria-pressed', 'true');
    const on = await look();
    expect(on.bg).not.toBe(off.bg); expect(on.bg).not.toBe('rgba(0, 0, 0, 0)');
    expect(on.after === 'none' || on.after === 'normal').toBe(true);
    expect(Math.round(on.w)).toBe(Math.round(on.h));   // a square
    await shuf.click();
  });
});
