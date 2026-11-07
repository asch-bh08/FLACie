import { test, expect, Page } from '@playwright/test';

// The equaliser in the full-screen player (owner, 7 Nov: "EQ back on the web, make it obvious"): a pill that says EQ beside speed, sleep and sound, opening
// the presets as words and five sliders. What it sets is the player's own five-band EQ (flacie.eq), the same one the Retro scenes use.
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

async function playLab(page: Page) {
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  await page.locator('.tracks .track-main').first().click();
  await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 20_000 });
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await expect(page.locator('#speed-sleep')).toBeVisible({ timeout: 20_000 });
}

const bands = (page: Page) => page.evaluate(() => (window as any).flacie.eqGet() as number[]);
const eqPill = (page: Page) => page.locator('#speed-sleep .eq-menu .chipbtn');

test.describe.serial('equaliser', () => {
  test('a pill that says EQ opens presets and five sliders; a preset sets the player EQ, a slider makes it Custom, Flat resets', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    await page.evaluate(() => (window as any).flacie.eq([0, 0, 0, 0, 0]));
    await expect(eqPill(page)).toBeVisible();
    await expect(eqPill(page)).toContainText('EQ');
    await eqPill(page).click();
    const pop = page.locator('#speed-sleep .eq-menu .chip-pop');
    await expect(pop).toBeVisible();
    await expect(pop.locator('input[type=range]')).toHaveCount(5);
    await pop.getByRole('button', { name: 'Bass boost' }).click();
    await expect.poll(() => bands(page)).toEqual([8, 3, 0, 0, 0]);
    await expect(pop.getByRole('button', { name: 'Bass boost' })).toHaveAttribute('aria-pressed', 'true');
    await expect(eqPill(page)).toContainText('Bass boost');
    await page.screenshot({ path: 'test-results/eq-open.png' });
    // a slider moved by hand: the setting is Custom
    const treble = pop.locator('input[type=range]').nth(4);
    await treble.focus();
    await page.keyboard.press('ArrowRight');
    await expect.poll(() => bands(page)).toEqual([8, 3, 0, 0, 1]);
    await expect(eqPill(page)).toContainText('Custom');
    await pop.getByRole('button', { name: 'Flat' }).click();
    await expect.poll(() => bands(page)).toEqual([0, 0, 0, 0, 0]);
    await expect(eqPill(page)).toHaveText(/^\s*EQ\s*$/);
  });
});
