import { test, expect, Page, Locator } from '@playwright/test';

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
/** Whether a record, reel or disc is turning (desk.js turns them on its own frames): its angle a quarter second apart. */
const turns = (l: Locator) => l.evaluate(async (d: HTMLElement) => { const a = d.style.rotate; await new Promise((r) => setTimeout(r, 250)); return d.style.rotate !== a; });
type Box = { x: number; y: number; width: number; height: number };
const inside = (a: Box, b: Box, slack = 2) => a.x >= b.x - slack && a.y >= b.y - slack && a.x + a.width <= b.x + b.width + slack && a.y + a.height <= b.y + b.height + slack;
/** The scenes that show the line being sung and what the file is (one by one, as each gets its display). */
const SINGING = new Set<string>(['car', 'hifi', 'minidisc', 'discman']);

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
        // the line being sung and what the file is: on the device itself, at every size
        if (SINGING.has(scene)) for (const id of ['retro-lyric', 'retro-format']) {
          const b = await page.getByTestId(id).boundingBox();
          expect(!!b && inside(b, root, 4) && b.width > 30 && b.height > 6, `${scene} ${id} shown inside the scene at ${w}x${h}`).toBe(true);
        }
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${w}x${h} scrolls sideways`).toBe(true);
      }
    });

    if (SINGING.has(scene)) test(`${scene}: shows the line being sung, in time with the song, and what the file is`, async ({ page }) => {
      const errors = consoleErrors(page);
      await signIn(page);
      await playInScene(page, scene);
      // what the file is (the mock's songs: CD-quality FLAC), once FLACie has read it
      const format = page.getByTestId('retro-format');
      await expect(format).toBeVisible();
      await expect(format).toContainText(/FLAC/);
      await expect(format).toContainText(/44\.1/, { timeout: 10_000 });
      // the line being sung: the one whose time has come by the song's own clock (the mock: a line every 0.6 s)
      const sung = () => page.getByTestId('retro-lyric').evaluate((box) => {
        const at = (window as any).flacie.clock()[0] * 1000;
        const lines = [...box.querySelectorAll('[data-ms]')] as HTMLElement[];
        const cur = lines.findIndex((l) => l.classList.contains('cur'));
        if (cur < 0) return `none at ${Math.round(at)} ms (${lines.length} lines)`;
        const ms = +lines[cur].dataset.ms!, next = lines[cur + 1] ? +lines[cur + 1].dataset.ms! : Infinity;
        return ms <= at + 400 && next > at - 250 ? 'in time' : `line ${cur} (${ms} ms) at ${Math.round(at)} ms`;
      });
      await expect.poll(sung, { timeout: 10_000, intervals: [100] }).toBe('in time');
      const line = page.locator('[data-testid=retro-lyric] .cur');
      await expect(line).toBeVisible();
      await expect(line).toContainText(/mock lyric \d+/);
      // and it goes on with the song
      const first = (await line.textContent())!;
      await expect(page.locator('[data-testid=retro-lyric] .cur')).not.toHaveText(first, { timeout: 3000 });
      await expect.poll(sung, { timeout: 5_000, intervals: [100] }).toBe('in time');
      expect(errors).toEqual([]);
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
        // on a narrow screen the panel is a sheet over the scene: put it away (as anyone would to use the scene), and wait until it has slid off (where it
        // is, not its class: it slides)
        const over = async () => { const p = await page.getByTestId('ipod-panel').boundingBox(); const r = (await page.getByTestId('retro').boundingBox())!; return !!p && p.x < r.x + r.width - 1 && p.x + p.width > r.x + 1 && p.y < r.y + r.height - 1 && p.y + p.height > r.y + 1; };
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
    await expect.poll(() => turns(page.getByTestId('dm-disc'))).toBe(true);
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

  test('discman: the remote shows MEGA BASS as the real state (it survives a change of scene) and its VOL keys are the real volume', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page);
    await playInScene(page, 'discman');
    const lit = page.getByTestId('dm-remote-bass');
    const opacity = () => lit.evaluate((e) => parseFloat(getComputedStyle(e).opacity));
    expect(await opacity()).toBeLessThan(0.3);
    await page.locator('[data-testid=retro] [data-ctl=bass]').click();
    await expect.poll(opacity).toBeGreaterThan(0.7);
    // another scene and back: the bass shelf is still on in the player, and the scene knows it
    await page.locator('[data-testid=scene-options] [data-scene=hifi]').click();
    await expect(page.locator('[data-testid=retro][data-scene=hifi]')).toBeVisible();
    await page.locator('[data-testid=scene-options] [data-scene=discman]').click();
    await expect(page.locator('[data-testid=retro][data-scene=discman]')).toBeVisible();
    await expect.poll(opacity).toBeGreaterThan(0.7);
    await expect(page.locator('[data-testid=retro] [data-ctl=bass]')).toHaveClass(/\bon\b/);
    expect(await page.evaluate(() => (window as any).flacie.bassLevel())).toBeGreaterThan(6);
    // the volume keys
    const real = () => page.evaluate(() => Math.round((window as any).flacie.volume() * 100));
    const lcd = async () => Number(((await page.getByTestId('dm-vol').textContent()) ?? '').replace(/\D/g, ''));
    const start = await real();
    expect(await lcd()).toBe(Math.round(start / 5));
    await page.locator('[data-testid=retro] [data-ctl=vol-down]').click();
    await expect.poll(real).toBe(start - 5);
    await expect.poll(lcd).toBe(Math.round((start - 5) / 5));
    await page.locator('[data-testid=retro] [data-ctl=vol-up]').click();
    await page.locator('[data-testid=retro] [data-ctl=vol-up]').click();
    await expect.poll(real).toBe(Math.min(100, start + 5));
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

  test('car: the EQ key steps the sounds of the real EQ, and the display names the sound', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page);
    await playInScene(page, 'car');
    const eq = () => page.evaluate(() => (window as any).flacie.eqGet());
    const key = page.locator('[data-testid=retro] [data-ctl=eq]');
    await expect(page.getByTestId('car-eq-name')).toHaveText('EQ FLAT');
    const seen: string[] = [];
    for (const [name, bands] of [['ROCK', [5, 2, -2, 2, 5]], ['POP', [-1, 2, 4, 2, -1]], ['JAZZ', [3, 1, -1, 2, 3]], ['CLASSIC', [2, 0, -1, 1, 3]], ['VOCAL', [-2, 0, 3, 3, 0]], ['BASS', [8, 3, 0, 0, 0]], ['FLAT', [0, 0, 0, 0, 0]]] as [string, number[]][]) {
      await key.click();
      await expect(page.getByTestId('car-eq-name')).toHaveText(`EQ ${name}`);
      await expect.poll(eq).toEqual(bands);   // (the music goes through it, and the desk's preamp shows the same)
      seen.push(name);
    }
    expect(seen).toHaveLength(7);
    // a setting made by hand is CUSTOM, and the key goes to FLAT from it
    await page.evaluate(() => (window as any).flacie.eq([1, 2, 3, 4, 5]));
    await page.reload(); await expect(page.locator('[data-testid=retro][data-scene=car]')).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId('car-eq-name')).toHaveText('EQ CUSTOM');
    await key.click();
    await expect(page.getByTestId('car-eq-name')).toHaveText('EQ FLAT');
    expect(errors).toEqual([]);
  });

  test('hifi: the record turns while playing and the tonearm follows the song; the record seeks, the pitch slider sets the speed, the crate flips and plays', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page);
    await playInScene(page, 'hifi');
    const rec = page.getByTestId('hifi-record');
    await freshSong(page);
    await expect.poll(() => turns(rec)).toBe(true);
    // the tonearm is on the record while it plays
    const arm = () => page.getByTestId('hifi-arm').evaluate((a) => parseFloat(getComputedStyle(a).getPropertyValue('--arm')) || 0);
    await expect.poll(arm).toBeGreaterThan(10);   // (on the record, past the lead-in; the mock's songs last 3 s, so how far in changes fast)
    // paused: the record stops, the arm lifts back to its rest
    await page.locator('[data-testid=retro] [data-ctl=play]').first().click();
    await expect.poll(() => turns(rec)).toBe(false);
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

  test('hifi: BASS and TREBLE are the real EQ bands and VOLUME the real volume: knobs that show them, turn by keys, scroll and drag', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page);
    await page.addInitScript(() => { try { localStorage.setItem('flacie.eq', '4,0,0,0,-3'); } catch { } });
    await playInScene(page, 'hifi');
    const bass = page.getByTestId('hifi-bass'), treble = page.getByTestId('hifi-treble'), vol = page.getByTestId('hifi-volume');
    const eq = () => page.evaluate(() => (window as any).flacie.eqGet());
    // they show the EQ as it is set
    await expect(bass).toHaveAttribute('aria-valuenow', '4'); await expect(treble).toHaveAttribute('aria-valuenow', '-3');
    await expect(bass).toHaveAttribute('aria-valuetext', '+4 dB');
    const rot = (l: any) => l.evaluate((e: HTMLElement) => parseFloat(e.style.getPropertyValue('--r')));
    expect(await rot(bass)).toBeGreaterThan(await rot(treble));
    // keys: a decibel a press; the music's own bands move
    await bass.focus(); await page.keyboard.press('ArrowUp');
    await expect.poll(async () => (await eq())[0]).toBe(5);
    await expect(bass).toHaveAttribute('aria-valuenow', '5');
    await expect.poll(() => page.evaluate(() => (window as any).flacie.toneLevel().low)).toBeGreaterThan(4);
    // scroll turns the treble down, a drag up turns it up (12 px a step)
    await treble.hover(); await page.mouse.wheel(0, 120);
    await expect.poll(async () => (await eq())[4]).toBe(-4);
    const b = (await treble.boundingBox())!;
    await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2); await page.mouse.down();
    await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2 - 40, { steps: 6 }); await page.mouse.up();
    await expect.poll(async () => (await eq())[4]).toBeGreaterThanOrEqual(-1);
    // the middle bands are left as they were
    expect((await eq()).slice(1, 4)).toEqual([0, 0, 0]);
    // volume
    const real = () => page.evaluate(() => Math.round((window as any).flacie.volume() * 100));
    await expect(vol).toHaveAttribute('aria-valuenow', String(await real()));
    const before = await real();
    await vol.focus(); await page.keyboard.press('ArrowDown');
    await expect.poll(real).toBe(before - 5);
    await expect(vol).toHaveAttribute('aria-valuenow', String(before - 5));
    expect(errors).toEqual([]);
  });

  test('boombox: deck A turns while playing and its counter runs; stop/eject pauses and opens the door', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page);
    await playInScene(page, 'boombox');
    await freshSong(page);
    const reels = page.locator('[data-testid=boombox-deck-a] .reel');
    const turning = () => turns(reels.first());
    await expect.poll(turning).toBe(true);
    await expect(page.getByTestId('boombox-counter')).toHaveText(/^\d{3}$/);
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

  test('boombox: the five EQ sliders are the real EQ: big enough to see, they show its bands as set, and a drag or a key sets one; VOL is the real volume', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page);
    // an EQ kept from last time: the bass up, the treble down
    await page.addInitScript(() => { try { localStorage.setItem('flacie.eq', '6,0,0,0,-6'); } catch { } });
    await playInScene(page, 'boombox');
    const faders = page.locator('[data-testid=retro] .bb-eq input[data-band]');
    await expect(faders).toHaveCount(5);
    expect(await page.locator('[data-testid=retro] .bb-eq small').allTextContents()).toEqual(['100', '400', '1k', '3k', '8k', 'VOL']);
    // (they were 3 by 4 px specks: app.css's own .eq, the playing song's little bars, sized them)
    const caps = page.locator('[data-testid=retro] .bb-eq .bb-cap');
    for (let i = 0; i < 5; i++) {
      const cap = (await caps.nth(i).boundingBox())!, f = (await faders.nth(i).boundingBox())!;
      expect(cap.width, `cap ${i} wide enough`).toBeGreaterThan(14); expect(cap.height, `cap ${i} tall enough`).toBeGreaterThan(5);
      expect(f.height, `slider ${i} long enough to drag`).toBeGreaterThan(45);
    }
    // they show the EQ as it is set (a real graphic EQ's sliders show the setting, not the music)
    await expect(faders.nth(0)).toHaveValue('6'); await expect(faders.nth(2)).toHaveValue('0'); await expect(faders.nth(4)).toHaveValue('-6');
    const capY = async (i: number) => (await caps.nth(i).boundingBox())!.y;
    expect(await capY(0)).toBeLessThan(await capY(2)); expect(await capY(2)).toBeLessThan(await capY(4));
    // a drag on the 1 kHz slider, from the middle to near its top, lifts that band of the music
    const f = (await faders.nth(2).boundingBox())!;
    await page.mouse.move(f.x + f.width / 2, f.y + f.height / 2); await page.mouse.down();
    await page.mouse.move(f.x + f.width / 2, f.y + f.height * 0.08, { steps: 8 }); await page.mouse.up();
    await expect.poll(() => page.evaluate(() => (window as any).flacie.eqGet()[2])).toBeGreaterThan(6);
    await expect.poll(() => page.evaluate(() => (window as any).flacie.toneLevel().bands[2])).toBeGreaterThan(5);
    await expect.poll(() => capY(2)).toBeLessThan(await capY(0));
    // the keys move one by a decibel
    await faders.nth(4).focus(); await page.keyboard.press('ArrowUp');
    await expect.poll(() => page.evaluate(() => (window as any).flacie.eqGet()[4])).toBe(-5);
    await expect(faders.nth(4)).toHaveAttribute('aria-valuetext', '-5 dB');
    // VOL: the player's volume
    const vol = page.locator('[data-testid=retro] .bb-eq input[data-ctl=volume]');
    const real = () => page.evaluate(() => Math.round((window as any).flacie.volume() * 100));
    await expect(vol).toHaveValue(String(await real()));
    const before = await real();
    await vol.focus(); await page.keyboard.press('ArrowDown');
    await expect.poll(real).toBe(before - 5);
    await expect(vol).toHaveValue(String(before - 5));
    expect(errors).toEqual([]);
  });

  test('minidisc: the EQ button steps the real EQ and the LCD names the sound; VOL - and + are the real volume and the LCD shows it', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page);
    await playInScene(page, 'minidisc');
    const eq = () => page.evaluate(() => (window as any).flacie.eqGet());
    await expect(page.getByTestId('md-eq-name')).toHaveText('EQ FLAT');
    await page.locator('[data-testid=retro] [data-ctl=eq]').click();
    await expect(page.getByTestId('md-eq-name')).toHaveText('EQ ROCK');
    await expect.poll(eq).toEqual([5, 2, -2, 2, 5]);
    await page.locator('[data-testid=retro] [data-ctl=eq]').click();
    await expect(page.getByTestId('md-eq-name')).toHaveText('EQ POP');
    await expect.poll(eq).toEqual([-1, 2, 4, 2, -1]);
    const real = () => page.evaluate(() => Math.round((window as any).flacie.volume() * 100));
    const lcd = async () => Number(((await page.getByTestId('md-vol').textContent()) ?? '').replace(/\D/g, ''));
    const start = await real();
    expect(await lcd()).toBe(Math.round(start / 5));
    await page.locator('[data-testid=retro] [data-ctl=vol-down]').click();
    await expect.poll(real).toBe(start - 5);
    await expect.poll(lcd).toBe(Math.round((start - 5) / 5));
    await page.locator('[data-testid=retro] [data-ctl=vol-up]').click();
    await page.locator('[data-testid=retro] [data-ctl=vol-up]').click();
    await expect.poll(real).toBe(Math.min(100, start + 5));
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
