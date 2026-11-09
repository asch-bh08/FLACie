import { test, expect, Page } from '@playwright/test';

// Owner, 9 Nov: "volume bar on the new sound button still is [bad], make it better". Each step of a drag went to the server and back before anything changed,
// so on a slow line the bar lagged, jumped back under the pointer and the sound followed late. Now the bar and the sound follow the pointer at once, the
// server hears about it a few times a second, and the pop-up stays open while you drag even if the pointer leaves it.

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

test('on a slow line the volume bar and the sound follow a drag at once, never jump back, and the pop-up stays open', async ({ page }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  // a slow line between the page and the server: everything on the live connection arrives 300 ms late, both ways (switched on once the player is open)
  let lag = 0;
  await page.routeWebSocket(/_blazor/, (ws) => {
    const server = ws.connectToServer();
    ws.onMessage((m) => { if (lag) setTimeout(() => server.send(m), lag); else server.send(m); });
    server.onMessage((m) => { if (lag) setTimeout(() => ws.send(m), lag); else ws.send(m); });
  });
  await signIn(page);
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto('/artist/Hires%20Lab');
  await ws; await page.waitForTimeout(600);
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await page.locator('.tracks .track-main').first().click();
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await expect(page.locator('.ls-names h2')).toBeVisible();
  lag = 300;
  await page.locator('.ls-sound > .ls-tile').hover();
  await expect(page.locator('.ls-sound-pop')).toHaveCSS('opacity', '1');
  const tb = (await page.locator('.ls-vol-track').boundingBox())!;
  const x = tb.x + tb.width / 2;
  const fill = () => page.locator('.ls-vol-track > i').evaluate((e) => e.getBoundingClientRect().height);
  const sound = () => page.evaluate(() => (window as any).flacie.volume() as number);
  await page.mouse.move(x, tb.y + tb.height * .9); await page.mouse.down(); await page.waitForTimeout(400);
  let last = await fill();
  // up the bar slowly: the fill only grows, and the sound is where the pointer is, without waiting for the server
  for (let i = 1; i <= 8; i++) {
    const y = tb.y + tb.height * (.9 - i * .1);
    await page.mouse.move(x, y);
    await page.waitForTimeout(60);
    const now = await fill();
    expect(now, `step ${i}`).toBeGreaterThanOrEqual(last - 0.5);
    last = now;
    expect(Math.abs((await sound()) - (1 - (y - tb.y) / tb.height)), `sound at step ${i}`).toBeLessThan(0.03);
  }
  // past the pop-up's edge while still dragging: it stays open, and the pointer still sets the top
  await page.mouse.move(x + 200, tb.y - 120, { steps: 4 });
  await expect(page.locator('.ls-sound-pop')).toHaveCSS('opacity', '1');
  expect(await sound()).toBeGreaterThan(0.97);
  await page.mouse.up();
  // and the player (the server) ends up with what was set
  await expect(page.locator('.ls-sound > .ls-tile')).toHaveAttribute('aria-label', 'Sound: volume 100%', { timeout: 10_000 });
  lag = 0;
});
