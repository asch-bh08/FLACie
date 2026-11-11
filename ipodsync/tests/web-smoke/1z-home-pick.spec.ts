import { test, expect, Page } from '@playwright/test';

// Home A (owner, 11 Nov: "Home doesn't excite me"): tonight's pick, a whole album as a card in the cover's colours, with Play and DJ it.
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

for (const size of [{ width: 1440, height: 900 }, { width: 390, height: 844 }]) {
  test(`Home shows tonight's pick at ${size.width} px, it plays the album, and nothing runs off the side`, async ({ page }) => {
    test.setTimeout(120_000);
    await page.setViewportSize(size);
    await signIn(page);
    await page.goto('/');
    const pick = page.locator('.home-pick');
    await expect(pick).toBeVisible({ timeout: 20_000 });
    await expect(pick.locator('h2')).toHaveText(/\S/);
    await expect(page.locator('.home-hero h1')).toContainText(/Put on|Pick up/);
    // the pick holds still once shown (it used to swap as the library loaded in parts)
    const first = (await pick.locator('h2').innerText()).trim();
    await page.waitForTimeout(2500);
    await expect(pick.locator('h2')).toHaveText(first);
    const b = (await pick.boundingBox())!;
    expect(b.x).toBeGreaterThanOrEqual(0); expect(b.x + b.width).toBeLessThanOrEqual(size.width + 1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
    await page.waitForTimeout(900);
    await page.screenshot({ path: `test-results/home-pick-${size.width}.png` });
    const album = (await pick.locator('h2').innerText()).trim();
    await pick.getByRole('button', { name: 'Play', exact: true }).click();
    await expect(page.locator('.ls-names h2, .player .bar-song').first()).toBeVisible({ timeout: 20_000 });
    await expect.poll(() => page.evaluate(() => !(document.querySelector('audio') as HTMLAudioElement | null)?.paused), { timeout: 20_000 }).toBe(true);
    expect(album.length).toBeGreaterThan(0);
  });
}
