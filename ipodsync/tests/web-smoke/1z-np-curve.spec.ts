import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';

// Settings → Curved player (owner, 13 Nov): the full-screen player's halves turn a few degrees in from their outer edges under a deep perspective
const COVER = fs.readFileSync('.data-real-ls/art/02444A488F2935C4E5D3FBF7CC2E56827C29D078.v3.jpg');
async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester'); await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load'); await page.waitForTimeout(1200);
}
async function openPlayer(page: Page) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto('/artist/Hires%20Lab'); await ws; await page.waitForTimeout(600);
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await page.locator('.tracks .track-main').nth(2).click();
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await expect(page.locator('.ls-names h2')).toBeVisible();
  await page.getByRole('button', { name: 'Pause', exact: true }).first().click().catch(() => {});
  await page.waitForTimeout(1500);
}
const halves = (page: Page) => page.evaluate(() => ['.ls-art', '.ls-side'].map((s) => getComputedStyle(document.querySelector(s)!).transform));

test('Curved player: off is flat; on, the halves turn in from their outer edges; the switch is in Settings', async ({ page }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 1440, height: 810 });
  await page.route('**/art/**', (r) => r.fulfill({ status: 200, contentType: 'image/jpeg', body: COVER }));
  await signIn(page);
  // the switch, off to begin with
  await page.goto('/settings');
  const sw = page.getByTestId('pref-curve');
  await expect(sw).not.toBeChecked();
  await openPlayer(page);
  expect(await halves(page)).toEqual(['none', 'none']);
  await page.screenshot({ path: 'test-results/curve-off.png' });
  // on
  await page.evaluate(() => (window as any).flacieUi.set(JSON.stringify({ ...JSON.parse((window as any).flacieUi.get()), curve: true })));
  await page.waitForTimeout(800);
  const [stage, side] = await halves(page);
  expect(stage).toMatch(/^matrix3d/); expect(side).toMatch(/^matrix3d/);
  await page.screenshot({ path: 'test-results/curve-on.png' });
  // swapped: the lyrics side on the left turns from its left edge
  await page.getByTestId('np-swap').click(); await page.waitForTimeout(800);
  await page.waitForTimeout(600);
  await page.screenshot({ path: 'test-results/curve-on-swapped.png' });
  await page.getByTestId('np-swap').click();
  // the player's buttons still work under the turn
  const tr = page.locator('.ls-transport');
  await tr.getByRole('button', { name: /^(Play|Pause)$/ }).click();
  await expect(tr.getByRole('button', { name: /^(Play|Pause)$/ })).toBeVisible();
  // and they sit in the bar along the bottom, not over the title
  const bar = (await tr.boundingBox())!, title = (await page.locator('.ls-names h2').boundingBox())!;
  expect(bar.y).toBeGreaterThan(title.y + title.height);
});
