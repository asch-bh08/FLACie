import { test, expect, Page } from '@playwright/test';

// The owner (10 Nov): on earlier versions his dad had to scroll down to see the playback controls and the graphs. Now Playing, the player bar and the
// full-screen graph fit every screen with nothing to scroll for: play / pause, previous / next, the seek bar, the volume and the graphs are all inside the
// window, none on top of another. Where room is short the cover shrinks first, then the graph gets shorter (never below about 80px, and drawn at the
// screen's own pixel density so it stays crisp), and only then do secondary things move behind More. On a phone held upright no main control sits in
// the bottom 90px (the system's home gesture lives there).
// Short laptops, a browser window with its toolbars (1366 x 657), browser zoom at 125% and 150% on a 1366 x 768 laptop (a smaller window drawn at a
// higher pixel density, as zoom does), tablets both ways round, and phones both ways round.

const sizes: [string, number, number, number][] = [
  ['laptop 1366x768', 1366, 768, 1], ['laptop 1280x720', 1280, 720, 1], ['laptop 1280x800', 1280, 800, 1], ['laptop 1440x900', 1440, 900, 1],
  ['laptop 1536x864', 1536, 864, 1], ['PC 1920x1080', 1920, 1080, 1], ['browser window 1366x657', 1366, 657, 1],
  ['1366x768 at 125% zoom', 1093, 614, 1.25], ['1366x768 at 150% zoom', 911, 512, 1.5],
  ['tablet 768x1024', 768, 1024, 2], ['tablet 1024x768', 1024, 768, 2], ['tablet 820x1180', 820, 1180, 2],
  ['phone 360x640', 360, 640, 3], ['phone 375x667', 375, 667, 2], ['phone 390x844', 390, 844, 3], ['phone 412x915', 412, 915, 2.625],
  ['phone on its side 667x375', 667, 375, 2],
];

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(1200);
}

/** Every listed element that is drawn: off-screen ones, ones over each other (pairs not in [allowed]) and, if [floor] is set, ones below it. */
async function check(page: Page, sels: string[], floor = 0, allowed: string[][] = []) {
  return page.evaluate(({ sels, floor, allowed }) => {
    const vw = document.documentElement.clientWidth, vh = window.innerHeight;
    const seen = new Set<Element>();
    const items: { n: string; b: DOMRect; e: Element }[] = [];
    for (const s of sels) for (const e of document.querySelectorAll(s)) {
      const el = e as HTMLElement, cs = getComputedStyle(el);
      if (seen.has(e) || !el.offsetParent && cs.position !== 'fixed' || cs.visibility === 'hidden' || cs.display === 'none') continue;
      const b = el.getBoundingClientRect(); if (b.width < 1 || b.height < 1) continue;
      seen.add(e); items.push({ n: e.getAttribute('aria-label') || el.innerText?.trim().split('\n')[0] || s, b, e });
    }
    const off = items.filter(({ b }) => b.left < -1 || b.top < -1 || b.right > vw + 1 || b.bottom > vh + 1).map((x) => `${x.n} off screen (${Math.round(x.b.top)}..${Math.round(x.b.bottom)} of ${vh})`);
    const low = floor ? items.filter(({ b }) => b.bottom > vh - floor + 1).map((x) => `${x.n} in the bottom ${floor}px (ends at ${Math.round(x.b.bottom)})`) : [];
    const over: string[] = [];
    for (let i = 0; i < items.length; i++) for (let j = i + 1; j < items.length; j++) {
      const a = items[i], c = items[j];
      if (a.e.contains(c.e) || c.e.contains(a.e)) continue;
      if (allowed.some(([x, y]) => (a.e.matches(x) && c.e.matches(y)) || (a.e.matches(y) && c.e.matches(x)))) continue;
      if (a.b.left < c.b.right - 1 && c.b.left < a.b.right - 1 && a.b.top < c.b.bottom - 1 && c.b.top < a.b.bottom - 1) over.push(`${a.n} over ${c.n}`);
    }
    return { n: items.length, names: items.map((x) => x.n), problems: [...off, ...low, ...over.map((o) => 'overlap: ' + o)] };
  }, { sels, floor, allowed });
}

/** The live graph: drawn, at least about 80px tall, inside the window and inside whatever box clips it, and its canvas at the screen's pixel density. */
async function graph(page: Page, where: string) {
  const viz = page.locator('#viz');
  await expect(viz, `${where}: the graph is drawn`).toBeVisible({ timeout: 20_000 });
  await page.waitForTimeout(600);
  return viz.evaluate((c: HTMLCanvasElement, where) => {
    const out: string[] = [], b = c.getBoundingClientRect(), vw = document.documentElement.clientWidth, vh = window.innerHeight, dpr = devicePixelRatio || 1;
    if (b.height < 78) out.push(`${where}: the graph is only ${Math.round(b.height)}px tall`);
    // the boxes that clip it: scrolling or clipped ancestors, up to the full-screen box (it sits on top of everything, so nothing above it clips it).
    // A scrolling panel fades its top 40px and bottom 64px out: the graph may only reach a little way into the fade
    let top = 0, bottom = vh, left = 0, right = vw;
    for (let p = c.parentElement; p; p = p.parentElement) {
      const s = getComputedStyle(p), r = p.getBoundingClientRect();
      if (s.overflowY !== 'visible' || s.overflowX !== 'visible') {
        const fade = p.classList.contains('ls-panel');
        top = Math.max(top, r.top + (fade ? 20 : 0)); bottom = Math.min(bottom, r.bottom - (fade ? 40 : 0)); left = Math.max(left, r.left); right = Math.min(right, r.right);
      }
      if (p === document.fullscreenElement || s.position === 'fixed' && p.classList.contains('graph-box')) break;
    }
    if (b.top < top - 1 || b.bottom > bottom + 1 || b.left < left - 1 || b.right > right + 1) out.push(`${where}: the graph is cut off (${Math.round(b.top)}..${Math.round(b.bottom)}, room ${Math.round(top)}..${Math.round(bottom)})`);
    if (Math.abs(c.height - Math.round(c.clientHeight * dpr)) > 2 || Math.abs(c.width - Math.round(c.clientWidth * dpr)) > 2) out.push(`${where}: the graph is drawn at ${c.width}x${c.height} for ${c.clientWidth}x${c.clientHeight} css px at ${dpr}x (blurry)`);
    return out;
  }, where);
}

for (const [name, w, h, dpr] of sizes) {
  test.describe(`Now Playing fits a ${name}`, () => {
    test.use({ viewport: { width: w, height: h }, deviceScaleFactor: dpr });
    const phone = w <= 760, upright = phone && h > w;

    test('the bar, Now Playing, its graph and the full-screen graph: every main control on screen, none overlapping, nothing to scroll', async ({ page }) => {
      test.setTimeout(90_000);
      await signIn(page);
      const ws = page.waitForEvent('websocket', { predicate: (x) => x.url().includes('_blazor'), timeout: 20_000 });
      await page.goto('/artist/Hires%20Lab');
      await ws; await page.waitForTimeout(600);
      await page.getByRole('link', { name: /Lab Album/ }).first().click();
      await page.locator('.tracks .track-main').first().click();
      await expect(page.locator('.player')).toBeVisible();
      await page.waitForTimeout(1000);
      const problems: string[] = [];

      // the player bar along the bottom of every page (its thin progress line along the edge has a taller invisible strip to grab, which may reach
      // over the buttons by a hair)
      const bar = await check(page, ['.player button', '.player input', '.player a'], 0, [['.bar-progress', '*']]);
      problems.push(...bar.problems.map((p) => 'bar: ' + p));
      expect(bar.n, 'the bar has its buttons').toBeGreaterThan(2);

      // Now Playing
      await page.getByRole('button', { name: 'Open Now Playing' }).click();
      await expect(page.locator('.ls-names h2')).toBeVisible();
      await page.waitForTimeout(1200);
      const main = ['.ls-top button[aria-label="Close Now Playing"]', '.ls-frame', '.ls-times', '.ls-transport > button', '.ls-transport .ls-play',
        ...(phone ? ['.ls-opt', '.ls-actions > button', '.ls-actions .ls-more-btn'] : ['.ls-dock-l > button', '.ls-dock .ls-more-btn', '.ls-tiles .ls-tile', '.ls-tile-eq .chipbtn'])];
      const np = await check(page, main, upright ? 90 : 0, [['.ls-frame', '.ls-times']]);
      problems.push(...np.problems.map((p) => 'Now Playing: ' + p));
      for (const need of ['Previous', 'Next', 'Pause']) if (!np.names.includes(need)) problems.push(`Now Playing: no ${need} on screen`);
      if (!(await page.locator('.ls-frame').isVisible())) problems.push('Now Playing: no seek bar');
      // the volume: the Sound tile on a PC, behind Options with rating, speed and sleep on a phone
      if (!np.names.some((n) => n.startsWith(phone ? 'Options' : 'Sound: volume'))) problems.push('Now Playing: no way to the volume');
      // the cover is still there, just smaller (never squeezed to nothing)
      const cover = (await page.locator('.ls-cover').boundingBox())!;
      if (!cover || cover.height < 90) problems.push(`Now Playing: the cover is ${Math.round(cover?.height ?? 0)}px`);
      // nothing to scroll: Now Playing is no taller than the window, and a turn of the mouse wheel over it moves nothing (the page under it is locked)
      const tall = await page.evaluate(() => {
        const ls = document.querySelector('.ls') as HTMLElement, stage = document.querySelector('.ls-stage') as HTMLElement;
        return { ls: ls.scrollHeight - innerHeight, stage: stage.scrollHeight - stage.clientHeight, y: scrollY };
      });
      if (tall.ls > 1 || tall.stage > 1) problems.push(`Now Playing: its content is taller than the window by ${Math.max(tall.ls, tall.stage)}px`);
      await page.mouse.move(w / 2, h / 2); await page.mouse.wheel(0, 800); await page.waitForTimeout(400);
      const moved = await page.evaluate((y0) => [scrollY - y0, (document.querySelector('.ls-stage') as HTMLElement).scrollTop, (document.querySelector('.ls') as HTMLElement).scrollTop], tall.y);
      if (moved.some((v) => Math.abs(v) > 0)) problems.push(`Now Playing: the mouse wheel scrolls it (${moved.join(', ')})`);
      await page.screenshot({ path: `test-results/shots/np-fit-${w}x${h}@${dpr}.png` });

      // the graph, in Info. (The stand-in's songs last a few seconds: Repeat one keeps the same song playing, so Info is not drawn afresh for the next one
      // in the middle of the checks)
      const rep = page.locator('.ls-transport button[aria-label^="Repeat"]');
      for (let i = 0; i < 3 && (await rep.getAttribute('aria-label')) !== 'Repeat one'; i++) { await rep.click(); await page.waitForTimeout(300); }
      await expect(rep).toHaveAttribute('aria-label', 'Repeat one');
      if (phone) await page.locator('.ls-actions').getByRole('button', { name: 'Info', exact: true }).click();
      else await page.locator('.ls-seg [role=tab]', { hasText: 'Info' }).click();
      await expect(page.locator('.info .graph-box')).toBeVisible({ timeout: 20_000 });
      problems.push(...await graph(page, 'Info'));
      if (!phone) {
        // the controls stay where they are while Info is open beside the cover
        const again = await check(page, main, 0, [['.ls-frame', '.ls-times']]);
        problems.push(...again.problems.map((p) => 'with Info open: ' + p));
      }
      await page.screenshot({ path: `test-results/shots/np-fit-info-${w}x${h}@${dpr}.png` });

      // the graph on the whole screen: the graph, its tabs, previous / play / next and the close button
      await page.locator('.graph-full-btn').click();
      await expect.poll(() => page.locator('.graph-box').evaluate((b) => b.classList.contains('full') || document.fullscreenElement === b)).toBe(true);
      await page.waitForTimeout(800);
      const full = await check(page, ['.graph-box .graph-tabs', '.graph-transport button', '.graph-full-btn', '#viz'], 0);
      problems.push(...full.problems.map((p) => 'full-screen graph: ' + p));
      for (const need of ['Previous', 'Next', 'Pause', 'Full screen']) if (!full.names.includes(need)) problems.push(`full-screen graph: no ${need} on screen`);
      problems.push(...await graph(page, 'full-screen graph'));
      const fb = (await page.locator('#viz').boundingBox())!;
      if (fb.height < h * 0.35) problems.push(`full-screen graph: the graph is only ${Math.round(fb.height)}px of ${h}`);
      await page.screenshot({ path: `test-results/shots/np-fit-full-${w}x${h}@${dpr}.png` });

      expect(problems).toEqual([]);
    });
  });
}
