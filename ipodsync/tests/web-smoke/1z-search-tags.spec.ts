import { test, expect, Page } from '@playwright/test';

// Every search result says what it is (owner, 7 Nov: "a label or small symbol on every result, like Spotify"), and never shows only that word: each row and
// card keeps its title, on a phone and on a PC.
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

for (const [w, h] of [[390, 844], [1280, 800]]) {
  test(`at ${w}px songs, albums and artists each carry their kind and a visible title`, async ({ page }) => {
    test.setTimeout(90_000);
    await page.setViewportSize({ width: w, height: h });
    await signIn(page);
    await page.goto('/search?q=Lab');
    const songs = page.getByRole('region', { name: 'Songs' });
    await expect(songs.locator('.track').first()).toBeVisible({ timeout: 20_000 });
    await expect(songs.locator('.kind-tag').first()).toHaveText('Song');
    const albums = page.getByRole('region', { name: 'Albums' });
    await expect(albums.locator('.kind-tag').first()).toHaveText('Album');
    // every row and card shows a title with real width, not only its kind word
    const bad = await page.evaluate(() => [...document.querySelectorAll('.sr-section .track, .sr-section a.card, .sr-section button.card')].filter((r) => {
      const t = r.querySelector('.track-title, .card-title') as HTMLElement | null;
      if (!t) return true;
      const b = t.getBoundingClientRect();
      return t.innerText.trim().length === 0 || b.width < 8 || b.height < 8 || getComputedStyle(t).visibility === 'hidden';
    }).map((r) => (r as HTMLElement).innerText.slice(0, 40)));
    expect(bad).toEqual([]);
    await page.screenshot({ path: `test-results/shots/search-tags-${w}.png`, fullPage: true });
    // the artist layout too (Top result, albums and playlists half each), and the Playlists kind on its own
    for (const url of ['/search?q=' + encodeURIComponent('Hires Lab'), '/search?q=Lab']) {
      await page.goto(url);
      await expect(page.locator('.sr-section').first()).toBeVisible({ timeout: 20_000 });
      await page.waitForTimeout(1000);
      if (url.endsWith('=Lab')) await page.getByRole('radiogroup', { name: 'Show only' }).getByRole('radio', { name: 'Playlists' }).click();
      await page.waitForTimeout(1000);
      const bad2 = await page.evaluate(() => [...document.querySelectorAll('.sr-section a.card, .sr-section button.card, .made-row a.card')].filter((r) => {
        const t = r.querySelector('.card-title') as HTMLElement | null;
        if (!t) return true;
        const b = t.getBoundingClientRect();
        return t.innerText.trim().length === 0 || b.width < 8 || b.height < 8;
      }).map((r) => (r as HTMLElement).innerText.slice(0, 40)));
      expect(bad2, url).toEqual([]);
      await page.screenshot({ path: `test-results/shots/search-tags-${w}-${url.endsWith('=Lab') ? 'lists' : 'artist'}.png`, fullPage: true });
    }
  });
}
