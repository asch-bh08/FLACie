// iPod mode, Look › Desk: the moving parts of the desk. One loop reads the player's analyser (flacie.vizFrame) and turns it into the earbuds' sound:
// a VU meter's needle, arcs from each bud (bass, mids, highs; each against the song's own range), a ring on every beat (and on the coffee), current running down the cable, a small twitch of the buds, and CSS variables
// (--low --mid --high --beat) the scene's light leak breathes with. Also the dust in the sun, the discs (the release's own picture, or a print from the cover)
// the viewer's own date, and the journal's lyrics (timed from the song's own clock). Without an analyser (iOS, nothing played yet, a phone on mobile data) it keeps a calm fixed pulse and no beats. It sleeps while the tab is hidden;
// with reduced motion it paints one still frame and no loop at all. Only what changed is written to the page.
window.desk = (() => {
  const NS = 'http://www.w3.org/2000/svg';
  let s = null;   // the running scene: { el, reduced, raf, last, lastDust, avg, lastBeat, beat, beats, motes, ... }
  const CALM = { low: 0.2, mid: 0.15, high: 0.1, live: false };

  const mean = (f, a, b) => { let t = 0; for (let i = a; i <= b; i++) t += f[i]; return t / (b - a + 1) / 255; };
  function bands() {
    // mobile data: no second copy of the song for the earbuds, just the calm pulse (and the analyser let go if it was open)
    let cell = false; try { cell = !!(window.flacie && window.flacie.metered && window.flacie.metered()); } catch { }
    if (cell) { if (!s.calm) { s.calm = true; try { window.flacie.vizRelease && window.flacie.vizRelease(); } catch { } } return CALM; }
    s.calm = false;
    let f = null;
    try { f = window.flacie && window.flacie.vizFrame ? window.flacie.vizFrame() : null; } catch { f = null; }
    if (!f || !f.playing || !f.freq || f.freq.length < 161) return CALM;
    return { low: mean(f.freq, 1, 8), mid: mean(f.freq, 9, 40), high: mean(f.freq, 41, 160), live: true };
  }

  // each band against the song's own recent range: its peak (falls back over about 3 s) and its floor (rises over about 2 s). Partly the level against the
  // peak (a steady song still glows), mostly where it sits between floor and peak (what moves): a quietly mastered song moves as much as a loud one
  const BANDS = ['low', 'mid', 'high'];
  function scaled(b, dt) {
    if (!b.live) { s.peak = null; return b; }
    if (!s.peak) { s.peak = [b.low, b.mid, b.high]; s.floor = [b.low, b.mid, b.high]; }
    const out = { live: true };
    BANDS.forEach((k, j) => {
      const raw = b[k];
      s.peak[j] = Math.max(raw, s.peak[j] - (s.peak[j] - raw) * Math.min(1, dt / 3));
      s.floor[j] = Math.min(raw, s.floor[j] + (raw - s.floor[j]) * Math.min(1, dt / 2));
      const top = Math.max(0.04, s.peak[j]), span = Math.max(0.05, s.peak[j] - s.floor[j]);
      out[k] = Math.min(1, Math.max(0, 0.35 * raw / top + 0.65 * (raw - s.floor[j]) / span));
    });
    return out;
  }

  function arcPath(j, lv) {
    const r = 30 + j * 11 + lv * 14, a = (0.55 + j * 0.08) * (0.55 + 0.45 * lv);   // the arcs open out of the shell, away from the stem
    const x = (Math.sin(a) * r).toFixed(1), y = (-Math.cos(a) * r).toFixed(1);
    return `M ${-x} ${y} A ${r.toFixed(1)} ${r.toFixed(1)} 0 0 1 ${x} ${y}`;
  }
  // writes only what changed (the same value written again still costs the page a style or attribute change)
  const attr = (el, name, v) => { const k = '_' + name; if (el[k] !== v) { el[k] = v; el.setAttribute(name, v); } };
  const prop = (el, name, v) => { const m = el._props || (el._props = {}); if (m[name] !== v) { m[name] = v; el.style.setProperty(name, v); } };

  function paint(b, now) {
    const el = s.el;
    prop(el, '--low', b.low.toFixed(2)); prop(el, '--mid', b.mid.toFixed(2)); prop(el, '--high', b.high.toFixed(2)); prop(el, '--beat', s.beat.toFixed(2));
    // the coffee follows the bass (desk.css draws its surface from --bass)
    if (s.coffee) prop(s.coffee, '--bass', (s.reduced ? 0.2 : b.low).toFixed(2));
    // the VU meter's needle: bass and mids, with a meter's ballistics (it eases to the level in about a tenth of a second); at rest on the left stop without music
    // (more than one in a scene: data-band picks what a needle follows, low, mid, high, or by default bass and mids together)
    for (const n of s.needles) {
      const band = n.dataset.band, lv = band === 'low' ? b.low : band === 'mid' ? b.mid : band === 'high' ? b.high : 0.55 * b.low + 0.45 * b.mid;
      const target = b.live ? -42 + 84 * Math.min(1, lv) : -42;
      n._vu = (n._vu ?? -42) + (target - (n._vu ?? -42)) * Math.min(1, (s.dt || 0.016) / 0.12);
      prop(n, '--a', n._vu.toFixed(1));
    }
    // arcs per bud: bass, mids, highs
    const lv = [b.low, b.mid, b.high];
    for (const p of s.arcs) {
      const j = +p.dataset.arc, v = s.reduced ? 0.25 : lv[j];
      attr(p, 'd', arcPath(j, v)); attr(p, 'opacity', (0.12 + 0.85 * v * (1 - j * 0.12)).toFixed(2));
    }
    if (s.reduced) return;
    // current down the cable: faster, longer and brighter with the bass
    const t = now / 1000, low = 0.35 + 0.65 * Math.min(1, b.low * 1.4), speed = 150 + 230 * low;
    for (const p of s.pulses) {
      const L = s.lengths.get(p) || 1000, a = p.dataset.pulse === 'a';
      attr(p, 'stroke-dasharray', a ? `${(16 + 28 * low).toFixed(1)} ${(95 - 30 * low).toFixed(1)}` : `${(6 + 10 * b.high).toFixed(1)} 150`);
      attr(p, 'stroke-dashoffset', (-(t * speed * (a ? 1 : 1.35) + (a ? 0 : 60)) % L).toFixed(1));
      attr(p, 'opacity', (a ? 0.2 + 0.6 * low : 0.12 + 0.45 * b.high).toFixed(2));
    }
    // the buds twitch on the bass
    s.buds.forEach((g, i) => {
      const k = s.beat < 0.005 ? 0 : s.beat, x = k ? (Math.sin(t * 61 + i) * k * 1.4).toFixed(2) : '0', y = k ? (Math.cos(t * 53 + i) * k * 1.4).toFixed(2) : '0';
      const shell = g.firstElementChild, v = k ? `scale(${(1 + k * 0.09).toFixed(3)}) translate(${x}px, ${y}px)` : '';
      if (shell._tf !== v) { shell._tf = v; shell.style.transform = v; }
    });
    // rings growing out of the buds
    for (const c of [...s.rippleBox?.children || []]) {
      const age = (now - +c.dataset.born) / 1000; if (age > 1.6) { c.remove(); continue; }
      c.setAttribute('r', (24 + age * 70).toFixed(1)); c.setAttribute('opacity', (0.7 * (1 - age / 1.6)).toFixed(3)); c.setAttribute('stroke-width', (2.6 * (1 - age / 1.6) + 0.3).toFixed(2));
    }
    if (now - s.lastDust > 40) { s.lastDust = now; dust(); }
  }

  function tick(now) {
    if (!s) return;
    s.raf = requestAnimationFrame(tick);
    if (document.hidden || now - s.last < 15) return;
    const dt = Math.min(0.1, (now - (s.last || now)) / 1000); s.last = now;
    const b = scaled(bands(), dt); s.dt = dt;
    // beats: the (scaled) bass jumping clearly above its last half second, at most one every 280 ms
    s.avg += (b.low - s.avg) * Math.min(1, dt / 0.5);
    if (b.live && b.low > 0.6 && b.low - s.avg > 0.3 && now - s.lastBeat > 280) { s.lastBeat = now; s.beat = 1; s.beats++; ripple(b.low - s.avg); }
    s.beat *= Math.pow(0.02, dt);   // the beat fades in about a second
    paint(b, now);
  }

  function ripple(hit) {
    // a ring on the coffee's surface, spreading further on a harder kick (gone again when its animation ends)
    if (s.coffee) { const r = document.createElement('i'); r.className = 'desk-ring'; r.style.setProperty('--k', (3.5 + 3 * Math.min(1, hit || 0.5)).toFixed(2)); s.coffee.appendChild(r); setTimeout(() => r.remove(), 1300); }
    if (!s.rippleBox) return;
    for (const g of s.buds) {
      const c = document.createElementNS(NS, 'circle');
      c.setAttribute('cx', g.dataset.cx); c.setAttribute('cy', g.dataset.cy); c.setAttribute('r', 24); c.setAttribute('fill', 'none'); c.setAttribute('stroke', '#fff3df');
      c.dataset.born = performance.now(); s.rippleBox.appendChild(c);
    }
  }

  function dust() {
    const cv = s.dust; if (!cv) return;
    const W = cv.clientWidth, H = cv.clientHeight; if (!W || !H) return;
    if (cv.width !== W || cv.height !== H) { cv.width = W; cv.height = H; }
    const g = cv.getContext('2d'); g.clearRect(0, 0, W, H);
    for (const m of s.motes) {
      m.x += m.vx + Math.sin(m.tw) * 0.00003; m.y += m.vy; m.tw += 0.012; if (m.y < -0.02) { m.y = 1.02; m.x = Math.random(); }
      const a = 0.1 + 0.32 * (0.5 + 0.5 * Math.sin(m.tw * 1.7));
      g.fillStyle = `rgba(255,226,180,${a.toFixed(3)})`; g.beginPath(); g.arc(m.x * W, m.y * H, m.r * W / 1920, 0, 6.283); g.fill();
    }
  }

  // cors: the picture is drawn into a canvas (which must stay readable); the disc picture is only shown, from FLACie itself
  // the journal's lyrics, timed from the song's own clock (flacie.clock) ten times a second, whatever the motion setting: every line up to the one being
  // sung is shown, that one is written in (its --dur from its length), and it is kept in view when the page is full. A quarter second early: the pen
  // starts as the line is sung. Lines without times are spread over the song.
  const LEAD = 0.25;
  function follow() {
    const box = s && s.el.querySelector('.j-lines'); if (!box) return;
    const lines = box.children, n = lines.length; if (!n) return;
    let c = null; try { c = window.flacie && window.flacie.clock ? window.flacie.clock() : null; } catch { c = null; }
    if (!c) return;
    const t = c[0], d = c[1], at = (t + LEAD) * 1000;
    let cur = -1;
    if ('synced' in box.dataset) { for (let i = 0; i < n; i++) { if (+lines[i].dataset.ms <= at) cur = i; else break; } }
    else cur = d > 0 ? Math.min(n - 1, Math.floor((t + LEAD) / d * n)) : n - 1;
    if (box._cur === cur && box._first === lines[0] && box._n === n) return;   // nothing changed (a new song brings new elements)
    if (box._first !== lines[0]) box._page = 0;
    box._cur = cur; box._first = lines[0]; box._n = n;
    for (let i = 0; i < n; i++) {
      const l = lines[i];
      l.classList.toggle('on', i <= cur);
      if (i === cur) { if (!l.classList.contains('writing')) { l.classList.add('writing'); writeIn(l, Math.min(3200, Math.max(900, 600 + l.textContent.length * 45))); } }
      else l.classList.remove('writing');
    }
    const l = lines[cur]; if (!l) { box.scrollLeft = 0; box._page = 0; return; }
    // the page the line being sung is on: the lines flow in page columns (two a spread in the notebook, one a note on a phone); show its spread or note,
    // turning the page (or peeling the note off) when the song goes on to the next one
    const cs = getComputedStyle(box), per = parseInt(cs.columnCount, 10) || 1, gap = parseFloat(cs.columnGap) || 0;
    const step = (box.clientWidth - gap * (per - 1)) / per + gap;
    const page = Math.floor(Math.max(0, Math.round(l.offsetLeft / step)) / per), target = page * per * step;
    if (Math.abs(box.scrollLeft - target) > 1) {
      if (page === (box._page || 0) + 1 && !s.reduced) turnPage(box, per);
      box.scrollLeft = target;
    }
    box._page = page;
  }

  // a page turn (the notebook: the right page turns over to the left about the spine) or a peel (a sticky note lifts off the pad): a copy of the journal
  // as it was is laid over it and goes, while the journal itself shows the next spread or note underneath. Short and quiet.
  function turnPage(box, per) {
    const j = box.closest('.desk-journal'); if (!j || !j.animate) return;
    const ghost = j.cloneNode(true);
    ghost.removeAttribute('data-testid'); ghost.querySelectorAll('[data-testid]').forEach((n) => n.removeAttribute('data-testid'));
    ghost.querySelectorAll('.writing').forEach((n) => n.classList.remove('writing'));
    ghost.classList.add(per > 1 ? 'j-turn' : 'j-peel'); ghost.setAttribute('aria-hidden', 'true');
    j.after(ghost);
    const gl = ghost.querySelector('.j-lines'); if (gl) gl.scrollLeft = box.scrollLeft;
    let a;
    if (per > 1) {
      ghost.style.clipPath = 'inset(-3% -3% -3% 50%)'; ghost.style.transformOrigin = '50% 50%'; ghost.style.backfaceVisibility = 'hidden';
      a = ghost.animate([{ transform: 'perspective(160cqh) rotateY(0deg)' }, { transform: 'perspective(160cqh) rotateY(-178deg)' }], { duration: 850, easing: 'cubic-bezier(.45,.05,.35,1)' });
    } else {
      ghost.style.transformOrigin = '30% 0%';
      a = ghost.animate([{ transform: 'translate(0, 0) rotate(0deg)', opacity: 1 }, { transform: 'translate(0, -2%) rotate(-2deg)', opacity: 1, offset: .25 }, { transform: 'translate(9%, -18%) rotate(9deg)', opacity: 0 }], { duration: 700, easing: 'cubic-bezier(.3,.1,.3,1)' });
    }
    const done = () => ghost.remove();
    a.onfinish = done; a.oncancel = done; setTimeout(done, 1600);
  }

  // the pen: the line is revealed left to right, a row at a time (a wrapped line's second row only once the first is written), at one speed throughout.
  // One animation of a clip polygon down the rows' right edges: rows already written are open to their end, the row being written up to the pen, the rest shut.
  function writeIn(l, ms) {
    if (s.reduced || !l.animate) return;
    const box = l.getBoundingClientRect(), r = document.createRange(); r.selectNodeContents(l);
    const byTop = new Map();
    for (const q of r.getClientRects()) {
      if (q.width <= 0) continue;
      const k = Math.round(q.top - box.top), row = byTop.get(k);
      if (row) { row.l = Math.min(row.l, q.left - box.left); row.r = Math.max(row.r, q.right - box.left); }
      else byTop.set(k, { t: q.top - box.top, b: q.bottom - box.top, l: q.left - box.left, r: q.right - box.left });
    }
    const rows = [...byTop.values()].sort((a, b) => a.t - b.t);
    if (!rows.length) return;
    rows[rows.length - 1].b = Math.max(rows[rows.length - 1].b, box.height);
    const poly = (k, x) => {
      const pts = [[0, 0]];
      rows.forEach((row, j) => { const e = j < k ? box.width : j === k ? x : 0; pts.push([e, j === 0 ? 0 : row.t], [e, j === rows.length - 1 ? box.height + 2 : rows[j + 1].t]); });
      pts.push([0, box.height + 2]);
      return `polygon(${pts.map(([x, y]) => `${x.toFixed(1)}px ${y.toFixed(1)}px`).join(', ')})`;
    };
    const total = rows.reduce((n, row) => n + Math.max(1, row.r), 0);
    const frames = [{ clipPath: poly(0, 0), offset: 0 }]; let done = 0;
    rows.forEach((row, k) => { done += Math.max(1, row.r); frames.push({ clipPath: poly(k, row.r + 2), offset: Math.min(1, done / total) }); });
    l.animate(frames, { duration: ms, easing: 'linear' });
  }

  const loadImage = async (url, cors) => { const img = new Image(); if (cors) img.crossOrigin = 'anonymous'; img.src = url; try { await img.decode(); return img; } catch { return null; } };

  return {
    /** Starts the desk's loop on a scene element (the one carrying data-bud, data-arc, data-pulse, data-ripples, data-dust inside it). */
    start(el, opts) {
      this.stop(true);
      if (!el) return;
      const reduced = !!(opts && opts.reduced) || matchMedia('(prefers-reduced-motion: reduce)').matches;
      const pulses = [...el.querySelectorAll('[data-pulse]')], lengths = new Map();
      for (const p of pulses) { const ref = document.querySelector(p.getAttribute('href')); try { lengths.set(p, ref ? ref.getTotalLength() : 1000); } catch { lengths.set(p, 1000); } }
      s = { el, reduced, raf: 0, last: 0, lastDust: 0, avg: 0.2, lastBeat: 0, beat: 0, beats: 0,
        arcs: [...el.querySelectorAll('[data-arc]')], pulses, lengths, buds: [...el.querySelectorAll('[data-bud]')],
        rippleBox: el.querySelector('[data-ripples]'), dust: el.querySelector('canvas[data-dust]'), coffee: el.querySelector('[data-coffee]'), needles: [...el.querySelectorAll('[data-needle]')],
        motes: Array.from({ length: 60 }, () => ({ x: Math.random(), y: Math.random(), r: 0.6 + Math.random() * 1.6, vx: (Math.random() - 0.5) * 0.0001, vy: -0.00003 - Math.random() * 0.0001, tw: Math.random() * 6.28 })) };
      // reduced motion: one still frame (no analyser, no loop)
      if (reduced) paint(CALM, performance.now()); else s.raf = requestAnimationFrame(tick);
      s.lyrics = setInterval(follow, 100); follow();
      // the scene's shape decides its layout (and the cable's route, drawn on the server): tell it when it turns portrait or landscape
      if (opts && opts.ref) {
        let last = !!opts.portrait;
        const check = () => { const p = window.desk.portrait(el.clientWidth, el.clientHeight); if (p !== last) { last = p; opts.ref.invokeMethodAsync('SetPortrait', p).catch(() => { }); } };
        s.ro = new ResizeObserver(check); s.ro.observe(el); check();
      }
    },
    /** keep: another start follows straight away (the analyser stays open). */
    stop(keep) {
      if (!s) return;
      cancelAnimationFrame(s.raf); clearInterval(s.lyrics); if (s.ro) s.ro.disconnect();
      if (s.rippleBox) s.rippleBox.replaceChildren();
      if (s.coffee) s.coffee.replaceChildren();
      s = null;
      if (!keep) { try { window.flacie && window.flacie.vizRelease && window.flacie.vizRelease(); } catch { } }
    },
    loops() { return s && s.raf ? 1 : 0; },
    beats() { return s ? s.beats : 0; },
    /** The portrait layout: a scene narrower than this (desk.css's @container desk query says the same). */
    portrait(w, h) { return w / Math.max(1, h) < 1.15; },
    /** The viewer's own day: ["15 oct", "thursday"]. */
    today() {
      const d = new Date();
      return [d.toLocaleDateString('en-GB', { day: 'numeric', month: 'short' }).toLowerCase().replace('.', ''), d.toLocaleDateString('en-GB', { weekday: 'long' }).toLowerCase()];
    },

    /** A disc print made from a cover: a closer crop of the art, a darker rim, ARTIST · ALBUM round the hub, the small print, the hub cut out. */
    async discPrint(coverUrl, artist, album) {
      const size = 512, c = size / 2, cv = Object.assign(document.createElement('canvas'), { width: size, height: size }), g = cv.getContext('2d');
      const img = await loadImage(coverUrl, true); if (!img) return null;
      g.save(); g.beginPath(); g.arc(c, c, c, 0, 7); g.clip();
      g.drawImage(img, -size * 0.3, -size * 0.25, size * 1.6, size * 1.6);
      const vg = g.createRadialGradient(c, c, size * 0.2, c, c, c); vg.addColorStop(0, 'rgba(0,0,0,0)'); vg.addColorStop(1, 'rgba(0,0,0,.28)'); g.fillStyle = vg; g.fillRect(0, 0, size, size);
      g.restore();
      const ring = (text, r, px, weight, alpha, start) => {
        g.save(); g.translate(c, c); g.fillStyle = `rgba(255,255,255,${alpha})`; g.font = `${weight} ${px}px "Helvetica Neue", Arial, sans-serif`; g.textBaseline = 'middle';
        let a = start; for (const ch of text) { const w = g.measureText(ch).width + px * 0.18; g.save(); g.rotate(a); g.translate(0, -r); g.fillText(ch, -w / 2, 0); g.restore(); a += w / r; }
        g.restore();
      };
      ring(`${(artist || '').toUpperCase()}  ·  ${(album || '').toUpperCase()}`, size * 0.205, 15, 700, 0.92, -1.2);
      ring('STEREO  ·  DIGITAL AUDIO', size * 0.44, 10, 600, 0.6, 2.2);
      g.strokeStyle = 'rgba(255,255,255,.35)'; g.lineWidth = 1.2; g.beginPath(); g.arc(c, c, size * 0.235, 0, 7); g.stroke();
      // the clear inner ring and the hole
      g.fillStyle = 'rgba(225,228,234,.92)'; g.beginPath(); g.arc(c, c, size * 0.15, 0, 7); g.fill();
      g.globalCompositeOperation = 'destination-out'; g.fillStyle = '#000'; g.beginPath(); g.arc(c, c, size * 0.066, 0, 7); g.fill();
      try { return cv.toDataURL('image/png'); } catch { return null; }   // a cover from another site without CORS: the caller keeps the plain disc
    },

    /** Puts an album's disc on the disc element: the release's own picture (discUrl, served by FLACie) when there is one, otherwise a print made from the cover.
     *  The old album's disc goes at once; an answer that arrives after another album came up is dropped. (The picture never goes back to the server:
     *  a print is far bigger than a Blazor message.) */
    async discInto(el, discUrl, coverUrl, artist, album) {
      if (!el) return false;
      const want = (el._discWant = {});
      el.style.removeProperty('--art');
      let url = null;
      if (discUrl && await loadImage(discUrl)) url = discUrl;
      else if (coverUrl) url = await this.discPrint(coverUrl, artist, album);
      if (el._discWant !== want) return false;   // another album came up meanwhile
      if (!url) return false;
      el.style.setProperty('--art', `url("${url.replace(/["\\\n]/g, '')}")`); return true;
    },
  };
})();
