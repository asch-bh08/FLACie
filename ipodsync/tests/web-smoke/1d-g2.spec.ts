import { test, expect, Page } from '@playwright/test';

// G2: short windows, unknown routes, About, keyboard shortcuts, buffering. Runs against the mock Jellyfin (see mock-jellyfin.mjs).

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

async function playFirst(page: Page, route = '/songs') {
  await page.goto(route);
  await page.waitForTimeout(2000);
  await page.locator('button.track-main').first().scrollIntoViewIfNeeded();
  await expect(async () => { await page.locator('button.track-main').first().click(); await expect(page.locator('.player')).toBeVisible({ timeout: 4000 }); }).toPass({ timeout: 30_000 });
  await expect(page.locator('.player')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 20_000 });
}

test.describe('signed in', () => {
  test.beforeEach(async ({ page }) => { await signIn(page); });

  for (const route of ['/this-is-nothing', '/song/xyz', '/folders', '/recent', '/about', '/playlist/']) {
    test(`unknown address ${route} shows the FLACie "page not found" page inside the layout, with a way home`, async ({ page }) => {
      const res = await page.goto(route);
      expect(res?.status()).toBe(404);
      await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible();
      await expect(page.locator('#main-rail')).toBeVisible();
      const home = page.locator('.notfound a[href="/"]');
      await expect(home).toBeVisible();
      await home.click();
      await expect(page).toHaveURL(/\/$/);
    });
  }

  test('an unknown link inside the app (no page load) also shows the not-found page', async ({ page }) => {
    await page.goto('/');
    await page.waitForTimeout(1500);
    await page.evaluate(() => { const a = document.createElement('a'); a.href = '/nothing-here'; a.id = 'x-nf'; a.textContent = 'x'; document.querySelector('main')!.appendChild(a); });
    await page.locator('#x-nf').click();
    await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible({ timeout: 10_000 });
  });

  test('artist and album routes ignore case', async ({ page }) => {
    await page.goto('/artist/aurora%20vale');
    await expect(page.locator('h1')).toContainText('Aurora Vale');
    await expect(page.locator('body')).not.toContainText("isn't in your library");
    await page.goto('/artist/AURORA%20VALE');
    await expect(page.locator('h1')).toContainText('Aurora Vale');
  });

  test('Settings > About shows the version, the server, the data folder and the licences', async ({ page }) => {
    await page.goto('/settings');
    await expect(page.locator('#about-version')).toHaveText('1.0-beta56');
    await expect(page.locator('#about-server')).toContainText('127.0.0.1');
    await expect(page.locator('#about-data')).not.toBeEmpty();
    const lic = page.locator('#about a[href="/licenses"]');
    await expect(lic).toBeVisible();
    const r = await page.request.get('/licenses');
    expect(r.status()).toBe(200);
    expect(await r.text()).toMatch(/FLACie|licen[cs]e/i);
  });

  for (const [w, h] of [[900, 700], [1280, 600], [1400, 700], [375, 667], [360, 360]]) {
    test(`full-screen Now Playing at ${w}x${h}: cover clear of the header, the title block and the bars; title visible`, async ({ page }) => {
      await page.setViewportSize({ width: w, height: h });
      await playFirst(page);
      await page.getByRole('button', { name: 'Open Now Playing' }).click();   // no force: wait until the bar has finished sliding in
      await expect(page.locator('.np')).toBeVisible();
      await page.waitForTimeout(1200);
      const r = await page.evaluate(() => {
        const q = (s: string) => document.querySelector(s)?.getBoundingClientRect();
        const cover = q('.np-cover'), top = q('.np-top'), h2 = q('.np-titles h2'), bar = q('.player'), np = q('.np');
        return { cover: cover && { t: cover.top, b: cover.bottom, w: cover.width }, top: top && { b: top.bottom }, h2: h2 && { t: h2.top, b: h2.bottom }, bar: bar && { t: bar.top }, np: np && { b: np.bottom } };
      });
      expect(r.cover!.t).toBeGreaterThanOrEqual(r.top!.b - 1);
      expect(r.h2!.t).toBeGreaterThanOrEqual(r.cover!.b - 1);
      expect(r.h2!.b).toBeLessThanOrEqual(h);
      await expect(page.locator('.np-titles h2')).toBeInViewport();
      const portrait = w <= 1024 && h >= w;
      if (!portrait) expect(r.h2!.b).toBeLessThanOrEqual(r.bar!.t + 1);
    });
  }

  test('at 900x600 the wordmark is hidden (the mark stays) and the search box is not overlapped; the A-Z rail ends above the player bar', async ({ page }) => {
    await page.setViewportSize({ width: 900, height: 600 });
    await playFirst(page);
    await expect(page.locator('.wordmark').first()).toBeHidden();
    await expect(page.locator('.logo-mark').first()).toBeVisible();
    const rail = page.locator('.az-rail');
    await expect(rail).toBeVisible();
    const rb = await rail.boundingBox(); const pb = await page.locator('.player').boundingBox();
    expect(rb!.y + rb!.height).toBeLessThanOrEqual(pb!.y);
  });

  test('the play button shows a buffering spinner right away while the song has no data yet', async ({ page }) => {
    await page.route('**/stream?p=*', async (route) => { await new Promise((r) => setTimeout(r, 3000)); await route.continue().catch(() => {}); });
    await playFirst(page);
    await expect(page.locator('html.buffering')).toHaveCount(1, { timeout: 2000 });
  });

  test('keyboard: Space plays/pauses, N skips, typing in a box is left alone', async ({ page }) => {
    await page.goto('/songs');
    await page.waitForTimeout(2000);
    await expect(async () => {
      await page.locator('input[type="search"]').first().fill('Velvet');
      await expect(page.locator('.track-title', { hasText: /^Velvet 1$/ })).toBeVisible({ timeout: 3000 });
    }).toPass({ timeout: 30_000 });
    await page.locator('.track', { has: page.locator('.track-title', { hasText: /^Velvet 1$/ }) }).locator('button.track-main').click();
    const play = page.locator('.bar .play');
    await expect(page.locator('.player')).toBeVisible();
    await expect.poll(async () => (await page.locator('.player .seek span').first().innerText()), { timeout: 15_000 }).not.toBe('0:00');
    await expect(play).toHaveAttribute('aria-label', 'Pause');
    await page.locator('h1').first().click();   // focus off the box
    await page.keyboard.press('Space');
    await expect(play).toHaveAttribute('aria-label', 'Play');
    await page.keyboard.press('Space');
    await expect(play).toHaveAttribute('aria-label', 'Pause');
    // typing a space into the search box does not toggle playback
    const box = page.locator('input[type="search"]').first();
    await box.click(); await page.keyboard.press('Space'); await page.keyboard.press('n');
    await expect(play).toHaveAttribute('aria-label', 'Pause');
    const before = await page.locator('.bar-text b').innerText();
    await page.locator('h1').first().click();
    await page.keyboard.press('n');
    await expect(page.locator('.bar-text b')).not.toHaveText(before, { timeout: 10_000 });
  });
});

test('an unknown address when signed out still shows a FLACie page with a way home', async ({ page }) => {
  const res = await page.goto('/folders');
  expect(res?.status()).toBe(404);
  await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible();
  await expect(page.locator('.notfound a[href="/"]')).toBeVisible();
});

// nothing may be cut off sideways on any page, at the sizes people really use
const sizes: [number, number][] = [[1400, 900], [768, 1024], [375, 667], [320, 568]];
const sweep = ['/', '/explore', '/songs', '/albums', '/artists', '/genres', '/charts', '/favorites', '/playlists', '/search?q=aurora', '/devices', '/settings', '/account', '/dashboard', '/downloads', '/import', '/jam', '/headphones', '/artist/Aurora%20Vale'];
for (const [w, h] of sizes) {
  test(`no page overflows sideways at ${w}x${h}`, async ({ page }) => {
    await signIn(page);
    await page.setViewportSize({ width: w, height: h });
    const bad: string[] = [];
    for (const route of sweep) {
      await page.goto(route);
      await page.waitForTimeout(900);
      const over = await page.evaluate(() => Math.max(document.documentElement.scrollWidth, document.body.scrollWidth) - window.innerWidth);
      if (over > 1) bad.push(`${route} +${over}px`);
    }
    expect(bad).toEqual([]);
  });
}