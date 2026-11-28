import { test, expect, Page } from '@playwright/test';

// A playlist's Edit page (owner, 26 Nov: its own page, as on the phone): rename, remove duplicates, delete, and every song with Remove (and Undo)
// and a ≡ handle that moves it by dragging or by the Up and Down keys. The new order is the playlist's own, so it is there after a reload.

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

async function live(page: Page, url: string) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto(url);
  await ws; await page.waitForTimeout(800);
}

const noSideScroll = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1);

for (const [name, size] of [['phone', { width: 390, height: 844 }], ['pc', { width: 1440, height: 900 }]] as const) {
  test(`the Edit playlist page on a ${name}`, async ({ page }) => {
    test.setTimeout(150_000);
    await page.setViewportSize(size);
    await signIn(page);
    const list = 'Edit ' + name + ' ' + Date.now().toString().slice(-6);
    await live(page, '/playlists');
    await page.getByRole('textbox', { name: 'New playlist name' }).fill(list);
    await page.getByRole('button', { name: 'Create' }).click();
    await expect(page).toHaveURL(/\/playlist\//, { timeout: 15_000 });
    const listUrl = page.url();
    await page.goto('/artist/Hires%20Lab');
    await page.getByRole('link', { name: /Lab Album/ }).first().click();
    await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
    for (const s of ['Lab 1', 'Lab 2', 'Lab 3']) {
      await page.getByRole('button', { name: `More for ${s}` }).click();
      await page.getByRole('menuitem', { name: 'Add to playlist' }).click();
      await page.getByRole('menuitem', { name: `Add to ${list}` }).click();
      await expect(page.locator('.toast')).toContainText('Added');
    }

    // the playlist page has Edit (a pencil); a phone has no More beside it, a PC has one (owner, 27 Nov; 1z-playlists.spec.ts tries it)
    await live(page, listUrl);
    const more = page.locator('.pl-hero').getByRole('button', { name: 'More for this playlist' });
    if (name === 'phone') await expect(more).toBeHidden(); else await expect(more).toBeVisible();
    await page.locator('.pl-hero').getByRole('link', { name: 'Edit', exact: true }).click();
    await page.waitForURL(/\/edit$/);
    await expect(page.getByRole('heading', { name: 'Edit playlist' })).toBeVisible();
    const titles = () => page.locator('.pe-list .track-title').allInnerTexts().then((a) => a.map((x) => x.trim()));
    await expect.poll(titles, { timeout: 20_000 }).toEqual(['Lab 1', 'Lab 2', 'Lab 3']);
    await page.waitForTimeout(800);   // the drag is wired once the live connection has drawn the page
    expect(await noSideScroll(page)).toBe(true);
    await page.screenshot({ path: `test-results/shots/playlist-edit-${name}.png` });
    // the kit's look (owner, 27 Nov): the top bar with Done sticks, rows as the kit's song row (a 52 cover, artist · album), ✕ and ≡ at least 48
    // Done is in the frame's top bar, which stays at the top while the list scrolls (ONE APP 1k)
    expect(await page.locator('.topbar').evaluate((e) => getComputedStyle(e).position)).toBe('sticky');
    await expect(page.locator('.topbar .pe-done')).toBeVisible();
    await expect(page.locator('.pe-list .track-sub').first()).toContainText('Lab Album');
    const cover = (await page.locator('.pe-row .cover').first().boundingBox())!;
    expect(Math.round(cover.width)).toBeGreaterThanOrEqual(52);
    for (const sel of ['.pe-remove', '.pe-handle']) {
      const b = (await page.locator(sel).first().boundingBox())!;
      expect(Math.min(b.width, b.height), sel).toBeGreaterThanOrEqual(48);
    }

    // the keyboard: Down on Lab 1's handle moves it one place, and the focus stays with Lab 1
    await page.getByRole('button', { name: 'Move Lab 1', exact: true }).focus();
    await page.keyboard.press('ArrowDown');
    await expect.poll(titles).toEqual(['Lab 2', 'Lab 1', 'Lab 3']);
    await expect(page.getByRole('button', { name: 'Move Lab 1', exact: true })).toBeFocused();

    // a drag: Lab 3's handle pulled above the first row
    const h = (await page.getByRole('button', { name: 'Move Lab 3', exact: true }).boundingBox())!;
    const first = (await page.locator('.pe-row').first().boundingBox())!;
    await page.mouse.move(h.x + h.width / 2, h.y + h.height / 2);
    await page.mouse.down();
    await page.mouse.move(h.x + h.width / 2, first.y + 4, { steps: 12 });
    await page.mouse.up();
    await expect.poll(titles).toEqual(['Lab 3', 'Lab 2', 'Lab 1']);

    // kept: after a reload, and on the playlist itself
    await live(page, page.url());
    await expect.poll(titles, { timeout: 20_000 }).toEqual(['Lab 3', 'Lab 2', 'Lab 1']);

    // Remove, then Undo puts it back where it was
    await page.getByRole('button', { name: 'Remove Lab 2 from playlist' }).click();
    await expect.poll(titles).toEqual(['Lab 3', 'Lab 1']);
    await expect(page.locator('.toast')).toContainText('Removed “Lab 2”');
    await page.locator('.toast').getByRole('button', { name: 'Undo' }).click();
    await expect.poll(titles).toEqual(['Lab 3', 'Lab 2', 'Lab 1']);

    await page.getByRole('button', { name: 'Remove duplicates' }).click();
    await expect(page.locator('.toast')).toContainText('No duplicates');

    // Rename in place, then Done goes back to the playlist under its new name
    const renamed = list + ' new';
    await page.getByRole('button', { name: 'Rename' }).click();
    await page.getByLabel('Playlist name').fill(renamed);
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.locator('.pe-name h2')).toHaveText(renamed);
    await page.getByRole('link', { name: 'Done' }).click();
    await page.waitForURL((u) => u.pathname === new URL(listUrl).pathname);
    await expect(page.locator('.pl-hero h1')).toHaveText(renamed);
    await expect.poll(() => page.locator('.pl-main > ol.tracks .track-title').allInnerTexts().then((a) => a.map((x) => x.trim())), { timeout: 20_000 }).toEqual(['Lab 3', 'Lab 2', 'Lab 1']);

    // Delete asks first, then goes to the playlists
    await page.locator('.pl-hero').getByRole('link', { name: 'Edit', exact: true }).click();
    await page.waitForURL(/\/edit$/);
    await page.getByRole('button', { name: 'Delete playlist' }).click();
    await page.getByRole('button', { name: 'Keep it' }).click();
    await page.getByRole('button', { name: 'Delete playlist' }).click();
    await page.getByRole('button', { name: 'Delete', exact: true }).click();
    await page.waitForURL(/\/playlists$/);
    await expect(page.locator('.pl-grid .card', { hasText: renamed })).toHaveCount(0);
  });
}
