import { test, expect, Page } from '@playwright/test';
// (named 1w- so it runs before 2-login, whose wrong-password tests lock this address out of signing in)

// ONE APP 1i: the server-wide controls are for administrators only. Signed in as "listener" (a normal Jellyfin user of the mock, see
// mock-jellyfin.mjs), no menu offers Server admin or Downloads, the admin pages say they are for administrators and show nothing of the server,
// and every server-wide endpoint answers 403. The list of controls is docs/plans/one-app-admin-only.md.

const STATE = 'test-results/listener-state.json';
test.beforeAll(async ({ browser }) => {
  const page = await browser.newPage();
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('listener');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.context().storageState({ path: STATE });
  await page.close();
});

async function open(page: Page, url: string) {
  const state = JSON.parse(require('fs').readFileSync(STATE, 'utf8'));
  await page.context().addCookies(state.cookies);
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 }).catch(() => null);
  await page.goto(url); await ws; await page.waitForTimeout(1200);
}

test('a normal user is offered no server-wide control in the menus', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 900 });
  await open(page, '/');
  await expect(page.locator('#main-rail')).not.toContainText('Server admin');
  await page.locator('.avatar-btn').click();
  const menu = page.locator('.account-menu');
  await expect(menu).toBeVisible();
  await expect(menu.getByRole('menuitem', { name: 'Server admin' })).toHaveCount(0);
  await expect(menu.getByRole('menuitem', { name: 'Downloads' })).toHaveCount(0);
});

test('the admin pages tell a normal user they are for administrators and show nothing of the server', async ({ page }) => {
  for (const url of ['/dashboard', '/downloads']) {
    await open(page, url);
    await expect(page.locator('main')).toContainText(/administrators/, { timeout: 15_000 });
    await expect(page.locator('main .subnav[aria-label="Server admin sections"]')).toHaveCount(0);
    await expect(page.locator('main .admin-block')).toHaveCount(0);
    await expect(page.locator('main')).not.toContainText("Everyone's downloads");
  }
});

test('every server-wide endpoint answers 403 to a normal user', async ({ page }) => {
  await open(page, '/');
  const calls: [string, string][] = [['GET', '/api/admin/load'], ['POST', '/api/settings'], ['POST', '/api/charts/run'], ['POST', '/api/dj/test']];
  const got = await page.evaluate(async (cs) => {
    const out: Record<string, number> = {};
    for (const [m, u] of cs) {
      // some are the phone's API: they take the Jellyfin token (here the mock's token for listener), not the browser's cookie; the others the cookie
      const headers: Record<string, string> = { 'Content-Type': 'application/json', 'X-Emby-Token': 'mock-token-listener' };
      const r = await fetch(u, { method: m, headers, body: m === 'POST' ? '{}' : undefined, credentials: 'same-origin' });
      out[m + ' ' + u] = r.status;
    }
    return out;
  }, calls);
  for (const [m, u] of calls) expect(got[m + ' ' + u], m + ' ' + u).toBe(403);
});
