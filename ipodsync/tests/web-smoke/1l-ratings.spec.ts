import { test, expect, Page } from '@playwright/test';

// Star ratings: the stars button beside the heart, kept in the profile (so it follows the account), written to Jellyfin as a rating of 0-10.
const MOCK = 'http://127.0.0.1:8196';
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
async function playLab(page: Page) {
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  await page.locator('.tracks .track-main').first().click();
  await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 20_000 });
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await expect(page.locator('.np-meta')).toBeVisible({ timeout: 20_000 });
}
const btn = (page: Page) => page.locator('.np-meta button[aria-label^="Rating:"]');

test.describe.serial('ratings', () => {
  test('stars are set, shown, written to Jellyfin and cleared', async ({ page, request }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    await expect(btn(page)).toHaveAttribute('aria-label', 'Rating: not rated');
    await btn(page).click();
    await expect(page.locator('.stars .star')).toHaveCount(5);
    await page.getByRole('button', { name: '4 stars', exact: true }).click();
    await expect(btn(page)).toHaveAttribute('aria-label', 'Rating: 4 stars');
    await expect.poll(async () => (await (await request.get(`${MOCK}/__userdata`)).json())['h1'], { timeout: 10_000 }).toBe(8);   // 4 stars = rating 8 of 10
    // another song starts unrated; coming back, the rating is still there
    await page.getByRole('button', { name: 'Next' }).last().click();
    await expect(btn(page)).toHaveAttribute('aria-label', 'Rating: not rated', { timeout: 15_000 });
    await page.getByRole('button', { name: 'Previous' }).last().click();
    await expect(btn(page)).toHaveAttribute('aria-label', 'Rating: 4 stars', { timeout: 15_000 });
    // tapping the same star again clears it
    await btn(page).click();
    await page.getByRole('button', { name: '4 stars', exact: true }).click();
    await expect(btn(page)).toHaveAttribute('aria-label', 'Rating: not rated');
    await expect.poll(async () => (await (await request.get(`${MOCK}/__userdata`)).json())['h1'], { timeout: 10_000 }).toBe(0);
    await page.screenshot({ path: 'test-results/ratings.png' });
  });
});
