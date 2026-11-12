import { test, expect, Page } from '@playwright/test';

// Swap sides (owner, 12 Nov): the cover side and the Up next / Lyrics / Info side change places on a PC, and it is remembered
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
async function openPlayer(page: Page) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto('/artist/Hires%20Lab');
  await ws; await page.waitForTimeout(600);
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await page.locator('.tracks .track-main').first().click();
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await expect(page.locator('.ls-names h2')).toBeVisible();
  await page.waitForTimeout(700);
}

test('a PC: Swap sides puts the lyrics side on the left, and it is remembered', async ({ page }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page); await openPlayer(page);
  const left = async (sel: string) => (await page.locator(sel).boundingBox())!.x;
  expect(await left('.ls-stage')).toBeLessThan(await left('.ls-side'));
  await page.getByTestId('np-swap').click();
  await expect(page.getByTestId('np-swap')).toHaveAttribute('aria-pressed', 'true');
  await page.waitForTimeout(300);
  expect(await left('.ls-side')).toBeLessThan(await left('.ls-stage'));
  await page.screenshot({ path: 'test-results/np-swapped.png' });
  // closed and opened again: still swapped
  await page.getByRole('button', { name: 'Close Now Playing' }).click();
  await page.waitForTimeout(600);
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await expect(page.getByTestId('np-swap')).toHaveAttribute('aria-pressed', 'true');
  await page.waitForTimeout(500);
  expect(await left('.ls-side')).toBeLessThan(await left('.ls-stage'));
});

test('a phone: no Swap sides (one side at a time)', async ({ page }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 390, height: 844 });
  await signIn(page); await openPlayer(page);
  await expect(page.getByTestId('np-swap')).toBeHidden();
});
