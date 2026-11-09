import { test, expect, Page } from '@playwright/test';

// Owner, 9 Nov: "just make a volume bar". The Sound pop-up's volume is a plain slider: the sound follows it while it moves, without waiting for the server
// (checked on a line that delays everything between the page and the server by 300 ms each way), and the player is told the value when it is let go.

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

test('on a slow line the sound follows the volume slider while it moves, and the player gets the value when it is let go', async ({ page }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 1440, height: 900 });
  let lag = 0;
  await page.routeWebSocket(/_blazor/, (ws) => {
    const server = ws.connectToServer();
    ws.onMessage((m) => { if (lag) setTimeout(() => server.send(m), lag); else server.send(m); });
    server.onMessage((m) => { if (lag) setTimeout(() => ws.send(m), lag); else ws.send(m); });
  });
  await signIn(page);
  const live = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto('/artist/Hires%20Lab');
  await live; await page.waitForTimeout(600);
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
  await page.locator('.tracks .track-main').first().click();
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await expect(page.locator('.ls-names h2')).toBeVisible();
  lag = 300;
  await page.locator('.ls-sound > .ls-tile').hover();
  await expect(page.locator('.ls-sound-pop')).toHaveCSS('opacity', '1');
  const bar = page.locator('.ls-vol-bar');
  const tb = (await bar.boundingBox())!;
  const x = tb.x + tb.width / 2;
  const sound = () => page.evaluate(() => (window as any).flacie.volume() as number);
  await page.mouse.move(x, tb.y + tb.height * .9); await page.mouse.down();
  for (let i = 1; i <= 8; i++) {
    await page.mouse.move(x, tb.y + tb.height * (.9 - i * .1));
    await page.waitForTimeout(40);
    const want = Number(await bar.inputValue()) / 100;
    expect(Math.abs((await sound()) - want), `sound at step ${i}`).toBeLessThan(0.02);
  }
  await page.mouse.up();
  const set = Number(await bar.inputValue());
  expect(set).toBeGreaterThan(80);
  await expect(page.locator('.ls-sound > .ls-tile')).toHaveAttribute('aria-label', `Sound: volume ${set}%`, { timeout: 10_000 });
  lag = 0;
});
