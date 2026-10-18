import { test, expect, Page } from '@playwright/test';
import { signIn } from './sharp-helpers';

// The desk at every window size: a big desk keeps everything (the open CD, the rack, the preamp, the keepsakes); a small one (a short or narrow window, a
// laptop with the panel open, a tablet, a phone either way up) is the compact desk: the iPod as big as it can be, the Now Playing card and the lyrics on a
// sticky note, the coffee only where there is room. At every size nothing lies on anything else, nothing is cut off at the edges, the text can be read, the
// iPod's controls are big enough to hit, and the back button, the gear and the panel stay clear.
// DESK_SHOTS=<folder> also saves a screenshot of each.
const SHOTS = process.env.DESK_SHOTS || '';
const MATRIX: [number, number, boolean][] = [
  [1920, 1080, false], [1920, 1080, true], [1600, 900, false], [1600, 900, true], [1366, 768, false], [1366, 768, true], [1280, 720, false], [1280, 720, true],
  [1024, 768, false], [1024, 768, true], [800, 1280, false], [768, 1024, false], [390, 844, false], [844, 390, false], [360, 640, false], [3440, 1440, true], [2560, 1440, false],
];
const EXTRA: [string, number, number, boolean][] = [
  ['touch5', 1366, 768, true], ['touch5', 844, 390, false], ['touch5', 390, 844, false], ['nano3', 1024, 768, false], ['nano3', 844, 390, false], ['nano3', 360, 640, false],
  ['nano4', 1280, 720, true], ['mini', 844, 390, false], ['ipod1', 1366, 768, true],
];
const PROPS = ['desk-journal', 'desk-np', 'desk-open-cd', 'desk-rack', 'desk-pop', 'desk-preamp', 'desk-ticket', 'desk-usb', 'desk-clip', 'desk-coffee', 'desk-pencil'];
const FULL_ONLY = ['desk-open-cd', 'desk-rack', 'desk-preamp', 'desk-ticket', 'desk-usb', 'desk-clip', 'desk-pencil'];

async function openPlaying(page: Page, model: string, panel: boolean) {
  await page.goto('/songs');
  await page.locator('.track').first().locator('button.track-main').click();
  await expect(page.locator('.player')).toBeVisible();
  await page.evaluate(([m, p]) => localStorage.setItem('flacie.ipod', JSON.stringify({ model: m, look: 2, panel: p })), [model, panel] as const);
  await page.evaluate(() => (window as any).Blazor.navigateTo('/ipod'));
  await expect(page.getByTestId('desk')).toBeVisible({ timeout: 20_000 });
  await expect(page.getByTestId('ipod')).toHaveAttribute('data-model', model);
  // the panel as asked (the page opens it on a wide window by itself)
  const open = async () => (await page.getByTestId('ipod-panel').getAttribute('class'))!.includes('open');
  if ((await open()) !== panel) { await page.getByTestId('ipod-gear').click(); }
  await expect.poll(open).toBe(panel);
  // paused, so the song (and the next CD popping out with it) holds still while the desk is measured (the mock's songs last 3 s)
  await page.getByTestId('ipod-keys').focus(); await page.keyboard.press(' ');
  await page.waitForTimeout(1300);
}

async function judge(page: Page, model: string, w: number, h: number, panel: boolean) {
  const r = await page.evaluate((ids) => {
    const vis = (e: Element | null) => { if (!e) return null; const q = e.getBoundingClientRect(); return q.width > 0 && q.height > 0 ? q : null; };
    const by = (id: string) => vis(document.querySelector(`[data-testid=${id}]`));
    const desk = by('desk')!, dev = by('ipod-device')!;
    const props: Record<string, DOMRect> = {};
    for (const id of ids) { const b = by(id); if (b) props[id] = b; }
    const rect = (q: DOMRect) => ({ x: q.left, y: q.top, r: q.right, b: q.bottom, w: q.width, h: q.height });
    // the cable, its leads and the buds, as points on the screen
    const pts: { x: number; y: number }[] = [];
    for (const id of ['desk-cable', 'desk-lead-l', 'desk-lead-r']) {
      const p = document.getElementById(id) as unknown as SVGPathElement | null; if (!p) continue;
      const m = p.getScreenCTM()!, L = p.getTotalLength();
      for (let i = 0; i <= 300; i++) { const q = p.getPointAtLength(L * i / 300); const s = new DOMPoint(q.x, q.y).matrixTransform(m); pts.push({ x: s.x, y: s.y }); }
    }
    const buds = [...document.querySelectorAll('[data-bud] > g')].map((b) => rect(b.getBoundingClientRect()));
    for (const q of buds) for (const fx of [0, .5, 1]) for (const fy of [0, .5, 1]) pts.push({ x: q.x + q.w * fx, y: q.y + q.h * fy });
    const px = (sel: string) => { const e = document.querySelector(sel); return e && vis(e) ? parseFloat(getComputedStyle(e).fontSize) : null; };
    const wheel = vis(document.querySelector('[data-testid=ipod-wheel]'));
    return {
      desk: rect(desk), dev: rect(dev), props: Object.fromEntries(Object.entries(props).map(([k, v]) => [k, rect(v)])), pts, buds,
      gear: rect(by('ipod-gear')!), exit: rect(by('ipod-exit')!), panel: by('ipod-panel') ? rect(by('ipod-panel')!) : null,
      fonts: { npTitle: px('.desk-np b'), npLine: px('.desk-np [data-np=artist]'), npFormat: px('.desk-np [data-np=format]'), jTitle: px('.desk-journal .j-title'), jLine: px('.desk-journal .j-lines') },
      wheel: wheel ? wheel.width : null, scrollsSideways: document.documentElement.scrollWidth > innerWidth + 1,
    };
  }, PROPS);
  type R = { x: number; y: number; r: number; b: number; w: number; h: number };
  const over = (a: R, b: R) => Math.max(0, Math.min(a.r, b.r) - Math.max(a.x, b.x)) * Math.max(0, Math.min(a.b, b.b) - Math.max(a.y, b.y));
  const inside = (a: R, b: R, s = 2) => a.x >= b.x - s && a.y >= b.y - s && a.r <= b.r + s && a.b <= b.b + s;
  const at = `${model} at ${w}x${h}${panel ? ' with the panel' : ''}`;
  const { desk } = r;
  const portrait = desk.w / desk.h < 1.15, compact = portrait || desk.h < 600 || desk.w < 1066.67;
  // the layout: the big desk keeps everything, the compact one only the iPod, the card, the note (and the coffee where there is room)
  for (const id of FULL_ONLY) expect(!!r.props[id], `${id} ${compact ? 'put away' : 'on the desk'} ${at}`).toBe(!compact);
  for (const id of ['desk-journal', 'desk-np']) expect(!!r.props[id], `${id} on the desk ${at}`).toBe(true);
  if (compact) expect(!!r.props['desk-pop'], `no up-next CD on the compact desk ${at}`).toBe(false);
  // the iPod: on the desk, on screen, clear of the back button and the gear, and big
  expect(inside(r.dev, desk), `iPod on the desk ${at}`).toBe(true);
  expect(over(r.dev, r.gear) + over(r.dev, r.exit), `iPod under the gear or the back button ${at}`).toBe(0);
  if (compact && !portrait) expect(r.dev.h / desk.h, `iPod height to the desk ${at}`).toBeGreaterThan(0.6);
  if (portrait) expect(r.dev.h / Math.min(desk.h, desk.w * 19 / 9), `iPod height to the frame ${at}`).toBeGreaterThan(0.42);
  if (r.wheel != null) expect(r.wheel, `click wheel width ${at}`).toBeGreaterThanOrEqual(compact ? 95 : 150);
  // every prop: in the frame (the big desk's coffee may run off its corner, as it always has), clear of the iPod, the buttons and each other
  for (const [id, b] of Object.entries(r.props)) {
    if (compact || id !== 'desk-coffee') expect(inside(b, desk), `${id} cut off at the edge ${at}`).toBe(true);
    expect(over(b, r.dev) / (b.w * b.h), `${id} on the iPod ${at}`).toBeLessThan(0.02);
    // (the big desk's notebook has always run under the back button's corner on a short window: the button stays on top, and the big desk is left as it was)
    if (compact || id !== 'desk-journal') expect(over(b, r.gear) + over(b, r.exit), `${id} under the gear or the back button ${at}`).toBe(0);
    if (r.panel && panel) expect(over(b, r.panel), `${id} under the panel ${at}`).toBe(0);
  }
  const list = Object.entries(r.props);
  for (const [a, p] of list) for (const [b, q] of list) if (a < b && !(a === 'desk-journal' && b === 'desk-pencil') && !(a === 'desk-np' && b === 'desk-pencil'))
    expect(over(p, q) / Math.min(p.w * p.h, q.w * q.h), `${a} on ${b} ${at}`).toBeLessThan(0.04);
  // the earbuds: across no prop, and in the frame
  for (const [id, q] of list) {
    if (id === 'desk-coffee' || id === 'desk-pencil') continue;
    expect(r.pts.filter((t) => t.x > q.x + 2 && t.x < q.r - 2 && t.y > q.y + 2 && t.y < q.b - 2).length, `the earbuds across ${id} ${at}`).toBe(0);
  }
  for (const b of r.buds) expect(inside(b, desk, 4), `a bud cut off at the edge ${at}`).toBe(true);
  // readable: on the compact desk the card and the note are never tiny
  if (compact) {
    expect(r.fonts.npTitle!, `card title px ${at}`).toBeGreaterThanOrEqual(13);
    if (r.fonts.npLine != null) expect(r.fonts.npLine, `card line px ${at}`).toBeGreaterThanOrEqual(9.5);
    expect(r.fonts.jTitle!, `note title px ${at}`).toBeGreaterThanOrEqual(12);
    expect(r.fonts.jLine!, `lyrics px ${at}`).toBeGreaterThanOrEqual(12);
  }
  expect(r.scrollsSideways, `${at} scrolls sideways`).toBe(false);
}

test.describe('the desk at every size', () => {
  for (const [w, h, panel] of MATRIX) {
    if (panel && w < 860) continue;
    test(`classic6 at ${w}x${h}${panel ? ' with the panel' : ''}`, async ({ page }) => {
      await signIn(page); await page.setViewportSize({ width: w, height: h });
      await openPlaying(page, 'classic6', panel);
      if (SHOTS) await page.screenshot({ path: `${SHOTS}/classic6-${w}x${h}${panel ? '-panel' : ''}.png` });
      await judge(page, 'classic6', w, h, panel);
    });
  }
  for (const [model, w, h, panel] of EXTRA) {
    test(`${model} at ${w}x${h}${panel ? ' with the panel' : ''}`, async ({ page }) => {
      await signIn(page); await page.setViewportSize({ width: w, height: h });
      await openPlaying(page, model, panel);
      if (SHOTS) await page.screenshot({ path: `${SHOTS}/${model}-${w}x${h}${panel ? '-panel' : ''}.png` });
      await judge(page, model, w, h, panel);
    });
  }
});
