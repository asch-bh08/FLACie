import { test, expect, Page } from '@playwright/test';
import http from 'node:http';

// The server-wide "Allow downloads" switch (Dashboard > Downloads on this server) and what it hides. The smoke server starts with FLACIE_DOWNLOADS=on
// (playwright.config.ts), so the ordinary specs keep their download buttons: the first block checks the switch is there, shows it is set by the host,
// and that the YouTube source is offered nowhere. The second block only runs on a server started with FLACIE_DOWNLOADS=off:
//   FLACIE_DOWNLOADS=off MOCK_PORT=8396 WEB_PORT=5490 FM_PORT=8397 npx playwright test 1z-downloads-switch
// and checks that a player-only server shows no download buttons, no "set up downloads" hints, no Fetch ahead and no import page.

const OFF = (process.env.FLACIE_DOWNLOADS || 'on') === 'off';
const FM = Number(process.env.FM_PORT || 8197);

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

// A page is read once it is live (the first copy shown on load is replaced when the live connection starts).
async function openLive(page: Page, path: string) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto(path);
  await ws; await page.waitForTimeout(800);
}

test.describe('downloads allowed (FLACIE_DOWNLOADS=on)', () => {
  test.skip(OFF, 'this server was started with downloads off');
  test.beforeEach(async ({ page }) => { await signIn(page); });

  test('the Dashboard has Allow downloads, set by the host, and the switch for Download all and imports', async ({ page }) => {
    await openLive(page, '/dashboard');
    const sec = page.locator('#allow-downloads');
    await expect(sec).toBeVisible({ timeout: 15_000 });
    const allow = sec.locator('.switch-row', { hasText: 'Allow downloads' }).locator('input[role="switch"]');
    await expect(allow).toBeChecked();
    await expect(allow).toBeDisabled();
    await expect(sec).toContainText('Set by FLACIE_DOWNLOADS');
    const bulk = sec.locator('.switch-row', { hasText: 'Let everyone use Download all and imports' }).locator('input[role="switch"]');
    await expect(bulk).toBeVisible();
    await expect(bulk).not.toBeChecked();                                     // off until an administrator turns it on
    await expect(page.locator('#downloads')).toBeVisible();                    // the services show while downloads are allowed
  });

  test('the YouTube source is offered nowhere on the Dashboard', async ({ page }) => {
    await openLive(page, '/dashboard');
    const open = page.locator('#opensources');
    await expect(open).toBeVisible({ timeout: 15_000 });
    await expect(open.locator('.src-row')).toHaveCount(3);                     // Internet Archive, Audius, Jamendo
    await expect(page.locator('body')).not.toContainText(/YouTube|yt-dlp/);
  });

  test('Settings still offers Fetch ahead', async ({ page }) => {
    await openLive(page, '/settings');
    await expect(page.locator('#playback')).toContainText('Fetch ahead', { timeout: 15_000 });
  });
});

test.describe('downloads off (FLACIE_DOWNLOADS=off)', () => {
  test.skip(!OFF, 'only on a server started with FLACIE_DOWNLOADS=off');

  // the public catalog, as FLACie Web asks for it when FLACIE_CATALOG_REDIRECT points here: one song the library doesn't have, for Search's "More music"
  let server: http.Server;
  test.beforeAll(async () => {
    server = http.createServer((req, res) => {
      const u = new URL(req.url!, `http://127.0.0.1:${FM}`);
      res.writeHead(200, { 'content-type': 'application/json' });
      if (u.pathname === '/api.deezer.com/search') return res.end(JSON.stringify({ data: [{ id: 991, title: 'Elsewhere Song', duration: 200, artist: { name: 'Nobody Here' }, album: { title: 'Far Album', cover_xl: '' } }] }));
      if (u.pathname === '/itunes.apple.com/search') return res.end(JSON.stringify({ results: [] }));
      if (u.pathname.startsWith('/musicbrainz.org/')) return res.end(JSON.stringify({ artists: [] }));
      res.end(JSON.stringify({ data: [] }));
    });
    await new Promise<void>((r) => server.listen(FM, '127.0.0.1', () => r()));
  });
  test.afterAll(async () => { await new Promise<void>((r) => server.close(() => r())); });
  test.beforeEach(async ({ page }) => { await signIn(page); });

  test('the Dashboard shows the switch off and none of the download sections', async ({ page }) => {
    await openLive(page, '/dashboard');
    const sec = page.locator('#allow-downloads');
    await expect(sec).toBeVisible({ timeout: 15_000 });
    await expect(sec.locator('.switch-row', { hasText: 'Allow downloads' }).locator('input[role="switch"]')).not.toBeChecked();
    await expect(sec).not.toContainText('Let everyone use Download all and imports');
    for (const id of ['#downloads', '#opensources', '#background']) await expect(page.locator(id)).toHaveCount(0);
  });

  test('Search lists what exists without a Download button or a set-up hint', async ({ page }) => {
    await openLive(page, '/search?q=elsewhere');
    await expect(page.getByText('Elsewhere Song')).toBeVisible({ timeout: 20_000 });
    await expect(page.locator('.track-actions .btn', { hasText: 'Download' })).toHaveCount(0);
    await expect(page.locator('body')).not.toContainText('To download these');
  });

  test('Settings has no Fetch ahead, and the import page is closed', async ({ page }) => {
    await openLive(page, '/settings');
    await expect(page.locator('#playback')).toBeVisible({ timeout: 15_000 });
    await expect(page.locator('#playback')).not.toContainText('Fetch ahead');
    await expect(page.locator('.subnav')).not.toContainText('Download log');
    await openLive(page, '/import');
    await expect(page.locator('.sp')).toContainText("isn't turned on for your account", { timeout: 15_000 });
    await expect(page.locator('input[type="file"]')).toHaveCount(0);
    await openLive(page, '/playlists');
    await expect(page.locator('a[href="/import"]')).toHaveCount(0);
  });
});
