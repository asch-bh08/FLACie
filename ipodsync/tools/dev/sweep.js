// Paste into the browser console on a signed-in FLACie Web page (the Browser pane's javascript tool works). Visits pages without reloading and
// reports anything that sticks out past the right/left edge of the window, text cut off in buttons, sideways page scroll, and a mini player
// that overlaps the phone tab bar. Resize the window first (e.g. 320x640, 360x740, 390x844, 768x1024, 1300x800).
//   await window.__sweep(['/', '/songs', '/albums', '/charts', '/settings', '/admin'])   -> { '/': 'ok', ... }
// Keep each call under ~40 s (about 12 pages) or the tool times out; the function stays defined until the page reloads.
window.__sweep = async (pages) => {
  const w = ms => new Promise(r => setTimeout(r, ms)); const out = {};
  for (const p of pages) {
    Blazor.navigateTo(p); await w(2000);
    const vw = innerWidth, bad = [];
    const scrolls = el => { let q = el.parentElement; while (q && q !== document.body) { const o = getComputedStyle(q).overflowX; if (o === 'auto' || o === 'scroll') return true; q = q.parentElement; } return false; };
    document.querySelectorAll('button, a, input, select, .badge, h1, h2, .card, .stat, .jf-card, .modal').forEach(el => {
      const r = el.getBoundingClientRect(); if (!r.width || !r.height) return;
      const cs = getComputedStyle(el); if (cs.visibility === 'hidden' || cs.display === 'none') return;
      if ((r.right > vw + 1 || r.left < -1) && !scrolls(el)) bad.push(`${el.tagName}.${(el.className + '').split(' ')[0]} "${(el.innerText || el.getAttribute('aria-label') || '').trim().slice(0, 22)}" ${Math.round(r.left)}→${Math.round(r.right)}`);
    });
    if (document.documentElement.scrollWidth > vw + 1) bad.push('PAGE SCROLLS SIDEWAYS ' + document.documentElement.scrollWidth + ' > ' + vw);
    const pl = document.querySelector('.player'), rl = document.querySelector('.rail');
    if (pl && rl && vw <= 760) { const a = pl.getBoundingClientRect(), b = rl.getBoundingClientRect(); if (a.bottom > b.top + 1) bad.push('mini player overlaps the tab bar'); }
    out[p] = bad.length ? bad.slice(0, 6) : 'ok';
  }
  return out;
};
