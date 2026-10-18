import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';

// Touch: the listening scenes (/ipod › Scene) and the desk look work with fingers on a phone (portrait and landscape) and a tablet: tap targets of 44 px,
// knobs turned and sliders moved by dragging with real touch events, no double-tap zoom or text selection on the controls, nothing that needs a hover.
const SIZES: [string, number, number][] = [['phone portrait', 390, 844], ['phone landscape', 844, 390], ['tablet', 768, 1024]];
test.use({ hasTouch: true, isMobile: true, viewport: { width: 390, height: 844 } });

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
const consoleErrors = (page: Page) => { const e: string[] = []; page.on('console', (m) => { if (m.type() === 'error') e.push(m.text()); }); page.on('pageerror', (x) => e.push(String(x))); return e; };
const SCENES = ['ipod', 'car', 'hifi', 'boombox', 'minidisc', 'discman'];
/** Plays the first song, then opens /ipod in a scene (or the desk look with the iPod, scene 'ipod'), at a size. */
async function open(page: Page, scene: string, size: [number, number]) {
  await page.setViewportSize({ width: size[0], height: size[1] });
  await page.goto('/songs');
  await page.locator('.track').first().locator('button.track-main').click();
  await expect(page.locator('.player')).toBeVisible();
  await page.evaluate((s) => localStorage.setItem('flacie.ipod', JSON.stringify({ model: 'classic6', look: s === 0 ? 2 : 1, panel: false, scene: s })), SCENES.indexOf(scene));
  await page.evaluate(() => (window as any).Blazor.navigateTo('/ipod'));
  await expect(page.locator(scene === 'ipod' ? '[data-testid=desk]' : `[data-testid=retro][data-scene=${scene}]`)).toBeVisible({ timeout: 20_000 });
  await page.waitForTimeout(600);
}
/** A finger: touchstart, touchmove steps and touchend on the page (CDP), the way a phone sends them. */
async function finger(page: Page, from: [number, number], to: [number, number], steps = 10) {
  const c = await page.context().newCDPSession(page);
  const pt = (x: number, y: number) => [{ x, y, id: 1 }];
  await c.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: pt(...from) });
  for (let i = 1; i <= steps; i++) { await c.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: pt(from[0] + (to[0] - from[0]) * i / steps, from[1] + (to[1] - from[1]) * i / steps) }); await page.waitForTimeout(16); }
  await c.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
  await c.detach(); await page.waitForTimeout(250);
}
const centre = async (page: Page, sel: string): Promise<[number, number]> => { const b = (await page.locator(sel).first().boundingBox())!; return [b.x + b.width / 2, b.y + b.height / 2]; };
/** The controls of the scene as a finger meets them: what is shown, not hidden or disabled; the part of each that is on screen. */
const targets = (page: Page) => page.evaluate(() => {
  const root = document.querySelector('[data-testid=retro], [data-testid=desk]')!;
  const out: { name: string; w: number; h: number }[] = [];
  root.querySelectorAll<HTMLElement>('button, input[type=range], [role=button], [role=slider], [data-ctl], [data-media]').forEach((e) => {
    if (e.closest('[aria-hidden=true], [data-testid=ipod-device], [data-testid=ios]') || (e as HTMLButtonElement).disabled || e.tabIndex < -1) return;
    const cs = getComputedStyle(e); if (cs.visibility === 'hidden' || cs.display === 'none' || cs.pointerEvents === 'none') return;
    const r = e.getBoundingClientRect(); if (r.width < 1 || r.height < 1 || r.right < 0 || r.bottom < 0 || r.left > innerWidth || r.top > innerHeight) return;
    out.push({ name: (e.getAttribute('data-testid') || e.getAttribute('data-ctl') || e.getAttribute('data-preset') || e.getAttribute('data-band') || e.className || e.tagName).toString().slice(0, 30) + ':' + (e.getAttribute('aria-label') || e.textContent || '').trim().slice(0, 20), w: Math.round(r.width), h: Math.round(r.height) });
  });
  return out;
});


// ---- what a finger does: taps and drags sent as real touch events (the page gets pointer events from them, as on a phone)
const title = (page: Page) => page.getByTestId('retro-title');
const playing = (page: Page) => page.getByTestId('retro').getAttribute('data-playing');
const ctl = (page: Page, c: string) => page.locator(`[data-testid=retro] [data-ctl=${c}]`).first();
/** A finger along a path of points (one touch, moved in steps). */
async function path(page: Page, pts: [number, number][]) {
  const c = await page.context().newCDPSession(page);
  await c.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: pts[0][0], y: pts[0][1], id: 1 }] });
  for (const [x, y] of pts.slice(1)) { await c.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x, y, id: 1 }] }); await page.waitForTimeout(16); }
  await c.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
  await c.detach(); await page.waitForTimeout(300);
}
/** A finger going round a point from one angle to another (degrees, clockwise from three o'clock). */
const arc = (cx: number, cy: number, r: number, from: number, to: number): [number, number][] => Array.from({ length: 21 }, (_, i) => { const a = (from + (to - from) * i / 20) * Math.PI / 180; return [cx + r * Math.cos(a), cy + r * Math.sin(a)] as [number, number]; });
const drag = async (page: Page, sel: string, dx: number, dy: number) => { const [x, y] = await centre(page, sel); await finger(page, [x, y], [x + dx, y + dy]); };
const num = async (l: ReturnType<Page['locator']>, a: string) => Number(await l.first().getAttribute(a));

test.describe('touch: the scenes answer a finger', () => {
  test('nothing in the styles waits for a hover', async ({ page }) => {
    await signIn(page); await page.goto('/songs');
    const hov = await page.evaluate(() => {
      const out: string[] = [];
      const walk = (rules: CSSRuleList, ok: boolean) => {
        for (const r of Array.from(rules)) {
          if (r instanceof CSSMediaRule) walk(r.cssRules, ok || /hover:\s*hover/.test(r.conditionText));
          else if (r instanceof CSSStyleRule && !ok && /:hover/.test(r.selectorText)) out.push(r.selectorText);
          else if ('cssRules' in r && !(r instanceof CSSStyleRule)) walk((r as CSSGroupingRule).cssRules, ok);
        }
      };
      for (const sh of Array.from(document.styleSheets)) { try { if (/(retro|desk)[^/]*\.css/.test(sh.href || '')) walk(sh.cssRules, false); } catch { } }
      return out;
    });
    expect(hov).toEqual([]);
  });

  test('car: taps work the keys, presets and dials; a finger drags the volume knob without muting', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await open(page, 'car', [390, 844]);
    await expect.poll(() => playing(page)).toBe('true');
    await ctl(page, 'play').tap(); await expect.poll(() => playing(page)).toBe('false');
    await ctl(page, 'play').tap(); await expect.poll(() => playing(page)).toBe('true');
    const t0 = await title(page).textContent(); await ctl(page, 'next').tap(); await expect.poll(async () => await title(page).textContent()).not.toBe(t0);
    const eq0 = await page.getByTestId('car-eq-name').textContent(); await ctl(page, 'eq').tap(); await expect(page.getByTestId('car-eq-name')).not.toHaveText(eq0!);
    await page.locator('[data-preset="2"]').tap(); await expect(page.locator('[data-preset="2"]')).toHaveClass(/on/);
    const dial = page.locator('.car-climate .dial.temp'); const r0 = await dial.getAttribute('style'); await dial.tap(); await expect.poll(() => dial.getAttribute('style')).not.toBe(r0);
    await ctl(page, 'eject').tap(); await expect(page.getByTestId('car-disc')).toHaveClass(/out/);
    await page.getByTestId('car-slot').tap(); await expect(page.getByTestId('car-disc')).not.toHaveClass(/out/);
    // the volume knob: dragged down (and up) by a finger moves the volume; the press that ends a drag does not mute
    const knob = page.getByTestId('car-knob');
    const v0 = await num(knob, 'aria-valuenow'); expect(v0).toBeGreaterThan(20);
    await drag(page, '[data-testid=car-knob]', 0, 60);
    await expect.poll(() => num(knob, 'aria-valuenow')).toBeLessThan(v0 - 10);
    const v1 = await num(knob, 'aria-valuenow'); expect(v1).toBeGreaterThan(0);
    await drag(page, '[data-testid=car-knob]', 0, -30);
    await expect.poll(() => num(knob, 'aria-valuenow')).toBeGreaterThan(v1);
    // a plain tap on the knob still mutes
    await knob.tap(); await expect.poll(() => num(knob, 'aria-valuenow')).toBe(0);
    expect(errors).toEqual([]);
  });

  test('hi-fi: taps work START, the switches and the crate; fingers turn the knobs and the tuning dial and move the pitch slider', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await open(page, 'hifi', [844, 390]);
    await expect.poll(() => playing(page)).toBe('true');
    await ctl(page, 'play').tap(); await expect.poll(() => playing(page)).toBe('false');
    await page.locator('[data-testid=retro] .hf-switches [data-ctl=play]').tap(); await expect.poll(() => playing(page)).toBe('true');
    const t0 = await title(page).textContent(); await ctl(page, 'next').tap(); await expect.poll(async () => await title(page).textContent()).not.toBe(t0);
    // the crate: flip on, then tap the front sleeve (it plays that album)
    await page.getByTestId('hifi-flip-next').tap(); await expect(page.getByTestId('hifi-flip-prev')).toBeEnabled();
    const front = page.locator('.sleeve.front'); expect(await front.getAttribute('data-album')).toBeTruthy();
    await front.tap(); await expect.poll(() => playing(page)).toBe('true');
    // the record: a tap drops the needle
    const g = (await page.getByTestId('hifi-groove').boundingBox())!;
    await page.touchscreen.tap(g.x + g.width * 0.5, g.y + g.height * 0.2);
    // BASS knob dragged up by a finger
    const bass = page.getByTestId('hifi-bass'); const b0 = await num(bass, 'aria-valuenow');
    await drag(page, '[data-testid=hifi-bass]', 0, -48); await expect.poll(() => num(bass, 'aria-valuenow')).toBeGreaterThan(b0);
    // the tuning dial: a finger round it (clockwise) turns it on
    const tune = page.getByTestId('hifi-tune'); const tb = (await tune.boundingBox())!; const cx = tb.x + tb.width / 2, cy = tb.y + tb.height / 2;
    const r0 = await tune.getAttribute('style');
    await path(page, arc(cx, cy, tb.width * 0.38, -90, -10));
    await expect.poll(() => tune.getAttribute('style')).not.toBe(r0);
    // the pitch slider: a finger across it
    const pitch = page.getByTestId('hifi-pitch'); const pb = (await pitch.boundingBox())!;
    await finger(page, [pb.x + pb.width * 0.5, pb.y + pb.height / 2], [pb.x + pb.width * 0.95, pb.y + pb.height / 2]);
    await expect.poll(async () => Number(await pitch.inputValue())).toBeGreaterThan(1.03);
    expect(errors).toEqual([]);
  });

  test('boombox: taps work the keys and deck B; fingers drag the EQ sliders and the volume', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await open(page, 'boombox', [390, 844]);
    await expect.poll(() => playing(page)).toBe('true');
    await ctl(page, 'play').tap(); await expect.poll(() => playing(page)).toBe('false');
    await ctl(page, 'play').tap(); await expect.poll(() => playing(page)).toBe('true');
    const t0 = await title(page).textContent(); await ctl(page, 'next').tap(); await expect.poll(async () => await title(page).textContent()).not.toBe(t0);
    // an EQ slider: dragged up by a finger from the middle
    const band = page.locator('[data-band="2"]'); const e0 = await band.inputValue();
    const bb = (await band.boundingBox())!;
    await finger(page, [bb.x + bb.width / 2, bb.y + bb.height * 0.5], [bb.x + bb.width / 2, bb.y + bb.height * 0.1]);
    await expect.poll(async () => Number(await band.inputValue())).toBeGreaterThan(Number(e0) + 4);
    // and down again, past the middle
    const e1 = Number(await band.inputValue());
    await finger(page, [bb.x + bb.width / 2, bb.y + bb.height * 0.2], [bb.x + bb.width / 2, bb.y + bb.height * 0.85]);
    await expect.poll(async () => Number(await band.inputValue())).toBeLessThan(e1 - 6);
    // VOL
    const vol = page.locator('[data-ctl=volume]'); const vb = (await vol.boundingBox())!; const v0 = Number(await vol.inputValue());
    await finger(page, [vb.x + vb.width / 2, vb.y + vb.height * 0.5], [vb.x + vb.width / 2, vb.y + vb.height * 0.9]);
    await expect.poll(async () => Number(await vol.inputValue())).toBeLessThan(v0);
    // deck B: a tap on its tape plays it
    const b = page.locator('.bb-deck.b .bb-cass'); await expect(b).toBeVisible(); expect(await b.getAttribute('data-album')).toBeTruthy();
    await b.tap(); await expect.poll(() => playing(page)).toBe('true');
    expect(errors).toEqual([]);
  });

  test('minidisc: taps work the buttons, the disc and the stack; a finger going round the jog dial skips', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await open(page, 'minidisc', [390, 844]);
    await expect.poll(() => playing(page)).toBe('true');
    await page.getByTestId('md-jog-center').tap(); await expect.poll(() => playing(page)).toBe('false');
    await page.getByTestId('md-jog-center').tap(); await expect.poll(() => playing(page)).toBe('true');
    const vol = async () => (await page.getByTestId('md-vol').textContent())!;
    const v0 = await vol(); await ctl(page, 'vol-down').tap(); await expect.poll(vol).not.toBe(v0);
    const eq0 = await page.getByTestId('md-eq-name').textContent(); await ctl(page, 'eq').tap(); await expect(page.getByTestId('md-eq-name')).not.toHaveText(eq0!);
    // the jog dial: a finger round its ring (clockwise) goes to the next song
    const t0 = await title(page).textContent();
    const jb = (await page.getByTestId('md-jog').boundingBox())!; const cx = jb.x + jb.width / 2, cy = jb.y + jb.height / 2;
    await path(page, arc(cx, cy, jb.width * 0.43, -90, 0));
    await expect.poll(async () => await title(page).textContent()).not.toBe(t0);
    await ctl(page, 'eject').tap(); await expect(page.getByTestId('md-disc')).toHaveClass(/out/);
    await page.getByTestId('md-disc').tap(); await expect(page.getByTestId('md-disc')).not.toHaveClass(/out/);
    await page.locator('.md-d').first().tap(); await expect.poll(() => playing(page)).toBe('true');
    expect(errors).toEqual([]);
  });

  test('discman: taps work the keys, the lid, the volume and the CDs', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await open(page, 'discman', [390, 844]);
    await expect.poll(() => playing(page)).toBe('true');
    await ctl(page, 'play').tap(); await expect.poll(() => playing(page)).toBe('false');
    await ctl(page, 'play').tap(); await expect.poll(() => playing(page)).toBe('true');
    const t0 = await title(page).textContent(); await ctl(page, 'next').tap(); await expect.poll(async () => await title(page).textContent()).not.toBe(t0);
    await ctl(page, 'open').tap(); await expect(page.getByTestId('dm-lid')).toHaveClass(/open/);
    await ctl(page, 'open').tap(); await expect(page.getByTestId('dm-lid')).not.toHaveClass(/open/);
    const vol = async () => (await page.getByTestId('dm-vol').textContent())!;
    const v0 = await vol(); await ctl(page, 'vol-down').tap(); await expect.poll(vol).not.toBe(v0);
    await ctl(page, 'bass').tap(); await expect(ctl(page, 'bass')).toHaveAttribute('aria-pressed', 'true');
    await page.locator('.dm-cd').first().tap(); await expect.poll(() => playing(page)).toBe('true');
    await ctl(page, 'stop').tap(); await expect.poll(() => playing(page)).toBe('false');
    expect(errors).toEqual([]);
  });
});

test.describe('touch: sizes and no selecting', () => {
  for (const scene of SCENES) for (const [label, w, h] of SIZES) {
    test(`${scene} ${label}: every control is at least 44 px and nothing selects or zooms`, async ({ page }) => {
      const errors = consoleErrors(page);
      await signIn(page); await open(page, scene, [w, h]);
      const all = await targets(page); expect(all.length).toBeGreaterThan(3);
      if (process.env.TOUCH_DUMP) fs.writeFileSync(`${process.env.TOUCH_DUMP}/${scene}-${label.replace(' ', '-')}.json`, JSON.stringify(all.filter((t) => Math.min(t.w, t.h) < 44)));
      const small = all.filter((t) => Math.min(t.w, t.h) < 44);
      expect(small, JSON.stringify(small)).toEqual([]);
      // controls do not select text or wait for a double tap
      const bad = await page.evaluate(() => {
        // (touch-action is not inherited, but a parent's value limits its children: the first one set on the way up is the one that counts)
        const ta = (e: HTMLElement) => { for (let n: HTMLElement | null = e; n; n = n.parentElement) { const t = getComputedStyle(n).touchAction; if (t !== 'auto') return t; } return 'auto'; };
        return [...document.querySelectorAll<HTMLElement>('[data-testid=retro] button, [data-testid=desk] button, [data-knob], [data-dial], [data-ctl]')]
          .filter((e) => getComputedStyle(e).userSelect !== 'none' || !['manipulation', 'none'].includes(ta(e))).map((e) => e.className + '/' + e.getAttribute('data-ctl')).slice(0, 10);
      });
      expect(bad).toEqual([]);
      expect(errors).toEqual([]);
    });
  }
});
