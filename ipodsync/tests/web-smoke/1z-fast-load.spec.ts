import { test, expect, Page } from '@playwright/test';

// Speed without losing anything: the page's files come compressed and are kept by the browser, and while music plays a page of songs is not
// sent to the browser again on every tick of the player's clock (only the player bar, which shows the time, is).

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(1500);
}

test("Blazor's own script comes compressed, and the app's styles and scripts are kept for good", async ({ page, request }) => {
  const br = await request.get('/_framework/blazor.web.js', { headers: { 'Accept-Encoding': 'br' } });
  expect(br.status()).toBe(200);
  expect(br.headers()['content-encoding']).toBe('br');
  expect((await br.body()).length).toBeGreaterThan(100_000);   // (the client reads it back decompressed)
  const gz = await request.get('/_framework/blazor.web.js', { headers: { 'Accept-Encoding': 'gzip' } });
  expect(gz.headers()['content-encoding']).toBe('gzip');
  // nothing else is compressed on the way (private data in a compressed answer can leak: BREACH)
  const health = await request.get('/healthz', { headers: { 'Accept-Encoding': 'br, gzip' } });
  expect(health.headers()['content-encoding'] ?? '').toBe('');
  await signIn(page);
  // the web fonts' sheet is one of the app's kept files (it used to be asked for again on every page load, holding the page up)
  const fontsHref = await page.locator('link[rel=stylesheet][href*="fonts/ls/"]').getAttribute('href');
  expect(fontsHref).toMatch(/fonts\/ls\/fonts\.[a-z0-9]{10}\.css$/);
  const fontsCss = await request.get('/' + fontsHref);
  expect(fontsCss.headers()['cache-control']).toContain('immutable');
  // ... and its fonts still load from beside it
  await expect.poll(() => page.evaluate(async () => { await document.fonts.ready; return [...document.fonts].some((f) => /Archivo/.test(f.family) && f.status === 'loaded'); })).toBe(true);
  const files: { url: string; enc: string; cache: string }[] = [];
  page.on('response', (r) => { if (/\/[a-z-]+\.[a-z0-9]{10}\.(css|js)$/.test(r.url())) files.push({ url: r.url(), enc: r.headers()['content-encoding'] || '', cache: r.headers()['cache-control'] || '' }); });
  await page.reload();
  await page.waitForLoadState('load');
  expect(files.length).toBeGreaterThan(10);
  for (const f of files) {
    expect(f.cache, f.url).toContain('immutable');
  }
});

test('while a song plays, a page of songs is not sent to the browser again on every clock tick', async ({ page, context }) => {
  test.setTimeout(90_000);
  // the mock's songs last 3 s: looped here, so the clock ticks on with no change of song
  await context.addInitScript(() => { const op = HTMLMediaElement.prototype.play; HTMLMediaElement.prototype.play = function () { this.loop = true; return op.call(this); }; });
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await page.goto('/artist/Brass%20Monkeys');
  await page.getByRole('link', { name: /Brass Album/ }).first().click();
  await page.locator('.tracks .track-main').first().click();
  await expect.poll(() => page.evaluate(() => (window as any).__flaciePlayingAt || 0), { timeout: 15000 }).toBeGreaterThan(0);
  await page.evaluate(() => (window as any).Blazor.navigateTo('/songs'));
  await expect(page.locator('.tracks .track-main').first()).toBeVisible();
  await page.waitForTimeout(2500);
  const cdp = await context.newCDPSession(page);
  await cdp.send('Network.enable');
  let bytes = 0;
  cdp.on('Network.webSocketFrameReceived', (e: any) => { bytes += e.response.payloadData.length; });
  // the clock still moves (the bar is drawn on each tick) ...
  const seen = new Set<string>();
  for (let i = 0; i < 15; i++) { seen.add((await page.locator('.bar-progress').getAttribute('aria-valuetext')) || ''); await page.waitForTimeout(400); }
  expect(seen.size).toBeGreaterThan(1);
  // ... but only the bar: before, the whole list came again twice a second (about 19 KB a second); the bar alone is about 4 KB a second
  console.log(`page updates while playing: ${Math.round(bytes / 6 / 1024)} KB/s`);
  expect(bytes / 6).toBeLessThan(9_000);
});

test('a song starting restyles the play buttons, not the whole page', async ({ page, browser }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await page.goto('/albums');
  await expect(page.locator('.grid .card').first()).toBeVisible();
  await page.waitForTimeout(1000);
  const all = await page.evaluate(() => document.getElementsByTagName('*').length);
  // the spinner on the play buttons comes and goes as each song starts (html.buffering), and the player bar's icon changes with it
  await browser.startTracing(page, { categories: ['devtools.timeline'] });
  for (let i = 0; i < 4; i++) {
    await page.evaluate(() => {
      document.documentElement.classList.toggle('buffering');
      const b = document.querySelector('.player .play') || document.body;
      b.appendChild(document.createElementNS('http://www.w3.org/2000/svg', 'svg')).remove();
      document.body.offsetHeight;
    });
    await page.waitForTimeout(100);
  }
  const events = JSON.parse((await browser.stopTracing()).toString()).traceEvents;
  const restyled = events.filter((e: any) => e.name === 'UpdateLayoutTree' && e.ph === 'X').map((e: any) => e.args?.elementCount ?? 0);
  console.log(`elements on the page: ${all}; restyled: ${restyled.join(', ')}`);
  expect(restyled.length).toBeGreaterThan(0);
  // before, every element was restyled each time (:has() rules and a rule ending in ".ic" made the browser look at the whole page)
  for (const n of restyled) expect(n).toBeLessThan(all / 10);
});
