import { test, expect, Page } from '@playwright/test';

// F26.3 (owner, 5 Nov: "make the playlist filter only show playlists and albums only show albums; the separate filter is where you customise more
// with genre"): the kind chips are one choice; Playlists shows only playlists; genre and decade come from the Filters panel.

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

const sections = (page: Page) => page.locator('main section.sr-section').evaluateAll((els) => els.map((e) => e.getAttribute('aria-label')));
const lit = (page: Page) => page.getByTestId('kinds').locator('.chip.on').allInnerTexts();

test('the kind chips are one choice: Albums only albums, Playlists only playlists', async ({ page }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await page.goto('/search?q=prodigy');
  const kinds = page.getByTestId('kinds');
  await expect(kinds).toBeVisible({ timeout: 20_000 });
  await page.waitForTimeout(1000);
  await kinds.getByRole('radio', { name: 'Albums' }).click();
  await expect.poll(() => lit(page)).toEqual(['Albums']);
  await expect.poll(() => sections(page)).toEqual(['Albums']);
  await kinds.getByRole('radio', { name: 'Playlists' }).click();
  await expect.poll(() => lit(page)).toEqual(['Playlists']);           // Albums is off now: one choice
  await expect.poll(() => sections(page)).toEqual(['All playlists']);
  const all = page.getByTestId('all-playlists');
  await expect(all.locator('.card').first()).toBeVisible();
  await expect(all.locator('.card', { hasText: 'Best of The Prodigy' })).toBeVisible();
  await expect(page.getByRole('heading', { name: /More music/ })).toHaveCount(0);
  await kinds.getByRole('radio', { name: 'All' }).click();
  await expect.poll(() => lit(page)).toEqual(['All']);
  await expect(page.getByRole('region', { name: 'Top result' })).toBeVisible();
});

test('genre from the Filters panel narrows the playlists; no genre chips under Playlists and no "Genre" or "Other" genre', async ({ page }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await page.goto('/search?q=electronic');
  const kinds = page.getByTestId('kinds');
  await expect(kinds).toBeVisible({ timeout: 20_000 });
  await page.waitForTimeout(1000);
  await kinds.getByRole('radio', { name: 'Playlists' }).click();
  const all = page.getByTestId('all-playlists');
  await expect(all.locator('.card').first()).toBeVisible({ timeout: 15_000 });
  await expect(page.locator('.lists-filter')).toHaveCount(0);           // the genres are not chips under Playlists any more
  await page.getByRole('button', { name: /^Filters/ }).click();
  const genre = page.locator('.dd', { has: page.locator('.dd-label', { hasText: 'Genre' }) });
  await genre.locator('.dd-btn').click();
  const options = await genre.getByRole('option').allInnerTexts();
  expect(options.map((o) => o.trim().toLowerCase())).not.toContain('genre');
  expect(options.map((o) => o.trim().toLowerCase())).not.toContain('other');
  await genre.getByRole('option', { name: 'Electronic' }).click();
  await expect(all.getByRole('heading')).toContainText('Electronic');
  await expect.poll(async () => (await all.locator('.card-title').allInnerTexts()).every((t) => /electronic/i.test(t))).toBe(true);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
});
