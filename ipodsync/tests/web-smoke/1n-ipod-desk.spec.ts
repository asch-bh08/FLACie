import { test, expect, Page } from '@playwright/test';
import * as path from 'node:path';
import { pathToFileURL } from 'node:url';

// The iPod desk (/ipod, Look › Desk): the iPod on a desk with a monitor that shows the music, studio speakers, and a shelf of tapes or CDs that play your sources.

/** How many pixels of the canvas are not plain black/transparent. */
const painted = (page: Page, sel: string) => page.evaluate((s) => {
  const c = document.querySelector(s) as HTMLCanvasElement; const d = c.getContext('2d')!.getImageData(0, 0, c.width, c.height).data;
  let n = 0; for (let i = 0; i < d.length; i += 4) if (d[i + 3] > 0 && d[i] + d[i + 1] + d[i + 2] > 30) n++; return n;
}, sel);

test.describe('desk visualiser on its own (desk.js harness)', () => {
  const harness = (q = '') => pathToFileURL(path.join(__dirname, 'fixtures', 'desk-viz.html')).href + q;

  test('desk-viz harness draws and idles without errors', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
    page.on('pageerror', (e) => errors.push(String(e)));
    await page.goto(harness());
    for (const style of ['bars', 'wave', 'particles']) {
      await page.evaluate((s) => { const w = window as any; w.desk.vizStart(document.getElementById('c'), s, document.getElementById('scene'), 'Song'); }, style);
      await page.waitForTimeout(300);
      expect(await painted(page, '#c'), style).toBeGreaterThan(500);
      expect(await page.evaluate(() => (window as any).desk.loops())).toBe(1);
    }
    // the bass reaches the scene as a CSS variable (the woofers move with it)
    expect(Number(await page.evaluate(() => getComputedStyle(document.getElementById('scene')!).getPropertyValue('--bass')))).toBeGreaterThan(0);
    await page.evaluate(() => (window as any).desk.vizStop());
    expect(await page.evaluate(() => (window as any).desk.loops())).toBe(0);
    expect(await page.evaluate(() => (window as any).released)).toBe(1);

    // no analyser (iOS, no AudioContext, nothing played yet): the monitor still shows its idle picture
    await page.goto(harness('?null=1'));
    await page.evaluate(() => { const w = window as any; w.desk.vizStart(document.getElementById('c'), 'bars', document.getElementById('scene'), ''); });
    await page.waitForTimeout(300);
    expect(await painted(page, '#c')).toBeGreaterThan(500);
    expect(Number(await page.evaluate(() => getComputedStyle(document.getElementById('scene')!).getPropertyValue('--bass') || '0'))).toBe(0);
    expect(errors).toEqual([]);
  });
});
