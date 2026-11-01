import { test, expect, Page } from '@playwright/test';

// The buttons, seek bar and highlights wear the colour of the cover playing. They have to be the cover's own main colour: a black and
// green cover once got orange buttons (owner, 1 Nov), because a dark cover fell back to FLACie's default orange, and a small bright
// speck could outweigh the colour the cover is mostly made of. Reds either side of 0° also averaged into cyan.

type Region = [x: number, y: number, w: number, h: number, colour: string];

// draws a 48x48 cover from regions, runs the page's own colour picker on it, and returns the accent as hue/saturation/lightness
async function accentOf(page: Page, regions: Region[]) {
  return page.evaluate(async (regions) => {
    const c = document.createElement('canvas'); c.width = c.height = 48;
    const g = c.getContext('2d')!;
    for (const [x, y, w, h, col] of regions) { g.fillStyle = col; g.fillRect(x, y, w, h); }
    const el = document.createElement('div'); document.body.appendChild(el);
    (window as any).flacie.npAccent(el, c.toDataURL('image/png'));
    for (let i = 0; i < 50 && !el.dataset.accent; i++) await new Promise((r) => setTimeout(r, 50));
    const m = /rgb\((\d+), (\d+), (\d+)\)/.exec(el.style.getPropertyValue('--np-accent'))!;
    const [h, s, l] = (window as any).flacie.hsl(+m[1], +m[2], +m[3]);
    const out = { h, s, l, from: el.dataset.accent }; el.remove(); return out;
  }, regions);
}

test.beforeEach(async ({ page }) => { await page.goto('/login'); await page.waitForFunction(() => !!(window as any).flacie?.npAccent); });

test('a black and green cover gets green, not orange, even with a small orange speck', async ({ page }) => {
  const a = await accentOf(page, [[0, 0, 48, 48, '#050505'], [0, 30, 48, 14, 'rgb(40, 150, 70)'], [2, 2, 4, 4, 'rgb(255, 120, 0)']]);
  expect(a.from).toBe('cover');
  expect(a.h).toBeGreaterThan(90); expect(a.h).toBeLessThan(170);
});

test('a dark cover still gives its own colour, not the default', async ({ page }) => {
  const a = await accentOf(page, [[0, 0, 48, 48, '#000000'], [0, 24, 48, 24, 'rgb(12, 62, 32)']]);
  expect(a.from).toBe('cover');
  expect(a.h).toBeGreaterThan(90); expect(a.h).toBeLessThan(170);
});

test('a very dark green cover is still green (it used to give up and keep the last song\'s colour)', async ({ page }) => {
  const a = await accentOf(page, [[0, 0, 48, 48, '#020202'], [0, 20, 48, 28, 'rgb(8, 42, 20)']]);
  expect(a.from).toBe('cover');
  expect(a.h).toBeGreaterThan(90); expect(a.h).toBeLessThan(170);
});

test('reds either side of 0° stay red', async ({ page }) => {
  const a = await accentOf(page, [[0, 0, 24, 48, 'rgb(220, 20, 45)'], [24, 0, 24, 48, 'rgb(220, 45, 20)']]);
  expect(Math.min(a.h, 360 - a.h)).toBeLessThan(20);
});

test('a black and white cover gets a plain neutral, not an invented colour', async ({ page }) => {
  const a = await accentOf(page, [[0, 0, 48, 48, '#111111'], [0, 0, 48, 20, '#e8e8e8'], [10, 30, 20, 10, '#777777']]);
  expect(a.from).toBe('cover');
  expect(a.s).toBeLessThan(0.15);
});

test('the colour most of the cover is wins over a brighter one in a corner', async ({ page }) => {
  // mostly a calm blue, a vivid yellow corner
  const a = await accentOf(page, [[0, 0, 48, 48, 'rgb(50, 80, 160)'], [40, 40, 8, 8, 'rgb(255, 230, 0)']]);
  expect(a.h).toBeGreaterThan(200); expect(a.h).toBeLessThan(240);
});
