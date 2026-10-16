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
  await page.evaluate((s) => localStorage.setItem('flacie.ipod', JSON.stringify({ model: 'classic6', look: 1, scene: ['ipod', 'car', 'hifi', 'boombox', 'minidisc', 'discman'].indexOf(s) })), scene);
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
    for (const s of ['car', 'hifi', 'boombox', 'minidisc', 'discman']) {
      // (clicked again until the page is live: a click before it connects does nothing)
      await expect(async () => { await page.locator(`[data-testid=scene-options] [data-scene=${s}]`).click(); await expect(page.locator(`[data-testid=retro][data-scene=${s}]`)).toBeVisible({ timeout: 1000 }); }).toPass({ timeout: 20_000 });
      await expect(page.getByTestId('ipod-device')).toHaveCount(0);
      await expect(page.locator('.ipod-panel [data-group]')).toHaveCount(0);   // the iPod's model picker is put away
      // the help is the scene's own, not the iPod's click wheel
      await expect(page.getByTestId('scene-help')).toBeVisible();
      await expect(page.getByTestId('scene-help')).not.toContainText('click the middle');
    }
    await expect(page.getByTestId('scene-help')).toContainText('MEGA BASS');
    await page.reload(); await expect(page.locator('[data-testid=retro][data-scene=discman]')).toBeVisible({ timeout: 20_000 });
    await page.locator('[data-testid=scene-options] [data-scene=ipod]').click();
    await expect(page.getByTestId('ipod-device')).toBeVisible();
    await expect(page.getByTestId('retro')).toHaveCount(0);
    await expect(page.getByTestId('scene-help')).toHaveCount(0);
    // the iPod works again: its keys move the menu
    await page.getByTestId('ipod-keys').focus();
    const sel = () => page.locator('[data-testid=ipod-row]').evaluateAll((rs) => rs.findIndex((r) => r.classList.contains('sel')));
    const before = await sel(); await page.keyboard.press('ArrowDown');
    await expect.poll(sel).toBe(before + 1);
    expect(errors).toEqual([]);
  });

  for (const scene of ['car', 'hifi', 'boombox', 'minidisc', 'discman']) {
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

    test(`${scene}: every control takes a real click at every size (nothing lies over it)`, async ({ page }) => {
      await signIn(page);
      await playInScene(page, scene);
      // a real pointer press reports where it landed and goes no further (so nothing plays or pauses meanwhile)
      await page.evaluate(() => {
        const w = window as any; w.__hit = null; w.__swallow = true;
        for (const t of ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click']) window.addEventListener(t, (e) => { if (!w.__swallow) return; if (t === 'pointerdown') w.__hit = e.target; e.stopImmediatePropagation(); e.preventDefault(); }, true);
      });
      for (const [w, h] of [[1600, 900], [1024, 768], [768, 1024], [390, 844]]) {
        await page.setViewportSize({ width: w, height: h }); await page.waitForTimeout(600);
        // on a narrow screen the panel is a sheet over the scene: put it away (as anyone would to use the scene)
        const over = async () => { const p = await page.getByTestId('ipod-panel').boundingBox(); const r = (await page.getByTestId('retro').boundingBox())!; return !!p && (await page.getByTestId('ipod-panel').evaluate((e) => e.classList.contains('open'))) && p.x < r.x + r.width - 1 && p.x + p.width > r.x + 1 && p.y < r.y + r.height - 1 && p.y + p.height > r.y + 1; };
        if (await over()) { await page.evaluate(() => { (window as any).__swallow = false; }); await page.getByTestId('ipod-gear').click(); await expect.poll(over).toBe(false); await page.evaluate(() => { (window as any).__swallow = true; }); await page.waitForTimeout(400); }
        const controls = page.locator('[data-testid=retro] :is(button, input, [role=slider]):not([tabindex="-1"]):not(:disabled):visible');
        const n = await controls.count();
        expect(n, `${scene} has controls at ${w}x${h}`).toBeGreaterThan(2);
        for (let i = 0; i < n; i++) {
          const c = controls.nth(i);
          const b = await c.boundingBox(); if (!b) continue;
          // (a quarter in from the left, half way down: inside every knob and button, and the side a fanned stack of discs leaves showing)
          const x = b.x + b.width / 4, y = b.y + b.height / 2;
          if (x < 0 || y < 0 || x > w || y > h) continue;   // (off the edge of a phone's frame, like a photo)
          await page.mouse.click(x, y);
          const ok = await c.evaluate((el) => { const t = (window as any).__hit as Element | null; return !!t && (el === t || el.contains(t)); });
          const got = ok ? '' : await page.evaluate(() => { const t = (window as any).__hit as Element | null; return t ? `${t.tagName}.${t.className}` : 'nothing'; });
          expect(ok, `${scene} at ${w}x${h}: ${await c.evaluate((el) => el.getAttribute('aria-label') || el.getAttribute('data-ctl') || el.className)} is covered by ${got}`).toBe(true);
        }
      }
    });

    if (scene === 'car' || scene === 'minidisc') test(`${scene}: the display shows a long title from its first letter, then scrolls it; a short one holds still`, async ({ page }) => {
      await signIn(page);
      await playInScene(page, scene);
      const probe = (text: string) => page.getByTestId('retro-title').evaluate(async (e, t) => {
        const host = (e.querySelector('span') ?? e) as HTMLElement; host.textContent = t;
        await new Promise((r) => setTimeout(r, 400));
        const node = host.firstChild!; const r = document.createRange(); r.setStart(node, 0); r.setEnd(node, 1);
        const first = r.getBoundingClientRect(), box = e.getBoundingClientRect();
        const anim = (el: Element) => getComputedStyle(el).animationName;
        return { firstShown: first.left >= box.left - 1 && first.right <= box.right + 1, moving: [e, host].some((x) => anim(x) !== 'none'), clip: getComputedStyle(e).overflow === 'hidden' };
      }, text);
      expect(await probe('TEENAGE DREAM (THE COMPLETE CONFECTION, DELUXE EDITION)')).toEqual({ firstShown: true, moving: true, clip: true });
      expect(await probe('TEENAGE')).toEqual({ firstShown: true, moving: false, clip: true });
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

  test('discman: the album art printed on the disc; the lid opens to show it spinning; the LCD shows the track and time; stop, skip and MEGA BASS work', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page);
    await playInScene(page, 'discman');
    await freshSong(page);
    // the segment LCD: the track number and the time
    await expect(page.getByTestId('dm-lcd')).toContainText(/TRACK\s*\d+/);
    await expect(page.getByTestId('dm-time')).toHaveText(/^\d{1,2}:\d{2}$/);
    // the disc carries a print of the album's cover
    await expect.poll(() => page.getByTestId('dm-disc').evaluate((d) => getComputedStyle(d).getPropertyValue('--art')), { timeout: 10_000 }).toContain('data:image/png');
    // the lid opens (OPEN) and shows the disc spinning while it plays; it shuts again
    await expect(page.getByTestId('dm-lid')).not.toHaveClass(/open/);
    await page.locator('[data-testid=retro] [data-ctl=open]').click();
    await expect(page.getByTestId('dm-lid')).toHaveClass(/open/);
    await expect.poll(() => page.getByTestId('dm-disc').evaluate((d) => d.getAnimations().some((a) => a.playState === 'running'))).toBe(true);
    await page.locator('[data-testid=retro] [data-ctl=open]').click();
    await expect(page.getByTestId('dm-lid')).not.toHaveClass(/open/);
    // stop: paused, back to the song's start; play goes on
    await page.locator('[data-testid=retro] [data-ctl=stop]').click();
    await expect.poll(() => playing(page)).toBe('false');
    await expect(page.getByTestId('dm-time')).toHaveText('0:00');
    await page.locator('[data-testid=retro] [data-ctl=play]').click();
    await expect.poll(() => playing(page)).toBe('true');
    // MEGA BASS: its light, and the player's bass shelf (about +9 dB) on, then off again
    await page.locator('[data-testid=retro] [data-ctl=bass]').click();
    await expect(page.locator('[data-testid=retro] [data-ctl=bass]')).toHaveClass(/\bon\b/);
    await expect.poll(() => page.evaluate(() => (window as any).flacie.bassLevel())).toBeGreaterThan(6);
    await page.locator('[data-testid=retro] [data-ctl=bass]').click();
    await expect(page.locator('[data-testid=retro] [data-ctl=bass]')).not.toHaveClass(/\bon\b/);
    await expect.poll(() => page.evaluate(() => (window as any).flacie.bassLevel())).toBeLessThan(0.5);
    expect(errors).toEqual([]);
  });

  test('car: the presets are radio stations named for your genres; a press plays the station and the display says so', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page);
    await playInScene(page, 'car');
    const keys = page.locator('[data-testid=retro] .cu-pre[data-media=genre]');
    await expect(keys).toHaveCount(5);   // (the mock's five genres; the sixth key is an album coming up)
    const names = (await keys.locator('small').allTextContents()).map((s) => s.trim());
    expect(names.sort()).toEqual(['CLASSIC', 'ELECTRO', 'JAZZ', 'POP', 'ROCK']);
    for (const n of names) expect(n).toMatch(/^[A-Z&-]{1,8}$/);
    const rock = page.locator('[data-testid=retro] .cu-pre[data-media=genre]', { has: page.locator('small', { hasText: /^ROCK$/ }) });
    await rock.click();
    await expect(rock).toHaveClass(/\bon\b/);
    await expect(page.getByTestId('car-display')).toContainText('ROCK');
    await expect.poll(() => playing(page)).toBe('true');
    expect(errors).toEqual([]);
  });

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

  test('boombox: deck A turns while playing and its counter runs; stop/eject pauses and opens the door; the EQ follows the music', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page);
    await playInScene(page, 'boombox');
    await freshSong(page);
    const reels = page.locator('[data-testid=boombox-deck-a] .reel');
    const turning = () => reels.first().evaluate((r) => r.getAnimations().some((a) => a.playState === 'running'));
    await expect.poll(turning).toBe(true);
    await expect(page.getByTestId('boombox-counter')).toHaveText(/^\d{3}$/);
    await expect(page.locator('[data-testid=retro] .bb-eq [data-band]')).toHaveCount(5);
    await page.locator('[data-testid=retro] [data-ctl=eject]').click();
    await expect.poll(() => playing(page)).toBe('false');
    await expect(page.getByTestId('boombox-deck-a')).toHaveClass(/open/);
    await expect.poll(turning).toBe(false);
    // play closes the door and plays
    await page.locator('[data-testid=retro] [data-ctl=play]').first().click();
    await expect.poll(() => playing(page)).toBe('true');
    await expect(page.getByTestId('boombox-deck-a')).not.toHaveClass(/open/);
    expect(errors).toEqual([]);
  });

  test('minidisc: the LCD shows the song and its track; the jog dial skips; eject pauses and pops the disc up', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page);
    await playInScene(page, 'minidisc');
    await freshSong(page);
    await expect(page.getByTestId('md-lcd')).toContainText(/TR\s*\d+/);
    const np = (await page.locator('.dock-meta b').textContent())!.trim();
    // the jog dial: a turn on (scroll down) is the next song
    await page.getByTestId('md-jog').hover(); await page.mouse.wheel(0, 120);
    await expect(page.locator('.dock-meta b')).not.toHaveText(np, { timeout: 3000 });
    // its centre plays and pauses (once the song skipped to is audibly under way: a pause while it still loads is taken back when it starts)
    await expect(page.locator('.dock-times span').first()).toHaveText('0:01', { timeout: 10_000 });
    await page.getByTestId('md-jog-center').click();
    await expect.poll(() => playing(page)).toBe('false');
    await page.getByTestId('md-jog-center').click();
    await expect.poll(() => playing(page)).toBe('true');
    await page.locator('[data-testid=retro] [data-ctl=eject]').click();
    await expect.poll(() => playing(page)).toBe('false');
    await expect(page.getByTestId('md-disc')).toHaveClass(/out/);
    expect(errors).toEqual([]);
  });
});
