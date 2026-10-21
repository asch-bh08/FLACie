import { test, expect, Page } from '@playwright/test';

// Sharing: Share makes a link (/s/...) to the songs. Whoever opens it signs in to FLACie as themselves (and comes back to the link after signing in),
// sees who shared it, plays the songs from their own library and can save them as one of their playlists.

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

test('share an album: the link opens it, signing in comes back to it, and it plays and saves', async ({ page, context, browser }) => {
  test.setTimeout(120_000);
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await signIn(page);
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  // no share sheet here (a PC browser): the link is copied
  await page.evaluate(() => { (navigator as any).share = undefined; });
  await page.getByRole('button', { name: 'Share' }).click();
  await expect(page.locator('.toast')).toContainText('Link copied');
  const link = await page.evaluate(() => navigator.clipboard.readText());
  expect(link).toMatch(/\/s\/[A-Za-z0-9]{10}$/);

  // someone signed out opens it: the sign-in page, then straight back to the link
  const other = await browser.newContext();
  const p2 = await other.newPage();
  await p2.goto(link);
  await expect(p2).toHaveURL(/\/login/);
  await p2.locator('#jf-form summary').click();
  await p2.locator('#user').fill('tester');
  await p2.locator('#password').fill('pw');
  await p2.locator('#jf-form button[type="submit"]').click();
  await expect(p2).toHaveURL(link, { timeout: 20_000 });
  await expect(p2.getByText('A playlist shared by')).toBeVisible({ timeout: 20_000 });
  await expect(p2.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  await p2.getByRole('button', { name: 'Play', exact: true }).first().click();
  await expect(p2.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 20_000 });
  await p2.getByRole('button', { name: 'Save to my playlists' }).click();
  await expect(p2.getByText('Saved as')).toBeVisible();
  await other.close();
});

test('a song shared from its menu opens on its own', async ({ page, context }) => {
  test.setTimeout(90_000);
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await signIn(page);
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  await page.evaluate(() => { (navigator as any).share = undefined; });
  await page.getByRole('button', { name: 'More for Lab 4' }).click();
  await page.getByRole('menuitem', { name: 'Share' }).click();
  await expect(page.locator('.toast')).toContainText('Link copied');
  const link = await page.evaluate(() => navigator.clipboard.readText());
  await page.goto(link);
  await expect(page.getByText('A song shared by')).toBeVisible({ timeout: 20_000 });
  await expect(page.locator('.tracks .track')).toHaveCount(1, { timeout: 20_000 });
  await expect(page.locator('.tracks .track-title')).toHaveText('Lab 4');
});

test('a link that leads nowhere says so', async ({ page }) => {
  await signIn(page);
  await page.goto('/s/abcdefghjk');
  await expect(page.getByText("This link doesn't lead to anything")).toBeVisible({ timeout: 20_000 });
});

test('the phone app makes a share through /api/share and gets the link back', async ({ page }) => {
  await signIn(page);
  const r = await page.request.post('/api/share', { data: { kind: 'playlist', name: 'From the phone', entries: [{ p: 'x', t: 'Lab 2', a: 'Hires Lab' }, { p: 'y', t: 'Lab 3', a: 'Hires Lab' }] } });
  expect(r.ok()).toBe(true);
  const { url } = await r.json();
  expect(url).toMatch(/\/s\/[A-Za-z0-9]{10}$/);
  await page.goto(url);
  await expect(page.locator('.tracks .track')).toHaveCount(2, { timeout: 20_000 });
  const empty = await page.request.post('/api/share', { data: { kind: 'song', entries: [] } });
  expect(empty.status()).toBe(400);
});
