import { test, expect, Page } from '@playwright/test';

// The iPod mode (/ipod): menus by wheel, keys and mouse wheel, songs from the library, Now Playing with the volume overlay.
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

async function dragWheel(page: Page, degrees: number) {
  const b = (await page.getByTestId('ipod-wheel').boundingBox())!;
  const cx = b.x + b.width / 2, cy = b.y + b.height / 2, r = b.width * 0.38;
  const at = (a: number) => [cx + r * Math.cos((a * Math.PI) / 180), cy + r * Math.sin((a * Math.PI) / 180)] as const;
  const [sx, sy] = at(-30);
  await page.mouse.move(sx, sy); await page.mouse.down();
  const steps = Math.max(4, Math.abs(degrees) / 5);
  for (let i = 1; i <= steps; i++) { const [x, y] = at(-30 + (degrees * i) / steps); await page.mouse.move(x, y); await page.waitForTimeout(12); }
  await page.waitForTimeout(80); await page.mouse.up(); await page.waitForTimeout(150);
}

test.describe.serial('ipod mode', () => {
  test('menus, wheel, keys and playback', async ({ page }) => {
    test.setTimeout(150_000);
    await signIn(page);
    await page.setViewportSize({ width: 900, height: 900 });
    await page.goto('/ipod');
    await expect(rows(page)).toHaveCount(6, { timeout: 20_000 });
    await expect(title(page)).toHaveText('FLACie');
    await expect(sel(page)).toHaveText(/Music/);

    // keys: down twice, up once, then into Music and back out with Escape
    await page.getByTestId('ipod').focus();
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('ArrowUp');
    await expect(sel(page)).toHaveText(/Playlists/);
    await page.keyboard.press('ArrowUp');
    await page.keyboard.press('Enter');
    await expect(title(page)).toHaveText('Music');
    await expect(rows(page)).toHaveCount(4);
    await page.keyboard.press('Escape');
    await expect(title(page)).toHaveText('FLACie');
    await expect(sel(page)).toHaveText(/Music/);          // the place is kept

    // the on-screen wheel: 60 degrees clockwise is four detents of 15 degrees, so the highlight goes down by four
    await page.getByTestId('ipod-center').click();         // Music
    await expect(title(page)).toHaveText('Music');
    await dragWheel(page, 45);
    await expect(sel(page)).toHaveText(/Favorites/);       // 3 detents: Artists -> Albums -> Songs -> Favorites
    await dragWheel(page, -30);
    await expect(sel(page)).toHaveText(/Albums/);
    await page.mouse.move(450, 300); await page.mouse.wheel(0, 100);
    await expect(sel(page)).toHaveText(/Songs/);
    await page.getByTestId('ipod-menu').click();
    await expect(title(page)).toHaveText('FLACie');

    // a song from Music > Songs
    await page.getByTestId('ipod-center').click();
    await expect(title(page)).toHaveText('Music');          // each step is awaited: a key sent before the last one is drawn would act on the old menu
    await page.keyboard.press('ArrowDown'); await expect(sel(page)).toHaveText(/Albums/);
    await page.keyboard.press('ArrowDown'); await expect(sel(page)).toHaveText(/Songs/);
    await page.keyboard.press('Enter');
    await expect(title(page)).toHaveText('Songs');
    await expect(sel(page)).toHaveText(/Harbour/);
    await expect(rows(page).first()).toBeVisible();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('ipod-np')).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId('ipod-np-title')).not.toHaveText('');

    // on Now Playing the wheel is the volume
    await dragWheel(page, 45);
    await expect(page.getByTestId('ipod-volume')).toBeVisible();
    await page.getByTestId('ipod-menu').click();
    await expect(page.getByTestId('ipod-np')).toHaveCount(0);
    await expect(title(page)).toHaveText('Songs');

    await page.mouse.move(5, 5); await page.waitForTimeout(500);
    await page.screenshot({ path: '../../../docs/img/ipod-web.png' });
  });

  test('the device fits small windows and the black model loads', async ({ page }) => {
    await signIn(page);
    await page.setViewportSize({ width: 360, height: 640 });
    await page.goto('/ipod?c=black');
    await expect(rows(page)).toHaveCount(6, { timeout: 20_000 });
    await expect(page.getByTestId('ipod-device')).toBeVisible();
    await page.waitForTimeout(300);   // the fit script has run
    const b = (await page.getByTestId('ipod-device').boundingBox())!;
    expect(b.x).toBeGreaterThanOrEqual(0); expect(b.y).toBeGreaterThanOrEqual(0);
    expect(b.x + b.width).toBeLessThanOrEqual(360 + 1); expect(b.y + b.height).toBeLessThanOrEqual(640 + 1);
    await expect(page.locator('.ipod-black')).toBeVisible();
  });
});
