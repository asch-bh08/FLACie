import { test, expect, Page } from '@playwright/test';

// "Recommended songs" under your own playlist: the section shows below the songs, "+" adds a song to the end (saved to the account), and the row leaves.

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

test('recommended songs under a playlist, one tap adds each', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page);
  const name = 'Recs ' + Date.now().toString().slice(-6);
  await page.goto('/playlists');
  await page.waitForTimeout(1500);
  await page.getByRole('textbox', { name: 'New playlist name' }).fill(name);
  await page.getByRole('button', { name: 'Create' }).click();
  await expect(page).toHaveURL(/\/playlist\//, { timeout: 15_000 });
  const listUrl = page.url();
  // an empty playlist has no section
  await expect(page.locator('section.pl-recs')).toHaveCount(0);
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  for (const s of ['Lab 1', 'Lab 2']) {
    await page.getByRole('button', { name: `More for ${s}` }).click();
    await page.getByRole('menuitem', { name: `Add to ${name}` }).click();
    await expect(page.locator('.toast')).toContainText('Added');
  }
  await page.goto(listUrl);
  const recs = page.locator('section.pl-recs');
  await expect(recs.getByRole('heading', { name: 'Recommended songs' })).toBeVisible({ timeout: 20_000 });
  await expect(recs).toContainText('Based on the songs in this playlist');
  const rows = recs.locator('li.pl-rec');
  await expect(rows.first()).toBeVisible({ timeout: 20_000 });
  const before = await rows.count();
  expect(before).toBeGreaterThan(0);
  const title = (await rows.first().locator('.track-title').innerText()).trim();
  await rows.first().getByRole('button', { name: `Add ${title} to this playlist` }).click();
  // a tick, then the row leaves; the playlist above has the song at its end
  await expect(recs.locator('li.pl-rec', { hasText: title })).toHaveCount(0, { timeout: 10_000 });
  await expect.poll(async () => (await page.locator('section.pl-recs').evaluate((el) => {
    const titles = [...document.querySelectorAll('.pl-main .track-title')].filter((t) => !el.contains(t)).map((t) => t.textContent?.trim());
    return titles;
  }))).toEqual(['Lab 1', 'Lab 2', title]);
  // saved to the account: still there after a reload
  await page.reload();
  await expect.poll(() => page.locator('.pl-main .track-title').allInnerTexts().then((a) => a.map((x) => x.trim()).slice(0, 3)), { timeout: 20_000 }).toEqual(['Lab 1', 'Lab 2', title]);
  await expect(page.getByRole('button', { name: 'Refresh' })).toBeVisible();
});
