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
    for (const id of ['ipod-keys', 'ipod-device', 'ipod-panel', 'ipod-gear', 'desk-journal', 'desk-polaroid', 'desk-coffee', 'desk-earbuds', 'desk-note']) await expect(page.getByTestId(id)).toBeVisible();
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
    await expect(page.locator('.j-title')).toContainText(song);
    await expect(page.getByTestId('desk-open-cd')).toBeVisible();
    const next = (await page.locator('[data-testid=desk-note] span').textContent())!.trim();
    expect(next).not.toBe('nothing queued');
    // the disc gets its print, made from the cover in the browser (it never travels to the server: it is bigger than a Blazor message)
    await expect.poll(() => page.getByTestId('desk-disc').evaluate((d) => getComputedStyle(d).getPropertyValue('--art')), { timeout: 10_000 }).toContain('data:image/png');
    // the wheel's next zone skips the song
    const b = (await page.getByTestId('ipod-wheel').boundingBox())!;
    await page.mouse.click(b.x + b.width / 2 + b.width * 0.42, b.y + b.height / 2); await page.waitForTimeout(500);
    await expect(page.getByTestId('ipod-np-title')).not.toHaveText(song, { timeout: 10_000 });
    // a click on the open CD plays that album: the note moves on to the album after it
    const before = (await page.locator('[data-testid=desk-note] span').textContent())!.trim();
    await page.getByTestId('desk-open-cd').click();
    await expect(page.locator('[data-testid=desk-note] span')).not.toHaveText(before, { timeout: 10_000 });
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

  test('the tilted iPod\'s wheel keeps its own zones: a tap just outside the middle button is the ring, just inside it is the button', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    const title = page.getByTestId('ipod-title'), home = (await title.textContent())!.trim(), row = await selIndex(page);
    // the wheel lies turned 7 degrees: its box on screen is bigger than the wheel by cos 7 + sin 7; the device's own right is 7 degrees up from the screen's
    const b = (await page.getByTestId('ipod-wheel').boundingBox())!, t = (-7 * Math.PI) / 180;
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
    // the iPod lies turned 7 degrees: a drag straight up the screen moves along the iPod by D cos 7, and the list moves that far in its own (zoomed) pixels
    const want = (D * Math.cos((7 * Math.PI) / 180)) / zoom;
    await expect.poll(() => list.evaluate((e) => e.scrollTop)).toBeGreaterThan(want * 0.96);
    expect(await list.evaluate((e) => e.scrollTop)).toBeLessThan(want * 1.04);
  });

  test('the CDs are keys too: Tab to one, Enter plays it', async ({ page }) => {
    const errors = consoleErrors(page);
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    await openDesk(page, { model: 'classic6', look: 2 });
    await shuffle(page);
    const cd = page.getByTestId('desk-open-cd');
    await expect(cd).toHaveAttribute('tabindex', '0'); await expect(cd).toHaveAttribute('role', 'button');
    await expect(cd).toHaveAttribute('aria-label', /^Play .+ by .+/);
    const before = (await page.locator('[data-testid=desk-note] span').textContent())!.trim();
    const screen = await page.getByTestId('ipod-np-title').textContent();
    await cd.focus(); await page.keyboard.press('Enter');
    await expect(page.locator('[data-testid=desk-note] span')).not.toHaveText(before, { timeout: 10_000 });
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
    await expect(page.locator('.j-title')).toHaveText('nothing playing');
    await expect(page.locator('[data-testid=desk-note] span')).toHaveText('nothing queued');
    await expect(page.getByTestId('desk-open-cd')).toHaveCount(0);
    await expect(page.getByTestId('desk-lyric')).toHaveCount(0);
    expect(errors).toEqual([]);
    await ctx.close();
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
      // a phone frames a closer look at the same desk: the journal, the CD and the coffee may run off its edges (their size stays the desk's), but the journal's
      // title, the note and the iPod are in view, and the disc and the coffee are at least partly in the frame
      const whole = w < h ? ['desk-note', 'ipod-device'] : ['desk-journal', 'desk-note', 'ipod-device'];
      for (const id of whole) {
        const b = await page.getByTestId(id).boundingBox(); if (!b) continue;
        expect(inside(b, deskBox, 30), `${id} at ${w}x${h}`).toBe(true);
      }
      if (w < h) {
        expect(inside((await page.locator('.j-title').boundingBox())!, deskBox, 30), `journal title at ${w}x${h}`).toBe(true);
        for (const sel of ['[data-testid=desk-disc]', '.desk-coffee .saucer']) {
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
