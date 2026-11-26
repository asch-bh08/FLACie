import { test, expect, Page } from '@playwright/test';
// (named 1w- so it runs before 2-login, whose wrong-password tests lock this address out of signing in)

// The ONE APP kit (wwwroot/kit.css, docs/plans/one-app-overhaul.md "The one design"): every piece has the phone kit's numbers, big by default and
// one step down when the window is short or very narrow; and the older duplicate classes (.dv-btn, .ls-slot, .badge, .switch ...) are drawn as
// the kit pieces they map to. The pieces are put on a real page, so they are measured with every stylesheet of the app around them.

const STATE = 'test-results/kit-state.json';
test.beforeAll(async ({ browser }) => {
  const page = await browser.newPage();
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
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
  await page.goto(url); await ws; await page.waitForTimeout(1500);
}

// one of each piece (and of each old class mapped onto one), measured: height, width, radius, font size / weight / family, colours
async function measure(page: Page) {
  return page.evaluate(() => {
    const box = document.createElement('div');
    box.id = 'kit-probe';
    box.style.cssText = 'position:fixed;left:0;top:0;z-index:9999;display:flex;flex-wrap:wrap;gap:8px;padding:8px;width:100vw;box-sizing:border-box';
    const svg = '<svg viewBox="0 0 24 24" width="19" height="19"></svg>';
    box.innerHTML = `
      <span class="k-title" data-k="title">A <i>title</i></span><span class="k-section" data-k="section">Section</span>
      <span class="k-body" data-k="body">Body</span><span class="k-small" data-k="small">Small</span><span class="k-label" data-k="label">Label</span>
      <button class="k-btn primary" data-k="btn">Play</button><button class="k-btn tone" data-k="btn-tone">Shuffle</button>
      <button class="k-btn-sm" data-k="btn-sm">All</button><button class="k-btn-sm on" data-k="btn-sm-on">Lossless</button>
      <button class="k-slot" data-k="slot">${svg}<span>Lyrics</span></button><button class="k-slot on" data-k="slot-on">${svg}<span>Up next</span></button>
      <button class="k-icon" data-k="icon">${svg}</button>
      <span class="k-tag" data-k="tag">Playing</span><span class="k-fmt" data-k="fmt">FLAC 16/44.1</span><span class="k-fmt hires" data-k="fmt-hires">Hi-Res</span>
      <div class="k-card" data-k="card" style="width:120px;height:40px"></div>
      <div class="k-row" data-k="row" style="width:300px"><span class="k-row-cover" data-k="row-cover"></span><span class="k-row-text"><b>Song</b><small>Artist</small></span><span class="k-row-time">3:20</span></div>
      <label class="switch-row"><input type="checkbox" checked><i class="switch" data-k="switch"></i></label>
      <a class="dv-btn" data-k="dv-btn">Play here</a><button class="pl-tool" data-k="pl-tool">Refresh</button><a class="mood" data-k="mood">Chill</a><a class="mood on" data-k="mood-on">Focus</a>
      <button class="chipbtn" data-k="chipbtn">Sort</button><button class="x-chip" data-k="x-chip">Rock</button>
      <span class="dv-chip" data-k="dv-chip">This device</span><span class="badge" data-k="badge">New</span><span class="dev-chip" data-k="dev-chip">On</span><span class="tag" data-k="tagold">Live</span>
      <span class="badge fmt" data-k="badge-fmt">MP3 320</span><span class="ls-chip" data-k="ls-chip">FLAC</span>
      <button class="ls-slot" data-k="ls-slot">${svg}<span>Info</span></button>
      <div class="empty-state" data-k="empty"><p class="status">Nothing here yet.</p></div>`;
    document.body.appendChild(box);
    const out: Record<string, any> = {};
    for (const el of box.querySelectorAll<HTMLElement>('[data-k]')) {
      const cs = getComputedStyle(el), r = el.getBoundingClientRect(), ic = el.querySelector('svg')?.getBoundingClientRect();
      const span = el.querySelector(':scope > span');
      out[el.dataset.k!] = {
        h: Math.round(r.height), w: Math.round(r.width), radius: cs.borderTopLeftRadius, size: cs.fontSize, weight: cs.fontWeight, family: cs.fontFamily,
        bg: cs.backgroundColor, fg: cs.color, border: cs.borderTopWidth, transform: cs.textTransform, spacing: cs.letterSpacing, align: cs.alignItems,
        icon: ic ? Math.round(ic.width) : 0, word: span ? getComputedStyle(span).fontSize : '', italic: getComputedStyle(el.querySelector('i') ?? el).fontStyle,
        knob: el.classList.contains('switch') ? (() => { const a = getComputedStyle(el, '::after'); return { w: a.width, h: a.height, bg: a.backgroundColor }; })() : null,
        touch: (() => { const b = getComputedStyle(el, '::before'); return b.content === 'none' ? 0 : parseFloat(b.height); })(),
      };
    }
    box.remove();
    return out;
  });
}

const big = { btn: 56, sm: 44, slot: 60, icon: 52, row: 72 }, tight = { btn: 52, sm: 40, slot: 52, icon: 48, row: 64 };
for (const size of [
  { name: 'phone', width: 390, height: 844, s: big },
  { name: 'PC', width: 1440, height: 900, s: big },
  { name: 'short phone (tight)', width: 390, height: 640, s: tight },
  { name: 'narrow phone (tight)', width: 340, height: 760, s: tight },
]) {
  test(`kit pieces have the phone kit's sizes: ${size.name}`, async ({ page }) => {
    await page.setViewportSize({ width: size.width, height: size.height });
    await open(page, '/songs');
    const k = await measure(page), s = size.s;
    // text
    expect(k.title.size).toBe('30px'); expect(k.title.family).toMatch(/Instrument Serif/); expect(k.title.italic).toBe('italic');
    expect(k.section.size).toBe('24px'); expect(k.body.size).toBe('16px'); expect(k.small.size).toBe('14px');
    expect(k.label).toMatchObject({ size: '11px', transform: 'uppercase' }); expect(k.label.family).toMatch(/DM Mono/);
    // buttons
    for (const b of ['btn', 'btn-tone']) { expect(k[b].h, b).toBe(s.btn); expect(k[b].radius).toBe('16px'); expect(k[b].size).toBe('15px'); expect(k[b].weight).toBe('600'); }
    expect(k.btn.bg).not.toBe(k['btn-tone'].bg);
    for (const b of ['btn-sm', 'dv-btn', 'pl-tool', 'mood', 'chipbtn', 'x-chip']) {
      expect(k[b].h, b).toBe(s.sm); expect(k[b].radius, b).toBe('16px'); expect(k[b].size, b).toBe('15px'); expect(k[b].border, b).toBe('1px');
      expect(k[b].touch, `${b} reaches 48 to the finger`).toBeGreaterThanOrEqual(48);
    }
    expect(k['btn-sm-on'].bg).toBe(k['mood-on'].bg);   // selected = ink fill, the same on both
    expect(k['btn-sm-on'].bg).not.toBe(k['btn-sm'].bg);
    // slots
    for (const b of ['slot', 'ls-slot']) { expect(k[b].h, b).toBe(s.slot); expect(k[b].radius, b).toBe('18px'); expect(k[b].icon, b).toBe(22); expect(k[b].word, b).toBe('12.5px'); }
    expect(k['slot-on'].bg).not.toBe(k.slot.bg);
    expect(k.icon).toMatchObject({ h: s.icon, w: s.icon, radius: '16px' });   // one button shape: no circles
    // tags
    for (const b of ['tag', 'dv-chip', 'badge', 'dev-chip', 'tagold']) { expect(k[b].size, b).toBe('11.5px'); expect(k[b].weight, b).toBe('600'); expect(k[b].radius, b).toBe('999px'); expect(k[b].border, b).toBe('1px'); }
    for (const b of ['fmt', 'badge-fmt', 'ls-chip']) { expect(k[b].size, b).toBe('10.5px'); expect(k[b].radius, b).toBe('5px'); expect(k[b].transform, b).toBe('uppercase'); expect(k[b].family, b).toMatch(/DM Mono/); }
    expect(k['fmt-hires'].bg).toBe('rgb(255, 200, 87)');   // Hi-Res amber
    // card, row, switch, empty
    expect(k.card.radius).toBe('22px');
    expect(k.row.h).toBe(s.row); expect(k['row-cover']).toMatchObject({ w: 52, h: 52, radius: '10px' });
    expect(k.switch).toMatchObject({ w: 44, h: 26 }); expect(k.switch.knob).toEqual({ w: '22px', h: '22px', bg: 'rgb(255, 255, 255)' });
    expect(k.empty.align).toBe('center');
  });
}

// the toast: as the phone's, it goes by itself after 2.5 s (5 s with Undo), and a tap on it dismisses it
test('a toast goes after 2.5 s, and a tap dismisses it', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, '/songs');
  const heart = page.locator('.track').first().locator('button[aria-label$="favourites"]');
  await heart.click();
  const toast = page.locator('.toast');
  await expect(toast).toBeVisible();
  await expect(toast).toBeHidden({ timeout: 4_000 });
  await heart.click();   // (takes the heart back, and says so)
  await expect(toast).toBeVisible();
  await toast.click();
  await expect(toast).toBeHidden({ timeout: 1_000 });
});
