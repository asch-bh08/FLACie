import { test, expect, Page } from '@playwright/test';

// Keyboard shortcuts on the web: / puts the cursor in the search box, ? opens the list of shortcuts (Escape closes it).

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

test('/ focuses search, ? lists the shortcuts', async ({ page }) => {
  test.setTimeout(60_000);
  await page.setViewportSize({ width: 1280, height: 800 });
  await signIn(page);
  await page.locator('body').click({ position: { x: 700, y: 400 } });
  await page.keyboard.press('/');
  await expect(page.locator('.top-search input')).toBeFocused();
  await expect(page.locator('.top-search input')).toHaveValue('');
  await page.locator('.top-search input').blur();
  await page.keyboard.press('Shift+?');
  const d = page.getByRole('dialog', { name: 'Keyboard shortcuts' });
  await expect(d).toBeVisible();
  await expect(d).toContainText('Play or pause');
  await page.keyboard.press('Escape');
  await expect(d).toBeHidden();
});
