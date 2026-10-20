import { test, expect, Page } from '@playwright/test';

// Search with nothing typed lists the recent searches (kept in the profile): a chip searches again, Clear empties the list.

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(1000);
}

test('recent searches come back as chips, and Clear empties them', async ({ page }) => {
  test.setTimeout(90_000);
  await signIn(page);
  await page.goto('/search?q=Lab');
  await page.waitForTimeout(4000);
  await page.goto('/search');
  const chip = page.locator('.recent-searches .chip', { hasText: 'Lab' });
  await expect(chip).toBeVisible({ timeout: 20_000 });
  await chip.click();
  await expect(page).toHaveURL(/\/search\?q=Lab/);
  await page.goto('/search');
  await page.locator('.recent-searches').getByRole('button', { name: 'Clear' }).click();
  await expect(page.locator('.recent-searches')).toHaveCount(0);
});
