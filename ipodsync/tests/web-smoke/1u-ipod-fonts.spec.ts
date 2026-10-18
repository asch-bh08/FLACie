import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

// Each iPod family's screen is set in the face the real device used, as near as freely licensed fonts allow: the black-and-white iPods and the minis in
// Chicago (Chicago Kare), the 5th generation and the first nanos in a Myriad-like sans (Source Sans 3: their Podium Sans was drawn from Myriad), the
// classic and the nanos of 2007-9 in Helvetica Bold (Arimo), the touch iPods and the 6th nano in Helvetica (Arimo), and the flat iOS 7+ screens in
// San Francisco's place (Inter, after the system's own on Apple devices). Each face is bundled, loaded and really used for the text on the screen.
// (Set IPOD_FONT_SHOTS to a folder to write a screenshot of every model's menu and Now Playing there instead of checking.)
async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(800);
}
async function openIpod(page: Page, model: string) {
  await page.goto('about:blank');
  await page.addInitScript((m) => { try { localStorage.setItem('flacie.ipod', JSON.stringify({ model: m, look: 1, panel: false })); } catch { } }, model);
  await page.goto('/ipod');
  await expect(page.getByTestId('ipod')).toHaveAttribute('data-model', model, { timeout: 20_000 });
  await page.waitForTimeout(900);
}
async function shuffle(page: Page) {
  await page.getByTestId('ipod-keys').focus();
  for (let i = 0; i < 10; i++) await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('ipod-np')).toBeVisible({ timeout: 20_000 });
  await page.waitForTimeout(500);
}
const app = (page: Page) => page.getByTestId('ios').getAttribute('data-app');
async function touchToNowPlaying(page: Page, model: string) {
  await page.locator('.ios-home .ios-app[data-app="music"]').first().click();
  await expect.poll(() => app(page)).toBe('music');
  if (model === 'touch7') await page.locator('.ios-row', { hasText: 'Songs' }).first().click();
  else if (model !== 'nano6') await page.locator('[data-tab="Songs"]').click();
  await expect(page.locator('.ios-nav b')).toHaveText('Songs');
  await page.getByTestId('ios-row').nth(1).click();
  await expect.poll(() => app(page), { timeout: 20_000 }).toBe('now');
  await expect(page.getByTestId('ios-np-title')).not.toHaveText('');
  await page.waitForTimeout(500);
}

const MODELS = ['ipod1', 'ipod2', 'ipod3', 'ipod4', 'mini', 'mini2', 'video5', 'nano1', 'nano2', 'classic6', 'classic7', 'nano3', 'nano4', 'nano5',
  'nano6', 'nano7', 'touch1', 'touch2', 'touch3', 'touch4', 'touch5', 'touch6', 'touch7'];
const SHOTS = process.env.IPOD_FONT_SHOTS;

// the family a model's screen is meant to be set in: the CSS family name, and the bundled file's real name
const WANT: Record<string, { family: string; models: string[]; sel: string }> = {
  chicago: { family: 'iPod Chicago', models: ['ipod1', 'ipod3', 'mini2'], sel: '[data-testid=ipod-lcd] .lcd-list li' },
  myriad: { family: 'iPod Myriad', models: ['video5', 'nano1'], sel: '[data-testid=ipod-lcd] .lcd-list li' },
  helvbold: { family: 'iPod Helvetica', models: ['classic6', 'nano4'], sel: '[data-testid=ipod-lcd] .lcd-list li' },
  helv: { family: 'iPod Helvetica', models: ['touch2', 'touch5', 'nano6', 'nano7'], sel: '[data-testid=ios] .ios-home .ios-app' },
  sf: { family: 'iPod SF', models: ['touch6', 'touch7'], sel: '[data-testid=ios] .ios-home .ios-app' },
};

if (SHOTS) {
  test('screenshots of every model (menu and Now Playing)', async ({ page }) => {
    test.setTimeout(600_000);
    fs.mkdirSync(SHOTS, { recursive: true });
    await signIn(page); await page.setViewportSize({ width: 1100, height: 900 });
    for (const m of MODELS) {
      await openIpod(page, m);
      const dev = page.getByTestId('ipod-device');
      const touch = m.startsWith('touch') || m === 'nano6' || m === 'nano7';
      const lcd = touch ? page.getByTestId('ipod-touch-screen') : page.getByTestId('ipod-lcd');
      await lcd.screenshot({ path: path.join(SHOTS, `${m}-menu.png`) });
      if (touch) await touchToNowPlaying(page, m); else await shuffle(page);
      await lcd.screenshot({ path: path.join(SHOTS, `${m}-np.png`) });
      await dev.screenshot({ path: path.join(SHOTS, `${m}-device.png`) });
    }
  });
} else {
  test.describe('screen fonts per iPod family', () => {
    for (const [key, w] of Object.entries(WANT)) {
      for (const m of w.models) {
        test(`${m}: ${key} (${w.family})`, async ({ page }) => {
          await signIn(page); await page.setViewportSize({ width: 1100, height: 900 });
          await openIpod(page, m);
          await page.evaluate(() => document.fonts.ready);
          const r = await page.locator(w.sel).first().evaluate(async (e, fam) => {
            const cs = getComputedStyle(e), names = cs.fontFamily.split(',').map((n) => n.trim().replace(/^["']|["']$/g, '')), first = names.includes(fam) ? fam : names[0];   // (the flat iOS stack leads with the system's own face, which only Apple's browsers have)
            await document.fonts.load(`${cs.fontWeight} ${cs.fontSize} "${fam}"`, 'Abc');
            return { first, weight: cs.fontWeight, size: cs.fontSize, loaded: document.fonts.check(`${cs.fontWeight} ${cs.fontSize} "${fam}"`, 'Abc'),
              face: [...document.fonts].filter((f) => f.family.replace(/["']/g, '') === fam && f.status === 'loaded').length };
          }, w.family);
          expect(r.first).toBe(w.family);
          expect(r.loaded).toBe(true);
          expect(r.face).toBeGreaterThan(0);                                                    // the bundled file really loaded, not a stand-in
          if (key === 'chicago') expect(Number.isInteger(parseFloat(r.size))).toBe(true);       // a pixel face is only crisp at whole sizes (Chicago 12 is 16px here)
          if (key === 'helvbold') expect(Number(r.weight)).toBeGreaterThanOrEqual(700);
        });
      }
    }
    test('the Now Playing lines of a black-and-white iPod are set in Chicago too, at whole pixel sizes', async ({ page }) => {
      await signIn(page); await page.setViewportSize({ width: 1100, height: 900 });
      await openIpod(page, 'ipod4'); await shuffle(page);
      const sizes = await page.locator('[data-testid=ipod-lcd] *').evaluateAll((es) => es.filter((e) => e.childNodes.length && [...e.childNodes].some((n) => n.nodeType === 3 && n.textContent!.trim())).map((e) => {
        const cs = getComputedStyle(e); return { fam: cs.fontFamily.split(',')[0].replace(/"/g, ''), size: parseFloat(cs.fontSize) };
      }));
      expect(sizes.length).toBeGreaterThan(3);
      for (const s of sizes) { expect(s.fam).toBe('iPod Chicago'); expect(Number.isInteger(s.size)).toBe(true); }
    });
  });
}
