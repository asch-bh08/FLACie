// The audio element for FLACie Web. The queue lives on the server (PlayerState); this plays one song at a time and
// reports time, state and the end of each song back. Media Session gives lock-screen and headphone controls.
window.flacie = (() => {
  const audio = new Audio();
  let baseVolume = 1, fadeGain = 1, loudGain = 1, xfadeGain = 1, xfadeSecs = 0, xfadeIn = false, xfadeOut = false, xfadeRaf = 0;
  const applyVol = () => { audio.volume = Math.min(1, Math.max(0, baseVolume * fadeGain * loudGain * xfadeGain)); };
  // fade between songs: the end fades out and the start fades in (not across the tracks of one album); the same arithmetic as the app: cosine, over at most a third of the song
  const cosFade = (rem, f) => rem >= f ? 1 : rem <= 0 ? 0 : 0.5 - 0.5 * Math.cos(Math.PI * rem / f);
  const xfadeStep = () => {
    xfadeRaf = 0;
    if (xfadeSecs <= 0 || audio.paused || !isFinite(audio.duration) || audio.duration <= 0) { if (xfadeGain !== 1) { xfadeGain = 1; applyVol(); } if (xfadeSecs > 0) xfadeRaf = requestAnimationFrame(xfadeStep); return; }
    const f = Math.min(xfadeSecs, audio.duration / 3);
    const g = Math.min(xfadeIn ? cosFade(audio.currentTime, f) : 1, xfadeOut ? cosFade(audio.duration - audio.currentTime, f) : 1);
    if (Math.abs(g - xfadeGain) > 0.002) { xfadeGain = g; applyVol(); }
    xfadeRaf = requestAnimationFrame(xfadeStep);
  };
  try { const v = parseFloat(localStorage.getItem("flacie.volume")); if (v >= 0 && v <= 1) { audio.volume = v; baseVolume = v; } } catch { }
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
  // buffering: the play button shows a spinner (html.buffering) from the moment a song is asked for until it has data to play
  const root = document.documentElement;
  const buffering = on => { root.classList.toggle("buffering", !!on); };
  const check = () => buffering(audio.src && !audio.paused && audio.readyState < 3 && !audio.error);
  ["loadstart", "waiting", "stalled", "seeking", "play"].forEach(ev => audio.addEventListener(ev, check));
  ["playing", "canplay", "canplaythrough", "seeked", "pause", "emptied", "error", "ended"].forEach(ev => audio.addEventListener(ev, check));
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
  // Live views for the Info tab: spectrum, bars, spectrogram, loudness, stereo. Playback itself goes straight from the audio element to the
  // output (no Web Audio in its path, so the browser never resamples it to a fixed rate). The graphs read a silent twin of the same stream
  // through an AudioContext opened at the file's own sample rate, so the spectrum reaches the file's real Nyquist. Not done on iOS.
  let actx = null, an = null, anL = null, anR = null, twin = null, vizOn = false, vizStyle = "curve", vs = {}, vh = null, vp = null, cp = null, ctxKey = "", vizCut = 0, vizCutLabel = "";
  const ios = /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  const AC = window.AudioContext || window.webkitAudioContext;
  const closeGraph = () => {
    if (twin) { try { twin.pause(); twin.removeAttribute("src"); twin.load(); } catch { } twin = null; }
    if (actx) { try { actx.close(); } catch { } }
    actx = an = anL = anR = null; ctxKey = "";
  };
  const syncTwin = () => {
    if (!twin || !audio.src) return;
    if (twin.src !== audio.src) twin.src = audio.src;
    if (Math.abs(twin.currentTime - audio.currentTime) > 0.3) { try { twin.currentTime = audio.currentTime; } catch { } }
    if (twin.playbackRate !== audio.playbackRate) twin.playbackRate = audio.playbackRate;
    if (audio.paused !== twin.paused) { if (audio.paused) twin.pause(); else twin.play().catch(() => { }); }
  };
  ["play", "pause", "seeked", "loadstart", "ratechange"].forEach(ev => audio.addEventListener(ev, syncTwin));
  const buildGraph = (rate) => {
    if (ios || !AC) return false;
    const key = String(rate || 0);
    if (actx && ctxKey === key) return true;
    closeGraph();
    try {
      try { actx = rate >= 8000 && rate <= 192000 ? new AC({ sampleRate: rate, latencyHint: "playback" }) : new AC(); } catch { actx = new AC(); }
      twin = new Audio(); twin.preload = "auto";
      const src = actx.createMediaElementSource(twin);
      // nothing here reaches the speakers: the analysers feed a gain of zero (which keeps them running)
      const sink = actx.createGain(); sink.gain.value = 0; sink.connect(actx.destination);
      const a = actx.createAnalyser(); a.fftSize = 2048; a.smoothingTimeConstant = 0.7; src.connect(a); a.connect(sink);
      // left and right on their own; the gain node first turns a mono file into two equal channels, so a mono song reads the same on both sides
      const up = actx.createGain(); up.channelCount = 2; up.channelCountMode = "explicit"; up.channelInterpretation = "speakers";
      const split = actx.createChannelSplitter(2);
      const l = actx.createAnalyser(), r = actx.createAnalyser(); l.fftSize = r.fftSize = 2048; l.smoothingTimeConstant = r.smoothingTimeConstant = 0;
      src.connect(up); up.connect(split); split.connect(l, 0); split.connect(r, 1); l.connect(sink); r.connect(sink);
      an = a; anL = l; anR = r; ctxKey = key;
      if (actx.state === "suspended") actx.resume().catch(() => { });
      syncTwin();
      return true;
    } catch { closeGraph(); return false; }
  };

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
    const maxHz = nyq, stepHz = maxHz > 48000 ? 20000 : maxHz > 26000 ? 10000 : 5000, use = Math.floor(bins * maxHz / nyq), bottom = h - 18 * dpr, top = 6 * dpr, span = bottom - top;
    if (!cp || cp.length !== use) cp = new Float32Array(use);
    g.clearRect(0, 0, w, h); labelFont(f); g.lineWidth = dpr;
    for (let k = 0; k <= maxHz; k += stepHz) {
      const x = Math.min(w - 1, k / maxHz * w);
      g.strokeStyle = "rgba(255,255,255,.08)"; g.beginPath(); g.moveTo(x, top); g.lineTo(x, bottom); g.stroke();
      g.textAlign = k === 0 ? "left" : "center"; g.fillText(k === 0 ? "0" : (k / 1000) + " kHz", k === 0 ? 2 : Math.min(x, w - 22 * dpr), h - 3 * dpr);
    }
    if (vizCut > 0 && maxHz > vizCut) { // where an encoder at this file's bitrate usually cuts the sound off
      const x = vizCut / maxHz * w; g.setLineDash([4 * dpr, 4 * dpr]); g.strokeStyle = "rgba(255,255,255,.3)"; g.beginPath(); g.moveTo(x, top); g.lineTo(x, bottom); g.stroke(); g.setLineDash([]);
      g.textAlign = "right"; g.fillText(vizCutLabel || "usually ends here", x - 5 * dpr, top + 10 * dpr);
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
    const bg = vs.buf.getContext("2d"), nyq = actx.sampleRate / 2, maxHz = nyq, stepHz = maxHz > 48000 ? 16000 : maxHz > 26000 ? 8000 : 4000;
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
    for (let k = 0; k <= maxHz; k += stepHz) {
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
  let lastSync = 0;
  const vizDraw = (ts) => {
    if (!vizOn) return;
    requestAnimationFrame(vizDraw);
    const cv = document.getElementById("viz"); if (!cv || !an) return;
    if (ts - lastSync > 500) { lastSync = ts; syncTwin(); }
    const dpr = window.devicePixelRatio || 1, w = Math.round(cv.clientWidth * dpr), h = Math.round(cv.clientHeight * dpr);
    if (!w || !h) return;
    if (cv.width !== w || cv.height !== h) { cv.width = w; cv.height = h; vs = {}; }
    const css = getComputedStyle(document.documentElement);
    views[vizStyle](Object.assign({ g: cv.getContext("2d"), w, h, dpr, ts, playing: !audio.paused, c: { accent: css.getPropertyValue("--accent").trim() || "#ff4d8d", dim: css.getPropertyValue("--faint").trim() || "#8a8a94", ink: css.getPropertyValue("--ink").trim() || "#f4f3f7" } }));
  };
  let npRef = null, npPushed = false;
  const npClosed = () => { const r = npRef; npRef = null; npPushed = false; document.body.classList.remove("np-open"); if (r) r.invokeMethodAsync("Closed").catch(() => { }); };
  // Back onto a "#now-playing" entry (after following a link out of the full-screen player) brings the player back instead of landing on the bare page
  let barRef = null;
  window.addEventListener("popstate", () => {
    if (npRef) npClosed();
    else if (location.hash === "#now-playing" && barRef) { npPushed = true; barRef.invokeMethodAsync("ReopenFull").catch(() => { }); }
    else npPushed = false;
  });
  // the filter bar on Explore slides away while scrolling down and comes back as soon as you scroll up
  let lastY = 0;
  window.addEventListener("scroll", () => {
    const y = window.scrollY, d = y - lastY, b = document.querySelector(".x-bar");
    if (!b) { lastY = y; return; }
    if (Math.abs(d) < 8) return;
    b.classList.toggle("away", d > 0 && y > 160); lastY = y;
  }, { passive: true });
  document.addEventListener("keydown", e => { if (e.key === "Escape" && npRef) window.flacie.npBack(); });
  // keyboard shortcuts for the player: Space play/pause, Left/Right seek 5 s, N next, P previous. Ignored while typing and on controls that use the key themselves.
  document.addEventListener("keydown", e => {
    if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey || !audio.src) return;
    const t = e.target, tag = t && t.tagName ? t.tagName.toLowerCase() : "";
    if (tag === "input" || tag === "textarea" || tag === "select" || (t && t.isContentEditable)) return;
    const k = e.key;
    if (k === " " || k === "Spacebar") { if (tag === "button" || tag === "a" || tag === "summary") return; e.preventDefault(); if (audio.paused) audio.play().catch(() => {}); else audio.pause(); }
    else if (k === "ArrowRight") { e.preventDefault(); audio.currentTime = Math.min(audio.duration || Infinity, audio.currentTime + 5); }
    else if (k === "ArrowLeft") { e.preventDefault(); audio.currentTime = Math.max(0, audio.currentTime - 5); }
    else if (k === "n" || k === "N") send("OnNext");
    else if (k === "p" || k === "P") send("OnPrev");
  });
  // a song row's "..." menu closes on a click outside it or Escape: press its own toggle, which Blazor already handles
  const closeRowMenu = () => document.querySelector(".track button[aria-expanded=true]")?.click();
  document.addEventListener("pointerdown", e => { if (!e.target.closest(".track .menu, .track button[aria-expanded]")) closeRowMenu(); }, true);
  document.addEventListener("keydown", e => { if (e.key === "Escape") closeRowMenu(); });
  // a reload keeps the #now-playing in the address but not the player; drop it
  if (location.hash === "#now-playing") history.replaceState(null, "", location.pathname + location.search);
  // ---- mini player (picture in picture): the cover, the title and a few controls in a small window that stays on top when you switch tabs or apps ----
  // Chrome and Edge open a real little page (Document Picture-in-Picture) with buttons; other browsers get a video picture-in-picture of the cover that
  // carries the play / previous / next buttons of Media Session. The sound keeps playing from this tab either way.
  const pip = (() => {
    let cur = { title: "", artist: "", art: "" }, win = null, el = null, vidWin = null, canvas = null, vid = null, img = null, lastDraw = 0;
    const ICON = {
      prev: '<svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor"><path d="M6 5h2v14H6zM20 5v14L9 12z"/></svg>',
      next: '<svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor"><path d="M16 5h2v14h-2zM4 5v14l11-7z"/></svg>',
      play: '<svg viewBox="0 0 24 24" width="26" height="26" fill="currentColor"><path d="M7 4.5v15a1 1 0 0 0 1.5.86l12-7.5a1 1 0 0 0 0-1.72l-12-7.5A1 1 0 0 0 7 4.5z"/></svg>',
      pause: '<svg viewBox="0 0 24 24" width="26" height="26" fill="currentColor"><rect x="6" y="4" width="4" height="16" rx="1"/><rect x="14" y="4" width="4" height="16" rx="1"/></svg>',
    };
    const CSS = "*{box-sizing:border-box}body{margin:0;background:#0d0d12;color:#f2f2f6;font:14px system-ui,-apple-system,Segoe UI,sans-serif;height:100vh;display:flex;flex-direction:column;overflow:hidden}"
      + ".art{flex:1;min-height:0;display:flex;align-items:center;justify-content:center;padding:12px 12px 0}.art img{max-width:100%;max-height:100%;aspect-ratio:1;object-fit:cover;border-radius:12px;box-shadow:0 8px 30px #0008;background:#1b1b24}"
      + ".t{padding:10px 16px 0;text-align:center}.t b{display:block;font-size:15px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.t span{display:block;color:#a8a8b8;font-size:12.5px;margin-top:2px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}"
      + "input[type=range]{width:calc(100% - 32px);margin:10px 16px 0;accent-color:#ff4f7b;height:16px}.c{display:flex;justify-content:center;align-items:center;gap:14px;padding:6px 0 14px}"
      + "button{border:0;background:none;color:#f2f2f6;width:44px;height:44px;border-radius:50%;display:grid;place-items:center;cursor:pointer}button:hover{background:#ffffff1c}"
      + "button.p{background:#f2f2f6;color:#0d0d12;width:52px;height:52px}button.p:hover{background:#fff}";
    const supportedDoc = () => "documentPictureInPicture" in window;
    const supportedVid = () => !!document.pictureInPictureEnabled;
    function paint() {
      if (win && el) {
        el.img.src = cur.art || ""; el.img.style.visibility = cur.art ? "visible" : "hidden";
        el.title.textContent = cur.title; el.artist.textContent = cur.artist;
        el.play.innerHTML = audio.paused ? ICON.play : ICON.pause; el.play.setAttribute("aria-label", audio.paused ? "Play" : "Pause");
        if (isFinite(audio.duration)) { el.seek.max = audio.duration; el.seek.value = audio.currentTime; }
      }
      if (vidWin) draw();
    }
    function draw() {
      if (!canvas) return;
      const c = canvas.getContext("2d"), W = canvas.width;
      c.fillStyle = "#0d0d12"; c.fillRect(0, 0, W, W);
      if (img && img.complete && img.naturalWidth) { const r = Math.max(W / img.naturalWidth, W / img.naturalHeight); c.drawImage(img, (W - img.naturalWidth * r) / 2, (W - img.naturalHeight * r) / 2, img.naturalWidth * r, img.naturalHeight * r); }
      const g = c.createLinearGradient(0, W * 0.6, 0, W); g.addColorStop(0, "#0d0d1200"); g.addColorStop(1, "#0d0d12ee"); c.fillStyle = g; c.fillRect(0, W * 0.6, W, W * 0.4);
      c.fillStyle = "#f2f2f6"; c.font = "600 30px system-ui,sans-serif"; c.textAlign = "left"; c.fillText((cur.title || "").slice(0, 34), 24, W - 62);
      c.fillStyle = "#c4c4d2"; c.font = "22px system-ui,sans-serif"; c.fillText((cur.artist || "").slice(0, 44), 24, W - 28);
      if (isFinite(audio.duration) && audio.duration > 0) { c.fillStyle = "#ffffff30"; c.fillRect(0, W - 6, W, 6); c.fillStyle = "#ff4f7b"; c.fillRect(0, W - 6, W * audio.currentTime / audio.duration, 6); }
    }
    async function openDoc() {
      const w = await window.documentPictureInPicture.requestWindow({ width: 340, height: 440 });
      const d = w.document;
      const st = d.createElement("style"); st.textContent = CSS; d.head.append(st);
      d.title = "FLACie";
      d.body.innerHTML = '<div class="art"><img alt=""></div><div class="t"><b></b><span></span></div><input type="range" min="0" max="1" step="1" value="0" aria-label="Seek"><div class="c"><button class="prev" aria-label="Previous"></button><button class="p play"></button><button class="next" aria-label="Next"></button></div>';
      el = { img: d.querySelector("img"), title: d.querySelector(".t b"), artist: d.querySelector(".t span"), seek: d.querySelector("input"), play: d.querySelector(".play") };
      d.querySelector(".prev").innerHTML = ICON.prev; d.querySelector(".next").innerHTML = ICON.next;
      d.querySelector(".prev").onclick = () => send("OnPrev"); d.querySelector(".next").onclick = () => send("OnNext");
      el.play.onclick = () => { if (audio.paused) audio.play().catch(() => { }); else audio.pause(); };
      el.seek.oninput = () => { audio.currentTime = +el.seek.value; };
      win = w; w.addEventListener("pagehide", () => { win = null; el = null; });
      paint();
    }
    async function openVid() {
      if (!canvas) { canvas = document.createElement("canvas"); canvas.width = canvas.height = 480; vid = document.createElement("video"); vid.muted = true; vid.playsInline = true; vid.srcObject = canvas.captureStream(5); vid.addEventListener("leavepictureinpicture", () => { vidWin = null; }); }
      draw(); await vid.play().catch(() => { });
      vidWin = await vid.requestPictureInPicture();
    }
    function setArt(url) { if (!url) { img = null; return; } img = new Image(); img.onload = () => { paint(); }; img.src = url; }
    ["play", "pause", "timeupdate"].forEach(ev => audio.addEventListener(ev, () => { const n = Date.now(); if (ev !== "timeupdate" || n - lastDraw > 900) { lastDraw = n; if (win || vidWin) paint(); } }));
    return {
      supported: () => supportedDoc() || supportedVid(),
      active: () => !!(win || vidWin),
      meta(title, artist, art) { cur = { title, artist, art }; setArt(art); paint(); },
      async toggle() {
        if (win) { win.close(); return false; }
        if (vidWin) { try { await document.exitPictureInPicture(); } catch { } return false; }
        try { if (supportedDoc()) await openDoc(); else if (supportedVid()) await openVid(); else return false; return true; } catch { return false; }
      },
    };
  })();
  if ("mediaSession" in navigator) {
    // Chrome can open the mini player by itself when you switch to another tab while music plays
    try { navigator.mediaSession.setActionHandler("enterpictureinpicture", () => { pip.toggle(); }); } catch { }
  }
  let outside = null, pendingLoad = 0, lastLoadAt = -1e9;
  let ipod = null, touchOs = null;
  // The next song's first bytes are fetched early (a second audio element, preload=auto), so a skip starts at once. One prefetch at a time, started only
  // once the current song can play (the song asked for gets the connection and the server first), and replaced, never added to, when the queue moves on.
  let pre = null, preTok = 0, preTimer = 0;
  const dropPre = () => { if (pre) { try { pre.removeAttribute("src"); pre.load(); } catch { } pre = null; } };
  const prefetch = (u) => {
    const tok = ++preTok; clearTimeout(preTimer);
    if (!u) { dropPre(); return; }
    if (pre && pre.dataset.u === u) return;
    const go = () => {
      if (tok !== preTok) return;
      preTimer = setTimeout(() => {
        if (tok !== preTok || (pre && pre.dataset.u === u)) return;
        dropPre();
        pre = new Audio(); pre.preload = "auto"; pre.dataset.u = u; pre.src = u + (u.includes("?") ? "&" : "?") + "pre=1";   // pre=1: the server reads only its first megabyte, as background work
        window.__flaciePrefetches = (window.__flaciePrefetches || 0) + 1;
      }, 400);
    };
    audio.addEventListener("canplay", go, { once: true });
    if (audio.src && audio.readyState >= 3) go();
  };
  return {
    prefetchOpen() { return pre ? 1 : 0; },
    pipSupported() { return pip.supported(); },
    pipActive() { return pip.active(); },
    pipToggle() { return pip.toggle(); },
    load(ref, url, title, artist, album, art, autoplay, dur, startAt, nextUrl) {
      dotnet = ref;
      pip.meta(title, artist, art);
      if ("mediaSession" in navigator) navigator.mediaSession.metadata = new MediaMetadata({ title, artist, album, artwork: art ? [{ src: art, sizes: "500x500", type: "image/jpeg" }] : [] });
      document.title = title ? `${title} · ${artist}` : "FLACie";
      const open = () => {
        pendingLoad = 0;
        audio.src = url;
        if (autoplay) buffering(true);
        if (startAt > 0) audio.addEventListener("loadedmetadata", () => { audio.currentTime = startAt; }, { once: true });
        if (autoplay) audio.play().catch(() => send("OnState", false)); else { audio.pause(); send("OnState", false); }
      };
      // A burst of skips: songs the user passes over never open a stream. The first load of a burst starts at once (a single play is not
      // delayed); a load that follows another within half a second drops whatever was loading and waits 150 ms for the next skip.
      const now = performance.now(), burst = now - lastLoadAt < 500;
      lastLoadAt = now;
      if (pendingLoad) { clearTimeout(pendingLoad); pendingLoad = 0; }
      if (burst) { audio.pause(); audio.removeAttribute("src"); audio.load(); pendingLoad = setTimeout(open, 150); } else open();
      prefetch(nextUrl);   // after the new source is set (or the old one cleared): "can play" then means this song
    },
    loadsStarted() { return lastLoadAt; },
    // starts the Info tab's live view; false when this browser can't do it
    viz(id, style, rate, cutHz, cutLabel) {
      vizCut = cutHz | 0; vizCutLabel = cutLabel || "";
      vizStyle = views[style] ? style : "curve"; vs = {};
      if (!buildGraph(rate | 0) || !an) return false;
      if (!vizOn) { vizOn = true; requestAnimationFrame(vizDraw); }
      return true;
    },
    vizStop() { vizOn = false; closeGraph(); },
    // the output device's own rate: a context made with no rate takes it
    outputRate() { try { if (!AC) return 0; const c = new AC(); const r = c.sampleRate; c.close(); return r; } catch { return 0; } },
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
    // the phone player: a sideways swipe moves between Lyrics, the cover, Info and Up next (never skips the song)
    paneSwipe(el, ref) {
      if (!el || el._swipe) return; el._swipe = true;
      let x0 = null, y0 = 0;
      el.addEventListener("pointerdown", e => { x0 = (e.pointerType === "mouse" || e.target.closest("input[type=range],button.play,.seek")) ? null : e.clientX; y0 = e.clientY; });
      el.addEventListener("pointerup", e => {
        if (x0 === null) return; const dx = e.clientX - x0, dy = e.clientY - y0; x0 = null;
        if (!matchMedia("(max-width: 1024px) and (orientation: portrait)").matches) return;
        if (Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy) * 1.4) ref.invokeMethodAsync("PaneSwipe", dx < 0 ? 1 : -1);
      });
      el.addEventListener("pointercancel", () => { x0 = null; });
    },
    npOpen(ref) {
      npRef = ref; document.body.classList.add("np-open");
      if (!npPushed) { history.pushState({ np: 1 }, "", location.pathname + location.search + "#now-playing"); npPushed = true; }
    },
    // a link inside the full-screen player: drop the player's #now-playing history entry (history.back), then go, so Back from the new page returns to the page under the player
    npGo(url) {
      const go = () => window.Blazor ? Blazor.navigateTo(url) : (location.href = url);
      if (!npPushed) { go(); return; }
      const after = () => { window.removeEventListener("popstate", after); setTimeout(go, 0); };
      window.addEventListener("popstate", after);
      history.back();
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
    // iPod mode: fit the device to the window, focus it for the keys, and run the wheel: a drag around the ring becomes degrees (sent a few times a second; the page turns them into
    // list rows), a tap is a zone (MENU, next, previous, play/pause, the middle button), a press held on next/previous seeks and held on MENU goes home, and a flick coasts on.
    ipodInit(stage, device, wheel, ref, cfg, fitEl) {
      window.flacie.ipodDispose();
      // the device is zoomed to fill the free area (the window less the panel); the zoom sits on a wrapper the page never re-renders, so a change of model or look cannot wipe it
      const fit = () => {
        const w = +device.dataset.w || 300, h = +device.dataset.h || 500;
        const s = Math.max(0.3, Math.min((stage.clientWidth - 24) / w, (stage.clientHeight - 24) / h, 3));
        fitEl.style.zoom = String(s);
      };
      fit(); addEventListener("resize", fit); stage.focus();
      const ro = new ResizeObserver(fit); ro.observe(stage);
      const mo = new MutationObserver(fit); mo.observe(device, { attributes: true, attributeFilter: ["data-w", "data-h"] });
      const cleanup = { ro, mo };
      // the keys the device uses are the device's alone: a focused button must not also click on Enter or Space, and the page-wide shortcuts (Space, arrows) must not act as well
      const keys = e => { if (["Enter", " ", "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(e.key)) e.preventDefault(); };
      stage.addEventListener("keydown", keys);
      if (!wheel) { ipod = { fit, wheel: null, stage, keys, ...cleanup }; return innerWidth >= 860; }   // touch models: no wheel, the screen is the FLACie player
      const st = ipod = { ...cleanup, fit, wheel, stage, keys, cfg: cfg || { deg: 34 }, last: null, acc: 0, moved: 0, rotating: false, zone: null, held: false, timer: null, hold: null, samples: [], coast: 0, ctx: null };
      const ang = e => { const r = wheel.getBoundingClientRect(); return Math.atan2(e.clientY - (r.top + r.height / 2), e.clientX - (r.left + r.width / 2)) * 180 / Math.PI; };
      const zoneOf = e => {
        const r = wheel.getBoundingClientRect(), dx = e.clientX - (r.left + r.width / 2), dy = e.clientY - (r.top + r.height / 2), d = Math.hypot(dx, dy), R = r.width / 2;
        if (d > R) return null;
        if (d < R * 0.36) return "center";
        const a = Math.atan2(dy, dx) * 180 / Math.PI;
        return a >= -135 && a < -45 ? "menu" : a >= -45 && a < 45 ? "next" : a >= 45 && a < 135 ? "play" : "prev";
      };
      const flush = () => { if (st.acc !== 0) { const d = st.acc; st.acc = 0; const v = Math.abs(speed()); ref.invokeMethodAsync("Turned", d, v).catch(() => { }); } };
      const speed = () => {
        const s = st.samples; if (s.length < 2) return 0;
        const a = s[0], b = s[s.length - 1], dt = b.t - a.t; return dt > 0 ? (b.a - a.a) * 1000 / dt : 0;
      };
      const down = e => {
        const z = zoneOf(e); if (!z) return;
        st.coast && cancelAnimationFrame(st.coast); st.coast = 0;
        st.zone = z; st.last = ang(e); st.moved = 0; st.slop = 0; st.rotating = false; st.held = false; st.samples = [{ t: performance.now(), a: 0 }]; st.total = 0;
        st.timer = setInterval(flush, 30);
        wheel.classList.add("turning");
        st.hold = setTimeout(() => {
          if (st.rotating || st.last === null) return;
          if (st.zone === "next" || st.zone === "prev") { st.held = true; const go = () => { if (st.last === null) return; ref.invokeMethodAsync("Hold", st.zone).catch(() => { }); st.hold = setTimeout(go, 130); }; go(); }
          else if (st.zone === "menu") { st.held = true; ref.invokeMethodAsync("Hold", "menu").catch(() => { }); }
        }, 500);
      };
      const move = e => {
        if (st.last === null) return;
        let d = ang(e) - st.last; st.last = ang(e);
        if (d > 180) d -= 360; if (d < -180) d += 360;
        st.moved += Math.abs(d); st.slop = (st.slop || 0) + d;
        if (st.zone !== "center" && st.moved > 8 && !st.rotating) { st.rotating = true; d = st.slop; }   // the first few degrees are not lost
        if (!st.rotating) return;
        st.total += d; st.acc += d;
        const s = st.samples; s.push({ t: performance.now(), a: st.total }); while (s.length > 2 && s[s.length - 1].t - s[0].t > 100) s.shift();
      };
      const up = () => {
        if (st.last === null) return; st.last = null; clearInterval(st.timer); clearTimeout(st.hold); wheel.classList.remove("turning");
        const v = speed(), zone = st.zone, rotating = st.rotating, held = st.held; flush();
        if (!rotating && !held) ref.invokeMethodAsync("Zone", zone).catch(() => { });
        if (rotating && Math.abs(v) >= 300) {
          // the coast of a flick: slows down exponentially (350 ms), at most 40 rows in all, ends at a hair above standstill; a touch stops it
          let w = v, t0 = performance.now(), sent = 0; const limit = 40 * (st.cfg.deg || 34);
          const step = t => {
            const dt = Math.min(100, t - t0); t0 = t; w *= Math.exp(-dt / 350);
            const d = w * dt / 1000; sent += Math.abs(d);
            if (Math.abs(w) < 45 || sent > limit) { st.coast = 0; ref.invokeMethodAsync("Released").catch(() => { }); return; }
            ref.invokeMethodAsync("Turned", d, Math.abs(w)).catch(() => { });
            st.coast = requestAnimationFrame(step);
          };
          st.coast = requestAnimationFrame(step);
        } else ref.invokeMethodAsync("Released").catch(() => { });
      };
      st.down = down; st.move = move; st.up = up;
      wheel.addEventListener("pointerdown", down); addEventListener("pointermove", move); addEventListener("pointerup", up); addEventListener("pointercancel", up);
      return innerWidth >= 860;
    },
    ipodConfig(cfg) { if (ipod) ipod.cfg = cfg; },
    ipodFit() { if (ipod) ipod.fit(); },
    // the clicker: a short tick from the page (Web Audio), a press a little lower; the phone buzzes where it can
    ipodTick(kind, sound, vol, haptic) {
      try {
        if (haptic && navigator.vibrate) navigator.vibrate(kind === "press" ? 8 : 3);
        if (!sound) return;
        const ctx = ipod && (ipod.ctx || (ipod.ctx = new (window.AudioContext || window.webkitAudioContext)())); if (!ctx) return;
        const o = ctx.createOscillator(), g = ctx.createGain(), t = ctx.currentTime, level = [0.04, 0.1, 0.22][vol] ?? 0.1;
        o.type = "square"; o.frequency.value = kind === "press" ? 900 : 1900;
        g.gain.setValueAtTime(level, t); g.gain.exponentialRampToValueAtTime(0.0001, t + 0.03);
        o.connect(g); g.connect(ctx.destination); o.start(t); o.stop(t + 0.035);
      } catch { }
    },
    ipodClock() { return new Date().toLocaleTimeString([], { hour: "numeric", minute: "2-digit" }); },
    async ipodBattery() { try { const b = await navigator.getBattery(); return [Math.round(b.level * 100), b.charging]; } catch { return [100, false]; } },
    // a text box the server never rewrites while it is being typed in (see TextBox.razor): the browser debounces and tells the server once
    textBox(el, ref, value, ms) {
      if (!el) return;
      el.value = value;
      let t = null; const send = () => { clearTimeout(t); t = null; ref.invokeMethodAsync("OnText", el.value).catch(() => { }); };
      const input = () => { clearTimeout(t); t = setTimeout(send, ms); };
      const key = e => { if (e.key === "Enter" && t !== null) send(); };
      const search = () => send();   // the x of a search box
      el.addEventListener("input", input); el.addEventListener("keydown", key); el.addEventListener("search", search);
      el._tb = { input, key, search, flush: () => { if (t !== null) send(); } };
    },
    textBoxSet(el, value) { if (el && document.activeElement !== el && el.value !== value) el.value = value; },
    textBoxOff(el) { if (el && el._tb) { el.removeEventListener("input", el._tb.input); el.removeEventListener("keydown", el._tb.key); el.removeEventListener("search", el._tb.search); el._tb = null; } },
    ipodDate() { return new Date().toLocaleDateString(undefined, { weekday: "long", month: "long", day: "numeric" }); },
    // the touch iPods' scrolling: a mouse can drag a list like a finger does (touch screens scroll by themselves); a drag is not a tap; the end of a long list asks for more rows
    iosInit(root) {
      window.flacie.iosDispose();
      let sc = null, y0 = 0, top0 = 0, dragged = false, k = 1;
      const down = e => {
        if (e.pointerType !== "mouse" || e.button !== 0) return;
        sc = e.target.closest(".ios-scroll"); if (!sc) return;
        const r = root.getBoundingClientRect(); k = r.width / (root.offsetWidth || 1) || 1;
        y0 = e.clientY; top0 = sc.scrollTop; dragged = false;
      };
      const move = e => { if (!sc) return; const dy = e.clientY - y0; if (!dragged && Math.abs(dy) > 5) dragged = true; if (dragged) sc.scrollTop = top0 - dy / k; };
      const up = () => { sc = null; setTimeout(() => { dragged = false; }, 0); };
      const click = e => { if (dragged) { e.stopPropagation(); e.preventDefault(); } };
      root.addEventListener("pointerdown", down); addEventListener("pointermove", move); addEventListener("pointerup", up); root.addEventListener("click", click, true);
      touchOs = { root, down, move, up, click, io: null };
    },
    iosMore(el, ref) {
      if (!touchOs || !el) return;
      if (touchOs.io) touchOs.io.disconnect();
      touchOs.io = new IntersectionObserver(es => { if (es.some(e => e.isIntersecting)) { touchOs.io.disconnect(); ref.invokeMethodAsync("More").catch(() => { }); } }, { rootMargin: "300px" });
      touchOs.io.observe(el);
    },
    iosDispose() {
      if (!touchOs) return;
      touchOs.root.removeEventListener("pointerdown", touchOs.down); removeEventListener("pointermove", touchOs.move); removeEventListener("pointerup", touchOs.up); touchOs.root.removeEventListener("click", touchOs.click, true);
      if (touchOs.io) touchOs.io.disconnect(); touchOs = null;
    },
    ipodDispose() {
      if (!ipod) return;
      try { ipod.ro && ipod.ro.disconnect(); ipod.mo && ipod.mo.disconnect(); } catch { }
      removeEventListener("resize", ipod.fit); if (ipod.wheel) ipod.wheel.removeEventListener("pointerdown", ipod.down); ipod.stage.removeEventListener("keydown", ipod.keys);
      removeEventListener("pointermove", ipod.move); removeEventListener("pointerup", ipod.up); removeEventListener("pointercancel", ipod.up);
      clearInterval(ipod.timer); clearTimeout(ipod.hold); if (ipod.coast) cancelAnimationFrame(ipod.coast); try { ipod.ctx && ipod.ctx.close(); } catch { } ipod = null;
    },
    // a song's menu is a short scrollable list: bring its last part (the Hi-Res question) into view
    menuToEnd() { document.querySelectorAll(".menu").forEach(m => { m.scrollTop = m.scrollHeight; }); },
    // a popup that closes when you click or tap anywhere outside it (selector = the popup's wrapper), or press Escape
    watchOutside(selector, ref) {
      window.flacie.unwatchOutside();
      const down = e => { if (!e.target.closest(selector)) ref.invokeMethodAsync("Dismiss").catch(() => { }); };
      const key = e => { if (e.key === "Escape") ref.invokeMethodAsync("Dismiss").catch(() => { }); };
      outside = { down, key };
      // after the click that opened it has finished, or it would close at once
      setTimeout(() => { if (outside && outside.down === down) { document.addEventListener("pointerdown", down, true); document.addEventListener("keydown", key); } }, 0);
    },
    // a dropdown just opened: open upwards when there is more room above than below, line up with the right edge when it would run off the screen,
    // and scroll the chosen option into view
    ddPlace(root) {
      const menu = root && root.querySelector(".dd-menu"); if (!menu) return;
      menu.classList.remove("up", "right");
      const r = root.getBoundingClientRect(), h = Math.min(menu.scrollHeight, 240), below = innerHeight - r.bottom - 12, above = r.top - 12;
      if (below < h && above > below) menu.classList.add("up");
      if (r.left + menu.offsetWidth > innerWidth - 8) menu.classList.add("right");
      const sel = menu.querySelector(".sel"); if (sel) menu.scrollTop = Math.max(0, sel.offsetTop - menu.clientHeight / 2 + sel.offsetHeight / 2);
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
    npReset() { npRef = null; npPushed = false; document.body.classList.remove("np-open"); },
    setBarRef(r) { barRef = r; },
    stash(key, json) { try { localStorage.setItem(key, json); } catch { } },
    unstash(key) { try { return localStorage.getItem(key); } catch { return null; } },
    ready() { return audio.readyState >= 3; },
    pause() { audio.pause(); },
    // the X on the bar: stop and let go of the file
    stop() { if (pendingLoad) { clearTimeout(pendingLoad); pendingLoad = 0; } audio.pause(); audio.removeAttribute("src"); audio.load(); document.title = "FLACie"; if ("mediaSession" in navigator) navigator.mediaSession.metadata = null; },
    seek(s) { if (isFinite(s)) audio.currentTime = s; },
    // playback speed with the pitch kept (setting src reloads the element, which would reset playbackRate: the default is set too)
    rate(r) {
      if (r === undefined || r === null) return audio.playbackRate;
      const x = Math.min(3, Math.max(0.5, Number(r) || 1));
      audio.defaultPlaybackRate = x; audio.playbackRate = x;
      audio.preservesPitch = true; audio.mozPreservesPitch = true; audio.webkitPreservesPitch = true;
      return audio.playbackRate;
    },
    // the sleep timer's fade-out scales the volume the person set
    fade(g) { fadeGain = Math.min(1, Math.max(0, Number(g))); applyVol(); return audio.volume; },
    xfade(secs, fin, fout) {
      if (secs === undefined || secs === null) return xfadeGain;
      xfadeSecs = Math.max(0, Number(secs) || 0); xfadeIn = !!fin; xfadeOut = !!fout;
      if (xfadeSecs <= 0) { xfadeGain = 1; applyVol(); } else if (!xfadeRaf) xfadeRaf = requestAnimationFrame(xfadeStep);
      return xfadeGain;
    },
    // even out volume: the song's own gain as a multiplier (1 = untouched)
    loud(f) { if (f === undefined || f === null) return audio.volume; loudGain = Math.min(4, Math.max(0, Number(f) || 1)); applyVol(); return audio.volume; },
    volume(v) {
      // the graphs read their own twin of the stream at full volume, so they look the same at any volume
      if (v === undefined || v === null) return baseVolume;
      const x = Math.min(1, Math.max(0, v));
      baseVolume = x; applyVol();
      try { localStorage.setItem("flacie.volume", String(x)); } catch { }
      return x;
    },
  };
})();

// Download tags ("Recently downloaded · 5s ago", see DownloadTag.razor). The age is worked out here once a second from the age the server gave when it drew the
// line, so the page is not drawn again for it and the line never moves. The wording and the 60 minute window are the server's (DownloadTags.AgeLabel) and the phone's.
// In narrow places (a card, the player bar on a narrow window, any list on a phone) the short form is used: "Downloaded · 4m ago".
window.flacieDl = (() => {
  const WINDOW = 60 * 60 * 1000, FUTURE = 60 * 1000;
  const ageLabel = (ms, compact) => {
    if (ms >= WINDOW || ms < -FUTURE) return null;
    if (ms < 5000) return "just now";
    if (ms < 60000) return Math.floor(ms / 1000) + "s ago";
    return Math.floor(ms / 60000) + (compact ? "m ago" : " min ago");
  };
  // the whole line for a download that finished ms ago: "Recently downloaded · 4 min ago" or, compact, "Downloaded · 4m ago"; null once it is over an hour
  const tagText = (ms, compact) => { const a = ageLabel(ms, compact); return a === null ? null : (compact ? "Downloaded" : "Recently downloaded") + " · " + a; };
  const narrow = window.matchMedia("(max-width: 560px)"), wide = window.matchMedia("(max-width: 900px)");
  // cards are narrow whatever the window; the player bar's text column is small below 900 px; everything else is short on a phone
  const compactFor = el => !!el.closest(".card") || (el.closest(".player") ? wide.matches : narrow.matches);
  const live = document.getElementsByClassName("dl-age");
  const tick = () => {
    if (live.length === 0) return;
    const now = performance.now();
    for (const el of live) {
      const given = el.getAttribute("data-age");
      const tag = el.closest(".dl-tag");
      // drawn again by the server: start from the age it gave this time
      if (el.__given !== given) { el.__given = given; el.__t0 = now; if (tag) tag.hidden = false; }
      const compact = compactFor(el);
      const label = ageLabel(Number(given) + (now - el.__t0), compact);
      // an hour has passed: it is a normal library song again, the line goes away
      if (label === null) { if (tag) tag.hidden = true; continue; }
      const text = el.firstChild;
      if (text && text.nodeType === 3) { if (text.nodeValue !== label) text.nodeValue = label; }
      else if (el.textContent !== label) el.textContent = label;
      const pre = tag && tag.querySelector(".dl-pre[data-compact]");
      if (pre) { const w = compact ? pre.getAttribute("data-compact") : pre.getAttribute("data-full"); if (pre.textContent !== w) pre.textContent = w; }
    }
  };
  setInterval(tick, 1000);
  // a tag that was just drawn (or drawn again with a new age) is put right at once, not up to a second later
  try { new MutationObserver(tick).observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ["data-age"] }); } catch { }
  for (const m of [narrow, wide]) try { m.addEventListener("change", tick); } catch { }
  return { ageLabel, tagText, tick };
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
