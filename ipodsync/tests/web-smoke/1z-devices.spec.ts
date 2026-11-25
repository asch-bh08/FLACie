import { test, expect, Page, APIRequestContext } from '@playwright/test';
import * as path from 'node:path';

// Devices, redone (owner, 25 Nov): this device first, the account's other devices as cards with labelled actions, and "Where you were": each device's
// last day, from which a past moment can be picked and carried on here. The phone's states arrive at /api/where with its Jellyfin token, as the app sends them.

const MOCK = `http://127.0.0.1:${process.env.MOCK_PORT || 8196}`;
const SHOTS = path.join(__dirname, '..', '..', '..', 'docs', 'screens-devices-2026-11-25');

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

const phone = { 'X-Emby-Token': 'mock-token', 'X-Jellyfin-Server': MOCK };
// the mock's songs: s3 "Harbour 3", s4 "Lantern 4", s5 "Paper 5" (Aurora Vale)
const report = (request: APIRequestContext, data: object) => request.post('/api/where', { headers: phone, data });

test.beforeAll(async ({ request }) => {
  await request.get(`${MOCK}/__sessions?on=1`);
  // the phone was on "Lantern 4" at 1:05 in "Road trip"; a tablet that is off now was on "Paper 5"
  expect((await report(request, { deviceId: 'phone-1', device: 'Pixel 8', kind: 'Phone', title: 'Lantern 4', artist: 'Aurora Vale', itemId: 's4',
    positionMs: 65_000, durationMs: 178_000, paused: false, queue: ['s3', 's4', 's5'], index: 1, context: 'Road trip' })).status()).toBe(200);
  await report(request, { deviceId: 'tablet-9', device: 'Galaxy Tab', kind: 'Phone', title: 'Paper 5', artist: 'Aurora Vale', itemId: 's5',
    positionMs: 30_000, durationMs: 185_000, paused: true, queue: ['s5'], index: 0, context: null });
});
test.afterAll(async ({ request }) => { await request.get(`${MOCK}/__sessions?on=0`); });

test('the phone API: a state needs the token, and comes back with its queue', async ({ request, playwright }) => {
  const anon = await playwright.request.newContext({ baseURL: `http://127.0.0.1:${process.env.WEB_PORT || 5290}` });
  expect((await anon.get('/api/where')).status()).toBe(401);
  expect((await anon.post('/api/where', { data: { deviceId: 'x', title: 'y' } })).status()).toBe(401);
  const list = await (await request.get('/api/where', { headers: phone })).json();
  const dev = list.devices.find((d: any) => d.deviceId === 'phone-1');
  expect(dev.device).toBe('Pixel 8');
  const st = dev.states.find((s: any) => s.title === 'Lantern 4');
  expect(st.context).toBe('Road trip');
  const one = await (await request.get(`/api/where/${st.id}`, { headers: phone })).json();
  expect(one.queue).toEqual(['s3', 's4', 's5']);
  expect(one.index).toBe(1);
});

for (const width of [1280, 390]) {
  test(`Devices at ${width}px: this device, the other devices, and carrying on from where the phone was`, async ({ page }) => {
    const problems: string[] = [];
    page.on('pageerror', (e) => problems.push(e.message));
    await page.setViewportSize({ width, height: width > 600 ? 900 : 844 });
    await signIn(page);
    await page.goto('/devices');
    await expect(page.locator('h1')).toHaveText(/Your devices/);
    await expect(page.locator('h1 i')).toHaveText('devices');   // design I: the last word in italics
    // this device first
    const cards = page.locator('.dv-card');
    await expect(cards.first()).toHaveAttribute('data-device', 'self');
    await expect(cards.first().locator('.dv-chip.you')).toHaveText('This device');
    // the phone, live in Jellyfin's sessions, with labelled actions
    const ph = page.locator('.dv-card[data-device="Pixel 8"]');
    await expect(ph).toBeVisible({ timeout: 15_000 });
    await expect(ph.locator('.dv-song b')).toHaveText('Static 6');
    for (const label of ['Play here', 'Previous', 'Pause', 'Next']) await expect(ph.getByRole('button', { name: label, exact: false }).first()).toBeVisible();
    // the tablet is not in the sessions: an offline card with where it left off
    const tab = page.locator('.dv-card.away[data-device="Galaxy Tab"]');
    await expect(tab.locator('.dv-chip.off')).toHaveText('Offline');
    await expect(tab.getByRole('button', { name: /Carry on here/ })).toBeVisible();
    // every button says what it does
    for (const b of await page.locator('.dv button').all()) expect(((await b.innerText()).trim() || (await b.getAttribute('aria-label')) || '').length).toBeGreaterThan(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: path.join(SHOTS, `web-devices-${width}.png`), fullPage: true });

    // Where you were: the phone's group, its moment with a relative time, and Carry on
    const group = page.locator('.dv-group', { has: page.locator('.dv-group-head b', { hasText: 'Pixel 8' }) });
    const moment = group.locator('.dv-moment', { hasText: 'Lantern 4' }).first();
    await expect(moment.locator('.dv-when b')).toHaveText(/just now|min ago/);
    await expect(moment.locator('.dv-what small')).toContainText('at 1:05 in Road trip');
    // the device pills narrow the list to one device
    await page.locator('.dv-filter').getByRole('tab', { name: 'Galaxy Tab' }).click();
    await expect(page.locator('.dv-group')).toHaveCount(1);
    await page.locator('.dv-filter').getByRole('tab', { name: 'All devices' }).click();
    // what the page asks the browser to play (the mock's FLAC files are not real audio, so the song itself fails and moves on: the ask is what counts)
    await page.evaluate(() => { const f = (window as any).flacie; (window as any).__loads = []; const real = f.load.bind(f);
      f.load = (...a: any[]) => { (window as any).__loads.push({ title: a[2], startAt: a[8] }); return real(...a); }; });
    await moment.getByRole('button', { name: /Carry on from Lantern 4/ }).click();
    await expect(page.locator('.dv-status')).toContainText('Carrying on: Lantern 4');
    // it plays here: the phone's song, from where it was, under the playlist it came from
    await expect.poll(() => page.evaluate(() => (window as any).__loads[0]), { timeout: 15_000 }).toEqual({ title: 'Lantern 4', startAt: 65 });
    await page.screenshot({ path: path.join(SHOTS, `web-devices-carried-on-${width}.png`), fullPage: true });
    expect(problems).toEqual([]);
  });
}
