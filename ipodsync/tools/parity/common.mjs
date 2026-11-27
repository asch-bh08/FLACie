// Shared by run.mjs (web + report) and phone.mjs (emulator driver): the one size list (plan part 1h), the screen list, the flag checks that work on
// pictures (empty band), and the page that puts web and phone side by side.
import fs from 'node:fs';
import path from 'node:path';

// css/dp size + the pixel density the web is drawn at. `phone: true` = also run on the emulator (wm size w*2 x h*2, wm density 320: 2 px per dp,
// so the emulator's pictures come out at twice the dp size like the web's at dpr 2, and halve to the same width).
export const SIZES = [
  { id: 'p360x640', name: 'Phone 360×640', w: 360, h: 640, dpr: 3, phone: true },
  { id: 'p375x667', name: 'Phone 375×667', w: 375, h: 667, dpr: 2, phone: true },
  { id: 'p390x844', name: 'Phone 390×844', w: 390, h: 844, dpr: 3, phone: true },
  { id: 'p412x915', name: 'Phone 412×915', w: 412, h: 915, dpr: 2.625, phone: true },
  { id: 'p412x1000', name: 'Tall phone 412×1000 (21:9)', w: 412, h: 1000, dpr: 2.625, phone: true },
  { id: 'fold-cover', name: 'Fold cover 344×882', w: 344, h: 882, dpr: 2.625, phone: true },
  { id: 'fold-inner', name: 'Fold inner 690×829', w: 690, h: 829, dpr: 2.625, phone: true },
  { id: 'p-land', name: 'Phone on its side 844×390', w: 844, h: 390, dpr: 3, phone: true },
  { id: 'tab768x1024', name: 'Tablet 768×1024', w: 768, h: 1024, dpr: 2, phone: true },
  { id: 'tab1024x768', name: 'Tablet 1024×768', w: 1024, h: 768, dpr: 2, phone: true },
  { id: 'lap1280x720', name: 'Laptop 1280×720', w: 1280, h: 720, dpr: 1 },
  { id: 'lap1366x768', name: 'Laptop 1366×768', w: 1366, h: 768, dpr: 1 },
  // browser zoom = a smaller window at a higher density
  { id: 'lap1366-125', name: 'Laptop 1366×768 at 125%', w: 1093, h: 614, dpr: 1.25 },
  { id: 'lap1366-150', name: 'Laptop 1366×768 at 150%', w: 911, h: 512, dpr: 1.5 },
  { id: 'pc1920x1080', name: 'PC 1920×1080', w: 1920, h: 1080, dpr: 1 },
  { id: 'uw2560x1080', name: 'Ultrawide 2560×1080', w: 2560, h: 1080, dpr: 1 },
  { id: '4k-200', name: '4K at 200%', w: 1920, h: 1080, dpr: 2 },
];

export const SCREENS = [
  ['home', 'Home'], ['search', 'Search (with a query)'], ['library', 'Library'], ['songs', 'Songs'], ['album', 'Album'], ['artist', 'Artist'],
  ['playlist', 'Playlist'], ['playlist-edit', 'Edit playlist'], ['favourites', 'Favourites'], ['devices', 'Devices'], ['settings', 'Settings'],
  ['downloads', 'Downloads / Offline'], ['account', 'Account'],
  ['np', 'Now Playing'], ['np-lyrics', 'Now Playing: Lyrics'], ['np-upnext', 'Now Playing: Up next'], ['np-info', 'Now Playing: Info'],
].map(([id, name]) => ({ id, name }));

export const pick = (list, csv) => (csv ? list.filter((x) => csv.split(',').includes(x.id)) : list);

/** A horizontal strip taller than this share of the picture with nothing in it is flagged. */
export const BAND = 0.12;

// Runs inside a Chromium page: decodes one PNG, finds the tallest strip of rows with no edge in them (a blurred or plain background has none; text,
// icons and covers do), and gives back a smaller JPEG. Pixels, not the DOM, so web and phone are judged the same way.
export const ANALYSE = async ({ b64, scale, quality }) => {
  const img = new Image(); img.src = 'data:image/png;base64,' + b64; await img.decode();
  const W = img.naturalWidth, H = img.naturalHeight;
  const c = new OffscreenCanvas(W, H), g = c.getContext('2d', { willReadFrequently: true }); g.drawImage(img, 0, 0);
  const d = g.getImageData(0, 0, W, H).data;
  let best = { top: 0, h: 0 }, run = 0;
  for (let y = 0; y <= H; y++) {
    let flat = y < H;
    if (flat) {
      for (let x = 3, o = y * W * 4; x < W; x += 2) {
        const i = o + x * 4, j = i - 12;
        const dl = Math.abs(d[i] - d[j]) + Math.abs(d[i + 1] - d[j + 1]) + Math.abs(d[i + 2] - d[j + 2]);
        if (dl > 36) { flat = false; break; }
      }
    }
    if (flat) run++; else { if (run > best.h) best = { top: y - run, h: run }; run = 0; }
  }
  const w2 = Math.max(1, Math.round(W * scale)), h2 = Math.max(1, Math.round(H * scale));
  const s = new OffscreenCanvas(w2, h2), sg = s.getContext('2d'); sg.imageSmoothingQuality = 'high'; sg.drawImage(img, 0, 0, w2, h2);
  const blob = await s.convertToBlob({ type: 'image/jpeg', quality });
  const buf = new Uint8Array(await blob.arrayBuffer()); let bin = '';
  for (let i = 0; i < buf.length; i += 0x8000) bin += String.fromCharCode(...buf.subarray(i, i + 0x8000));
  return { jpeg: btoa(bin), w: W, h: H, band: { top: best.top / H, h: best.h / H } };
};

/** PNG file -> JPEG file next to the page (+ the empty band). `cssW` is the picture's width in css/dp pixels: shots are kept at that width (half of a
 *  2x picture) and halved again when wider than 1024, so a PC shot is 960 wide and a phone's 390. */
export async function shrink(page, png, jpgOut, cssW) {
  const b64 = fs.readFileSync(png).toString('base64');
  const W = pngWidth(png);
  const target = cssW > 1024 ? cssW / 2 : cssW;
  const r = await page.evaluate(ANALYSE, { b64, scale: Math.min(1, target / W), quality: 0.7 });
  fs.mkdirSync(path.dirname(jpgOut), { recursive: true });
  fs.writeFileSync(jpgOut, Buffer.from(r.jpeg, 'base64'));
  const flags = [];
  if (r.band.h > BAND) flags.push({ kind: 'band', text: `empty band ${Math.round(r.band.top * 100)}–${Math.round((r.band.top + r.band.h) * 100)}% (${Math.round(r.band.h * 100)}% of the height)`, band: r.band });
  return flags;
}
const pngWidth = (f) => { const b = Buffer.alloc(24); const fd = fs.openSync(f, 'r'); fs.readSync(fd, b, 0, 24, 0); fs.closeSync(fd); return b.readUInt32BE(16); };

const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

/** data = { web: {at, sizes: {sizeId: {screenId: {img, flags[]}}}}, phone: {...} } -> index.html. One row per screen, one column pair per size. */
export function writePage(outDir, data) {
  const sides = ['web', 'phone'];
  const cell = (side, size, scr) => {
    const r = data[side]?.sizes?.[size.id]?.[scr.id];
    if (!r) return `<td class="none">${side === 'phone' && !size.phone ? '' : 'not shot'}</td>`;
    const band = r.flags.find((f) => f.band);
    const flags = r.flags.map((f) => `<li class="${f.kind}">${esc(f.text)}</li>`).join('');
    return `<td class="${r.flags.length ? 'bad' : 'ok'}"><a href="${esc(r.img)}" target="_blank"><span class="shot">` +
      `<img loading="lazy" src="${esc(r.img)}" alt="${esc(side + ' ' + scr.name + ' ' + size.name)}">` +
      (band ? `<i class="bandbox" style="top:${(band.band.top * 100).toFixed(1)}%;height:${(band.band.h * 100).toFixed(1)}%"></i>` : '') +
      `</span></a>${r.error ? `<p class="err">${esc(r.error)}</p>` : ''}<ul>${flags}</ul></td>`;
  };
  const problems = [];
  for (const side of sides) for (const size of SIZES) for (const scr of SCREENS) {
    const r = data[side]?.sizes?.[size.id]?.[scr.id];
    if (r?.flags?.length) problems.push([side, size, scr, r.flags]);
  }
  const sec = SIZES.filter((s) => sides.some((sd) => data[sd]?.sizes?.[s.id])).map((size) => `
<section id="${size.id}"><h2>${esc(size.name)} <small>${size.w}×${size.h} @${size.dpr}x</small></h2>
<table><thead><tr><th>Screen</th><th>Web</th><th>Phone</th></tr></thead><tbody>
${SCREENS.map((scr) => `<tr><th>${esc(scr.name)}</th>${cell('web', size, scr)}${cell('phone', size, scr)}</tr>`).join('\n')}
</tbody></table></section>`).join('\n');
  const html = `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Parity check</title>
<style>
:root{--bg:#f6f6f4;--fg:#1b1b1b;--mut:#6b6b6b;--card:#fff;--line:#ddd;--bad:#c62828;--badbg:#fdecea;--ok:#2e7d32}
@media (prefers-color-scheme:dark){:root:not([data-theme=light]){--bg:#141414;--fg:#eee;--mut:#9a9a9a;--card:#1f1f1f;--line:#333;--bad:#ff6b6b;--badbg:#3a1c1c;--ok:#7bd88f}}
:root[data-theme=dark]{--bg:#141414;--fg:#eee;--mut:#9a9a9a;--card:#1f1f1f;--line:#333;--bad:#ff6b6b;--badbg:#3a1c1c;--ok:#7bd88f}
body{margin:0;padding:16px;background:var(--bg);color:var(--fg);font:14px/1.4 system-ui,sans-serif}
h1{margin:0 0 4px}h2{margin:28px 0 8px}small,.mut{color:var(--mut);font-weight:400}
nav a{margin-right:10px;white-space:nowrap}a{color:inherit}
table{border-collapse:collapse;width:100%;table-layout:fixed}th,td{border:1px solid var(--line);padding:6px;vertical-align:top;background:var(--card)}
thead th:first-child,tbody th{width:140px;text-align:left}
.shot{position:relative;display:inline-block;max-width:100%}.shot img{display:block;max-width:100%;max-height:480px;height:auto}
.bandbox{position:absolute;left:0;right:0;background:rgba(229,57,53,.28);outline:2px dashed var(--bad)}
td.bad{background:var(--badbg)}ul{margin:4px 0 0;padding-left:16px;color:var(--bad)}.none{color:var(--mut)}.err{color:var(--bad)}
.sum li{color:var(--fg)}.wrap{overflow-x:auto}
</style></head><body>
<h1>Parity check</h1>
<p class="mut">Web shot ${esc(data.web?.at ?? 'never')} · phone shot ${esc(data.phone?.at ?? 'never')} (emulator, mock library). Flags: off screen,
overlapping controls, sideways scroll, and an empty band (a strip over ${BAND * 100}% of the height with nothing in it, drawn in red).
Made by <code>node ipodsync/tools/parity/run.mjs</code>.</p>
<nav>${SIZES.filter((s) => sides.some((sd) => data[sd]?.sizes?.[s.id])).map((s) => `<a href="#${s.id}">${esc(s.name)}</a>`).join(' ')}</nav>
<h2>Flagged (${problems.length})</h2>
<ul class="sum">${problems.map(([side, size, scr, f]) => `<li><a href="#${size.id}">${esc(size.name)}</a> · ${side} · ${esc(scr.name)}: ${esc(f.map((x) => x.text).join('; '))}</li>`).join('')}</ul>
<div class="wrap">${sec}</div>
</body></html>`;
  fs.writeFileSync(path.join(outDir, 'index.html'), html);
  return problems;
}
