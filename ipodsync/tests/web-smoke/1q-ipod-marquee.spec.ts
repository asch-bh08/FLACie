import { test, expect, Page } from '@playwright/test';

// Text too long for an iPod's screen scrolls, as on a real iPod: the Now Playing title, artist and album, the highlighted menu row, the touch iPods'
// Now Playing, the Cover Flow caption. It scrolls inside its own line (never past the screen's edge), pauses at each end, and holds still (with an
// ellipsis) when motion is reduced. Short text never moves. (The mock's titles are short: a long one is put on the page.)
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
async function openIpod(page: Page, prefs: Record<string, unknown>) {
  await page.addInitScript((p) => { try { localStorage.setItem('flacie.ipod', JSON.stringify(p)); } catch { } }, prefs);
  await page.goto('/ipod');
  await expect(page.getByTestId('ipod')).toHaveAttribute('data-model', String(prefs.model), { timeout: 20_000 });
  await page.waitForTimeout(700);
}
async function shuffle(page: Page) {
  await page.getByTestId('ipod-keys').focus();
  for (let i = 0; i < 10; i++) await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('ipod-np')).toBeVisible({ timeout: 20_000 });
}
const LONG = 'A Remarkably Long Song Title That Never Fits On An iPod Screen (Extended Version)';
/** Puts a long text in an element and reports its scrolling: whether it moves, and whether its own box stays inside the screen. */
const longText = (page: Page, sel: string, screenSel: string) => page.evaluate(async ([s, scr, text]) => {
  const e = document.querySelector(s) as HTMLElement; e.textContent = text;
  await new Promise((r) => setTimeout(r, 900));
  const cs = getComputedStyle(e), b = e.getBoundingClientRect(), sb = document.querySelector(scr)!.getBoundingClientRect();
  return { moving: cs.animationName.includes('ipod-mq') && cs.animationPlayState === 'running', inside: b.left >= sb.left - 1.5 && b.right <= sb.right + 1.5, clip: cs.overflow === 'hidden' };
}, [sel, screenSel, LONG] as const);

test.describe.serial('long text on the iPod screens scrolls', () => {
  test('Now Playing and the highlighted menu row on a wheel iPod', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1366, height: 768 });
    await openIpod(page, { model: 'classic6', look: 1 });
    // a short row holds still; a long one, highlighted, scrolls
    expect(await page.locator('[data-testid=ipod-row].sel .lcd-label').evaluate((e) => getComputedStyle(e).animationName)).toBe('none');
    expect(await longText(page, '[data-testid=ipod-row].sel .lcd-label', '[data-testid=ipod-lcd]')).toEqual({ moving: true, inside: true, clip: true });
    await shuffle(page);
    expect(await longText(page, '[data-testid=ipod-np-title]', '[data-testid=ipod-lcd]')).toEqual({ moving: true, inside: true, clip: true });
    expect(await longText(page, '.lnp-album', '[data-testid=ipod-lcd]')).toEqual({ moving: true, inside: true, clip: true });
  });

  test('the title bar (an album or playlist name) on a colour and a mono iPod, and the touch iPods\' title', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1366, height: 768 });
    for (const model of ['classic6', 'ipod4']) {
      await page.goto('about:blank');
      await openIpod(page, { model, look: 1 });
      expect(await longText(page, '.lcd-hdr b', '[data-testid=ipod-lcd]'), model).toEqual({ moving: true, inside: true, clip: true });
    }
    await page.goto('about:blank');
    await openIpod(page, { model: 'touch5', look: 1 });
    await expect(async () => { await page.locator('.ios-app[data-app=music]').first().click(); await expect(page.getByTestId('ios')).toHaveAttribute('data-app', 'music', { timeout: 1000 }); }).toPass({ timeout: 20_000 });
    await expect(page.locator('[data-testid=ios-nav] b')).toBeVisible();
    expect(await longText(page, '[data-testid=ios-nav] b', '[data-testid=ipod-touch-screen]')).toEqual({ moving: true, inside: true, clip: true });
  });

  test('the touch iPods\' Now Playing', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1366, height: 768 });
    await page.goto('/songs');
    await page.locator('.track').first().locator('button.track-main').click();
    await page.evaluate(() => localStorage.setItem('flacie.ipod', JSON.stringify({ model: 'touch5', look: 1 })));
    await page.evaluate(() => (window as any).Blazor.navigateTo('/ipod'));
    await expect(page.getByTestId('ios')).toHaveAttribute('data-app', 'now', { timeout: 20_000 });
    expect(await longText(page, '[data-testid=ios-np-title]', '[data-testid=ipod-touch-screen]')).toEqual({ moving: true, inside: true, clip: true });
  });

  test('reduced motion: long text holds still, cut with an ellipsis', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await signIn(page); await page.setViewportSize({ width: 1366, height: 768 });
    await openIpod(page, { model: 'classic6', look: 1 });
    await shuffle(page);
    const st = await longText(page, '[data-testid=ipod-np-title]', '[data-testid=ipod-lcd]');
    expect(st.moving).toBe(false); expect(st.inside).toBe(true);
    expect(await page.getByTestId('ipod-np-title').evaluate((e) => getComputedStyle(e).textOverflow)).toBe('ellipsis');
  });
});
