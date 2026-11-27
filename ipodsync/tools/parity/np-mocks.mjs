// ONE APP 1g-2 (owner, 27 Nov: Now Playing "still bad"): three layouts for Now Playing drawn as static mock-ups, each at phone tall, phone short,
// unfolded and PC, in the normal state and with Lyrics / Info open, beside today's player (the parity shots), on one page: docs/parity/np-1g2.html.
// Mock-ups only (plain HTML with the kit's numbers), no app code. Run: node ipodsync/tools/parity/np-mocks.mjs
import { createRequire } from 'node:module';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(here, '../../..');
const require = createRequire(path.join(ROOT, 'ipodsync/tests/web-smoke/package.json'));
const { chromium } = require('@playwright/test');
const OUT = path.join(ROOT, 'docs/parity/np-1g2');
fs.mkdirSync(OUT, { recursive: true });

const SIZES = [
  { id: 'tall', name: 'Phone tall 390×844', w: 390, h: 844, cur: 'p390x844' },
  { id: 'short', name: 'Phone short 360×640', w: 360, h: 640, cur: 'p360x640' },
  { id: 'unfolded', name: 'Unfolded 690×829', w: 690, h: 829, cur: 'fold-inner' },
  { id: 'pc', name: 'PC 1366×768', w: 1366, h: 768, cur: 'lap1366x768' },
];
const STATES = [['np', 'Playing'], ['lyrics', 'Lyrics'], ['info', 'Info']];

const ICON = {
  shuffle: '<path d="M16 3h5v5M4 20 21 3M21 16v5h-5M15 15l6 6M4 4l5 5"/>', repeat: '<path d="m17 2 4 4-4 4M3 11V9a3 3 0 0 1 3-3h15M7 22l-4-4 4-4M21 13v2a3 3 0 0 1-3 3H3"/>',
  prev: '<path d="M19 20 9 12l10-8zM5 19V5"/>', next: '<path d="m5 4 10 8-10 8zM19 5v14"/>', pause: '<path d="M6 4h4v16H6zM14 4h4v16h-4z"/>',
  lyrics: '<path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>', info: '<circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/>',
  more: '<circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/>', heart: '<path d="M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.7l-1-1a5.5 5.5 0 0 0-7.8 7.8l1 1L12 21l7.8-7.8 1-1a5.5 5.5 0 0 0 0-7.8z"/>',
  down: '<path d="m6 9 6 6 6-6"/>', plus: '<path d="M12 5v14M5 12h14"/>', queue: '<path d="M3 6h13M3 12h13M3 18h9M17 15l4 3-4 3z"/>',
};
const ic = (n, s = 22) => `<svg width="${s}" height="${s}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${ICON[n]}</svg>`;

const CSS = `
*{box-sizing:border-box;margin:0}body{width:100vw;height:100vh;overflow:hidden;background:linear-gradient(#3b2a6b,#141018 70%);color:#f4f3f7;font:15px/1.3 Archivo,system-ui,sans-serif}
.serif{font-family:'Instrument Serif',Georgia,serif;font-weight:400}.mono{font-family:'DM Mono',ui-monospace,monospace}
.top{display:flex;align-items:center;justify-content:space-between;height:56px;padding:0 12px}.ib{width:48px;height:48px;border-radius:16px;display:grid;place-items:center;background:#ffffff14}
.cover{border-radius:22px;background:linear-gradient(135deg,#5b3bd6,#2a1d63);box-shadow:0 20px 50px #0009;position:relative;flex:none}
.title{font-size:30px;line-height:1}.title i{font-style:italic}.artist{font-weight:700;font-size:16px}.fmt{display:inline-block;padding:3px 7px;border-radius:5px;background:#ffffff1f;font:500 10.5px/1.3 'DM Mono',monospace;letter-spacing:.06em;margin-right:6px}
.seek{height:6px;border-radius:3px;background:#ffffff30;position:relative}.seek b{position:absolute;left:0;top:0;bottom:0;width:38%;border-radius:3px;background:#fff}.seek i{position:absolute;left:38%;top:-7px;width:8px;height:20px;border-radius:4px;background:#fff;margin-left:-4px}
.times{display:flex;justify-content:space-between;font:500 11px 'DM Mono',monospace;letter-spacing:.12em;color:#ffffffb3;margin-top:6px}
.tr{display:flex;align-items:center;justify-content:center;gap:14px}.tb{height:64px;width:84px;border-radius:18px;display:grid;place-items:center;background:#ffffff14}.tb.play{width:112px;height:72px;background:#f4f3f7;color:#141018}
.tb.sm{width:52px;height:52px;background:none;border:1px solid #ffffff30}.tb.sm.on{background:#f4f3f7;color:#141018;border-color:#f4f3f7}
.slots{display:flex;gap:8px}.slot{flex:1 1 0;height:60px;border-radius:18px;background:#ffffff14;border:1px solid #ffffff24;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:3px;font:600 12.5px/1 Archivo,sans-serif}
.slot.on{background:#f4f3f7;color:#141018}.card{border-radius:22px;background:#0000004d;border:1px solid #ffffff24;padding:16px;overflow:hidden}
.ly p{font-weight:700;font-size:22px;color:#ffffff73;margin:0 0 12px}.ly p.now{color:#fff}
.tabs{display:flex;gap:8px;margin-bottom:12px}.tab{height:44px;padding:0 16px;border-radius:16px;border:1px solid #ffffff30;display:inline-flex;align-items:center;gap:6px;font:600 15px Archivo}.tab.on{background:#f4f3f7;color:#141018}
.graph{height:46%;border-radius:14px;background:repeating-linear-gradient(90deg,#ffffff0d 0 1px,transparent 1px 25%),linear-gradient(0deg,#7c5cff55,transparent 70%);margin-bottom:10px}
.stats{display:flex;gap:8px}.stat{flex:1;border:1px solid #ffffff24;border-radius:12px;padding:8px;font:500 16px 'DM Mono'}.stat small{display:block;font-size:9px;letter-spacing:.12em;color:#ffffff99}
.cv-tog{position:absolute;bottom:12px;width:48px;height:48px;border-radius:16px;display:grid;place-items:center;background:#00000066;border:1px solid #ffffff30}.cv-tog.on{background:#f4f3f7;color:#141018}
.mini{display:flex;align-items:center;gap:12px}.mini .cover{width:48px;height:48px;border-radius:10px;box-shadow:none}
.col{display:flex;flex-direction:column}.grow{flex:1 1 auto;min-height:0}
`;

const lyrics = () => `<div class="card ly grow" style="width:100%">${[...Array(9)].map((_, i) => `<p class="${i === 2 ? 'now' : ''}">The words of line ${i + 1} go here</p>`).join('')}</div>`;
const info = () => `<div class="card grow" style="width:100%"><div class="tabs"><span class="tab on">${ic('info', 16)}Spectrum</span><span class="tab">Waveform</span><span class="tab">Loudness</span></div><div class="graph"></div><div class="stats"><div class="stat">1000<small>KBPS</small></div><div class="stat">44.1<small>KHZ</small></div><div class="stat">16<small>BIT</small></div></div></div>`;
const head = (s) => `<div class="title serif" style="font-size:${s}px">Harbour <i>19</i></div><div class="artist" style="margin-top:4px">Cobalt Sky</div><div style="margin-top:8px"><span class="fmt">FLAC 16/44.1</span><span class="fmt">JELLYFIN</span></div>`;
const top = () => `<div class="top"><span class="ib">${ic('down')}</span><span class="mono" style="font-size:11px;letter-spacing:.12em;opacity:.7">PLAYING FROM SONGS</span><span class="ib">${ic('more')}</span></div>`;
const transport = (ends, k = 1) => `<div class="tr">${ends ? `<span class="tb sm on">${ic('shuffle', 20)}</span>` : ''}<span class="tb" style="width:${84 * k}px;height:${64 * k}px">${ic('prev', 26)}</span><span class="tb play" style="width:${112 * k}px;height:${72 * k}px">${ic('pause', 30)}</span><span class="tb" style="width:${84 * k}px;height:${64 * k}px">${ic('next', 26)}</span>${ends ? `<span class="tb sm">${ic('repeat', 20)}</span>` : ''}</div>`;
const seek = () => `<div><div class="seek"><b></b><i></i></div><div class="times"><span>1:12</span><span>3:05</span></div></div>`;
const slots = (items, state) => `<div class="slots">${items.map(([n, w]) => `<span class="slot ${(n === 'lyrics' && state === 'lyrics') || (n === 'info' && state === 'info') || n === 'shuffle' ? 'on' : ''}">${ic(n)}<span>${w}</span></span>`).join('')}</div>`;

// layout bodies: (state, size) => inner html. Each fills the window with no scroll: the cover (or the open card) takes whatever height is left.
const LAYOUTS = {
  A: { name: 'A · Ends of the row', note: 'Shuffle and Repeat back beside the transport, as small outlined squares at its two ends, so they read as quieter than previous / play / next. Seek line full width under the cover, the times under its ends. Slots: Lyrics, More, Info, Add.',
    body: (st, z) => stage(st, z, { ends: true, slotItems: [['lyrics', 'Lyrics'], ['more', 'More'], ['info', 'Info'], ['plus', 'Add']] }) },
  B: { name: 'B · Mode slots', note: 'Shuffle and Repeat become two-state slots in the slot row (ink when on), like Lyrics and Info: everything quiet lives in one row and the transport keeps its big row alone. Add goes into More. Seek line full width under the cover. Lyrics / Info: the card takes the cover\'s place, the song as a small line above the seek.',
    body: (st, z) => stage(st, z, { ends: false, slotItems: [['shuffle', 'Shuffle'], ['repeat', 'Repeat'], ['lyrics', 'Lyrics'], ['info', 'Info'], ['more', 'More']] }) },
  C: { name: 'C · On the cover', note: 'Shuffle and Repeat sit on the cover\'s lower corners as small glass toggles (they change what plays next, the cover is what plays). Transport alone in its row, seek full width. In Lyrics / Info they ride on the small cover beside the title.',
    body: (st, z) => stage(st, z, { ends: false, onCover: true, slotItems: [['lyrics', 'Lyrics'], ['more', 'More'], ['info', 'Info'], ['plus', 'Add']] }) },
};

function stage(st, z, o) {
  const wide = z.w >= 900, short = z.h < 700, k = short ? .82 : 1;
  const card = st === 'lyrics' ? lyrics() : st === 'info' ? info() : '';
  // controls block height, so the cover takes exactly the rest (the unfolded screen's cover was too small: now it is as big as the room allows)
  const ctrlH = (st === 'np' ? (short ? 82 : 100) : 64) + 34 + 72 * k + 24 + 60 + 24;
  if (wide) {
    const side = Math.min(z.h - 56 - 40, z.w * .46);
    const left = st === 'np' ? `<div class="cover" style="width:${side}px;height:${side}px">${o.onCover ? togs() : ''}</div>` : `<div class="col" style="width:${side}px;height:${side}px">${card}</div>`;
    return `${top()}<div style="display:flex;gap:40px;padding:0 40px 24px;height:calc(100vh - 56px);align-items:center">${left}<div class="col" style="flex:1;gap:22px;justify-content:center">${head(44)}${seek()}${transport(o.ends)}${slots(o.slotItems, st)}</div></div>`;
  }
  const avail = z.h - 56 - ctrlH - 16;
  const side = Math.max(120, Math.min(z.w - 32, avail));
  const main = st === 'np'
    ? `<div class="cover" style="width:${side}px;height:${side}px;align-self:center">${o.onCover ? togs() : ''}</div><div style="padding:0 4px">${head(short ? 26 : 30)}</div>`
    : `<div class="col grow" style="gap:10px">${card}<div class="mini"><div class="cover">${o.onCover ? '' : ''}</div><div><b>Harbour 19</b><div style="opacity:.7;font-size:13px">Cobalt Sky</div></div>${o.onCover ? `<span style="margin-left:auto;display:flex;gap:6px"><span class="tb sm on" style="width:44px;height:44px">${ic('shuffle', 18)}</span><span class="tb sm" style="width:44px;height:44px">${ic('repeat', 18)}</span></span>` : ''}</div></div>`;
  return `${top()}<div class="col" style="height:calc(100vh - 56px);padding:0 16px 16px;gap:${short ? 10 : 14}px">${st === 'np' ? `<div class="col" style="gap:14px;flex:1 1 auto;justify-content:space-between">${main}</div>` : main}${seek()}${transport(o.ends, k)}${slots(o.slotItems, st)}</div>`;
}
const togs = () => `<span class="cv-tog on" style="left:12px">${ic('shuffle', 20)}</span><span class="cv-tog" style="right:12px">${ic('repeat', 20)}</span>`;

const browser = await chromium.launch(process.env.PW_CHROMIUM ? { executablePath: process.env.PW_CHROMIUM } : {});
const fonts = `<link rel="stylesheet" href="file://${path.join(ROOT, 'ipodsync/src/FLACie.Server/wwwroot/fonts/ls/fonts.css')}">`;
for (const [lid, l] of Object.entries(LAYOUTS)) for (const z of SIZES) for (const [st] of STATES) {
  const page = await browser.newPage({ viewport: { width: z.w, height: z.h }, deviceScaleFactor: 1 });
  await page.setContent(`<!doctype html><html><head>${fonts}<style>${CSS}</style></head><body>${l.body(st, z)}</body></html>`, { waitUntil: 'load' });
  await page.waitForTimeout(150);
  await page.screenshot({ path: path.join(OUT, `${lid}-${z.id}-${st}.png`) });
  await page.close();
}
await browser.close();

// the page: one block per size; a row per state; columns Today, A, B, C
// Today: the player before 1g-2 (kept in np-1g2/before-*); Built: the parity page's current shot, layout B as built
const cur = (z, st) => `np-1g2/before-${z.cur}-${st === 'np' ? 'np' : 'np-' + st}.jpg`;
const built = (z, st) => `img/web-${z.cur}-${st === 'np' ? 'np' : 'np-' + st}.jpg`;
const cell = (src, z) => `<td><img src="${src}" style="width:${z.w >= 900 ? 420 : 200}px"></td>`;
const html = `<!doctype html><meta charset="utf-8"><title>Now Playing 1g-2 mock-ups</title>
<style>body{font:14px system-ui;background:#111;color:#eee;padding:20px}td,th{vertical-align:top;padding:6px}img{border:1px solid #333;border-radius:6px}th{text-align:left}h2{margin-top:30px}p{max-width:90ch;color:#bbb}</style>
<h1>Now Playing rethink (1g-2): three layouts</h1>
<p>The owner's points (docs/plans/one-app-overhaul.md 1g-2): the buttons look weird and Shuffle / Repeat beside the time is not right; in Lyrics / Info the toggles look odd, the room is not used and the seek bar is squashed; unfolded, the cover is too small; the full-screen Info graphs' buttons need the kit. "Before" is the web player before 1g-2; "Built" is layout B as built (the parity shots). <b>Chosen: B</b>: Shuffle and Repeat are modes (on / off), and the kit\'s slot is the piece with an "on" state, so they sit with Lyrics and Info; the transport keeps its big row alone (A put them back in it, which the owner ruled out; C hides them on the cover and loses them in Lyrics / Info); the seek line is never squashed between them. <a href="index.html" style="color:#9cf">Back to the parity page</a></p>
${Object.entries(LAYOUTS).map(([id, l]) => `<p><b>${l.name}</b>: ${l.note}</p>`).join('')}
${SIZES.map((z) => `<h2>${z.name}</h2><table><tr><th></th><th>Before</th>${Object.values(LAYOUTS).map((l) => `<th>${l.name}</th>`).join('')}<th>Built: B (web)</th></tr>
${STATES.map(([st, sn]) => `<tr><th>${sn}</th>${cell(cur(z, st), z)}${Object.keys(LAYOUTS).map((id) => cell(`np-1g2/${id}-${z.id}-${st}.png`, z)).join('')}${cell(built(z, st), z)}</tr>`).join('\n')}</table>`).join('\n')}`;
fs.writeFileSync(path.join(ROOT, 'docs/parity/np-1g2.html'), html);
console.log('wrote docs/parity/np-1g2.html and', fs.readdirSync(OUT).length, 'mock-ups');
