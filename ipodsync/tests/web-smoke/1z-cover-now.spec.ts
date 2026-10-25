import { test, expect, Page } from '@playwright/test';

// The owner: covers don't load instantly. Every cover waited for the browser's lazy loading, the big Now Playing cover included, and that one is a separate
// full-size file, so it started blank even when the small cover was already in the cache. Covers always on screen load at once, and the big one shows the
// small cover underneath straight away.

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

test('the player and Now Playing covers load at once, the big one over the small one', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await page.locator('.tracks .track-main').first().click();
  await expect(page.locator('.player')).toBeVisible();
  const bar = page.locator('.player .bar-song .cover img').first();
  expect(await bar.getAttribute('loading')).not.toBe('lazy');
  await page.locator('.player [aria-label="Full screen player"]').click();
  const big = page.locator('.np .np-cover img');
  await expect(big.first()).toBeAttached();
  const imgs = await big.evaluateAll((els) => els.map((e) => ({ src: (e as HTMLImageElement).src, lazy: e.getAttribute('loading') })));
  expect(imgs.some((i) => i.src.includes('s=full'))).toBe(true);
  expect(imgs.some((i) => !i.src.includes('s=full'))).toBe(true);   // the small one underneath
  expect(imgs.every((i) => i.lazy !== 'lazy')).toBe(true);
});
