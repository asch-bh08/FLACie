import { test, expect, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Accessibility (the 1.0 bar): every main page passes axe's WCAG 2 A and AA checks, on a phone and on a PC.

// one sign-in for the whole file (signing in twenty times in a row is slowed down on purpose by the login throttle), kept as the browser's state
const STATE = 'test-results/a11y-state.json';
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
  if (signedIn) {
    const state = JSON.parse(require('fs').readFileSync(STATE, 'utf8'));
    await page.context().addCookies(state.cookies);
    return;
  }
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
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 }).catch(() => null);
  await page.goto(url); await ws; await page.waitForTimeout(2000);
}

const scan = async (page: Page) => {
  const r = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  return r.violations.map((v) => `${v.id} (${v.impact}): ${v.nodes.length}× e.g. ${v.nodes[0]?.target.join(' ')}`);
};

const pages = ['/', '/search', '/search?q=Lab', '/songs', '/albums', '/artists', '/playlists', '/favorites', '/settings', '/artist/Hires%20Lab'];

for (const size of [{ name: 'phone', width: 390, height: 844 }, { name: 'PC', width: 1440, height: 900 }]) {
  test.describe(`accessibility on a ${size.name}`, () => {
    for (const url of pages) {
      test(`${url} passes axe`, async ({ page }) => {
        test.setTimeout(90_000);
        await page.setViewportSize({ width: size.width, height: size.height });
        await signIn(page);
        await live(page, url);
        expect(await scan(page)).toEqual([]);
      });
    }
  });
}

test('the login page passes axe', async ({ page }) => {
  await page.goto('/login');
  expect(await scan(page)).toEqual([]);
});

for (const size of [{ name: 'phone', width: 390, height: 844 }, { name: 'PC', width: 1440, height: 900 }]) {
  test(`the full-screen player passes axe on a ${size.name}`, async ({ page }) => {
    test.setTimeout(90_000);
    await page.setViewportSize({ width: size.width, height: size.height });
    await signIn(page);
    await live(page, '/artist/Hires%20Lab');
    await page.getByRole('link', { name: /Lab Album/ }).first().click();
    await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
    await page.locator('.tracks .track-main').first().click();
    await page.getByRole('button', { name: 'Pause', exact: true }).first().click();
    await page.getByRole('button', { name: 'Open Now Playing' }).click();
    await page.waitForTimeout(2500);
    expect(await scan(page)).toEqual([]);
  });
}

test('with reduced motion on, nothing on the page animates or eases', async ({ page }) => {
  test.setTimeout(90_000);
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await signIn(page);
  await live(page, '/');
  const slow = await page.evaluate(() => {
    const ms = (v: string) => Math.max(...v.split(',').map((x) => (x.trim().endsWith('ms') ? parseFloat(x) : parseFloat(x) * 1000)));
    return [...document.querySelectorAll('*')].filter((e) => { const s = getComputedStyle(e); return ms(s.animationDuration) > 1 || ms(s.transitionDuration) > 1; }).length;
  });
  expect(slow).toBe(0);
});

test('every Tab stop on Home shows where the focus is', async ({ page }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await live(page, '/');
  const hidden: string[] = [];
  for (let i = 0; i < 40; i++) {
    await page.keyboard.press('Tab');
    await page.waitForTimeout(60);
    const r = await page.evaluate(() => {
      const e = document.activeElement as HTMLElement | null;
      if (!e || e === document.body) return null;
      const shows = (el: Element) => { const s = getComputedStyle(el); return (s.outlineStyle !== 'none' && parseFloat(s.outlineWidth) > 0) || (s.boxShadow !== 'none' && s.boxShadow !== ''); };
      // a field may show it on its frame instead (:focus-within)
      return shows(e) || (e.parentElement && shows(e.parentElement)) ? null : `${e.tagName}.${e.className} ${(e.getAttribute('aria-label') || e.textContent || '').trim().slice(0, 30)}`;
    });
    if (r) hidden.push(r);
  }
  expect(hidden).toEqual([]);
});
