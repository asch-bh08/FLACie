import { test, expect, Page } from '@playwright/test';
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

  test('vizFrame wakes an analyser the browser opened suspended (a reload of /ipod in the Desk look) once the song plays', async ({ page }) => {
    const errors = errorsOf(page);
    await page.goto(pathToFileURL(path.join(__dirname, 'fixtures', 'viz-resume.html')).href);
    expect(await page.evaluate(() => (window as any).flacie.vizFrame() !== null)).toBe(true);
    const asked = await page.evaluate(() => (window as any).resumes);
    // still suspended, the song now playing (the user pressed play): the next frame asks again
    await page.evaluate(() => { (window as any).playing = true; (window as any).flacie.vizFrame(); });
    expect(await page.evaluate(() => (window as any).resumes)).toBeGreaterThan(asked);
    expect(errors).toEqual([]);
  });

  test('the journal always shows the line being sung, on a desk and on a phone', async ({ page }) => {
    for (const [w, h] of [[1600, 900], [1280, 720], [390, 760], [360, 640]]) {
      await page.goto(pathToFileURL(path.join(__dirname, 'fixtures', 'desk-journal.html')).href + `?w=${w}&h=${h}`);
      const pageBox = (await page.locator('.desk-journal .page').boundingBox())!;
      const sung = (await page.locator('.writing').boundingBox())!;
      expect(sung, `${w}x${h}: the line being sung is shown`).not.toBeNull();
      expect(sung.y + sung.height, `${w}x${h}: the line being sung is on the page`).toBeLessThanOrEqual(pageBox.y + pageBox.height + 1);
      expect(sung.y, `${w}x${h}: below the title`).toBeGreaterThan(pageBox.y);
    }
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
