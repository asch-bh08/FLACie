import { test, expect, Page } from '@playwright/test';

// Design J on the music pages (owner, 9 Nov: "fewer icons, round buttons, one look, nothing hanging off or overlapping"). At a PC, a small laptop and a
// phone: Explore's tabs, search bar and song rows stop short of the A–Z strip; every pill is fully round; a shelf's Play all stays on one line beside
// its title; an album's Play, Shuffle and Share sit on one row inside the page with the cover in the middle on a phone; a playlist's buttons stay inside
// its hero; and an empty page offers one thing to do.

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForTimeout(1200);
}
async function live(page: Page, url: string) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto(url);
  await ws; await page.waitForTimeout(1500);
}
type Box = { left: number; right: number; top: number; bottom: number; width: number; height: number };
const box = (page: Page, sel: string) => page.locator(sel).first().evaluate((e) => { const r = e.getBoundingClientRect(); return { left: r.left, right: r.right, top: r.top, bottom: r.bottom, width: r.width, height: r.height }; }) as Promise<Box>;

for (const [w, h] of [[1440, 900], [1024, 768], [390, 844]]) {
  test.describe(`music pages at ${w}x${h}`, () => {
    test.beforeEach(async ({ page }) => { await page.setViewportSize({ width: w, height: h }); await signIn(page); });

    test('Explore: the tabs, the search bar and the song rows stop short of the A–Z strip; the pills are round', async ({ page }) => {
      await live(page, '/songs');
      const rail = await box(page, '.az-rail');
      for (const sel of ['.mood-chips[aria-label="Explore"]', '.x-bar', '.tracks .track', '.page-head .btn']) {
        const b = await box(page, sel);
        expect(b.right, `${sel} reaches the A–Z strip`).toBeLessThanOrEqual(rail.left);
        expect(b.left, `${sel} is off the left edge`).toBeGreaterThanOrEqual(0);
      }
      // every tab is a full pill: its corner radius is at least half its height
      const flat = await page.locator('.mood-chips .mood').evaluateAll((els) => els.filter((e) => parseFloat(getComputedStyle(e).borderTopLeftRadius) < e.getBoundingClientRect().height / 2 - 1).length);
      expect(flat).toBe(0);
      // a song's title has room for more than a few letters
      expect((await box(page, '.tracks .track .track-title')).width).toBeGreaterThan(80);
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(w);
    });

    test('Home: the mood chips are round and each Play all sits on one line inside its shelf, clear of the title', async ({ page }) => {
      await live(page, '/');
      const flat = await page.locator('.mood-chips .mood').evaluateAll((els) => els.filter((e) => parseFloat(getComputedStyle(e).borderTopLeftRadius) < e.getBoundingClientRect().height / 2 - 1).length);
      expect(flat).toBe(0);
      const heads = page.locator('.shelf-head:has(.chip.small)');
      const n = await heads.count();
      expect(n).toBeGreaterThan(0);
      for (let i = 0; i < n; i++) {
        const bad = await heads.nth(i).evaluate((head) => {
          const hb = head.getBoundingClientRect(), play = head.querySelector('.chip.small')!.getBoundingClientRect(), title = head.querySelector(':scope > div')!.getBoundingClientRect();
          const out: string[] = [];
          if (play.height > 46) out.push(`Play all is ${Math.round(play.height)} px tall (two lines)`);
          if (play.right > hb.right + 1 || play.left < hb.left - 1) out.push('Play all hangs off the shelf');
          if (title.right > play.left + 1 && title.bottom > play.top && title.top < play.bottom) out.push('the title runs under Play all');
          return out;
        });
        expect(bad, `shelf ${i}`).toEqual([]);
      }
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(w);
    });

    test('an album: Play, Shuffle and Share on one row inside the page; on a phone the cover is in the middle', async ({ page }) => {
      await live(page, '/albums');
      await page.locator('.grid .card').first().click();
      await page.waitForURL(/\/album\//); await page.waitForTimeout(1200);
      const main = await box(page, 'main.content');
      const btns = await page.locator('.detail-actions > *').evaluateAll((els) => els.map((e) => { const r = e.getBoundingClientRect(); return { top: r.top, left: r.left, right: r.right }; }));
      expect(btns.length).toBe(3);
      for (const b of btns) { expect(Math.abs(b.top - btns[0].top)).toBeLessThan(2); expect(b.right).toBeLessThanOrEqual(main.right); expect(b.left).toBeGreaterThanOrEqual(main.left); }
      if (w < 761) {
        const cover = await box(page, '.detail-head .detail-cover'), head = await box(page, '.detail-head');
        expect(Math.abs((cover.left + cover.right) / 2 - (head.left + head.right) / 2)).toBeLessThan(2);
      }
    });

    test('a playlist: every button stays inside the hero; an empty one offers a way to find songs', async ({ page }) => {
      await live(page, '/playlists');
      await page.getByLabel('New playlist name').fill('Fit ' + w);
      await page.getByRole('button', { name: 'Create' }).click();
      await page.waitForURL(/\/playlist\//); await page.waitForTimeout(1200);
      const hero = await box(page, '.pl-hero');
      const out = await page.locator('.pl-hero .chips button').evaluateAll((els, hb) => els.filter((e) => { const r = e.getBoundingClientRect(); return r.width > 0 && (r.right > hb.right + 1 || r.left < hb.left - 1 || r.bottom > hb.bottom + 1); }).map((e) => (e as HTMLElement).innerText.trim()), hero);
      expect(out).toEqual([]);
      const empty = page.locator('.empty-state');
      await expect(empty.getByRole('link', { name: 'Find songs to add' })).toBeVisible();
      const e = await box(page, '.empty-state .btn');
      expect(e.right).toBeLessThanOrEqual(w);
    });

    test('Favourites, empty: a friendly line and one button to go and find songs', async ({ page }) => {
      await live(page, '/favorites');
      // another spec may have left a heart on: empty the list first
      if (await page.getByRole('button', { name: 'Clear all', exact: true }).isVisible()) {
        await page.getByRole('button', { name: 'Clear all', exact: true }).click();
        await page.getByRole('button', { name: /^Clear all \d+/ }).click();
      }
      const empty = page.locator('.empty-state');
      await expect(empty).toContainText('No favourites yet');
      await expect(empty.getByRole('link', { name: 'Browse songs' })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(w);
    });
  });
}

test('Explore: Clear filters on an empty list brings every song back', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  await live(page, '/songs');
  await page.locator('.x-search input').fill('zzzz-nothing-like-this');
  await expect(page.getByRole('button', { name: 'Clear filters' })).toBeVisible({ timeout: 10_000 });
  await page.getByRole('button', { name: 'Clear filters' }).click();
  await expect(page.locator('.tracks .track').first()).toBeVisible();
  await expect(page.locator('.x-search input')).toHaveValue('');
});
