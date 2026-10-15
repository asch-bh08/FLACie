// iPod mode, Look › Desk: the moving parts of the desk. One loop reads the player's analyser (flacie.vizFrame) and turns it into the earbuds' sound:
// arcs from each bud (bass, mids, highs), a ring on every beat, current running down the cable, a small twitch of the buds, and CSS variables
// (--low --mid --high --beat) the scene's light leak breathes with. Also the dust in the sun, the discs (the release's own picture, or a print from the cover)
// and the viewer's own date. Without an analyser (iOS, nothing played yet) it keeps a calm fixed pulse and no beats. It sleeps while the tab is hidden;
// with reduced motion it paints one still frame and no loop at all. Only what changed is written to the page.
window.desk = (() => {
  const NS = 'http://www.w3.org/2000/svg';
  let s = null;   // the running scene: { el, reduced, raf, last, lastDust, avg, lastBeat, beat, beats, motes, ... }
  const CALM = { low: 0.2, mid: 0.15, high: 0.1, live: false };

  const mean = (f, a, b) => { let t = 0; for (let i = a; i <= b; i++) t += f[i]; return t / (b - a + 1) / 255; };
  function bands() {
    let f = null;
    try { f = window.flacie && window.flacie.vizFrame ? window.flacie.vizFrame() : null; } catch { f = null; }
    if (!f || !f.playing || !f.freq || f.freq.length < 161) return CALM;
    return { low: mean(f.freq, 1, 8), mid: mean(f.freq, 9, 40), high: mean(f.freq, 41, 160), live: true };
  }

  function arcPath(j, lv) {
    const r = 30 + j * 11 + lv * 6, a = (0.55 + j * 0.08) * (0.6 + 0.4 * lv);   // the arcs open out of the shell, away from the stem
    const x = (Math.sin(a) * r).toFixed(1), y = (-Math.cos(a) * r).toFixed(1);
    return `M ${-x} ${y} A ${r.toFixed(1)} ${r.toFixed(1)} 0 0 1 ${x} ${y}`;
  }
  // writes only what changed (the same value written again still costs the page a style or attribute change)
  const attr = (el, name, v) => { const k = '_' + name; if (el[k] !== v) { el[k] = v; el.setAttribute(name, v); } };
  const prop = (el, name, v) => { const m = el._props || (el._props = {}); if (m[name] !== v) { m[name] = v; el.style.setProperty(name, v); } };

  function paint(b, now) {
    const el = s.el;
    prop(el, '--low', b.low.toFixed(2)); prop(el, '--mid', b.mid.toFixed(2)); prop(el, '--high', b.high.toFixed(2)); prop(el, '--beat', s.beat.toFixed(2));
    // arcs per bud: bass, mids, highs
    const lv = [b.low, b.mid, b.high];
    for (const p of s.arcs) {
      const j = +p.dataset.arc, v = s.reduced ? 0.25 : lv[j];
      attr(p, 'd', arcPath(j, v)); attr(p, 'opacity', (0.12 + 0.75 * v * (1 - j * 0.15)).toFixed(2));
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
      const k = s.beat < 0.005 ? 0 : s.beat, x = k ? (Math.sin(t * 61 + i) * k * 0.8).toFixed(2) : '0', y = k ? (Math.cos(t * 53 + i) * k * 0.8).toFixed(2) : '0';
      const shell = g.firstElementChild, v = k ? `scale(${(1 + k * 0.05).toFixed(3)}) translate(${x}px, ${y}px)` : '';
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
    const b = bands();
    // beats: the bass jumping clearly above its recent level, at most one every 280 ms
    s.avg += (b.low - s.avg) * Math.min(1, dt / 0.5);
    if (b.live && b.low - s.avg > 0.12 && now - s.lastBeat > 280) { s.lastBeat = now; s.beat = 1; s.beats++; ripple(); }
    s.beat *= Math.pow(0.02, dt);   // the beat fades in about a second
    paint(b, now);
  }

  function ripple() {
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
        rippleBox: el.querySelector('[data-ripples]'), dust: el.querySelector('canvas[data-dust]'),
        motes: Array.from({ length: 60 }, () => ({ x: Math.random(), y: Math.random(), r: 0.6 + Math.random() * 1.6, vx: (Math.random() - 0.5) * 0.0001, vy: -0.00003 - Math.random() * 0.0001, tw: Math.random() * 6.28 })) };
      // reduced motion: one still frame (no analyser, no loop)
      if (reduced) paint(CALM, performance.now()); else s.raf = requestAnimationFrame(tick);
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
      cancelAnimationFrame(s.raf); if (s.ro) s.ro.disconnect();
      if (s.rippleBox) s.rippleBox.replaceChildren();
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
