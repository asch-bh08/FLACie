import { test, expect, Page } from '@playwright/test';

// Taking a song out of a playlist offers Undo, which puts it back in the same place.

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

test('removing from a playlist can be undone', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page);
  const name = 'Undo ' + Date.now().toString().slice(-6);
  await page.goto('/playlists');
  await page.waitForTimeout(1500);
  await page.getByRole('textbox', { name: 'New playlist name' }).fill(name);
  await page.getByRole('button', { name: 'Create' }).click();
  await expect(page).toHaveURL(/\/playlist\//, { timeout: 15_000 });
  const listUrl = page.url();
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  for (const s of ['Lab 1', 'Lab 2', 'Lab 3']) {
    await page.getByRole('button', { name: `More for ${s}` }).click();
    await page.getByRole('menuitem', { name: `Add to ${name}` }).click();
    await expect(page.locator('.toast')).toContainText('Added');
  }
  await page.goto(listUrl);
  const titles = () => page.locator('.tracks .track-title').allInnerTexts().then((a) => a.map((x) => x.trim()));
  await expect.poll(titles, { timeout: 20_000 }).toEqual(['Lab 1', 'Lab 2', 'Lab 3']);
  await page.getByRole('button', { name: 'More for Lab 2' }).click();
  await page.getByRole('menuitem', { name: 'Remove from playlist' }).click();
  await expect.poll(titles).toEqual(['Lab 1', 'Lab 3']);
  await expect(page.locator('.toast')).toContainText('Removed “Lab 2”');
  await page.locator('.toast').getByRole('button', { name: 'Undo' }).click();
  await expect.poll(titles).toEqual(['Lab 1', 'Lab 2', 'Lab 3']);
});
