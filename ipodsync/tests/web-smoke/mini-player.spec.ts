import { test, expect, Page, FrameLocator } from '@playwright/test';

// F19: the mini player (Document Picture-in-Picture) built like the full player. Headless Chromium cannot always open a real picture-in-picture
// window, so the same content (flacie-pip.js + mini-player.css) is built into a frame of the same size; its buttons press the real player bar's.

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

async function startMini(page: Page, w: number, h: number): Promise<FrameLocator> {
  await page.evaluate(async ([w, h]) => {
    document.getElementById('mp-frame')?.remove();
    const f = document.createElement('iframe'); f.id = 'mp-frame'; f.style.cssText = `position:fixed;left:0;top:0;width:${w}px;height:${h}px;border:0;z-index:99999`;
    document.body.append(f);
    await new Promise((r) => setTimeout(r, 50));
    const css = await (await fetch('/mini-player.css')).text();
    const audio = new Audio();
    const v = (window as any).flaciePip.build(f.contentWindow, audio, css, () => {}, () => {});
    (window as any).__mp = v;
    (window as any).flaciePip.setLyrics(v, [{ t: 0, text: 'First line of the song' }]);
    (window as any).flaciePip.paint(v, audio, { title: document.querySelector('.bar-text b')?.textContent, artist: document.querySelector('.bar-text small')?.textContent, art: '' });
  }, [w, h]);
  return page.frameLocator('#mp-frame');
}

test('the mini player has the seek bar with times, shuffle, repeat, heart, DJ and mic, and a Next line', async ({ page }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 1280, height: 800 });
  await signIn(page);
  await page.goto('/artist/Aurora%20Vale');
  await page.waitForTimeout(1500);
  await page.getByRole('button', { name: 'Play', exact: true }).first().click();
  await expect(page.locator('section.player')).toBeVisible({ timeout: 20_000 });
  await expect(page.locator('section.player')).not.toHaveAttribute('data-pip-next', '');
  const mp = await startMini(page, 340, 520);
  await expect(mp.locator('.mp-title')).not.toBeEmpty();
  await expect(mp.locator('.mp-seek canvas.mp-wave')).toBeVisible();
  await expect(mp.locator('.mp-pos')).toHaveText(/^\d+:\d\d$/);
  await expect(mp.locator('.mp-dur')).toHaveText(/^\d+:\d\d$/);
  for (const what of ['shuffle', 'repeat', 'heart', 'mic', 'prev', 'play', 'next']) await expect(mp.locator(`[data-do="${what}"]`)).toBeVisible();
  await expect(mp.locator('.mp-next')).toHaveText(/^Next: [^·]+ · Aurora Vale$/);
  await expect(mp.locator('.mp-lyric')).toHaveText('First line of the song');
  // its buttons are the bar's: shuffle and the heart turn on in both
  await mp.locator('[data-do="shuffle"]').click();
  await expect(page.locator('section.player [data-pip="shuffle"]')).toHaveAttribute('aria-pressed', 'true');
  await expect(mp.locator('[data-do="shuffle"]')).toHaveAttribute('aria-pressed', 'true');
  await mp.locator('[data-do="heart"]').click();
  await expect(page.locator('section.player [data-pip="heart"]')).toHaveClass(/\bon\b/);
  await expect(mp.locator('[data-do="heart"]')).toHaveAttribute('aria-pressed', 'true');
  await page.locator('#mp-frame').screenshot({ path: 'test-results/shots/mini-player-tall.png' });
});

test('a window wider than it is tall shows one bar with the cover on the left', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await signIn(page);
  await page.goto('/artist/Brass%20Monkeys');
  await page.waitForTimeout(1500);
  await page.getByRole('button', { name: 'Play', exact: true }).first().click();
  await expect(page.locator('section.player')).toBeVisible({ timeout: 20_000 });
  const mp = await startMini(page, 560, 150);
  await expect(mp.locator('body')).toHaveClass(/wide/);
  const art = (await mp.locator('.mp-art').boundingBox())!, side = (await mp.locator('.mp-side').boundingBox())!;
  expect(art.x + art.width).toBeLessThanOrEqual(side.x + 1);
  expect(Math.abs((art.y + art.height / 2) - (side.y + side.height / 2))).toBeLessThan(40);
  for (const what of ['shuffle', 'repeat', 'heart', 'mic', 'play']) await expect(mp.locator(`[data-do="${what}"]`)).toBeVisible();
  await page.locator('#mp-frame').screenshot({ path: 'test-results/shots/mini-player-wide.png' });
});
