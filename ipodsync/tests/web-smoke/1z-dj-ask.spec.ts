import { test, expect, Page } from '@playwright/test';

// "Talk to the DJ" (docs/dj-ask-report.md): POST /api/dj/ask turns the recognised words into an action for the signed-in person, and the mic button beside the
// controls (Now Playing, the player bar, the search box) listens with the browser's recogniser, here a stand-in that "hears" window.__said. The test server has no
// Cloudflare, so answers come back with no clip and show as a toast. Also: Settings → DJ from Now Playing's DJ button closes the player first.

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(1000);
}

async function live(page: Page, url: string) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto(url);
  await ws; await page.waitForTimeout(800);
}

async function newQueue(page: Page) {
  await live(page, '/songs');
  await page.getByRole('button', { name: /^Play .+ by / }).first().click();
  await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 20_000 });
}

// a stand-in recogniser: start() "hears" window.__said after a moment (or nothing, when it is null, until stop())
const fakeRecogniser = () => {
  class FakeRec {
    lang = ''; continuous = false; interimResults = false; maxAlternatives = 1;
    onresult: any = null; onend: any = null; onerror: any = null; running = false;
    start() {
      this.running = true; (window as any).__listening = true;
      const said = (window as any).__said;
      if (said) setTimeout(() => { if (!this.running) return; this.onresult?.({ results: [[{ transcript: said }]] }); this.stop(); }, 150);
    }
    stop() { if (!this.running) return; this.running = false; (window as any).__listening = false; setTimeout(() => this.onend?.(), 10); }
    abort() { this.stop(); }
  }
  (window as any).webkitSpeechRecognition = FakeRec;
  (window as any).__said = null;
};

test.describe.serial('talk to the DJ', () => {
  test('the endpoint understands requests for the signed-in person only', async ({ page, playwright, baseURL }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await live(page, '/songs');
    const ask = async (text: string) => { const r = await page.request.post('/api/dj/ask', { data: { text } }); expect(r.ok()).toBe(true); return r.json(); };
    const artist = await ask('play Cobalt Sky');
    expect(artist).toMatchObject({ do: 'play', say: "Here's some Cobalt Sky.", clip: null, intent: 'Play' });
    expect(artist.paths.length).toBeGreaterThan(0);
    expect(await ask('next')).toMatchObject({ do: 'next', say: '' });
    expect(await ask('turn it up')).toMatchObject({ do: 'volumeup' });
    expect(await ask('DJ off')).toMatchObject({ do: 'djoff' });
    expect(await ask('shuffle my library')).toMatchObject({ do: 'play', say: 'Shuffling your library.' });
    expect(await ask('blah blah weather')).toMatchObject({ do: 'none', say: "Sorry, I didn't catch that." });
    expect((await page.request.post('/api/dj/ask', { data: { nope: 1 } })).status()).toBe(400);
    // no sign-in, no answer
    const anon = await playwright.request.newContext({ baseURL });
    expect((await anon.post('/api/dj/ask', { data: { text: 'next' } })).status()).toBe(401);
    expect((await anon.get('/api/dj/ask/landed?artist=a&title=b')).status()).toBe(401);
    await anon.dispose();
    expect(await (await page.request.get('/api/dj/ask/landed?artist=Nobody&title=Nothing')).json()).toEqual({ path: null });
  });

  test('the mic on Now Playing listens, then plays what was asked and shows the answer', async ({ page }) => {
    test.setTimeout(120_000);
    await page.addInitScript(fakeRecogniser);
    await signIn(page);
    await newQueue(page);
    // the player bar has one too
    await expect(page.locator('.player [data-testid="dj-mic"]')).toBeVisible();
    await page.getByRole('button', { name: 'Open Now Playing' }).click();
    await expect(page.locator('.ls-transport')).toBeVisible({ timeout: 20_000 });
    const mic = page.locator('.ls-transport [data-testid="dj-mic"]');
    await expect(mic).toBeVisible();
    // mirrored: the DJ button before Previous, the mic after Next
    const order = await page.locator('.ls-transport > *').evaluateAll((els) => els.map((e) => e.getAttribute('aria-label') || e.className));
    expect(order.indexOf('Talk to the DJ')).toBeGreaterThan(order.indexOf('Next'));
    expect(order.indexOf('DJ')).toBeLessThan(order.indexOf('Previous'));

    // nothing heard: listening until pressed again
    await mic.click();
    await expect(mic).toHaveAttribute('aria-pressed', 'true');
    await expect(mic).toHaveClass(/listening/);
    expect(await page.evaluate(() => (window as any).__listening)).toBe(true);
    await mic.click();
    await expect(mic).toHaveAttribute('aria-pressed', 'false');
    expect(await page.evaluate(() => (window as any).__listening)).toBe(false);

    // a request: carried out, and the answer shown (no Cloudflare here, so no voice)
    await page.evaluate(() => { (window as any).__said = 'play Cobalt Sky'; });
    await mic.click();
    await expect(page.locator('.toast', { hasText: "Here's some Cobalt Sky." })).toBeVisible({ timeout: 15_000 });
    await expect(page.locator('.ls-transport')).toBeVisible();
    await expect(page.locator('.ls-side, .ls-main').getByText('Cobalt Sky').first()).toBeVisible({ timeout: 15_000 });
    await expect(mic).toHaveAttribute('aria-pressed', 'false');
    await page.screenshot({ path: 'test-results/shots/dj-ask-now-playing.png' });

    // a control: pause
    await page.evaluate(() => { (window as any).__said = 'pause'; });
    await mic.click();
    await expect(page.locator('.ls-play')).toHaveAttribute('aria-label', 'Play', { timeout: 10_000 });
  });

  test('the search box mic fills the search, or runs a request', async ({ page }) => {
    test.setTimeout(120_000);
    await page.addInitScript(fakeRecogniser);
    await signIn(page);
    await live(page, '/');
    const mic = page.locator('.top-search [data-testid="dj-mic"]');
    await expect(mic).toBeVisible();
    await page.evaluate(() => { (window as any).__said = 'delta hum'; });
    await mic.click();
    await page.waitForURL(/\/search\?q=delta%20hum/, { timeout: 15_000 });
    await expect(page.getByRole('searchbox', { name: 'Search' })).toHaveValue('delta hum');
    // a request is carried out instead of searched for
    await page.evaluate(() => { (window as any).__said = 'play Echo Harbour'; });
    await mic.click();
    await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 20_000 });
    await expect(page.locator('.player .bar-text')).toContainText('Echo Harbour', { timeout: 15_000 });
    expect(new URL(page.url()).searchParams.get('q')).toBe('delta hum');
  });

  test('no recogniser in this browser (Firefox): no mic anywhere', async ({ page }) => {
    test.setTimeout(120_000);
    await page.addInitScript(() => { delete (window as any).webkitSpeechRecognition; delete (window as any).SpeechRecognition; });
    await signIn(page);
    await newQueue(page);
    await page.getByRole('button', { name: 'Open Now Playing' }).click();
    await expect(page.locator('.ls-transport')).toBeVisible({ timeout: 20_000 });
    await page.waitForTimeout(500);
    await expect(page.locator('[data-testid="dj-mic"]')).toHaveCount(0);
  });

  test('a title with descenders ("Lovegame") is not clipped on the player bar', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await newQueue(page);
    const b = page.locator('.player .bar-text b').first();
    await expect(b).toBeVisible();
    // the bar's title set to "Lovegame" in the bar's own markup (last word in italics), then measured against every box that clips it
    const r = await b.evaluate((el) => {
      el.innerHTML = 'Love<i>game</i>';
      const range = document.createRange(); range.selectNodeContents(el);
      const text = range.getBoundingClientRect();
      let clip = Infinity;
      for (let n: HTMLElement | null = el; n; n = n.parentElement) {
        const s = getComputedStyle(n);
        if (s.overflow !== 'visible' || s.overflowY !== 'visible') clip = Math.min(clip, n.getBoundingClientRect().bottom - parseFloat(s.borderBottomWidth || '0'));
      }
      return { textBottom: text.bottom, clip };
    });
    await page.screenshot({ path: '../../../docs/screens-new-look/dj-ask-bar-lovegame.png', clip: (await page.locator('.player').first().boundingBox())! });
    expect(r.textBottom).toBeLessThanOrEqual(r.clip + 0.5);
  });

  test('Settings → DJ from the DJ button closes Now Playing first', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await newQueue(page);
    await page.getByRole('button', { name: 'Open Now Playing' }).click();
    await expect(page.locator('.ls-transport')).toBeVisible({ timeout: 20_000 });
    await page.locator('.ls-dj').click({ button: 'right' });
    await page.waitForURL(/\/settings#dj$/, { timeout: 15_000 });
    await expect(page.locator('.ls-transport')).toHaveCount(0, { timeout: 10_000 });
    await expect(page.locator('#dj')).toBeVisible();
  });
});
