import { test, expect, Page } from '@playwright/test';

// The owner (overhaul 10 Nov): "peak 2026" motion everywhere, and none of it when Settings → Animations is off.
// Pages cross-fade (a view transition), the player bar's cover flies into Now Playing, menus spring open, Up next rows glide to their new place,
// and nothing is left with a transform or a view-transition-name afterwards (a leftover one traps menus under the page).

async function signIn(page: Page, calm = false) {
  // Animations off is kept in this browser (flacie.ui), set before any page script runs
  await page.addInitScript((c) => { try { localStorage.setItem('flacie.ui', JSON.stringify({ motion: !c })); } catch { } }, calm);
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(1500);
}

const runs = (page: Page) => page.evaluate(() => (window as any).flacieMotion.runs as number);
const idle = (page: Page) => expect.poll(() => page.evaluate(() => (window as any).flacieMotion.active as boolean)).toBe(false);
// anything still carrying a view-transition-name (the page itself is always 'root', the browser's own), or a menu still moved by a transform
const leftovers = (page: Page) => page.evaluate(() => [...document.querySelectorAll('body, body *')]
  .filter((e) => getComputedStyle(e).viewTransitionName !== 'none' || (e.matches('.menu, .account-menu, .q-item') && getComputedStyle(e).transform !== 'none'))
  .map((e) => e.tagName + '.' + (e as HTMLElement).className));

async function playAlbum(page: Page) {
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await page.locator('.tracks .track-main').first().click();
  await expect(page.locator('.player')).toBeVisible();
}

test('a page change runs a view transition, and nothing is left named after it', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await idle(page);
  const before = await runs(page);
  await page.locator('#main-rail').getByRole('link', { name: 'Favourites' }).click();
  await page.waitForURL(/\/favorites/);
  expect(await runs(page)).toBeGreaterThan(before);
  expect(await page.evaluate(() => (window as any).flacieMotion.last)).toBe('page');
  await idle(page);
  expect(await leftovers(page)).toEqual([]);
});

test('the player bar cover flies into Now Playing: named only while it moves', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await playAlbum(page);
  await idle(page);
  await page.locator('.player .bar-song').click();
  // during the move one cover carries the name (first the bar's, then the big one)
  const during = await page.evaluate(() => [...document.querySelectorAll('.cover')].filter((e) => (e as HTMLElement).style.viewTransitionName === 'm-cover').length);
  expect(during).toBe(1);
  await expect(page.locator('.np')).toBeVisible();
  await idle(page);
  expect(await page.evaluate(() => (window as any).flacieMotion.last)).toBe('np');
  expect(await leftovers(page)).toEqual([]);
  // and back: closing flies it to the bar; afterwards nothing is named and the player is gone
  await page.getByRole('button', { name: 'Close Now Playing' }).click();
  await expect(page.locator('.np')).toHaveCount(0);
  await idle(page);
  expect(await leftovers(page)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.className)).not.toMatch(/np-morph|np-hide|vt-/);
});

test('a menu springs open, and settles with no transform left', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await playAlbum(page);
  await page.getByRole('button', { name: 'More player options' }).click();
  const menu = page.locator('.player .bar-menu');
  await expect(menu).toBeVisible();
  const anim = await menu.evaluate((m) => m.getAnimations().map((a) => (a as CSSAnimation).animationName));
  expect(anim).toContain('m-pop');
  await page.waitForTimeout(500);
  expect(await menu.evaluate((m) => getComputedStyle(m).transform)).toBe('none');
  expect(await leftovers(page)).toEqual([]);
});

test('Up next: moving a song down glides the rows, and the order is right', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await playAlbum(page);
  await page.locator('.player [aria-label="Full screen player"]').click();
  await expect(page.locator('.np .upnext .q-item').nth(1)).toBeVisible();
  await idle(page);
  const titles = () => page.locator('.np .upnext .q-item b').allTextContents();
  const was = await titles();
  expect(was.length).toBeGreaterThan(1);
  // pressed in the page, then watched frame by frame for a row moving
  const moved = await page.evaluate(async (t) => {
    const btn = document.querySelector(`.upnext .q-item [aria-label="Move ${t} down"]`) as HTMLButtonElement;
    btn.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
    btn.click();
    for (let i = 0; i < 120; i++) {
      await new Promise((r) => requestAnimationFrame(r));
      if ([...document.querySelectorAll('.upnext > li')].some((li) => li.getAnimations().some((a) => a.playState === 'running') && getComputedStyle(li).transform !== 'none')) return true;
    }
    return false;
  }, was[0]);
  expect(moved).toBe(true);
  await expect.poll(titles).toEqual([was[1], was[0], ...was.slice(2)]);
  await page.waitForTimeout(600);
  expect(await leftovers(page)).toEqual([]);
});

test('with Animations off nothing moves: no view transition, the menu is instant', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page, true);
  expect(await page.evaluate(() => document.documentElement.classList.contains('pref-calm'))).toBe(true);
  const before = await runs(page);
  await page.locator('#main-rail').getByRole('link', { name: 'Favourites' }).click();
  await page.waitForURL(/\/favorites/);
  await playAlbum(page);
  await page.locator('.player .bar-song').click();
  await expect(page.locator('.np')).toBeVisible();
  expect(await runs(page)).toBe(before);
  await page.getByRole('button', { name: 'Close Now Playing' }).click();
  await expect(page.locator('.np')).toHaveCount(0);
  await page.getByRole('button', { name: 'More player options' }).click();
  const menu = page.locator('.player .bar-menu');
  await expect(menu).toBeVisible();
  expect(await menu.evaluate((m) => m.getAnimations().length)).toBe(0);
  expect(await menu.evaluate((m) => getComputedStyle(m).transform)).toBe('none');
  expect(await runs(page)).toBe(before);
});

test('a computer asking for less motion gets no view transitions either', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  const before = await runs(page);
  await page.locator('#main-rail').getByRole('link', { name: 'Favourites' }).click();
  await page.waitForURL(/\/favorites/);
  await page.waitForTimeout(400);
  expect(await runs(page)).toBe(before);
});
