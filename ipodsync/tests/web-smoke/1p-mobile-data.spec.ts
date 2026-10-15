import { test, expect, Page } from '@playwright/test';

// On mobile data (or with Data Saver on) nothing downloads a song twice behind your back. The live graphs in the Info tab and the iPod desk's earbuds read
// a silent second copy of the song (an analyser twin), so: the Info tab's live graphs wait for "Show anyway", and the desk's earbuds keep a calm pulse.
// On Wi-Fi and on a desktop everything is as before.
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
/** A phone on mobile data (what Chrome on Android reports), and a count of the analyser twins the page opens. */
async function onMobileData(page: Page, cellular: boolean) {
  await page.addInitScript((cell) => {
    if (cell) Object.defineProperty(navigator, 'connection', { value: { type: 'cellular', saveData: false, addEventListener() { }, removeEventListener() { } }, configurable: true });
    (window as any).twins = 0;
    const AC = window.AudioContext; const make = AC.prototype.createMediaElementSource;
    AC.prototype.createMediaElementSource = function (this: AudioContext, el: HTMLMediaElement) { (window as any).twins++; return make.call(this, el); };
  }, cellular);
}
async function playAndOpenInfo(page: Page) {
  await page.goto('/songs');
  await page.locator('.track', { has: page.locator('.track-title', { hasText: /^Velvet 1$|^Paper 2$|^Lantern \d+$/ }) }).first().locator('button.track-main').click();
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await page.getByRole('tab', { name: 'Info' }).click();
  await expect(page.locator('.info .graph-box')).toBeVisible({ timeout: 20_000 });
}

test('on mobile data the Info tab\'s live graphs wait for "Show anyway"', async ({ page }) => {
  await onMobileData(page, true);
  await signIn(page);
  await playAndOpenInfo(page);
  await expect(page.locator('.viz-off')).toContainText('mobile data');
  await expect(page.locator('canvas#viz')).toHaveCount(0);
  await page.waitForTimeout(1000);
  expect(await page.evaluate(() => (window as any).twins)).toBe(0);
  await page.getByRole('button', { name: 'Show anyway' }).click();
  await expect(page.locator('canvas#viz')).toBeVisible();
  await expect.poll(() => page.evaluate(() => (window as any).twins)).toBe(1);
});

test('on Wi-Fi the Info tab\'s live graphs start at once', async ({ page }) => {
  await onMobileData(page, false);
  await signIn(page);
  await playAndOpenInfo(page);
  await expect(page.locator('canvas#viz')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Show anyway' })).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => (window as any).twins)).toBe(1);
});

test('on mobile data the iPod desk plays without a second copy of the song', async ({ page }) => {
  await onMobileData(page, true);
  await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
  await page.addInitScript(() => { try { localStorage.setItem('flacie.ipod', JSON.stringify({ model: 'classic6', look: 2 })); } catch { } });
  await page.goto('/ipod');
  await expect(page.getByTestId('desk')).toBeVisible({ timeout: 20_000 });
  await page.getByTestId('ipod-keys').focus();
  for (let i = 0; i < 10; i++) await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('ipod-np')).toBeVisible({ timeout: 20_000 });
  await page.waitForTimeout(2000);
  expect(await page.evaluate(() => (window as any).twins)).toBe(0);
  expect(await page.evaluate(() => (window as any).desk.loops())).toBe(1);   // the earbuds still move (the calm pulse)
});
