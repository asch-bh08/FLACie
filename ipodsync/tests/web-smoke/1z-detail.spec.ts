import { test, expect, Page } from '@playwright/test';

// The album, artist and genre pages in design "I": a hero with a big rounded cover, a serif title with its last word in italics,
// a mono facts line, and a big Play button in the colours of the cover.

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

// a page is read once it is live: the copy drawn first by the server is replaced when the live connection starts
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

    for (const [what, title] of [['album', 'Lab Album'], ['artist', 'Hires Lab']] as const) {
      test(`${what}: serif title with an italic last word, mono facts, rounded cover, accent Play`, async ({ page }) => {
        await live(page, '/artist/Hires%20Lab');
        if (what === 'album') {
          await page.getByRole('link', { name: /Lab Album/ }).first().click();
          await page.waitForURL(/\/album\//); await page.waitForTimeout(800);
        }
        const head = page.locator('.detail-head');
        const h1 = head.locator('h1');
        await expect(h1).toContainText(title);
        expect(await font(h1)).toContain('Instrument Serif');
        const words = (await h1.innerText()).trim().split(/\s+/);
        expect((await h1.locator('i').innerText()).trim()).toBe(words[words.length - 1]);
        const facts = head.locator('.detail-facts');
        await expect(facts).toBeVisible();
        expect(await font(facts)).toContain('DM Mono');
        expect(await radius(head.locator('.cover').first())).toBeGreaterThanOrEqual(20);
        const play = head.getByRole('button', { name: /Play/ }).first();
        await expect(play).toBeVisible();
        const same = await play.evaluate((e) => {
          const probe = document.createElement('i'); probe.style.background = 'var(--np-accent)'; e.parentElement!.appendChild(probe);
          const want = getComputedStyle(probe).backgroundColor; probe.remove();
          return getComputedStyle(e).backgroundColor === want;
        });
        expect(same).toBe(true);
        await expect(head.getByRole('button', { name: /Shuffle/ })).toBeVisible();
        expect(await noSideScroll(page)).toBe(true);
      });
    }

    test('artist: sections are serif titles with the last word in italics', async ({ page }) => {
      await live(page, '/artist/Hires%20Lab');
      const sec = page.locator('h2.section').first();
      await expect(sec).toBeVisible();
      expect(await font(sec)).toContain('Instrument Serif');
      await expect(sec.locator('i')).toBeVisible();
    });

    test('screenshots', async ({ page }) => {
      await live(page, '/artist/Hires%20Lab');
      await page.screenshot({ path: `../../../docs/screens-new-look/detail-artist-${size.width}.png` });
      await page.getByRole('link', { name: /Lab Album/ }).first().click();
      await page.waitForURL(/\/album\//); await page.waitForTimeout(1500);
      await page.screenshot({ path: `../../../docs/screens-new-look/detail-album-${size.width}.png` });
    });

    test('genre: the same hero, no sideways scroll', async ({ page }) => {
      await live(page, '/genres');
      const link = page.locator('a[href^="/genre/"]').first();
      await expect(link).toBeVisible();
      await link.click(); await page.waitForURL(/\/genre\//); await page.waitForTimeout(800);
      const h1 = page.locator('.detail-head h1');
      expect(await font(h1)).toContain('Instrument Serif');
      await expect(h1.locator('i')).toBeVisible();
      expect(await noSideScroll(page)).toBe(true);
    });
  });
}
