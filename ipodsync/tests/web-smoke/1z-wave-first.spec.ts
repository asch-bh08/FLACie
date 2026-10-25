import { test, expect, Page } from '@playwright/test';
import * as fs from 'node:fs';
import * as path from 'node:path';

// The owner: a song started from Listen again showed no VU ring. A song nobody has played has no shape yet, so this browser works it out from the whole
// file, one song at a time; while a song plays, the page was also working out the next songs' shapes that way, so the song just picked waited behind them.
// The song being played goes first: looking ahead only fetches shapes the server already has.

const MOCK = `http://127.0.0.1:${process.env.MOCK_PORT || 8196}`;

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

test.afterEach(async ({ request }) => { await request.get(`${MOCK}/__stream?delay=0`); });

test('the song picked gets its VU shape first, not after the next songs\' shapes', async ({ page, request }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  // no shapes kept from earlier runs: every song has to be worked out
  fs.rmSync(path.join(__dirname, '.pub-smoke', '.data-smoke', 'waves'), { recursive: true, force: true });
  fs.mkdirSync(path.join(__dirname, '.pub-smoke', '.data-smoke', 'waves'), { recursive: true });
  await signIn(page);
  await request.get(`${MOCK}/__stream?delay=500`);   // a whole file takes a few seconds to read
  await page.goto('/artist/Cobalt%20Sky');
  await page.getByRole('link', { name: /Cobalt Album/ }).first().click();
  const rows = page.locator('.tracks .track-main');
  await rows.first().click();
  await page.locator('.player [aria-label="Full screen player"]').click();
  await page.waitForTimeout(1500);
  await page.keyboard.press('Escape');   // the next songs' shapes are being worked out by now
  const posted: { p: string; at: number }[] = [];
  page.on('request', (r) => { if (r.method() === 'POST' && r.url().includes('/api/wave')) posted.push({ p: new URL(r.url()).searchParams.get('p') || '', at: Date.now() }); });
  const t0 = Date.now();
  await page.goto('/artist/Cobalt%20Sky');
  await page.getByRole('link', { name: /Cobalt Album/ }).first().click();
  await rows.last().click();   // a song further on: not one being looked ahead to
  await page.locator('.player [aria-label="Full screen player"]').click();   // as Listen again does: the ring is drawn there
  await expect.poll(() => page.evaluate(() => (window as any).flacie.npWaveFor()), { timeout: 5000 }).not.toBe('');
  const picked = await page.evaluate(() => (window as any).flacie.npWaveFor());
  await expect.poll(() => posted.some((x) => x.p === picked), { timeout: 20000 }).toBe(true);
  const ms = posted.find((x) => x.p === picked)!.at - t0;
  console.log(`shape of the song picked sent after ${ms} ms (${posted.length} shapes sent)`);
  expect(posted[0].p).toBe(picked);
});
