import { test, expect, Page } from '@playwright/test';

// Owner, 25 Nov: "clicking on the now playing bar needs 2 clicks or doesn't work on the right side". One click anywhere on the bar that isn't a
// button opens Now Playing, on a phone-wide window and a wide one.

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(1000);
}

for (const size of [{ width: 390, height: 844 }, { width: 1400, height: 900 }]) {
  test(`one click on the bar opens Now Playing (${size.width} wide)`, async ({ page }) => {
    await page.setViewportSize(size);
    await signIn(page);
    await page.goto('/artist/Hires%20Lab');
    await page.getByRole('link', { name: /Lab Album/ }).first().click();
    await page.locator('.tracks .track-main').first().click();
    const bar = page.locator('.bar').first();
    await expect(bar).toBeVisible({ timeout: 20_000 });
    const box = (await bar.boundingBox())!;
    // every 5% along the bar, at its middle height: a point on a button is a button press, anywhere else opens the player
    for (let f = 0.03; f < 1; f += 0.05) {
      const x = box.x + box.width * f, y = box.y + box.height / 2;
      const onButton = await page.evaluate(([x, y]) => { const el = document.elementFromPoint(x, y); return !!el?.closest('button:not(.bar-song), a, input, [role=menu]'); }, [x, y]);
      if (onButton) continue;
      await page.mouse.click(x, y);
      await expect(page.locator('.ls-names h2'), `click at ${Math.round(f * 100)}%`).toBeVisible({ timeout: 4000 });
      await page.getByRole('button', { name: 'Close Now Playing' }).click();
      await expect(page.locator('.ls-names h2')).toBeHidden({ timeout: 3000 });
    }
  });
}
