import { test, expect, Page } from '@playwright/test';

// The click-wheel iPods' controls are the real ones: the 1st and 2nd generations' four buttons on a ring around the scroll wheel, the 3rd generation's row of
// four touch buttons above a wheel with nothing printed on it, and the iPod mini 2nd generation's wheel lettered in the colour of its body (the 1st's in grey).
async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(800);
}
/** Opens /ipod showing these prefs (seeded before the page loads). */
async function openIpod(page: Page, prefs: Record<string, unknown>) {
  await page.addInitScript((p) => { try { const nav = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined; if (nav?.type !== 'reload') localStorage.setItem('flacie.ipod', JSON.stringify(p)); } catch { } }, prefs);
  await page.goto('/ipod');
  await expect(page.getByTestId('ipod')).toHaveAttribute('data-model', String(prefs.model), { timeout: 20_000 });
  await page.waitForTimeout(600);
}
const title = (page: Page) => page.getByTestId('ipod-title');
const playState = (page: Page) => page.locator('[data-testid=ipod-header] .lcd-play').first();
type Box = { x: number; y: number; width: number; height: number };
const mid = (b: Box) => ({ x: b.x + b.width / 2, y: b.y + b.height / 2 });
const box = async (page: Page, sel: string) => (await page.locator(sel).first().boundingBox())!;
/** A colour's hue (degrees) and saturation (0..1, HSL) from a CSS rgb() string. */
const hsl = (css: string) => {
  const [r, g, b] = (css.match(/[\d.]+/g) ?? ['0', '0', '0']).slice(0, 3).map((v) => +v / 255);
  const max = Math.max(r, g, b), min = Math.min(r, g, b), l = (max + min) / 2, d = max - min;
  const s = d === 0 ? 0 : d / (1 - Math.abs(2 * l - 1));
  const h = d === 0 ? 0 : max === r ? 60 * (((g - b) / d) % 6) : max === g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
  return { h: (h + 360) % 360, s, l };
};
const hueGap = (a: number, b: number) => { const d = Math.abs(a - b) % 360; return d > 180 ? 360 - d : d; };

test.describe.serial('the click-wheel iPods have their real controls', () => {
  test('the 3rd generation: four round touch buttons in a row between the screen and a wheel with nothing printed on it, and they work', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page); await page.setViewportSize({ width: 1366, height: 900 });
    await openIpod(page, { model: 'ipod3', look: 1 });
    await expect(page.getByTestId('ipod-device')).toHaveAttribute('data-controls', 'row');
    // the row: left to right previous, MENU, play/pause, next; round, on one line, under the screen and over the wheel
    const lcd = await box(page, '[data-testid=ipod-lcd]'), wheel = await box(page, '[data-testid=ipod-wheel]'), dev = await box(page, '[data-testid=ipod-device]');
    const keys = [];
    for (const k of ['prev', 'menu', 'play', 'next']) keys.push(await box(page, `[data-testid=ipod-keyrow] [data-testid=ipod-${k}]`));
    for (let i = 0; i < keys.length; i++) {
      const b = keys[i];
      expect(Math.abs(b.width - b.height), 'round').toBeLessThan(1.5);
      expect(Math.abs(mid(b).y - mid(keys[0]).y), 'one row').toBeLessThan(1);
      if (i > 0) expect(mid(b).x).toBeGreaterThan(keys[i - 1].x + keys[i - 1].width);
      expect(b.y, 'under the screen').toBeGreaterThan(lcd.y + lcd.height);
      expect(b.y + b.height, 'over the wheel').toBeLessThan(wheel.y);
      expect(b.x).toBeGreaterThan(dev.x); expect(b.x + b.width).toBeLessThan(dev.x + dev.width);
    }
    await expect(page.locator('[data-testid=ipod-keyrow] [data-testid=ipod-menu]')).toHaveText('MENU');
    // the buttons' lettering is lit red-orange; the wheel has nothing printed on it
    const lit = hsl(await page.locator('[data-testid=ipod-keyrow] [data-testid=ipod-menu]').evaluate((e) => getComputedStyle(e).color));
    expect(lit.s).toBeGreaterThan(0.5); expect(lit.h < 40 || lit.h > 345).toBe(true);
    await expect(page.locator('[data-testid=ipod-wheel] .wl')).toHaveCount(0);
    await expect(page.locator('[data-testid=ipod-wheel] [data-testid^=ipod-]:not([data-testid=ipod-center])')).toHaveCount(0);

    // MENU goes back; held, it goes home
    await page.getByTestId('ipod-keys').focus();
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await expect(title(page)).toHaveText('Browse');
    await page.keyboard.press('Enter'); await expect(title(page)).toHaveText('Artists');
    await page.locator('[data-testid=ipod-keyrow] [data-testid=ipod-menu]').click();
    await expect(title(page)).toHaveText('Browse');
    await page.keyboard.press('Enter'); await expect(title(page)).toHaveText('Artists');
    const m = mid(await box(page, '[data-testid=ipod-keyrow] [data-testid=ipod-menu]'));
    await page.mouse.move(m.x, m.y); await page.mouse.down(); await page.waitForTimeout(800); await page.mouse.up();
    await expect(title(page)).toHaveText('iPod');
    // the wheel only scrolls (a tap on its ring is not a button: the buttons are the row); its middle still selects
    await page.keyboard.press('Enter'); await expect(title(page)).toHaveText('Browse');                // (home keeps the highlight on Browse)
    const c = mid(wheel), r = wheel.width / 2;
    await page.mouse.click(c.x, c.y - r * 0.84); await page.waitForTimeout(400);
    await expect(title(page)).toHaveText('Browse');
    await page.mouse.click(c.x, c.y); await expect(title(page)).toHaveText('Artists');

    // play/pause, next and previous
    await page.keyboard.press('Escape'); await page.keyboard.press('Escape');
    for (let i = 0; i < 10; i++) await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('ipod-np')).toBeVisible({ timeout: 20_000 });
    const first = (await page.getByTestId('ipod-np-title').textContent())!.trim();
    await page.locator('[data-testid=ipod-keyrow] [data-testid=ipod-next]').click();
    await expect(page.getByTestId('ipod-np-title')).not.toHaveText(first, { timeout: 10_000 });
    await page.locator('[data-testid=ipod-keyrow] [data-testid=ipod-prev]').click();
    await expect(page.getByTestId('ipod-np-title')).toHaveText(first, { timeout: 10_000 });
    // (the mock's songs are only a few seconds long, so the state is sampled and the button pressed again until the press shows)
    await expect(async () => {
      const before = (await playState(page).textContent())!.trim();
      await page.locator('[data-testid=ipod-keyrow] [data-testid=ipod-play]').click();
      await expect(playState(page)).not.toHaveText(before, { timeout: 1_500 });
    }).toPass({ timeout: 15_000 });

    // the wheel-only look keeps the row between the screen and the wheel
    await page.locator('.ipod-panel .ipod-chip[data-look="wheel"]').click();
    await expect(page.getByTestId('ipod')).toHaveAttribute('data-look', 'wheel');
    const l2 = await box(page, '[data-testid=ipod-lcd]'), w2 = await box(page, '[data-testid=ipod-wheel]'), k2 = await box(page, '[data-testid=ipod-keyrow] [data-testid=ipod-menu]');
    expect(k2.y).toBeGreaterThan(l2.y + l2.height); expect(k2.y + k2.height).toBeLessThan(w2.y);
  });

  test('the 1st and 2nd generations: MENU, previous, next and play/pause on a ring of four buttons around the scroll wheel, no bubbles', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page); await page.setViewportSize({ width: 1366, height: 900 });
    for (const [model, kind] of [['ipod1', 'scroll'], ['ipod2', 'touch']]) {
      await page.goto('about:blank');
      await openIpod(page, { model, look: 1 });
      await expect(page.getByTestId('ipod-device')).toHaveAttribute('data-controls', kind);
      await expect(page.locator('.wb')).toHaveCount(0);
      await expect(page.getByTestId('ipod-keyrow')).toHaveCount(0);
      const wheel = await box(page, '[data-testid=ipod-wheel]'), scroll = await box(page, '[data-testid=ipod-wheel] .wscroll'), centre = await box(page, '[data-testid=ipod-center]');
      const c = mid(wheel), R = wheel.width / 2, rs = scroll.width / 2;
      expect(rs, model + ': the scroll wheel inside the ring').toBeLessThan(R * 0.85); expect(rs).toBeGreaterThan(R * 0.6);
      expect(centre.width / 2).toBeLessThan(rs * 0.6);
      // each label sits on the ring, outside the scroll wheel, at its own side
      const at: Record<string, { x: number; y: number }> = {};
      for (const k of ['menu', 'prev', 'next', 'play']) {
        const p = mid(await box(page, `[data-testid=ipod-wheel] [data-testid=ipod-${k}]`)); at[k] = p;
        const d = Math.hypot(p.x - c.x, p.y - c.y);
        expect(d, `${model} ${k} on the ring`).toBeGreaterThan(rs); expect(d).toBeLessThan(R);
      }
      expect(at.menu.y).toBeLessThan(c.y - rs); expect(at.play.y).toBeGreaterThan(c.y + rs);
      expect(at.prev.x).toBeLessThan(c.x - rs); expect(at.next.x).toBeGreaterThan(c.x + rs);
      // the ring's buttons work: MENU at the top goes back
      await page.getByTestId('ipod-keys').focus();
      await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await expect(title(page)).toHaveText('Browse');
      await page.mouse.click(at.menu.x, at.menu.y); await expect(title(page)).toHaveText('iPod');
    }
  });

  test('the iPod mini: the 2nd generation\'s wheel is lettered in the colour of its body, the 1st generation\'s in grey', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page); await page.setViewportSize({ width: 1366, height: 900 });
    const swatches = page.locator('.ipod-panel .ipod-chip.swatch');
    const read = () => page.evaluate(() => {
      const label = getComputedStyle(document.querySelector('[data-testid=ipod-wheel] .wl-menu')!).color;
      const body = getComputedStyle(document.querySelector('[data-testid=ipod-device]')!).getPropertyValue('--bottom').trim();
      const ring = getComputedStyle(document.querySelector('[data-testid=ipod-device]')!).getPropertyValue('--wr2').trim();
      const toRgb = (hex: string) => `rgb(${parseInt(hex.slice(1, 3), 16)}, ${parseInt(hex.slice(3, 5), 16)}, ${parseInt(hex.slice(5, 7), 16)})`;
      return { label, body: toRgb(body), ring: toRgb(ring) };
    });
    for (const [model, count] of [['mini2', 4], ['mini', 5]] as const) {
      await page.goto('about:blank');
      await openIpod(page, { model, look: 1 });
      await expect(swatches).toHaveCount(count);
      for (let i = 0; i < count; i++) {
        const was = await page.getByTestId('ipod').getAttribute('data-color');
        await swatches.nth(i).click();
        if (i > 0) await expect(page.getByTestId('ipod')).not.toHaveAttribute('data-color', was!);
        const name = (await page.getByTestId('ipod').getAttribute('data-color'))!;
        await page.waitForTimeout(150);
        const { label, body, ring } = await read();
        const L = hsl(label), B = hsl(body), W = hsl(ring);
        if (model === 'mini') { expect(L.s, `${model} ${name}: grey lettering`).toBeLessThan(0.12); continue; }
        if (name === 'Silver') { expect(L.s, 'silver lettering on the silver mini').toBeLessThan(0.15); continue; }
        expect(L.s, `${model} ${name}: coloured lettering`).toBeGreaterThan(0.35);
        expect(hueGap(L.h, B.h), `${model} ${name}: the body's colour`).toBeLessThan(20);
        expect(L.l, `${model} ${name}: readable on the wheel`).toBeLessThan(W.l - 0.15);
      }
    }
  });
});
