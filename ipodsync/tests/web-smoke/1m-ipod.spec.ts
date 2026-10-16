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
// the screenshots in docs/img are only rewritten on request (IPOD_SHOTS=1), so a plain test run leaves the repository clean
const SHOTS = !!process.env.IPOD_SHOTS;
const shot = async (page: Page, name: string) => { if (!SHOTS) return; await page.mouse.move(5, 5); await page.waitForTimeout(600); await page.screenshot({ path: `../../../docs/img/ipod-web-${name}.png` }); };
/** Stop the song (the mock's last three seconds, and a new one would start the rating again at none): Space until the title bar shows "paused", and it stays so for a moment. */
async function holdStill(page: Page) {
  const state = () => page.locator('[data-testid=ipod-header] .lcd-play').first().textContent();
  for (let i = 0; i < 8; i++) {
    if ((await state())?.includes('❚❚')) { await page.waitForTimeout(500); if ((await state())?.includes('❚❚')) return; }
    else await page.keyboard.press('Space');
    await page.waitForTimeout(350);
  }
  throw new Error('could not pause the song');
}
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
      if (fam === 'touch') { await expect(page.getByTestId('ios')).toBeVisible(); await expect(page.getByTestId('ios-home')).toBeVisible(); }
      else { await expect(page.getByTestId('ipod-lcd')).toBeVisible(); await expect(rows(page).first()).toBeVisible(); await expect(page.getByTestId('ipod-wheel')).toBeVisible(); }
      await page.getByTestId('ipod-gear').click();
      await expect(page.locator('.ipod-panel .ipod-chip.swatch')).toHaveCount(+colours);
      await page.getByTestId('ipod-gear').click();
      if (SHOTS && shots.has(id)) { await page.mouse.move(5, 5); await page.waitForTimeout(500); await page.screenshot({ path: `../../../docs/img/ipod-web-${id}.png` }); }
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
    expect(await labels(page)).toEqual(['Music', 'Cover Flow', 'Extras', 'Settings', 'Shuffle Songs']);
    await expect(page.getByTestId('ipod-pane')).toBeVisible();        // nano / classic: the preview tile beside the main menu
    await page.getByTestId('ipod-keys').focus();
    await page.keyboard.press('Enter');
    await expect(title(page)).toHaveText('Music');
    expect(await labels(page)).toEqual(['Playlists', 'Artists', 'Albums', 'Songs', 'Genres']);

    await page.goto('about:blank'); await openIpod(page, { model: 'ipod4' });
    await page.getByTestId('ipod-keys').focus();
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
    await expect(title(page)).toHaveText('Browse');
    expect(await labels(page)).toEqual(['Artists', 'Albums', 'Songs', 'Genres']);
  });

  test('songs, the art pane, Now Playing with volume and scrub', async ({ page }) => {
    test.setTimeout(150_000);
    await signIn(page);
    await page.setViewportSize({ width: 700, height: 900 });
    await openIpod(page, { model: 'video5' });
    await page.getByTestId('ipod-keys').focus();
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
    await page.getByTestId('ipod-keys').focus();
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
    await page.getByTestId('ipod-keys').focus();
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

    // Main Menu: hide "Extras"; Reset brings everything back
    await page.getByTestId('ipod-keys').focus();
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');    // Settings
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');    // Main Menu
    await expect(title(page)).toHaveText('Main Menu');
    expect(await labels(page)).toEqual(['Cover Flow', 'Extras', 'Shuffle Songs']);
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
    await page.getByTestId('ipod-keys').focus();
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
    await page.locator('.ipod-panel [data-group="nano"]').click();
    await page.locator('.ipod-panel .ipod-chip[data-model="nano4"]').click();
    await expect(stage(page)).toHaveAttribute('data-model', 'nano4');
    const l = (await page.getByTestId('ipod-lcd').boundingBox())!; expect(l.height).toBeGreaterThan(l.width * 1.1);   // the tall nano has a portrait screen
    await page.locator('.ipod-panel .ipod-chip.swatch').nth(2).click();
    await expect(stage(page)).toHaveAttribute('data-color', 'Purple');
    await page.locator('.ipod-panel .ipod-chip[data-look="wheel"]').click();
    await expect(stage(page)).toHaveAttribute('data-look', 'wheel');
    const l2 = (await page.getByTestId('ipod-lcd').boundingBox())!; expect(l2.width).toBeGreaterThan(l2.height);          // wheel only: the landscape screen
    await page.locator('.ipod-panel .ipod-chip[data-look="device"]').click();
    await page.locator('.ipod-panel [data-group="touch"]').click();
    await page.locator('.ipod-panel .ipod-chip[data-model="touch5"]').click();
    await expect(page.getByTestId('ios')).toBeVisible();
    await expect(page.getByTestId('ipod-wheel')).toHaveCount(0);
    await page.locator('.ipod-panel [data-group="Classic"]').click();
    await page.locator('.ipod-panel .ipod-chip[data-model="video5"]').click();    // back to a wheel
    await expect(page.getByTestId('ipod-wheel')).toBeVisible();
  });

  test('touch iPods run their own system: home screen, Music with tabs, Now Playing, Settings, Clock, Stopwatch', async ({ page }) => {
    test.setTimeout(180_000);
    await signIn(page);
    await page.setViewportSize({ width: 1366, height: 768 });
    for (const model of ['touch5', 'touch1', 'nano7']) {
      await page.goto('about:blank');
      await openIpod(page, { model });
      const app = () => page.getByTestId('ios').getAttribute('data-app');
      await expect.poll(app).toBe('home');
      await expect(page.locator('.ios-app[data-app="music"]').first()).toBeVisible();
      await page.locator('.ios-grid .ios-app[data-app="music"]').click();
      await expect.poll(app).toBe('music');
      await expect(page.getByTestId('ios-tabs')).toBeVisible();
      // Songs tab is the default: rows with covers, titles and artists
      await expect(page.getByTestId('ios-row').first()).toBeVisible();
      // a mouse drags the list like a finger: it scrolls, and the drag is not a tap on the row under it
      {
        const list = page.getByTestId('ios-list'); const lb = (await list.boundingBox())!;
        const x = lb.x + lb.width / 2, y = lb.y + lb.height * 0.8;
        await page.mouse.move(x, y); await page.mouse.down(); await page.mouse.move(x, y - 40, { steps: 4 }); await page.mouse.move(x, y - lb.height * 0.6, { steps: 8 }); await page.mouse.up();
        await expect.poll(() => list.evaluate((e) => e.scrollTop)).toBeGreaterThan(40);
        expect(await app()).toBe('music');
        await list.evaluate((e) => { e.scrollTop = 0; });
      }
      await page.locator('[data-tab="Artists"]').click();
      await expect(page.locator('.ios-nav b')).toHaveText('Artists');
      await page.getByTestId('ios-row').first().click();                    // an artist opens (its albums or its songs)
      await expect(page.getByTestId('ios-back')).toBeVisible();
      await page.getByTestId('ios-back').click();
      await expect(page.locator('.ios-nav b')).toHaveText('Artists');
      await page.locator('[data-tab="Songs"]').click();
      await expect(page.locator('.ios-nav b')).toHaveText('Songs');
      await page.getByTestId('ios-row').nth(1).click();                      // play a song: Now Playing opens
      await expect.poll(app, { timeout: 20_000 }).toBe('now');
      await expect(page.getByTestId('ios-np-title')).not.toHaveText('');
      await page.getByTestId('ios-play').click();                            // pause, then play again
      await page.getByTestId('ios-play').click();
      await page.getByTestId('ios-scrub').click({ position: { x: 40, y: 4 } });
      await page.getByTestId('ios-back').click();
      await expect.poll(app).toBe('music');
      // Settings: a row cycles its setting
      await page.getByTestId('ios-now').click(); await expect.poll(app).toBe('now');
      await page.keyboard.press('Escape'); await page.keyboard.press('Escape');
      await page.getByTestId('ipod-keys').focus();
      await page.keyboard.press('Escape'); await page.keyboard.press('Escape');
      await expect.poll(app).toBe('home');
      await page.locator('.ios-grid .ios-app[data-app="settings"]').click();
      await expect(page.getByTestId('ios-settings')).toBeVisible();
      const shuffle = page.getByTestId('ios-setting').first();
      await expect(shuffle.locator('.ios-v')).toHaveText(/Off|Songs/);
      const before = await shuffle.locator('.ios-v').textContent();
      await shuffle.click();
      await expect(shuffle.locator('.ios-v')).not.toHaveText(before!);
      await page.getByTestId('ios-back').click();
      // Clock and Stopwatch
      await page.locator('.ios-grid .ios-app[data-app="clock"]').click();
      await expect(page.getByTestId('ios-clock')).toBeVisible(); await page.getByTestId('ios-back').click();
      await page.locator('.ios-grid .ios-app[data-app="stopwatch"]').click();
      await expect(page.getByTestId('ios-sw-time')).toHaveText('00:00.00');
      await page.getByTestId('ios-sw-go').click(); await page.waitForTimeout(700);
      await page.getByTestId('ios-sw-go').click();
      await expect(page.getByTestId('ios-sw-time')).not.toHaveText('00:00.00');
      await page.getByTestId('ios-sw-reset').click(); await expect(page.getByTestId('ios-sw-time')).toHaveText('00:00.00');
      await page.getByTestId('ios-back').click();
      if (model === 'touch5') { await page.locator('.ios-grid .ios-app[data-app="music"]').click(); await page.getByTestId('ipod-home-button').click(); await expect.poll(app).toBe('home'); }   // the Home button
    }
  });

  test('Extras: clock and stopwatch; Now Playing has a rating mode', async ({ page }) => {
    test.setTimeout(150_000);
    await signIn(page);
    await page.setViewportSize({ width: 1200, height: 800 });
    for (const model of ['ipod4', 'video5', 'classic6']) {
      await page.goto('about:blank');
      await openIpod(page, { model });
      await page.getByTestId('ipod-keys').focus();
      const mono = model === 'ipod4';
      for (let i = 0; i < (mono ? 2 : 2); i++) await page.keyboard.press('ArrowDown');
      await expect(sel(page)).toContainText('Extras'); await page.keyboard.press('Enter');
      await expect(title(page)).toHaveText('Extras');
      expect(await labels(page)).toEqual(['Clock', 'Stopwatch', 'Voice Memos']);
      await page.keyboard.press('Enter');
      await expect(title(page)).toHaveText('Clock');
      await expect(page.getByTestId('ipod-tool-time')).toHaveText(/\d{1,2}:\d{2}/);
      await page.keyboard.press('Escape'); await expect(title(page)).toHaveText('Extras');
      await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
      await expect(title(page)).toHaveText('Stopwatch');
      await expect(page.getByTestId('ipod-tool-time')).toHaveText('00:00.00');
      await page.keyboard.press('Enter'); await page.waitForTimeout(800); await page.keyboard.press('Enter');     // start, stop
      await expect(page.getByTestId('ipod-tool-time')).not.toHaveText('00:00.00');
      await page.keyboard.press('ArrowLeft');                                                                      // reset
      await expect(page.getByTestId('ipod-tool-time')).toHaveText('00:00.00');
      await page.keyboard.press('Escape'); await expect(title(page)).toHaveText('Extras');
      await page.keyboard.press('Escape');
      // Now Playing: the middle button goes volume -> scrub -> stars -> volume; the wheel sets the stars
      await page.keyboard.press('ArrowUp'); await page.keyboard.press('ArrowUp'); await page.keyboard.press('ArrowUp');
      await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown');
      await expect(sel(page)).toContainText('Shuffle Songs'); await page.keyboard.press('Enter');
      await expect(page.getByTestId('ipod-np')).toBeVisible({ timeout: 20_000 });
      await holdStill(page);
      await page.keyboard.press('Enter'); await expect(page.getByTestId('ipod-scrub')).toBeVisible();
      await page.keyboard.press('Enter'); await expect(page.getByTestId('ipod-rating')).toBeVisible();
      await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown');
      await expect(page.getByTestId('ipod-rating')).toContainText('★★★☆☆');
      await page.keyboard.press('ArrowUp'); await expect(page.getByTestId('ipod-rating')).toContainText('★★☆☆☆');
      await page.keyboard.press('Enter'); await expect(page.getByTestId('ipod-volume')).toBeVisible();
    }
  });

  test('every iPod is laid out cleanly at every window size and shape', async ({ page }) => {
    test.setTimeout(600_000);
    await signIn(page);
    const sizes: Array<[number, number]> = [[1920, 1080], [1366, 768], [1024, 768], [768, 1024], [390, 844], [360, 640], [320, 568], [2560, 1080], [1200, 400], [900, 300], [500, 500]];
    const models = ['ipod1', 'mini', 'video5', 'nano2', 'classic6', 'nano4', 'nano6', 'touch5', 'nano7'];
    // the screen's text must be inside the screen, the device inside the free area, and nothing may scroll sideways
    const problems = async (touch: boolean) => page.evaluate((isTouch) => {
      const out: string[] = [];
      const box = (e: Element) => e.getBoundingClientRect();
      // the rectangle the letters really occupy (an element can be wider than its text)
      const ink = (e: Element) => { if (e.matches('.ios-back, .ios-now')) return box(e); const r = document.createRange(); r.selectNodeContents(e); return r.getBoundingClientRect(); };
      const area = box(document.querySelector('[data-testid=ipod-keys]')!), dev = box(document.querySelector('[data-testid=ipod-device]')!);
      if (dev.left < area.left - 1 || dev.right > area.right + 1 || dev.top < area.top - 1 || dev.bottom > area.bottom + 1) out.push('device outside the free area');
      if (dev.width < 40 || dev.height < 60) out.push('device tiny');
      const root = document.querySelector(isTouch ? '[data-testid=ios]' : '.lcd-in')!, screen = box(document.querySelector(isTouch ? '[data-testid=ipod-touch-screen]' : '[data-testid=ipod-lcd]')!);
      for (const e of root.querySelectorAll('*')) {
        if (e.closest('.cf-item, .ios-list, .ios-settings, svg')) continue;
        const own = [...e.childNodes].some((n) => n.nodeType === 3 && (n.textContent || '').trim().length > 0);
        if (!own) continue;
        const r = ink(e);
        if (r.width === 0 || r.height === 0) continue;
        if (r.left < screen.left - 1.5 || r.right > screen.right + 1.5 || r.top < screen.top - 1.5 || r.bottom > screen.bottom + 1.5) out.push('text outside the screen: ' + (e.textContent || '').trim().slice(0, 24));
      }
      const hasText = (e: Element) => [...e.childNodes].some((n) => n.nodeType === 3 && (n.textContent || '').trim().length > 0);
      const texts = [...root.querySelectorAll('*')].filter((e) => !e.closest('.cf-item, svg') && (e.matches('.ios-back, .ios-now') || hasText(e)) && box(e).width > 0);
      for (let i = 0; i < texts.length; i++) for (let j = i + 1; j < texts.length; j++) {
        if (texts[i].contains(texts[j]) || texts[j].contains(texts[i])) continue;
        const a = ink(texts[i]), b = ink(texts[j]); const w = Math.min(a.right, b.right) - Math.max(a.left, b.left), h = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top);
        if (w > 3 && h > 3) out.push('text on text: ' + (texts[i].textContent || '').trim().slice(0, 16) + ' / ' + (texts[j].textContent || '').trim().slice(0, 16));
      }
      if (document.documentElement.scrollWidth > innerWidth + 1) out.push('page scrolls sideways');
      return out;
    }, touch);
    for (const model of models) {
      for (const [w, h] of sizes) {
        await page.setViewportSize({ width: w, height: h });
        await page.goto('about:blank');
        await openIpod(page, { model });
        await page.waitForTimeout(250);
        const touch = model === 'touch5' || model === 'nano7' || model === 'nano6';
        const bad = await problems(touch);
        expect(bad, model + ' at ' + w + 'x' + h).toEqual([]);
        if (w === 1366 || w === 390 || w === 320) {
          // the song screen, Now Playing and the settings page too
          if (touch) { await page.locator('.ios-grid .ios-app[data-app="music"]').click(); await page.getByTestId('ios-row').nth(1).click(); await expect(page.getByTestId('ios-np')).toBeVisible({ timeout: 20_000 }); }
          else {
            await page.getByTestId('ipod-keys').focus();
            for (let i = 0; i < 5; i++) await page.keyboard.press('ArrowDown');
            await expect(sel(page)).toContainText('Shuffle Songs'); await page.keyboard.press('Enter');
            await expect(page.getByTestId('ipod-np')).toBeVisible({ timeout: 20_000 });
          }
          await page.waitForTimeout(300);
          expect(await problems(touch), model + ' playing at ' + w + 'x' + h).toEqual([]);
          if (!touch) {
            // and the settings pages: long names beside their values
            await page.keyboard.press('Escape');
            for (let i = 0; i < 6; i++) await page.keyboard.press('ArrowUp');
            for (let i = 0; i < 3; i++) await page.keyboard.press('ArrowDown');
            await expect(sel(page)).toContainText('Settings'); await page.keyboard.press('Enter');
            await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
            await expect(title(page)).toHaveText('Playback'); await page.waitForTimeout(300);
            expect(await problems(false), model + ' playback settings at ' + w + 'x' + h).toEqual([]);
            await page.keyboard.press('Escape'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
            await expect(title(page)).toHaveText('Controls'); await page.waitForTimeout(300);
            expect(await problems(false), model + ' controls at ' + w + 'x' + h).toEqual([]);
          }
        }
      }
    }
    // the wheel-only look (no body: the screen across the top, the wheel under it) at the same shapes
    for (const model of ['ipod1', 'classic6', 'nano4']) {
      for (const [w, h] of sizes) {
        await page.setViewportSize({ width: w, height: h });
        await page.goto('about:blank');
        await openIpod(page, { model, look: 0 });
        await page.waitForTimeout(250);
        await expect(stage(page)).toHaveAttribute('data-look', 'wheel');
        expect(await problems(false), model + ' wheel only at ' + w + 'x' + h).toEqual([]);
      }
    }
  });

  test('cover art shows: in the lists, the pane, Now Playing, Cover Flow and the panel', async ({ page }) => {
    test.setTimeout(150_000);
    await signIn(page);
    await page.setViewportSize({ width: 1366, height: 768 });
    await openIpod(page, { model: 'video5' });
    await page.getByTestId('ipod-keys').focus();
    await page.keyboard.press('Enter');
    for (let i = 0; i < 3; i++) await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Enter'); await page.keyboard.press('ArrowDown');
    await expect(page.getByTestId('ipod-pane')).toBeVisible();
    // every cover on the screen asks for a real picture (a key from the library, never the name of a variable) and gets one
    const covers = async (scope: string) => page.evaluate((sel) => [...document.querySelectorAll(sel + ' img')].map((i) => ({ src: i.getAttribute('src') || '', ok: (i as HTMLImageElement).naturalWidth > 0 })), scope);
    await expect.poll(async () => (await covers('.ipod-lcd')).length).toBeGreaterThan(0);
    for (const c of await covers('.ipod-lcd')) expect(c.src).toMatch(/^\/art\/[^?]+\?v=3/), expect(c.src).not.toContain('ArtKey');
    await expect.poll(async () => (await covers('.ipod-lcd')).every((c) => c.ok)).toBe(true);
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('ipod-np')).toBeVisible({ timeout: 20_000 });
    await expect.poll(async () => { const c = await covers('.lnp-art'); return c.length > 0 && c.every((x) => x.ok && !x.src.includes('ArtKey')); }).toBe(true);
    // the panel shows the same cover, big
    await expect(page.getByTestId('ipod-dock-np')).toBeVisible();
    await expect.poll(async () => { const c = await covers('.dock-cover'); return c.length > 0 && c.every((x) => x.ok); }).toBe(true);
    // and Cover Flow
    await page.keyboard.press('Escape'); await page.keyboard.press('Escape'); await page.keyboard.press('Escape');
    await page.keyboard.press('ArrowUp');
    await page.goto('about:blank'); await openIpod(page, { model: 'classic6' });
    await page.getByTestId('ipod-keys').focus(); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
    await expect(page.getByTestId('ipod-coverflow')).toBeVisible();
    await expect.poll(async () => { const c = await covers('.cf'); return c.length > 1 && c.every((x) => x.ok && !x.src.includes('ArtKey')); }).toBe(true);
  });

  test('the device fills the free space in both looks, the panel is there on a wide window and the keys survive a click on it', async ({ page }) => {
    test.setTimeout(150_000);
    await signIn(page);
    await page.setViewportSize({ width: 1366, height: 768 });
    await openIpod(page, { model: 'classic6' });
    await expect(page.getByTestId('ipod-panel')).toHaveClass(/open/);                      // wide window: the panel is open
    const area = async () => (await page.getByTestId('ipod-keys').boundingBox())!;
    const dev = async () => (await page.getByTestId('ipod-device').boundingBox())!;
    const fitted = async (label: string) => {
      await page.waitForTimeout(500);
      const a = await area(), d = await dev();
      expect(d.height, label + ' fills the height').toBeGreaterThan(a.height * 0.85); expect(d.height, label + ' fits the height').toBeLessThanOrEqual(a.height);
      expect(d.width, label + ' fits the width').toBeLessThanOrEqual(a.width);
      expect(d.x, label + ' stays inside').toBeGreaterThanOrEqual(a.x - 1); expect(d.x + d.width).toBeLessThanOrEqual(a.x + a.width + 1);
      return d.height;
    };
    const h1 = await fitted('device');
    await page.locator('.ipod-panel [data-look="wheel"]').click();
    const h2 = await fitted('wheel only');
    await page.locator('.ipod-panel [data-look="device"]').click();
    const h3 = await fitted('device again');
    expect(Math.abs(h3 - h1), 'going back to the device gives the same size as the first time').toBeLessThan(2);
    expect(Math.abs(h2 - h1)).toBeLessThan(h1 * 0.15);
    // a click on the panel does not take the keys away (and a focused button must not act on Enter as well)
    await page.locator('.ipod-panel .ipod-chip.swatch').nth(1).click();
    await expect(stage(page)).toHaveAttribute('data-color', 'Black');
    await page.keyboard.press('Enter');
    await expect(title(page)).toHaveText('Music');                                       // once, not twice
    // narrow window: the panel starts closed and the gear opens it
    await page.setViewportSize({ width: 600, height: 800 });
    await page.reload();
    await expect(stage(page)).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId('ipod-panel')).not.toHaveClass(/open/);
    await page.getByTestId('ipod-gear').click();
    await expect(page.getByTestId('ipod-panel')).toHaveClass(/open/);
    const b = (await page.getByTestId('ipod-device').boundingBox())!; expect(b.x).toBeGreaterThanOrEqual(0); expect(b.x + b.width).toBeLessThanOrEqual(601);
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
