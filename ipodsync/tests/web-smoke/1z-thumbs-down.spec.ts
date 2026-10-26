import * as crypto from 'node:crypto';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test, expect, Page } from '@playwright/test';

// Song menus: Thumbs down takes the row out of the list at once and the listening log gets a ThumbDown; Less like this leaves the row and logs LessLikeThis.

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

// the account's log (data/listen/<id>.jsonl), the same id global-setup.ts works out; kinds are written as numbers (Play 0, Heart 1, ThumbDown 3, LessLikeThis 4) or names
function logged(kind: 'ThumbDown' | 'LessLikeThis' | 'Heart' | 'Play', title: string) {
  const mock = `http://127.0.0.1:${process.env.MOCK_PORT || 8196}`;
  const id = crypto.createHash('sha256').update(`jf|${mock}|u1`).digest('hex').toUpperCase();
  const file = path.join(__dirname, '.pub-smoke', '.data-smoke', 'listen', `${id}.jsonl`);
  if (!fs.existsSync(file)) return false;
  const num = { Play: 0, Heart: 1, ThumbDown: 3, LessLikeThis: 4 }[kind];
  return fs.readFileSync(file, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l))
    .some((e) => (e.kind === kind || e.kind === num) && e.title === title);
}

test('Thumbs down removes the row and is logged; Less like this is logged', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page);
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  const rows = page.locator('.tracks .track');
  await expect(rows).toHaveCount(8, { timeout: 20_000 });

  await rows.nth(2).getByRole('button', { name: /^More for/ }).click();
  await rows.nth(2).getByRole('menuitem', { name: 'Less like this' }).click();
  await expect.poll(() => logged('LessLikeThis', 'Lab 3')).toBe(true);
  await expect(rows).toHaveCount(8);

  await rows.nth(4).getByRole('button', { name: /^More for/ }).click();
  await rows.nth(4).getByRole('menuitem', { name: 'Thumbs down' }).click();
  await expect(rows).toHaveCount(7);
  await expect(page.locator('.tracks .track-title', { hasText: 'Lab 5' })).toHaveCount(0);
  await expect.poll(() => logged('ThumbDown', 'Lab 5')).toBe(true);
});

test('The heart and a song that was played are logged', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page);
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  const rows = page.locator('.tracks .track');
  await expect(rows).toHaveCount(8, { timeout: 20_000 });
  const heart = rows.nth(0).getByRole('button', { name: 'Add to favourites' });
  if (await heart.count()) await heart.click();
  await expect.poll(() => logged('Heart', 'Lab 1')).toBe(true);
  // a song heard for a few seconds is logged as a play when the next one starts
  await rows.nth(1).locator('.track-main').click();
  await page.waitForTimeout(3500);
  await rows.nth(2).locator('.track-main').click();
  await expect.poll(() => logged('Play', 'Lab 2'), { timeout: 20_000 }).toBe(true);
});
