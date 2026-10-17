import { test, expect } from '@playwright/test';
import { signIn, open } from './sharp-helpers';

// The iPod on the desk is big enough to read and use: at 1920x1080 its screen text is not tiny, the wheel is a decent target, and it lies clear of every prop.
const PROPS = ['desk-journal', 'desk-rack', 'desk-pop', 'desk-ticket', 'desk-usb', 'desk-clip', 'desk-preamp', 'desk-np', 'desk-coffee', 'desk-open-cd'];

test.describe('desk iPod size', () => {
  test.beforeEach(async ({ page }) => { await signIn(page); });

  test('at 1080p the iPod fills most of the desk height and the click wheel is a real target', async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 });
    await open(page, { model: 'classic6', look: 2, panel: false });
    const dev = (await page.getByTestId('ipod-device').boundingBox())!, wheel = (await page.getByTestId('ipod-wheel').boundingBox())!, desk = (await page.getByTestId('desk').boundingBox())!;
    expect(dev.height / desk.height, 'iPod height to the desk').toBeGreaterThan(0.58);
    expect(wheel.width, 'wheel width in px').toBeGreaterThanOrEqual(240);
    const row = await page.locator('[data-testid=ipod-row]').first().boundingBox();
    expect(row!.height, 'a menu row in px').toBeGreaterThanOrEqual(30);
  });

  const check = async (page: any) => {
    const r = await page.evaluate((ids: string[]) => {
      const box = (e: Element | null) => e && e.getBoundingClientRect(), d = box(document.querySelector('[data-testid=ipod-device]'))!, k = box(document.querySelector('[data-testid=desk]'))!;
      const out: { onDesk: boolean; over: [string, number][] } = { onDesk: d.left >= k.left && d.top >= k.top && d.right <= k.right && d.bottom <= k.bottom, over: [] };
      for (const id of ids) {
        const b = box(document.querySelector(`[data-testid=${id}]`)); if (!b || !b.width) continue;
        const o = Math.max(0, Math.min(d.right, b.right) - Math.max(d.left, b.left)) * Math.max(0, Math.min(d.bottom, b.bottom) - Math.max(d.top, b.top));
        out.over.push([id, o / (b.width * b.height)]);
      }
      return out;
    }, PROPS);
    expect(r.onDesk, 'on the desk').toBe(true);
    for (const [id, o] of r.over) expect(o, `${id} on the iPod`).toBeLessThan(0.02);
  };
  for (const model of ['ipod1', 'classic6', 'mini', 'nano4', 'touch5']) {
    for (const [w, h] of [[1920, 1080], [1366, 768], [1600, 1000]]) {
      test(`${model} at ${w}x${h}: nothing lies on the iPod, and it stays on the desk`, async ({ page }) => {
        await page.setViewportSize({ width: w, height: h });
        await open(page, { model, look: 2, panel: false });
        await check(page);
      });
    }
  }
  test('with a song playing (the CDs and the Now Playing card out) nothing lies on the iPod either', async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 });
    await open(page, { model: 'classic6', look: 2, panel: false });
    await page.getByTestId('ipod-keys').focus();
    for (let i = 0; i < 10; i++) await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('ipod-np')).toBeVisible({ timeout: 20_000 });
    await page.getByTestId('desk-open-cd').click();
    await page.waitForTimeout(900);
    await check(page);
  });
});
