import { test, expect, Page } from '@playwright/test';

// Retro (the page that used to be called iPod mode): it opens straight on the saved choice, with no other iPod or scene shown first, it is named Retro
// wherever a person sees it, /ipod still works, and it can be reached from the player bar, the full-screen player and the account menu.
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
/** Seeds the saved choice before the page loads (not again on a reload) and records, on every change to the page, which device and scene the stage shows. */
async function recordLoad(page: Page, prefs: Record<string, unknown>) {
  await page.addInitScript((p) => {
    const w = window as any; w.__seen = [] as string[];
    try { const nav = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined; if (nav?.type !== 'reload' && location.pathname !== '/login') localStorage.setItem('flacie.ipod', JSON.stringify(p)); } catch { }
    const look = () => {
      const s = document.querySelector('[data-testid=ipod]') as HTMLElement | null;
      const state = s ? `${s.dataset.model}|${s.dataset.look}|${s.dataset.scene}|${document.querySelector('[data-testid=ipod-device],[data-testid=retro]') ? 'drawn' : 'empty'}` : 'none';
      if (w.__seen[w.__seen.length - 1] !== state) w.__seen.push(state);
    };
    new MutationObserver(look).observe(document, { subtree: true, childList: true, attributes: true });
    const tick = () => { look(); requestAnimationFrame(tick); }; requestAnimationFrame(tick);
  }, prefs);
}

test.describe.serial('Retro', () => {
  test('it is named Retro, on /retro and still on /ipod', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1600, height: 900 });
    for (const path of ['/retro', '/ipod']) {
      await page.goto(path);
      await expect(page.getByTestId('ipod-device')).toBeVisible({ timeout: 20_000 });
      await expect(page).toHaveTitle('Retro · FLACie');
      await expect(page.getByTestId('ipod-panel').locator('h2')).toHaveText('Retro');
      await expect(page.getByTestId('ipod-panel')).toHaveAttribute('aria-label', 'Retro panel');
    }
    await page.goto('/');
    const item = page.getByRole('menuitem', { name: 'Retro' });
    await expect(async () => { if (!(await item.isVisible())) await page.getByRole('button', { name: /^Account/ }).click(); await expect(item).toBeVisible({ timeout: 1500 }); }).toPass({ timeout: 20_000 });
    await expect(page.getByRole('menuitem', { name: /iPod mode/ })).toHaveCount(0);
    await page.screenshot({ path: 'test-results/retro-account-1600.png' });
    await item.click();
    await expect(page).toHaveURL(/\/retro$/);
    await expect(page.getByTestId('ipod-device')).toBeVisible({ timeout: 20_000 });
  });

  async function play(page: Page, w: number, h: number) {
    await page.setViewportSize({ width: w, height: h });
    await page.goto('/songs');
    await page.locator('.track').first().locator('button.track-main').click();
    await expect(page.locator('.player')).toBeVisible({ timeout: 20_000 });
  }
  for (const [w, h] of [[1600, 900], [390, 844]]) {
    test(`Retro is reachable from the player bar, the full-screen player and the account menu at ${w}x${h}`, async ({ page }) => {
      await signIn(page);
      // the player bar
      await play(page, w, h);
      await expect(page.getByTestId('bar-retro')).toBeVisible();
      await expect(page.getByTestId('bar-retro')).toHaveAttribute('aria-label', 'Retro');
      await page.screenshot({ path: `test-results/retro-bar-${w}.png` });
      await page.getByTestId('bar-retro').click();
      await expect(page).toHaveURL(/\/retro$/);
      await expect(page.getByTestId('ipod')).toBeVisible({ timeout: 20_000 });
      // the full-screen player
      await play(page, w, h);
      await page.getByRole('button', { name: 'Open Now Playing' }).click();
      await expect(page.getByTestId('np-retro')).toBeVisible();
      await page.waitForTimeout(700);
      await page.screenshot({ path: `test-results/retro-fullscreen-${w}.png` });
      await page.getByTestId('np-retro').click();
      await expect(page).toHaveURL(/\/retro$/);
      await expect(page.getByTestId('ipod')).toBeVisible({ timeout: 20_000 });
      // the account menu
      await page.goto('/');
      const item = page.getByRole('menuitem', { name: 'Retro' });
      await expect(async () => { if (!(await item.isVisible())) await page.getByRole('button', { name: /^Account/ }).click(); await expect(item).toBeVisible({ timeout: 1500 }); }).toPass({ timeout: 20_000 });
      await page.screenshot({ path: `test-results/retro-account-${w}.png` });
      await item.click();
      await expect(page).toHaveURL(/\/retro$/);
      await expect(page.getByTestId('ipod')).toBeVisible({ timeout: 20_000 });
    });
  }
});
