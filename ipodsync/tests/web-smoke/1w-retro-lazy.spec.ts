import { test, expect } from '@playwright/test';
// (named 1w- so it runs before 2-login, whose wrong-password tests lock this address out of signing in)

// The Retro / iPod theme's styles and scripts load only where it is shown (/retro, /ipod): an ordinary page does not download them (about 30% of its
// bytes), and moving into Retro from the app fetches them first, in place: no reload, so the music keeps playing.
const RETRO = /(ipod|ipod-touch|desk|retro|design-j-retro)\.[^/]*css|\/(desk|retro)\.[^/]*js/;

test.beforeEach(async ({ page }) => {
  await page.goto('/login'); await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester'); await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click(); await page.waitForURL((u) => !u.pathname.startsWith('/login'));
});

test('Home does not load the Retro theme; /retro does, and a Retro link from the app gets it', async ({ page }) => {
  const asked: string[] = [];
  page.on('request', (r) => { if (RETRO.test(r.url())) asked.push(r.url()); });
  await page.goto('/'); await page.waitForTimeout(1500);
  expect(asked).toEqual([]);
  expect(await page.evaluate(() => !!(window as any).retro)).toBe(false);
  // the account menu's Retro link: the files are fetched before the theme draws
  await page.evaluate(() => { (window as any).__samePage = true; });   // gone after a reload: proves the move is in place
  await page.locator('.avatar-btn').click();
  await page.locator('.account-menu').getByRole('menuitem', { name: 'Retro' }).click();
  await page.waitForURL(/\/retro/);
  await page.waitForFunction(() => !!(window as any).retro && !!(window as any).desk, null, { timeout: 20_000 });
  expect(asked.length).toBeGreaterThanOrEqual(7);
  expect(await page.evaluate(() => (window as any).__samePage === true)).toBe(true);
  // the stylesheets went back where they stood: before design-j-music.css, so the cascade is as it was
  expect(await page.evaluate(() => { const l = [...document.querySelectorAll('link[rel=stylesheet]')].map((x) => x.getAttribute('href') || ''); return l.findIndex((h) => /retro\./.test(h)) < l.findIndex((h) => /design-j-music/.test(h)); })).toBe(true);
});
