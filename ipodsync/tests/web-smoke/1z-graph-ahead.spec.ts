import { test, expect, Page } from '@playwright/test';

// The graph started late on each new song (owner, 12 Nov: "graph delay when a song first plays: check it's gone"): the graphs' own copy of the song only
// began loading when the song began. Now the next song's copy loads while the graph is open, and the moment the song changes it is swapped in.
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

test('the next song\'s graph copy is ready before the song changes', async ({ page }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto('/artist/Hires%20Lab');
  await ws; await page.waitForTimeout(600);
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await page.locator('.tracks .track-main').first().click();
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await page.locator('.ls-seg [role=tab]', { hasText: 'Info' }).click();
  await expect(page.locator('#viz')).toBeVisible({ timeout: 20_000 });
  await page.waitForTimeout(1500);
  const before = await page.evaluate(() => (window as any).__flacieTwinSwaps || 0);
  await page.locator('.ls-transport').getByRole('button', { name: 'Next', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as any).__flacieTwinSwaps || 0), { timeout: 10_000 }).toBeGreaterThan(before);
});
