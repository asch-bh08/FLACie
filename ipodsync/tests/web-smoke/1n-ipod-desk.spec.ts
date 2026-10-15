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

  test('the disc: the old album goes at once, a late answer is dropped, the release\'s own picture first, else a print from the cover', async ({ page }) => {
    const errors = errorsOf(page);
    await page.goto(harness());
    const st = await page.evaluate(async () => {
      const d = (window as any).desk, el = document.getElementById('disc')!, art = () => el.style.getPropertyValue('--art');
      const png = (c: string) => { const k = document.createElement('canvas'); k.width = k.height = 32; const g = k.getContext('2d')!; g.fillStyle = c; g.fillRect(0, 0, 32, 32); return k.toDataURL(); };
      const A = png('#c33'), B = png('#33c'), own = png('#3c3');
      // the print for A is slow; B comes up meanwhile
      const print = d.discPrint.bind(d); let releaseA: () => void = () => { };
      d.discPrint = (c: string, ...r: string[]) => c === A ? new Promise((res) => { releaseA = () => print(c, ...r).then(res); }) : print(c, ...r);
      el.style.setProperty('--art', 'url("old.png")');
      const pA = d.discInto(el, null, A, 'Ann', 'First');
      const cleared = art();
      const okB = await d.discInto(el, null, B, 'Bob', 'Second'); const printedB = art();
      releaseA(); const okA = await pA;
      const afterLate = art();
      const okOwn = await d.discInto(el, own, A, 'Ann', 'First'); const ownArt = art();
      const okMissing = await d.discInto(el, 'missing-disc.png', B, 'Bob', 'Second'); const fallback = art();
      await d.discInto(el, null, null, 'Cy', 'Third'); const nothing = art();
      return { cleared, okB, printedB: printedB.slice(0, 26), okA, same: afterLate === printedB, okOwn, ownArt: ownArt.includes(own), okMissing, fallback: fallback.slice(0, 26), nothing };
    });
    expect(st.cleared).toBe('');                        // no moment of the old album's disc on the new album
    expect(st.okB).toBe(true); expect(st.printedB).toBe('url("data:image/png;base64');
    expect(st.okA).toBe(false); expect(st.same).toBe(true);   // the late print for A did not replace B's
    expect(st.okOwn).toBe(true); expect(st.ownArt).toBe(true);
    expect(st.okMissing).toBe(true); expect(st.fallback).toBe('url("data:image/png;base64');
    expect(st.nothing).toBe('');
    expect(errors.filter((e) => !e.startsWith('Failed to load resource'))).toEqual([]);   // (the missing disc picture, on purpose)
  });

  test('reduced motion: one still frame, no loop reading the analyser', async ({ page }) => {
    await page.goto(harness());
    await page.evaluate(() => (window as any).desk.start(document.getElementById('scene'), { reduced: true }));
    await page.waitForTimeout(700);
    const st = await page.evaluate(() => ({ calls: (window as any).vizCalls || 0, arc: document.querySelector('[data-arc="0"]')!.getAttribute('d') }));
    expect(st.calls).toBeLessThanOrEqual(1);
    expect(st.arc).toMatch(/^M /);
  });

  test('a calm fixed pulse writes nothing frame after frame (no analyser: only the current along the cable moves)', async ({ page }) => {
    await page.goto(harness('?null=1'));
    await page.evaluate(() => (window as any).desk.start(document.getElementById('scene'), { reduced: false }));
    await page.waitForTimeout(400);
    const writes = await page.evaluate(() => new Promise<number>((done) => {
      let n = 0; const mo = new MutationObserver((rs) => { n += rs.length; });
      const scene = document.getElementById('scene')!;
      mo.observe(scene, { attributes: true, attributeFilter: ['style'] });
      document.querySelectorAll('[data-arc], [data-bud] > g').forEach((x) => mo.observe(x, { attributes: true }));
      setTimeout(() => { mo.disconnect(); done(n); }, 600);
    }));
    expect(writes).toBe(0);
  });

  test('vizFrame hands back the same buffers each frame and keeps the analyser\'s copy of the song in step a few times a second', async ({ page }) => {
    await page.goto(pathToFileURL(path.join(__dirname, 'fixtures', 'viz-resume.html')).href);
    const st = await page.evaluate(() => {
      const f = (window as any).flacie; (window as any).playing = true;
      let srcReads = 0; const d = Object.getOwnPropertyDescriptor(HTMLMediaElement.prototype, 'src')!;
      Object.defineProperty(HTMLMediaElement.prototype, 'src', { get() { srcReads++; return d.get!.call(this); }, set(v) { d.set!.call(this, v); }, configurable: true });
      const a = f.vizFrame(); srcReads = 0;
      let b = a; for (let i = 0; i < 60; i++) b = f.vizFrame();   // a second of frames
      return { same: a.freq === b.freq, wave: 'wave' in a, syncs: srcReads };
    });
    expect(st.same).toBe(true);
    expect(st.wave).toBe(false);
    expect(st.syncs).toBeLessThanOrEqual(2);
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
