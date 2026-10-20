import { test, expect, Page } from '@playwright/test';

// Editing Up next in the full-screen player: move a song up, down or to the top ("Play next"), take one out. The song that plays is never touched, the
// buttons are real buttons with labels (keyboard), and the arranged queue comes back after a reload. Uses the mock's "Hires Lab" album: Lab 1 ... Lab 8.

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
}

async function openQueue(page: Page) {
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await expect(page.locator('.ls-names h2')).toBeVisible();
  const tab = page.getByRole('tab', { name: 'Up next' });
  if ((await tab.getAttribute('aria-selected')) !== 'true') await tab.click();
  await expect(page.locator('.upnext li.q-item').first()).toBeVisible({ timeout: 15_000 });
}

const upNext = (page: Page) => page.locator('.upnext li.q-item .q-row b').allInnerTexts().then((a) => a.map((s) => s.trim()).filter((s) => /^Lab \d/.test(s)));
const nowTitle = async (page: Page) => ((await page.locator('.ls-names h2').innerText()) || '').trim();

test.describe.serial('Up next editing', () => {
  test('move, play next and remove: Up next changes, the playing song does not, and it survives a reload', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    await openQueue(page);
    expect(await nowTitle(page)).toBe('Lab 1');
    expect(await upNext(page)).toEqual(['Lab 2', 'Lab 3', 'Lab 4', 'Lab 5', 'Lab 6', 'Lab 7', 'Lab 8']);
    await expect.poll(() => page.evaluate(() => (window as any).flacie.ready()), { timeout: 15_000 }).toBe(true);
    const clock = async () => (await page.locator('.player .seek span').first().innerText()).trim();
    // the mock's songs are 3 seconds long: pause, so the queue does not move on by itself while the edits are made
    await expect.poll(clock, { timeout: 10_000 }).not.toBe('0:00');
    await page.getByRole('button', { name: 'Pause', exact: true }).first().click();
    await expect(page.getByRole('button', { name: 'Play', exact: true }).first()).toBeVisible();
    const c0 = await clock();
    expect(c0).not.toBe('0:00');

    // up: the first row cannot go up (disabled), another row swaps with the one above it
    await expect(page.getByRole('button', { name: 'Move Lab 2 up' })).toBeDisabled();
    await page.getByRole('button', { name: 'Move Lab 4 up' }).click();
    await expect.poll(() => upNext(page)).toEqual(['Lab 2', 'Lab 4', 'Lab 3', 'Lab 5', 'Lab 6', 'Lab 7', 'Lab 8']);
    // down: the last row cannot go down
    await expect(page.getByRole('button', { name: 'Move Lab 8 down' })).toBeDisabled();
    await page.getByRole('button', { name: 'Move Lab 2 down' }).click();
    await expect.poll(() => upNext(page)).toEqual(['Lab 4', 'Lab 2', 'Lab 3', 'Lab 5', 'Lab 6', 'Lab 7', 'Lab 8']);
    // play next: straight to the top (the button's name has no "next" in it on purpose: the specs find the player's Next button by that word)
    await page.getByRole('button', { name: 'Move Lab 7 to the top of the queue' }).click();
    await expect.poll(() => upNext(page)).toEqual(['Lab 7', 'Lab 4', 'Lab 2', 'Lab 3', 'Lab 5', 'Lab 6', 'Lab 8']);
    // remove (with the keyboard: focus the button and press Enter)
    const rm = page.getByRole('button', { name: 'Remove Lab 3 from the queue' });
    await rm.focus();
    await page.keyboard.press('Enter');
    await expect.poll(() => upNext(page)).toEqual(['Lab 7', 'Lab 4', 'Lab 2', 'Lab 5', 'Lab 6', 'Lab 8']);
    // the removal says so, with Undo: the song goes back where it was
    await expect(page.locator('.toast')).toContainText('Removed “Lab 3”');
    await page.locator('.toast').getByRole('button', { name: 'Undo' }).click();
    await expect.poll(() => upNext(page)).toEqual(['Lab 7', 'Lab 4', 'Lab 2', 'Lab 3', 'Lab 5', 'Lab 6', 'Lab 8']);
    await page.getByRole('button', { name: 'Remove Lab 3 from the queue' }).click();
    await expect.poll(() => upNext(page)).toEqual(['Lab 7', 'Lab 4', 'Lab 2', 'Lab 5', 'Lab 6', 'Lab 8']);

    // what plays was never touched: same song, at the same second (nothing was loaded again), still paused where it was
    expect(await nowTitle(page)).toBe('Lab 1');
    expect(await clock()).toBe(c0);
    await expect(page.getByRole('button', { name: 'Play', exact: true }).first()).toBeVisible();

    // Next goes to the song that was moved to the top, and the list follows
    await page.getByRole('button', { name: 'Next' }).first().click();
    await expect.poll(() => nowTitle(page)).toBe('Lab 7');
    await page.getByRole('button', { name: 'Pause', exact: true }).first().click();   // (3 s songs: hold it here)
    await expect.poll(() => upNext(page)).toEqual(['Lab 4', 'Lab 2', 'Lab 5', 'Lab 6', 'Lab 8']);

    // a reload (a new page over the same browser storage): the last song comes back paused with Up next as it was arranged
    await page.reload();
    await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 40_000 });
    await openQueue(page);
    expect(await nowTitle(page)).toBe('Lab 7');
    expect(await upNext(page)).toEqual(['Lab 4', 'Lab 2', 'Lab 5', 'Lab 6', 'Lab 8']);
    // and edits go on from there
    await page.getByRole('button', { name: 'Move Lab 8 to the top of the queue' }).click();
    await expect.poll(() => upNext(page)).toEqual(['Lab 8', 'Lab 4', 'Lab 2', 'Lab 5', 'Lab 6']);
    await page.reload();
    await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 40_000 });
    await openQueue(page);
    expect(await upNext(page)).toEqual(['Lab 8', 'Lab 4', 'Lab 2', 'Lab 5', 'Lab 6']);
  });

  test('the queue saves as a playlist, in the order it will play', async ({ page }) => {
    test.setTimeout(120_000);
    await signIn(page);
    await playLab(page);
    await openQueue(page);
    await page.getByRole('button', { name: 'Pause', exact: true }).first().click();
    await page.getByRole('button', { name: 'Move Lab 5 to the top of the queue' }).click();
    await expect.poll(() => upNext(page)).toEqual(['Lab 5', 'Lab 2', 'Lab 3', 'Lab 4', 'Lab 6', 'Lab 7', 'Lab 8']);
    await page.getByRole('button', { name: 'Save the queue as a playlist' }).click();
    await expect(page.locator('.toast')).toContainText('Saved the queue as');
    await page.goto('/playlists');
    await page.getByRole('link', { name: /Lab Album/ }).first().click();
    await expect.poll(() => page.locator('.tracks .track-title').allInnerTexts().then((a) => a.map((x) => x.trim())), { timeout: 20_000 })
      .toEqual(['Lab 1', 'Lab 5', 'Lab 2', 'Lab 3', 'Lab 4', 'Lab 6', 'Lab 7', 'Lab 8']);
  });
});
