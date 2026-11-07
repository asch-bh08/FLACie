import { test, expect, Page } from '@playwright/test';

// Settings → Playback has the player's Sound choices too (Even out volume, Fade between songs), as the phone's Settings does: before, they were
// only in Now Playing's Sound menu, so someone looking in Settings never found them. Both are the same setting, kept in this browser.

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

test('Settings → Playback sets Even out volume and Fade between songs, and keeps them', async ({ page }) => {
  test.setTimeout(90_000);
  await signIn(page);
  await page.goto('/settings');
  await page.waitForTimeout(2500);
  const playback = page.locator('#playback');
  const level = playback.getByRole('switch', { name: /Even out volume/ });
  await expect(level).not.toBeChecked();
  await level.click();
  await expect(level).toBeChecked();
  const fade = playback.getByRole('group', { name: 'Fade between songs' });
  await expect(fade.getByRole('button', { name: 'Off' })).toHaveAttribute('aria-pressed', 'true');
  await fade.getByRole('button', { name: '4 s' }).click();
  await expect(fade.getByRole('button', { name: '4 s' })).toHaveAttribute('aria-pressed', 'true');

  // kept in this browser: a fresh page shows both as chosen before any song has played
  await page.reload();
  await page.waitForTimeout(2500);
  await expect(page.locator('#playback').getByRole('switch', { name: /Even out volume/ })).toBeChecked();
  await expect(page.locator('#playback').getByRole('group', { name: 'Fade between songs' }).getByRole('button', { name: '4 s' })).toHaveAttribute('aria-pressed', 'true');
  await page.locator('#playback').screenshot({ path: 'test-results/settings-sound.png' });

  // the settings finder knows them
  await page.getByRole('searchbox', { name: 'Find a setting' }).fill('fade');
  await expect(page.getByRole('heading', { name: 'Playback', exact: true })).toBeVisible();

  // back to the defaults, so later specs start from Off
  await page.getByRole('searchbox', { name: 'Find a setting' }).fill('');
  await page.locator('#playback').getByRole('switch', { name: /Even out volume/ }).click();
  await page.locator('#playback').getByRole('group', { name: 'Fade between songs' }).getByRole('button', { name: 'Off' }).click();
  await expect(page.locator('#playback').getByRole('switch', { name: /Even out volume/ })).not.toBeChecked();
});

// the phone's "Volume limit" on the web too (owner, 7 Nov: everything on the phone on the web): the player never goes above it, and it is kept
test('Settings → Playback has Volume limit: the player stays under it and it is kept in this browser', async ({ page }) => {
  test.setTimeout(90_000);
  await signIn(page);
  await page.goto('/settings');
  await page.waitForTimeout(2500);
  const limit = page.locator('#playback').getByRole('group', { name: 'Volume limit' });
  await expect(limit.getByRole('button', { name: 'Off' })).toHaveAttribute('aria-pressed', 'true');
  await limit.getByRole('button', { name: '50%' }).click();
  await expect(limit.getByRole('button', { name: '50%' })).toHaveAttribute('aria-pressed', 'true');
  // the slider at full: the music is still at half
  expect(await page.evaluate(() => { (window as any).flacie.volume(1); return (window as any).flacie.loud(); })).toBeCloseTo(0.5, 2);
  await page.reload();
  await page.waitForTimeout(2500);
  await expect(page.locator('#playback').getByRole('group', { name: 'Volume limit' }).getByRole('button', { name: '50%' })).toHaveAttribute('aria-pressed', 'true');
  expect(await page.evaluate(() => { (window as any).flacie.volume(1); return (window as any).flacie.loud(); })).toBeCloseTo(0.5, 2);
  // back off: the full volume again
  await page.locator('#playback').getByRole('group', { name: 'Volume limit' }).getByRole('button', { name: 'Off' }).click();
  expect(await page.evaluate(() => (window as any).flacie.loud())).toBeCloseTo(1, 2);
});
