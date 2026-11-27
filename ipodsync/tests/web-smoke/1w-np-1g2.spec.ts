import { test, expect, Page } from '@playwright/test';
// (named 1w- so it runs before 2-login, whose wrong-password tests lock this address out of signing in)

// ONE APP 1g-2, layout B ("mode slots", docs/parity/np-1g2.html): on a phone Shuffle and Repeat are slots in the row of quiet actions; with Lyrics
// or Info open the sheet keeps the song in reach at its foot (its line, a full-width seek line, previous / play / next, Shuffle and Repeat); an
// unfolded screen gives the cover the room.
const STATE = 'test-results/np-1g2-state.json';
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

async function openPlayer(page: Page, w: number, h: number) {
  await page.setViewportSize({ width: w, height: h });
  await open(page, '/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await page.locator('.tracks .track-main').first().click();
  await expect(page.locator('.player')).toBeVisible();
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await expect(page.locator('.ls-names h2')).toBeVisible();
  await page.waitForTimeout(1000);
}

test('phone: the row of quiet actions is Shuffle, Repeat, Lyrics, Info, More; Lyrics keeps the song in reach at the foot', async ({ page }) => {
  await openPlayer(page, 390, 844);
  const words = (await page.locator('.ls-actions > .ls-slot, .ls-actions .ls-more-btn').allInnerTexts()).map((x) => x.trim());
  expect(words).toEqual(['Shuffle', 'Repeat', 'Lyrics', 'Info', 'More']);
  await page.locator('.ls-actions').getByRole('button', { name: 'Lyrics', exact: true }).click();
  const ctl = page.locator('.ls-side-ctl');
  await expect(ctl).toBeVisible();
  for (const sel of ['.ls-side-seek input', '.ls-side-play', '.ls-side-tr .ls-slot-shuf', '.ls-side-tr .ls-slot-rep']) await expect(ctl.locator(sel)).toBeInViewport();
  // the seek line has the sheet's width, not squashed between two buttons
  const seek = (await ctl.locator('.ls-side-seek input').boundingBox())!;
  expect(seek.width).toBeGreaterThan(390 - 60);
  const before = await ctl.locator('.ls-slot-shuf').getAttribute('aria-pressed');
  await ctl.locator('.ls-slot-shuf').click(); await page.waitForTimeout(300);
  expect(await ctl.locator('.ls-slot-shuf').getAttribute('aria-pressed')).not.toBe(before);
  await ctl.locator('.ls-slot-shuf').click();
});

test('unfolded (690x829): the cover takes the room', async ({ page }) => {
  await openPlayer(page, 690, 829);
  const frame = (await page.locator('.ls-frame').boundingBox())!;
  expect(frame.width).toBeGreaterThan(330);
});
