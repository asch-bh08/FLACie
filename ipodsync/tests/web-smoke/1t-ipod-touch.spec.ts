import { test, expect, Page } from '@playwright/test';

// The systems of the later touch iPods (/ipod): the iPod touch 6 (iOS 8-9 Music), the iPod touch 7 (iOS 10-12 Music), the nano 7 (the flat 2012 system) and
// the nano 6 (the 2010 square one). Each goes home screen -> Music -> Now Playing; the nanos page their home screens with a swipe or the dots and go back with
// a swipe to the right; the controls lie on top of what they cover and are big enough for a finger. (1m-ipod covers the touch 1-5 and the shared contract.)
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
  await page.addInitScript((m) => { try { localStorage.setItem('flacie.ipod', JSON.stringify({ model: m, look: 0, panel: false })); } catch { } }, model);
  await page.goto('/ipod');
  await expect(page.getByTestId('ipod')).toHaveAttribute('data-model', model, { timeout: 20_000 });
  await expect(page.getByTestId('ios-home')).toBeVisible({ timeout: 20_000 });
  await page.waitForTimeout(300);
}
const app = (page: Page) => page.getByTestId('ios').getAttribute('data-app');
const homeApps = async (page: Page) => page.locator('.ios-home .ios-grid .ios-app').evaluateAll((es) => es.map((e) => e.getAttribute('data-app')));
/** A quick sideways drag across the screen with the mouse (as a finger would swipe): "left" pages forward, "right" goes back. */
async function swipe(page: Page, dir: 'left' | 'right') {
  const b = (await page.getByTestId('ipod-touch-screen').boundingBox())!;
  const y = b.y + b.height * 0.45, from = dir === 'left' ? b.x + b.width * 0.8 : b.x + b.width * 0.2, to = dir === 'left' ? b.x + b.width * 0.2 : b.x + b.width * 0.8;
  await page.mouse.move(from, y); await page.mouse.down();
  await page.mouse.move((from + to) / 2, y + 2, { steps: 3 }); await page.mouse.move(to, y + 4, { steps: 3 });
  await page.mouse.up(); await page.waitForTimeout(300);
}
/** Whether a tap at the middle of the element lands on it (nothing, a cover say, lies over it). */
// (the covers' pictures let taps through, so for the check they are made to catch them: a picture drawn over a button is found)
const onTop = (page: Page, testid: string) => page.getByTestId(testid).first().evaluate((e) => {
  const imgs = [...document.querySelectorAll<HTMLElement>('[data-testid=ios] img')]; imgs.forEach((i) => { i.style.pointerEvents = 'auto'; });
  const r = e.getBoundingClientRect(); const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
  imgs.forEach((i) => { i.style.pointerEvents = ''; });
  return !!hit && (hit === e || e.contains(hit));
});
/** The size of an element in the screen's own points (the screen is scaled into the glass). */
const points = (page: Page, testid: string) => page.getByTestId(testid).first().evaluate((e: HTMLElement) => ({ w: e.offsetWidth, h: e.offsetHeight }));
/** Opens Music from the home screen (the touch 6 and 7 keep it in the dock) and plays the second row of Songs. */
async function playFromSongs(page: Page, model: string) {
  await page.locator('.ios-home .ios-app[data-app="music"]').first().click();
  await expect.poll(() => app(page)).toBe('music');
  if (model === 'touch7') { await page.locator('.ios-row', { hasText: 'Songs' }).first().click(); }
  else if (model !== 'nano6') { await page.locator('[data-tab="Songs"]').click(); }
  await expect(page.locator('.ios-nav b')).toHaveText('Songs');
  await page.getByTestId('ios-row').nth(1).click();
  await expect.poll(() => app(page), { timeout: 20_000 }).toBe('now');
  await expect(page.getByTestId('ios-np-title')).not.toHaveText('');
}

test.describe('touch iPod systems', () => {
  test('the nano 7 shows six round apps a page and swipes to the next', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1200, height: 900 });
    await openIpod(page, 'nano7');
    const first = await homeApps(page);
    expect(first).toHaveLength(6);
    for (const a of ['music', 'clock', 'stopwatch', 'settings']) expect(first).toContain(a);
    await swipe(page, 'left');
    const second = await homeApps(page);
    expect(second.length).toBeGreaterThan(0); expect(second).not.toContain('music');
    await swipe(page, 'right');
    expect(await homeApps(page)).toEqual(first);
    // a swipe to the right inside an app goes back
    await page.locator('.ios-grid .ios-app[data-app="settings"]').click();
    await expect(page.getByTestId('ios-settings')).toBeVisible();
    await swipe(page, 'right');
    await expect.poll(() => app(page)).toBe('home');
  });

  for (const model of ['touch6', 'touch7', 'nano7']) {
    test(`${model}: home screen, Music, Now Playing and back`, async ({ page }) => {
      test.setTimeout(90_000);
      const errors: string[] = []; page.on('pageerror', (e) => errors.push(String(e)));
      await signIn(page); await page.setViewportSize({ width: 1200, height: 900 });
      await openIpod(page, model);
      await expect(page.getByTestId('ios')).toHaveAttribute('data-os', model.startsWith('touch') ? 'flat' : model);
      await expect(page.locator('.ios-home .ios-app[data-app="music"]')).toHaveCount(1);
      if (model === 'touch7') {
        // iOS 10-12: Music opens in Library, a big title over the menu and the newest albums
        await page.locator('.ios-home .ios-app[data-app="music"]').click();
        await expect(page.locator('.ios-nav.big b')).toHaveText('Library');
        await expect(page.locator('[data-tab]')).toHaveText(['Library', 'For You', 'Browse', 'Radio']);
        await expect(page.locator('.ios-tile').first()).toBeVisible();
        await page.getByTestId('ipod-home-button').click(); await expect.poll(() => app(page)).toBe('home');
      }
      if (model === 'touch6') {
        await page.locator('.ios-home .ios-app[data-app="music"]').click();
        await expect(page.locator('[data-tab]')).toHaveText(['Radio', 'Playlists', 'Artists', 'Songs', 'More']);
        // iOS 8.4: Shuffle heads the songs, a red line without a cover
        await page.locator('[data-tab="Songs"]').click();
        const shuffle = page.getByTestId('ios-row').first();
        await expect(shuffle).toContainText('Shuffle'); await expect(shuffle.locator('.ios-thumb')).toHaveCount(0);
        await page.getByTestId('ipod-home-button').click(); await expect.poll(() => app(page)).toBe('home');
      }
      await playFromSongs(page, model);
      // the controls: on top of the cover, and big enough for a finger
      for (const id of ['ios-play', 'ios-next', 'ios-prev', 'ios-back']) expect(await onTop(page, id), id + ' on top').toBe(true);
      const play = await points(page, 'ios-play');
      expect(Math.min(play.w, play.h)).toBeGreaterThanOrEqual(model === 'nano6' || model === 'nano7' ? 40 : 44);
      const scrub = page.getByTestId('ios-scrub');
      if (model === 'nano6') {
        // the controls lie over the cover: a tap on the cover hides them and brings them back; a swipe shows the scrubber
        await expect(scrub).toHaveCount(0);
        await swipe(page, 'left'); await expect(scrub).toBeVisible();
        expect(await onTop(page, 'ios-scrub')).toBe(true);
        await swipe(page, 'right'); await expect(page.getByTestId('ios-play')).toBeVisible();
        await page.getByTestId('ios-np-art').click();
        await expect(page.getByTestId('ios-play')).toHaveCount(0);
        await page.getByTestId('ios-np-art').click();
        await expect(page.getByTestId('ios-play')).toBeVisible();
      } else {
        await expect(scrub).toBeVisible();
        expect(await onTop(page, 'ios-scrub')).toBe(true);
      }
      await page.getByTestId('ios-back').click();
      await expect.poll(() => app(page)).toBe('music');
      // the way back to Now Playing: the mini player above the tab bar (iOS), the Now Playing button (nanos)
      if (model.startsWith('touch')) { await expect(page.getByTestId('ios-mini')).toBeVisible(); await page.getByTestId('ios-mini').click(); }
      else await page.getByTestId('ios-now').click();
      await expect.poll(() => app(page)).toBe('now');
      await page.getByTestId('ios-back').click();
      if (model === 'nano6') {
        // the nano 6's Music opened in Songs: a step back lists the rest, a swipe to the right goes back too
        await page.getByTestId('ios-back').click();
        await expect(page.locator('.ios-nav b')).toHaveText('Music');
        await expect(page.getByTestId('ios-row')).toHaveText(['Playlists', 'Artists', 'Albums', 'Songs', 'Genres']);
        await swipe(page, 'right');
        await expect.poll(() => app(page)).toBe('home');
      }
      expect(errors).toEqual([]);
    });
  }

  test('nothing on the new systems spills off the screen or lies text on text', async ({ page }) => {
    test.setTimeout(240_000);
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await signIn(page);
    const problems = () => page.evaluate(() => {
      const out: string[] = [];
      const box = (e: Element) => e.getBoundingClientRect();
      const ink = (e: Element) => { const r = document.createRange(); r.selectNodeContents(e); return r.getBoundingClientRect(); };
      const screen = box(document.querySelector('[data-testid=ipod-touch-screen]')!), root = document.querySelector('[data-testid=ios]')!;
      const own = (e: Element) => [...e.childNodes].some((n) => n.nodeType === 3 && (n.textContent || '').trim().length > 0);
      const texts = [...root.querySelectorAll('*')].filter((e) => !e.closest('svg, .ios-scroll') && own(e) && box(e).width > 0);
      for (const e of texts) { const r = ink(e); if (r.left < screen.left - 1.5 || r.right > screen.right + 1.5 || r.top < screen.top - 1.5 || r.bottom > screen.bottom + 1.5) out.push('outside: ' + e.textContent!.trim().slice(0, 20)); }
      for (let i = 0; i < texts.length; i++) for (let j = i + 1; j < texts.length; j++) {
        if (texts[i].contains(texts[j]) || texts[j].contains(texts[i])) continue;
        const a = ink(texts[i]), b = ink(texts[j]); if (Math.min(a.right, b.right) - Math.max(a.left, b.left) > 3 && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 3) out.push('overlap: ' + texts[i].textContent!.trim().slice(0, 14) + ' / ' + texts[j].textContent!.trim().slice(0, 14));
      }
      return out;
    });
    for (const [w, h] of [[1366, 768], [390, 844]] as Array<[number, number]>) {
      await page.setViewportSize({ width: w, height: h });
      for (const model of ['touch6', 'touch7', 'nano7']) {
        await openIpod(page, model);
        expect(await problems(), model + ' home at ' + w).toEqual([]);
        await playFromSongs(page, model);
        await page.waitForTimeout(300);
        expect(await problems(), model + ' playing at ' + w).toEqual([]);
        await page.getByTestId('ios-back').click(); await page.waitForTimeout(300);
        expect(await problems(), model + ' music at ' + w).toEqual([]);
      }
    }
  });
});
