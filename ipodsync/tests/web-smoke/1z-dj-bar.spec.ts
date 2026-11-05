import { test, expect, Page } from '@playwright/test';

// F26.5 (owner, 5 Nov): a small bar above the player shows what the DJ is doing: Listening…, what it heard, Thinking / Finding your favourites…,
// then its words in time with its voice. Headless Chromium has no speech recogniser or voice: stand-ins "hear" one request and "speak" each line.

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

test('the DJ bar shows Listening, what it heard, what it is doing, then its words', async ({ page }) => {
  test.setTimeout(120_000);
  await page.addInitScript(() => {
    // a recogniser that hears one request 1.5 s after it starts
    class FakeRecogniser {
      onresult: any; onend: any; onerror: any; onspeechstart: any; lang = ''; continuous = false; interimResults = false; maxAlternatives = 1;
      start() { setTimeout(() => { this.onspeechstart?.(); this.onresult?.({ results: [[{ transcript: 'play my favourite aurora vale songs' }]] }); }, 1500); }
      stop() { setTimeout(() => this.onend?.(), 10); }
      abort() { setTimeout(() => this.onend?.(), 10); }
    }
    (window as any).SpeechRecognition = FakeRecogniser; (window as any).webkitSpeechRecognition = FakeRecogniser;
    // a voice that "speaks" each line for 3 s
    const ss = window.speechSynthesis as any;
    if (ss) ss.speak = (u: any) => { setTimeout(() => { u.onstart?.(); setTimeout(() => u.onend?.(), 3000); }, 50); };
    // everything the bar showed, in order
    const seen: string[] = []; (window as any).__bar = seen;
    new MutationObserver(() => {
      const t = (document.querySelector('[data-testid=dj-bar-dj]') as HTMLElement | null)?.innerText.trim();
      if (t && seen[seen.length - 1] !== t) seen.push(t);
    }).observe(document, { subtree: true, childList: true, characterData: true });
  });
  await page.setViewportSize({ width: 1280, height: 860 });
  await signIn(page);
  expect((await page.request.post('/api/dj/settings', { data: { enabled: true, chatty: 'rare', deviceVoice: true, captions: true, cue: false } })).ok()).toBe(true);
  await page.goto('/songs');
  await page.waitForTimeout(1200);
  await page.getByRole('button', { name: /^Play .+ by / }).first().click();
  await expect(page.locator('section.player')).toBeVisible({ timeout: 20_000 });
  const mic = page.locator('section.player').getByTestId('dj-mic');
  await expect(mic).toBeVisible({ timeout: 20_000 });
  await mic.click();
  const bar = page.getByTestId('dj-bar-dj');
  await expect(bar).toContainText('Listening…', { timeout: 5_000 });
  await expect(bar.locator('.dj-meter i')).toHaveCount(5);
  // above the player bar, not over it
  const bb = (await bar.boundingBox())!, pb = (await page.locator('section.player').boundingBox())!;
  expect(bb.y + bb.height).toBeLessThanOrEqual(pb.y + 1);
  await expect.poll(() => page.evaluate(() => (window as any).__bar as string[]), { timeout: 20_000 })
    .toEqual(expect.arrayContaining([expect.stringContaining('Listening…'), expect.stringMatching(/Heard: “play my favourite aurora vale songs”\s*Finding your favourites…/)]));
  // then the DJ's answer, in its words, while its voice speaks
  await expect(page.getByTestId('dj-bar-caption')).toContainText(/favourite|hits|Aurora/i, { timeout: 20_000 });
  await page.screenshot({ path: 'test-results/shots/dj-bar.png' });
  expect((await page.request.post('/api/dj/settings', { data: { enabled: false, deviceVoice: false, chatty: 'often', cue: true } })).ok()).toBe(true);
});
