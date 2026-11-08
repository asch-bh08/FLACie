import { test, expect, Page } from '@playwright/test';

// Search shows the person's playlists that hold the song or artist typed, not only those named like it (owner, 7 Nov: "dreamer", "with me", "eminem",
// "aura" should list the playlists they are in), each card saying what it holds.
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

test('a song search lists the playlist that holds it, saying so', async ({ page }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 1280, height: 800 });
  await signIn(page);
  const name = 'Holds ' + Date.now().toString().slice(-6);
  await page.goto('/playlists');
  await page.waitForTimeout(1500);
  await page.getByRole('textbox', { name: 'New playlist name' }).fill(name);
  await page.getByRole('button', { name: 'Create' }).click();
  await expect(page).toHaveURL(/\/playlist\//, { timeout: 15_000 });
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  await page.getByRole('button', { name: 'More for Lab 3' }).click();
  await page.getByRole('menuitem', { name: `Add to ${name}` }).click();
  await expect(page.locator('.toast')).toContainText('Added');
  await page.goto('/search?q=' + encodeURIComponent('Lab 3'));
  const lists = page.getByRole('region', { name: 'Playlists' });
  const card = lists.locator('a.card', { hasText: name });
  await expect(card).toBeVisible({ timeout: 20_000 });
  await expect(card.locator('.card-sub')).toContainText('Has “Lab 3”');
});
