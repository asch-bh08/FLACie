// The iPod desk's monitor (BackgroundVisualizerCanvas): bars, a waveform or particles drawn from the player's own analyser (flacie.vizFrame), or a slow idle
// picture when there is nothing to read. One loop at a time; it sleeps while the tab is hidden and puts the bass on the scene as --bass (the woofers move with it).
window.desk = (() => {
  const TAU = Math.PI * 2;
  let loop = null;   // { canvas, g, scene, style, title, raf, ro, last, peaks, parts, t }

  const frame = () => { try { return window.flacie && window.flacie.vizFrame ? window.flacie.vizFrame() : null; } catch { return null; } };
  const size = (s) => {
    const r = s.canvas.getBoundingClientRect(), dpr = Math.min(2, window.devicePixelRatio || 1);
    const w = Math.max(1, Math.round(r.width * dpr)), h = Math.max(1, Math.round(r.height * dpr));
    if (s.canvas.width !== w || s.canvas.height !== h) { s.canvas.width = w; s.canvas.height = h; }
  };
  // 48 bars on a log scale from ~40 Hz up: the bins each bar averages, worked out once per spectrum size
  const bands = (n, count = 48) => {
    const out = [], lo = Math.log(2), hi = Math.log(n * 0.85);
    for (let i = 0; i < count; i++) {
      const a = Math.floor(Math.exp(lo + (hi - lo) * i / count)), b = Math.max(a + 1, Math.floor(Math.exp(lo + (hi - lo) * (i + 1) / count)));
      out.push([a, b]);
    }
    return out;
  };
  const level = (freq, [a, b]) => { let s = 0; for (let i = a; i < b; i++) s += freq[i]; return s / (b - a) / 255; };

  function idle(s, g, w, h) {
    const k = 0.5 + 0.5 * Math.sin(s.t / 1.6);
    const grd = g.createRadialGradient(w / 2, h * 0.55, 0, w / 2, h * 0.55, w * (0.45 + 0.08 * k));
    grd.addColorStop(0, `rgba(122, 92, 255, ${0.35 + 0.2 * k})`); grd.addColorStop(0.6, 'rgba(255, 94, 135, 0.12)'); grd.addColorStop(1, 'rgba(0, 0, 0, 0)');
    g.fillStyle = grd; g.fillRect(0, 0, w, h);
    g.fillStyle = `rgba(255, 255, 255, ${0.55 + 0.25 * k})`; g.textAlign = 'center'; g.textBaseline = 'middle';
    g.font = `600 ${Math.round(h * 0.09)}px "Segoe UI", system-ui, sans-serif`;
    g.fillText(s.title || 'FLACie', w / 2, h / 2, w * 0.86);
  }

  function bars(s, g, w, h, f) {
    if (!s.bands || s.bands.n !== f.freq.length) { s.bands = bands(f.freq.length); s.bands.n = f.freq.length; s.peaks = new Float32Array(s.bands.length); }
    const n = s.bands.length, gap = w * 0.004, bw = (w * 0.9 - gap * (n - 1)) / n, x0 = w * 0.05, base = h * 0.9;
    const grd = g.createLinearGradient(0, base, 0, h * 0.1);
    grd.addColorStop(0, '#5b7cff'); grd.addColorStop(0.55, '#c25bff'); grd.addColorStop(1, '#ff6b9a');
    for (let i = 0; i < n; i++) {
      const v = level(f.freq, s.bands[i]), bh = Math.max(h * 0.01, v * h * 0.78), x = x0 + i * (bw + gap);
      g.fillStyle = grd; g.beginPath(); g.roundRect(x, base - bh, bw, bh, [bw / 3, bw / 3, 0, 0]); g.fill();
      s.peaks[i] = Math.max(v, s.peaks[i] - 0.012);
      g.fillStyle = 'rgba(255, 255, 255, 0.85)'; g.fillRect(x, base - s.peaks[i] * h * 0.78 - h * 0.012, bw, h * 0.008);
    }
    g.globalAlpha = 0.18; g.scale(1, -1); g.drawImage(s.canvas, 0, 0, w, base, 0, -base - (h - base) * 2.2, w, base * 0.25); g.setTransform(1, 0, 0, 1, 0, 0); g.globalAlpha = 1;
  }

  function wave(s, g, w, h, f) {
    const d = f.wave, n = d.length, step = Math.max(1, Math.floor(n / 512));
    g.lineWidth = Math.max(2, h * 0.008); g.lineJoin = 'round'; g.shadowBlur = h * 0.05; g.shadowColor = '#8f7bff'; g.strokeStyle = '#d8ceff';
    g.beginPath();
    let prev = 0.5;
    for (let i = 0, j = 0; i < n; i += step, j++) {
      const v = prev * 0.6 + (d[i] / 255) * 0.4; prev = v;   // a little smoothing so the line reads as a line, not noise
      const x = (i / (n - 1)) * w, y = h / 2 + (v - 0.5) * h * 1.6;
      if (j === 0) g.moveTo(x, y); else g.lineTo(x, y);
    }
    g.stroke(); g.shadowBlur = 0;
  }

  function particles(s, g, w, h, f) {
    if (!s.parts) s.parts = Array.from({ length: 160 }, () => ({ a: Math.random() * TAU, r: Math.random(), v: 0.2 + Math.random() * 0.8, band: Math.floor(Math.random() * 8) }));
    const bins = f.freq.length, e = [];
    for (let b = 0; b < 8; b++) e.push(level(f.freq, [Math.floor(bins * b / 64) + 1, Math.floor(bins * (b + 1) / 64) + 2]));
    const cx = w / 2, cy = h / 2, R = Math.hypot(w, h) / 2;
    for (const p of s.parts) {
      const en = e[p.band];
      p.r += (0.002 + en * 0.02) * p.v;
      if (p.r > 1) { p.r = 0; p.a = Math.random() * TAU; }
      const x = cx + Math.cos(p.a) * p.r * R, y = cy + Math.sin(p.a) * p.r * R * 0.7;
      const rad = (1 + en * 5) * (w / 640) * (0.6 + p.r);
      g.fillStyle = `hsla(${250 + p.band * 14}, 90%, ${60 + en * 25}%, ${0.25 + en * 0.7})`;
      g.beginPath(); g.arc(x, y, rad, 0, TAU); g.fill();
    }
  }

  const views = { bars, wave, particles };
  function tick(now) {
    const s = loop; if (!s) return;
    s.raf = requestAnimationFrame(tick);
    if (document.hidden || now - s.last < 15) return;   // at most ~60 frames a second, none while the tab is away
    s.t += (now - (s.last || now)) / 1000; s.last = now;
    size(s);
    const g = s.g, w = s.canvas.width, h = s.canvas.height;
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.fillStyle = s.style === 'particles' ? 'rgba(6, 6, 12, 0.32)' : '#06060c'; g.fillRect(0, 0, w, h);   // particles leave short trails
    const f = frame();
    let bass = 0;
    if (f && f.playing && f.freq && f.freq.length) {
      for (let i = 1; i <= 8; i++) bass += f.freq[i]; bass /= 8 * 255;
      (views[s.style] || bars)(s, g, w, h, f);
    } else idle(s, g, w, h);
    if (s.scene) s.scene.style.setProperty('--bass', bass.toFixed(2));
  }

  return {
    vizStart(canvas, style, scene, title) {
      this.vizStop(true);
      if (!canvas || !canvas.getContext) return;
      const s = loop = { canvas, g: canvas.getContext('2d'), scene: scene || null, style: views[style] ? style : 'bars', title: title || '', raf: 0, last: 0, t: 0 };
      s.ro = new ResizeObserver(() => size(s)); s.ro.observe(canvas);
      size(s); s.raf = requestAnimationFrame(tick);
    },
    vizStyle(style) { if (loop && views[style]) { loop.style = style; loop.parts = null; } },
    vizTitle(title) { if (loop) loop.title = title || ''; },
    // keep: true when another loop starts straight away (the analyser stays open for it)
    vizStop(keep) {
      const s = loop; if (!s) return;
      cancelAnimationFrame(s.raf); s.ro.disconnect(); loop = null;
      if (s.scene) s.scene.style.setProperty('--bass', '0');
      if (!keep) { try { window.flacie && window.flacie.vizRelease && window.flacie.vizRelease(); } catch { } }
    },
    loops() { return loop ? 1 : 0; },
  };
})();
