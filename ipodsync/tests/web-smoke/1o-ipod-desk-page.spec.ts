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
/** The first colour in a CSS colour or gradient, as [r, g, b] (a color-mix() is given as color(srgb r g b), each 0 to 1). */
const rgb3 = (s: string) => {
  const m = s.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/), k = s.match(/color\(srgb ([\d.]+) ([\d.]+) ([\d.]+)/);
  const at = (x: RegExpMatchArray | null) => x ? x.index! : Infinity;
  return at(m) < at(k) ? [+m![1], +m![2], +m![3]] : k ? [1, 2, 3].map((i) => Math.round(+k[i] * 255)) : null;
};
const selIndex = (page: Page) => page.locator('[data-testid=ipod-row]').evaluateAll((rs) => rs.findIndex((r) => r.classList.contains('sel')));

test.describe.serial('desk look on /ipod', () => {
  test('desk renders with the 1m test ids, the Desk chip on', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await expect(page.getByTestId('desk')).toBeVisible();
    for (const id of ['ipod-keys', 'ipod-device', 'ipod-panel', 'ipod-gear', 'desk-journal', 'desk-np', 'desk-coffee', 'desk-earbuds', 'desk-rack']) await expect(page.getByTestId(id)).toBeVisible();
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
    await expect(page.getByTestId('desk-rack')).toBeVisible();
    await expect(page.getByTestId('desk-pop')).toBeVisible();
    await expect(page.getByTestId('desk-queue')).toHaveCount(0);   // (the CDs say what is next: no separate card)
    // the disc gets its print, made from the cover in the browser (it never travels to the server: it is bigger than a Blazor message)
    await expect.poll(() => page.getByTestId('desk-disc').evaluate((d) => getComputedStyle(d).getPropertyValue('--art')), { timeout: 10_000 }).toContain('data:image/png');
    // the open CD lies apart from the rack and the CD popped out of it
    const open = (await page.getByTestId('desk-open-cd').boundingBox())!, rack = (await page.getByTestId('desk-rack').boundingBox())!;
    expect(open.y + open.height, 'the open CD above the rack').toBeLessThanOrEqual(rack.y + 4);
    // the wheel's next zone skips the song
    const b = (await page.getByTestId('ipod-wheel').boundingBox())!;
    await page.mouse.click(b.x + b.width / 2 + b.width * 0.42, b.y + b.height / 2); await page.waitForTimeout(500);
    await expect(page.getByTestId('ipod-np-title')).not.toHaveText(song, { timeout: 10_000 });
    // the open CD is the album playing now (its disc in the tray); a click on it pauses and plays
    const album = (await page.locator('.lnp-album').textContent())!.trim();
    expect((await page.getByTestId('desk-open-cd').getAttribute('aria-label'))!.startsWith(`Now playing: ${album} by `)).toBe(true);
    await page.getByTestId('desk-open-cd').click();
    await expect(page.getByTestId('desk-disc')).toHaveClass(/paused/);
    await page.getByTestId('desk-open-cd').click();
    await expect(page.getByTestId('desk-disc')).not.toHaveClass(/paused/);
    // a CD from the rack plays that album: it becomes the one playing
    const spine = page.getByTestId('desk-spine').last(), stacked = (await spine.getAttribute('data-album'))!;
    await spine.click();
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

  test('the CD rack is what comes next: the next album pops out flat and bigger; hover, scroll or arrow keys pick another, a click plays it', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await shuffle(page);
    await page.getByTestId('desk-open-cd').click();   // (paused: the mock's songs last 3 s, and what is next would move on meanwhile)
    await expect(page.getByTestId('desk-disc')).toHaveClass(/paused/);
    const rack = page.getByTestId('desk-rack'), spines = rack.getByTestId('desk-spine'), pop = page.getByTestId('desk-pop');
    expect(await spines.count()).toBeGreaterThanOrEqual(3);
    expect(await spines.count()).toBeLessThanOrEqual(8);
    // at rest the selection is the next album: it pops out of the rack, flat, its cover showing, bigger than its spine, saying it is next
    await expect(rack).toHaveAttribute('data-sel', '0');
    await expect(spines.first()).toHaveClass(/\bsel\b/);
    await expect(pop).toHaveAttribute('data-album', (await spines.first().getAttribute('data-album'))!);
    await expect(pop.locator('[data-pop=label]')).toHaveText(/up next/i);
    await expect(pop.locator('img, .cover').first()).toBeVisible();
    const pb = (await pop.boundingBox())!, sb = (await spines.first().boundingBox())!;
    expect(pb.width, 'the popped CD lies flat: wider than a spine').toBeGreaterThan(sb.width * 4);
    expect(pb.width, 'and wider than it is tall (flat, not standing)').toBeGreaterThan(pb.height);
    // its songs, in the order they will play
    expect(await pop.locator('[data-pop=songs] li').count()).toBeGreaterThanOrEqual(1);
    // hovering another CD selects it: it lifts in the rack and pops out instead
    await spines.nth(2).hover();
    await expect(rack).toHaveAttribute('data-sel', '2');
    await expect(spines.nth(2)).toHaveClass(/\bsel\b/);
    await expect(spines.first()).not.toHaveClass(/\bsel\b/);
    await expect(pop).toHaveAttribute('data-album', (await spines.nth(2).getAttribute('data-album'))!);
    await expect(pop.locator('[data-pop=label]')).toHaveText(/3rd/i);
    // the wheel over the rack goes through them, and the arrow keys too
    await page.mouse.wheel(0, 120);
    await expect(rack).toHaveAttribute('data-sel', '3');
    await rack.focus(); await page.keyboard.press('ArrowLeft'); await page.keyboard.press('ArrowLeft');
    await expect(rack).toHaveAttribute('data-sel', '1');
    // a click on the CD that popped out plays that album
    const album = (await pop.getAttribute('data-album'))!;
    await pop.click();
    await expect(page.locator('.lnp-album')).toHaveText(album, { timeout: 10_000 });
    expect(errors).toEqual([]);
  });

  test('the Now Playing card: the cover, the song, its album and year, and what the file is (format, bitrate)', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    const card = page.getByTestId('desk-np');
    await expect(card).toContainText(/nothing playing/i);
    await shuffle(page);
    await page.getByTestId('desk-open-cd').click();   // (paused, so the song stays put)
    await expect(card.locator('[data-np=title]')).toHaveText((await page.getByTestId('ipod-np-title').textContent())!.trim());
    await expect(card.locator('[data-np=album]')).toContainText((await page.locator('.lnp-album').textContent())!.trim());
    await expect(card.locator('[data-np=album]')).toHaveText(/\b(19|20)\d\d\b/);
    await expect(card.locator('img, .cover').first()).toBeVisible();
    await expect(card.locator('[data-np=format]')).toHaveText(/(FLAC|ALAC|WAV|MP3|AAC|OGG|OPUS).*\d+\s*kbps/i, { timeout: 10_000 });
    expect(errors).toEqual([]);
  });

  test('the preamp: the VU meter, a volume knob that is the player\'s volume, and bass and treble that shape the sound', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await shuffle(page);
    const amp = page.getByTestId('desk-preamp');
    await expect(amp.locator('[data-needle]')).toHaveCount(1);
    const vol = amp.locator('[data-knob=vol]'), v0 = Number(await vol.getAttribute('aria-valuenow'));
    await vol.focus(); await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown');
    await expect.poll(async () => Number(await vol.getAttribute('aria-valuenow'))).toBe(Math.max(0, v0 - 10));
    await expect.poll(() => page.evaluate(() => Math.round(((window as any).flacie.loud() ?? -1) * 100))).toBeLessThan(v0);
    // bass up three steps: the bass shelf on the music itself
    const bass = amp.locator('[data-knob=bass]');
    await bass.focus(); for (let i = 0; i < 3; i++) await page.keyboard.press('ArrowUp');
    await expect(bass).toHaveAttribute('aria-valuenow', '3');
    await expect.poll(() => page.evaluate(() => (window as any).flacie.toneLevel().low)).toBeGreaterThan(2.5);
    // treble down, and it is kept for next time
    const treble = amp.locator('[data-knob=treble]');
    await treble.focus(); await page.keyboard.press('ArrowDown');
    await expect.poll(() => page.evaluate(() => (window as any).flacie.toneLevel().high)).toBeLessThan(-0.5);
    await page.reload(); await expect(page.getByTestId('desk-preamp')).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId('desk-preamp').locator('[data-knob=bass]')).toHaveAttribute('aria-valuenow', '3');
    await expect(page.getByTestId('desk-preamp').locator('[data-knob=treble]')).toHaveAttribute('aria-valuenow', '-1');
    // flat again (back to how it was, for the tests after)
    await page.evaluate(() => (window as any).flacie.tone(0, 0));
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

  test('the preamp\'s meter: VU, a spectrum in Hz, or a five-band EQ; it says the file\'s rate in kHz; the choice is kept', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await shuffle(page);
    await page.getByTestId('desk-open-cd').click();   // (paused, so the song and its facts stay put)
    const amp = page.getByTestId('desk-preamp');
    await expect(amp).toHaveAttribute('data-meter-mode', 'vu');
    await expect(amp.locator('[data-needle]')).toBeVisible();
    // what the file is played at (the mock's songs: CD-quality FLAC)
    await expect(amp.getByTestId('desk-rate')).toHaveText('44.1 kHz · 16-bit', { timeout: 10_000 });
    // the spectrum: its bars on a canvas, the scale in Hz
    await amp.locator('[data-meter=spec]').click();
    await expect(amp).toHaveAttribute('data-meter-mode', 'spec');
    await expect(amp.locator('canvas[data-spectrum]')).toBeVisible();
    await expect(amp.locator('[data-needle]')).toBeHidden();
    // its scale reaches half the song's sample rate (a 44.1 kHz song: 22 kHz), and the analyser is opened at the song's own rate to see that high
    await expect(amp.locator('.face-spec .hz')).toContainText('22k');
    await expect.poll(() => page.evaluate(() => (window as any).flacie.vizFrame()?.fileRate)).toBe(44100);
    await expect(amp.locator('.face-spec .hz')).toContainText('Hz');
    // the EQ: five bands, each one shaping the music
    await amp.locator('[data-meter=eq]').click();
    const sliders = amp.locator('[data-band]');
    await expect(sliders).toHaveCount(5);
    // (the faders show once the page has answered the click: a hidden control takes no focus, so the arrow keys would go nowhere)
    await expect(amp).toHaveAttribute('data-meter-mode', 'eq'); await expect(sliders.nth(2)).toBeVisible();
    await sliders.nth(2).focus(); for (let i = 0; i < 4; i++) await page.keyboard.press('ArrowUp');
    await expect(sliders.nth(2)).toHaveAttribute('aria-valuenow', '4');
    await expect.poll(() => page.evaluate(() => (window as any).flacie.eqGet()[2])).toBe(4);
    await expect.poll(() => page.evaluate(() => (window as any).flacie.toneLevel().bands[2])).toBeGreaterThan(3.5);
    // the meter and the EQ are kept for next time
    await page.reload(); await expect(page.getByTestId('desk-preamp')).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId('desk-preamp')).toHaveAttribute('data-meter-mode', 'eq');
    await expect(page.getByTestId('desk-preamp').locator('[data-band]').nth(2)).toHaveAttribute('aria-valuenow', '4');
    await page.evaluate(() => { (window as any).flacie.eq([0, 0, 0, 0, 0]); (window as any).flacie.meter('vu'); });
    expect(errors).toEqual([]);
  });

  test('one download: once the EQ is on, the earbuds and meters read the music itself, not a second copy of the song', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await page.addInitScript(() => { try { localStorage.removeItem('flacie.eq'); localStorage.removeItem('flacie.tone'); } catch { } });
    await openDesk(page, { model: 'classic6', look: 2 });
    await shuffle(page);
    // the EQ flat: the music plays untouched, and the visualisers read a silent copy (as before)
    await expect.poll(() => page.evaluate(() => (window as any).flacie.vizSource())).toBe('twin');
    // the EQ on: the music goes through Web Audio anyway, so the visualisers read that, and the copy is let go
    const bass = page.getByTestId('desk-preamp').locator('[data-knob=bass]');
    await bass.focus(); await page.keyboard.press('ArrowUp');
    await expect.poll(() => page.evaluate(() => (window as any).flacie.vizSource())).toBe('playback');
    // and nothing asks for the copy again while it plays on (the earbuds still have their frames)
    await page.waitForTimeout(1200);
    expect(await page.evaluate(() => (window as any).flacie.vizSource())).toBe('playback');
    expect(await page.evaluate(() => !!(window as any).flacie.vizFrame())).toBe(true);
    await page.evaluate(() => (window as any).flacie.eq([0, 0, 0, 0, 0]));
  });

  test('the iPod\'s look, colour and desk go with the account: another browser (nothing stored there) opens the same desk', async ({ page, browser }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    const swatch = page.locator('.ipod-panel .ipod-chip.swatch').nth(1);
    const colour = (await swatch.getAttribute('data-color'))!;
    await expect(async () => { await swatch.click(); await expect(page.getByTestId('ipod')).toHaveAttribute('data-color', colour, { timeout: 1000 }); }).toPass({ timeout: 20_000 });
    await page.locator('[data-testid=desk-options] [data-desk-opt=pencil]').click();
    await expect(page.getByTestId('desk-pencil')).toHaveCount(0);
    await page.waitForTimeout(2500);   // (the profile is saved a moment after the last change)
    // a fresh browser: the same account, nothing in its storage
    const ctx = await browser.newContext({ viewport: { width: 1600, height: 900 } }); const other = await ctx.newPage();
    await signIn(other); await other.goto('/ipod');
    await expect(other.getByTestId('ipod')).toHaveAttribute('data-model', 'classic6', { timeout: 20_000 });
    await expect(other.getByTestId('ipod')).toHaveAttribute('data-look', 'desk');
    await expect(other.getByTestId('ipod')).toHaveAttribute('data-color', colour);
    await expect(other.getByTestId('desk')).toBeVisible();
    await expect(other.getByTestId('desk-pencil')).toHaveCount(0);
    await ctx.close();
    // (back as it was, for the tests after)
    await page.locator('[data-testid=desk-options] [data-desk-opt=pencil]').click();
    await page.locator('.ipod-panel .ipod-chip.swatch').first().click();
    await page.waitForTimeout(2000);
    expect(errors).toEqual([]);
  });

  test('the preamp\'s VOLUME is the volume the music plays at, also when iPod mode is opened straight away (no player bar on the page)', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await page.addInitScript(() => { try { localStorage.setItem('flacie.volume', '0.4'); } catch { } });
    await openDesk(page, { model: 'classic6', look: 2 });
    await expect(page.getByTestId('desk-preamp').locator('[data-knob=vol]')).toHaveAttribute('aria-valuenow', '40', { timeout: 10_000 });
    expect(await page.evaluate(() => Math.round((window as any).flacie.volume() * 100))).toBe(40);
  });

  test('a song playing: changing the model or the look comes back on Now Playing', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await shuffle(page);
    await page.getByTestId('ipod-keys').focus(); await page.keyboard.press('Escape');
    await expect(page.getByTestId('ipod-np')).toHaveCount(0);
    await page.locator('.ipod-panel .ipod-chip[data-model="video5"]').click();
    await expect(page.getByTestId('ipod')).toHaveAttribute('data-model', 'video5');
    await expect(page.getByTestId('ipod-np')).toBeVisible();
    await page.getByTestId('ipod-keys').focus(); await page.keyboard.press('Escape');
    await page.locator('.ipod-panel .ipod-chip[data-look="device"]').click();
    await expect(page.getByTestId('ipod-np')).toBeVisible();
  });

  test('the CDs are keys too: Tab to the rack, the arrow keys pick one, Enter plays it', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await shuffle(page);
    await page.getByTestId('desk-open-cd').click();
    const rack = page.getByTestId('desk-rack');
    await expect(rack).toHaveAttribute('tabindex', '0'); await expect(rack).toHaveAttribute('role', 'listbox');
    // (back to the first and one on, again if the page put the rack back meanwhile: the mock's songs are 3 s long, and a new song resets the pick)
    await expect(async () => {
      await rack.focus(); for (let i = 0; i < 8; i++) await page.keyboard.press('ArrowLeft');
      await page.keyboard.press('ArrowRight');
      await expect(rack).toHaveAttribute('data-sel', '1', { timeout: 1500 });
    }).toPass({ timeout: 20_000 });
    const album = (await page.getByTestId('desk-pop').getAttribute('data-album'))!;
    await page.keyboard.press('Enter');
    await expect(page.locator('.lnp-album')).toHaveText(album, { timeout: 10_000 });
    await expect(page.getByTestId('ipod-np')).toBeVisible();   // the iPod did not take the Enter as its own (still Now Playing)
    expect(errors).toEqual([]);
  });

  test('the journal keeps the viewer\'s own day, and the lyrics are written in as they are sung', async ({ page }) => {
    await page.clock.setFixedTime(new Date('2031-02-03T10:00:00'));   // a Monday, not the server's day
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await expect(page.getByTestId('desk-date')).toHaveText('3 feb', { timeout: 10_000 });
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
    await expect(page.getByTestId('desk-rack')).toContainText('nothing queued');
    await expect(page.getByTestId('desk-spine')).toHaveCount(0);
    await expect(page.getByTestId('desk-pop')).toHaveCount(0);
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
    // a new desk: earbuds, the pencil by the notebook, the preamp, the concert ticket, a USB stick and a paperclip (no guitar picks any more); the iPod lies on the desk
    // itself (no dock, no option for one); no cassette or MiniDiscs any more
    await expect(page.locator('[data-desk-phones="0"]')).toHaveClass(/on/);
    for (const id of ['desk-preamp', 'desk-ticket', 'desk-pencil']) await expect(page.getByTestId(id), id).toBeVisible();
    await expect(page.getByTestId('desk-usb')).toBeVisible(); await expect(page.getByTestId('desk-clip')).toBeVisible(); await expect(opt('usb')).toHaveClass(/on/);
    await expect(page.getByTestId('desk-picks')).toHaveCount(0); await expect(opt('picks')).toHaveCount(0);
    for (const id of ['desk-cassette', 'desk-minidiscs']) await expect(page.getByTestId(id), id).toHaveCount(0);
    for (const k of ['tape', 'md']) await expect(opt(k), k).toHaveCount(0);
    await expect(page.getByTestId('desk-dock')).toHaveCount(0); await expect(opt('dock')).toHaveCount(0);
    // the others off and on again; the preamp left off
    for (const [k, id] of [['usb', 'desk-usb'], ['pencil', 'desk-pencil'], ['ticket', 'desk-ticket']]) {
      await opt(k).click(); await expect(page.getByTestId(id)).toHaveCount(0); await expect(opt(k)).not.toHaveClass(/on/);
      await opt(k).click(); await expect(page.getByTestId(id)).toBeVisible();
    }
    await opt('vu').click(); await expect(page.getByTestId('desk-preamp')).toHaveCount(0);
    // studio headphones: a coiled cable to a gold adapter, no earbuds
    await page.locator('[data-desk-phones="1"]').click();
    await expect(page.getByTestId('desk-adapter')).toBeVisible();
    await expect(page.locator('[data-bud]')).toHaveCount(0);
    // kept across a reload
    await page.reload(); await expect(page.getByTestId('desk')).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId('desk-adapter')).toBeVisible(); await expect(page.getByTestId('desk-preamp')).toHaveCount(0);
    // the options belong to the desk look
    await page.locator('.ipod-panel .ipod-chip[data-look="device"]').click();
    await expect(page.getByTestId('desk-options')).toHaveCount(0);
    expect(errors).toEqual([]);
  });

  test('the desk keeps its props apart: the ticket follows the artist playing, nothing lies on another, the cable crosses none', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await expect(page.getByTestId('desk-ticket')).toBeVisible();
    await expect(page.getByTestId('desk-usb')).toBeVisible();
    await shuffle(page);
    const artist = () => page.locator('[data-testid=ipod-np-title] + span').textContent().then((s) => s!.trim().toUpperCase());
    await expect(page.getByTestId('desk-ticket')).toContainText(await artist(), { timeout: 10_000 });
    await expect(page.getByTestId('desk-ticket')).toContainText('ADMIT ONE');
    // another artist's song: the stub is for them (the mock's songs last 3 s: wait for an artist change)
    const first = await artist();
    await expect.poll(artist, { timeout: 20_000 }).not.toBe(first);
    await expect(page.getByTestId('desk-ticket')).toContainText(await artist(), { timeout: 10_000 });
    await page.getByTestId('desk-open-cd').click();
    // the props lie apart: none on another
    const ids = ['desk-ticket', 'desk-usb', 'desk-clip', 'desk-rack', 'desk-pop', 'desk-open-cd', 'desk-np', 'desk-preamp', 'desk-journal', 'desk-coffee'];
    const boxes = await Promise.all(ids.map(async (id) => [id, (await page.getByTestId(id).boundingBox())!] as const));
    for (const [a, p] of boxes) for (const [b, q] of boxes) if (a < b) {
      const o = Math.max(0, Math.min(p.x + p.width, q.x + q.width) - Math.max(p.x, q.x)) * Math.max(0, Math.min(p.y + p.height, q.y + q.height) - Math.max(p.y, q.y));
      expect(o / Math.min(p.width * p.height, q.width * q.height), `${a} on ${b}`).toBeLessThan(0.04);
    }
    // and the earbuds (the cable, both leads, both buds) lie across none of them
    const crossed = await page.evaluate(() => {
      const out: string[] = [], pts: DOMPoint[] = [];
      for (const id of ['desk-cable', 'desk-lead-l', 'desk-lead-r']) {
        const path = document.getElementById(id) as unknown as SVGPathElement, m = path.getScreenCTM()!, L = path.getTotalLength();
        for (let i = 0; i <= 300; i++) { const q = path.getPointAtLength(L * i / 300); pts.push(new DOMPoint(q.x, q.y).matrixTransform(m)); }
      }
      for (const b of document.querySelectorAll('[data-bud] > g')) { const q = b.getBoundingClientRect(); for (const fx of [0, .5, 1]) for (const fy of [0, .5, 1]) pts.push(new DOMPoint(q.left + q.width * fx, q.top + q.height * fy)); }
      for (const s of ['.desk-rack', '.desk-pop', '.desk-ticket', '.desk-journal', '.desk-preamp', '.desk-np', '.desk-open']) {
        const q = document.querySelector(s)!.getBoundingClientRect();
        if (pts.some((t) => t.x > q.left + 2 && t.x < q.right - 2 && t.y > q.top + 2 && t.y < q.bottom - 2)) out.push(s);
      }
      return out;
    });
    expect(crossed).toEqual([]);
    expect(errors).toEqual([]);
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

  test('the iPod\'s edge on the desk is its real back: steel behind a classic, the colour picked on an aluminium mini, and it follows the swatches', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    // the edge's colours, averaged (what it is drawn with), and how far from grey they are
    const edge = page.locator('.desk-ipod-edge');
    const tone = () => edge.evaluate((e) => {
      const cs = getComputedStyle(e), all = [...cs.backgroundImage.matchAll(/rgba?\((\d+),\s*(\d+),\s*(\d+)/g)].map((m) => [+m[1], +m[2], +m[3]]);
      const avg = [0, 1, 2].map((i) => all.reduce((n, c) => n + c[i], 0) / Math.max(1, all.length));
      return { r: avg[0], g: avg[1], b: avg[2], chroma: Math.max(...avg) - Math.min(...avg), stops: all.length };
    });
    await openDesk(page, { model: 'mini', look: 2, color: 1 });   // a blue mini: anodised aluminium all round
    await expect(page.getByTestId('ipod')).toHaveAttribute('data-color', 'Blue');
    await expect(edge).toHaveAttribute('data-back', 'aluminium');
    let t = await tone();
    expect(t.stops).toBeGreaterThan(2);
    expect(t.b, 'a blue mini\'s edge is blue').toBeGreaterThan(t.r + 40);
    // another colour from the panel: the edge follows at once (the 1st generation mini's pink is a muted one, a pale rose: red clearly above green, a little above blue)
    await page.locator('.ipod-panel .swatch[data-color="Pink"]').click();
    await expect(page.getByTestId('ipod')).toHaveAttribute('data-color', 'Pink');
    await expect.poll(async () => { const p = await tone(); return p.r > p.b + 10 && p.r > p.g + 30; }, { message: 'a pink mini\'s edge is pink' }).toBe(true);
    // a black classic: its front is black aluminium, its back polished steel, mirroring the warm room (not a cold silver)
    await openDesk(page, { model: 'classic6', look: 2, color: 1 });
    await expect(page.getByTestId('ipod')).toHaveAttribute('data-color', 'Black');
    await expect(edge).toHaveAttribute('data-back', 'steel');
    t = await tone();
    expect(t.chroma, 'steel is not coloured').toBeLessThan(30);
    expect(t.r, 'steel mirrors the warm desk').toBeGreaterThanOrEqual(t.b);
    expect(errors).toEqual([]);
  });

  test('the desk takes the iPod\'s colour: the ticket\'s band, the preamp\'s pointer, the USB stick\'s light, the sticky notes; a silver iPod leaves it its warm red', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    // the colours the accented things are drawn in (the first colour of a gradient), as [r, g, b]
    const colours = () => page.evaluate(() => {
      const rgb = (s: string) => { const m = s.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/), k = s.match(/color\(srgb ([\d.]+) ([\d.]+) ([\d.]+)/); return m ? [+m[1], +m[2], +m[3]] : k ? [1, 2, 3].map((i) => Math.round(+k[i] * 255)) : null; };
      const q = (sel: string) => document.querySelector(sel)!;
      return {
        band: rgb(getComputedStyle(q('.desk-ticket'), '::before').backgroundImage),
        pointer: rgb(getComputedStyle(q('.pre-knob.big .knob i')).backgroundColor),
        light: rgb(getComputedStyle(q('.desk-usb .led')).backgroundColor),
        key: rgb(getComputedStyle(q('.pre-modes button.on')).color),
      };
    });
    const blueish = (c: number[] | null) => !!c && c[2] > c[0] + 30;
    const pinkish = (c: number[] | null) => !!c && c[0] > c[1] + 40 && c[2] > c[1] + 10;
    await openDesk(page, { model: 'nano2', look: 2, color: 1 });   // a blue nano
    await expect(page.getByTestId('ipod')).toHaveAttribute('data-color', 'Blue');
    let c = await colours();
    for (const [k, v] of Object.entries(c)) expect(blueish(v), `${k} with a blue iPod: ${v}`).toBe(true);
    // another colour from the panel: the desk follows
    await page.locator('.ipod-panel .swatch[data-color="Pink"]').click();
    await expect.poll(async () => { const p = await colours(); return pinkish(p.band) && pinkish(p.pointer) && pinkish(p.light); }, { message: 'the desk turns pink' }).toBe(true);
    // the sticky notes on a phone are the colour too
    await page.setViewportSize({ width: 390, height: 844 }); await page.waitForTimeout(600);
    const note = rgb3(await page.locator('[data-testid=desk-journal] .sheet.left').evaluate((e) => getComputedStyle(e).backgroundImage));
    expect(pinkish(note), `the sticky note with a pink iPod: ${note}`).toBe(true);
    // a silver classic: the desk's own colours (the red pointer, the red-to-amber band, the green light, yellow notes)
    await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2, color: 0 });
    c = await colours();
    expect(c).toEqual({ band: [200, 69, 47], pointer: [192, 57, 43], light: [46, 125, 58], key: [122, 36, 24] });
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
      // a phone is the compact desk: the sticky note, the Now Playing card and the iPod, all in view (the CDs and the coffee put away; the size matrix in
      // 1o-ipod-desk-sizes.spec.ts checks every size)
      const whole = w < h ? ['desk-np', 'ipod-device'] : ['desk-journal', 'desk-pop', 'desk-rack', 'ipod-device'];
      for (const id of whole) {
        const b = await page.getByTestId(id).boundingBox(); if (!b) continue;
        expect(inside(b, deskBox, 30), `${id} at ${w}x${h}`).toBe(true);
      }
      if (w < h) {
        expect(inside((await page.getByTestId('desk-journal').locator('.j-title').boundingBox())!, deskBox, 30), `journal title at ${w}x${h}`).toBe(true);
        for (const id of ['desk-pop', 'desk-rack', 'desk-open-cd', 'desk-coffee']) await expect(page.getByTestId(id), `${id} put away at ${w}x${h}`).toBeHidden();
      }
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${w}x${h} scrolls sideways`).toBe(true);
      if (w < h) expect(((await page.getByTestId('ipod-device').boundingBox())!.height) / h, 'portrait iPod size').toBeGreaterThan(0.45);
    }
  });

  test('the journal\'s handwriting sits on the printed rules, each line at its own slight angle', async ({ page }) => {
    await signIn(page);
    for (const [w, h] of [[1600, 900], [1280, 720]]) {
      await page.setViewportSize({ width: w, height: h });
      await page.goto('/songs'); await page.locator('.track').first().locator('button.track-main').click();
      await page.evaluate(() => localStorage.setItem('flacie.ipod', JSON.stringify({ model: 'classic6', look: 2, panel: false })));
      await page.evaluate(() => (window as any).Blazor.navigateTo('/ipod'));
      await expect(page.locator('[data-testid=desk-journal] .j-lines div.on').first()).toBeVisible({ timeout: 15_000 });
      // the panel closed (the page opens it on a wide window): with it open a 1280 wide window is the compact desk, where the lyrics are on a sticky note
      if ((await page.getByTestId('ipod-panel').getAttribute('class'))!.includes('open')) await page.getByTestId('ipod-gear').click();
      await expect(page.getByTestId('desk-pencil')).toBeVisible(); await page.waitForTimeout(600);
      const r = await page.getByTestId('desk-journal').evaluate((j) => {
        const sheet = j.querySelector('.sheet.left') as HTMLElement, lines = [...j.querySelectorAll('.j-lines div.on')] as HTMLElement[];
        // the rule spacing in px: the line height (they are the same by design); the rules are the bottom 0.12/3.32 of each band
        const step = parseFloat(getComputedStyle(j.querySelector('.j-lines')!).lineHeight), rule0 = step * (3.26 / 3.32);
        const top = (el: HTMLElement) => { let y = 0; for (let n: HTMLElement | null = el; n && n !== j; n = n.offsetParent as HTMLElement | null) y += n.offsetTop; return y; };
        const off = lines.map((d) => { const s = document.createElement('span'); s.style.cssText = 'display:inline-block;width:0;height:0;vertical-align:baseline'; d.prepend(s); const y = top(s); s.remove(); const k = Math.round((y - rule0) / step); return Math.abs(y - (rule0 + k * step)) / step; });
        const angles = lines.map((d) => parseFloat(getComputedStyle(d).rotate) || 0);
        return { worst: Math.max(...off), sheet: !!sheet, angles };
      });
      expect(r.worst, `baseline off the rule at ${w}x${h}`).toBeLessThan(0.12);
      // the pencil lies on the desk beside the notebook, not on its pages (a page turns over them)
      const pen = (await page.getByTestId('desk-pencil').boundingBox())!, book = (await page.getByTestId('desk-journal').boundingBox())!;
      expect(Math.max(0, Math.min(pen.x + pen.width, book.x + book.width) - Math.max(pen.x, book.x)) * Math.max(0, Math.min(pen.y + pen.height, book.y + book.height) - Math.max(pen.y, book.y)), `pencil on the notebook at ${w}x${h}`).toBeLessThan(pen.width * pen.height * 0.05);
      for (const a of r.angles) expect(Math.abs(a)).toBeLessThanOrEqual(0.5);
      if (r.angles.length > 1) expect(new Set(r.angles).size).toBeGreaterThan(1);
    }
  });

  test('on a narrow screen the sticky note and the Now Playing card sit above the iPod, clear of it and of the earbud cable', async ({ page }) => {
    await signIn(page);
    for (const model of ['classic6', 'nano3', 'mini2']) {
      for (const [w, h] of [[390, 844], [360, 640], [768, 1024]]) {
        await page.setViewportSize({ width: w, height: h });
        // music on (the next CD is there only when something comes next), then iPod mode in the same page
        await page.goto('/songs'); await page.locator('.track').first().locator('button.track-main').click();
        await page.evaluate((m) => localStorage.setItem('flacie.ipod', JSON.stringify({ model: m, look: 2, panel: false })), model);
        await page.evaluate(() => (window as any).Blazor.navigateTo('/ipod'));
        await expect(page.getByTestId('desk-np')).toBeVisible({ timeout: 20_000 });
        await page.waitForTimeout(900);
        const r = await page.evaluate(() => {
          const box = (s: string) => { const e = document.querySelector(s); if (!e) return null; const q = e.getBoundingClientRect(); return q.width ? q : null; };
          const over = (a: DOMRect | null, c: DOMRect | null) => a && c ? Math.max(0, Math.min(a.right, c.right) - Math.max(a.left, c.left)) * Math.max(0, Math.min(a.bottom, c.bottom) - Math.max(a.top, c.top)) : 0;
          const inside = (x: number, y: number, q: DOMRect | null) => !!q && x > q.left + 2 && x < q.right - 2 && y > q.top + 2 && y < q.bottom - 2;
          const note = box('[data-testid=desk-journal]'), card = box('[data-testid=desk-np]'), ipod = box('[data-testid=ipod-device]');
          const path = document.getElementById('desk-cable') as unknown as SVGPathElement; const m = path.getScreenCTM()!; const L = path.getTotalLength();
          let onNote = 0, onCard = 0;
          for (let i = 0; i <= 200; i++) { const q = path.getPointAtLength(L * i / 200); const s = new DOMPoint(q.x, q.y).matrixTransform(m); if (inside(s.x, s.y, note)) onNote++; if (inside(s.x, s.y, card)) onCard++; }
          return { noteOnIpod: Math.round(over(note, ipod)), cardOnIpod: Math.round(over(card, ipod)), onNote, onCard, above: !!note && !!card && !!ipod && note.top < ipod.top && card.top < ipod.top };
        });
        expect(r, `${model} at ${w}x${h}`).toEqual({ noteOnIpod: 0, cardOnIpod: 0, onNote: 0, onCard: 0, above: true });
      }
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
