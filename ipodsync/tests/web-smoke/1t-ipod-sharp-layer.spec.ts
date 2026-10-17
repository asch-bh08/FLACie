import { test, expect } from '@playwright/test';
import { signIn, open, layerInfo } from './sharp-helpers';

// On the desk the iPod sits in an axis-aligned layer with the tilt drawn into it, not a rotated bitmap.
test.describe('desk iPod layer', () => {
  test.beforeEach(async ({ page }) => { await signIn(page); await page.setViewportSize({ width: 1920, height: 1080 }); });

  test('desk: the layers holding the iPod are axis-aligned, the tilt is drawn into them', async ({ page }) => {
    await open(page, { model: 'ipod4', look: 2, panel: false });
    const ls = (await layerInfo(page)).filter((l) => l.inArea);
    expect(ls.length).toBeGreaterThan(0);
    for (const l of ls) { expect(Math.abs(l.t[1]), l.name).toBeLessThan(0.001); expect(Math.abs(l.t[4]), l.name).toBeLessThan(0.001); }
    // and the iPod still lies at the net -4deg
    expect(await page.locator('.desk-ipod').evaluate((e) => getComputedStyle(e).rotate)).toBe('-4deg');
  });

});
