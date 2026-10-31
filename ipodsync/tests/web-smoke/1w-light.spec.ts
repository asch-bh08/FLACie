import { test, expect, Page } from '@playwright/test';
// (named 1w- so it runs before 2-login, whose wrong-password tests lock this address out of signing in for 15 minutes)
import AxeBuilder from '@axe-core/playwright';

// The light theme (the 1.0 plan: light, dark and system themes everywhere, as the phone has): Settings → Appearance → Theme picks Dark, Light
// or System (follows the computer). It is set on the page before it is first drawn, so a light page never flashes dark. Every main page
// still passes axe's WCAG AA checks in light, on a phone and on a PC. Retro and the iPod keep their own scenes in either theme.

const STATE = 'test-results/light-state.json';
let signedIn = false;
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
  signedIn = true;
});

async function signIn(page: Page) {
  const state = JSON.parse(require('fs').readFileSync(STATE, 'utf8'));
  await page.context().addCookies(state.cookies);
}

async function live(page: Page, url: string) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 }).catch(() => null);
  await page.goto(url); await ws; await page.waitForTimeout(2000);
  // a page still saying "Loading…" swaps that line for its result in a moment, and the new line fades in: judge it once it has settled
  await page.waitForFunction(() => ![...document.querySelectorAll('.status')].some((e) => /^Loading/.test(e.textContent ?? '')), null, { timeout: 20_000 }).catch(() => null);
}

// the theme as this browser keeps it (flacie.ui in localStorage), set before any page script runs
const withTheme = (page: Page, theme: string) => page.addInitScript((t) => {
  try { const p = JSON.parse(localStorage.getItem('flacie.ui') || '{}'); if (!p._seeded) { p.theme = t; p._seeded = true; localStorage.setItem('flacie.ui', JSON.stringify(p)); } } catch { }
}, theme);

const bgLuminance = (page: Page) => page.evaluate(() => {
  const m = getComputedStyle(document.body).backgroundColor.match(/\d+(\.\d+)?/g)!.map(Number);
  const lin = (c: number) => { c /= 255; return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4; };
  return 0.2126 * lin(m[0]) + 0.7152 * lin(m[1]) + 0.0722 * lin(m[2]);
});

const scan = async (page: Page) => {
  await page.evaluate(() => Promise.all(document.getAnimations().filter((a) => a.effect?.getTiming().iterations !== Infinity).map((a) => a.finished.catch(() => null))));
  const r = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  return r.violations.map((v) => `${v.id} (${v.impact}): ${v.nodes.length}× e.g. ${v.nodes[0]?.target.join(" ")}: ${v.nodes[0]?.any[0]?.message ?? ""}`);
};

test('Settings → Appearance → Theme switches between Dark, Light and System, and is kept', async ({ page }) => {
  test.setTimeout(90_000);
  await signIn(page);
  await live(page, '/settings');
  const theme = page.locator('#look').getByRole('radiogroup', { name: 'Theme' });
  await expect(theme.getByRole('radio', { name: 'Dark' })).toHaveAttribute('aria-checked', 'true');
  expect(await bgLuminance(page)).toBeLessThan(0.05);

  await theme.getByRole('radio', { name: 'Light' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  expect(await bgLuminance(page)).toBeGreaterThan(0.75);

  // kept: the page is light from its first paint (set in <head>, before Blazor)
  await page.goto('/', { waitUntil: 'commit' });
  await page.waitForSelector('body');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await page.waitForLoadState('load');
  await page.screenshot({ path: 'test-results/light-home.png' });

  // System follows the computer
  await live(page, '/settings');
  await page.locator('#look').getByRole('radiogroup', { name: 'Theme' }).getByRole('radio', { name: 'System' }).click();
  await page.emulateMedia({ colorScheme: 'light' });
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');

  // back to the default for later specs
  await page.locator('#look').getByRole('radiogroup', { name: 'Theme' }).getByRole('radio', { name: 'Dark' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
});

const pages = ['/', '/search', '/search?q=Lab', '/songs', '/albums', '/artists', '/playlists', '/favorites', '/settings', '/artist/Hires%20Lab',
  '/downloads', '/genres', '/genre/Rock', '/explore', '/headphones', '/account', '/devices', '/charts'];

for (const size of [{ name: 'phone', width: 390, height: 844 }, { name: 'PC', width: 1440, height: 900 }]) {
  test.describe(`light theme on a ${size.name}`, () => {
    for (const url of pages) {
      test(`${url} is light and passes axe`, async ({ page }) => {
        test.setTimeout(90_000);
        await page.setViewportSize({ width: size.width, height: size.height });
        await withTheme(page, 'light');
        await signIn(page);
        await live(page, url);
        expect(await bgLuminance(page)).toBeGreaterThan(0.75);
        if (['/', '/songs', '/settings', '/artist/Hires%20Lab', '/search?q=Lab', '/downloads', '/headphones'].includes(url))
          await page.screenshot({ path: `test-results/light-${size.name}-${url.replace(/[^a-z]+/gi, '_')}.png` });
        expect(await scan(page)).toEqual([]);
      });
    }
  });
}

test('the login page is light and passes axe', async ({ page }) => {
  await withTheme(page, 'light');
  await page.goto('/login');
  await page.waitForTimeout(1000);
  expect(await bgLuminance(page)).toBeGreaterThan(0.75);
  await page.screenshot({ path: 'test-results/light-login.png' });
  expect(await scan(page)).toEqual([]);
});

for (const size of [{ name: 'phone', width: 390, height: 844 }, { name: 'PC', width: 1440, height: 900 }]) {
  test(`an album page and the full-screen player pass axe in the light theme on a ${size.name}`, async ({ page }) => {
    test.setTimeout(90_000);
    await page.setViewportSize({ width: size.width, height: size.height });
    await withTheme(page, 'light');
    await signIn(page);
    await live(page, '/artist/Hires%20Lab');
    await page.getByRole('link', { name: /Lab Album/ }).first().click();
    await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
    await page.waitForTimeout(1500);
    if (size.name === 'PC') await page.screenshot({ path: 'test-results/light-album.png' });
    expect(await scan(page)).toEqual([]);
    await page.locator('.tracks .track-main').first().click();
    await page.getByRole('button', { name: 'Pause', exact: true }).first().click();
    if (size.name === 'PC') await page.screenshot({ path: 'test-results/light-bar.png' });
    expect(await scan(page)).toEqual([]);
    await page.getByRole('button', { name: 'Open Now Playing' }).click();
    await page.waitForTimeout(2500);
    if (size.name === 'PC') await page.screenshot({ path: 'test-results/light-player.png' });
    expect(await scan(page)).toEqual([]);
  });
}

// menus, pop-ups and dialogs are only on screen when opened: each is checked open, in light
test('menus, the filters, the shortcut list and the account menu read in the light theme', async ({ page }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await withTheme(page, 'light');
  await signIn(page);
  await live(page, '/songs');
  await page.getByRole('button', { name: /^More for / }).first().click();
  await page.waitForTimeout(400);
  await page.screenshot({ path: 'test-results/light-menu.png' });
  expect(await scan(page)).toEqual([]);
  await page.keyboard.press('Escape');
  await live(page, '/search?q=Lab');
  await page.getByRole('button', { name: /Filters/ }).first().click();
  await page.waitForTimeout(400);
  await page.screenshot({ path: 'test-results/light-filters.png' });
  expect(await scan(page)).toEqual([]);
  await page.keyboard.press('Escape');
  await live(page, '/');
  await page.locator('body').click({ position: { x: 700, y: 200 } });
  await page.keyboard.press('Shift+Slash');
  await page.waitForTimeout(400);
  await page.screenshot({ path: 'test-results/light-shortcuts.png' });
  expect(await scan(page)).toEqual([]);
  await page.keyboard.press('Escape');
  await page.locator('.avatar').first().click();
  await page.waitForTimeout(400);
  await page.screenshot({ path: 'test-results/light-account.png' });
  expect(await scan(page)).toEqual([]);
});
