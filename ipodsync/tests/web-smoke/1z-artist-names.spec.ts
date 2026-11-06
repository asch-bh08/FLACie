import { test, expect, Page } from '@playwright/test';

// F24 (owner, 6 Nov): Server admin > Artist names checks the library as a dry run; each proposed change has "Leave as it is", which takes that
// kind of change off the list (and off every later check). Nothing is written to Jellyfin without Approve.

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

test('checking artist names is a dry run, and Leave as it is takes a change off the list', async ({ page }) => {
  await signIn(page);
  await page.goto('/admin');
  const block = page.getByTestId('artist-fixes');
  await expect(block).toBeVisible();
  await block.getByRole('button', { name: /Check artist names|Check again/ }).click();
  const leave = block.getByRole('button', { name: /^Leave .* as it is$/ });
  const allRight = block.getByText('Every artist name looks right.');
  await expect(leave.first().or(allRight)).toBeVisible({ timeout: 60_000 });
  if (await allRight.isVisible()) return;
  const before = await leave.count();
  await leave.first().click();
  await expect.poll(() => leave.count()).toBeLessThan(before);
  // still a dry run: the list says nothing has been changed
  await expect(block.getByText(/nothing has been changed yet|Every artist name looks right/)).toBeVisible();
});
