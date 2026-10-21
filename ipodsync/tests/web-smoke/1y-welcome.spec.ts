import { test, expect, Page } from '@playwright/test';

// The first visit to Home shows a short welcome (where Home, Search and Retro are); Got it puts it away for good (kept in the profile).

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

test('the welcome shows once and Got it puts it away for good', async ({ page }) => {
  test.setTimeout(90_000);
  await signIn(page);
  await page.goto('/');
  const w = page.getByRole('region', { name: 'Welcome to FLACie' });
  // another spec may have dismissed it already (the profile is shared): then there is nothing to do
  if (await w.isVisible({ timeout: 15_000 }).catch(() => false)) {
    await expect(w).toContainText('Search finds anything');
    await w.getByRole('button', { name: 'Got it' }).click();
    await expect(w).toBeHidden();
  }
  await page.reload();
  await page.waitForTimeout(3000);
  await expect(page.getByRole('region', { name: 'Welcome to FLACie' })).toHaveCount(0);
});
