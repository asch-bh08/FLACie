import { test, expect } from '@playwright/test';
import { signIn, open, layerInfo } from './sharp-helpers';

// A finished screen slide or fade must not keep its fill, which keeps a compositor layer (a resampled bitmap) over the screen's text.
test.describe('screen animations let go', () => {
  test.beforeEach(async ({ page }) => { await signIn(page); await page.setViewportSize({ width: 1920, height: 1080 }); });

  for (const model of ['ipod4', 'classic6', 'nano4']) {
    test(`${model}: after the screen settles no animation or layer is left inside it`, async ({ page }) => {
      await open(page, { model, look: 1, panel: false });
      // a screen change plays the slide, then it must let go of its fill
      await page.getByTestId('ipod-keys').focus();
      await page.keyboard.press('Enter'); await page.waitForTimeout(1300);
      const anims = await page.getByTestId('ipod-lcd').evaluate((e) => e.getAnimations({ subtree: true }).length);
      expect(anims).toBe(0);
      const ls = await layerInfo(page);
      expect(ls.filter((l) => l.inLcd).map((l) => l.name)).toEqual([]);
    });
  }

});
