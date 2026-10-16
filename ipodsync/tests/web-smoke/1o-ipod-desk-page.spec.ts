import { test, expect, Page } from '@playwright/test';

// The iPod desk on the real /ipod page (Look › Desk), against the mock Jellyfin: the emulator lying on the desk still works exactly as on the stage,
// the journal, the CDs and the note follow the song, every iPod family fits its place, and the desk holds still when motion is reduced.
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
/** Opens /ipod with these prefs (seeded before the page loads; not again on a reload: the page must show what it saved itself). */
async function openDesk(page: Page, prefs: Record<string, unknown>) {
  await page.addInitScript((p) => { try { const nav = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined; if (nav?.type !== 'reload') localStorage.setItem('flacie.ipod', JSON.stringify(p)); } catch { } }, prefs);
  await page.goto('/ipod');
  await expect(page.getByTestId('ipod')).toHaveAttribute('data-model', String(prefs.model), { timeout: 20_000 });
  await page.waitForTimeout(700);
}
const consoleErrors = (page: Page) => { const e: string[] = []; page.on('console', (m) => { if (m.type() === 'error') e.push(m.text()); }); page.on('pageerror', (x) => e.push(String(x))); return e; };
/** Starts playing from the iPod: the last main-menu row is "Shuffle Songs" on the click-wheel iPods. */
async function shuffle(page: Page) {
  await page.getByTestId('ipod-keys').focus();
  for (let i = 0; i < 10; i++) await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('ipod-np')).toBeVisible({ timeout: 20_000 });
}
async function dragWheel(page: Page, degrees: number) {
  const b = (await page.getByTestId('ipod-wheel').boundingBox())!;
  const cx = b.x + b.width / 2, cy = b.y + b.height / 2, r = b.width * 0.42;
  const at = (a: number) => [cx + r * Math.cos((a * Math.PI) / 180), cy + r * Math.sin((a * Math.PI) / 180)] as const;
  const [sx, sy] = at(-30); await page.mouse.move(sx, sy); await page.mouse.down();
  const steps = Math.max(4, Math.abs(degrees) / 5);
  for (let i = 1; i <= steps; i++) { const [x, y] = at(-30 + (degrees * i) / steps); await page.mouse.move(x, y); await page.waitForTimeout(12); }
  await page.waitForTimeout(80); await page.mouse.up(); await page.waitForTimeout(250);
}
type Box = { x: number; y: number; width: number; height: number };
const inside = (a: Box, b: Box, slack = 2) => a.x >= b.x - slack && a.y >= b.y - slack && a.x + a.width <= b.x + b.width + slack && a.y + a.height <= b.y + b.height + slack;
const selIndex = (page: Page) => page.locator('[data-testid=ipod-row]').evaluateAll((rs) => rs.findIndex((r) => r.classList.contains('sel')));

test.describe.serial('desk look on /ipod', () => {
  test('desk renders with the 1m test ids, the Desk chip on', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await expect(page.getByTestId('desk')).toBeVisible();
    for (const id of ['ipod-keys', 'ipod-device', 'ipod-panel', 'ipod-gear', 'desk-journal', 'desk-polaroid', 'desk-coffee', 'desk-earbuds', 'desk-queue']) await expect(page.getByTestId(id)).toBeVisible();
    await expect(page.getByTestId('ipod')).toHaveAttribute('data-look', 'desk');
    await expect(page.locator('.ipod-panel .ipod-chip[data-look="desk"]')).toHaveClass(/on/);
    expect(await page.evaluate(() => (window as any).desk.loops())).toBe(1);
    expect(errors).toEqual([]);
  });

  test('keys and zones still drive the iPod; the journal and the CDs follow the song', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await shuffle(page);
    const song = (await page.getByTestId('ipod-np-title').textContent())!.trim();
    await expect(page.getByTestId('desk-journal').locator('.j-title')).toContainText(song);
    await expect(page.getByTestId('desk-open-cd')).toBeVisible();
    await expect(page.locator('[data-q=next]').first()).toBeVisible();
    // the disc gets its print, made from the cover in the browser (it never travels to the server: it is bigger than a Blazor message)
    await expect.poll(() => page.getByTestId('desk-disc').evaluate((d) => getComputedStyle(d).getPropertyValue('--art')), { timeout: 10_000 }).toContain('data:image/png');
    // the wheel's next zone skips the song
    const b = (await page.getByTestId('ipod-wheel').boundingBox())!;
    await page.mouse.click(b.x + b.width / 2 + b.width * 0.42, b.y + b.height / 2); await page.waitForTimeout(500);
    await expect(page.getByTestId('ipod-np-title')).not.toHaveText(song, { timeout: 10_000 });
    // the open CD is the album playing now (its disc in the tray); a click on it pauses and plays
    const album = (await page.locator('.lnp-album').textContent())!.trim();
    expect(await page.getByTestId('desk-open-cd').getAttribute('aria-label')).toMatch(new RegExp(`^Now playing: ${album.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')} by `));
    await page.getByTestId('desk-open-cd').click();
    await expect(page.getByTestId('desk-disc')).toHaveClass(/paused/);
    await page.getByTestId('desk-open-cd').click();
    await expect(page.getByTestId('desk-disc')).not.toHaveClass(/paused/);
    // a CD from the stack plays that album: it becomes the one playing
    const stacked = (await page.getByTestId('desk-case').last().getAttribute('aria-label'))!.replace(/^Play /, '').replace(/ by .*$/, '');
    await page.getByTestId('desk-case').last().click();
    await expect(page.locator('.lnp-album')).toHaveText(stacked, { timeout: 10_000 });
    // Escape leaves Now Playing
    await page.getByTestId('ipod-keys').focus(); await page.keyboard.press('Escape');
    await expect(page.getByTestId('ipod-np')).toHaveCount(0);
    expect(errors).toEqual([]);
  });

  test('wheel drag scrolls the same in the desk look as on the stage', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'ipod4', look: 1 });
    const a0 = await selIndex(page); await dragWheel(page, 120); const onStage = (await selIndex(page)) - a0;
    await page.locator('.ipod-panel .ipod-chip[data-look="desk"]').click(); await expect(page.getByTestId('desk')).toBeVisible(); await page.waitForTimeout(700);
    const b0 = await selIndex(page); await dragWheel(page, 120); const onDesk = (await selIndex(page)) - b0;
    expect(onStage).toBeGreaterThan(0);
    expect(onDesk).toBe(onStage);
  });

  test('opening iPod mode while a song is playing starts on Now Playing (a wheel iPod and a touch iPod)', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await page.goto('/songs');
    await page.locator('.track', { has: page.locator('.track-title', { hasText: /^Velvet 1$|^Paper 2$|^Lantern \d+$/ }) }).first().locator('button.track-main').click();
    await expect(page.locator('.player')).toBeVisible();
    // the player keeps playing across the move to /ipod (the same page connection)
    await page.evaluate(() => (window as any).Blazor.navigateTo('/ipod'));   // (what the menu's iPod mode link does: the same page connection, the song still playing)
    await expect(page.getByTestId('ipod')).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId('ipod-np')).toBeVisible({ timeout: 10_000 });
    // a touch iPod: its Now Playing app
    await page.locator('.ipod-panel .dock-seg [data-group]').nth(3).click();
    await page.locator('.ipod-panel .ipod-chip[data-model="touch5"]').click();
    await page.goto('/songs');
    await page.locator('.track', { has: page.locator('.track-title', { hasText: /^Velvet 1$|^Paper 2$|^Lantern \d+$/ }) }).first().locator('button.track-main').click();
    await page.evaluate(() => (window as any).Blazor.navigateTo('/ipod'));   // (what the menu's iPod mode link does: the same page connection, the song still playing)
    await expect(page.getByTestId('ios')).toHaveAttribute('data-app', 'now', { timeout: 10_000 });
  });

  test('the queue card: the song playing (title, artist, album, year), the next songs in play order, and a click on one plays it', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await shuffle(page);
    const card = page.getByTestId('desk-queue');
    await expect(card.locator('[data-q=title]')).toHaveText((await page.getByTestId('ipod-np-title').textContent())!.trim());
    await expect(card.locator('[data-q=meta]')).toContainText((await page.locator('.lnp-album').textContent())!.trim());
    await expect(card.locator('[data-q=meta]')).toHaveText(/\b(19|20)\d\d\b/);   // the year
    await expect(page.locator('[data-q=next]')).toHaveCount(4);
    await expect(card).toContainText(/more in the queue/);
    // the first one is what plays next
    const first = (await page.locator('[data-q=next] .t').first().textContent())!.trim();
    await page.getByTestId('ipod-keys').focus(); await page.keyboard.press('ArrowRight');
    await expect(page.getByTestId('ipod-np-title')).toHaveText(first, { timeout: 5_000 });
    // a click on a later one plays it
    const third = (await page.locator('[data-q=next] .t').nth(2).textContent())!.trim();
    await page.locator('[data-q=next]').nth(2).click();
    await expect(page.getByTestId('ipod-np-title')).toHaveText(third, { timeout: 5_000 });
    expect(errors).toEqual([]);
  });

  test('the tilted iPod\'s wheel keeps its own zones: a tap just outside the middle button is the ring, just inside it is the button', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    const title = page.getByTestId('ipod-title'), home = (await title.textContent())!.trim(), row = await selIndex(page);
    // the wheel lies turned (the desk's tilt): its box on screen is bigger than the wheel by |cos| + |sin|; the device's own right is turned as much from the screen's
    const tilt = await page.locator('.desk-ipod').evaluate((e) => parseFloat(getComputedStyle(e).rotate) || 0);
    expect(Math.abs(tilt)).toBeGreaterThan(1);
    const b = (await page.getByTestId('ipod-wheel').boundingBox())!, t = (tilt * Math.PI) / 180;
    const R = b.width / 2 / (Math.cos(t) + Math.abs(Math.sin(t)));   // the middle button ends at 0.36 of the wheel
    const tap = async (d: number) => { await page.mouse.click(b.x + b.width / 2 + d * R * Math.cos(t), b.y + b.height / 2 + d * R * Math.sin(t)); await page.waitForTimeout(500); };
    await tap(0.39);                                       // the ring's next zone: a menu stays where it is
    await expect(title).toHaveText(home); expect(await selIndex(page)).toBe(row);
    await tap(0.3);                                        // the middle button: the selected row opens
    await expect(title).not.toHaveText(home);
  });

  test('a mouse drags the tilted touch iPod\'s list as far as the pointer goes (along the iPod)', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'touch5', look: 2 });
    await page.locator('.ios-grid .ios-app[data-app="music"]').click();
    const list = page.getByTestId('ios-list'); await expect(page.getByTestId('ios-row').first()).toBeVisible();
    const lb = (await list.boundingBox())!, zoom = await page.locator('.ipod-fit').evaluate((e) => parseFloat(getComputedStyle(e).zoom) || 1);
    const x = lb.x + lb.width / 2, y = lb.y + lb.height * 0.8, D = 200;
    await page.mouse.move(x, y); await page.mouse.down(); await page.mouse.move(x, y - 40, { steps: 4 }); await page.mouse.move(x, y - D, { steps: 10 }); await page.mouse.up();
    // the iPod lies turned (the desk's tilt): a drag straight up the screen moves along the iPod by D cos(tilt), and the list moves that far in its own (zoomed) pixels
    const tilt = await page.locator('.desk-ipod').evaluate((e) => parseFloat(getComputedStyle(e).rotate) || 0);
    const want = (D * Math.cos((tilt * Math.PI) / 180)) / zoom;
    await expect.poll(() => list.evaluate((e) => e.scrollTop)).toBeGreaterThan(want * 0.96);
    expect(await list.evaluate((e) => e.scrollTop)).toBeLessThan(want * 1.04);
  });

  test('the CDs are keys too: Tab to one, Enter plays it', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await shuffle(page);
    const cd = page.getByTestId('desk-case').last();
    await expect(cd).toHaveAttribute('tabindex', '0'); await expect(cd).toHaveAttribute('role', 'button');
    await expect(cd).toHaveAttribute('aria-label', /^Play .+ by .+/);
    const before = (await page.locator('[data-q=next] .t').first().textContent())!.trim();
    const screen = await page.getByTestId('ipod-np-title').textContent();
    await cd.focus(); await page.keyboard.press('Enter');
    await expect(page.locator('[data-q=next] .t').first()).not.toHaveText(before, { timeout: 10_000 });
    await expect(page.getByTestId('ipod-np')).toBeVisible();   // the iPod did not take the Enter as its own (still Now Playing)
    expect(await page.getByTestId('ipod-np-title').textContent()).not.toBe(screen);
    expect(errors).toEqual([]);
  });

  test('the journal and the polaroid keep the viewer\'s own day, and the lyrics are written in as they are sung', async ({ page }) => {
    await page.clock.setFixedTime(new Date('2031-02-03T10:00:00'));   // a Monday, not the server's day
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await expect(page.getByTestId('desk-date')).toHaveText('3 feb', { timeout: 10_000 });
    await expect(page.locator('.desk-polaroid > span')).toHaveText('monday ♡');
    await shuffle(page);
    await expect(page.getByTestId('desk-lyric').first()).toContainText('mock lyric', { timeout: 15_000 });
    const pageBox = (await page.locator('.desk-journal .page').boundingBox())!, sung = (await page.locator('[data-testid=desk-lyric].writing').boundingBox())!;
    expect(sung.y + sung.height).toBeLessThanOrEqual(pageBox.y + pageBox.height + 1);
  });

  test('the disc picture comes from FLACie only, for a release someone matched, and only for someone signed in', async ({ page, request }) => {
    await signIn(page);
    const res = await page.request.get('/desk/disc/6b3f1d2e-0000-4000-8000-000000000001');
    expect(res.status()).toBe(404);   // nobody matched this release
    expect((await page.request.get('/desk/disc/not-an-id')).status()).toBe(404);
    const anon = await request.get('/desk/disc/6b3f1d2e-0000-4000-8000-000000000001', { maxRedirects: 0 });
    expect([401, 302]).toContain(anon.status());
  });

  test('an empty desk: nothing playing, nothing queued, no CD, no errors', async ({ browser }) => {
    const ctx = await browser.newContext(); const page = await ctx.newPage();
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await expect(page.getByTestId('desk-journal').locator('.j-title')).toHaveText('nothing playing');
    await expect(page.getByTestId('desk-queue')).toContainText('nothing queued');
    await expect(page.locator('[data-q=next]')).toHaveCount(0);
    await expect(page.getByTestId('desk-open-cd')).toHaveCount(0);
    await expect(page.getByTestId('desk-lyric')).toHaveCount(0);
    expect(errors).toEqual([]);
    await ctx.close();
  });

  test('the earbud cable has no kinks and runs straight into the splitter (top and bottom jacks, desk and phone)', async ({ page }) => {
    await signIn(page);
    for (const [w, h] of [[1600, 900], [390, 844]]) {
      await page.setViewportSize({ width: w, height: h });
      for (const model of ['classic6', 'nano3']) {
        await openDesk(page, { model, look: 2 }); await page.waitForTimeout(600);
        const st = await page.evaluate(() => {
          const p = document.querySelector('#desk-cable') as SVGPathElement, L = p.getTotalLength();
          let worst = 0, prev: number | null = null;
          for (let s = 0; s + 3 <= L; s += 3) {
            const a = p.getPointAtLength(s), b = p.getPointAtLength(s + 3), ang = Math.atan2(b.y - a.y, b.x - a.x) * 180 / Math.PI;
            if (prev !== null) { let d = Math.abs(ang - prev); if (d > 180) d = 360 - d; worst = Math.max(worst, d); }
            prev = ang;
          }
          const a = p.getPointAtLength(L - 6), b = p.getPointAtLength(L), end = Math.atan2(b.y - a.y, b.x - a.x) * 180 / Math.PI;
          // the splitter: a rounded bar turned so its long side runs along the cable (rotate(r) turns its up-down axis to r + 90 degrees)
          const g = document.querySelector('[data-splitter]')!.getAttribute('transform')!, r = Number(/rotate\((-?[\d.]+)\)/.exec(g)![1]);
          let off = Math.abs(end - (r + 90)); if (off > 180) off = 360 - off;
          // the two leads out of the splitter: no hairpins either
          let leads = 0;
          for (const id of ['#desk-lead-l', '#desk-lead-r']) {
            const q = document.querySelector(id) as SVGPathElement, M = q.getTotalLength(); let pa: number | null = null;
            for (let s = 0; s + 3 <= M; s += 3) {
              const a1 = q.getPointAtLength(s), b1 = q.getPointAtLength(s + 3), ang = Math.atan2(b1.y - a1.y, b1.x - a1.x) * 180 / Math.PI;
              if (pa !== null) { let d = Math.abs(ang - pa); if (d > 180) d = 360 - d; leads = Math.max(leads, d); }
              pa = ang;
            }
          }
          return { worst, off, leads };
        });
        expect(st.worst, `${model} at ${w}x${h}: sharpest turn over 3 units of cable`).toBeLessThan(25);
        expect(st.leads, `${model} at ${w}x${h}: sharpest turn over 3 units of the leads`).toBeLessThan(25);
        expect(st.off, `${model} at ${w}x${h}: the cable meets the splitter along it`).toBeLessThan(12);
        await page.evaluate(() => localStorage.removeItem('flacie.ipod'));
      }
    }
  });

  test('the Desk options: the props come and go, studio headphones swap the earbuds, no dock, kept across a reload', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    const opt = (k: string) => page.locator(`[data-testid=desk-options] [data-desk-opt="${k}"]`);
    await expect(page.getByTestId('desk-options')).toBeVisible();
    // a new desk: earbuds, the pencil on the notebook, the VU amp, the cassette and the MiniDiscs; the iPod lies on the desk itself (no dock, no option for one)
    await expect(page.locator('[data-desk-phones="0"]')).toHaveClass(/on/);
    for (const id of ['desk-vu', 'desk-cassette', 'desk-minidiscs', 'desk-pencil']) await expect(page.getByTestId(id), id).toBeVisible();
    await expect(page.getByTestId('desk-dock')).toHaveCount(0); await expect(opt('dock')).toHaveCount(0);
    // the others off and on again; the VU meter left off
    for (const [k, id] of [['md', 'desk-minidiscs'], ['pencil', 'desk-pencil'], ['tape', 'desk-cassette']]) {
      await opt(k).click(); await expect(page.getByTestId(id)).toHaveCount(0); await expect(opt(k)).not.toHaveClass(/on/);
      await opt(k).click(); await expect(page.getByTestId(id)).toBeVisible();
    }
    await opt('vu').click(); await expect(page.getByTestId('desk-vu')).toHaveCount(0);
    // studio headphones: a coiled cable to a gold adapter, no earbuds
    await page.locator('[data-desk-phones="1"]').click();
    await expect(page.getByTestId('desk-adapter')).toBeVisible();
    await expect(page.locator('[data-bud]')).toHaveCount(0);
    // kept across a reload
    await page.reload(); await expect(page.getByTestId('desk')).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId('desk-adapter')).toBeVisible(); await expect(page.getByTestId('desk-vu')).toHaveCount(0);
    // the options belong to the desk look
    await page.locator('.ipod-panel .ipod-chip[data-look="device"]').click();
    await expect(page.getByTestId('desk-options')).toHaveCount(0);
    expect(errors).toEqual([]);
  });

  test('the cassette and the MiniDiscs are your playlists: their labels, and a click plays them', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    const stamp = Date.now().toString(36), tape = `Tape ${stamp}`, disc = `Disc ${stamp}`;
    // two playlists with a song each (made in the app: Playlists page, then Now Playing › More › Add to playlist)
    const songs: Record<string, string> = {};
    for (const [name, nth] of [[disc, 0], [tape, 1]] as const) {   // (the first two songs of the list: the list only draws the rows in view)
      await page.goto('/playlists');
      // (typed again until the page is live: a name typed before it connects never reaches the server, and Create stays off)
      await expect(async () => { await page.getByLabel('New playlist name').fill(''); await page.getByLabel('New playlist name').fill(name); await expect(page.getByRole('button', { name: 'Create' })).toBeEnabled({ timeout: 1000 }); }).toPass({ timeout: 20_000 });
      await page.getByRole('button', { name: 'Create' }).click();
      await page.waitForURL(/\/playlist\//);
      await page.goto('/songs');
      // a song whose title is not the other playlist's (the mock library has songs of the same name on different albums)
      let row = page.locator('.track').nth(nth);
      for (let i = nth; Object.values(songs).includes((await row.locator('.track-title').textContent())!.trim()); i++) row = page.locator('.track').nth(i + 1);
      songs[name] = (await row.locator('.track-title').textContent())!.trim();
      await row.locator('button.track-main').click();
      await page.getByRole('button', { name: 'Open Now Playing' }).click();
      await expect(page.locator('.np-titles h2')).toHaveText(songs[name]);   // (Now Playing on this song before its menu adds it)
      await page.getByRole('button', { name: 'More options' }).first().click();
      await page.getByRole('menuitem', { name: /Add to playlist/ }).click();
      await page.getByRole('menuitem', { name }).click();
    }
    await page.goto('/songs');
    await page.locator('.track', { has: page.locator('.track-title', { hasText: /^Lantern \d+$/ }) }).first().locator('button.track-main').click();
    await page.evaluate(() => localStorage.setItem('flacie.ipod', JSON.stringify({ model: 'classic6', look: 2, desk: { md: true } })));   // (read when iPod mode opens)
    await page.evaluate(() => (window as any).Blazor.navigateTo('/ipod'));
    await expect(page.getByTestId('desk')).toBeVisible({ timeout: 20_000 });
    // the newest playlist is the cassette, the others the MiniDiscs
    await expect(page.getByTestId('desk-cassette')).toContainText(tape);
    await expect(page.getByTestId('desk-minidiscs')).toContainText(disc);
    // (the mock's songs last 3 s and a one-song playlist goes on to autoplay: watch the title from the click on)
    const watch = () => page.evaluate(() => { (window as any).seen = []; (window as any).seenTimer = setInterval(() => { const t = document.querySelector('[data-testid=ipod-np-title]')?.textContent?.trim(); if (t) (window as any).seen.push(t); }, 50); });
    const saw = (title: string) => expect.poll(() => page.evaluate(() => (window as any).seen as string[]), { timeout: 8_000 }).toContain(title);
    await watch(); await page.getByTestId('desk-cassette').click();
    await saw(songs[tape]);
    await expect(page.locator('.desk-cassette .reel').first()).not.toHaveClass(/paused/);
    await watch(); await page.locator('[data-testid=desk-minidiscs] [data-md]').filter({ hasText: disc }).click();
    await saw(songs[disc]);
  });

  test('the VU meter\'s needle is driven by the desk (the mock songs are silent: its swing is checked in the desk.js harness)', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await expect.poll(() => page.locator('[data-needle]').evaluate((n) => (n as HTMLElement).style.getPropertyValue('--a'))).toMatch(/^-?\d/);
  });

  test('every family sits on the desk', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'ipod1', look: 2 });
    for (const [group, model] of [['classic', 'ipod1'], ['mini', 'mini'], ['classic', 'video5'], ['nano', 'nano3'], ['classic', 'classic6'], ['touch', 'touch5']]) {
      await page.locator(`.ipod-panel .dock-seg [data-group]`).nth(['classic', 'mini', 'nano', 'touch'].indexOf(group)).click();
      await page.locator(`.ipod-panel .ipod-chip[data-model="${model}"]`).click();
      await expect(page.getByTestId('ipod')).toHaveAttribute('data-model', model); await page.waitForTimeout(700);
      const dev = (await page.getByTestId('ipod-device').boundingBox())!, slot = (await page.locator('.desk-ipod').boundingBox())!;
      expect(inside(dev, slot, 24), `${model} inside its place`).toBe(true);
      expect(dev.height, `${model} fills its place`).toBeGreaterThan(slot.height * 0.6);
      expect(inside(dev, { x: 0, y: 0, width: 1600, height: 900 }), `${model} on screen`).toBe(true);
    }
    expect(errors).toEqual([]);
  });

  test('fits at desktop sizes, and a portrait phone keeps the iPod big', async ({ page }) => {
    await signIn(page);
    await page.setViewportSize({ width: 1280, height: 720 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await shuffle(page);   // music on: the CD is on the desk
    for (const [w, h] of [[1280, 720], [1920, 1080], [2560, 1080], [390, 844]]) {
      await page.setViewportSize({ width: w, height: h }); await page.waitForTimeout(900);
      const deskBox = (await page.getByTestId('desk').boundingBox())!;
      // a phone frames a closer look at the same desk: the journal and the coffee may run off its edges (their size stays the desk's), but the journal's
      // title, the queue card and the iPod are in view, and the coffee is at least partly in the frame
      const whole = w < h ? ['desk-queue', 'ipod-device'] : ['desk-journal', 'desk-queue', 'ipod-device'];
      for (const id of whole) {
        const b = await page.getByTestId(id).boundingBox(); if (!b) continue;
        expect(inside(b, deskBox, 30), `${id} at ${w}x${h}`).toBe(true);
      }
      if (w < h) {
        expect(inside((await page.getByTestId('desk-journal').locator('.j-title').boundingBox())!, deskBox, 30), `journal title at ${w}x${h}`).toBe(true);
        // (on a phone the queue card takes the open CD's corner: what plays and what is next are on it)
        expect(inside((await page.getByTestId('desk-queue').boundingBox())!, deskBox, 30), `queue card at ${w}x${h}`).toBe(true);
        for (const sel of ['.desk-coffee .saucer']) {
          const b = (await page.locator(sel).boundingBox())!;
          expect(b.x < deskBox.x + deskBox.width && b.y < deskBox.y + deskBox.height && b.x + b.width > deskBox.x, `${sel} in the frame at ${w}x${h}`).toBe(true);
        }
        // the coffee keeps its size beside the iPod (about half the iPod's height, as on the desk)
        const cup = (await page.locator('.desk-coffee .saucer').boundingBox())!, dev = (await page.getByTestId('ipod-device').boundingBox())!;
        expect(cup.height / dev.height, `coffee to iPod at ${w}x${h}`).toBeGreaterThan(0.45);
      }
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${w}x${h} scrolls sideways`).toBe(true);
      if (w < h) expect(((await page.getByTestId('ipod-device').boundingBox())!.height) / h, 'portrait iPod size').toBeGreaterThan(0.45);
    }
  });

  test('remembers the desk look, and leaves it', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 1 });
    await page.locator('.ipod-panel .ipod-chip[data-look="desk"]').click();
    await expect(page.getByTestId('desk')).toBeVisible();
    await page.reload(); await expect(page.getByTestId('desk')).toBeVisible({ timeout: 20_000 });
    await page.locator('.ipod-panel .ipod-chip[data-look="device"]').click();
    await expect(page.getByTestId('desk')).toHaveCount(0);
    await expect(page.getByTestId('ipod')).toHaveAttribute('data-look', 'device');
  });

  test('reduced motion holds the desk still', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await page.waitForTimeout(800);
    const running = await page.evaluate(() => document.querySelector('.desk')!.getAnimations({ subtree: true })
      .filter((a) => a.playState === 'running' && !((a.effect as KeyframeEffect).target as Element).closest('.ipod')).length);
    expect(running).toBe(0);
  });
});
