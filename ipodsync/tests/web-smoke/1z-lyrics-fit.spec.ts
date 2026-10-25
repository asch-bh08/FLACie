import { test, expect, Page } from '@playwright/test';

// The owner: on a long lyric line the last word is cut off. The line being sung is drawn 6% bigger from its left edge; a line that fills the width then
// reached past the panel's edge and was clipped. The enlarged line stays inside the panel.

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

for (const [name, w, h] of [['PC', 1440, 900], ['phone', 390, 844]] as const) {
  test(`the line being sung fits, last word and all (${name})`, async ({ page }) => {
    await page.setViewportSize({ width: w, height: h });
    await signIn(page);
    await page.goto('/artist/Brass%20Monkeys');
    await page.getByRole('link', { name: /Brass Album/ }).first().click();
    await page.locator('.tracks .track-main').first().click();
    await page.locator('.player [aria-label="Full screen player"], .player [aria-label="Open Now Playing"]').first().click();
    await page.locator('[role=tab]:has-text("Lyrics"):visible, button:has-text("Lyrics"):visible').first().click();
    const cur = page.locator('.ls-lyrics li.cur').first();
    await expect(cur).toBeVisible({ timeout: 10000 });
    const over = await cur.evaluate((li: HTMLElement) => {
      // a long line that fills the width, as in many real songs
      li.textContent = 'And I would walk five hundred miles and I would walk five hundred more just to be the man who walks a thousand miles to fall down';
      const clip = (() => { for (let e = li.parentElement; e; e = e.parentElement) { const s = getComputedStyle(e); if (s.overflowX !== 'visible' || s.overflowY !== 'visible') return e; } return document.documentElement; })();
      // held as the line being sung, at its full size (the test song's lines move on every 0.6 s)
      li.className = 'cur'; li.style.transition = 'none';
      return li.getBoundingClientRect().right - clip.getBoundingClientRect().right;
    });
    expect(over).toBeLessThanOrEqual(0.5);
  });
}
