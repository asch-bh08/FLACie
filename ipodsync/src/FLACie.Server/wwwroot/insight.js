// The graphs on the Info tab. The song is fetched once more (the same /stream the player uses), decoded in the browser and measured, so the
// graphs show the whole file straight away and never touch the audio that is playing. One analysis feeds three graphs:
//   spec   - a spectrogram like Spek: time across, pitch up the side (0 Hz at the bottom, the top is half the sample rate), colour = loudness
//   wave   - the waveform with dB lines (how loud, and where it is squashed or clipped)
//   stereo - left and right drawn one above the other
window.insight = (() => {
  const cache = new Map();
  let view = null, raf = 0, frame = null, frameKey = "";

  function makeFft(n) {
    const levels = Math.round(Math.log2(n)), rev = new Uint32Array(n), cos = new Float32Array(n / 2), sin = new Float32Array(n / 2);
    for (let i = 0; i < n; i++) { let r = 0; for (let b = 0; b < levels; b++) r |= ((i >> b) & 1) << (levels - 1 - b); rev[i] = r; }
    for (let i = 0; i < n / 2; i++) { cos[i] = Math.cos(2 * Math.PI * i / n); sin[i] = Math.sin(2 * Math.PI * i / n); }
    return (re, im) => {
      for (let i = 0; i < n; i++) { const j = rev[i]; if (j > i) { let t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; } }
      for (let size = 2; size <= n; size <<= 1) {
        const half = size >> 1, step = n / size;
        for (let i = 0; i < n; i += size) for (let j = i, k = 0; j < i + half; j++, k += step) {
          const l = j + half, tr = re[l] * cos[k] + im[l] * sin[k], ti = im[l] * cos[k] - re[l] * sin[k];
          re[l] = re[j] - tr; im[l] = im[j] - ti; re[j] += tr; im[j] += ti;
        }
      }
    };
  }

  // black, deep purple, magenta, orange, pale yellow: easy to read on a dark page
  const lut = (() => {
    const stops = [[0, 0, 0, 4], [.18, 38, 12, 84], [.4, 104, 22, 112], [.62, 190, 52, 82], [.82, 250, 140, 10], [1, 252, 255, 170]], out = new Uint8ClampedArray(256 * 3);
    for (let i = 0; i < 256; i++) {
      const t = i / 255; let k = 1; while (k < stops.length - 1 && stops[k][0] < t) k++;
      const a = stops[k - 1], b = stops[k], u = (t - a[0]) / (b[0] - a[0]);
      for (let c = 0; c < 3; c++) out[i * 3 + c] = a[c + 1] + (b[c + 1] - a[c + 1]) * u;
    }
    return out;
  })();

  const db = (x) => 20 * Math.log10(Math.max(x, 1e-9));

  async function analyse(url, rate) {
    const res = await fetch(url);
    if (!res.ok) throw new Error("The song couldn't be read");
    const buf = await res.arrayBuffer();
    const ab = await new OfflineAudioContext(2, 2, Math.min(Math.max(rate || 44100, 8000), 192000)).decodeAudioData(buf);
    const L = ab.getChannelData(0), R = ab.numberOfChannels > 1 ? ab.getChannelData(1) : L, n = L.length, sr = ab.sampleRate;

    // waveform and loudness
    const B = 900, size = Math.ceil(n / B), wl = new Float32Array(B * 2), wr = new Float32Array(B * 2), rms = new Float32Array(B);
    let peak = 0, sq = 0, clipped = 0, sLR = 0, sLL = 0, sRR = 0, sMid = 0, sSide = 0;
    for (let b = 0; b < B; b++) {
      const s = b * size, e = Math.min(n, s + size); let a0 = 0, a1 = 0, c0 = 0, c1 = 0, bs = 0;
      for (let i = s; i < e; i++) {
        const l = L[i], r = R[i];
        if (l < a0) a0 = l; if (l > a1) a1 = l; if (r < c0) c0 = r; if (r > c1) c1 = r;
        const m = (l + r) / 2, d = (l - r) / 2; bs += m * m; sMid += m * m; sSide += d * d; sLR += l * r; sLL += l * l; sRR += r * r;
        if (l >= 0.9999 || l <= -0.9999) clipped++;
      }
      wl[b * 2] = a0; wl[b * 2 + 1] = a1; wr[b * 2] = c0; wr[b * 2 + 1] = c1; rms[b] = Math.sqrt(bs / Math.max(1, e - s));
      peak = Math.max(peak, -a0, a1, -c0, c1); sq += bs;
    }
    const sorted = Array.from(rms).filter(v => v > 1e-5).sort((a, b) => a - b);
    const pick = (p) => sorted.length ? db(sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * p))]) : -120;

    // spectrogram
    const N = 2048, half = N / 2, C = Math.max(60, Math.min(560, Math.floor(n / N))), RW = 360, fft = makeFft(N);
    const win = new Float32Array(N); for (let i = 0; i < N; i++) win[i] = 0.5 - 0.5 * Math.cos(2 * Math.PI * i / (N - 1));
    const re = new Float32Array(N), im = new Float32Array(N), tmp = new Float32Array(half), spec = new Uint8Array(C * RW), avg = new Float64Array(RW);
    for (let c = 0; c < C; c++) {
      const start = Math.max(0, Math.min(n - N, Math.floor(c * (n - N) / Math.max(1, C - 1))));
      for (let i = 0; i < N; i++) { re[i] = ((L[start + i] + R[start + i]) / 2) * win[i]; im[i] = 0; }
      fft(re, im);
      for (let k = 0; k < half; k++) tmp[k] = db(Math.hypot(re[k], im[k]) / (N / 4));
      for (let r = 0; r < RW; r++) {
        const k0 = Math.floor(r * half / RW), k1 = Math.max(k0 + 1, Math.floor((r + 1) * half / RW)); let m = -200;
        for (let k = k0; k < k1; k++) if (tmp[k] > m) m = tmp[k];
        avg[r] += m; spec[c * RW + r] = Math.max(0, Math.min(255, Math.round((m + 120) / 110 * 255)));
      }
    }
    // the highest pitch that carries real sound, averaged over the song
    let top = 0; for (let r = RW - 1; r >= 0; r--) if (avg[r] / C > -100) { top = r + 1; break; }
    const nyq = sr / 2;
    return {
      sr, nyq, n, dur: n / sr, C, RW, spec, wl, wr, rms, B,
      stats: {
        sampleRate: sr, durationSec: n / sr, stereo: ab.numberOfChannels > 1,
        peakDb: db(peak), rmsDb: db(Math.sqrt(sq / Math.max(1, n))), dynamicDb: pick(0.95) - pick(0.1), clipped,
        correlation: ab.numberOfChannels > 1 ? sLR / Math.sqrt(Math.max(1e-12, sLL * sRR)) : 1,
        widthPct: ab.numberOfChannels > 1 ? Math.round(100 * Math.sqrt(sSide) / Math.max(1e-9, Math.sqrt(sMid) + Math.sqrt(sSide))) : 0,
        contentKhz: top / RW * nyq / 1000, nyquistKhz: nyq / 1000,
      },
    };
  }

  // ---- drawing ----
  const colors = () => { const css = getComputedStyle(document.documentElement); return { accent: css.getPropertyValue("--accent").trim() || "#ff4d8d", dim: css.getPropertyValue("--faint").trim() || "#8a8a94", ink: css.getPropertyValue("--ink").trim() || "#fff" }; };

  function timeLabels(g, a, x0, x1, y, dpr, dim) {
    const step = [10, 15, 30, 60, 120].find(s => a.dur / s <= 9) || 120;
    g.fillStyle = dim; g.textAlign = "center"; g.textBaseline = "alphabetic";
    for (let t = 0; t <= a.dur - step * 0.3; t += step) g.fillText(`${Math.floor(t / 60)}:${String(Math.floor(t % 60)).padStart(2, "0")}`, x0 + t / a.dur * (x1 - x0), y);
  }

  function render(a, mode, w, h, dpr) {
    const cv = document.createElement("canvas"); cv.width = w; cv.height = h; const g = cv.getContext("2d"), col = colors();
    const ml = 48 * dpr, mr = 6 * dpr, mt = 6 * dpr, mb = 20 * dpr, x0 = ml, x1 = w - mr, y0 = mt, y1 = h - mb;
    g.font = `${11 * dpr}px system-ui, sans-serif`; g.lineWidth = dpr;
    if (mode === "spec") {
      // one pixel per output cell, taking the loudest of the measurements it covers (so shrinking never makes stripes)
      const W = Math.max(1, Math.round(x1 - x0)), H = Math.max(1, Math.round(y1 - y0));
      const img = document.createElement("canvas"); img.width = W; img.height = H; const ig = img.getContext("2d"), d = ig.createImageData(W, H);
      for (let py = 0; py < H; py++) {
        const r0 = Math.floor((H - 1 - py) * a.RW / H), r1 = Math.max(r0 + 1, Math.floor((H - py) * a.RW / H));
        for (let px = 0; px < W; px++) {
          const c0 = Math.floor(px * a.C / W), c1 = Math.max(c0 + 1, Math.floor((px + 1) * a.C / W)); let v = 0;
          for (let c = c0; c < c1 && c < a.C; c++) for (let r = r0; r < r1 && r < a.RW; r++) { const s = a.spec[c * a.RW + r]; if (s > v) v = s; }
          const o = (py * W + px) * 4; d.data[o] = lut[v * 3]; d.data[o + 1] = lut[v * 3 + 1]; d.data[o + 2] = lut[v * 3 + 2]; d.data[o + 3] = 255;
        }
      }
      ig.putImageData(d, 0, 0); g.drawImage(img, x0, y0);
      const step = a.nyq > 30000 ? 8000 : 4000;
      g.textAlign = "right"; g.textBaseline = "middle";
      for (let f = 0; f <= a.nyq; f += step) {
        const y = y1 - f / a.nyq * (y1 - y0), key = f === 16000 || f === 20000;
        g.fillStyle = key ? col.ink : col.dim; g.fillText(f === 0 ? "0" : (f / 1000) + " kHz", x0 - 6 * dpr, Math.min(y1 - 4 * dpr, Math.max(y0 + 6 * dpr, y)));
        if (f > 0) { g.strokeStyle = key ? "rgba(255,255,255,.55)" : "rgba(255,255,255,.12)"; g.setLineDash(key ? [5 * dpr, 4 * dpr] : []); g.beginPath(); g.moveTo(x0, y); g.lineTo(x1, y); g.stroke(); }
      }
      g.setLineDash([]);
      const cut = a.stats.contentKhz * 1000;
      if (cut > 0 && cut < a.nyq * 0.97) { const y = y1 - cut / a.nyq * (y1 - y0); g.strokeStyle = col.accent; g.lineWidth = 2 * dpr; g.beginPath(); g.moveTo(x0, y); g.lineTo(x1, y); g.stroke(); g.lineWidth = dpr; g.fillStyle = col.accent; g.textAlign = "right"; g.textBaseline = "bottom"; g.fillText(`sound ends near ${(cut / 1000).toFixed(1)} kHz`, x1 - 4 * dpr, y - 3 * dpr); }
      timeLabels(g, a, x0, x1, h - 5 * dpr, dpr, col.dim);
    } else {
      const lanes = mode === "stereo" ? [[a.wl, "L", y0, (y0 + y1) / 2 - 3 * dpr], [a.wr, "R", (y0 + y1) / 2 + 3 * dpr, y1]] : [[null, "", y0, y1]];
      for (const [wv, label, ya, yb] of lanes) {
        const mid = (ya + yb) / 2, amp = (yb - ya) / 2 - 2 * dpr;
        g.strokeStyle = "rgba(255,255,255,.1)"; g.textAlign = "right"; g.textBaseline = "middle"; g.fillStyle = col.dim;
        if (mode === "wave") for (const d of [0, -6, -12, -24]) { const k = Math.pow(10, d / 20); for (const s of [-1, 1]) { g.beginPath(); g.moveTo(x0, mid - s * k * amp); g.lineTo(x1, mid - s * k * amp); g.stroke(); } g.fillText(d === 0 ? "0 dB" : d + "", x0 - 6 * dpr, mid - k * amp + (d === 0 ? 5 * dpr : 0)); }
        else { g.beginPath(); g.moveTo(x0, mid); g.lineTo(x1, mid); g.stroke(); g.fillStyle = col.ink; g.fillText(label, x0 - 10 * dpr, mid); }
        const cw = x1 - x0, grad = g.createLinearGradient(0, ya, 0, yb); grad.addColorStop(0, col.accent); grad.addColorStop(.5, "rgba(255,255,255,.85)"); grad.addColorStop(1, col.accent);
        for (let px = 0; px < cw; px++) {
          const b = Math.min(a.B - 1, Math.floor(px / cw * a.B));
          let lo, hi;
          if (wv) { lo = wv[b * 2]; hi = wv[b * 2 + 1]; } else { lo = Math.min(a.wl[b * 2], a.wr[b * 2]); hi = Math.max(a.wl[b * 2 + 1], a.wr[b * 2 + 1]); }
          g.fillStyle = col.accent; g.globalAlpha = .55; g.fillRect(x0 + px, mid - hi * amp, 1, Math.max(1, (hi - lo) * amp)); g.globalAlpha = 1;
          const r = a.rms[b] * (wv ? 1 : 1); g.fillStyle = "rgba(255,255,255,.9)"; g.fillRect(x0 + px, mid - Math.min(hi, r) * amp, 1, Math.max(1, (Math.min(hi, r) + Math.min(-lo, r)) * amp));
        }
      }
      timeLabels(g, a, x0, x1, h - 5 * dpr, dpr, col.dim);
    }
    return { cv, x0, x1, y0, y1 };
  }

  function tick() {
    if (!view) return;
    raf = requestAnimationFrame(tick);
    const a = cache.get(view.key), cv = document.getElementById(view.id); if (!a || !cv) return;
    const dpr = window.devicePixelRatio || 1, w = Math.round(cv.clientWidth * dpr), h = Math.round(cv.clientHeight * dpr); if (!w || !h) return;
    if (cv.width !== w || cv.height !== h) { cv.width = w; cv.height = h; frameKey = ""; }
    const fk = `${view.key}|${view.mode}|${w}x${h}`;
    if (fk !== frameKey) { frame = render(a, view.mode, w, h, dpr); frameKey = fk; if (!cv.dataset.seek) { cv.dataset.seek = "1"; cv.addEventListener("click", seekClick); } }
    const g = cv.getContext("2d"); g.clearRect(0, 0, w, h); g.drawImage(frame.cv, 0, 0);
    const au = window.flacieAudio, dur = (au && au.duration) || a.dur, t = au ? au.currentTime : 0;
    if (dur > 0) {
      const x = frame.x0 + Math.min(1, t / dur) * (frame.x1 - frame.x0);
      if (view.mode !== "spec") { g.fillStyle = "rgba(0,0,0,.42)"; g.fillRect(x, frame.y0, frame.x1 - x, frame.y1 - frame.y0); }
      g.strokeStyle = "#fff"; g.lineWidth = 2 * dpr; g.beginPath(); g.moveTo(x, frame.y0); g.lineTo(x, frame.y1); g.stroke();
    }
  }

  function seekClick(e) {
    const cv = e.currentTarget, au = window.flacieAudio; if (!au || !frame || !au.duration) return;
    const dpr = cv.width / cv.clientWidth, x = (e.offsetX * dpr - frame.x0) / (frame.x1 - frame.x0);
    if (x >= 0 && x <= 1) au.currentTime = x * au.duration;
  }

  return {
    // returns the numbers about the song; throws a message the page can show
    async load(url, rate, key) {
      if (!cache.has(key)) {
        const a = await analyse(url, rate);
        if (cache.size >= 3) cache.delete(cache.keys().next().value);
        cache.set(key, a);
      }
      return cache.get(key).stats;
    },
    show(id, key, mode) { view = { id, key, mode }; frameKey = ""; if (!raf) raf = requestAnimationFrame(tick); },
    stop() { view = null; if (raf) cancelAnimationFrame(raf); raf = 0; frame = null; frameKey = ""; },
  };
})();
