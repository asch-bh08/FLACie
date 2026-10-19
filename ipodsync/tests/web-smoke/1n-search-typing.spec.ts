import { test, expect, Page } from '@playwright/test';

// Typing in a search box must never lose or swap letters: the browser keeps the text and the server never writes over it while a person types.
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

test.describe.serial('typing in search boxes', () => {
  test('the top search box keeps every letter, however fast and however the server answers', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await page.goto('/');
    const box = page.locator('input[aria-label="Search"]');
    await expect(box).toBeVisible({ timeout: 20_000 });
    // the box's text is watched for the whole run: it must only ever grow by the letters typed
    await page.evaluate(() => {
      const el = document.querySelector('input[aria-label="Search"]') as HTMLInputElement; const seen: string[] = []; (window as any).__seen = seen;
      new MutationObserver(() => { }).observe(el, { attributes: true });
      const poll = () => { if (seen[seen.length - 1] !== el.value) seen.push(el.value); requestAnimationFrame(poll); }; poll();
    });
    const text = 'harbour 11 brass monkeys';
    await box.click();
    for (const ch of text) { await page.keyboard.type(ch); await page.waitForTimeout(ch === ' ' ? 130 : 25 + Math.floor(Math.random() * 90)); }   // an uneven typist, now and then pausing long enough for a search to start
    await expect(box).toHaveValue(text);
    await page.waitForTimeout(1200);
    await expect(box).toHaveValue(text);                                    // after the searches came back it is still what was typed
    await expect(page).toHaveURL(/\/search\?q=harbour%2011%20brass%20monkeys/);
    const seen: string[] = await page.evaluate(() => (window as any).__seen);
    for (let i = 1; i < seen.length; i++) expect(text.startsWith(seen[i]) && seen[i].length > seen[i - 1].length, 'the text only grows: ' + JSON.stringify(seen[i - 1]) + ' then ' + JSON.stringify(seen[i])).toBe(true);
    // typing in the middle of the text and deleting works too
    await box.press('Home'); await page.keyboard.type('x'); await page.keyboard.press('Backspace');
    await page.waitForTimeout(900);
    await expect(box).toHaveValue(text);
    // the clear button empties the box and the search
    await page.getByRole('button', { name: 'Clear search' }).click();
    await expect(box).toHaveValue('');
    await expect(page).toHaveURL(/\/search$/);
    // the guard itself: the server's text never goes over the box while it has the focus, and does go in once it has not
    await box.click(); await page.keyboard.type('abc');
    await page.evaluate(() => (window as any).flacie.textBoxSet(document.querySelector('input[aria-label="Search"]'), 'SERVER'));
    await expect(box).toHaveValue('abc');
    await page.locator('body').click({ position: { x: 5, y: 300 } });
    await page.evaluate(() => (window as any).flacie.textBoxSet(document.querySelector('input[aria-label="Search"]'), 'SERVER'));
    await expect(box).toHaveValue('SERVER');
    // a shared link fills the box
    await page.goto('/search?q=lab%202');
    await expect(box).toHaveValue('lab 2', { timeout: 20_000 });
  });

  test('letters typed before the page goes live are kept, and the box keeps the focus', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    // the page arrives drawn by the server and goes live a moment later; hold the live part back so typing surely lands in that gap
    await page.route('**/_framework/blazor.web.js', async (r) => { await new Promise((ok) => setTimeout(ok, 2500)); await r.continue(); });
    await page.goto('/', { waitUntil: 'commit' });
    const box = page.locator('input[aria-label="Search"]');
    await expect(box).toBeVisible({ timeout: 20_000 });
    const live = () => page.evaluate(() => !!(window as any).Blazor && !!(document.querySelector('input[aria-label="Search"]') as any)?._tb);
    expect(await live(), 'typing starts before the page is live').toBe(false);
    await box.click();
    await page.keyboard.type('harbour', { delay: 40 });
    // the page goes live while the person is still typing
    await expect.poll(live, { timeout: 20_000 }).toBe(true);
    await page.keyboard.type(' 11', { delay: 40 });
    await expect(box).toHaveValue('harbour 11');
    await expect(page).toHaveURL(/\/search\?q=harbour%2011/);
    await expect(box).toBeFocused();
    await expect(box).toHaveValue('harbour 11');
  });

  test('the Explore and Downloads filters keep every letter too', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await page.goto('/albums');
    const x = page.locator('input[aria-label="Search this list"]');
    await expect(x).toBeVisible({ timeout: 20_000 });
    const text = 'brass album';
    await x.click();
    for (const ch of text) { await page.keyboard.type(ch); await page.waitForTimeout(20 + Math.floor(Math.random() * 60)); }
    await page.waitForTimeout(800);
    await expect(x).toHaveValue(text);
    await page.goto('/downloads');
    const d = page.locator('input[aria-label="Filter downloads"]');
    await expect(d).toBeVisible({ timeout: 20_000 });
    await d.click();
    for (const ch of 'velvet') { await page.keyboard.type(ch); await page.waitForTimeout(15 + Math.floor(Math.random() * 50)); }
    await page.waitForTimeout(600);
    await expect(d).toHaveValue('velvet');
  });
});
