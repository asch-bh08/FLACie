import { test, expect, Page } from '@playwright/test';

// F6.2 (owner, 5 Nov): searching "prodigy" is laid out like Spotify: The Prodigy as the Top result with their top 5 songs beside it (no big empty card),
// then Popular, Albums, Singles & EPs (newest first), Playlists (Best of, Radio, decade and genre lists) and other artists with similar names, small.
// A search for a song keeps the song-first layout. The mock library has The Prodigy (12 songs) and Prodigy (3).

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

// the page as the person sees it once it is live (the content checks below wait for what they need)
async function live(page: Page, url: string) {
  await page.goto(url);
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.waitForTimeout(1500);
}

const noSideScroll = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1);
const titles = (page: Page, region: string, item: string) => page.getByRole('region', { name: region }).locator(item).allInnerTexts();

test('"prodigy" puts The Prodigy on top with their top 5 songs beside it, then the rows', async ({ page }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await live(page, '/search?q=prodigy');
  const top = page.getByRole('region', { name: 'Top result' });
  await expect(top).toBeVisible({ timeout: 20_000 });
  await expect(top.locator('.sr-hero-title')).toHaveText('The Prodigy');
  const songs = top.locator('.sr-top-songs .track');
  await expect(songs).toHaveCount(5);
  // beside it, not under it, and the card is no taller than the list next to it (no big empty card)
  const hero = (await top.locator('.sr-hero').boundingBox())!, list = (await top.locator('.sr-top-songs').boundingBox())!;
  expect(list.x).toBeGreaterThan(hero.x + hero.width - 1);
  expect(Math.abs(list.y - hero.y)).toBeLessThan(80);
  expect(hero.height).toBeLessThan(list.height + 80);
  await expect(page.getByRole('region', { name: 'Popular' }).locator('.card')).toHaveCount(7);
  expect(await titles(page, 'Albums', '.card-title')).toEqual(['The Fat of the Land', 'Music for the Jilted Generation']);
  expect(await titles(page, 'Singles & EPs', '.card-title')).toEqual(['Firestarter', 'Charly EP']);
  const lists = await titles(page, 'Playlists', '.card-title');
  expect(lists).toContain('Best of The Prodigy');
  expect(lists).toContain('The Prodigy Radio');
  expect(lists).toContain('Electronic');
  expect(lists.some((l) => /\d0s$/.test(l))).toBe(true);
  await expect(page.getByRole('region', { name: 'Other artists' }).getByRole('link', { name: 'Prodigy', exact: true })).toBeVisible();
  // the rows come in this order
  const order = await page.locator('section.sr-section').evaluateAll((els) => els.map((e) => e.getAttribute('aria-label')));
  expect(order).toEqual(['Top result', 'Popular', 'Albums', 'Singles & EPs', 'Playlists', 'Other artists']);
  expect(await noSideScroll(page)).toBe(true);
  await page.screenshot({ path: 'test-results/shots/search-prodigy-pc.png', fullPage: true });
});

test('on a phone the top songs go under the Top result, with no side scroll', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await signIn(page);
  await live(page, '/search?q=prodigy');
  const top = page.getByRole('region', { name: 'Top result' });
  await expect(top.locator('.sr-top-songs .track')).toHaveCount(5, { timeout: 20_000 });
  const hero = (await top.locator('.sr-hero').boundingBox())!, list = (await top.locator('.sr-top-songs').boundingBox())!;
  expect(list.y).toBeGreaterThan(hero.y + hero.height - 1);
  expect(await noSideScroll(page)).toBe(true);
  await page.screenshot({ path: 'test-results/shots/search-prodigy-phone.png', fullPage: true });
});

test('a search for a song keeps the song-first layout', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await live(page, '/search?q=smack my bitch up');
  const top = page.getByRole('region', { name: 'Top result' });
  await expect(top.locator('.sr-hero-title')).toHaveText('Smack My Bitch Up', { timeout: 20_000 });
  await expect(top.locator('.sr-top-songs')).toHaveCount(0);
  await expect(page.getByRole('region', { name: 'Popular' })).toHaveCount(0);
});
