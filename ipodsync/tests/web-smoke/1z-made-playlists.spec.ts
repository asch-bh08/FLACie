import { test, expect, Page } from '@playwright/test';

// Ready-made playlists, like Spotify's (owner, 2 Nov): searching a genre or an artist shows "Playlists for you" above the results; one opens with
// Play, Shuffle and "Add to my playlists", which keeps a copy among the account's playlists. The mock library has 16 Electronic songs and 8 by each artist.

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

async function live(page: Page, url: string) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto(url);
  await ws; await page.waitForTimeout(800);
}

const noSideScroll = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1);

test('a genre search shows a made playlist above the results', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await signIn(page);
  await live(page, '/search?q=electronic');
  const row = page.getByRole('region', { name: 'Playlists for you' });
  await expect(row).toBeVisible({ timeout: 20_000 });
  await expect(row.locator('.card', { hasText: 'Electronic' })).toBeVisible();
  await expect(row.locator('.card-sub').first()).toContainText('made for you');
  // the row comes before the library's own results
  const rowTop = (await row.boundingBox())!.y;
  const songs = page.getByRole('region', { name: 'Songs' });
  if (await songs.count()) expect(rowTop).toBeLessThan((await songs.first().boundingBox())!.y);
  // nothing made matches a search for a song name
  await live(page, '/search?q=Lab 3');
  await expect(page.getByRole('region', { name: 'Songs' }).or(page.getByRole('region', { name: 'Top result' })).first()).toBeVisible({ timeout: 20_000 });
  await expect(page.getByRole('region', { name: 'Playlists for you' })).toHaveCount(0);
});

for (const [name, size] of [['phone', { width: 390, height: 844 }], ['pc', { width: 1440, height: 900 }]] as const) {
  test(`a Best of opens, plays and is added to my playlists (${name})`, async ({ page }) => {
    test.setTimeout(90_000);
    await page.setViewportSize(size);
    await signIn(page);
    await live(page, '/search?q=best of aurora');
    const card = page.getByRole('region', { name: 'Playlists for you' }).locator('.card', { hasText: 'Best of Aurora Vale' });
    await expect(card).toBeVisible({ timeout: 20_000 });
    await card.click();
    await page.waitForURL(/\/made\//);
    await page.waitForTimeout(800);
    const head = page.locator('.detail-head');
    await expect(head.locator('.eyebrow')).toHaveText('Made for you');
    await expect(head.locator('h1')).toContainText('Aurora');
    await expect(page.locator('.track')).toHaveCount(8);
    expect(await noSideScroll(page)).toBe(true);
    await page.screenshot({ path: `test-results/shots/made-${name}.png` });

    await expect(head.getByRole('button', { name: 'Play', exact: true })).toBeVisible();
    await expect(head.getByRole('button', { name: 'Shuffle' })).toBeVisible();

    await head.getByRole('button', { name: 'Add to my playlists' }).click();
    const added = head.getByRole('status');
    await expect(added).toHaveText('Added');
    await added.click();
    await page.waitForURL(/\/playlist\//);
    await expect(page.locator('.pl-hero h1')).toContainText('Aurora');
    await live(page, '/playlists');
    await expect(page.locator('.card', { hasText: 'Best of Aurora Vale' }).first()).toBeVisible();
  });
}

test('the phone API finds, opens and saves a made playlist', async ({ page }) => {
  await signIn(page);
  const found = await (await page.request.get('/api/made?q=electronic')).json();
  const list = found.lists.find((l: { name: string }) => l.name === 'Electronic');
  expect(list).toBeTruthy();
  expect(list.id).toBe('made:genre:electronic');
  expect(list.count).toBeGreaterThanOrEqual(6);
  const one = await (await page.request.get('/api/made/' + encodeURIComponent(list.id))).json();
  expect(one.tracks.length).toBe(list.count);
  expect(one.tracks[0]).toHaveProperty('path');
  expect(one.tracks[0]).toHaveProperty('durationMs');
  expect((await page.request.get('/api/made/' + encodeURIComponent('made:genre:polka'))).status()).toBe(404);
  const saved = await (await page.request.post('/api/made/' + encodeURIComponent(list.id) + '/save')).json();
  expect(saved.name).toMatch(/^Electronic/);
  expect(saved.count).toBe(list.count);
});
