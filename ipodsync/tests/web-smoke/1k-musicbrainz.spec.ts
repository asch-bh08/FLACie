import { test, expect, Page } from '@playwright/test';
import http from 'node:http';

// The MusicBrainz match in the Info panel, against a stand-in for MusicBrainz (FLACIE_CATALOG_REDIRECT sends musicbrainz.org here, see playwright.config.ts):
//  - nothing is asked of MusicBrainz until the button is pressed (not at launch, not when the song or the Info tab opens)
//  - the request carries a descriptive User-Agent and the song's title and artist
//  - the best match is first, a person chooses, the choice is kept and can be forgotten
const FM = Number(process.env.FM_PORT || 8197);
const seen: { url: string; ua: string }[] = [];
const server = http.createServer((req, res) => {
  const u = new URL(req.url ?? '/', 'http://x');
  if (u.pathname === '/musicbrainz.org/ws/2/recording') {
    seen.push({ url: u.search, ua: String(req.headers['user-agent'] ?? '') });
    res.writeHead(200, { 'content-type': 'application/json' });
    return res.end(JSON.stringify({ recordings: [
      { id: 'mbid-live', score: 90, title: 'Lab 1 (Live)', length: 300000, 'artist-credit': [{ name: 'Hires Lab' }], releases: [{ id: 'rel-live', title: 'Lab Live', date: '2001' }] },
      { id: 'mbid-lab1', score: 100, title: 'Lab 1', length: 120000, 'artist-credit': [{ name: 'Hires Lab' }], releases: [{ id: 'rel-lab', title: 'Lab Album', date: '2020-01-02', 'release-group': { id: 'rg-lab' } }] },
    ] }));
  }
  res.writeHead(200, { 'content-type': 'application/json' }); res.end('{}');
});

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

async function openInfo(page: Page) {
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  await page.locator('.tracks .track-main').first().click();
  await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 20_000 });
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await page.getByRole('tab', { name: 'Info' }).click();
  await expect(page.locator('#mb-match')).toBeVisible({ timeout: 20_000 });
}

test.describe.serial('musicbrainz match', () => {
  test.beforeAll(async () => { await new Promise<void>((ok) => server.listen(FM, '127.0.0.1', ok)); });
  test.afterAll(async () => { await new Promise<void>((ok) => server.close(() => ok())); });

  test('nothing is asked until the button is pressed; then the best match comes first and the choice is kept', async ({ page }) => {
    test.setTimeout(120_000);
    seen.length = 0;
    await signIn(page);
    await openInfo(page);
    await page.waitForTimeout(1500);
    expect(seen.length, 'no MusicBrainz request before the button').toBe(0);
    await expect(page.locator('#mb-match')).toContainText('Not looked up yet');

    await page.getByRole('button', { name: 'Find on MusicBrainz' }).click();
    await expect(page.locator('.mb-results li')).toHaveCount(2, { timeout: 20_000 });
    expect(seen.length).toBe(1);
    expect(seen[0].ua).toMatch(/^FLACie\/\S+ \( https:\/\/github\.com\/asch-bh08\/FLACie \)$/);
    expect(decodeURIComponent(seen[0].url)).toContain('recording:"Lab 1"');
    expect(decodeURIComponent(seen[0].url)).toContain('artist:"Hires Lab"');
    await expect(page.locator('.mb-results li').first()).toContainText('Lab 1');
    await expect(page.locator('.mb-results li').first()).not.toContainText('Live');
    await expect(page.locator('.mb-results li').first().locator('.mb-score')).toHaveText(/^(8\d|9\d|100)%$/);

    await page.locator('.mb-results li').first().getByRole('button', { name: 'Use this match' }).click();
    await expect(page.locator('#mb-match')).toContainText('Your match: Lab 1');
    await expect(page.locator('#mb-match a')).toHaveAttribute('href', 'https://musicbrainz.org/recording/mbid-lab1');

    // the choice survives a reload; the same lookup again costs MusicBrainz nothing (cached)
    await page.reload();
    await page.waitForLoadState('load');
    await page.waitForTimeout(1500);
    const before = seen.length;
    await page.getByRole('button', { name: 'Open Now Playing' }).click().catch(() => { });
    await page.getByRole('tab', { name: 'Info' }).click().catch(() => { });
    await expect(page.locator('#mb-match')).toContainText('Your match: Lab 1', { timeout: 20_000 });
    await page.getByRole('button', { name: 'Change…' }).click();
    await expect(page.locator('.mb-results li')).toHaveCount(2, { timeout: 20_000 });
    expect(seen.length, 'answered from the cache').toBe(before);

    await page.getByRole('button', { name: 'Forget this match' }).click();
    await expect(page.locator('#mb-match')).toContainText('Not looked up yet');
  });
});
