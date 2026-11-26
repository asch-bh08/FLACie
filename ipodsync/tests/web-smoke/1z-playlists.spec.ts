import { test, expect, Page } from '@playwright/test';

// Playlists, a single playlist and Favourites in design "I": serif titles with the last word in italics, mono facts, rounded covers,
// a big accent Play button, and no sideways scroll on a phone or a PC.

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

// a page is read once it is live: the copy drawn first by the server is replaced when the live connection starts, and a style read in between finds a detached element
async function live(page: Page, url: string) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto(url);
  await ws; await page.waitForTimeout(800);
}

const font = (l: ReturnType<Page['locator']>) => l.evaluate((e) => getComputedStyle(e).fontFamily);
const radius = (l: ReturnType<Page['locator']>) => l.evaluate((e) => parseFloat(getComputedStyle(e).borderRadius));
const noSideScroll = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1);

for (const [name, size] of [['phone', { width: 390, height: 844 }], ['pc', { width: 1440, height: 900 }]] as const) {
  test.describe(name, () => {
    test.beforeEach(async ({ page }) => { await page.setViewportSize(size); await signIn(page); });

    test('playlists, a playlist and favourites in the new look', async ({ page }) => {
      const plName = 'Road trips ' + name;
      await live(page, '/playlists');
      const h1 = page.locator('.page-head h1');
      expect(await font(h1)).toContain('Instrument Serif');
      await expect(h1.locator('i')).toHaveText('Playlists');
      const input = page.getByLabel('New playlist name');
      expect(await radius(page.locator('.pl-newbar'))).toBeGreaterThanOrEqual(12);   // the rounded bar holds the field now (12 Nov)
      await input.fill(plName);
      expect(await radius(page.getByRole('button', { name: 'Create' }))).toBeGreaterThanOrEqual(12);
      expect(await noSideScroll(page)).toBe(true);
      await page.getByRole('button', { name: 'Create' }).click();
      await page.waitForURL(/\/playlist\//);
      const url = page.url();

      // add a song from the library, then look at the playlist
      await live(page, '/songs');
      await page.getByRole('button', { name: /^More for / }).first().click();
      await page.getByRole('menuitem', { name: 'Add to playlist' }).first().click();
      await page.getByRole('menuitem', { name: 'Add to ' + plName }).click();
      await page.waitForTimeout(500);

      await live(page, '/playlists');
      const card = page.locator('.pl-grid .card', { hasText: plName });
      await expect(card).toBeVisible();
      expect(await radius(card.locator('.cover').first())).toBeGreaterThanOrEqual(14);
      expect(await noSideScroll(page)).toBe(true);
      await page.screenshot({ path: `test-results/shots/playlists-list-${name}.png` });

      await live(page, url);
      const hero = page.locator('.pl-hero');
      await expect(hero).toBeVisible();
      const t = hero.locator('h1');
      expect(await font(t)).toContain('Instrument Serif');
      const words = (await t.innerText()).trim().split(/\s+/);
      expect((await t.locator('i').innerText()).trim()).toBe(words[words.length - 1]);
      expect(await font(hero.locator('.pl-facts'))).toContain('DM Mono');
      expect(await radius(hero.locator('.cover').first())).toBeGreaterThanOrEqual(14);
      const play = hero.getByRole('button', { name: 'Play', exact: true });
      expect(await radius(play)).toBeGreaterThanOrEqual(14);
      expect((await play.boundingBox())!.height).toBeGreaterThanOrEqual(48);
      const bg = await play.evaluate((e) => getComputedStyle(e).backgroundColor);
      const shuffleBg = await hero.getByRole('button', { name: 'Shuffle' }).evaluate((e) => getComputedStyle(e).backgroundColor);
      expect(bg).not.toBe(shuffleBg);
      // the Devices look (owner, 26 Nov): the songs on a rounded card, the quieter actions as outlined round buttons, and calm tags: no file format
      // or source on every row (only Hi-Res and Streaming show), every tag the same outlined round pill
      const list = page.locator('.pl-main > ol.tracks');
      // on a phone the songs start on the first screen: a small cover beside the name, not a big cover over it (owner, 26 Nov)
      if (name === 'phone') {
        expect((await list.locator('.track').first().boundingBox())!.y).toBeLessThan(size.height * .6);
        await page.screenshot({ path: `test-results/shots/playlist-page-${name}.png` });
      }
      expect(await radius(list)).toBeGreaterThanOrEqual(20);
      const share = hero.getByRole('button', { name: 'Share' });
      expect(await share.evaluate((e) => e.closest('.pl-tools') !== null)).toBe(true);
      expect(await radius(share)).toBeGreaterThanOrEqual(18);
      const shownTags = await list.locator('.track .pills .badge, .track .track-src, .track .track-tag').evaluateAll((els) =>
        els.filter((e) => getComputedStyle(e).display !== 'none').map((e) => ({ cls: e.className, r: parseFloat(getComputedStyle(e).borderRadius), border: getComputedStyle(e).borderStyle })));
      for (const t of shownTags) { expect(t.cls).toMatch(/hires|cloud/); expect(t.r).toBeGreaterThanOrEqual(10); expect(t.border).toBe('solid'); }
      // the quieter actions are equal rounded slots, the icon over its word (the phone's ToolSlot); no More: Rename, Remove duplicates and Delete are
      // on the Edit page, never where a second click on the playlist lands (owner, 1 Nov and 26 Nov)
      await expect(hero.getByRole('button', { name: 'More for this playlist' })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Rename' })).toHaveCount(0);
      const slots = hero.locator('.pl-tools > .pl-slot');
      expect(await slots.count()).toBeGreaterThanOrEqual(2);
      const boxes = await slots.evaluateAll((els) => els.map((e) => { const r = e.getBoundingClientRect(), ic = e.querySelector('svg')!.getBoundingClientRect(), w = e.querySelector('span')!.getBoundingClientRect();
        return { top: Math.round(r.top), h: r.height, iconAbove: ic.bottom <= w.top + 1, r: parseFloat(getComputedStyle(e).borderRadius) }; }));
      for (const b of boxes) { expect(b.top).toBe(boxes[0].top); expect(b.iconAbove).toBe(true); expect(b.r).toBeGreaterThanOrEqual(14); expect(b.r).toBeLessThan(b.h / 2); }
      const edit = hero.getByRole('link', { name: 'Edit', exact: true });
      await expect(edit).toBeVisible();
      expect(await noSideScroll(page)).toBe(true);
      await page.screenshot({ path: `test-results/shots/playlists-one-${name}.png` });

      // clean up so a rerun starts clean: Delete is on the Edit page and asks first
      await edit.click();
      await page.waitForURL(/\/edit$/);
      await page.getByRole('button', { name: 'Delete playlist' }).click();
      await page.getByRole('button', { name: /^Delete "/ }).click();
      await page.waitForURL(/\/playlists$/);

      await live(page, '/favorites');
      const fh = page.locator('.pl-hero h1');
      expect(await font(fh)).toContain('Instrument Serif');
      await expect(fh.locator('i')).toBeVisible();
      expect(await font(page.locator('.pl-hero .pl-facts'))).toContain('DM Mono');
      expect(await radius(page.locator('.pl-hero').getByRole('button', { name: 'Play', exact: true }))).toBeGreaterThanOrEqual(14);
      expect(await noSideScroll(page)).toBe(true);
    });
  });
}
