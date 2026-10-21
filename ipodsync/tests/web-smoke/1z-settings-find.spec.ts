import { test, expect, Page } from '@playwright/test';

// Settings: the 1.0 groups (Playback, Library, Appearance, Account, About) and a box that finds a setting: only the groups that mention it stay.

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

test('Settings has the 1.0 groups and a search that narrows them', async ({ page }) => {
  test.setTimeout(90_000);
  await signIn(page);
  await page.goto('/settings');
  await page.waitForTimeout(2500);
  for (const h of ['Playback', 'Library', 'Appearance', 'Account', 'About']) await expect(page.getByRole('heading', { name: h, exact: true })).toBeVisible();
  const box = page.getByRole('searchbox', { name: 'Find a setting' });
  await box.fill('version');
  await expect(page.getByRole('heading', { name: 'About', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Playback', exact: true })).toBeHidden();
  await box.fill('zzzz');
  await expect(page.getByText('No setting matches')).toBeVisible();
  await box.fill('');
  await expect(page.getByRole('heading', { name: 'Playback', exact: true })).toBeVisible();
});
