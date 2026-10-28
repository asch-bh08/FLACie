import { test, expect, Page } from '@playwright/test';

// The owner's Windows app showed the bar's heart drawn on top of Next (window about 1257 wide): the middle controls and the right-hand buttons
// overlapped. At every PC width no two of the bar's buttons may overlap.

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

for (const w of [900, 1024, 1180, 1257, 1366, 1440]) {
  test(`the bar's buttons don't overlap at ${w} wide`, async ({ page }) => {
    await page.setViewportSize({ width: w, height: 860 });
    await signIn(page);
    await page.goto('/artist/Hires%20Lab');
    await page.getByRole('link', { name: /Lab Album/ }).first().click();
    await page.locator('.tracks .track-main').first().click();
    await expect(page.locator('.player')).toBeVisible();
    await page.waitForTimeout(1500);
    const hits = await page.evaluate(() => {
      const els = [...document.querySelectorAll('.player .bar-controls button, .player .bar-controls input, .player .bar-right button, .player .bar-right a, .player .bar-right input')]
        .filter((e) => (e as HTMLElement).offsetParent !== null);
      const r = els.map((e) => ({ n: e.getAttribute('aria-label') || e.tagName, b: e.getBoundingClientRect() }));
      const out: string[] = [];
      for (let i = 0; i < r.length; i++) for (let j = i + 1; j < r.length; j++) {
        const a = r[i].b, b = r[j].b;
        if (a.width && b.width && a.left < b.right - 1 && b.left < a.right - 1 && a.top < b.bottom - 1 && b.top < a.bottom - 1) out.push(`${r[i].n} / ${r[j].n}`);
      }
      return out;
    });
    expect(hits).toEqual([]);
    // the DJ's switch and its mic sit together on the right, beside the heart (owner, 27 Oct: a DJ button on the bar, like the phone's)
    await expect(page.locator('.player .bar-right .bar-dj')).toBeVisible();
    await expect(page.locator('.player .bar-right [data-testid="dj-mic"]')).toBeVisible();
    await expect(page.locator('.player .bar-controls [data-testid="dj-mic"]')).toHaveCount(0);
    // and the song itself keeps room (making room for the buttons once squeezed it to nothing at 900 wide)
    expect((await page.locator('.player .bar-song').boundingBox())!.width).toBeGreaterThan(120);
  });
}
