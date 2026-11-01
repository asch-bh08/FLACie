import { test, expect, Page } from '@playwright/test';

// The owner (1 Nov, before 1.0): "make sure our app fits any phone screen or aspect ratio on web", with no spacing bugs. Every main page at small and
// large phones, a tablet, a phone on its side and a short laptop: nothing scrolls sideways, no button pokes out of the window, no two buttons of the
// player overlap, and the full-screen player's controls are on screen.

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(1200);
}

const sizes: [string, number, number][] = [
  ['small phone', 320, 568], ['phone', 360, 740], ['S22', 384, 854], ['big phone', 430, 932],
  ['phone on its side', 844, 390], ['tablet', 768, 1024], ['short laptop', 1280, 640],
];
const pages = ['/', '/search', '/songs', '/albums', '/playlists', '/favorites', '/settings', '/import', '/account', '/devices'];

/** Buttons and links whose box reaches outside the window sideways (hidden ones and ones inside a sideways-scrolling row don't count). */
async function poking(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const w = document.documentElement.clientWidth;
    const scrollsSideways = (e: Element | null): boolean => {
      for (let p = e?.parentElement; p; p = p.parentElement) { const s = getComputedStyle(p); if ((s.overflowX === 'auto' || s.overflowX === 'scroll' || s.overflowX === 'hidden') && p.scrollWidth > p.clientWidth + 1) return true; }
      return false;
    };
    return [...document.querySelectorAll('button, a, input, select')].filter((e) => {
      const el = e as HTMLElement; if (!el.offsetParent || getComputedStyle(el).visibility === 'hidden') return false;
      const r = el.getBoundingClientRect(); if (!r.width || !r.height) return false;
      return (r.left < -1 || r.right > w + 1) && !scrollsSideways(el);
    }).slice(0, 8).map((e) => (e.getAttribute('aria-label') || (e as HTMLElement).innerText || e.tagName).trim().slice(0, 40));
  });
}

for (const [name, w, h] of sizes) {
  test.describe(`fits a ${name} (${w} x ${h})`, () => {
    test.beforeEach(async ({ page }) => { await page.setViewportSize({ width: w, height: h }); await signIn(page); });

    test('every main page: no sideways scroll, nothing pokes out', async ({ page }) => {
      const bad: string[] = [];
      for (const p of pages) {
        await page.goto(p); await page.waitForLoadState('load'); await page.waitForTimeout(700);
        const sw = await page.evaluate(() => document.documentElement.scrollWidth);
        if (sw > w + 1) bad.push(`${p}: page is ${sw} wide`);
        const out = await poking(page);
        if (out.length) bad.push(`${p}: ${out.join(', ')}`);
      }
      expect(bad).toEqual([]);
    });

    test('the full-screen player fits: its controls on screen, none overlapping', async ({ page }) => {
      await page.goto('/songs');
      await page.locator('.tracks .track-main').first().click();
      await expect(page.locator('.player')).toBeVisible();
      await page.locator('.player .bar-song').click();
      await expect(page.locator('.ls')).toBeVisible();
      await page.waitForTimeout(900);
      const r = await page.evaluate(() => {
        const vw = document.documentElement.clientWidth, vh = window.innerHeight;
        const els = [...document.querySelectorAll('.ls-transport button, .ls-play, .ls-actions button')].filter((e) => (e as HTMLElement).offsetParent !== null);
        const boxes = els.map((e) => ({ n: e.getAttribute('aria-label') || (e as HTMLElement).innerText.trim() || e.className, b: e.getBoundingClientRect() }));
        const off = boxes.filter(({ b }) => b.left < -1 || b.right > vw + 1 || b.top < -1 || b.bottom > vh + 1).map((x) => x.n);
        const over: string[] = [];
        for (let i = 0; i < boxes.length; i++) for (let j = i + 1; j < boxes.length; j++) {
          const a = boxes[i].b, b = boxes[j].b;
          if (a.left < b.right - 1 && b.left < a.right - 1 && a.top < b.bottom - 1 && b.top < a.bottom - 1) over.push(`${boxes[i].n} / ${boxes[j].n}`);
        }
        return { off, over, sw: document.documentElement.scrollWidth, vw };
      });
      expect(r.off).toEqual([]);
      expect(r.over).toEqual([]);
      expect(r.sw).toBeLessThanOrEqual(r.vw + 1);
      await page.screenshot({ path: `test-results/shots/fit-player-${w}x${h}.png` });
    });
  });
}
