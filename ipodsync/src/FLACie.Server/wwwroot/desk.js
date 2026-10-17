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
    s.frame = f;
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

  // the preamp's spectrum (its SPECTRUM meter): sixteen bars from 40 Hz up to half the song's sample rate (what it can hold) on a log scale, each a column of segments, the top ones red like the VU's
  // red zone; they fall back slowly, with a peak segment that lingers. Drawn only while the meter shows it, from the same frame as everything else
  const SPEC_N = 16, SPEC_LO = 40, SPEC_SEGS = 12;
  function spectrum(b) {
    let c = s.spec; if (!c || !c.isConnected) c = s.spec = s.el.querySelector('canvas[data-spectrum]');   // (the preamp put away and back: a new canvas)
    if (!c || !c.offsetParent) return;
    const w = c.clientWidth, h = c.clientHeight; if (!w || !h) return;
    const dpr = Math.min(2, devicePixelRatio || 1);
    if (c.width !== Math.round(w * dpr) || c.height !== Math.round(h * dpr)) { c.width = Math.round(w * dpr); c.height = Math.round(h * dpr); }
    const g = c.getContext('2d'), W = c.width, H = c.height, f = s.frame, dt = s.dt || 0.016;
    if (!s.specV) { s.specV = new Float32Array(SPEC_N); s.specP = new Float32Array(SPEC_N); }
    const live = b.live && f && f.freq && f.rate, binHz = live ? f.rate / 2 / f.freq.length : 1;
    const SPEC_HI = live ? Math.min(f.rate, f.fileRate || f.rate) / 2 : 22050;
    for (let i = 0; i < SPEC_N; i++) {
      let v = 0;
      if (live) {
        const lo = SPEC_LO * Math.pow(SPEC_HI / SPEC_LO, i / SPEC_N), hi = SPEC_LO * Math.pow(SPEC_HI / SPEC_LO, (i + 1) / SPEC_N);
        const a = Math.max(1, Math.floor(lo / binHz)), z = Math.min(f.freq.length - 1, Math.max(a, Math.ceil(hi / binHz)));
        for (let k = a; k <= z; k++) v = Math.max(v, f.freq[k]); v /= 255;
      }
      s.specV[i] = Math.max(v, s.specV[i] - dt * 1.6);
      s.specP[i] = Math.max(s.specV[i], s.specP[i] - dt * 0.35);
    }
    g.clearRect(0, 0, W, H);
    const gap = W * 0.012, bw = (W - gap * (SPEC_N + 1)) / SPEC_N, sh = H / SPEC_SEGS;
    for (let i = 0; i < SPEC_N; i++) {
      const x = gap + i * (bw + gap), on = Math.round(s.specV[i] * SPEC_SEGS), pk = Math.min(SPEC_SEGS - 1, Math.round(s.specP[i] * SPEC_SEGS));
      for (let j = 0; j < SPEC_SEGS; j++) {
        const y = H - (j + 1) * sh, red = j >= SPEC_SEGS - 3, lit = j < on || (j === pk && pk > 0);
        g.fillStyle = lit ? (red ? '#c0392b' : '#3a2a1a') : 'rgba(58,42,26,.1)';
        g.fillRect(x, y + sh * 0.18, bw, sh * 0.64);
      }
    }
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
    // written on the few things that follow them (data-viz), not on the scene: a variable changed there is worked out again for everything inside it
    const vl = b.low.toFixed(2), vm = b.mid.toFixed(2), vh = b.high.toFixed(2), vb = s.beat.toFixed(2);
    for (const v of s.viz) { prop(v, '--low', vl); prop(v, '--mid', vm); prop(v, '--high', vh); prop(v, '--beat', vb); }
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
    spectrum(b);
    // arcs per bud: bass, mids, highs
    const lv = [b.low, b.mid, b.high];
    for (const p of s.arcs) {
      const j = +p.dataset.arc, v = s.reduced ? 0.25 : lv[j];
      attr(p, 'd', arcPath(j, v)); attr(p, 'opacity', (0.12 + 0.85 * v * (1 - j * 0.12)).toFixed(2));
    }
    if (s.reduced) return;
    // the discs turn (still while paused), the steam rises and the coffee's rings spread: on these frames, which are drawn anyway (animations of
    // their own would draw frames between them, and every frame puts the whole screen together again under the film)
    // (the desk's CD once in 10 s; a record, a tape's reels or a Discman's disc at their own speed: data-spin, in degrees a second)
    for (const d of s.el.querySelectorAll('[data-spin]')) { if (d.classList.contains('paused')) continue; d._rot = ((d._rot || 0) + (parseFloat(d.dataset.spin) || 36) * s.dt) % 360; d.style.rotate = d._rot.toFixed(1) + 'deg'; }
    steam(now / 1000); writing(now);
    for (const r of s.rings) {
      const p = Math.min(1, (now - r._born) / 1200), e = 1 - Math.pow(1 - p, 2.2);   // (easing out)
      r.style.scale = (1 + (r._k - 1) * e).toFixed(3); r.style.opacity = (0.95 * (1 - e)).toFixed(3);
    }
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
      const v = k ? `scale(${(1 + k * 0.09).toFixed(3)}) translate(${x}px, ${y}px)` : '';
      if (g._tf !== v) { g._tf = v; g.style.transform = v; }
    });
    // rings growing out of the buds
    for (const c of [...s.rippleBox?.children || []]) {
      const age = (now - +c.dataset.born) / 1000; if (age > 1.6) { c.remove(); continue; }
      c.setAttribute('r', (24 + age * 70).toFixed(1)); c.setAttribute('opacity', (0.7 * (1 - age / 1.6)).toFixed(3)); c.setAttribute('stroke-width', (2.6 * (1 - age / 1.6) + 0.3).toFixed(2));
    }
    if (now - s.lastDust > 40) { s.lastDust = now; dust(); }
  }

  // three wisps over the coffee, a third of the way apart: each rises, sways left and spreads over 7 s, fading in quickly and out slowly
  function steam(t) {
    s.steam.forEach((w, i) => {
      const p = ((t + i * 7 / 3) / 7) % 1, e = p * p * (3 - 2 * p);
      const o = p < 0.25 ? 0.8 * p / 0.25 : p < 0.7 ? 0.8 - 0.45 * (p - 0.25) / 0.45 : 0.35 * (1 - p) / 0.3;
      w.style.transform = `translate(${(-35 * e).toFixed(1)}%, ${(10 - 170 * e).toFixed(1)}%) scale(${(0.7 + 0.55 * e).toFixed(3)}, ${(0.6 + 0.75 * e).toFixed(3)}) rotate(${(-8 * e).toFixed(1)}deg)`;
      w.style.opacity = o.toFixed(3);
    });
  }

  function tick(now) {
    if (!s) return;
    s.raf = requestAnimationFrame(tick);
    // 30 frames a second while music plays, 15 while it does not (nothing then moves but the steam, the dust and the current down the cable): every frame makes the
    // browser put the whole desk together again under its light and film, so fewer frames is most of what this page costs
    const idle = !(s.frame && s.frame.playing) && s.beat < 0.05;
    if (document.hidden || now - s.last < (idle ? 66 : 31)) return;
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
    if (s.coffee) {
      const r = document.createElement('i'); r.className = 'desk-ring'; r._k = 3.5 + 3 * Math.min(1, hit || 0.5); r._born = performance.now(); s.coffee.appendChild(r); s.rings.push(r);
      setTimeout(() => { r.remove(); if (s) s.rings = s.rings.filter((x) => x !== r); }, 1300);
    }
    if (!s.rippleBox) return;
    for (const g of s.buds) {
      const c = document.createElementNS(NS, 'circle');
      c.setAttribute('cx', g.dataset.cx); c.setAttribute('cy', g.dataset.cy); c.setAttribute('r', 24); c.setAttribute('fill', 'none'); c.setAttribute('stroke', '#fff3df');
      c.dataset.born = performance.now(); s.rippleBox.appendChild(c);
    }
  }

  // the film's colours for this scene (desk.css, on the canvas), read again when the canvas changes size
  function film(cv) {
    const cs = getComputedStyle(cv), v = (n) => cs.getPropertyValue(n).trim();
    return { fade: v('--film-fade'), a: v('--leak-a'), b: v('--leak-b'), c: v('--leak-c'), o: parseFloat(v('--leak-o')) || 0, beat: parseFloat(v('--leak-beat')) || 0 };
  }
  // a colour with no alpha left (a gradient's end: towards the same colour, clear, not towards clear black)
  const clear = (c) => /^rgba?\(/.test(c) ? c.replace(/^rgba?\(([^,]+),([^,]+),([^,)]+).*$/, 'rgba($1,$2,$3,0)') : 'rgba(0,0,0,0)';
  // one leak: an ellipse rx by ry round (x, y), through the stops given ([offset, colour]), clear at 70%
  function leak(g, x, y, rx, ry, stops) {
    if (!stops[0][1] || stops[0][1] === 'transparent') return;
    g.save(); g.translate(x, y); g.scale(1, ry / rx);
    const gr = g.createRadialGradient(0, 0, 0, 0, 0, rx);
    for (const [o, c] of stops) gr.addColorStop(o, c);
    gr.addColorStop(0.7, clear(stops[stops.length - 1][1]));
    g.fillStyle = gr; g.fillRect(-rx, -rx, 2 * rx, 2 * rx); g.restore();
  }
  function dust() {
    const cv = s.dust; if (!cv) return;
    const W = cv.clientWidth, H = cv.clientHeight; if (!W || !H) return;
    if (cv.width !== W || cv.height !== H || !s.film) { cv.width = W; cv.height = H; s.film = film(cv); }
    const g = cv.getContext('2d'); g.clearRect(0, 0, W, H);
    // the lifted fade, and the light leak breathing with the beat (under the dust)
    const f = s.film;
    if (f.fade && f.fade !== 'transparent') { g.fillStyle = f.fade; g.fillRect(0, 0, W, H); }
    g.globalAlpha = Math.min(1, f.o + f.beat * (s.reduced ? 0 : s.beat));
    leak(g, W, 0, 0.6 * H, 0.48 * H, f.b && f.b !== 'transparent' ? [[0, f.a], [0.45, f.b]] : [[0, f.a]]);
    leak(g, 0, H, 0.34 * H, 0.44 * H, [[0, f.c]]);
    g.globalAlpha = 1;
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
  const LEAD = 0.35;
  // (the pen writes a line in 0.6 to 1.8 s: quick enough that it is written while it is sung, not after)
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
      if (i === cur) { if (!l.classList.contains('writing')) { l.classList.add('writing'); writeIn(l, Math.min(1800, Math.max(600, 300 + l.textContent.length * 26))); } }
      else l.classList.remove('writing');
    }
    const l = lines[cur]; if (!l) { box.scrollLeft = 0; box._page = 0; return; }
    // the page the line being sung is on: the lines flow in page columns (two a spread in the notebook, one a note on a phone); show its spread or note,
    // turning the page (or peeling the note off) when the song goes on to the next one
    const cs = getComputedStyle(box), per = parseInt(cs.columnCount, 10) || 1, gap = parseFloat(cs.columnGap) || 0;
    const step = (box.clientWidth - gap * (per - 1)) / per + gap;
    const page = Math.floor(Math.max(0, Math.round(l.offsetLeft / step)) / per), target = page * per * step;
    if (Math.abs(box.scrollLeft - target) > 1) {
      if (page === (box._page || 0) + 1 && !s.reduced) turnPage(box, per, target);
      box.scrollLeft = target;
    }
    box._page = page;
  }

  // a page turn (the notebook: the right page is lifted by its bottom corner and folded over the spine) or a peel (a sticky note: its corner curls up and over, then the
  // note comes away): paper folding, in two dimensions. The corner C is taken to a point Q; the crease is the perpendicular bisector of C and Q, and the part of the
  // leaf beyond it lies folded back, mirrored in the crease. For a page turn Q goes round to the mirror of C in the spine, where the crease is the spine itself.
  // A copy of the journal as it was is laid over it in layers, and the journal itself shows the next spread or note underneath:
  //   jt-left   the old left page, which stays until the turning page covers it        jt-right   the old right page (the old note), cut along the crease
  //   jt-shade  a shadow on what the leaf uncovers                                     jt-fshadow the folded part's shadow (two steps, no blur)
  //   jt-flap   the folded part: the BACK of the leaf, which is the new left page (a plain note back for a peel), shaded along the crease like a curl
  // The folded part of a page is the new state mirrored in the spine and then in the crease, which is one turn about a point. Everything is a few element.animate
  // calls on transform and clip-path (many short steps, so the fold is smooth), once, and gone when it ends.
  const TURN_MS = 900, PEEL_MS = 760;
  function turnPage(box, per, target) {
    const j = box.closest('.desk-journal'); if (!j || !j.animate) return;
    const W = j.offsetWidth, H = j.offsetHeight; if (W < 8 || H < 8) return;
    const turn = per > 1, ms = turn ? TURN_MS : PEEL_MS, S = W / 2, L = (W + H) * 2;
    j.parentNode.querySelectorAll('.j-turn, .j-peel').forEach((n) => n.remove());
    const ghost = j.cloneNode(false);
    ghost.removeAttribute('data-testid'); ghost.classList.remove('lift'); ghost.classList.add(turn ? 'j-turn' : 'j-peel'); ghost.setAttribute('aria-hidden', 'true');
    const px = (v) => v.toFixed(1) + 'px';
    const layer = (cls, css, parent) => { const d = document.createElement('div'); d.className = cls; d.style.cssText = 'position:absolute;inset:0;pointer-events:none;transform-origin:0 0;' + (css || ''); (parent || ghost).appendChild(d); return d; };
    const copyPage = (parent, scroll) => {   // (a copy of the journal's page, its lines scrolled to a spread)
      const c = j.querySelector('.page').cloneNode(true);
      c.querySelectorAll('[data-testid]').forEach((n) => n.removeAttribute('data-testid')); c.querySelectorAll('.writing').forEach((n) => n.classList.remove('writing'));
      parent.appendChild(c); return { c, scroll };
    };
    const pending = [];
    const page = (parent, scroll) => { pending.push(copyPage(parent, scroll)); };
    const poly = (mx, my, nx, ny, sign) => {   // the half plane sign * (P - M) . n >= 0, as a four cornered polygon far larger than the page
      const tx = -ny, ty = nx, ax = sign * nx, ay = sign * ny, pt = (x, y) => px(x) + ' ' + px(y);
      return `polygon(${pt(mx + tx * L, my + ty * L)}, ${pt(mx - tx * L, my - ty * L)}, ${pt(mx - tx * L + ax * L, my - ty * L + ay * L)}, ${pt(mx + tx * L + ax * L, my + ty * L + ay * L)})`;
    };
    const fold = (qx, qy) => { let dx = W - qx, dy = H - qy, d = Math.hypot(dx, dy); if (d < 1e-3) { dx = .7; dy = .7; d = 1; } return { mx: (W + qx) / 2, my: (H + qy) / 2, nx: dx / d, ny: dy / d }; };
    const mirror = (f) => `translate(${px(f.mx)}, ${px(f.my)}) rotate(${(2 * (Math.atan2(f.ny, f.nx) + Math.PI / 2)).toFixed(4)}rad) scale(1, -1) translate(${px(-f.mx)}, ${px(-f.my)})`;
    const band = (parent, w, bg) => layer('jt-strip', `inset:auto;left:0;top:0;width:${px(w)};height:${px(3 * H + 2 * W)};background:${bg};transform-origin:0 0;`, parent);
    const bandT = (g) => `translate(${px(g.mx)}, ${px(g.my)}) rotate(${Math.atan2(g.ny, g.nx).toFixed(4)}rad) translate(0, ${px(-1.5 * H - W)})`;
    const N = turn ? 40 : 28, frames = [];
    for (let i = 0; i <= N; i++) {
      const u = i / N;
      if (turn) {
        const th = Math.max(0.004, Math.PI * (0.5 - 0.5 * Math.cos(Math.PI * u)));
        frames.push(fold(S + S * Math.cos(th) - 0.25 * S * Math.sin(th) * (1 - th / Math.PI), H - 0.2 * H * Math.sin(th)));
      } else {
        const k = Math.min(1, u / 0.62), e = 0.5 - 0.5 * Math.cos(Math.PI * k);
        frames.push(fold(W - W * 0.78 * Math.max(e, 0.004), H - H * 0.74 * Math.max(e, 0.004)));
      }
    }
    const opts = { duration: ms, easing: 'linear', fill: 'both' };
    const mk = (el, e) => el.animate(frames.map((f, i) => {
      const o = { offset: i / N }; if (e.transform) o.transform = e.transform(f); if (e.clip) o.clipPath = e.clip(f); return o;
    }), opts);
    const shadeBg = 'linear-gradient(90deg, rgba(45,26,10,.34), rgba(45,26,10,.12) 35%, rgba(45,26,10,0))';
    const curlBg = 'linear-gradient(90deg, rgba(48,28,10,.38), rgba(255,255,255,.2) 14%, rgba(255,255,255,0) 36%, rgba(48,28,10,.1))';
    const oldScroll = box.scrollLeft;
    let main, extra = null;
    if (turn) {
      const xs = (f) => ({ mx: 2 * S - f.mx, my: f.my, nx: -f.nx, ny: f.ny });   // the crease as the new left page sees it (mirrored in the spine)
      const flapT = (f, dx, dy) => `translate(${px(dx || 0)}, ${px(dy || 0)}) ${mirror(f)} translate(${px(S)}, 0) scale(-1, 1) translate(${px(-S)}, 0)`;
      const flapClip = (f) => { const g = xs(f); return poly(g.mx, g.my, g.nx, g.ny, 1); };
      const left = layer('jt-left', 'clip-path:inset(-6% 50% -6% -6%);'); page(left, oldScroll);
      const sh = layer('jt-shade', 'clip-path:inset(0 0 0 50%);'); mk(band(sh, 0.2 * H, shadeBg), { transform: bandT });
      const right = layer('jt-right'); page(layer('jt-in', 'clip-path:inset(-6% -6% -6% 50%);', right), oldScroll);
      mk(right, { clip: (f) => poly(f.mx, f.my, f.nx, f.ny, -1) });
      for (const [dx, dy, a] of [[.014 * H, .02 * H, .13], [.034 * H, .05 * H, .08]]) {
        const sd = layer('jt-fshadow'); layer('jt-in', `clip-path:inset(0 50% 0 0);background:rgba(36,20,6,${a});`, sd);
        mk(sd, { transform: (f) => flapT(f, dx, dy), clip: flapClip });
      }
      const flap = layer('jt-flap'), fin = layer('jt-in', 'clip-path:inset(-6% 50% -6% -6%);', flap);
      page(fin, target);
      mk(band(fin, 0.62 * W, curlBg), { transform: (f) => bandT(xs(f)) });
      main = mk(flap, { transform: (f) => flapT(f), clip: flapClip });
    } else {
      const sh = layer('jt-shade', 'clip-path:inset(0);'); mk(band(sh, 0.5 * H, shadeBg), { transform: bandT });
      const right = layer('jt-right'); page(right, oldScroll);
      mk(right, { clip: (f) => poly(f.mx, f.my, f.nx, f.ny, -1) });
      for (const [dx, dy, a] of [[.02 * H, .03 * H, .16], [.05 * H, .08 * H, .09]]) {
        const sd = layer('jt-fshadow', 'background:rgba(60,36,6,' + a + ');');
        mk(sd, { transform: (f) => `translate(${px(dx)}, ${px(dy)}) ${mirror(f)}`, clip: (f) => poly(f.mx, f.my, f.nx, f.ny, 1) });
      }
      const flap = layer('jt-flap', 'background:linear-gradient(175deg, var(--note-b, #fbe47a), var(--note-under, #f2d865));');
      mk(band(layer('jt-in', 'clip-path:inset(0);', flap), 0.7 * W, curlBg), { transform: bandT });
      main = mk(flap, { transform: (f) => mirror(f), clip: (f) => poly(f.mx, f.my, f.nx, f.ny, 1) });
      // then the note comes away: up and off, a little turned, fading (the fold takes the first 62% of the time)
      extra = ghost.animate([{ transform: 'translate(0, 0) rotate(0deg) scale(1)', opacity: 1, offset: 0 }, { transform: 'translate(0, 0) rotate(0deg) scale(1)', opacity: 1, offset: .62 },
        { transform: `translate(${px(.1 * W)}, ${px(-.55 * H)}) rotate(9deg) scale(1.06)`, opacity: 0, offset: 1 }], opts);
    }
    j.after(ghost);
    for (const p of pending) { const gl = p.c.querySelector('.j-lines'); if (gl) gl.scrollLeft = p.scroll; }
    // the pen waits until the leaf has landed
    const land = performance.now() + ms; s.landAt = land;
    for (const w of s.writes || []) w.born = Math.max(w.born, land);
    const done = () => ghost.remove(); main.onfinish = done; main.oncancel = done; const guard = () => { if (main.playState === 'finished' || main.playState === 'idle') done(); else setTimeout(guard, 400); }; setTimeout(guard, ms + 700);
    void extra;
  }

  // the pen: the line is revealed left to right, a row at a time (a wrapped line's second row only once the first is written), at one speed throughout.
  // One animation of a clip polygon down the rows' right edges: rows already written are open to their end, the row being written up to the pen, the rest shut.
  // The rows are measured on screen, but the polygon is in the line's own pixels: the desk is scaled to fit the window (and may be zoomed), so every
  // measure is divided by that scale, or the pen stops short and the last letter is cut. Handwriting also reaches past its own box: the pen goes on a little.
  // a line written out by hand: uncovered (a clip path) left to right, a row at a time, over ms. desk.js's loop moves it on, on the frames it draws anyway
  // (an animation of its own would draw a frame of the notebook sixty times a second)
  function writeIn(l, ms) {
    if (s.reduced) return;
    const scr = l.getBoundingClientRect(), r = document.createRange(); r.selectNodeContents(l);
    const k = scr.width > 0 && l.offsetWidth > 0 ? l.offsetWidth / scr.width : 1, over = parseFloat(getComputedStyle(l).fontSize) * 0.6 || 8;
    const box = { left: scr.left, top: scr.top, width: l.offsetWidth + over, height: l.offsetHeight };
    const byTop = new Map();
    for (const q of r.getClientRects()) {
      if (q.width <= 0) continue;
      const key = Math.round(q.top - box.top), row = byTop.get(key);
      if (row) { row.l = Math.min(row.l, (q.left - box.left) * k); row.r = Math.max(row.r, (q.right - box.left) * k); }
      else byTop.set(key, { t: (q.top - box.top) * k, b: (q.bottom - box.top) * k, l: (q.left - box.left) * k, r: (q.right - box.left) * k });
    }
    const rows = [...byTop.values()].sort((a, b) => a.t - b.t);
    if (!rows.length) return;
    rows[rows.length - 1].b = Math.max(rows[rows.length - 1].b, box.height);
    const poly = (k, x) => {
      const pts = [[-over, 0]];
      rows.forEach((row, j) => { const e = j < k ? box.width : j === k ? x : 0; pts.push([e, j === 0 ? 0 : row.t], [e, j === rows.length - 1 ? box.height + 2 : rows[j + 1].t]); });
      pts.push([-over, box.height + 2]);
      return `polygon(${pts.map(([x, y]) => `${x.toFixed(1)}px ${y.toFixed(1)}px`).join(', ')})`;
    };
    const total = rows.reduce((n, row) => n + Math.max(1, row.r), 0);
    s.writes = (s.writes || []).filter((w) => w.l !== l);
    s.writes.push({ l, rows, total, over, poly, born: Math.max(performance.now(), s.landAt || 0), ms });
    l.style.clipPath = poly(0, 0);
  }
  function writing(now) {
    if (!s.writes || !s.writes.length) return;
    s.writes = s.writes.filter((w) => {
      const p = s.hold != null ? s.hold : Math.max(0, (now - w.born) / w.ms);
      if (p >= 1 || !w.l.isConnected) { w.l.style.clipPath = ''; return false; }
      let d = p * w.total, j = 0;
      while (j < w.rows.length - 1 && d > Math.max(1, w.rows[j].r)) { d -= Math.max(1, w.rows[j].r); j++; }
      w.l.style.clipPath = w.poly(j, Math.min(1, d / Math.max(1, w.rows[j].r)) * (w.rows[j].r + w.over));
      return true;
    });
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
        viz: el.querySelectorAll('[data-viz]').length ? [...el.querySelectorAll('[data-viz]')] : [el], steam: [...el.querySelectorAll('.desk-steam i')], rings: [],
        motes: Array.from({ length: 60 }, () => ({ x: Math.random(), y: Math.random(), r: 0.6 + Math.random() * 1.6, vx: (Math.random() - 0.5) * 0.0001, vy: -0.00003 - Math.random() * 0.0001, tw: Math.random() * 6.28 })) };
      // reduced motion: one still frame (no analyser, no loop)
      if (reduced) { paint(CALM, performance.now()); dust(); } else s.raf = requestAnimationFrame(tick);
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
    /** For tests: holds the line being written at a fraction of its stroke (null: ends it at once, the line whole). */
    holdWrite(f) {
      if (!s) return;
      if (f == null) { for (const w of s.writes || []) w.l.style.clipPath = ''; s.writes = []; s.hold = null; return; }
      s.hold = f; writing(performance.now());
    },
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
