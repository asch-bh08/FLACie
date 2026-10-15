import { test, expect, Page } from '@playwright/test';

// The listening scenes beside the iPod (/ipod, panel › Scene): a car stereo, a hi-fi with a turntable, a boombox, a MiniDisc system. Every scene shows the
// song playing and drives the real player (play/pause, next, previous, media that play an album or a playlist), fits a desktop and a phone, and holds still
// when motion is reduced. Each has its own hardware on top (tested below the shared part).
async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(800);
}
const consoleErrors = (page: Page) => { const e: string[] = []; page.on('console', (m) => { if (m.type() === 'error') e.push(m.text()); }); page.on('pageerror', (x) => e.push(String(x))); return e; };
/** Plays the songs list from its first song, then opens /ipod in a scene (in-page, so the song keeps playing). */
async function playInScene(page: Page, scene: string, size: [number, number] = [1600, 900]) {
  await page.setViewportSize({ width: size[0], height: size[1] });
  await page.goto('/songs');
  await page.locator('.track').first().locator('button.track-main').click();
  await expect(page.locator('.player')).toBeVisible();
  await page.evaluate((s) => localStorage.setItem('flacie.ipod', JSON.stringify({ model: 'classic6', look: 1, scene: ['ipod', 'car', 'hifi', 'boombox', 'minidisc'].indexOf(s) })), scene);
  await page.evaluate(() => (window as any).Blazor.navigateTo('/ipod'));
  await expect(page.locator(`[data-testid=retro][data-scene=${scene}]`)).toBeVisible({ timeout: 20_000 });
}
const title = (page: Page) => page.getByTestId('retro-title');
/** Waits for a song to start from its beginning: the mock's songs last 3 s, so what a test does next has a whole song's time, not the end of one. */
const freshSong = (page: Page) => expect.poll(async () => (await page.locator('.dock-times span').first().textContent())?.trim(), { timeout: 10_000, intervals: [50] }).toBe('0:00');
const playing = (page: Page) => page.getByTestId('retro').getAttribute('data-playing');
type Box = { x: number; y: number; width: number; height: number };
const inside = (a: Box, b: Box, slack = 2) => a.x >= b.x - slack && a.y >= b.y - slack && a.x + a.width <= b.x + b.width + slack && a.y + a.height <= b.y + b.height + slack;

test.describe.serial('listening scenes', () => {
  test('the panel switches scenes, keeps the choice, and the iPod comes back working', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await page.addInitScript(() => { try { if (!sessionStorage.getItem('seeded')) { sessionStorage.setItem('seeded', '1'); localStorage.setItem('flacie.ipod', JSON.stringify({ model: 'classic6', look: 1 })); } } catch { } });
    await page.goto('/ipod');
    await expect(page.getByTestId('ipod-device')).toBeVisible({ timeout: 20_000 });
    for (const s of ['car', 'hifi', 'boombox', 'minidisc']) {
      // (clicked again until the page is live: a click before it connects does nothing)
      await expect(async () => { await page.locator(`[data-testid=scene-options] [data-scene=${s}]`).click(); await expect(page.locator(`[data-testid=retro][data-scene=${s}]`)).toBeVisible({ timeout: 1000 }); }).toPass({ timeout: 20_000 });
      await expect(page.getByTestId('ipod-device')).toHaveCount(0);
      await expect(page.locator('.ipod-panel [data-group]')).toHaveCount(0);   // the iPod's model picker is put away
    }
    await page.reload(); await expect(page.locator('[data-testid=retro][data-scene=minidisc]')).toBeVisible({ timeout: 20_000 });
    await page.locator('[data-testid=scene-options] [data-scene=ipod]').click();
    await expect(page.getByTestId('ipod-device')).toBeVisible();
    await expect(page.getByTestId('retro')).toHaveCount(0);
    // the iPod works again: its keys move the menu
    await page.getByTestId('ipod-keys').focus();
    const sel = () => page.locator('[data-testid=ipod-row]').evaluateAll((rs) => rs.findIndex((r) => r.classList.contains('sel')));
    const before = await sel(); await page.keyboard.press('ArrowDown');
    await expect.poll(sel).toBe(before + 1);
    expect(errors).toEqual([]);
  });

  for (const scene of ['car', 'hifi']) {
    test(`${scene}: shows the song, and play/pause, next, previous and its media drive the player`, async ({ page }) => {
      const errors = consoleErrors(page);
      await signIn(page);
      await playInScene(page, scene);
      const np = (await page.locator('.dock-meta b').textContent())!.trim();
      await expect(title(page)).toContainText(np, { ignoreCase: true });
      await expect.poll(() => playing(page)).toBe('true');
      await freshSong(page);
      await page.locator('[data-testid=retro] [data-ctl=play]').first().click();
      await expect.poll(() => playing(page)).toBe('false');
      await page.locator('[data-testid=retro] [data-ctl=play]').first().click();
      await expect.poll(() => playing(page)).toBe('true');
      await page.locator('[data-testid=retro] [data-ctl=next]').first().click();
      await expect(page.locator('.dock-meta b')).not.toHaveText(np, { timeout: 5_000 });
      const second = (await page.locator('.dock-meta b').textContent())!.trim();
      await expect(title(page)).toContainText(second, { ignoreCase: true });
      await page.locator('[data-testid=retro] [data-ctl=prev]').first().click();
      await page.locator('[data-testid=retro] [data-ctl=prev]').first().click();   // (a second press: the first goes back to the song's start once it has played 3 s)
      await expect(page.locator('.dock-meta b')).not.toHaveText(second, { timeout: 5_000 });
      // a piece of media plays an album coming up
      const media = page.locator('[data-testid=retro] [data-media=album]').first();
      const album = (await media.getAttribute('data-album'))!;
      await media.click();
      await expect(page.locator('.dock-meta small')).toHaveText(album, { timeout: 5_000 });
      expect(errors).toEqual([]);
    });

    test(`${scene}: fits a desktop, a laptop with the panel and a phone`, async ({ page }) => {
      await signIn(page);
      await playInScene(page, scene);
      for (const [w, h] of [[1600, 900], [1280, 720], [1920, 1080], [390, 844], [360, 640]]) {
        await page.setViewportSize({ width: w, height: h }); await page.waitForTimeout(500);
        const root = (await page.getByTestId('retro').boundingBox())!;
        const unit = (await page.getByTestId('retro-unit').boundingBox())!;
        expect(inside(unit, root, 4), `${scene} unit inside the scene at ${w}x${h}`).toBe(true);
        expect(unit.width, `${scene} unit not tiny at ${w}x${h}`).toBeGreaterThan(Math.min(root.width, root.height) * 0.45);
        await expect(title(page)).toBeVisible();
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${w}x${h} scrolls sideways`).toBe(true);
      }
    });

    test(`${scene}: reduced motion holds the scene still`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: 'reduce' });
      await signIn(page);
      await playInScene(page, scene);
      await page.waitForTimeout(600);
      const running = await page.getByTestId('retro').evaluate((r) => r.getAnimations({ subtree: true }).filter((a) => a.playState === 'running').length);
      expect(running).toBe(0);
    });
  }

  test('hifi: the record turns while playing and the tonearm follows the song; the record seeks, the pitch slider sets the speed, the crate flips and plays', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page);
    await playInScene(page, 'hifi');
    const rec = page.getByTestId('hifi-record');
    await freshSong(page);
    await expect.poll(() => rec.evaluate((r) => r.getAnimations().some((a) => a.playState === 'running'))).toBe(true);
    // the tonearm is on the record while it plays
    const arm = () => page.getByTestId('hifi-arm').evaluate((a) => parseFloat(getComputedStyle(a).getPropertyValue('--arm')) || 0);
    await expect.poll(arm).toBeGreaterThan(10);   // (on the record, past the lead-in; the mock's songs last 3 s, so how far in changes fast)
    // paused: the record stops, the arm lifts back to its rest
    await page.locator('[data-testid=retro] [data-ctl=play]').first().click();
    await expect.poll(() => rec.evaluate((r) => r.getAnimations().some((a) => a.playState === 'running'))).toBe(false);
    await expect.poll(arm).toBe(0);
    await page.locator('[data-testid=retro] [data-ctl=play]').first().click();
    // a click near the record's centre seeks far into the song (the mock's songs last 3 s: from a fresh start, 2 s in within a second is the seek)
    await freshSong(page);
    const box = (await page.getByTestId('hifi-groove').boundingBox())!;
    await page.mouse.click(box.x + box.width * 0.5 + box.width * 0.2, box.y + box.height * 0.5);
    await expect.poll(async () => { const t = (await page.locator('.dock-times span').first().textContent())!.trim(); const [m, sec] = t.split(':').map(Number); return m * 60 + sec; }, { timeout: 900, intervals: [50] }).toBeGreaterThanOrEqual(2);
    // the pitch slider sets the speed
    await page.getByTestId('hifi-pitch').fill('1.06');
    await expect(page.getByTestId('hifi-pitch')).toHaveAttribute('aria-valuetext', '+6%');
    // the crate: flipping changes the front sleeve; the front one plays (paused first: the mock's 3 s songs would move the albums coming up meanwhile)
    await page.locator('[data-testid=retro] [data-ctl=play]').first().click();
    await expect.poll(() => playing(page)).toBe('false');
    const front = () => page.locator('[data-testid=hifi-crate] .sleeve.front').getAttribute('data-album');
    const f0 = await front();
    await page.getByTestId('hifi-flip-next').click();
    await expect.poll(front).not.toBe(f0);
    const f1 = (await front())!;
    await page.locator('[data-testid=hifi-crate] .sleeve.front').click();
    await expect(page.locator('.dock-meta small')).toHaveText(f1, { timeout: 5000 });
    expect(errors).toEqual([]);
  });
});
