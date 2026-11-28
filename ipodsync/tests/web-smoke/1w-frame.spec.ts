import { test, expect, Page } from '@playwright/test';
// (named 1w- so it runs before 2-login, whose wrong-password tests lock this address out of signing in)

// ONE APP 1k, the one frame (docs/plans/one-app-consolidate.md): walks every page and fails if its back button, tab bar, lit tab, title style
// or close differ from its type. Standard: the frame's top bar (back top left except on the five tab roots, the serif title with its last word in
// italics, no header of the page's own), the tab bar with the right tab lit. Full screen: no bars, its close top left.
const STATE = 'test-results/frame-state.json';
test.beforeAll(async ({ browser }) => {
  const page = await browser.newPage();
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.context().storageState({ path: STATE });
  await page.close();
});

async function open(page: Page, url: string) {
  const state = JSON.parse(require('fs').readFileSync(STATE, 'utf8'));
  await page.context().addCookies(state.cookies);
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 }).catch(() => null);
  await page.goto(url); await ws; await page.waitForTimeout(1200);
}

type P = [path: string, tab: string, title: string | null];
const STANDARD: P[] = [
  ['/', 'Home', null], ['/search', 'Search', null], ['/library', 'Library', 'Your library'], ['/devices', 'Devices', 'Your devices'], ['/settings', 'Settings', 'Settings'],
  ['/songs', 'Library', 'Songs'], ['/albums', 'Library', 'Albums'], ['/artists', 'Library', 'Artists'], ['/genres', 'Library', 'Genres'],
  ['/explore', 'Search', 'Explore'], ['/charts', 'Search', 'Charts'], ['/favorites', 'Library', 'Favourites'], ['/playlists', 'Library', 'Playlists'],
  ['/artist/Aurora%20Vale', 'Library', 'Aurora Vale'], ['/genre/Rock', 'Library', 'Rock'], ['/jam', 'Devices', 'Jam'], ['/account', 'Settings', 'Account'],
  ['/downloads', 'Settings', 'Downloads'], ['/dashboard', 'Settings', 'Server admin'], ['/headphones', 'Settings', 'Headphone test'], ['/import', 'Library', 'Import playlists'],
];
const ROOTS = ['/', '/search', '/library', '/devices', '/settings'];

for (const [w, h] of [[390, 844], [1366, 900]]) {
  test(`every Standard page at ${w}x${h} has the frame's top bar, back, title and tab bar`, async ({ page }) => {
    test.setTimeout(240_000);
    await page.setViewportSize({ width: w, height: h });
    const bad: string[] = [];
    for (const [path, tab, title] of STANDARD) {
      await open(page, path);
      const back = page.locator('.topbar .frame-back');
      const wantBack = !ROOTS.includes(path);
      if ((await back.count()) !== (wantBack ? 1 : 0)) bad.push(`${path}: back button ${wantBack ? 'missing' : 'should not be there'}`);
      if (wantBack) {
        const b = (await back.boundingBox())!;
        if (b.x > 24 || b.y > 20) bad.push(`${path}: back is not top left (${Math.round(b.x)},${Math.round(b.y)})`);
      }
      if (!(await page.locator('#main-rail').isVisible())) bad.push(`${path}: no tab bar`);
      const lit = (await page.locator('#main-rail .rail-main a.active').allInnerTexts()).map((x) => x.trim());
      if (lit.length !== 1 || lit[0] !== tab) bad.push(`${path}: lit tab ${JSON.stringify(lit)}, want ${tab}`);
      if (title) {
        const t = page.locator('.topbar .frame-title');
        if ((await t.count()) !== 1) { bad.push(`${path}: no frame title`); continue; }
        const txt = (await t.innerText()).replace(/\s+/g, ' ').trim();
        if (txt !== title) bad.push(`${path}: title "${txt}", want "${title}"`);
        const font = await t.evaluate((e) => getComputedStyle(e).fontFamily);
        if (!/Instrument Serif/.test(font)) bad.push(`${path}: title not serif (${font})`);
        const last = (await t.locator('i').innerText()).trim();
        if (last !== title.split(' ').pop()) bad.push(`${path}: last word "${last}" not in italics`);
      }
      if (await page.locator('main .page-head').count()) bad.push(`${path}: the page still draws its own header`);
      // one heading per page: the frame's title, or the hero's own on an album, artist or playlist page
      if ((await page.locator('h1:not(.sr-only)').count()) > 1) bad.push(`${path}: more than one h1`);
      if (await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth) > 0) bad.push(`${path}: sideways scroll`);
    }
    expect(bad).toEqual([]);
  });
}

for (const w of [320, 360, 390, 412, 690]) {
  test(`at ${w} px the five tabs share the whole width of the bar equally, no gap at either end`, async ({ page }) => {
    await page.setViewportSize({ width: w, height: 800 });
    await open(page, '/library');
    const tabs = await page.locator('#main-rail .rail-main > a').evaluateAll((as) => as.filter((a) => a.getBoundingClientRect().width > 0).map((a) => { const r = a.getBoundingClientRect(); return { x: r.x, r: r.right, w: r.width }; }));
    expect(tabs).toHaveLength(5);
    expect(tabs[0].x).toBeLessThanOrEqual(6);
    expect(tabs[4].r).toBeGreaterThanOrEqual(w - 6);
    for (const t of tabs) expect(Math.abs(t.w - tabs[0].w)).toBeLessThanOrEqual(1);
  });
}

test('moving from a list to one of its items changes the title (Albums -> an album)', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, '/albums');
  await expect(page.locator('.topbar .frame-title')).toHaveText(/Albums/);
  await page.locator('.grid .card').first().click();
  await page.waitForURL(/\/album\//); await page.waitForTimeout(800);
  const name = (await page.locator('.detail-head h1').innerText()).replace(/\s+/g, ' ').trim();
  await expect(page.locator('.topbar .frame-title')).toHaveText(name);
});

test('Full screen (Car): no top bar, no tab bar, no player bar, its close top left; back from a Standard page returns', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, '/playlists');
  await open(page, '/car');
  await expect(page.locator('.app')).toHaveClass(/frame-full/);
  for (const sel of ['.topbar', '#main-rail', '.player']) await expect(page.locator(sel)).toBeHidden();
  const c = (await page.locator('[data-frame-close]').boundingBox())!;
  expect(c.x).toBeLessThan(390 / 3); expect(c.y).toBeLessThan(120);
  await open(page, '/albums');
  await page.locator('.topbar .frame-back').click();
  await page.waitForTimeout(800);
  await expect(page).toHaveURL(/\/car$/);
});
