import { test, expect } from '@playwright/test';
import * as path from 'node:path';
import { pathToFileURL } from 'node:url';

// The iPod desk (/ipod, Look › Desk): the real iPod emulator lying on a sunlit desk, the song's lyrics written into a journal, the next album's CD open
// with its printed disc, coffee, and the earbuds as the visualiser.

test.describe('desk engine on its own (desk.js harness)', () => {
  const harness = (q = '') => pathToFileURL(path.join(__dirname, 'fixtures', 'desk-viz.html')).href + q;
  const errorsOf = (page: import('@playwright/test').Page) => { const e: string[] = []; page.on('console', (m) => { if (m.type() === 'error') e.push(m.text()); }); page.on('pageerror', (x) => e.push(String(x))); return e; };

  test('desk engine: bands, beats, fallback, disc print, one loop', async ({ page }) => {
    const errors = errorsOf(page);
    await page.goto(harness());
    await page.evaluate(() => (window as any).desk.start(document.getElementById('scene'), { reduced: false }));
    await page.waitForTimeout(2000);
    const st = await page.evaluate(() => {
      const s = document.getElementById('scene')!, v = (n: string) => Number(getComputedStyle(s).getPropertyValue(n));
      return { beats: (window as any).desk.beats(), low: v('--low'), arc: document.querySelector('[data-arc="0"]')!.getAttribute('d'), dash: document.querySelector('[data-pulse="a"]')!.getAttribute('stroke-dasharray'), ripples: document.querySelectorAll('[data-ripples] circle').length };
    });
    expect(st.beats).toBeGreaterThanOrEqual(2);
    expect(st.low).toBeGreaterThan(0); expect(st.low).toBeLessThanOrEqual(1);
    expect(st.arc).toMatch(/^M /);
    expect(st.dash).toBeTruthy();
    expect(st.ripples).toBeGreaterThan(0);

    // started twice, stopped once: no loop left, the analyser let go
    await page.evaluate(() => { const d = (window as any).desk, s = document.getElementById('scene'); d.start(s, {}); d.stop(); });
    expect(await page.evaluate(() => (window as any).desk.loops())).toBe(0);
    expect(await page.evaluate(() => (window as any).released)).toBe(1);

    // the disc print: a picture with the hub cut out (the tray shows through), the artist and album in it
    const print = await page.evaluate(async () => {
      const c = document.createElement('canvas'); c.width = c.height = 64; const g = c.getContext('2d')!; g.fillStyle = '#c33'; g.fillRect(0, 0, 64, 64);
      const url = await (window as any).desk.discPrint(c.toDataURL(), 'Ann', 'First');
      const img = new Image(); img.src = url; await img.decode();
      const k = document.createElement('canvas'); k.width = img.width; k.height = img.height; const kg = k.getContext('2d')!; kg.drawImage(img, 0, 0);
      return { url: url.slice(0, 22), centre: kg.getImageData(img.width / 2, img.height / 2, 1, 1).data[3], art: Array.from(kg.getImageData(img.width * 0.5, img.height * 0.08, 1, 1).data) };
    });
    expect(print.url).toBe('data:image/png;base64,');
    expect(print.centre).toBe(0);
    expect(print.art[0]).toBeGreaterThan(print.art[2]);   // the cover's red is printed on the disc
    expect(errors).toEqual([]);
  });

  test('no analyser: a calm fixed pulse, no beats, no errors', async ({ page }) => {
    const errors = errorsOf(page);
    await page.goto(harness('?null=1'));
    await page.evaluate(() => (window as any).desk.start(document.getElementById('scene'), { reduced: false }));
    await page.waitForTimeout(1200);
    const st = await page.evaluate(() => ({ beats: (window as any).desk.beats(), low: Number(getComputedStyle(document.getElementById('scene')!).getPropertyValue('--low')), ripples: document.querySelectorAll('[data-ripples] circle').length }));
    expect(st.beats).toBe(0); expect(st.ripples).toBe(0);
    expect(st.low).toBeCloseTo(0.2, 1);
    expect(errors).toEqual([]);
  });
});
