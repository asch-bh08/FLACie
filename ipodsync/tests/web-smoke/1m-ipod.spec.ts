import { test, expect, Page } from '@playwright/test';

// The iPod mode (/ipod): the whole line-up of the Android emulator on the page: 23 models and their colours, the mono / colour / nano screens, Cover Flow, the settings, the wheel.
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
const rows = (page: Page) => page.getByTestId('ipod-row');
const sel = (page: Page) => page.locator('[data-testid=ipod-row].sel');
const title = (page: Page) => page.getByTestId('ipod-title');
const labels = async (page: Page) => (await page.locator('[data-testid=ipod-row] .lcd-label').allTextContents()).map((s) => s.trim());
const stage = (page: Page) => page.getByTestId('ipod');
const shot = async (page: Page, name: string) => { await page.mouse.move(5, 5); await page.waitForTimeout(600); await page.screenshot({ path: `../../../docs/img/ipod-web-${name}.png` }); };
const selText = async (page: Page) => (await sel(page).locator('.lcd-label').textContent()) ?? '';

/** Open /ipod showing a given model/colour/look (the page keeps these in the browser, so they are seeded before it loads). */
async function openIpod(page: Page, prefs: Record<string, unknown> = {}) {
  // not again on a reload: the page must show what it saved itself
  await page.addInitScript((p) => { try { const nav = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined; if (nav?.type !== 'reload') localStorage.setItem('flacie.ipod', JSON.stringify(p)); } catch { } }, prefs);
  await page.goto('/ipod');
  await expect(stage(page)).toBeVisible({ timeout: 20_000 });
  await expect(stage(page)).toHaveAttribute('data-model', (prefs.model as string) ?? 'ipod4', { timeout: 20_000 });
  await page.waitForTimeout(400);
}

async function dragWheel(page: Page, degrees: number, ms = 12, hold = 80) {
  const b = (await page.getByTestId('ipod-wheel').boundingBox())!;
  const cx = b.x + b.width / 2, cy = b.y + b.height / 2, r = b.width * 0.42;
  const at = (a: number) => [cx + r * Math.cos((a * Math.PI) / 180), cy + r * Math.sin((a * Math.PI) / 180)] as const;
  const [sx, sy] = at(-30);
  await page.mouse.move(sx, sy); await page.mouse.down();
  const steps = Math.max(4, Math.abs(degrees) / 5);
  for (let i = 1; i <= steps; i++) { const [x, y] = at(-30 + (degrees * i) / steps); await page.mouse.move(x, y); await page.waitForTimeout(ms); }
  if (hold) await page.waitForTimeout(hold); await page.mouse.up(); await page.waitForTimeout(150);
}
/** A tap on one zone of the ring: top MENU, right next, bottom play, left previous, the middle select. */
async function tapZone(page: Page, zone: 'menu' | 'next' | 'play' | 'prev' | 'center') {
  const b = (await page.getByTestId('ipod-wheel').boundingBox())!;
  const cx = b.x + b.width / 2, cy = b.y + b.height / 2, r = b.width * 0.42;
  const p = { menu: [cx, cy - r], next: [cx + r, cy], play: [cx, cy + r], prev: [cx - r, cy], center: [cx, cy] }[zone];
  await page.mouse.click(p[0], p[1]); await page.waitForTimeout(150);
}

test.describe.serial('ipod mode', () => {
  test('every iPod of the line-up shows up with its own screen', async ({ page }) => {
    test.setTimeout(240_000);
    await signIn(page);
    await page.setViewportSize({ width: 700, height: 900 });
    const all: Array<[string, string, string]> = [   // model, family, how many colours the picker offers
      ['ipod1', 'mono', '1'], ['ipod2', 'mono', '1'], ['ipod3', 'mono', '1'], ['ipod4', 'mono', '2'], ['video5', 'video', '2'], ['classic6', 'nano', '2'], ['classic7', 'nano', '2'],
      ['mini', 'mono', '5'], ['mini2', 'mono', '4'], ['nano1', 'video', '2'], ['nano2', 'video', '6'], ['nano3', 'nano', '5'], ['nano4', 'nano', '9'], ['nano5', 'nano', '9'],
      ['nano6', 'touch', '7'], ['nano7', 'touch', '7'], ['touch1', 'touch', '1'], ['touch2', 'touch', '1'], ['touch3', 'touch', '1'], ['touch4', 'touch', '2'], ['touch5', 'touch', '6'], ['touch6', 'touch', '6'], ['touch7', 'touch', '6'],
    ];
    const shots = new Set(['ipod1', 'ipod4', 'video5', 'classic6', 'nano3', 'nano4', 'nano6', 'touch5', 'mini']);
    for (const [id, fam, colours] of all) {
      await page.goto('about:blank');
      await openIpod(page, { model: id });
      await expect(page.locator(`.ipod.fam-${fam}`)).toBeVisible();
      if (fam === 'touch') await expect(page.getByTestId('ipod-touch-screen').locator('iframe')).toBeVisible();
      else { await expect(page.getByTestId('ipod-lcd')).toBeVisible(); await expect(rows(page).first()).toBeVisible(); await expect(page.getByTestId('ipod-wheel')).toBeVisible(); }
      await page.getByTestId('ipod-gear').click();
      await expect(page.locator('.ipod-panel .ipod-chip.swatch')).toHaveCount(+colours);
      await page.getByTestId('ipod-gear').click();
      if (shots.has(id)) { await page.mouse.move(5, 5); await page.waitForTimeout(500); await page.screenshot({ path: `../../../docs/img/ipod-web-${id}.png` }); }
    }
  });

  test('each family has its own main menu, preview and screen', async ({ page }) => {
    test.setTimeout(150_000);
    await signIn(page);
    await page.setViewportSize({ width: 700, height: 900 });
    await openIpod(page, { model: 'ipod4' });
    await expect(title(page)).toHaveText('iPod');
    expect(await labels(page)).toEqual(['Playlists', 'Browse', 'Extras', 'Settings', 'Shuffle Songs']);
    await expect(page.locator('.ipod-lcd.fam-mono')).toBeVisible(); await expect(page.getByTestId('ipod-pane')).toHaveCount(0);
    const lcd = (await page.getByTestId('ipod-lcd').boundingBox())!; expect(lcd.width / lcd.height).toBeGreaterThan(1.1);

    await page.goto('about:blank'); await openIpod(page, { model: 'video5' });
    expect(await labels(page)).toEqual(['Music', 'Cover Flow', 'Extras', 'Settings', 'Shuffle Songs']);
    await expect(page.getByTestId('ipod-pane')).toHaveCount(0);       // the colour video menus have no preview

    await page.goto('about:blank'); await openIpod(page, { model: 'classic6' });
    expect(await labels(page)).toEqual(['Music', 'Cover Flow', 'Voice Memos', 'Settings', 'Shuffle Songs']);
    await expect(page.getByTestId('ipod-pane')).toBeVisible();        // nano / classic: the preview tile beside the main menu
    await page.getByTestId('ipod').focus();
    await page.keyboard.press('Enter');
    await expect(title(page)).toHaveText('Music');
    expect(await labels(page)).toEqual(['Playlists', 'Artists', 'Albums', 'Songs', 'Genres']);

    await page.goto('about:blank'); await openIpod(page, { model: 'ipod4' });
    await page.getByTestId('ipod').focus();
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
    await expect(title(page)).toHaveText('Browse');
    expect(await labels(page)).toEqual(['Artists', 'Albums', 'Songs', 'Genres']);
  });

  test('songs, the art pane, Now Playing with volume and scrub', async ({ page }) => {
    test.setTimeout(150_000);
    await signIn(page);
    await page.setViewportSize({ width: 700, height: 900 });
    await openIpod(page, { model: 'video5' });
    await page.getByTestId('ipod').focus();
    await page.keyboard.press('Enter');                               // Music
    await expect(title(page)).toHaveText('Music');
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown');
    await expect(sel(page)).toContainText('Songs');
    await page.keyboard.press('Enter');
    await expect(title(page)).toHaveText('Songs');
    await expect(page.getByTestId('ipod-pane')).toBeVisible();        // songs show their cover and details on the right
    await expect(sel(page)).toContainText('Shuffle');                 // the first row of a list is Shuffle
    await page.keyboard.press('ArrowDown');
    await expect(sel(page)).toContainText('Harbour');
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('ipod-np')).toBeVisible({ timeout: 20_000 });
    const song = await page.getByTestId('ipod-np-title').textContent();
    expect(song).toMatch(/Harbour/);
    await shot(page, 'now-playing-video');
    await expect(page.getByTestId('ipod-np-count')).toContainText('of');
    // the wheel is the volume first
    await dragWheel(page, 70);
    await expect(page.getByTestId('ipod-volume')).toBeVisible();
    // the middle button switches the wheel to scrubbing
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('ipod-scrub')).toBeVisible();
    await expect(page.getByTestId('ipod-volume')).toHaveCount(0);
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('ipod-scrub')).toHaveCount(0);
    // next on the ring moves to the next song; the bottom of the ring pauses
    await tapZone(page, 'next');
    await expect(page.getByTestId('ipod-np-title')).not.toHaveText(song!, { timeout: 10_000 });
    await tapZone(page, 'play');
    await tapZone(page, 'menu');
    await expect(page.getByTestId('ipod-np')).toHaveCount(0);
    await expect(title(page)).toHaveText('Songs');
  });

  test('Cover Flow, albums and artists', async ({ page }) => {
    test.setTimeout(150_000);
    await signIn(page);
    await page.setViewportSize({ width: 700, height: 900 });
    await openIpod(page, { model: 'classic6' });
    await page.getByTestId('ipod').focus();
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
    await expect(title(page)).toHaveText('Cover Flow');
    await expect(page.getByTestId('ipod-coverflow')).toBeVisible();
    await page.waitForTimeout(500); await shot(page, 'coverflow');
    const first = await page.getByTestId('ipod-cf-title').textContent();
    expect(await page.locator('.cf-item').count()).toBeGreaterThan(2);
    await expect(page.locator('.cf-item.mid')).toHaveCount(1);
    await page.keyboard.press('ArrowDown');
    await expect(page.getByTestId('ipod-cf-title')).not.toHaveText(first!);
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('ipod-coverflow')).toHaveCount(0);   // the middle album opened as a list of songs
    await expect(rows(page).first()).toBeVisible();
    await page.keyboard.press('Escape'); await expect(page.getByTestId('ipod-coverflow')).toBeVisible();
    await page.keyboard.press('Escape');
    // Music > Artists opens an artist (several albums: All Songs first)
    await page.keyboard.press('ArrowUp'); await page.keyboard.press('Enter');
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
    await expect(title(page)).toHaveText('Artists');
    await page.keyboard.press('Enter');
    await expect(rows(page).first()).toBeVisible();
  });

  test('settings: model, colour, look, controls, main menu and reset, kept in the browser', async ({ page }) => {
    test.setTimeout(180_000);
    await signIn(page);
    await page.setViewportSize({ width: 700, height: 900 });
    await openIpod(page, { model: 'ipod4' });
    await page.getByTestId('ipod').focus();
    // Settings (4th row of the mono menu) > Controls
    for (let i = 0; i < 3; i++) await page.keyboard.press('ArrowDown');
    await expect(sel(page)).toContainText('Settings'); await page.keyboard.press('Enter');
    await expect(title(page)).toHaveText('Settings');
    expect(await labels(page)).toEqual(['Appearance', 'Playback', 'Controls', 'Main Menu', 'About', 'Account', 'FLACie Settings', 'Reset Settings']);
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
    await expect(title(page)).toHaveText('Controls');
    await expect(sel(page)).toContainText('Wheel Speed'); await expect(sel(page).locator('.lcd-val')).toHaveText('Medium');
    await page.keyboard.press('Enter');
    await expect(sel(page).locator('.lcd-val')).toHaveText('Fast');   // each press goes to the next choice
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
    await expect(sel(page).locator('.lcd-val')).toHaveText('Sound');
    await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('flacie.ipod') || '{}'))).toMatchObject({ speed: 3, clicker: 1 });
    await page.keyboard.press('Escape');

    // Appearance > Model > iPod nano > 3rd gen: the screen changes family and the menu is rebuilt
    await page.keyboard.press('ArrowUp'); await page.keyboard.press('ArrowUp'); await page.keyboard.press('Enter');
    await expect(title(page)).toHaveText('Appearance');
    expect(await labels(page)).toEqual(['Model', 'Colour', 'Look', 'Display']);
    await expect(sel(page).locator('.lcd-val')).toHaveText('iPod (4th gen)');
    await page.keyboard.press('Enter');
    await expect(title(page)).toHaveText('Model');
    expect(await labels(page)).toEqual(['iPod / classic', 'iPod mini', 'iPod nano', 'iPod touch']);
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
    await expect(title(page)).toHaveText('iPod nano');
    expect(await labels(page)).toHaveLength(7);
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');   // nano 3rd gen
    await expect(stage(page)).toHaveAttribute('data-model', 'nano3');
    await expect(page.locator('.ipod.fam-nano')).toBeVisible();
    await expect(title(page)).toHaveText('Appearance');               // put back where the person was
    // Colour
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
    await expect(title(page)).toHaveText('Colour');
    expect(await labels(page)).toEqual(['Silver', 'Black', 'Blue', 'Green', 'Red']);
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
    await expect(stage(page)).toHaveAttribute('data-color', 'Black');
    await expect(page.locator('.ipod-lcd.pal-dark')).toBeVisible();   // a black nano has the dark screen
    await page.keyboard.press('Escape');
    // Look: wheel only (no body), and back
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
    await expect(stage(page)).toHaveAttribute('data-look', 'wheel');
    await page.keyboard.press('Enter');
    await expect(stage(page)).toHaveAttribute('data-look', 'device');
    await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('flacie.ipod') || '{}'))).toMatchObject({ model: 'nano3', color: 1 });

    // it is still there after a reload
    await page.reload();
    await expect(stage(page)).toHaveAttribute('data-model', 'nano3', { timeout: 20_000 });
    await expect(stage(page)).toHaveAttribute('data-color', 'Black');

    // Main Menu: hide "Voice Memos"; Reset brings everything back
    await page.getByTestId('ipod').focus();
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');    // Settings
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');    // Main Menu
    await expect(title(page)).toHaveText('Main Menu');
    expect(await labels(page)).toEqual(['Cover Flow', 'Voice Memos', 'Shuffle Songs']);
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
    await expect(sel(page).locator('.lcd-tick')).toHaveText('');
    await page.keyboard.press('Escape'); await page.keyboard.press('Escape');
    expect(await labels(page)).toEqual(['Music', 'Cover Flow', 'Settings', 'Shuffle Songs']);
    for (let i = 0; i < 4; i++) await page.keyboard.press('ArrowUp');          // the highlight stays where it was, so start from the top
    for (let i = 0; i < 2; i++) await page.keyboard.press('ArrowDown');
    await expect(sel(page)).toContainText('Settings'); await page.keyboard.press('Enter');
    for (let i = 0; i < 7; i++) await page.keyboard.press('ArrowDown');
    await expect(sel(page)).toContainText('Reset Settings'); await page.keyboard.press('Enter');
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');     // Reset
    await expect(stage(page)).toHaveAttribute('data-model', 'ipod4');
    await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('flacie.ipod') || '{}'))).toMatchObject({ model: 'ipod4', speed: 2, clicker: 0 });
  });

  test('the wheel: drag, tap zones, speed setting and a flick that coasts', async ({ page }) => {
    test.setTimeout(150_000);
    await signIn(page);
    await page.setViewportSize({ width: 700, height: 900 });
    await openIpod(page, { model: 'video5' });
    await page.getByTestId('ipod').focus();
    await page.keyboard.press('Enter');                               // Music
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');   // Songs
    await expect(title(page)).toHaveText('Songs');
    expect(await selText(page)).toBe('Shuffle');
    // Medium: 34 degrees a row, so 70 degrees is two rows
    await dragWheel(page, 70);
    await expect.poll(() => selText(page)).toBe('Harbour 19');
    await dragWheel(page, -35);
    await expect.poll(() => selText(page)).toBe('Harbour 11');
    // a quick flick goes on after the finger lifts: well beyond the rows the finger itself covered (about 3)
    await dragWheel(page, 160, 1, 0);          // lifted while still moving
    await page.waitForTimeout(900);
    const flicked = await selText(page);
    expect(['Harbour 11', 'Harbour 19', 'Harbour 27', 'Harbour 3']).not.toContain(flicked);
    // the centre taps like Enter; MENU goes back
    await tapZone(page, 'menu'); await expect(title(page)).toHaveText('Music');
    await tapZone(page, 'center'); await expect(title(page)).toHaveText('Songs');      // the highlight was kept on Songs
    await tapZone(page, 'menu'); await expect(title(page)).toHaveText('Music');
    // holding MENU goes home
    const b = (await page.getByTestId('ipod-wheel').boundingBox())!;
    await page.mouse.move(b.x + b.width / 2, b.y + b.height * 0.1); await page.mouse.down(); await page.waitForTimeout(800); await page.mouse.up();
    await expect(title(page)).toHaveText('iPod');
  });

  test('the picker panel changes the model, colour and look, and touch models show the player', async ({ page }) => {
    test.setTimeout(150_000);
    await signIn(page);
    await page.setViewportSize({ width: 800, height: 900 });
    await openIpod(page, { model: 'ipod4' });
    await page.getByTestId('ipod-gear').click();
    await page.locator('.ipod-panel .ipod-chip[data-model="nano4"]').click();
    await expect(stage(page)).toHaveAttribute('data-model', 'nano4');
    const l = (await page.getByTestId('ipod-lcd').boundingBox())!; expect(l.height).toBeGreaterThan(l.width * 1.1);   // the tall nano has a portrait screen
    await page.locator('.ipod-panel .ipod-chip.swatch').nth(2).click();
    await expect(stage(page)).toHaveAttribute('data-color', 'Purple');
    await page.locator('.ipod-panel .ipod-chip[data-look="wheel"]').click();
    await expect(stage(page)).toHaveAttribute('data-look', 'wheel');
    const l2 = (await page.getByTestId('ipod-lcd').boundingBox())!; expect(l2.width).toBeGreaterThan(l2.height);          // wheel only: the landscape screen
    await page.locator('.ipod-panel .ipod-chip[data-look="device"]').click();
    await page.locator('.ipod-panel .ipod-chip[data-model="touch5"]').click();
    await expect(page.getByTestId('ipod-touch-screen').locator('iframe')).toBeVisible();
    await expect(page.getByTestId('ipod-wheel')).toHaveCount(0);
    const frame = page.frameLocator('[data-testid=ipod-touch-screen] iframe');
    await expect(frame.locator('body')).toBeVisible({ timeout: 20_000 });
    await page.locator('.ipod-panel .ipod-chip[data-model="video5"]').click();    // back to a wheel
    await expect(page.getByTestId('ipod-wheel')).toBeVisible();
  });

  test('the device fits small windows and the black model loads', async ({ page }) => {
    await signIn(page);
    await page.setViewportSize({ width: 360, height: 640 });
    await openIpod(page, { model: 'classic6', color: 1 });
    await expect(page.getByTestId('ipod-device')).toBeVisible();
    await page.waitForTimeout(400);
    const b = (await page.getByTestId('ipod-device').boundingBox())!;
    expect(b.x).toBeGreaterThanOrEqual(0); expect(b.y).toBeGreaterThanOrEqual(0);
    expect(b.x + b.width).toBeLessThanOrEqual(360 + 1); expect(b.y + b.height).toBeLessThanOrEqual(640 + 1);
    await expect(stage(page)).toHaveAttribute('data-color', 'Black');
    await expect(page.locator('.ipod-lcd.pal-dark')).toBeVisible();
  });
});
