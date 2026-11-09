import { test, expect, Page } from '@playwright/test';

// Owner, 9 Nov: the DJ mic worked once, the music came back, then it said "listening" and could not be pressed again. The listening must end when the
// music plays again, on an error, and after a time limit, and the mic must take a second press. Headless Chromium has no recogniser: a stand-in
// "hears" what each start is told to.

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

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    // each start does what window.__plan says next: "ask:<words>" hears a request, "music" hears the song's words on and on (a recogniser left
    // open while the music plays), "error" fails, "quiet" hears nothing
    (window as any).__plan = [];
    (window as any).__starts = 0;
    class FakeRecogniser {
      onresult: any; onend: any; onerror: any; onspeechstart: any; lang = ''; continuous = false; interimResults = false; maxAlternatives = 1;
      t: any = 0; done = false;
      start() {
        (window as any).__starts++;
        const step: string = (window as any).__plan.shift() || 'quiet';
        if (step.startsWith('ask:')) setTimeout(() => { this.onspeechstart?.(); this.onresult?.({ results: [[{ transcript: step.slice(4) }]] }); }, 600);
        else if (step === 'music') { let n = 0; this.t = setInterval(() => this.onresult?.({ results: [[{ transcript: 'la la la ' + n++ }]] }), 300); }
        else if (step === 'error') setTimeout(() => { this.onerror?.({ error: 'audio-capture' }); this.finish(); }, 400);
      }
      finish() { clearInterval(this.t); if (this.done) return; this.done = true; setTimeout(() => this.onend?.(), 10); }
      stop() { this.finish(); }
      abort() { this.finish(); }
    }
    (window as any).SpeechRecognition = FakeRecogniser; (window as any).webkitSpeechRecognition = FakeRecogniser;
    const ss = window.speechSynthesis as any;
    if (ss) ss.speak = (u: any) => { setTimeout(() => { u.onstart?.(); setTimeout(() => u.onend?.(), 800); }, 50); };
  });
  await page.setViewportSize({ width: 1280, height: 860 });
  await signIn(page);
  expect((await page.request.post('/api/dj/settings', { data: { enabled: true, chatty: 'rare', deviceVoice: true, captions: true, cue: false } })).ok()).toBe(true);
  await page.goto('/songs');
  await page.waitForTimeout(1200);
  await page.getByRole('button', { name: /^Play .+ by / }).first().click();
  await expect(page.locator('section.player')).toBeVisible({ timeout: 20_000 });
});

test.afterEach(async ({ page }) => {
  await page.request.post('/api/dj/settings', { data: { enabled: false, deviceVoice: false, chatty: 'often', cue: true } });
});

const plan = (page: Page, ...steps: string[]) => page.evaluate((s) => { (window as any).__plan.push(...s); }, steps);
const mic = (page: Page) => page.locator('section.player').getByTestId('dj-mic');

test('the mic takes a second press after the DJ played something', async ({ page }) => {
  test.setTimeout(90_000);
  await expect(mic(page)).toBeVisible({ timeout: 20_000 });
  await plan(page, 'ask:play aurora vale', 'ask:pause');
  await mic(page).click();
  await expect(mic(page)).toHaveAttribute('aria-pressed', 'true', { timeout: 5_000 });
  // it heard, played, and let go
  await expect(mic(page)).toHaveAttribute('aria-pressed', 'false', { timeout: 20_000 });
  await expect(mic(page)).not.toHaveClass(/\bon\b|busy/, { timeout: 20_000 });
  await mic(page).click();
  await expect.poll(() => page.evaluate(() => (window as any).__starts), { timeout: 10_000 }).toBe(2);
  await expect(mic(page)).toHaveAttribute('aria-pressed', 'true', { timeout: 5_000 });
  await expect(mic(page)).toHaveAttribute('aria-pressed', 'false', { timeout: 20_000 });
});

test('the music coming back ends the listening, and the mic works again', async ({ page }) => {
  test.setTimeout(90_000);
  await expect(mic(page)).toBeVisible({ timeout: 20_000 });
  // "what's this song" is answered and the mic listens again; there it hears only the song
  await plan(page, "ask:what's this song", 'music');
  await mic(page).click();
  await expect.poll(() => page.evaluate(() => (window as any).__starts), { timeout: 20_000 }).toBe(2);
  await expect(mic(page)).toHaveAttribute('aria-pressed', 'true');
  // the music comes back while it listens
  await page.locator('section.player button.play').click();
  await expect(mic(page)).toHaveAttribute('aria-pressed', 'false', { timeout: 5_000 });
  await expect(page.getByTestId('dj-bar-dj')).not.toContainText('Listening', { timeout: 5_000 });
  await plan(page, 'quiet');
  await mic(page).click();
  await expect.poll(() => page.evaluate(() => (window as any).__starts), { timeout: 10_000 }).toBe(3);
});

test('an error ends the listening, and the mic works again', async ({ page }) => {
  test.setTimeout(60_000);
  await expect(mic(page)).toBeVisible({ timeout: 20_000 });
  await plan(page, 'error');
  await mic(page).click();
  await expect.poll(() => page.evaluate(() => (window as any).__starts), { timeout: 10_000 }).toBe(1);
  await page.waitForTimeout(600);
  await expect(mic(page)).toHaveAttribute('aria-pressed', 'false', { timeout: 5_000 });
  await expect(mic(page)).not.toHaveClass(/\bon\b/, { timeout: 5_000 });
  await mic(page).click();
  await expect.poll(() => page.evaluate(() => (window as any).__starts), { timeout: 10_000 }).toBe(2);
});

test('listening stops by itself after a time limit', async ({ page }) => {
  test.setTimeout(90_000);
  await expect(mic(page)).toBeVisible({ timeout: 20_000 });
  // a recogniser that never stops hearing (words with no pause)
  await plan(page, 'music', 'quiet');
  await mic(page).click();
  await expect(mic(page)).toHaveAttribute('aria-pressed', 'true', { timeout: 5_000 });
  await expect(mic(page)).toHaveAttribute('aria-pressed', 'false', { timeout: 25_000 });
  await expect(mic(page)).not.toHaveClass(/busy/, { timeout: 30_000 });
  await mic(page).click();
  await expect.poll(() => page.evaluate(() => (window as any).__starts), { timeout: 10_000 }).toBeGreaterThanOrEqual(2);
});
