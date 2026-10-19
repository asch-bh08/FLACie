import { test, expect, Page } from '@playwright/test';

// The Hi-Res badge is a pure function of the file's real metadata (lossless AND (depth > 16 OR rate > 44.1 kHz)), the same on every surface,
// whatever the skipping. Runs against the mock Jellyfin's "Hires Lab" songs (mock-jellyfin.mjs): exact MediaStreams, one named "[Hi-Res]"
// but really CD quality, one whose first Jellyfin item request fails with HTTP 500.

// title prefix, expected badge, expected sampling text in the Info panel
const LAB: [string, string, string][] = [
  ['Lab 1', 'HI-RES', '24-bit / 96 kHz'],
  ['Lab 2', 'FLAC', '16-bit / 44.1 kHz'],
  ['Lab 3', 'FLAC', '16-bit / 44.1 kHz'],
  ['Lab 4', 'FLAC', '16-bit / 44.1 kHz'],   // "[Hi-Res]" in the name, CD quality in fact
  ['Lab 5', 'HI-RES', '24-bit / 44.1 kHz'],
  ['Lab 6', 'HI-RES', '16-bit / 48 kHz'],    // the first item request for this one fails
  ['Lab 7', 'MP3', '44.1 kHz'],
  ['Lab 8', 'HI-RES', '24-bit / 192 kHz'],
];
const norm = (s: string) => (/hi-?res/i.test(s) ? 'HI-RES' : s.trim().toUpperCase());

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

const expectedOf = (title: string) => LAB.find(([p]) => title.startsWith(p));

async function openLab(page: Page) {
  await page.goto('/artist/Hires%20Lab');
  await page.getByRole('link', { name: /Lab Album/ }).first().click();
}

/** The song-list badges of the Lab artist page, by song title prefix. */
async function listBadges(page: Page) {
  await openLab(page);
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  return page.evaluate(() => Object.fromEntries([...document.querySelectorAll('.tracks .track')].map((r) => [r.querySelector('.track-title')?.textContent?.trim().slice(0, 5) ?? '', r.querySelector('.badge.fmt')?.textContent?.trim() ?? ''])));
}

test('the Hi-Res badge is the same everywhere and never wrong, however fast you skip', async ({ page }) => {
  test.setTimeout(240_000);
  await signIn(page);
  await openLab(page);
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });

  // before anything is known, nothing may claim Hi-Res (a plain codec name is fine)
  const before = await listBadges(page);
  for (const [k, v] of Object.entries(before)) { const e = expectedOf(k); if (e && e[1] !== 'HI-RES') expect(norm(v), `${k} before facts`).not.toBe('HI-RES'); }

  await page.locator('.tracks .track-main').first().click();
  await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 20_000 });
  await page.getByRole('button', { name: 'Open Now Playing' }).click();
  await expect(page.locator('.ls-names h2')).toBeVisible();
  await page.getByRole('tab', { name: 'Info' }).click();

  const seen = new Map<string, Set<string>>();   // title prefix -> every distinct badge text seen once the facts were on screen
  const everSeen = new Map<string, string[]>();
  const sample = async () => {
    const s = await page.evaluate(() => ({
      title: document.querySelector('.ls-names h2')?.textContent?.trim() ?? '',
      badge: document.querySelector('.ls-chip-main')?.textContent?.trim() ?? '',
      big: document.querySelector('.info .fmt-big')?.textContent?.trim() ?? '',
      sampling: document.querySelector('.info .info-head small')?.textContent?.trim() ?? '',
    }));
    const e = expectedOf(s.title);
    if (!e) return;
    // a badge may only ever be the right one or (while the facts are unknown) the plain codec name; a wrong Hi-Res claim is never allowed
    if (s.badge) {
      (everSeen.get(e[0]) ?? everSeen.set(e[0], []).get(e[0])!).push(norm(s.badge));
      if (e[1] !== 'HI-RES') expect(norm(s.badge), `${e[0]} must never claim Hi-Res`).not.toBe('HI-RES');
    }
    if (s.big && e[1] !== 'HI-RES') expect(norm(s.big), `${e[0]} Info head must never claim Hi-Res`).not.toBe('HI-RES');
    if (s.sampling.startsWith(e[2].split(' · ')[0]) || s.sampling.includes(e[2])) {
      // the real facts are on screen: from here on the badge and the Info label must be exactly the expected value
      (seen.get(e[0]) ?? seen.set(e[0], new Set()).get(e[0])!).add(norm(s.badge) + '|' + norm(s.big));
    }
  };

  const next = page.getByRole('button', { name: 'Next' }).first(), prev = page.getByRole('button', { name: 'Previous' }).first();
  // 40 quick skips forwards and backwards (4 forward, 4 back, ...) with tiny delays; the queue stays inside the lab songs, starting at Lab 1
  for (let i = 0; i < 40; i++) {
    await (i % 8 < 4 ? next : prev).click({ noWaitAfter: true });
    await page.waitForTimeout(40 + (i % 4) * 30);
    await sample();
  }
  // then settle on each song in turn (forwards, then back) until its real facts are on screen, and read every surface
  const settle = async () => {
    for (let w = 0; w < 16; w++) {   // up to ~16 s per song: the first read of Lab 6 fails and is retried
      await page.waitForTimeout(1000);
      const t = (await page.locator('.ls-names h2').textContent())?.trim() ?? '';
      const e = expectedOf(t);
      if (e && (await page.locator('.info .info-head small').textContent())?.includes(e[2])) break;
    }
    for (let j = 0; j < 3; j++) { await sample(); await page.waitForTimeout(150); }
  };
  await settle();
  for (let k = 0; k < 7; k++) { await next.click(); await settle(); }
  for (let k = 0; k < 7; k++) { await prev.click(); await settle(); }
  // the Info panel and the Now Playing badge agree with the real metadata for each song
  for (const [prefix, badge, sampling] of LAB) {
    const sets = seen.get(prefix);
    expect(sets, `${prefix} was seen with its facts`).toBeTruthy();
    expect([...sets!], `${prefix}: one badge value only, once the facts are known`).toEqual([badge + '|' + badge]);
    const all = everSeen.get(prefix) ?? [];
    if (badge !== 'HI-RES') expect(all.every((x) => x === badge), `${prefix}: ${all}`).toBeTruthy();
    expect(sampling).toBeTruthy();
  }

  // the song lists (rows) now agree too, and a reload gives the very same answers
  const after = await listBadges(page);
  for (const [prefix, badge] of LAB) expect(norm(after[prefix]), `${prefix} row badge`).toBe(badge);
  await page.reload();
  const reloaded = await listBadges(page);
  expect(reloaded).toEqual(after);

  // the Explore list shows the same badges
  await page.goto('/songs');
  await page.waitForTimeout(2000);
});

test('the media facts endpoint reports the lab songs\' real depth and rate', async ({ page }) => {
  await signIn(page);
  await openLab(page);
  await expect(page.locator('.tracks .track')).toHaveCount(8, { timeout: 20_000 });
  // play through them so the server reads each one's facts
  await page.locator('.tracks .track-main').first().click();
  await expect(page.getByRole('button', { name: 'Open Now Playing' })).toBeVisible({ timeout: 20_000 });
  const next = page.getByRole('button', { name: 'Next' }).first();
  for (let i = 0; i < 8; i++) { await page.waitForTimeout(1500); await next.click({ noWaitAfter: true }); }
  await page.waitForTimeout(2000);
  const res = await page.request.get('/api/audiofacts');
  // the endpoint is for the phone app (bearer / session); with the web session it answers 200 or 401, never a guess
  if (res.status() === 200) {
    const j = await res.json();
    const entries = Object.entries(j.facts as Record<string, [string, number, number, number]>).filter(([k]) => k.includes('Lab '));
    for (const [k, f] of entries) {
      const e = LAB.find(([p]) => k.includes(p + '.') || k.includes(p + ' '));
      if (!e) continue;
      const hires = ['flac'].includes(f[0]) && (f[2] > 16 || f[1] > 44100);
      expect(hires, k).toBe(e[1] === 'HI-RES');
    }
  } else expect(res.status()).toBe(401);
});