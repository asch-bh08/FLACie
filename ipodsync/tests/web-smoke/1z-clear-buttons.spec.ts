import { test, expect, Page } from '@playwright/test';

// "Make the buttons clearer" (owner, 26 Nov): a button that is only a picture says what it is in a tooltip (title), and the ones that otherwise give no
// sign (the heart, Shuffle, Repeat) say what they did in the toast at the bottom, in the same words as the phone.

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

// buttons whose only content is a picture and that have no tooltip
const pictureOnlyWithoutTitle = (page: Page) => page.evaluate(() =>
  Array.from(document.querySelectorAll('button, a')).filter((el) => {
    if (!(el as HTMLElement).offsetParent && getComputedStyle(el).position !== 'fixed') return false;
    const text = (el.textContent || '').trim();
    return text === '' && el.querySelector('svg, img, .icon, i') && !el.getAttribute('title');
  }).map((el) => (el.getAttribute('aria-label') || el.className || el.tagName)));

// Shuffle and Repeat change their icon and say nothing more (owner, 26 Nov: those messages got annoying); the heart still says what it did
test('picture-only buttons have a tooltip; Shuffle and Repeat show it on the button with no message, the heart says what it did', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page);
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  expect(await pictureOnlyWithoutTitle(page)).toEqual([]);

  // play a song so the player bar is there
  await page.locator('.tracks .track .track-main').first().click();
  await expect(page.locator('.bar-text b')).toBeVisible({ timeout: 20_000 });
  expect(await pictureOnlyWithoutTitle(page)).toEqual([]);

  const toast = page.locator('.toast');
  const shuffle = page.locator('.bar [data-pip="shuffle"]');
  const was = (await shuffle.getAttribute('aria-pressed')) === 'true';
  await shuffle.click();
  await expect(shuffle).toHaveAttribute('aria-pressed', was ? 'false' : 'true');

  const repeat = page.locator('.bar [data-pip="repeat"]');
  const before = await repeat.getAttribute('aria-label');
  await repeat.click();
  await expect(repeat).not.toHaveAttribute('aria-label', before!);
  await page.waitForTimeout(600);
  await expect(toast).toHaveCount(0);

  const heart = page.locator('.bar [data-pip="heart"]');
  const hearted = (await heart.getAttribute('aria-label')) === 'Remove from favourites';
  await heart.click();
  await expect(heart).toHaveAttribute('aria-label', hearted ? 'Add to favourites' : 'Remove from favourites');
  await expect(toast).toContainText(hearted ? /^Removed .+ from favourites$/ : /^Added .+ to favourites$/);
  await heart.click();   // leave it as it was
});
