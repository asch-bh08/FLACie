import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';

// owner's recording, 13 Nov: opening the player flashed teal and orange before the cover's colours, and with Swap sides on it opened the other way
// round, then jumped. The player's first frame must already wear the song's colours and be the right way round.
const COVER = fs.readFileSync('.data-real-ls/art/02444A488F2935C4E5D3FBF7CC2E56827C29D078.v3.jpg');
async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester'); await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load'); await page.waitForTimeout(1200);
}

test('the player opens in the song\'s colours and the right way round, no flash and no jump', async ({ page }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 1440, height: 810 });
  await page.route('**/art/**', (r) => r.fulfill({ status: 200, contentType: 'image/jpeg', body: COVER }));
  // what the player looks like the moment it is put on the page (before anything can change it)
  await page.addInitScript(() => {
    (window as any).__npFirst = [];
    new MutationObserver((ms) => { for (const m of ms) for (const n of m.addedNodes) if (n instanceof HTMLElement && n.matches('.np.ls')) {
      (window as any).__npFirst.push({ accent: getComputedStyle(n).getPropertyValue('--np-accent').trim(), swapped: n.classList.contains('swapped') });
    } }).observe(document, { childList: true, subtree: true });
  });
  await signIn(page);
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto('/artist/Hires%20Lab'); await ws; await page.waitForTimeout(600);
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await page.locator('.tracks .track-main').first().click();
  await page.waitForTimeout(2500);   // the page takes the cover's colours as the song starts
  const open = async () => { await page.getByRole('button', { name: 'Open Now Playing' }).click(); await expect(page.locator('.ls-names h2')).toBeVisible(); await page.waitForTimeout(1500); };
  const close = async () => { await page.getByRole('button', { name: 'Close Now Playing' }).click(); await expect(page.locator('.np.ls')).toHaveCount(0); await page.waitForTimeout(400); };
  await open();
  const settled = (await page.locator('.np.ls').evaluate((e) => getComputedStyle(e).getPropertyValue('--np-accent').trim()));
  expect(settled).not.toBe('rgb(232, 119, 79)');                    // the cover's colour, not the default orange
  await page.getByTestId('np-swap').click(); await page.waitForTimeout(300);
  await close(); await open(); await close(); await open();
  const first = await page.evaluate(() => (window as any).__npFirst);
  // every opening after the first: drawn in the song's colour straight away
  for (const f of first.slice(1)) expect(f.accent).toBe(settled);
  // and the openings after Swap sides was turned on were drawn swapped from their first frame
  expect(first.slice(1).map((f: any) => f.swapped)).toEqual([true, true]);
  await page.getByTestId('np-swap').click();
});
