// The audio element for FLACie Web. The queue lives on the server (PlayerState); this plays one song at a time and
// reports time, state and the end of each song back. Media Session gives lock-screen and headphone controls.
window.flacie = (() => {
  const audio = new Audio();
  try { const v = parseFloat(localStorage.getItem("flacie.volume")); if (v >= 0 && v <= 1) audio.volume = v; } catch { }
  audio.preload = "auto";
  let dotnet = null, lastSent = 0;
  const send = (m, ...a) => dotnet && dotnet.invokeMethodAsync(m, ...a).catch(() => {});
  audio.addEventListener("timeupdate", () => {
    const now = performance.now();
    if (now - lastSent > 500) { lastSent = now; send("OnTime", audio.currentTime, audio.duration || 0); }
  });
  audio.addEventListener("play", () => send("OnState", true));
  audio.addEventListener("pause", () => send("OnState", false));
  audio.addEventListener("ended", () => send("OnEnded"));
  // a dropped connection mid-song: pick up again at the same second. A song the browser can't decode (e.g. WMA) fails
  // the same way every time, so after three tries it is skipped instead of retried forever.
  let failures = 0, failedSrc = "";
  audio.addEventListener("playing", () => { failures = 0; });
  audio.addEventListener("error", () => {
    if (!audio.src) return;
    const at = audio.currentTime, src = audio.src;
    failures = src === failedSrc ? failures + 1 : 1; failedSrc = src;
    if (failures > 3) { failures = 0; send("OnEnded"); return; }
    setTimeout(() => { if (audio.src !== src) return; audio.src = src; audio.currentTime = at; audio.play().catch(() => {}); }, 1500);
  });
  if ("mediaSession" in navigator) {
    navigator.mediaSession.setActionHandler("play", () => audio.play());
    navigator.mediaSession.setActionHandler("pause", () => audio.pause());
    navigator.mediaSession.setActionHandler("nexttrack", () => send("OnNext"));
    navigator.mediaSession.setActionHandler("previoustrack", () => send("OnPrev"));
    navigator.mediaSession.setActionHandler("seekto", e => { audio.currentTime = e.seekTime; });
  }
  // synced lyrics: the line being sung is found from the audio clock every frame, so the highlight lands with the voice
  let lyr = null;
  const lyricTick = () => {
    if (lyr) {
      const ms = audio.currentTime * 1000 + 150; // a hair early: the eye needs the line before it is sung
      let lo = 0, hi = lyr.times.length - 1, c = -1;
      while (lo <= hi) { const m = (lo + hi) >> 1; if (lyr.times[m] <= ms) { c = m; lo = m + 1; } else hi = m - 1; }
      if (c !== lyr.cur) {
        const items = document.querySelectorAll("#lyrics li");
        if (items.length === lyr.times.length) {
          items.forEach((li, i) => { li.classList.toggle("cur", i === c); li.classList.toggle("past", i < c); });
          lyr.cur = c;
          const el = items[c], panel = document.querySelector(".np-panel");
          if (el && panel) panel.scrollTo({ top: el.offsetTop - panel.clientHeight / 2 + el.clientHeight / 2, behavior: "smooth" });
        }
      }
    }
    requestAnimationFrame(lyricTick);
  };
  requestAnimationFrame(lyricTick);
  // Live views for the Info tab: spectrum, bars, spectrogram, loudness, stereo. The audio runs through analysers from the first song on
  // (set up before any sound plays, so switching a view on or off never interrupts it). Not done on iOS, where routing through Web Audio
  // can stop playback in the background.
  let actx = null, an = null, anL = null, anR = null, outGain = null, vizOn = false, vizStyle = "curve", vs = {}, vh = null, vp = null, cp = null;
  const ios = /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  const ensureGraph = () => {
    if (an || ios || actx) return;
    try {
      actx = new (window.AudioContext || window.webkitAudioContext)();
      const src = actx.createMediaElementSource(audio);
      // the sound's own path first, so it can never be lost: the volume is applied here, and the graphs read what comes out of it
      outGain = actx.createGain(); outGain.gain.value = audio.volume; audio.volume = 1;
      src.connect(outGain); outGain.connect(actx.destination);
      try {
        const sink = actx.createGain(); sink.gain.value = 0; sink.connect(actx.destination); // keeps the analysers running
        const a = actx.createAnalyser(); a.fftSize = 2048; a.smoothingTimeConstant = 0.7; outGain.connect(a); a.connect(sink);
        // left and right on their own; the gain node first turns a mono file into two equal channels, so a mono song reads the same on both sides
        const up = actx.createGain(); up.channelCount = 2; up.channelCountMode = "explicit"; up.channelInterpretation = "speakers";
        const split = actx.createChannelSplitter(2);
        const l = actx.createAnalyser(), r = actx.createAnalyser(); l.fftSize = r.fftSize = 2048; l.smoothingTimeConstant = r.smoothingTimeConstant = 0;
        outGain.connect(up); up.connect(split); split.connect(l, 0); split.connect(r, 1); l.connect(sink); r.connect(sink);
        an = a; anL = l; anR = r;
      } catch { an = null; }
    } catch { an = null; }
  };
  audio.addEventListener("play", () => { if (actx && actx.state === "suspended") actx.resume().catch(() => { }); });

  const db = (x) => 20 * Math.log10(Math.max(x, 1e-6));
  const freqData = () => { const d = new Uint8Array(an.frequencyBinCount); an.getByteFrequencyData(d); return d; };
  const fill = (g, x, y, w, h, r, color, alpha) => { g.globalAlpha = alpha; g.fillStyle = color; g.beginPath(); g.roundRect(x, y, w, h, r); g.fill(); g.globalAlpha = 1; };
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
  const labelFont = (f) => { f.g.font = `${11 * f.dpr}px system-ui, sans-serif`; f.g.textBaseline = "alphabetic"; f.g.fillStyle = f.c.dim; };

  // ---- spectrum: how loud each pitch is, 0 Hz on the left to 22 kHz on the right, with a slowly falling peak line ----
  const drawCurve = (f) => {
    const { g, w, h, dpr, c } = f, data = freqData(), nyq = actx.sampleRate / 2, bins = data.length;
    const maxHz = Math.min(nyq, 22050), use = Math.floor(bins * maxHz / nyq), bottom = h - 18 * dpr, top = 6 * dpr, span = bottom - top;
    if (!cp || cp.length !== use) cp = new Float32Array(use);
    g.clearRect(0, 0, w, h); labelFont(f); g.lineWidth = dpr;
    for (let k = 0; k <= maxHz; k += 5000) {
      const x = Math.min(w - 1, k / maxHz * w);
      g.strokeStyle = "rgba(255,255,255,.08)"; g.beginPath(); g.moveTo(x, top); g.lineTo(x, bottom); g.stroke();
      g.textAlign = k === 0 ? "left" : "center"; g.fillText(k === 0 ? "0" : (k / 1000) + " kHz", k === 0 ? 2 : Math.min(x, w - 22 * dpr), h - 3 * dpr);
    }
    if (maxHz > 16000) { // where MP3s are usually cut off
      const x = 16000 / maxHz * w; g.setLineDash([4 * dpr, 4 * dpr]); g.strokeStyle = "rgba(255,255,255,.3)"; g.beginPath(); g.moveTo(x, top); g.lineTo(x, bottom); g.stroke(); g.setLineDash([]);
      g.textAlign = "right"; g.fillText("MP3 usually ends here", x - 5 * dpr, top + 10 * dpr);
    }
    const pts = [], pk = [];
    for (let i = 0; i < use; i++) {
      const v = data[i] / 255; cp[i] = Math.max(v, cp[i] - 0.004);
      pts.push([i / (use - 1) * w, bottom - Math.pow(v, 1.15) * span]); pk.push([i / (use - 1) * w, bottom - Math.pow(cp[i], 1.15) * span]);
    }
    const path = (p) => { g.moveTo(p[0][0], p[0][1]); for (let i = 1; i < p.length - 1; i++) { const mx = (p[i][0] + p[i + 1][0]) / 2, my = (p[i][1] + p[i + 1][1]) / 2; g.quadraticCurveTo(p[i][0], p[i][1], mx, my); } };
    const grad = g.createLinearGradient(0, top, 0, bottom); grad.addColorStop(0, c.accent); grad.addColorStop(1, "rgba(255,255,255,.02)");
    g.beginPath(); path(pk); g.strokeStyle = "rgba(255,255,255,.28)"; g.lineWidth = 1.2 * dpr; g.stroke();
    g.beginPath(); path(pts); g.lineTo(w, bottom); g.lineTo(0, bottom); g.closePath(); g.fillStyle = grad; g.globalAlpha = .85; g.fill(); g.globalAlpha = 1;
    g.beginPath(); path(pts); g.strokeStyle = c.accent; g.lineWidth = 2 * dpr; g.stroke();
  };

  // ---- bars: classic LED columns, deep sounds left, high right ----
  const drawLeds = (f) => {
    const { g, w, h, dpr, c } = f, data = freqData(), nyq = actx.sampleRate / 2, bins = data.length;
    const N = Math.max(18, Math.min(48, Math.floor(w / (13 * dpr)))), lo = 35, hi = Math.min(nyq, 18000);
    if (!vh || vh.length !== N) { vh = new Float32Array(N); vp = new Float32Array(N); }
    g.clearRect(0, 0, w, h);
    const seg = 7 * dpr, gap = 2.5 * dpr, rows = Math.max(6, Math.floor((h - 16 * dpr) / (seg + gap))), slot = w / N, bw = Math.max(3 * dpr, slot * 0.7), base = h - 16 * dpr;
    for (let i = 0; i < N; i++) {
      const f0 = lo * Math.pow(hi / lo, i / N), f1 = lo * Math.pow(hi / lo, (i + 1) / N);
      const b0 = Math.floor(f0 / nyq * bins), b1 = Math.max(b0 + 1, Math.ceil(f1 / nyq * bins));
      let m = 0; for (let k = b0; k < b1 && k < bins; k++) m = Math.max(m, data[k]);
      const v = Math.min(1, Math.pow(m / 255, 1.5) * (1 + 0.7 * i / N)); // the highs carry less energy in real music, so they are lifted a little
      vh[i] = Math.max(v, vh[i] - 0.035); vp[i] = Math.max(vh[i], vp[i] - 0.008);
      const lit = Math.round(vh[i] * rows), peak = Math.min(rows - 1, Math.round(vp[i] * rows)), x = i * slot + (slot - bw) / 2;
      for (let r = 0; r < rows; r++) {
        const y = base - (r + 1) * (seg + gap) + gap;
        if (r < lit) fill(g, x, y, bw, seg, 2 * dpr, c.accent, 0.45 + 0.55 * (r / rows));
        else if (r === peak && peak > 0) fill(g, x, y, bw, seg, 2 * dpr, "#fff", 0.9);
        else fill(g, x, y, bw, seg, 2 * dpr, "#fff", 0.06);
      }
    }
    labelFont(f); g.textAlign = "left"; g.fillText("BASS", 0, h - 3 * dpr); g.textAlign = "center"; g.fillText("MID", w / 2, h - 3 * dpr); g.textAlign = "right"; g.fillText("TREBLE", w, h - 3 * dpr);
  };

  // ---- spectrogram: a waterfall. Pitch up the side, newest sound at the right edge, scrolling left as the song plays ----
  const drawWall = (f) => {
    const { g, w, h, dpr, c } = f, x0 = 44 * dpr, x1 = w - 2 * dpr, y0 = 4 * dpr, y1 = h - 18 * dpr, pw = Math.round(x1 - x0), ph = Math.round(y1 - y0), speed = 60 * dpr;
    if (!vs.buf || vs.buf.width !== pw || vs.buf.height !== ph) { vs.buf = document.createElement("canvas"); vs.buf.width = pw; vs.buf.height = ph; vs.last = f.ts; vs.acc = 0; }
    const bg = vs.buf.getContext("2d"), nyq = actx.sampleRate / 2, maxHz = Math.min(nyq, 22050);
    const dt = Math.min(100, f.ts - (vs.last || f.ts)); vs.last = f.ts;
    if (f.playing) vs.acc += dt / 1000 * speed;
    const step = Math.floor(vs.acc);
    if (step >= 1) {
      vs.acc -= step;
      bg.drawImage(vs.buf, -step, 0);
      const data = freqData(), use = Math.floor(data.length * maxHz / nyq);
      for (let y = 0; y < ph; y++) {
        const k0 = Math.floor((ph - 1 - y) * use / ph), k1 = Math.max(k0 + 1, Math.floor((ph - y) * use / ph)); let m = 0;
        for (let k = k0; k < k1; k++) if (data[k] > m) m = data[k];
        const i = Math.round(Math.pow(m / 255, 1.25) * 255);
        bg.fillStyle = `rgb(${lut[i * 3]},${lut[i * 3 + 1]},${lut[i * 3 + 2]})`; bg.fillRect(pw - step, y, step, 1);
      }
    }
    g.clearRect(0, 0, w, h); g.fillStyle = "#04000a"; g.fillRect(x0, y0, pw, ph); g.drawImage(vs.buf, x0, y0);
    labelFont(f); g.lineWidth = dpr; g.textAlign = "right"; g.textBaseline = "middle";
    for (let k = 0; k <= maxHz; k += 4000) {
      const y = y1 - k / maxHz * ph, key = k === 16000 || k === 20000;
      g.fillStyle = key ? c.ink : c.dim; g.fillText(k === 0 ? "0" : (k / 1000) + " kHz", x0 - 6 * dpr, Math.min(y1 - 5 * dpr, Math.max(y0 + 6 * dpr, y)));
      if (k > 0) { g.strokeStyle = key ? "rgba(255,255,255,.5)" : "rgba(255,255,255,.12)"; g.setLineDash(key ? [5 * dpr, 4 * dpr] : []); g.beginPath(); g.moveTo(x0, y); g.lineTo(x1, y); g.stroke(); }
    }
    g.setLineDash([]); g.textBaseline = "alphabetic"; g.fillStyle = c.dim; g.textAlign = "left"; g.fillText(`${Math.round(pw / speed)} s ago`, x0, h - 3 * dpr); g.textAlign = "right"; g.fillText("now", x1, h - 3 * dpr);
  };

  // ---- loudness: the level right now, a peak marker, and the last ten seconds ----
  const drawLoud = (f) => {
    const { g, w, h, dpr, c } = f, buf = vs.t || (vs.t = new Float32Array(2048));
    an.getFloatTimeDomainData(buf);
    let pk = 0, sq = 0; for (let i = 0; i < buf.length; i++) { const v = buf[i]; if (Math.abs(v) > pk) pk = Math.abs(v); sq += v * v; }
    const rdb = db(Math.sqrt(sq / buf.length)), pdb = db(pk);
    vs.r = vs.r === undefined ? rdb : vs.r + (rdb - vs.r) * 0.25; vs.ph = Math.max(pdb, (vs.ph ?? -90) - 0.4);
    const hist = vs.hist || (vs.hist = []);
    if (f.playing && (!vs.lh || f.ts - vs.lh > 33)) { vs.lh = f.ts; hist.push(rdb); if (hist.length > 300) hist.shift(); }
    const lo = -60, X = (d) => Math.max(0, Math.min(1, (d - lo) / -lo));
    g.clearRect(0, 0, w, h);
    // readout
    g.textBaseline = "alphabetic"; g.textAlign = "left"; g.fillStyle = c.ink; g.font = `800 ${30 * dpr}px system-ui, sans-serif`;
    g.fillText(vs.r <= -59 ? "−∞" : vs.r.toFixed(1), 0, 30 * dpr); const tw = g.measureText(vs.r <= -59 ? "−∞" : vs.r.toFixed(1)).width;
    g.font = `600 ${13 * dpr}px system-ui, sans-serif`; g.fillStyle = c.dim; g.fillText("dB average now", tw + 8 * dpr, 30 * dpr);
    g.textAlign = "right"; g.fillText(`peak ${vs.ph <= -89 ? "−∞" : vs.ph.toFixed(1)} dB`, w, 30 * dpr);
    // meter
    const my = 44 * dpr, mh = 12 * dpr, grad = g.createLinearGradient(0, 0, w, 0); grad.addColorStop(0, c.accent); grad.addColorStop(.75, "#ffb35c"); grad.addColorStop(1, "#ff5c5c");
    fill(g, 0, my, w, mh, mh / 2, "#fff", 0.08); g.save(); g.beginPath(); g.roundRect(0, my, w * X(vs.r), mh, mh / 2); g.clip(); g.fillStyle = grad; g.fillRect(0, my, w, mh); g.restore();
    g.fillStyle = "#fff"; g.fillRect(Math.min(w - 2 * dpr, w * X(vs.ph)), my - 2 * dpr, 2 * dpr, mh + 4 * dpr);
    // history
    const hy0 = my + mh + 16 * dpr, hy1 = h - 18 * dpr, hh = hy1 - hy0; labelFont(f);
    g.lineWidth = dpr; g.textAlign = "left";
    for (const d of [-12, -24, -36, -48]) { const y = hy1 - X(d) * hh; g.strokeStyle = "rgba(255,255,255,.09)"; g.beginPath(); g.moveTo(0, y); g.lineTo(w, y); g.stroke(); g.fillText(d + "", 2, y - 3 * dpr); }
    if (hist.length > 1) {
      const px = (i) => w - (hist.length - 1 - i) / 299 * w, py = (i) => hy1 - X(hist[i]) * hh, gr = g.createLinearGradient(0, hy0, 0, hy1); gr.addColorStop(0, c.accent); gr.addColorStop(1, "rgba(255,255,255,.02)");
      g.beginPath(); g.moveTo(px(0), hy1); for (let i = 0; i < hist.length; i++) g.lineTo(px(i), py(i)); g.lineTo(px(hist.length - 1), hy1); g.closePath(); g.fillStyle = gr; g.globalAlpha = .8; g.fill(); g.globalAlpha = 1;
      g.beginPath(); for (let i = 0; i < hist.length; i++) i ? g.lineTo(px(i), py(i)) : g.moveTo(px(i), py(i)); g.strokeStyle = c.accent; g.lineWidth = 2 * dpr; g.stroke();
    }
    g.fillStyle = c.dim; g.lineWidth = dpr; g.textAlign = "left"; g.fillText("10 s ago", 0, h - 3 * dpr); g.textAlign = "right"; g.fillText("now", w, h - 3 * dpr);
  };

  // ---- stereo: a phase scope (what left and right do together) beside a level meter for each side ----
  const drawStereo = (f) => {
    const { g, w, h, dpr, c } = f, bl = vs.l || (vs.l = new Float32Array(2048)), br = vs.rr || (vs.rr = new Float32Array(2048));
    anL.getFloatTimeDomainData(bl); anR.getFloatTimeDomainData(br);
    let sl = 0, sr = 0, slr = 0, pl = 0, pr = 0;
    for (let i = 0; i < bl.length; i++) { const l = bl[i], r = br[i]; sl += l * l; sr += r * r; slr += l * r; if (Math.abs(l) > pl) pl = Math.abs(l); if (Math.abs(r) > pr) pr = Math.abs(r); }
    const rl = db(Math.sqrt(sl / bl.length)), rr = db(Math.sqrt(sr / br.length)), corr = sl + sr > 1e-9 ? slr / Math.sqrt(Math.max(1e-12, sl * sr)) : 0;
    vs.sm = vs.sm || [rl, rr, pl, pr]; vs.sm[0] += (rl - vs.sm[0]) * 0.25; vs.sm[1] += (rr - vs.sm[1]) * 0.25;
    vs.pk = vs.pk || [-90, -90]; vs.pk[0] = Math.max(db(pl), vs.pk[0] - 0.4); vs.pk[1] = Math.max(db(pr), vs.pk[1] - 0.4);
    vs.cor = vs.cor === undefined ? corr : vs.cor + (corr - vs.cor) * 0.1;
    const s = Math.min(h - 6 * dpr, Math.floor(w * 0.52)), cx = s / 2, cy = h / 2, R = s / 2 - 6 * dpr;
    if (!vs.sc || vs.sc.width !== s || vs.sc.height !== s) { vs.sc = document.createElement("canvas"); vs.sc.width = s; vs.sc.height = s; }
    const sg = vs.sc.getContext("2d"), gx = s / 2;
    sg.globalCompositeOperation = "destination-out"; sg.fillStyle = "rgba(0,0,0,.22)"; sg.fillRect(0, 0, s, s); sg.globalCompositeOperation = "source-over";
    if (f.playing) { sg.fillStyle = c.accent; for (let i = 0; i < bl.length; i += 2) { const l = bl[i], r = br[i], x = gx + (r - l) * 0.7071 * (R * 1.2), y = gx - (l + r) * 0.7071 * (R * 1.2); sg.globalAlpha = .7; sg.fillRect(x - dpr * .75, y - dpr * .75, dpr * 1.5, dpr * 1.5); } sg.globalAlpha = 1; }
    g.clearRect(0, 0, w, h); labelFont(f); g.lineWidth = dpr; g.strokeStyle = "rgba(255,255,255,.12)";
    g.beginPath(); g.arc(cx, cy, R, 0, Math.PI * 2); g.stroke();
    g.beginPath(); g.moveTo(cx, cy - R); g.lineTo(cx, cy + R); g.moveTo(cx - R, cy); g.lineTo(cx + R, cy); g.moveTo(cx - R * .7071, cy - R * .7071); g.lineTo(cx + R * .7071, cy + R * .7071); g.moveTo(cx + R * .7071, cy - R * .7071); g.lineTo(cx - R * .7071, cy + R * .7071); g.stroke();
    g.drawImage(vs.sc, 0, (h - s) / 2);
    g.textAlign = "center"; g.fillText("L", cx - R * .7071 - 8 * dpr, cy - R * .7071 - 2 * dpr); g.fillText("R", cx + R * .7071 + 8 * dpr, cy - R * .7071 - 2 * dpr);
    // the two level meters
    const mx = s + 18 * dpr, mw = Math.max(14 * dpr, Math.min(34 * dpr, (w - mx - 90 * dpr) / 2)), mt = 6 * dpr, mb = h - 22 * dpr, mhh = mb - mt, X = (d) => Math.max(0, Math.min(1, (d + 60) / 60));
    [[vs.sm[0], vs.pk[0], "L", c.accent], [vs.sm[1], vs.pk[1], "R", "#7cc4ff"]].forEach(([v, p, name, col], i) => {
      const x = mx + i * (mw + 12 * dpr);
      fill(g, x, mt, mw, mhh, 5 * dpr, "#fff", 0.08);
      g.save(); g.beginPath(); g.roundRect(x, mb - X(v) * mhh, mw, X(v) * mhh, 5 * dpr); g.clip(); g.fillStyle = col; g.globalAlpha = .9; g.fillRect(x, mt, mw, mhh); g.restore(); g.globalAlpha = 1;
      g.fillStyle = "#fff"; g.fillRect(x, mb - X(p) * mhh - 1.5 * dpr, mw, 2 * dpr);
      g.fillStyle = c.ink; g.textAlign = "center"; g.fillText(name, x + mw / 2, h - 6 * dpr);
    });
    const tx = mx + 2 * (mw + 12 * dpr) + 4 * dpr; g.textAlign = "left"; g.fillStyle = c.dim;
    for (const d of [0, -12, -24, -36, -48]) { const y = mb - X(d) * mhh; g.fillText(d + " dB", tx, y + 4 * dpr); }
  };

  const views = { curve: drawCurve, leds: drawLeds, wall: drawWall, loud: drawLoud, stereo: drawStereo };
  const vizDraw = (ts) => {
    if (!vizOn) return;
    requestAnimationFrame(vizDraw);
    const cv = document.getElementById("viz"); if (!cv || !an) return;
    const dpr = window.devicePixelRatio || 1, w = Math.round(cv.clientWidth * dpr), h = Math.round(cv.clientHeight * dpr);
    if (!w || !h) return;
    if (cv.width !== w || cv.height !== h) { cv.width = w; cv.height = h; vs = {}; }
    const css = getComputedStyle(document.documentElement);
    views[vizStyle](Object.assign({ g: cv.getContext("2d"), w, h, dpr, ts, playing: !audio.paused, c: { accent: css.getPropertyValue("--accent").trim() || "#ff4d8d", dim: css.getPropertyValue("--faint").trim() || "#8a8a94", ink: css.getPropertyValue("--ink").trim() || "#f4f3f7" } }));
  };
  let npRef = null, npPushed = false;
  const npClosed = () => { const r = npRef; npRef = null; npPushed = false; document.body.classList.remove("np-open"); if (r) r.invokeMethodAsync("Closed").catch(() => { }); };
  window.addEventListener("popstate", () => { if (npRef) npClosed(); else npPushed = false; });
  document.addEventListener("keydown", e => { if (e.key === "Escape" && npRef) window.flacie.npBack(); });
  // a reload keeps the #now-playing in the address but not the player; drop it
  if (location.hash === "#now-playing") history.replaceState(null, "", location.pathname + location.search);
  let outside = null;
  return {
    load(ref, url, title, artist, album, art, autoplay, dur, startAt) {
      dotnet = ref;
      ensureGraph();
      audio.src = url;
      if (startAt > 0) audio.addEventListener("loadedmetadata", () => { audio.currentTime = startAt; }, { once: true });
      if ("mediaSession" in navigator) navigator.mediaSession.metadata = new MediaMetadata({ title, artist, album, artwork: art ? [{ src: art, sizes: "500x500", type: "image/jpeg" }] : [] });
      document.title = title ? `${title} · ${artist}` : "FLACie";
      if (autoplay) audio.play().catch(() => send("OnState", false)); else { audio.pause(); send("OnState", false); }
    },
    // starts the Info tab's live view; false when this browser can't do it
    viz(id, style) {
      vizStyle = views[style] ? style : "curve"; vs = {};
      ensureGraph();
      if (!an) return false;
      if (actx.state === "suspended") actx.resume().catch(() => { });
      if (!vizOn) { vizOn = true; requestAnimationFrame(vizDraw); }
      return true;
    },
    vizStop() { vizOn = false; },
    // what this browser is, for the admin dashboard
    async clientInfo() {
      let who = {};
      try { who = await (await fetch("/api/whoami", { credentials: "same-origin" })).json(); } catch { }
      const ua = navigator.userAgent;
      let browser = /Edg\//.test(ua) ? "Edge" : /OPR\//.test(ua) ? "Opera" : /SamsungBrowser/.test(ua) ? "Samsung Internet" : /Firefox\//.test(ua) ? "Firefox" : /Chrome\//.test(ua) ? "Chrome" : /Safari\//.test(ua) ? "Safari" : "Browser";
      try { if (navigator.brave && await navigator.brave.isBrave()) browser = "Brave"; } catch { }
      const model = (/Android [\d.]+; ([^;)]+)/.exec(ua) || [])[1];
      const os = /Windows/.test(ua) ? "Windows" : /Android/.test(ua) ? "Android" + (model && model !== "K" ? " (" + model.trim() + ")" : "") : /iPhone|iPad/.test(ua) ? "iOS" : /Mac OS X/.test(ua) ? "macOS" : /CrOS/.test(ua) ? "ChromeOS" : /Linux/.test(ua) ? "Linux" : "";
      return { ip: who.ip || "", ua, browser, os, screen: `${screen.width}×${screen.height}`, language: navigator.language || "" };
    },
    // a tab in the full-screen player starts at its top, not wherever the last tab was scrolled to
    panelTop() { const p = document.querySelector(".np-panel"); if (p) p.scrollTop = 0; },
    // is the player laid out as a phone (tabs open a sheet over the cover)?
    sheetMode() { return window.matchMedia("(max-width: 1024px) and (orientation: portrait)").matches; },
    toggle() { audio.paused ? audio.play().catch(() => {}) : audio.pause(); },
    play() { audio.play().catch(() => send("OnState", false)); },
    setLyrics(times) { lyr = { times, cur: -2 }; },
    clearLyrics() { lyr = null; },
    // keeps the line being sung in the middle of the lyrics panel, scrolling only that panel (never the page behind it)
    scrollLyric() {
      const el = document.querySelector("#lyrics .cur"), panel = document.querySelector(".np-panel");
      if (el && panel) panel.scrollTo({ top: el.offsetTop - panel.clientHeight / 2 + el.clientHeight / 2, behavior: "smooth" });
    },
    // the full-screen player is a history entry, so the browser's Back button closes it
    // the arrows in a shelf heading scroll the row below it, a page at a time
    scrollRow(btn, dir) { const row = btn.closest(".shelf")?.querySelector(".row-scroll"); if (row) row.scrollBy({ left: dir * row.clientWidth * 0.85, behavior: "smooth" }); },
    npOpen(ref) {
      npRef = ref; document.body.classList.add("np-open");
      if (!npPushed) { history.pushState({ np: 1 }, "", location.pathname + location.search + "#now-playing"); npPushed = true; }
    },
    npBack() { if (npPushed) history.back(); else npClosed(); },
    // when Blazor gives up on the live connection ("rejected": the server was restarted and has forgotten this page; "failed": it could not be reached in time),
    // wait for the server to answer again and reload, instead of leaving a stuck "Can't reach FLACie" toast
    autoRecover() {
      let polling = false;
      const poll = async () => {
        try { const r = await fetch("/healthz", { cache: "no-store" }); if (r.ok) { location.reload(); return; } } catch { }
        setTimeout(poll, 2500);
      };
      const check = () => {
        const m = document.getElementById("components-reconnect-modal");
        if (!m || polling) return;
        if (m.classList.contains("components-reconnect-rejected") || m.classList.contains("components-reconnect-failed")) { polling = true; poll(); }
      };
      const m = document.getElementById("components-reconnect-modal");
      if (m) new MutationObserver(check).observe(m, { attributes: true, attributeFilter: ["class"] });
      document.addEventListener("components-reconnect-state-changed", check);
    },
    // a popup that closes when you click or tap anywhere outside it (selector = the popup's wrapper), or press Escape
    watchOutside(selector, ref) {
      window.flacie.unwatchOutside();
      const down = e => { if (!e.target.closest(selector)) ref.invokeMethodAsync("Dismiss").catch(() => { }); };
      const key = e => { if (e.key === "Escape") ref.invokeMethodAsync("Dismiss").catch(() => { }); };
      outside = { down, key };
      // after the click that opened it has finished, or it would close at once
      setTimeout(() => { if (outside && outside.down === down) { document.addEventListener("pointerdown", down, true); document.addEventListener("keydown", key); } }, 0);
    },
    unwatchOutside() {
      if (!outside) return;
      document.removeEventListener("pointerdown", outside.down, true); document.removeEventListener("keydown", outside.key); outside = null;
    },
    // times on the Downloads page: the exact local time in a tooltip, and the clock time itself where the row asks for it (data-abs)
    times() {
      document.querySelectorAll("time[data-utc]").forEach(t => {
        const d = new Date(t.dataset.utc); if (isNaN(d)) return;
        t.title = d.toLocaleString();
        if (t.dataset.abs === "1") t.textContent = d.toLocaleString([], { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" }) + (t.dataset.rel ? " (" + t.dataset.rel + ")" : "");
        else if (t.dataset.abs === "time") t.textContent = d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
      });
    },
    npReset() { npRef = null; document.body.classList.remove("np-open"); },
    stash(key, json) { try { localStorage.setItem(key, json); } catch { } },
    unstash(key) { try { return localStorage.getItem(key); } catch { return null; } },
    ready() { return audio.readyState >= 3; },
    pause() { audio.pause(); },
    // the X on the bar: stop and let go of the file
    stop() { audio.pause(); audio.removeAttribute("src"); audio.load(); document.title = "FLACie"; if ("mediaSession" in navigator) navigator.mediaSession.metadata = null; },
    seek(s) { if (isFinite(s)) audio.currentTime = s; },
    volume(v) {
      // with the graph in place the volume is applied after the analysers, so the graphs look the same at any volume
      const cur = () => outGain ? outGain.gain.value : audio.volume;
      if (v === undefined || v === null) return cur();
      const x = Math.min(1, Math.max(0, v));
      if (outGain) outGain.gain.value = x; else audio.volume = x;
      try { localStorage.setItem("flacie.volume", String(x)); } catch { }
      return x;
    },
  };
})();

// Personal look-and-feel tweaks (Settings > Look and feel), kept in this browser and applied on every page load
window.flacieUi = (() => {
  const KEY = "flacie.ui";
  const defaults = { accent: "", compact: false, badges: true, motion: true, artbg: true };
  const read = () => { try { return { ...defaults, ...JSON.parse(localStorage.getItem(KEY) || "{}") }; } catch { return { ...defaults }; } };
  const lighter = hex => { const n = parseInt(hex.slice(1), 16); const m = c => Math.round(c + (255 - c) * 0.28); return "#" + [(n >> 16) & 255, (n >> 8) & 255, n & 255].map(m).map(v => v.toString(16).padStart(2, "0")).join(""); };
  const apply = p => {
    const r = document.documentElement;
    if (/^#[0-9a-f]{6}$/i.test(p.accent)) { r.style.setProperty("--accent", p.accent); r.style.setProperty("--accent-2", lighter(p.accent)); }
    else { r.style.removeProperty("--accent"); r.style.removeProperty("--accent-2"); }
    r.classList.toggle("pref-compact", !!p.compact);
    r.classList.toggle("pref-nobadges", p.badges === false);
    r.classList.toggle("pref-calm", p.motion === false);
    r.classList.toggle("pref-noartbg", p.artbg === false);
  };
  apply(read());
  return {
    get: () => JSON.stringify(read()),
    set(json) { try { localStorage.setItem(KEY, json); } catch { } apply(read()); },
  };
})();
