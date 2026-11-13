import { test, expect, Page } from '@playwright/test';

// owner, 12 Nov: "can we change to see the whole playlist in the queue": Up next lists the songs already played (above, quieter) and every song to come,
// and opens on the song playing now
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

test('Up next shows the played songs above the one playing and the rest below', async ({ page }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page);
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto('/artist/Hires%20Lab');
  await ws; await page.waitForTimeout(600);
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await page.locator('.tracks .track-main').nth(3).click();   // the fourth song of eight
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await expect(page.locator('.upnext li.now')).toBeVisible();
  await expect(page.locator('.upnext .q-played')).toHaveCount(3);
  await expect(page.locator('.upnext li.q-divider').first()).toContainText('Played');
  expect(await page.locator('.upnext .q-item:not(.q-played)').count()).toBeGreaterThanOrEqual(4);
  await page.screenshot({ path: 'test-results/queue-whole.png' });
});
