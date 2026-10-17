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

  test('on mobile data the earbuds keep the calm pulse and never open the analyser (no second copy of the song)', async ({ page }) => {
    await page.goto(harness('?metered=1'));
    await page.evaluate(() => (window as any).desk.start(document.getElementById('scene'), { reduced: false }));
    await page.waitForTimeout(1200);
    const st = await page.evaluate(() => ({ calls: (window as any).vizCalls || 0, beats: (window as any).desk.beats(), low: Number(getComputedStyle(document.getElementById('scene')!).getPropertyValue('--low')) }));
    expect(st.calls).toBe(0); expect(st.beats).toBe(0);
    expect(st.low).toBeCloseTo(0.2, 1);
  });

  test('mobile data is told from the browser\'s connection: cellular or Data Saver; Wi-Fi, a cable or no answer is not', async ({ page }) => {
    await page.goto(pathToFileURL(path.join(__dirname, 'fixtures', 'viz-resume.html')).href);
    const seen = await page.evaluate(() => {
      const f = (window as any).flacie, set = (c: unknown) => Object.defineProperty(navigator, 'connection', { value: c, configurable: true });
      const out: Record<string, boolean> = {};
      set({ type: 'cellular', saveData: false }); out.cellular = f.metered();
      set({ type: 'wifi', saveData: true }); out.saveData = f.metered();
      set({ type: 'wifi', saveData: false }); out.wifi = f.metered();
      set({ effectiveType: '4g' }); out.desktop = f.metered();
      set(undefined); out.none = f.metered();
      return out;
    });
    expect(seen).toEqual({ cellular: true, saveData: true, wifi: false, desktop: false, none: false });
  });

  test('the analyser\'s copy of the song stops while the tab is hidden, and comes back when it is shown again', async ({ page }) => {
    await page.goto(pathToFileURL(path.join(__dirname, 'fixtures', 'viz-resume.html')).href);
    const st = await page.evaluate(() => {
      const f = (window as any).flacie; let hidden = false;
      Object.defineProperty(document, 'hidden', { get: () => hidden, configurable: true });
      Object.defineProperty(document, 'visibilityState', { get: () => (hidden ? 'hidden' : 'visible'), configurable: true });
      f.vizFrame(); const open = (window as any).contexts;
      hidden = true; document.dispatchEvent(new Event('visibilitychange'));
      const closedWhenHidden = (window as any).closes;
      hidden = false; document.dispatchEvent(new Event('visibilitychange'));
      const back = f.vizFrame() !== null;
      return { open, closedWhenHidden, back, contexts: (window as any).contexts };
    });
    expect(st.open).toBe(1);
    expect(st.closedWhenHidden).toBe(1);
    expect(st.back).toBe(true); expect(st.contexts).toBe(2);
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

  const journal = (q: string) => pathToFileURL(path.join(__dirname, 'fixtures', 'desk-journal.html')).href + q;
  const startJournal = (page: import('@playwright/test').Page, reduced = false) => page.evaluate((r) => (window as any).desk.start(document.getElementById('desk'), { reduced: r }), reduced);
  const shownLines = (page: import('@playwright/test').Page) => page.evaluate(() => ({
    shown: [...document.querySelectorAll('[data-testid=desk-lyric]')].filter((l) => getComputedStyle(l).display !== 'none').map((l) => l.textContent!.split(':')[0]),
    writing: document.querySelector('[data-testid=desk-lyric].writing')?.textContent?.split(':')[0] ?? null,
  }));

  test('the journal follows the song\'s own clock, a hair early (the pen starts as the line is sung)', async ({ page }) => {
    await page.goto(journal('?n=6')); await startJournal(page);
    await page.evaluate(() => { (window as any).t = 0.1; }); await page.waitForTimeout(300);
    expect(await shownLines(page)).toEqual({ shown: ['line 0'], writing: 'line 0' });
    await page.evaluate(() => { (window as any).t = 1.0; }); await page.waitForTimeout(300);   // line 2 starts at 1.2 s: shown from about 0.85 s
    expect(await shownLines(page)).toEqual({ shown: ['line 0', 'line 1', 'line 2'], writing: 'line 2' });
    await page.evaluate(() => { (window as any).t = 0.2; }); await page.waitForTimeout(300);   // a seek back (line 1 starts at 0.6 s: shown from 0.25 s)
    expect((await shownLines(page)).writing).toBe('line 0');
  });

  test('lyrics without times are spread over the song; reduced motion still follows the song', async ({ page }) => {
    await page.goto(journal('?n=4&plain=1')); await startJournal(page, true);
    await page.evaluate(() => { (window as any).t = 31; }); await page.waitForTimeout(300);   // 60 s song, 4 lines: the third quarter
    expect(await shownLines(page)).toEqual({ shown: ['line 0', 'line 1', 'line 2'], writing: 'line 2' });
  });

  test('a long line wraps onto the next rule instead of being cut off', async ({ page }) => {
    await page.goto(journal('?n=3&long=1')); await startJournal(page);
    await page.evaluate(() => { (window as any).t = 1.3; }); await page.waitForTimeout(300);
    const st = await page.evaluate(() => { const l = document.querySelector('[data-testid=desk-lyric].writing') as HTMLElement; return { lines: Math.round(l.getBoundingClientRect().height / parseFloat(getComputedStyle(l).lineHeight)), cut: l.scrollWidth > l.clientWidth + 1 }; });
    expect(st.lines).toBeGreaterThanOrEqual(2); expect(st.cut).toBe(false);
  });

  test('a line that wraps is written row by row: nothing of the second row appears before the first is done', async ({ page }) => {
    await page.goto(journal('?n=3&long=1'));
    await page.addStyleTag({ content: '.desk-journal { rotate: none !important; }' });   // (straight, so each row's box on screen is that row alone)
    await startJournal(page);
    await page.evaluate(() => { (window as any).t = 1.3; }); await page.waitForTimeout(250);
    // the pen held part way through the first row, then at the end; ink is counted in the second row's box (dark pixels of the handwriting)
    const rows = await page.evaluate(() => {
      const l = document.querySelector('[data-testid=desk-lyric].writing') as HTMLElement, r = document.createRange(); r.selectNodeContents(l);
      const byTop = new Map<number, DOMRect>(); for (const q of r.getClientRects()) if (q.width > 0) byTop.set(Math.round(q.top), q);
      return [...byTop.values()].sort((a, b) => a.top - b.top).map((q) => ({ x: q.left, y: q.top, width: q.width, height: q.height }));
    });
    expect(rows.length).toBeGreaterThanOrEqual(2);
    const hold = (f: number) => page.evaluate((frac) => (window as any).desk.holdWrite(frac), f);
    const ink = async (clip: { x: number; y: number; width: number; height: number }) => {
      const png = await page.screenshot({ clip });
      return page.evaluate(async (b64) => {
        const img = new Image(); img.src = 'data:image/png;base64,' + b64; await img.decode();
        const c = document.createElement('canvas'); c.width = img.width; c.height = img.height; const g = c.getContext('2d')!; g.drawImage(img, 0, 0);
        const d = g.getImageData(0, 0, c.width, c.height).data; let n = 0;
        for (let i = 0; i < d.length; i += 4) if (d[i] + d[i + 1] + d[i + 2] < 330) n++;
        return n;
      }, png.toString('base64'));
    };
    await hold(0.3);
    expect(await ink(rows[1]), 'ink in the second row while the first is being written').toBe(0);
    await hold(1);
    expect(await ink(rows[1]), 'ink in the second row at the end').toBeGreaterThan(20);
  });

  for (const fit of ['zoom: .6', 'transform: scale(.6); transform-origin: 0 0']) test(`the pen finishes every line, last letter included, on a desk scaled to fit the window (${fit.split(':')[0]})`, async ({ page }) => {
    await page.setViewportSize({ width: 1700, height: 1000 });
    await page.goto(journal('?n=3&w=2600&h=1460'));
    await page.addStyleTag({ content: `.desk-journal { rotate: none !important; } #desk { ${fit}; } .j-lines div { rotate: none !important; translate: none !important; }` });
    await startJournal(page);
    await page.evaluate(() => { (window as any).t = 0.1; }); await page.waitForTimeout(250);
    const l = page.locator('[data-testid=desk-lyric].writing');
    const box = (await l.boundingBox())!;
    const clip = { x: box.x - 4, y: box.y - 4, width: box.width + 8, height: box.height + 8 };
    const ink = async () => {
      const png = await page.screenshot({ clip });
      return page.evaluate(async (b64) => {
        const img = new Image(); img.src = 'data:image/png;base64,' + b64; await img.decode();
        const c = document.createElement('canvas'); c.width = img.width; c.height = img.height; const g = c.getContext('2d')!; g.drawImage(img, 0, 0);
        const d = g.getImageData(0, 0, c.width, c.height).data; let n = 0;
        for (let i = 0; i < d.length; i += 4) if (d[i] + d[i + 1] + d[i + 2] < 420) n++;
        return n;
      }, png.toString('base64'));
    };
    // the animation's last moment, against the line with no clip at all
    await page.evaluate(() => (window as any).desk.holdWrite(0.999));
    const end = await ink();
    await page.evaluate(() => (window as any).desk.holdWrite(null));
    const whole = await ink();
    expect(whole).toBeGreaterThan(20);
    expect(end, 'ink at the end of the pen stroke vs the whole line').toBeGreaterThanOrEqual(whole * 0.98);
  });

  // the lines on show, as page columns: the x of each shown line's page
  const pagesShown = (page: import('@playwright/test').Page) => page.evaluate(() => {
    // in the journal's own layout (it lies tilted on the desk): which page columns the shown lines are in, of those in view
    const box = document.querySelector('.j-lines') as HTMLElement;
    const xs = [...document.querySelectorAll('[data-testid=desk-lyric].on')].map((l) => (l as HTMLElement).offsetLeft - box.scrollLeft).filter((x) => x >= -2 && x < box.clientWidth - 2).map((x) => Math.round(x / 10));
    return [...new Set(xs)].length;
  });

  test('on a big screen the journal is an open notebook: the lyrics fill the left page, then the right, then the page turns', async ({ page }) => {
    await page.goto(journal('?w=1600&h=900&n=40')); await startJournal(page);
    await page.evaluate(() => { (window as any).t = 3.4; }); await page.waitForTimeout(400);   // lines 0 to 6: the left page, full (seven rules)
    expect(await pagesShown(page)).toBe(1);
    await page.evaluate(() => { (window as any).t = 7.6; }); await page.waitForTimeout(400);   // line 12: on the right page too
    expect(await pagesShown(page)).toBe(2);
    // the spread fills: the right page turns over (a turning page for a moment), and the line being sung is on the fresh spread
    const turned = await page.evaluate(() => new Promise<boolean>((done) => {
      const mo = new MutationObserver(() => { if (document.querySelector('.j-turn')) { mo.disconnect(); done(true); } });
      mo.observe(document.body, { childList: true, subtree: true }); (window as any).t = 10.6; setTimeout(() => done(false), 1500);
    }));
    expect(turned, 'a page turned').toBe(true);
    await page.waitForTimeout(1200);
    expect(await page.locator('.j-turn').count(), 'the turning page is gone again').toBe(0);
    const box = (await page.getByTestId('desk-journal').locator('.j-lines').boundingBox())!, sung = (await page.locator('[data-testid=desk-lyric].writing').boundingBox())!;
    expect(sung.x).toBeGreaterThanOrEqual(box.x - 1); expect(sung.x + sung.width).toBeLessThanOrEqual(box.x + box.width + 1);
    expect(await pagesShown(page)).toBeLessThanOrEqual(2);
  });

  test('the page turn folds the right page over the spine: the old left page stays until the turning page covers it, and the pen waits for the page to land', async ({ page }) => {
    await page.goto(journal('?w=1600&h=900&n=40')); await startJournal(page);
    await page.evaluate(() => { (window as any).t = 7.6; }); await page.waitForTimeout(400);   // a full spread: lines 0 to 12 on it
    const clipOf = () => page.evaluate(() => (document.querySelector('[data-testid=desk-lyric].writing') as HTMLElement | null)?.style.clipPath ?? null);
    await page.evaluate(() => { (window as any).t = 10.6; });
    await page.locator('.j-turn').waitFor({ state: 'attached', timeout: 1500 });
    // just after it starts: the ghost has the old spread's first line still showing on its left, the journal underneath has moved on
    const st = await page.evaluate(() => {
      const g = document.querySelector('.j-turn')!, anims = g.getAnimations({ subtree: true });
      anims.forEach((a) => a.pause());
      const left = g.querySelector('.jt-left')!, fliplayer = g.querySelector('.jt-flap');
      const vis = (el: Element) => [...el.querySelectorAll('.j-lines div.on')].filter((d) => { const l = el.querySelector('.j-lines') as HTMLElement; return (d as HTMLElement).offsetLeft - l.scrollLeft >= -2 && (d as HTMLElement).offsetLeft - l.scrollLeft < l.clientWidth; }).map((d) => d.textContent);
      const real = document.querySelector('[data-testid=desk-journal]') as HTMLElement;
      return { layers: g.children.length, anims: anims.length, oldLeft: vis(left), flap: !!fliplayer, realFirst: (real.querySelector('.j-lines div.on') as HTMLElement).textContent,
        realScroll: (real.querySelector('.j-lines') as HTMLElement).scrollLeft, pageShadow: getComputedStyle(g.querySelector('.page')!).boxShadow };
    });
    expect(st.layers, 'old left, old right, the revealed shade, the flap and its shadows').toBeGreaterThanOrEqual(5);
    expect(st.anims, 'the fold is animated').toBeGreaterThanOrEqual(4);
    expect(st.oldLeft, 'the old left page keeps its ink until covered').toContain('line 0');
    expect(st.realScroll, 'the journal beneath already shows the next spread').toBeGreaterThan(0);
    expect(st.pageShadow, 'the copy has no shadow of its own (the journal under it has)').toBe('none');
    // half-way through the leaf is mid-fold: its flap is moved (rotated or mirrored), not still flat on the right
    const mid = await page.evaluate(() => { const g = document.querySelector('.j-turn')!; g.getAnimations({ subtree: true }).forEach((a) => { a.currentTime = 450; }); const f = g.querySelector('.jt-flap') as HTMLElement; return { t: getComputedStyle(f).transform, clip: getComputedStyle(f).clipPath }; });
    expect(mid.t).not.toBe('none'); expect(mid.clip).toContain('polygon');
    // the pen waits while the page turns
    const shut = await clipOf(); expect(shut, 'the new line is shut').toBeTruthy();
    await page.evaluate(() => { document.querySelector('.j-turn')!.getAnimations({ subtree: true }).forEach((a) => { a.currentTime = 400; a.play(); }); });
    await page.waitForTimeout(250);
    expect(await page.locator('.j-turn').count(), 'still turning').toBe(1);
    expect(await clipOf(), 'the pen has not moved while the page is up').toBe(shut);
    await page.locator('.j-turn').waitFor({ state: 'detached', timeout: 2500 });
    await expect.poll(clipOf, { message: 'the pen writes once the page has landed', timeout: 1500 }).not.toBe(shut);
    expect(await pagesShown(page)).toBeLessThanOrEqual(2);
  });

  test('on a phone the journal is a pad of sticky notes: a full note peels off to the next', async ({ page }) => {
    await page.goto(journal('?w=390&h=844&n=20')); await startJournal(page);
    await page.evaluate(() => { (window as any).t = 0.7; }); await page.waitForTimeout(400);
    expect(await pagesShown(page)).toBe(1);
    const peeled = await page.evaluate(() => new Promise<boolean>((done) => {
      const mo = new MutationObserver(() => { if (document.querySelector('.j-peel')) { mo.disconnect(); done(true); } });
      mo.observe(document.body, { childList: true, subtree: true }); (window as any).t = 3.1; setTimeout(() => done(false), 1500);
    }));
    expect(peeled, 'a note peeled off').toBe(true);
    {
      // paper peeling off: the old note is still whole (cut along the fold), its back curls over it, and a shaded strip lies on the note beneath
      const pl = await page.evaluate(() => {
        const g = document.querySelector('.j-peel')!, anims = g.getAnimations({ subtree: true }); anims.forEach((a) => { a.currentTime = 250; a.pause(); });
        const f = g.querySelector('.jt-flap') as HTMLElement | null;
        return { flap: !!f, flapT: f ? getComputedStyle(f).transform : '', note: g.querySelector('.jt-right .j-lines')?.textContent ?? '', shade: !!g.querySelector('.jt-shade'), ghostT: getComputedStyle(g).transform, anims: anims.length };
      });
      expect(pl.flap, 'the back of the note curls over').toBe(true); expect(pl.flapT).not.toBe('none'); expect(pl.shade).toBe(true);
      expect(pl.note, 'the old note is still whole under the curl').toContain('line 0');
      await page.evaluate(() => { document.querySelector('.j-peel')!.getAnimations({ subtree: true }).forEach((a) => a.play()); });
    }
    await page.waitForTimeout(1200);
    expect(await page.locator('.j-peel').count()).toBe(0);
    expect(await pagesShown(page)).toBe(1);
    // reduced motion: the next note shows at once, nothing peels
    await page.goto(journal('?w=390&h=844&n=20')); await startJournal(page, true);
    await page.evaluate(() => { (window as any).t = 3.1; }); await page.waitForTimeout(400);
    expect(await page.locator('.j-peel').count()).toBe(0);
  });

  test('the line being sung is always in view, on a desk and on a phone, however far into the song', async ({ page }) => {
    for (const [w, h] of [[1600, 900], [1280, 720], [390, 844], [360, 640]]) {
      await page.goto(journal(`?w=${w}&h=${h}&n=16&long=1`)); await startJournal(page);
      await page.evaluate(() => { (window as any).t = 8.6; }); await page.waitForTimeout(400);   // line 14 of 16, after several long ones
      const box = (await page.getByTestId('desk-journal').locator('.j-lines').boundingBox())!, sung = (await page.locator('[data-testid=desk-lyric].writing').boundingBox())!;
      expect(sung.y, `${w}x${h}: the line being sung is not above the lines' box`).toBeGreaterThanOrEqual(box.y - 1);
      // (a line taller than the box, on a phone: its start is at the top of the box)
      if (sung.height <= box.height + 1) expect(sung.y + sung.height, `${w}x${h}: nor below it`).toBeLessThanOrEqual(box.y + box.height + 1);
      else expect(Math.abs(sung.y - box.y), `${w}x${h}: a tall line starts at the top`).toBeLessThanOrEqual(2);   // (2 px: each line lies at its own slight angle, up to half a degree)
      expect(sung.x, `${w}x${h}: on screen`).toBeGreaterThanOrEqual(0);
    }
  });

  test('a quietly mastered song moves the earbuds as much as a loud one, and its kicks are beats', async ({ page }) => {
    await page.goto(harness('?quiet=1'));
    await page.evaluate(() => (window as any).desk.start(document.getElementById('scene'), { reduced: false }));
    const lows: number[] = [];
    for (let i = 0; i < 40; i++) { await page.waitForTimeout(50); lows.push(await page.evaluate(() => Number(getComputedStyle(document.getElementById('scene')!).getPropertyValue('--low')))); }
    const later = lows.slice(10);   // after the first half second: the level has found the song
    expect(Math.max(...later) - Math.min(...later), 'how far the bass level swings').toBeGreaterThan(0.4);
    expect(await page.evaluate(() => (window as any).desk.beats())).toBeGreaterThanOrEqual(2);
  });

  test('the coffee is a bass visualiser: its surface follows the bass all the time, from the same analyser read as the earbuds', async ({ page }) => {
    await page.goto(harness());
    await page.evaluate(() => (window as any).desk.start(document.getElementById('scene'), { reduced: false }));
    await page.waitForTimeout(600);
    const st = await page.evaluate(() => new Promise<{ swing: number; calls: number }>((done) => {
      const cup = document.querySelector('[data-coffee]') as HTMLElement, seen: number[] = []; const c0 = (window as any).vizCalls;
      const iv = setInterval(() => seen.push(Number(getComputedStyle(cup).getPropertyValue('--bass'))), 40);
      setTimeout(() => { clearInterval(iv); done({ swing: Math.max(...seen) - Math.min(...seen), calls: (window as any).vizCalls - c0 }); }, 1000);
    }));
    expect(st.swing, 'how far the coffee follows the bass').toBeGreaterThan(0.4);
    expect(st.calls, 'analyser reads in a second (one per frame, shared with the earbuds)').toBeLessThanOrEqual(65);
    // mobile data: the coffee stays calm (no analyser at all)
    await page.goto(harness('?metered=1'));
    await page.evaluate(() => (window as any).desk.start(document.getElementById('scene'), { reduced: false }));
    await page.waitForTimeout(800);
    const calm = await page.evaluate(() => ({ calls: (window as any).vizCalls || 0, rings: document.querySelector('[data-coffee]')!.children.length }));
    expect(calm).toEqual({ calls: 0, rings: 0 });
  });

  test('the VU meter\'s needle swings with the music, from the same analyser read, and rests on mobile data', async ({ page }) => {
    await page.goto(harness());
    await page.evaluate(() => (window as any).desk.start(document.getElementById('scene'), { reduced: false }));
    const angles: number[] = [];
    for (let i = 0; i < 30; i++) { await page.waitForTimeout(50); angles.push(await page.evaluate(() => Number((document.querySelector('[data-needle]') as HTMLElement).style.getPropertyValue('--a')))); }
    expect(Math.max(...angles) - Math.min(...angles), 'needle swing in degrees').toBeGreaterThan(15);
    await page.goto(harness('?metered=1'));
    await page.evaluate(() => (window as any).desk.start(document.getElementById('scene'), { reduced: false }));
    await page.waitForTimeout(600);
    expect(await page.evaluate(() => Number((document.querySelector('[data-needle]') as HTMLElement).style.getPropertyValue('--a')))).toBeLessThan(-30);   // at rest, near the left stop
  });

  test('the coffee ripples on the beat', async ({ page }) => {
    await page.goto(harness());
    const rings = await page.evaluate(() => new Promise<number>((done) => {
      let n = 0; const mo = new MutationObserver((rs) => rs.forEach((r) => { n += r.addedNodes.length; }));
      mo.observe(document.querySelector('[data-coffee]')!, { childList: true });
      (window as any).desk.start(document.getElementById('scene'), { reduced: false });
      setTimeout(() => { mo.disconnect(); done(n); }, 2000);
    }));
    expect(rings).toBeGreaterThanOrEqual(2);
    // the rings go again (no pile of old ones)
    await page.waitForTimeout(1500);
    expect(await page.evaluate(() => document.querySelector('[data-coffee]')!.children.length)).toBeLessThanOrEqual(4);
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
