import { test, expect, Page } from '@playwright/test';

// Owner, 9 Nov (a crop of the CD player): "that's still hanging off the Discman". The green screen and the keys under the lid sat over the body's rounded
// corners, so their outer corners stuck out past its edge. Every corner of the screen and of the first and last key must be inside the body's rounded shape.

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForTimeout(1200);
}

for (const [w, h] of [[1440, 900], [390, 844], [1024, 768]]) {
  test(`at ${w}x${h} the CD player's screen and keys sit inside its rounded body`, async ({ page }) => {
    await page.setViewportSize({ width: w, height: h });
    await signIn(page);
    await page.goto('/retro'); await page.waitForTimeout(2500);
    await page.getByTestId('ipod-gear').click();
    await page.locator('[data-testid=ipod-panel] [data-scene="discman"]').click();
    await page.getByTestId('ipod-gear').click();
    await page.waitForTimeout(1200);
    const outside = await page.evaluate(() => {
      const body = document.querySelector('.dm-body') as HTMLElement;
      const b = body.getBoundingClientRect(), cs = getComputedStyle(body);
      // the corner radii as the browser resolved them (elliptical: across / down)
      const rad = (v: string, size: number) => v.endsWith('%') ? parseFloat(v) / 100 * size : parseFloat(v);
      const [rx, ry] = (cs.borderBottomLeftRadius.split(' ').length > 1 ? cs.borderBottomLeftRadius.split(' ') : [cs.borderBottomLeftRadius, cs.borderBottomLeftRadius]);
      const RX = rad(rx, b.width), RY = rad(ry, b.height);
      const inside = (x: number, y: number) => {
        const cx = x < b.left + RX ? b.left + RX : x > b.right - RX ? b.right - RX : x;
        const cy = y < b.top + RY ? b.top + RY : y > b.bottom - RY ? b.bottom - RY : y;
        if (cx === x || cy === y) return x >= b.left + 8 && x <= b.right - 8 && y >= b.top + 8 && y <= b.bottom - 8;
        return ((x - cx) / RX) ** 2 + ((y - cy) / RY) ** 2 <= 0.7;   // well inside: right on the bevelled edge it still reads as hanging off
      };
      const parts = [document.querySelector('.dm-lcd'), ...Array.from(document.querySelectorAll('.dm-keys button')).filter((_, i, a) => i === 0 || i === a.length - 1)] as HTMLElement[];
      const bad: string[] = [];
      for (const el of parts) {
        const r = el.getBoundingClientRect(), k = Math.min(2, r.width / 8);   // the corners themselves (2 px in: anti-aliasing)
        for (const [x, y] of [[r.left + k, r.top + k], [r.right - k, r.top + k], [r.left + k, r.bottom - k], [r.right - k, r.bottom - k]])
          if (!inside(x, y)) bad.push(`${el.className || el.tagName} at ${Math.round(x - b.left)},${Math.round(y - b.top)} of ${Math.round(b.width)}x${Math.round(b.height)}`);
      }
      return bad;
    });
    expect(outside).toEqual([]);
  });
}
