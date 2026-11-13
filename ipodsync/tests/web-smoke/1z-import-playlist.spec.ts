import { test, expect, Page } from '@playwright/test';

// F26.6 (owner, 5 Nov: the 99-song import showed only the 24 downloaded): an imported playlist lists every song in the import's order;
// the missing ones are greyed in their place with their state and can't be played; its downloads view lists that import's songs only.

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

test('an imported playlist shows every song in order, the missing ones greyed, and its own downloads view', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await signIn(page);
  await page.goto('/import');
  await expect(page.getByRole('heading', { name: 'Import playlists' })).toBeVisible({ timeout: 20_000 });
  await page.waitForTimeout(1200);
  // a name of its own each run: the test data keeps earlier runs' imports, and an import into a list of the same name adds to it
  const stamp = Date.now().toString(36), file = `f26-${stamp}.txt`;
  await page.getByLabel('Choose a playlist file').setInputFiles({ name: file, mimeType: 'text/plain',
    buffer: Buffer.from(`## Every Song ${stamp}\nNobody Known - Missing First\nAurora Vale - Harbour 3\nNobody Known - Missing Last\n`) });
  const row = page.getByTestId('import-job').filter({ hasText: file }).first();
  await expect(row.getByRole('link', { name: 'Open' })).toBeVisible({ timeout: 30_000 });
  await row.getByRole('link', { name: 'Open' }).click();

  const items = page.locator('ol.tracks > li');
  await expect(items).toHaveCount(3, { timeout: 20_000 });
  await expect(items.nth(0)).toHaveAttribute('data-testid', 'pending-song');
  await expect(items.nth(0)).toContainText('Missing First');
  await expect(items.nth(1)).toContainText('Harbour 3');
  await expect(items.nth(1).getByRole('button', { name: /^Play Harbour 3/ })).toBeVisible();
  await expect(items.nth(2)).toContainText('Missing Last');
  // greyed and not playable: no Play button on them, and a state under the title
  await expect(page.getByTestId('pending-song').getByRole('button', { name: /^Play / })).toHaveCount(0);
  await expect(page.getByTestId('pending-state')).toHaveCount(2);
  expect(Number(await items.nth(0).evaluate((e) => getComputedStyle(e).opacity))).toBeLessThan(0.9);

  // the downloads for this playlist: this import's songs only
  await page.getByTestId('pl-downloads').click();
  await expect(page.getByRole('heading', { name: /Downloads for/ })).toBeVisible({ timeout: 20_000 });
  const dl = page.getByTestId('import-download');
  await expect(dl).toHaveCount(3);
  await expect(dl.nth(1)).toContainText('In the library');
  await page.screenshot({ path: 'test-results/shots/import-playlist-downloads.png', fullPage: true });
  // and from the Import row too
  await page.goto('/import');
  await expect(page.getByTestId('import-job').filter({ hasText: file }).first().getByTestId('imp-downloads')).toBeVisible({ timeout: 20_000 });
});
