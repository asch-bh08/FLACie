import { test, expect } from '@playwright/test';
import { signIn, open } from './sharp-helpers';

// The desk scene is centred by layout, so the filter's surface lands on whole pixels.
test.describe('desk scene centring', () => {
  test.beforeEach(async ({ page }) => { await signIn(page); await page.setViewportSize({ width: 1920, height: 1080 }); });

  test('desk: the scene is centred by layout, not by a fractional translate', async ({ page }) => {
    for (const [w, h] of [[1920, 1080], [1366, 768], [1000, 1000]]) {
      await page.setViewportSize({ width: w, height: h });
      await open(page, { model: 'classic6', look: 2, panel: false });
      const r = await page.evaluate(() => {
        const s = document.querySelector('.desk-scene')!, d = document.querySelector('[data-testid=desk]')!;
        const a = s.getBoundingClientRect(), b = d.getBoundingClientRect();
        return { translate: getComputedStyle(s).translate, dx: (a.left + a.width / 2) - (b.left + b.width / 2), dy: (a.top + a.height / 2) - (b.top + b.height / 2) };
      });
      expect(r.translate).toBe('none');
      expect(Math.abs(r.dx)).toBeLessThan(1); expect(Math.abs(r.dy)).toBeLessThan(1);
    }
  });
});
