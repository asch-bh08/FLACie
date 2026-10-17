import { test, expect, Page } from '@playwright/test';

// The systems of the touch iPods (/ipod): the iPod touch 1 to 3 (iPhone OS 1 to 3), the touch 4 (iOS 4-5), the touch 5 (iOS 6), the touch 6 (iOS 8-9 Music),
// the touch 7 (iOS 10-12 Music), the nano 7 (the flat 2012 system) and the nano 6 (the 2010 square one). Each goes home screen -> Music -> Now Playing; the nanos page their home screens with a swipe or the dots and go back with
// a swipe to the right; the controls lie on top of what they cover and are big enough for a finger. (1m-ipod covers the shared contract.)
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
  test('the nano 6 pages its home screen with a swipe and the dots, four apps to a page', async ({ page }) => {
    await signIn(page); await page.setViewportSize({ width: 1200, height: 900 });
    await openIpod(page, 'nano6');
    const first = await homeApps(page);
    expect(first).toEqual(['music', 'radio', 'fitness', 'photos']);
    const dots = page.locator('[data-testid=ios-dots] button');
    await expect(dots).toHaveCount(3);
    await expect(dots.nth(0)).toHaveClass(/on/);
    await swipe(page, 'left');
    expect(await app(page)).toBe('home');                                   // a swipe over an icon is not a tap on it
    await expect(dots.nth(1)).toHaveClass(/on/);
    const second = await homeApps(page);
    expect(second).toEqual(['clock', 'stopwatch', 'podcasts', 'settings']);
    await swipe(page, 'left'); await expect(dots.nth(2)).toHaveClass(/on/);
    expect(await homeApps(page)).toEqual(['flacie']);
    await swipe(page, 'left'); await expect(dots.nth(2)).toHaveClass(/on/);  // the last page stays
    await swipe(page, 'right'); await expect(dots.nth(1)).toHaveClass(/on/);
    await dots.nth(0).click(); await expect(dots.nth(0)).toHaveClass(/on/);
    expect(await homeApps(page)).toEqual(first);
    // the dots are easy to hit
    const d = await dots.nth(0).evaluate((e: HTMLElement) => ({ w: e.offsetWidth, h: e.offsetHeight }));
    expect(d.w).toBeGreaterThanOrEqual(18); expect(d.h).toBeGreaterThanOrEqual(18);
  });

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

  for (const model of ['touch6', 'touch7', 'nano7', 'nano6']) {
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

  // the glossy iPhone OS systems: each generation has its own chrome, the same apps and the same ways in and out
  const OLD: Record<string, { tabs: string[]; status: 'black' | 'grey' | 'blue' }> = {
    touch1: { tabs: ['Playlists', 'Artists', 'Songs', 'Videos', 'More'], status: 'black' },
    touch2: { tabs: ['Playlists', 'Artists', 'Songs', 'Videos', 'More'], status: 'grey' },
    touch3: { tabs: ['Playlists', 'Artists', 'Songs', 'Videos', 'More'], status: 'grey' },
    touch4: { tabs: ['Playlists', 'Artists', 'Songs', 'Albums', 'More'], status: 'blue' },
    touch5: { tabs: ['Playlists', 'Artists', 'Songs', 'Albums', 'More'], status: 'blue' },
  };
  const lum = (c: string) => { const m = c.match(/\d+/g)!.map(Number); return (m[0] + m[1] + m[2]) / 3; };
  const timeInk = (page: Page) => page.locator('[data-testid=ios-status] .ios-time').evaluate((e) => getComputedStyle(e).color);
  for (const model of Object.keys(OLD)) {
    test(`${model}: home screen, Music with its tab bar, Now Playing (cover panel), back`, async ({ page }) => {
      test.setTimeout(90_000);
      const spec = OLD[model];
      const errors: string[] = []; page.on('pageerror', (e) => errors.push(String(e)));
      await signIn(page); await page.setViewportSize({ width: 1200, height: 900 });
      await openIpod(page, model);
      await expect(page.getByTestId('ios')).toHaveAttribute('data-os', 'classic');
      await expect(page.getByTestId('ios')).toHaveClass(new RegExp('m-' + model + '\\b'));
      // home: the apps on the page, four in the dock (with their names); the status bar over the wallpaper is black with white letters
      await expect(page.locator('.ios-grid .ios-app[data-app="music"]')).toHaveCount(1);
      await expect(page.locator('.ios-dock .ios-app')).toHaveCount(4);
      await expect(page.locator('.ios-dock .ios-label').first()).toBeVisible();
      expect(lum(await timeInk(page))).toBeGreaterThan(200);
      // an app of the dock; inside an app the status bar is the generation's own: black (touch 1), grey with dark letters (touch 2, 3), blue like the navigation bar (touch 4, 5)
      await page.locator('.ios-dock .ios-app[data-app="videos"]').click();
      await expect.poll(() => app(page)).toBe('videos');
      await expect(page.getByTestId('ios-stub')).toContainText('No Videos');
      if (spec.status === 'grey') expect(lum(await timeInk(page)), 'dark letters on the grey bar').toBeLessThan(110); else expect(lum(await timeInk(page))).toBeGreaterThan(200);
      await page.getByTestId('ios-back').click(); await expect.poll(() => app(page)).toBe('home');
      await page.locator('.ios-dock .ios-app[data-app="photos"]').click();
      await expect.poll(() => app(page)).toBe('photos'); await expect(page.getByTestId('ios-stub')).toContainText('No Photos');
      await page.getByTestId('ipod-home-button').click(); await expect.poll(() => app(page)).toBe('home');
      // Music: the tab bar of the generation
      await page.locator('.ios-grid .ios-app[data-app="music"]').click();
      await expect.poll(() => app(page)).toBe('music');
      await expect(page.locator('[data-tab]')).toHaveText(spec.tabs);
      if (spec.tabs.includes('Videos')) {
        await page.locator('[data-tab="Videos"]').click();
        await expect(page.locator('.ios-nav b')).toHaveText('Videos'); await expect(page.locator('.ios-empty')).toContainText('No Videos');
      }
      await page.locator('[data-tab="Artists"]').click(); await expect(page.locator('.ios-nav b')).toHaveText('Artists');
      await page.getByTestId('ios-row').first().click();
      await page.getByTestId('ios-back').click(); await expect(page.locator('.ios-nav b')).toHaveText('Artists');
      await page.locator('[data-tab="Songs"]').click(); await expect(page.locator('.ios-nav b')).toHaveText('Songs');
      expect((await points(page, 'ios-row')).h).toBeGreaterThanOrEqual(44);   // finger-sized rows
      await page.getByTestId('ios-row').nth(1).click();
      await expect.poll(() => app(page), { timeout: 20_000 }).toBe('now');
      await expect(page.getByTestId('ios-np-title')).not.toHaveText('');
      for (const id of ['ios-play', 'ios-next', 'ios-prev', 'ios-back', 'ios-scrub', 'ios-vol']) expect(await onTop(page, id), id + ' on top').toBe(true);
      for (const id of ['ios-play', 'ios-prev', 'ios-next']) { const b = await points(page, id); expect(Math.min(b.w, b.h), id).toBeGreaterThanOrEqual(44); }
      // pause and play; the scrubber and the volume bar take a tap
      await page.getByTestId('ios-play').click(); await page.getByTestId('ios-play').click();
      await page.getByTestId('ios-scrub').click({ position: { x: 60, y: 8 } });
      await page.getByTestId('ios-vol').click({ position: { x: 30, y: 8 } });
      // a tap on the cover brings the panel with the stars, repeat and shuffle (they are not on the front)
      await expect(page.getByTestId('ios-repeat')).toHaveCount(0);
      await page.getByTestId('ios-np-art').click();
      await expect(page.getByTestId('ios-repeat')).toBeVisible();
      for (const id of ['ios-repeat', 'ios-shuffle']) { expect(await onTop(page, id), id + ' on top').toBe(true); const b = await points(page, id); expect(Math.min(b.w, b.h), id).toBeGreaterThanOrEqual(40); }
      await page.getByTestId('ios-shuffle').click(); await expect(page.getByTestId('ios-shuffle')).toHaveClass(/on/);
      await page.getByTestId('ios-shuffle').click(); await expect(page.getByTestId('ios-shuffle')).not.toHaveClass(/on/);
      const stars = page.locator('.ios-np-panel .ios-stars button');
      await expect(stars).toHaveCount(5);
      await stars.nth(3).click(); await expect(page.locator('.ios-np-panel .ios-stars button.on')).toHaveCount(4);
      await stars.nth(3).click(); await expect(page.locator('.ios-np-panel .ios-stars button.on')).toHaveCount(0);
      await page.getByTestId('ios-np-art').click(); await expect(page.getByTestId('ios-repeat')).toHaveCount(0);
      // the way back: Now Playing returns to the list, whose "Now Playing" button comes back to it
      await page.getByTestId('ios-back').click(); await expect.poll(() => app(page)).toBe('music');
      await page.getByTestId('ios-now').click(); await expect.poll(() => app(page)).toBe('now');
      await page.getByTestId('ipod-home-button').click(); await expect.poll(() => app(page)).toBe('home');
      // settings and the stopwatch keep working
      await page.locator('.ios-grid .ios-app[data-app="settings"]').click();
      const row = page.getByTestId('ios-setting').first(); const before = await row.locator('.ios-v').textContent();
      await row.click(); await expect(row.locator('.ios-v')).not.toHaveText(before!);
      await page.getByTestId('ios-back').click();
      await page.locator('.ios-grid .ios-app[data-app="stopwatch"]').click();
      await page.getByTestId('ios-sw-go').click(); await page.waitForTimeout(500); await page.getByTestId('ios-sw-go').click();
      await expect(page.getByTestId('ios-sw-time')).not.toHaveText('00:00.00');
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
      for (const model of ['touch1', 'touch2', 'touch3', 'touch4', 'touch5', 'touch6', 'touch7', 'nano7', 'nano6']) {
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
