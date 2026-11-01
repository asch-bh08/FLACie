import { test, expect } from '@playwright/test';

// While a list loads, the page shows grey placeholders where the covers and rows will be (the 1.0 plan: skeletons instead of a bare
// "Loading…" line), still and quiet (no endless shimmer), with the words kept for a screen reader; they give way to the real list.

const MOCK = `http://127.0.0.1:${process.env.MOCK_PORT || 8196}`;

test('Home shows placeholder covers while the profile loads, then the real page', async ({ page, request }) => {
  test.setTimeout(90_000);
  await request.get(`${MOCK}/__prefs?delay=6000`);
  try {
    await page.goto('/login');
    await page.locator('#jf-form summary').click();
    await page.locator('#user').fill('tester');
    await page.locator('#password').fill('pw');
    await page.locator('#jf-form button[type="submit"]').click();
    await page.waitForURL((u) => !u.pathname.startsWith('/login'));
    const skel = page.locator('.skel');
    await expect(skel).toBeVisible({ timeout: 5_000 });
    await expect(skel).toHaveAttribute('role', 'status');
    await expect(skel).toContainText('Loading your music');
    expect(await skel.locator('.skel-card').count()).toBeGreaterThanOrEqual(4);
    // still: nothing in it animates for ever
    expect(await skel.evaluate((e) => e.getAnimations({ subtree: true }).filter((a) => a.effect?.getTiming().iterations === Infinity).length)).toBe(0);
    await page.screenshot({ path: 'test-results/skeleton-home.png' });
    await expect(skel).toBeHidden({ timeout: 20_000 });
    await expect(page.getByRole('group', { name: 'Moods' })).toBeVisible();
  } finally {
    await request.get(`${MOCK}/__prefs?delay=0`);
  }
});
