import { test, expect, Page } from '@playwright/test';
// (named 1w- so it runs before 2-login, whose wrong-password tests lock this address out of signing in)

// ONE APP 1j: someone invited to a server signs in and that's it: the first screen is their music, and Settings shows playback, the DJ, the look
// and their account, never a server address or a NAS / Jellyfin form (those are for whoever runs the server). "listener" is a normal user of
// the mock Jellyfin (mock-jellyfin.mjs).


const STATE = 'test-results/normal-user-state.json';
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

test('a normal user lands on their music, not on setup', async ({ page }) => {
  await open(page, '/');
  await expect(page).toHaveURL(/\/$/);
  await expect(page.locator('main')).toContainText(/Aurora Vale|Brass Monkeys|Cobalt Sky|Delta Hum|Echo Harbour/, { timeout: 15_000 });
  await expect(page.locator('main')).not.toContainText(/Jellyfin server|NAS address|Add Jellyfin|Add a NAS|Lidarr/);
});

test("a normal user's Settings has no server addresses and no NAS or Jellyfin forms", async ({ page }) => {
  await open(page, '/settings');
  await expect(page.locator('#sources')).toContainText(/songs/);
  await expect(page.locator('main')).not.toContainText(/Add a NAS|Add Jellyfin|NAS address|Jellyfin server|127\.0\.0\.1/);
  await expect(page.locator('main')).not.toContainText(/Server admin/);
});

test("a normal user's Account says they are signed in, with no server address and nothing to set up", async ({ page }) => {
  await open(page, '/account');
  await expect(page.locator('main')).toContainText(/Signed in as listener/, { timeout: 15_000 });
  await expect(page.locator('main')).not.toContainText(/127\.0\.0\.1|Not set up|Set up/);
  await expect(page.locator('main').getByRole('link', { name: 'Add' })).toHaveCount(0);
});

// sign the listener out again, so its live session does no more background work while the specs after this one run
test.afterAll(async ({ browser }) => {
  const page = await browser.newPage();
  await open(page, '/settings');
  await page.locator('form[action="/auth/logout"] button').first().click().catch(() => {});
  await page.waitForTimeout(1000);
  await page.close();
});
