import { test, expect, Page } from '@playwright/test';

// Search results can be narrowed to one kind: the chips All, Songs, Albums, Artists (and Playlists when any match) show only that section.

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

test('the Songs chip keeps only the songs, All brings everything back', async ({ page }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 1280, height: 800 });
  await signIn(page);
  await page.goto('/search?q=Lab');
  const kinds = page.getByRole('group', { name: 'Show only' });
  await expect(kinds).toBeVisible({ timeout: 20_000 });
  await expect(page.getByRole('region', { name: 'Albums' })).toBeVisible();
  await kinds.getByRole('button', { name: 'Songs' }).click();
  await expect(kinds.getByRole('button', { name: 'Songs' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('region', { name: 'Songs' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Albums' })).toHaveCount(0);
  await expect(page.getByRole('region', { name: 'Top result' })).toHaveCount(0);
  await kinds.getByRole('button', { name: 'All' }).click();
  await expect(page.getByRole('region', { name: 'Albums' })).toBeVisible();
});
