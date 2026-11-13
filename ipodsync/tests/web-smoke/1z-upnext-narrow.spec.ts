import { test, expect } from '@playwright/test';
// owner's screenshot, 13 Nov: in the narrow Up next panel (sides swapped) the titles were squeezed to "...Baby One..." by the hidden edit buttons
test('Up next titles keep their width; the edit buttons sit over the row on hover', async ({ page }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 1100, height: 900 });
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester'); await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto('/artist/Hires%20Lab'); await ws; await page.waitForTimeout(600);
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await page.locator('.tracks .track-main').first().click();
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  const row = page.locator('.upnext li.q-item:not(.q-played)').first();
  await expect(row).toBeVisible();
  const text = row.locator('.q-row > span').first();
  const before = (await text.boundingBox())!.width;
  const rowW = (await row.boundingBox())!.width;
  expect(before).toBeGreaterThan(rowW * 0.55);   // the title's part of the row is most of it, with the buttons hidden
  await row.hover(); await page.waitForTimeout(300);
  expect((await text.boundingBox())!.width).toBe(before);   // hovering does not squeeze it
  await expect(row.getByRole('button', { name: /Remove .* from the queue/ })).toBeVisible();
  await page.screenshot({ path: 'test-results/upnext-hover.png' });
});
