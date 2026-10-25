// The audio element for FLACie Web. The queue lives on the server (PlayerState); this plays one song at a time and
// reports time, state and the end of each song back. Media Session gives lock-screen and headphone controls.
window.flacie = (() => {
  const audio = new Audio();
  // the text colour that reads on a colour ("rgb(r, g, b)" or "#rrggbb"): near-black when the colour is light, white when it is dark
  const accentFg = (c) => {
    const m = /rgba?\((\d+),\s*(\d+),\s*(\d+)/.exec(c) || /#(..)(..)(..)/.exec(c);
    if (!m) return "#1a120c";
    const v = m.slice(1, 4).map((x) => (c.startsWith("#") ? parseInt(x, 16) : +x) / 255).map((x) => (x <= 0.03928 ? x / 12.92 : ((x + 0.055) / 1.055) ** 2.4));
    const L = 0.2126 * v[0] + 0.7152 * v[1] + 0.0722 * v[2];
    // the colour on which near-black (L 0.008) and white give the same contrast is about L 0.18
    return L > 0.18 ? "#1a120c" : "#ffffff";
  };
  window.flacieAccentFg = accentFg;
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
  // the EQ (eq(), tone(), bass()): five bands the music goes through (a bass shelf, three peaks, a treble shelf), made the first time one is moved off
  // flat; what was set last time comes back. Once the music goes through them, the analysers tap the same graph (no second copy of the song: buildGraph)
  const EQ_HZ = [100, 400, 1000, 3000, 8000];
  let toneCtx = null, eqF = null, eqAn = null, eqL = null, eqR = null, eqGains = [0, 0, 0, 0, 0], mega = false, toneRateHint = 0;
  try {
    const e = (localStorage.getItem("flacie.eq") || "").split(",").map(Number);
    if (e.length === 5 && e.every(Number.isFinite)) eqGains = e;
    else { const t = (localStorage.getItem("flacie.tone") || "").split(",").map(Number); if (t.length === 2 && t.every(Number.isFinite)) eqGains = [t[0], 0, 0, 0, t[1]]; }
  } catch { }
  const eqFlat = () => eqGains.every((g) => !g);
  const toneGraph = () => {
    if (eqF) return true;
    try {
      const AC = window.AudioContext || window.webkitAudioContext;
      // opened at the file's own rate when it is known, so the song is not resampled on its way through
      try { toneCtx = toneRateHint >= 8000 && toneRateHint <= 192000 ? new AC({ sampleRate: toneRateHint, latencyHint: "playback" }) : new AC({ latencyHint: "playback" }); } catch { toneCtx = new AC(); }
      const src = toneCtx.createMediaElementSource(audio);
      eqF = EQ_HZ.map((hz, i) => { const f = toneCtx.createBiquadFilter(); f.type = i === 0 ? "lowshelf" : i === 4 ? "highshelf" : "peaking"; f.frequency.value = hz; f.Q.value = 0.9; f.gain.value = 0; return f; });
      let at = src; for (const f of eqF) { at.connect(f); at = f; }
      at.connect(toneCtx.destination);
      // the analysers on what is heard (the earbuds, the meters, the Info tab's graphs read these instead of a second copy)
      eqAn = toneCtx.createAnalyser(); eqAn.fftSize = 2048; eqAn.smoothingTimeConstant = 0.7; at.connect(eqAn);
      const up = toneCtx.createGain(); up.channelCount = 2; up.channelCountMode = "explicit"; up.channelInterpretation = "speakers";
      const split = toneCtx.createChannelSplitter(2); eqL = toneCtx.createAnalyser(); eqR = toneCtx.createAnalyser(); eqL.fftSize = eqR.fftSize = 2048; eqL.smoothingTimeConstant = eqR.smoothingTimeConstant = 0;
      at.connect(up); up.connect(split); split.connect(eqL, 0); split.connect(eqR, 1);
      if (typeof onPlaybackGraph === "function") onPlaybackGraph();
      return true;
    } catch { toneCtx = eqF = eqAn = eqL = eqR = null; return false; }
  };
  let onPlaybackGraph = null;   // (set below: the twin is let go once the music itself can be read)
  const toneApply = () => {
    if (!eqF) return;
    if (toneCtx.state === "suspended") toneCtx.resume().catch(() => { });
    eqF.forEach((f, i) => f.gain.setTargetAtTime(eqGains[i] + (i === 0 && mega ? 9 : 0), toneCtx.currentTime, 0.04));
  };
  const eqSave = () => { try { localStorage.setItem("flacie.eq", eqGains.join(",")); localStorage.removeItem("flacie.tone"); } catch { } };
  // an EQ kept from last time is put back as the music starts (a page can only start its audio after a click or a key)
  audio.addEventListener("play", () => { if (eqF) { if (toneCtx.state === "suspended") toneCtx.resume().catch(() => { }); } else if (!eqFlat() && toneGraph()) toneApply(); });
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
  // __flaciePlayingAt: when sound last started, for the song-start timing test (1z-song-start)
  audio.addEventListener("playing", () => { failures = 0; window.__flaciePlayingAt = performance.now(); });
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
          const el = items[c], panel = document.querySelector(".ls-panel, .np-panel");
          if (el && panel) panel.scrollTo({ top: el.offsetTop - panel.clientHeight / 2 + el.clientHeight / 2, behavior: "smooth" });
        }
      }
    }
    // every frame while synced lyrics play; otherwise four times a second is plenty (a loop on every frame keeps the browser drawing frames on every page)
    if (lyr && !audio.paused) requestAnimationFrame(lyricTick); else setTimeout(lyricTick, 250);
  };
  requestAnimationFrame(lyricTick);
  // Live views for the Info tab: spectrum, bars, spectrogram, loudness, stereo. Playback itself goes straight from the audio element to the
  // output (no Web Audio in its path, so the browser never resamples it to a fixed rate). The graphs read a silent twin of the same stream
  // through an AudioContext opened at the file's own sample rate, so the spectrum reaches the file's real Nyquist. Not done on iOS.
  // Once the music itself goes through Web Audio (the EQ or Mega Bass is on), the graphs read that instead, and the twin (a second download) is let go.
  let npWaveRaf = 0, npWaveBusy = false, waveChain = Promise.resolve(), waveNow = '';
  const waveCache = new Map(), waveAsking = new Map();   // shapes kept for the songs about to play, and the ones being asked for
  let actx = null, an = null, anL = null, anR = null, twin = null, vizOn = false, vizStyle = "curve", vs = {}, vh = null, vp = null, cp = null, ctxKey = "", vizCut = 0, vizCutLabel = "";
  const ios = /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  const AC = window.AudioContext || window.webkitAudioContext;
  const closeGraph = () => {
    if (twin) { try { twin.pause(); twin.removeAttribute("src"); twin.load(); } catch { } twin = null; }
    if (actx && actx !== toneCtx) { try { actx.close(); } catch { } }   // (the playback graph is the music itself: never closed here)
    actx = an = anL = anR = null; ctxKey = "";
  };
  onPlaybackGraph = () => { if (twin || (actx && actx !== toneCtx)) closeGraph(); };
  const syncTwin = () => {
    if (!twin || !audio.src) return;
    if (twin.src !== audio.src) twin.src = audio.src;
    if (Math.abs(twin.currentTime - audio.currentTime) > 0.3) { try { twin.currentTime = audio.currentTime; } catch { } }
    if (twin.playbackRate !== audio.playbackRate) twin.playbackRate = audio.playbackRate;
    if (audio.paused !== twin.paused) { if (audio.paused) twin.pause(); else twin.play().catch(() => { }); }
  };
  ["play", "pause", "seeked", "loadstart", "ratechange"].forEach(ev => audio.addEventListener(ev, syncTwin));
  // the twin is a second download of the song: it stops while the tab is hidden (nothing is drawn then), and the graphs open it again when they are shown
  document.addEventListener("visibilitychange", () => { if (document.hidden && actx) closeGraph(); });
  let vizRate = 0;   // the rate the Info tab's graph was opened at (to open it again)
  const buildGraph = (rate) => {
    // the music itself goes through Web Audio (the EQ): read it there, one download
    if (eqF) { if (actx !== toneCtx) { closeGraph(); actx = toneCtx; an = eqAn; anL = eqL; anR = eqR; ctxKey = "play"; } return true; }
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
  let deskFrame = null, deskSync = 0;   // the iPod desk's frame (vizFrame)
  const clockOut = [0, 0, true];
  const freqData = () => { const d = new Uint8Array(an.frequencyBinCount); an.getByteFrequencyData(d); return d; };
  const fill = (g, x, y, w, h, r, color, alpha) => { g.globalAlpha = alpha; g.fillStyle = color; g.beginPath(); g.roundRect(x, y, w, h, r); g.fill(); g.globalAlpha = 1; };
  // Design I's graphs: drawn in the cover's colours (accent, tone, ground from the player, flacie.npAccent), labels in DM Mono, soft rounded shapes, no hard boxes
  const rgbArr = (s) => { s = (s || "").trim(); if (s[0] === "#") { const n = parseInt(s.length === 4 ? s.slice(1).replace(/./g, "$&$&") : s.slice(1, 7), 16); return [n >> 16 & 255, n >> 8 & 255, n & 255]; } const m = s.match(/[\d.]+/g); return m ? m.slice(0, 3).map(Number) : [255, 255, 255]; };
  const rgba = (s, a) => { const [r, g, b] = rgbArr(s); return `rgba(${r},${g},${b},${a})`; };
  // the spectrogram's colours: the ground, through the tone and the cover's colour, to near white; built again only when the cover changes
  let lutKey = "", lut = null;
  const lutFor = (c) => {
    const key = c.ground + c.tone + c.accent; if (key === lutKey) return lut;
    const gr = rgbArr(c.ground), tn = rgbArr(c.tone), ac = rgbArr(c.accent);
    const stops = [[0, ...gr.map(v => v * .5)], [.3, ...tn], [.65, ...ac], [1, ...ac.map(v => v + (255 - v) * .8)]], out = new Uint8ClampedArray(256 * 3);
    for (let i = 0; i < 256; i++) {
      const t = i / 255; let k = 1; while (k < stops.length - 1 && stops[k][0] < t) k++;
      const a = stops[k - 1], b = stops[k], u = (t - a[0]) / (b[0] - a[0]);
      for (let q = 0; q < 3; q++) out[i * 3 + q] = a[q + 1] + (b[q + 1] - a[q + 1]) * u;
    }
    lutKey = key; lut = out; return out;
  };
  const labelFont = (f, size = 10.5) => { f.g.font = `500 ${size * f.dpr}px 'DM Mono', ui-monospace, monospace`; f.g.textBaseline = "alphabetic"; f.g.fillStyle = f.c.dim; };
  const hzLabel = (k) => k === 0 ? "0" : (k / 1000) + "K";

  // ---- spectrum: how loud each pitch is, 0 Hz on the left to the file's top on the right: a glowing line over a soft fill, the slowly falling peaks as dots ----
  const drawCurve = (f) => {
    const { g, w, h, dpr, c } = f, data = freqData(), nyq = actx.sampleRate / 2, bins = data.length;
    const maxHz = nyq, stepHz = maxHz > 48000 ? 20000 : maxHz > 26000 ? 10000 : 5000, use = Math.floor(bins * maxHz / nyq), bottom = h - 20 * dpr, top = 10 * dpr, span = bottom - top;
    if (!cp || cp.length !== use) cp = new Float32Array(use);
    g.clearRect(0, 0, w, h); labelFont(f); g.lineWidth = dpr;
    for (let k = 0; k <= maxHz; k += stepHz) {
      const x = Math.min(w - 1, k / maxHz * w);
      g.fillStyle = rgba(c.ink, .14); for (let y = top; y < bottom; y += 6 * dpr) g.fillRect(x, y, dpr, 2 * dpr);   // dotted guides
      g.fillStyle = c.dim; g.textAlign = k === 0 ? "left" : "center"; g.fillText(hzLabel(k), k === 0 ? 2 : Math.min(x, w - 14 * dpr), h - 4 * dpr);
    }
    if (vizCut > 0 && maxHz > vizCut) { // where an encoder at this file's bitrate usually cuts the sound off
      const x = vizCut / maxHz * w; g.fillStyle = rgba(c.ink, .45); for (let y = top; y < bottom; y += 6 * dpr) g.fillRect(x, y, dpr, 3 * dpr);
      g.textAlign = "right"; g.fillStyle = c.ink; g.fillText((vizCutLabel || "usually ends here").toUpperCase(), x - 6 * dpr, top + 8 * dpr);
    }
    const pts = [];
    for (let i = 0; i < use; i++) { const v = data[i] / 255; cp[i] = Math.max(v, cp[i] - 0.004); pts.push([i / (use - 1) * w, bottom - Math.pow(v, 1.15) * span]); }
    const path = (p) => { g.moveTo(p[0][0], p[0][1]); for (let i = 1; i < p.length - 1; i++) { const mx = (p[i][0] + p[i + 1][0]) / 2, my = (p[i][1] + p[i + 1][1]) / 2; g.quadraticCurveTo(p[i][0], p[i][1], mx, my); } };
    const grad = g.createLinearGradient(0, top, 0, bottom); grad.addColorStop(0, rgba(c.accent, .55)); grad.addColorStop(1, rgba(c.accent, 0));
    g.beginPath(); path(pts); g.lineTo(w, bottom); g.lineTo(0, bottom); g.closePath(); g.fillStyle = grad; g.fill();
    g.save(); g.shadowColor = c.accent; g.shadowBlur = 12 * dpr; g.beginPath(); path(pts); g.strokeStyle = c.accent; g.lineWidth = 2.5 * dpr; g.lineJoin = "round"; g.stroke(); g.restore();
    // peaks: a dot every few pixels
    const every = Math.max(1, Math.round(use / (w / (5 * dpr)))); g.fillStyle = rgba(c.ink, .75);
    for (let i = 0; i < use; i += every) { const y = bottom - Math.pow(cp[i], 1.15) * span; if (y < bottom - 2 * dpr) { g.beginPath(); g.arc(i / (use - 1) * w, y, 1.3 * dpr, 0, 7); g.fill(); } }
    g.fillStyle = rgba(c.ink, .18); g.fillRect(0, bottom, w, dpr);
  };

  // ---- visualizer: rounded bars mirrored about a centre line, deep sounds left, high right, each with a floating peak dot ----
  const drawLeds = (f) => {
    const { g, w, h, dpr, c } = f, data = freqData(), nyq = actx.sampleRate / 2, bins = data.length;
    const N = Math.max(20, Math.min(64, Math.floor(w / (10 * dpr)))), lo = 35, hi = Math.min(nyq, 18000);
    if (!vh || vh.length !== N) { vh = new Float32Array(N); vp = new Float32Array(N); }
    g.clearRect(0, 0, w, h);
    const slot = w / N, bw = Math.max(3 * dpr, slot * 0.62), mid = (h - 16 * dpr) / 2, half = mid - 6 * dpr;
    const grad = g.createLinearGradient(0, mid - half, 0, mid + half); grad.addColorStop(0, rgba(c.ink, .95)); grad.addColorStop(.35, c.accent); grad.addColorStop(.5, c.accent); grad.addColorStop(.65, c.accent); grad.addColorStop(1, rgba(c.ink, .95));
    for (let i = 0; i < N; i++) {
      const f0 = lo * Math.pow(hi / lo, i / N), f1 = lo * Math.pow(hi / lo, (i + 1) / N);
      const b0 = Math.floor(f0 / nyq * bins), b1 = Math.max(b0 + 1, Math.ceil(f1 / nyq * bins));
      let m = 0; for (let k = b0; k < b1 && k < bins; k++) m = Math.max(m, data[k]);
      const v = Math.min(1, Math.pow(m / 255, 1.5) * (1 + 0.7 * i / N)); // the highs carry less energy in real music, so they are lifted a little
      vh[i] = Math.max(v, vh[i] - 0.035); vp[i] = Math.max(vh[i], vp[i] - 0.008);
      const x = i * slot + (slot - bw) / 2, bh = Math.max(bw, vh[i] * half);
      g.fillStyle = rgba(c.ink, .06); g.beginPath(); g.roundRect(x, mid - half, bw, half * 2, bw / 2); g.fill();
      g.fillStyle = grad; g.beginPath(); g.roundRect(x, mid - bh, bw, bh * 2, bw / 2); g.fill();
      if (vp[i] > .03) { g.fillStyle = c.ink; const py = vp[i] * half + 4 * dpr; g.beginPath(); g.arc(x + bw / 2, mid - py, bw / 2.4, 0, 7); g.arc(x + bw / 2, mid + py, bw / 2.4, 0, 7); g.fill(); }
    }
    labelFont(f); g.textAlign = "left"; g.fillText("BASS", 0, h - 3 * dpr); g.textAlign = "center"; g.fillText("MID", w / 2, h - 3 * dpr); g.textAlign = "right"; g.fillText("TREBLE", w, h - 3 * dpr);
  };

  // ---- spectrogram: a waterfall in the cover's colours. Pitch up the side, newest sound at the right edge, scrolling left as the song plays ----
  const drawWall = (f) => {
    const { g, w, h, dpr, c } = f, x0 = 40 * dpr, x1 = w, y0 = 2 * dpr, y1 = h - 20 * dpr, pw = Math.round(x1 - x0), ph = Math.round(y1 - y0), speed = 60 * dpr, L = lutFor(c);
    if (!vs.buf || vs.buf.width !== pw || vs.buf.height !== ph) { vs.buf = document.createElement("canvas"); vs.buf.width = pw; vs.buf.height = ph; vs.last = f.ts; vs.acc = 0; const b = vs.buf.getContext("2d"); b.fillStyle = `rgb(${L[0]},${L[1]},${L[2]})`; b.fillRect(0, 0, pw, ph); }
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
        bg.fillStyle = `rgb(${L[i * 3]},${L[i * 3 + 1]},${L[i * 3 + 2]})`; bg.fillRect(pw - step, y, step, 1);
      }
    }
    g.clearRect(0, 0, w, h);
    g.save(); g.beginPath(); g.roundRect(x0, y0, pw, ph, 12 * dpr); g.clip(); g.drawImage(vs.buf, x0, y0);
    for (let k = stepHz; k <= maxHz; k += stepHz) { const y = y1 - k / maxHz * ph; g.fillStyle = rgba(c.ink, k === 16000 || k === 20000 ? .4 : .14); for (let x = x0; x < x1; x += 6 * dpr) g.fillRect(x, y, 2 * dpr, dpr); }
    g.restore();
    labelFont(f); g.textAlign = "right"; g.textBaseline = "middle";
    for (let k = 0; k <= maxHz; k += stepHz) { const y = y1 - k / maxHz * ph; g.fillStyle = k === 16000 || k === 20000 ? c.ink : c.dim; g.fillText(hzLabel(k), x0 - 8 * dpr, Math.min(y1 - 5 * dpr, Math.max(y0 + 6 * dpr, y))); }
    g.textBaseline = "alphabetic"; g.fillStyle = c.dim; g.textAlign = "left"; g.fillText(`${Math.round(pw / speed)} S AGO`, x0, h - 4 * dpr); g.textAlign = "right"; g.fillText("NOW", x1, h - 4 * dpr);
  };

  // ---- loudness: the level right now as a big number and a row of lit capsules with a peak tick, then the last ten seconds as a soft hill ----
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
    const num = vs.r <= -59 ? "−∞" : vs.r.toFixed(1);
    g.textBaseline = "alphabetic"; g.textAlign = "left"; g.fillStyle = c.ink; g.font = `500 ${34 * dpr}px 'DM Mono', ui-monospace, monospace`;
    g.fillText(num, 0, 34 * dpr); const tw = g.measureText(num).width;
    labelFont(f, 11); g.fillText("DB AVERAGE NOW", tw + 10 * dpr, 32 * dpr);
    g.textAlign = "right"; g.fillStyle = c.ink; g.fillText(`PEAK ${vs.ph <= -89 ? "−∞" : vs.ph.toFixed(1)} DB`, w, 32 * dpr);
    // meter: capsules that light up to the level; the last few turn warm near 0 dB
    const my = 48 * dpr, mh = 14 * dpr, n = Math.max(20, Math.floor(w / (9 * dpr))), cw = w / n, lit = X(vs.r) * n, pki = Math.min(n - 1, Math.floor(X(vs.ph) * n));
    for (let i = 0; i < n; i++) {
      const hot = i >= n * .88, col = i < lit ? (hot ? "#ff7a5c" : c.accent) : rgba(c.ink, .08);
      g.fillStyle = i === pki ? c.ink : col; g.beginPath(); g.roundRect(i * cw + dpr, my, cw - 2.5 * dpr, mh, 3 * dpr); g.fill();
    }
    // history
    const hy0 = my + mh + 18 * dpr, hy1 = h - 20 * dpr, hh = hy1 - hy0; labelFont(f);
    g.textAlign = "left";
    for (const d of [-12, -24, -36, -48]) { const y = hy1 - X(d) * hh; g.fillStyle = rgba(c.ink, .12); for (let x = 0; x < w; x += 6 * dpr) g.fillRect(x, y, 2 * dpr, dpr); g.fillStyle = c.dim; g.fillText(d + "", 2, y - 4 * dpr); }
    if (hist.length > 1) {
      const px = (i) => w - (hist.length - 1 - i) / 299 * w, py = (i) => hy1 - X(hist[i]) * hh, gr = g.createLinearGradient(0, hy0, 0, hy1); gr.addColorStop(0, rgba(c.accent, .6)); gr.addColorStop(1, rgba(c.accent, 0));
      g.beginPath(); g.moveTo(px(0), hy1); for (let i = 0; i < hist.length; i++) g.lineTo(px(i), py(i)); g.lineTo(px(hist.length - 1), hy1); g.closePath(); g.fillStyle = gr; g.fill();
      g.save(); g.shadowColor = c.accent; g.shadowBlur = 10 * dpr; g.beginPath(); for (let i = 0; i < hist.length; i++) i ? g.lineTo(px(i), py(i)) : g.moveTo(px(i), py(i)); g.strokeStyle = c.accent; g.lineWidth = 2.5 * dpr; g.lineJoin = "round"; g.stroke(); g.restore();
    }
    labelFont(f); g.textAlign = "left"; g.fillText("10 S AGO", 0, h - 4 * dpr); g.textAlign = "right"; g.fillText("NOW", w, h - 4 * dpr);
  };

  // ---- stereo: a phase scope (what left and right do together) in a soft disc, beside a capsule meter for each side ----
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
    sg.globalCompositeOperation = "destination-out"; sg.fillStyle = "rgba(0,0,0,.2)"; sg.fillRect(0, 0, s, s); sg.globalCompositeOperation = "source-over";
    if (f.playing) { sg.fillStyle = c.accent; for (let i = 0; i < bl.length; i += 2) { const l = bl[i], r = br[i], x = gx + (r - l) * 0.7071 * (R * 1.2), y = gx - (l + r) * 0.7071 * (R * 1.2); sg.globalAlpha = .75; sg.beginPath(); sg.arc(x, y, dpr, 0, 7); sg.fill(); } sg.globalAlpha = 1; }
    g.clearRect(0, 0, w, h);
    const disc = g.createRadialGradient(cx, cy, 0, cx, cy, R); disc.addColorStop(0, rgba(c.tone, .9)); disc.addColorStop(1, rgba(c.tone, .25));
    g.fillStyle = disc; g.beginPath(); g.arc(cx, cy, R, 0, Math.PI * 2); g.fill();
    g.lineWidth = dpr; g.strokeStyle = rgba(c.ink, .14); g.setLineDash([3 * dpr, 4 * dpr]);
    g.beginPath(); g.moveTo(cx, cy - R); g.lineTo(cx, cy + R); g.moveTo(cx - R, cy); g.lineTo(cx + R, cy); g.moveTo(cx - R * .7071, cy - R * .7071); g.lineTo(cx + R * .7071, cy + R * .7071); g.moveTo(cx + R * .7071, cy - R * .7071); g.lineTo(cx - R * .7071, cy + R * .7071); g.stroke(); g.setLineDash([]);
    g.drawImage(vs.sc, 0, (h - s) / 2);
    labelFont(f); g.fillStyle = c.ink; g.textAlign = "center"; g.fillText("L", cx - R * .7071 - 8 * dpr, cy - R * .7071 - 2 * dpr); g.fillText("R", cx + R * .7071 + 8 * dpr, cy - R * .7071 - 2 * dpr);
    // the two level meters: stacks of capsules
    const mx = s + 20 * dpr, mw = Math.max(14 * dpr, Math.min(30 * dpr, (w - mx - 70 * dpr) / 2)), mt = 6 * dpr, mb = h - 22 * dpr, mhh = mb - mt, X = (d) => Math.max(0, Math.min(1, (d + 60) / 60));
    const rows = Math.max(10, Math.floor(mhh / (7 * dpr))), rh = mhh / rows;
    [[vs.sm[0], vs.pk[0], "L", c.accent], [vs.sm[1], vs.pk[1], "R", c.ink]].forEach(([v, p, name, col], i) => {
      const x = mx + i * (mw + 12 * dpr), lit = X(v) * rows, pr = Math.min(rows - 1, Math.floor(X(p) * rows));
      for (let r = 0; r < rows; r++) { const y = mb - (r + 1) * rh; g.fillStyle = r === pr ? c.ink : r < lit ? col : rgba(c.ink, .07); g.beginPath(); g.roundRect(x, y + dpr, mw, rh - 2.5 * dpr, 3 * dpr); g.fill(); }
      labelFont(f); g.fillStyle = c.ink; g.textAlign = "center"; g.fillText(name, x + mw / 2, h - 6 * dpr);
    });
    const tx = mx + 2 * (mw + 12 * dpr) + 2 * dpr; labelFont(f); g.textAlign = "left";
    for (const d of [0, -12, -24, -36, -48]) { const y = mb - X(d) * mhh; g.fillText(d + "", tx, y + 4 * dpr); }
  };

  const views = { curve: drawCurve, leds: drawLeds, wall: drawWall, loud: drawLoud, stereo: drawStereo };
  let lastSync = 0;
  const vizDraw = (ts) => {
    if (!vizOn) return;
    requestAnimationFrame(vizDraw);
    const cv = document.getElementById("viz"); if (!cv) return;
    if (!an && !buildGraph(vizRate)) return;   // (closed while the tab was hidden)
    if (ts - lastSync > 500) { lastSync = ts; syncTwin(); }
    const dpr = window.devicePixelRatio || 1, w = Math.round(cv.clientWidth * dpr), h = Math.round(cv.clientHeight * dpr);
    if (!w || !h) return;
    if (cv.width !== w || cv.height !== h) { cv.width = w; cv.height = h; vs = {}; }
    // the colours are read off the graph itself, so inside the player they are the cover's (accent, tone, ground)
    const css = getComputedStyle(cv), v = (n, d) => css.getPropertyValue(n).trim() || d;
    views[vizStyle](Object.assign({ g: cv.getContext("2d"), w, h, dpr, ts, playing: !audio.paused, c: { accent: v("--accent", "#ff4d8d"), dim: v("--ink-2", "") || v("--faint", "#8a8a94"), ink: v("--ink", "#f4f3f7"), tone: v("--tone", "#2a2a33"), ground: v("--ground", "#0b0b0e") } }));
  };
  // a device lying turned (the Desk look tilts it a few degrees): its angle, from the CSS rotate of it and what holds it; and the zoom it is drawn at (the page's fit)
  const tiltOf = el => { let a = 0; for (let n = el; n && n.nodeType === 1; n = n.parentElement) { const m = /^(-?[\d.]+)deg$/.exec(getComputedStyle(n).rotate || ""); if (m) a += +m[1]; } return a; };
  const zoomOf = el => { let z = 1; for (let n = el; n && n.nodeType === 1; n = n.parentElement) { const v = parseFloat(getComputedStyle(n).zoom); if (v > 0) z *= v; } return z; };
  // the iPod screens' long text (as on a real iPod): a line too long (the title bar, the highlighted row, Now Playing) for the screen scrolls inside itself, pausing at both ends (ipod.css: .mq, from how far,
  // --mq, and how long, --mqt; still, with an ellipsis, when motion is reduced). A line is measured again only when its text changes, the highlighted row
  // moves, or the screen's size changes, so the clock and the progress bar ticking never restart it
  const MQ = ".lcd-hdr b, .lcd-pane b, .lcd-pane span, .lcd-pane small, .ios-nav b, .lcd-list li.sel .lcd-label, .lnp-meta b, .lnp-meta span, .cf-cap b, .cf-cap span, .ios-np-meta b, .ios-np-meta span, .ios-np-meta small, .ios-mini-t b";
  const marquee = (root, sel = MQ) => {
    // (a line that scrolls is marked data-mq, an attribute the page never writes: a class would be wiped whenever the screen is drawn again, every second on
    // the touch iPods, and the scroll started over each time, a shake and half a second of movement. A line whose text and width are as they were keeps scrolling)
    const measure = e => {
      const key = e.matches(sel) ? e.textContent + "|" + e.clientWidth : "";
      if (key && key === e._mqKey && e.hasAttribute("data-mq")) return;
      e._mqKey = key;
      if (e.hasAttribute("data-mq")) e.removeAttribute("data-mq");
      const over = key ? e.scrollWidth - e.clientWidth : 0;
      // (the text slides inside a wrapper of its own, moved by the GPU: an indent changed on every frame waits for the page's main thread, which on a busy
      // desk drew no steps in between, so the text sat still and then jumped to its end. The page's own text node is moved in, not copied, so it keeps updating)
      let w = e.firstElementChild && e.firstElementChild.classList.contains("mq-in") ? e.firstElementChild : null;
      if (over > 2) {
        if (!w) { w = document.createElement("span"); w.className = "mq-in"; while (e.firstChild) w.appendChild(e.firstChild); e.appendChild(w); }
        e.style.setProperty("--mq", -(over + 6) + "px"); e.style.setProperty("--mqt", (4 + over / 30).toFixed(1) + "s"); e.style.setProperty("--mqn", String(Math.round(over + 6))); void e.offsetWidth; e.setAttribute("data-mq", "");
      } else if (w) { while (w.firstChild) e.insertBefore(w.firstChild, w); w.remove(); }
    };
    // (measuring changes a line's class, which the observer below sees: those changes are taken off its list, or every line measured itself again on every
    // frame for as long as the screen was open, and a long one started its scroll over each time)
    const all = () => { root.querySelectorAll("[data-mq]").forEach((e) => { e._mqKey = null; measure(e); }); root.querySelectorAll(sel).forEach((e) => { e._mqKey = null; measure(e); }); mo.takeRecords(); };
    let queued = new Set(), frame = 0;
    const seen = rs => { for (const r of rs) { touched(r.target); r.addedNodes.forEach(touched); } };
    const flush = () => { frame = 0; seen(mo.takeRecords()); const q = queued; queued = new Set(); q.forEach(measure); mo.takeRecords(); };
    const touched = n => {
      const el = n.nodeType === 1 ? n : n.parentElement; if (!el) return;
      // a line whose text changed; or, a row that became (or stopped being) the highlighted one, its label
      const line = el.closest(sel); if (line) queued.add(line);
      if (el.matches && el.matches(".lcd-list li")) { const l = el.querySelector(".lcd-label"); if (l) queued.add(l); }
      el.querySelectorAll && el.querySelectorAll(sel).forEach(x => queued.add(x));
      if (!frame) frame = requestAnimationFrame(flush);
    };
    const mo = new MutationObserver(seen);
    mo.observe(root, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ["class"] });
    let w = root.clientWidth, h = root.clientHeight;
    const ro = new ResizeObserver(() => { if (root.clientWidth !== w || root.clientHeight !== h) { w = root.clientWidth; h = root.clientHeight; all(); } });
    ro.observe(root); all();
    return { stop() { mo.disconnect(); ro.disconnect(); if (frame) cancelAnimationFrame(frame); } };
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
  // / jumps to the search box; ? lists the shortcuts (a small dialog, Escape or a click outside closes it). Not while typing.
  const shortcutList = [["Space", "Play or pause"], ["← / →", "Back or forward 5 seconds"], ["N", "Next song"], ["P", "Previous song"], ["/", "Search"], ["?", "This list"], ["Esc", "Close the player or a menu"]];
  const showShortcuts = () => {
    let d = document.getElementById("kb-help");
    if (d) { d.remove(); return; }
    d = document.createElement("div");
    d.id = "kb-help"; d.className = "kb-help"; d.setAttribute("role", "dialog"); d.setAttribute("aria-modal", "true"); d.setAttribute("aria-label", "Keyboard shortcuts"); d.tabIndex = -1;
    const box = document.createElement("div"); box.className = "kb-box";
    const h = document.createElement("h2"); h.textContent = "Keyboard shortcuts"; box.appendChild(h);
    const dl = document.createElement("dl");
    for (const [k, v] of shortcutList) { const dt = document.createElement("dt"); const kb = document.createElement("kbd"); kb.textContent = k; dt.appendChild(kb); const dd = document.createElement("dd"); dd.textContent = v; dl.append(dt, dd); }
    box.appendChild(dl); d.appendChild(box);
    d.addEventListener("pointerdown", e => { if (e.target === d) d.remove(); });
    document.body.appendChild(d); d.focus();
  };
  document.addEventListener("keydown", e => {
    if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return;
    const t = e.target, tag = t && t.tagName ? t.tagName.toLowerCase() : "";
    if (e.key === "Escape") { document.getElementById("kb-help")?.remove(); return; }
    if (tag === "input" || tag === "textarea" || tag === "select" || (t && t.isContentEditable)) return;
    if (e.key === "/") { const i = document.querySelector(".top-search input"); if (i) { e.preventDefault(); i.focus(); i.select(); } }
    else if (e.key === "?") { e.preventDefault(); showShortcuts(); }
  });
  // Back (or Forward) to a page returns to the place in it: each page's scroll is kept (this tab only) and put back once the page is tall enough again
  (() => {
    const KEY = "flacie.scroll";
    const read = () => { try { return JSON.parse(sessionStorage.getItem(KEY) || "{}"); } catch { return {}; } };
    const here = () => location.pathname + location.search;
    let restoring = 0, saveTimer = 0;
    addEventListener("scroll", () => {
      if (restoring || saveTimer) return;
      saveTimer = setTimeout(() => { saveTimer = 0; if (restoring) return; const m = read(); m[here()] = scrollY; try { sessionStorage.setItem(KEY, JSON.stringify(m)); } catch { } }, 200);
    }, { passive: true });
    addEventListener("popstate", () => {
      const y = read()[here()];
      if (!(y > 0)) return;
      const until = Date.now() + 3000, id = ++restoring;
      const go = () => {
        if (id !== restoring) return;
        if (document.documentElement.scrollHeight >= y + innerHeight * 0.5) { scrollTo(0, y); setTimeout(() => { if (id === restoring) { scrollTo(0, y); restoring = 0; } }, 250); return; }
        if (Date.now() < until) setTimeout(go, 100); else restoring = 0;
      };
      setTimeout(go, 60);
    });
  })();
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
  // A song the pointer rests on (150 ms, so a sweep across a shelf fetches nothing) or the keyboard reaches: its start is fetched as an early read, which the
  // server keeps, so pressing it starts at once instead of after the server's first byte (3 s from Listen again on the owner's server). Once per song, not on
  // mobile data: one megabyte for a song someone is about to press.
  const warmed = new Set(); let warmTimer = 0;
  const warm = (u) => {
    if (!u || warmed.has(u) || window.flacie?.metered?.()) return;
    if (warmed.size > 40) warmed.clear();
    warmed.add(u);
    fetch(u + (u.includes("?") ? "&" : "?") + "pre=1", { credentials: "same-origin", headers: { Range: "bytes=0-1048575" } }).then(r => r.arrayBuffer()).catch(() => { });
  };
  const warmFrom = (e) => { const el = e.target?.closest?.("[data-warm]"); clearTimeout(warmTimer); if (el) warmTimer = setTimeout(() => warm(el.dataset.warm), e.type === "focusin" ? 0 : 150); };
  document.addEventListener("pointerover", warmFrom, { passive: true });
  document.addEventListener("focusin", warmFrom);
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
      vizStyle = views[style] ? style : "curve"; vs = {}; vizRate = rate | 0;
      if (!buildGraph(vizRate) || !an) return false;
      if (!vizOn) { vizOn = true; requestAnimationFrame(vizDraw); }
      return true;
    },
    vizStop() { vizOn = false; closeGraph(); },
    // the iPod desk's earbuds: one frame of the same analyser (the graph is built on first use and follows the song); null where it can't run.
    // Called every frame: the same buffer each time, and the silent twin is put back in step twice a second (play, pause and seek do it at once anyway).
    vizFrame() {
      // (opened at the file's own rate when it is known, so the spectrum reaches half of it: 48 kHz for a 96 kHz file)
      if (!buildGraph(toneRateHint) || !an) return null;
      // a context opened before any click (a reload in the Desk look) stays suspended: ask again once the song plays, which a click started
      if (actx.state === "suspended" && !audio.paused) actx.resume().catch(() => { });
      const now = performance.now(); if (now - deskSync > 500) { deskSync = now; syncTwin(); }
      if (!deskFrame || deskFrame.freq.length !== an.frequencyBinCount) deskFrame = { freq: new Uint8Array(an.frequencyBinCount), playing: false };
      an.getByteFrequencyData(deskFrame.freq); deskFrame.playing = !audio.paused; deskFrame.rate = actx.sampleRate; deskFrame.fileRate = toneRateHint;
      return deskFrame;
    },
    vizRelease() { if (!vizOn) closeGraph(); },
    // the song's own clock, read in the page (the iPod desk's journal times its lyrics with it): [position s, length s, paused], the same array each time
    clock() { clockOut[0] = audio.currentTime || 0; clockOut[1] = isFinite(audio.duration) ? audio.duration : 0; clockOut[2] = audio.paused; return clockOut; },
    // mobile data (or Data Saver): the graphs' second copy of the song is not fetched without asking. Chrome on Android says so; elsewhere nothing is reported (not metered)
    metered() { const c = navigator.connection; return !!c && (c.saveData === true || c.type === "cellular"); },
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
    panelTop() { const p = document.querySelector(".ls-panel, .np-panel"); if (p) p.scrollTop = 0; },
    // is the player laid out as a phone (tabs open a sheet over the cover)?
    sheetMode() { return window.matchMedia("(max-width: 760px)").matches; },
    toggle() { audio.paused ? audio.play().catch(() => {}) : audio.pause(); },
    play() { audio.play().catch(() => send("OnState", false)); },
    setLyrics(times) { lyr = { times, cur: -2 }; },
    clearLyrics() { lyr = null; },
    // keeps the line being sung in the middle of the lyrics panel, scrolling only that panel (never the page behind it)
    scrollLyric() {
      const el = document.querySelector("#lyrics .cur"), panel = document.querySelector(".ls-panel, .np-panel");
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
    // Living Sleeve: the full-screen player's two colours, read from the cover once per cover (a 24 px copy, so it costs nothing). The main colour is the
    // most vivid one in the picture, lifted until dark text on it is easy to read (the play button); the second is the next most common hue, for the other field.
    // Design I's seek bar: the song's own loudness as bars (from /api/wave, worked out once), the played part in the cover's colour, starting at 12 o'clock
    // and going clockwise. Two shapes, whichever is on screen: on a phone a RING round the play button (bars hanging in from its outer edge); on a PC a FRAME
    // round the cover (bars standing out from a rounded square just outside the picture). The shape stays still; only the played part moves.
    // A press or drag on the bars seeks: the range input in the ring gets the value (input while dragging, change on release).
    // Redrawn four times a second while the player is open and the tab is shown, and at once when the size changes or while dragging.
    npWave(cv, url, box, fcv, fbox) {
        flacie.npWaveStop();
        if (!cv) return;
        const np = cv.closest('.ls, .np'), input = box?.querySelector('input[type=range]');
        let shape = null, last = 0, lastPlayed = -1, lastKey = '', drag = null;
        const got = (a) => { if (cv.isConnected && Array.isArray(a) && a.length) { shape = a; lastPlayed = -1; } };
        if (url) { waveNow = url; const known = waveCache.get(url); if (known) got(known); else flacie.npWaveGet(url).then(got); }
        // a loudly mastered song barely changes level, so its bars are stretched between its own quiet and loud parts (the 3rd and 97th percentiles):
        // the shape always shows where it builds and drops, instead of a ring of equal bars
        let lo = 0, hi = 255, spanFor = null;
        const span = () => { if (spanFor === shape) return; spanFor = shape; const v = [...shape].sort((x, y) => x - y); lo = v[Math.floor(v.length * .03)]; hi = Math.max(lo + 1, v[Math.floor(v.length * .97)]); };
        // the frame is a loop: the last bar stands beside the first at 12 o'clock, so the bars either side of that seam (about 6% each) ease towards the height
        // where the two ends meet, and a loud ending never jumps to a quiet start (the Android app does the same, WaveBars.blendSeam)
        let seamFor = null, seamN = 0, seamH = null;
        const levelAt = (i, n) => {
            if (seamFor !== shape || seamN !== n) {
                seamFor = shape; seamN = n; const h = Array.from({ length: n }, (_, k) => rawLevel(k, n));
                const k = Math.max(3, Math.floor(n * .06)), meet = (h[0] + h[n - 1]) / 2; seamH = h.slice();
                for (let j = 0; j < k && n >= 8; j++) { const w = .5 * (1 + Math.cos(Math.PI * j / k)); seamH[j] = h[j] + (meet - h[j]) * w; seamH[n - 1 - j] = h[n - 1 - j] + (meet - h[n - 1 - j]) * w; }
            }
            return seamH[i];
        };
        const rawLevel = (i, n) => { if (!shape) return .4; span(); const a = Math.floor(i / n * shape.length), b = Math.max(a + 1, Math.floor((i + 1) / n * shape.length)); let m = 0; for (let k = a; k < b; k++) m = Math.max(m, shape[k]); return .12 + .88 * Math.min(1, Math.max(0, (m - lo) / (hi - lo))); };
        // the frame's geometry, in CSS pixels: the cover's square (side S, corner r) grown by a gap; walk(t) gives the point and outward normal at a fraction t of the way round
        const frameGeo = () => {
            const W = fcv.clientWidth, F = parseFloat(getComputedStyle(fbox).getPropertyValue('--frame')) || 40, S = W - 2 * F, gap = 8, a = S / 2 + gap, rr = 26 + gap, side = 2 * a - 2 * rr, arc = Math.PI / 2 * rr;
            const P = 4 * side + 4 * arc;
            const walk = (t) => {
                let d = ((t % 1) + 1) % 1 * P;
                const segs = [[side / 2, (u) => [u, -a, 0, -1]],
                    [arc, (u) => { const q = -Math.PI / 2 + u / rr; return [a - rr + rr * Math.cos(q), -a + rr + rr * Math.sin(q), Math.cos(q), Math.sin(q)]; }],
                    [side, (u) => [a, -a + rr + u, 1, 0]],
                    [arc, (u) => { const q = u / rr; return [a - rr + rr * Math.cos(q), a - rr + rr * Math.sin(q), Math.cos(q), Math.sin(q)]; }],
                    [side, (u) => [a - rr - u, a, 0, 1]],
                    [arc, (u) => { const q = Math.PI / 2 + u / rr; return [-a + rr + rr * Math.cos(q), a - rr + rr * Math.sin(q), Math.cos(q), Math.sin(q)]; }],
                    [side, (u) => [-a, a - rr - u, -1, 0]],
                    [arc, (u) => { const q = Math.PI + u / rr; return [-a + rr + rr * Math.cos(q), -a + rr + rr * Math.sin(q), Math.cos(q), Math.sin(q)]; }],
                    [side / 2, (u) => [-a + rr + u, -a, 0, -1]]];
                for (const [len, f] of segs) { if (d <= len) return f(d); d -= len; }
                return [0, -a, 0, -1];
            };
            return { W, F, S, a, P, walk, L: F - gap - 4 };
        };
        const ringFrac = (e) => { const r = box.getBoundingClientRect(), x = e.clientX - r.left - r.width / 2, y = e.clientY - r.top - r.height / 2; const d = Math.hypot(x, y) / (r.width / 2); return d < .55 || d > 1.05 ? null : ((Math.atan2(x, -y) / (2 * Math.PI)) + 1) % 1; };
        const frameFrac = (e) => {
            const r = fcv.getBoundingClientRect(), x = e.clientX - r.left - r.width / 2, y = e.clientY - r.top - r.height / 2, G = frameGeo();
            if (Math.max(Math.abs(x), Math.abs(y)) < G.S / 2) return null;   // on the picture itself: that is play / pause
            let best = 0, bd = 1e9; for (let i = 0; i < 400; i++) { const [px, py] = G.walk(i / 400), dd = (px - x) ** 2 + (py - y) ** 2; if (dd < bd) { bd = dd; best = i / 400; } }
            return best;
        };
        const put = (f, type) => { if (!input) return; drag = f; input.value = String(Math.round(f * Number(input.max || 1))); input.dispatchEvent(new Event(type, { bubbles: true })); lastPlayed = -1; };
        const hook = (el, frac) => {
            if (!el || el.dataset.ring) return; el.dataset.ring = '1';
            el.addEventListener('pointerdown', e => { if (e.target.closest('button') || e.button > 0) return; const f = frac(e); if (f === null) return; el.setPointerCapture(e.pointerId); put(f, 'input'); e.preventDefault(); });
            el.addEventListener('pointermove', e => { if (drag !== null && el.hasPointerCapture(e.pointerId)) { const f = frac(e); if (f !== null) put(f, 'input'); } });
            el.addEventListener('pointerup', e => { if (drag === null) return; put(frac(e) ?? drag, 'change'); drag = null; });
            el.addEventListener('pointercancel', () => { drag = null; });
        };
        hook(box, ringFrac); if (fcv && fbox) hook(fbox, frameFrac);
        const size = (c, dpr) => { const w = Math.round(c.clientWidth * dpr), h = Math.round(c.clientHeight * dpr); if (c.width !== w || c.height !== h) { c.width = w; c.height = h; } return [w, h]; };
        const drawRing = (played, on, dpr) => {
            const [w, h] = size(cv, dpr), g = cv.getContext('2d'); if (!w || !h) return;
            const R = Math.min(w, h) / 2, n = 80, bw = Math.max(2 * dpr, R * .048), maxL = R * .27, minL = R * .05;
            g.clearRect(0, 0, w, h); g.save(); g.translate(w / 2, h / 2);
            for (let i = 0; i < n; i++) {
                g.fillStyle = (i + .5) / n <= played ? on : 'rgba(244,239,230,.25)';
                g.beginPath(); g.roundRect(-bw / 2, -R, bw, Math.max(minL, levelAt(i, n) * maxL), bw / 2); g.fill();
                g.rotate(2 * Math.PI / n);
            }
            g.restore();
        };
        const drawFrame = (played, on, dpr) => {
            const [w, h] = size(fcv, dpr), g = fcv.getContext('2d'); if (!w || !h) return;
            const G = frameGeo(), bw = 3, n = Math.max(60, Math.round(G.P / (bw * 2.3)));
            g.clearRect(0, 0, w, h); g.save(); g.scale(dpr, dpr); g.translate(G.W / 2, G.W / 2); g.lineCap = 'round'; g.lineWidth = bw;
            for (let i = 0; i < n; i++) {
                const [x, y, nx, ny] = G.walk((i + .5) / n), L = Math.max(3, levelAt(i, n) * G.L);
                g.strokeStyle = (i + .5) / n <= played ? on : 'rgba(244,239,230,.22)';
                g.beginPath(); g.moveTo(x + nx * 1.5, y + ny * 1.5); g.lineTo(x + nx * (L - 1.5), y + ny * (L - 1.5)); g.stroke();
            }
            g.restore();
        };
        const draw = (now) => {
            npWaveRaf = requestAnimationFrame(draw);
            const frame = !!(fcv && fcv.offsetParent), key = (frame ? 'f' : 'r') + (frame ? fcv.clientWidth : cv.clientWidth);
            if (document.hidden || drag === null && now - last < 250 && key === lastKey) return; last = now;
            const c = flacie.clock(), played = drag ?? (c[1] > 0 ? c[0] / c[1] : 0), dpr = devicePixelRatio || 1;
            if (key !== lastKey) { lastKey = key; lastPlayed = -1; }
            if (Math.abs(played - lastPlayed) < .0005 && lastPlayed >= 0) return; lastPlayed = played;
            const on = getComputedStyle(np).getPropertyValue('--np-accent').trim() || '#e8774f';
            if (frame) drawFrame(played, on, dpr); else if (cv.offsetParent) drawRing(played, on, dpr);
        };
        npWaveRaf = requestAnimationFrame(draw);
    },
    // nobody has sent this song's shape yet: this browser reads the whole file once (quietly: not a play, behind the song being listened to), decodes it,
    // measures how loud each of 200 slices is (RMS, which follows the ear better than peaks) and sends the bars to the server, which keeps them for
    // everyone. Skipped on mobile data and for files over 200 MB; one at a time.
    npWaveMake(url) {
        // one at a time: the next one waits for the one being worked out (each reads a whole file)
        const run = waveChain.then(() => flacie.npWaveMakeNow(url));
        waveChain = run.catch(() => null);
        return run;
    },
    // a song's shape, from this page's memory, else the server, else worked out here; kept for the songs about to play
    npWaveGet(url) {
        if (waveCache.has(url)) return Promise.resolve(waveCache.get(url));
        let p = waveAsking.get(url);
        if (!p) {
            // a shape the server only estimated (X-Wave: rough, from the FLAC frames) is shown at once; a browser that can decode the song works out the exact one
            // in the background and sends it, so the next person gets that
            p = fetch(url, { credentials: 'same-origin' }).then(r => {
                if (r.ok && r.headers.get('X-Wave') === 'rough' && !flacie.metered()) flacie.npWaveMake(url).then(a => { if (Array.isArray(a) && a.length) waveCache.set(url, a); });
                return r.ok ? r.json() : r.status === 404 ? flacie.npWaveMake(url) : null;
            }).catch(() => null)
                .then(a => { waveAsking.delete(url); if (Array.isArray(a) && a.length) { waveCache.set(url, a); while (waveCache.size > 12) waveCache.delete(waveCache.keys().next().value); } return a; });
            waveAsking.set(url, p);
        }
        return p;
    },
    // while a song plays: the next songs' shapes the server already has are fetched, so each is drawn the moment it starts. Not on mobile data. They are never
    // worked out here in advance (that reads a whole file each): the song someone picks would wait behind them, and a song from Listen again showed no ring.
    npWavePrefetch(paths) {
        if (flacie.metered() || !Array.isArray(paths)) return;
        paths.reduce((chain, p) => chain.then(() => {
            const url = '/api/wave?p=' + encodeURIComponent(p);
            if (waveCache.has(url)) return null;
            return fetch(url + '&known=1', { credentials: 'same-origin' }).then(r => r.status === 200 ? r.json() : null).then(a => { if (Array.isArray(a) && a.length) waveCache.set(url, a); }).catch(() => null);
        }), Promise.resolve());
    },
    /** The path of the song whose shape the player is drawing (for the tests). */
    npWaveFor() { try { return new URL(waveNow, location.href).searchParams.get('p') ?? ''; } catch { return ''; } },
    async npWaveMakeNow(url) {
        if (flacie.metered() || npWaveBusy) return null;
        npWaveBusy = true;
        try {
            const p = new URL(url, location.href).searchParams.get('p');
            const r = await fetch('/stream?wave=1&p=' + encodeURIComponent(p), { credentials: 'same-origin' });
            if (!r.ok || Number(r.headers.get('Content-Length') || 0) > 200e6) return null;
            const buf = await r.arrayBuffer();
            const ctx = new (window.OfflineAudioContext || window.webkitOfflineAudioContext)(1, 1, 22050);
            const audioBuf = await ctx.decodeAudioData(buf);
            const d = audioBuf.getChannelData(0), n = 200, rms = new Array(n).fill(0), step = Math.max(1, Math.floor(d.length / n / 2000));
            for (let b = 0; b < n; b++) {
                const from = Math.floor(d.length * b / n), to = Math.max(from + 1, Math.floor(d.length * (b + 1) / n));
                let sum = 0, cnt = 0; for (let i = from; i < to; i += step) { sum += d[i] * d[i]; cnt++; }
                rms[b] = Math.sqrt(sum / Math.max(1, cnt));
            }
            const max = Math.max(...rms) || 1, bars = rms.map(v => Math.round(255 * v / max));
            fetch(url, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(bars) }).catch(() => { });
            return bars;
        } catch { return null; } finally { npWaveBusy = false; }
    },
    npWaveStop() { if (npWaveRaf) cancelAnimationFrame(npWaveRaf); npWaveRaf = 0; },
    // design I: the whole app wears the colours of the song playing (the menu's highlight, buttons, chips): the cover's colours on the page itself
    appAccent(url) {
        flacie.npAccent(document.documentElement, url);
    },
    npAccent(el, url) {
        // design I adds the ground (the dark colour the cover fades into), the tone (tinted buttons and chips) and the text on the tone, all from the second colour
        const set = (a, b, from, ground, tone, tonefg) => { if (!el) return; if (flacie.metered()) el.dataset.calm = ''; const st = el.style; st.setProperty('--np-accent', a); st.setProperty('--np-accent-2', b); st.setProperty('--ground', ground); st.setProperty('--tone', tone); st.setProperty('--tonefg', tonefg); el.dataset.accent = from;
            // text on the accent: dark on a light accent, white on a dark one, so it always reads (WCAG AA)
            const fg = accentFg(a); st.setProperty('--np-accent-fg', fg);
            if (el === document.documentElement && from === 'cover') { st.setProperty('--accent', a); st.setProperty('--accent-2', a); st.setProperty('--accent-fg', fg); } };
        const fallback = () => set('rgb(232, 119, 79)', 'rgb(47, 93, 98)', 'default', 'rgb(22, 58, 63)', 'rgb(36, 82, 90)', 'rgb(231, 243, 240)');
        if (!url) { fallback(); return; }
        const img = new Image();
        img.onerror = fallback;
        img.onload = () => {
            try {
                const c = document.createElement('canvas'); c.width = c.height = 24;
                const g = c.getContext('2d', { willReadFrequently: true }); g.drawImage(img, 0, 0, 24, 24);
                const d = g.getImageData(0, 0, 24, 24).data, bins = new Map();
                for (let i = 0; i < d.length; i += 4) {
                    const [h, s, l] = flacie.hsl(d[i], d[i + 1], d[i + 2]);
                    if (l < .12 || l > .92) continue;
                    const k = Math.round(h / 30) % 12, w = .04 + s * s * s * 4, b = bins.get(k) ?? { w: 0, h: 0, s: 0, n: 0 };
                    b.w += w; b.h += h; b.s += s; b.n++; bins.set(k, b);
                }
                const top = [...bins.values()].sort((x, y) => y.w - x.w);
                if (!top.length) { fallback(); return; }
                const col = (b, l, smax = .85) => { const [r, gg, bb] = flacie.rgbOf(b.h / b.n / 360, Math.min(smax, Math.max(.35, b.s / b.n)), l); return `rgb(${r}, ${gg}, ${bb})`; };
                const two = top[1] ?? top[0];
                set(col(top[0], .66), col(two, .32), 'cover', col(two, .16, .5), col(two, .26, .45), col(two, .92, .4));
            } catch { fallback(); }
        };
        img.src = url;
    },
    hsl(r, g, b) {
        r /= 255; g /= 255; b /= 255;
        const mx = Math.max(r, g, b), mn = Math.min(r, g, b), l = (mx + mn) / 2, dd = mx - mn;
        if (!dd) return [0, 0, l];
        const s = dd / (1 - Math.abs(2 * l - 1));
        const h = mx === r ? ((g - b) / dd + 6) % 6 : mx === g ? (b - r) / dd + 2 : (r - g) / dd + 4;
        return [h * 60, s, l];
    },
    rgbOf(h, s, l) {
        const f = (n) => { const k = (n + h * 12) % 12, a = s * Math.min(l, 1 - l); return Math.round(255 * (l - a * Math.max(-1, Math.min(k - 3, 9 - k, 1)))); };
        return [f(0), f(8), f(4)];
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
    // Settings' search box: keeps the groups (.set) whose text has every typed word, hides the rest, and says so when none does
    settingsFind(q) {
      const words = (q || "").toLowerCase().split(/\s+/).filter(Boolean);
      let shown = 0;
      for (const sec of document.querySelectorAll("section.set")) {
        const hit = words.every((w) => sec.textContent.toLowerCase().includes(w));
        sec.hidden = !hit; if (hit) shown++;
      }
      const none = document.querySelector(".set-none"); if (none) none.hidden = shown > 0;
    },
    // Share on a detail page: the device's share sheet ("shared"), else the link copied ("copied"); "" when neither worked, "cancelled" when the person closed the sheet
    async shareLink(title, link) {
      const url = link || location.href;
      if (navigator.share) { try { await navigator.share({ title, url }); return "shared"; } catch (e) { if (e && e.name === "AbortError") return "cancelled"; } }
      try { await navigator.clipboard.writeText(url); return "copied"; } catch { return ""; }
    },
    // the reload after the server comes back: a song that was playing (on what the browser had buffered) plays on from the second it reached
    recoverReload(playing, at) {
      try { sessionStorage.setItem("flacie.recover", JSON.stringify({ playing: !!playing, t: +at || 0 })); } catch { }
      location.reload();
    },
    // read once, after that reload (PlayerState.RestoreAsync)
    takeRecover() {
      try { const v = sessionStorage.getItem("flacie.recover"); sessionStorage.removeItem("flacie.recover"); return v; } catch { return null; }
    },
    // when Blazor gives up on the live connection ("rejected": the server was restarted and has forgotten this page; "failed": it could not be reached in time),
    // wait for the server to answer again and reload, instead of leaving a stuck "Can't reach FLACie" toast
    autoRecover() {
      let polling = false;
      const poll = async () => {
        try { const r = await fetch("/healthz", { cache: "no-store" }); if (r.ok) { window.flacie.recoverReload(!audio.paused, audio.currentTime); return; } } catch { }
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
      const cleanup = { ro, mo, mq: marquee(stage) };
      // the keys the device uses are the device's alone: a focused button must not also click on Enter or Space, and the page-wide shortcuts (Space, arrows) must not act as well
      const keys = e => { if (["Enter", " ", "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(e.key)) e.preventDefault(); };
      stage.addEventListener("keydown", keys);
      if (!wheel) { ipod = { fit, wheel: null, stage, keys, ...cleanup }; return innerWidth >= 860; }   // touch models: no wheel, the screen is the FLACie player
      const st = ipod = { ...cleanup, fit, wheel, stage, keys, cfg: cfg || { deg: 34 }, last: null, acc: 0, moved: 0, rotating: false, zone: null, held: false, timer: null, hold: null, samples: [], coast: 0, ctx: null };
      // the wheel's box on screen. The device is scaled with CSS zoom; browsers before Chromium 128 (older Android WebViews among them) report boxes inside a
      // zoomed element unzoomed while touches come in screen pixels, so taps missed the ring there. A plain 100px probe in the zoomed wrapper tells which it is.
      const box = () => {
        const r = wheel.getBoundingClientRect(), z = parseFloat(fitEl && fitEl.style.zoom) || 1;
        if (Math.abs(z - 1) < 0.01) return r;
        // added and taken away again at once (the wrapper's children are the page's), and read once per zoom
        if (st.zoomK === undefined || st.zoomAt !== z) {
          const p = document.createElement("div");
          p.style.cssText = "position:absolute;left:0;top:0;width:100px;height:0;visibility:hidden;pointer-events:none";
          fitEl.appendChild(p); const w = p.getBoundingClientRect().width; p.remove();
          st.zoomAt = z; st.zoomK = Math.abs(w - 100) < 1 ? z : 1;
        }
        const k = st.zoomK;
        return k === 1 ? r : { left: r.left * k, top: r.top * k, width: r.width * k, height: r.height * k };
      };
      // a device lying turned (the Desk look tilts it a few degrees): its own angle, from the CSS rotate of the wheel and what holds it. The box on screen is then
      // bigger than the wheel (by |cos| + |sin|), and the zones turn with the device; read at each press (the look can change between presses)
      const ang = e => { const r = box(); return Math.atan2(e.clientY - (r.top + r.height / 2), e.clientX - (r.left + r.width / 2)) * 180 / Math.PI - (st.tilt || 0); };
      const zoneOf = e => {
        const tilt = st.tilt = tiltOf(wheel), t = tilt * Math.PI / 180;
        const r = box(), dx = e.clientX - (r.left + r.width / 2), dy = e.clientY - (r.top + r.height / 2), d = Math.hypot(dx, dy), R = r.width / 2 / (Math.abs(Math.cos(t)) + Math.abs(Math.sin(t)));
        if (d > R) return null;
        if (d < R * 0.36) return "center";
        let a = Math.atan2(dy, dx) * 180 / Math.PI - tilt; if (a < -180) a += 360; if (a >= 180) a -= 360;
        return a >= -135 && a < -45 ? "menu" : a >= -45 && a < 45 ? "next" : a >= 45 && a < 135 ? "play" : "prev";
      };
      const flush = () => { if (st.acc !== 0) { const d = st.acc; st.acc = 0; const v = Math.abs(speed()); ref.invokeMethodAsync("Turned", d, v).catch(() => { }); } };
      const speed = () => {
        const s = st.samples; if (s.length < 2) return 0;
        const a = s[0], b = s[s.length - 1], dt = b.t - a.t; return dt > 0 ? (b.a - a.a) * 1000 / dt : 0;
      };
      // the 3rd generation's wheel only scrolls (and its middle selects): its MENU, previous, next and play/pause are the row of buttons above it, so a tap
      // on its ring is no button (read at each press: the model can change while the wheel stays)
      const ringless = () => device.dataset.controls === "row";
      // a press held on next/previous seeks (again and again while held), held on MENU goes home: the ring's zones and the 3rd generation's buttons alike
      const holdLater = () => {
        st.hold = setTimeout(() => {
          if (st.rotating || st.last === null || (!st.key && ringless())) return;
          if (st.zone === "next" || st.zone === "prev") { st.held = true; const go = () => { if (st.last === null) return; ref.invokeMethodAsync("Hold", st.zone).catch(() => { }); st.hold = setTimeout(go, 130); }; go(); }
          else if (st.zone === "menu") { st.held = true; ref.invokeMethodAsync("Hold", "menu").catch(() => { }); }
        }, 500);
      };
      const down = e => {
        const z = zoneOf(e); if (!z) return;
        st.coast && cancelAnimationFrame(st.coast); st.coast = 0;
        st.zone = z; st.key = null; st.last = ang(e); st.moved = 0; st.slop = 0; st.rotating = false; st.held = false; st.samples = [{ t: performance.now(), a: 0 }]; st.total = 0;
        st.timer = setInterval(flush, 30);
        wheel.classList.add("turning");
        holdLater();
      };
      // a button of its own (the 3rd generation's row, data-zone="menu" etc.): pressed like that zone of the ring, never turned. Listened for on the device,
      // as the row comes and goes with the model while the device stays
      const keyDown = e => {
        const k = e.target.closest && e.target.closest("[data-zone]"); if (!k || !device.contains(k) || (e.pointerType === "mouse" && e.button !== 0)) return;
        e.preventDefault();
        st.coast && cancelAnimationFrame(st.coast); st.coast = 0;
        st.zone = k.dataset.zone; st.key = k; st.last = 0; st.moved = 0; st.slop = 0; st.rotating = false; st.held = false; st.samples = []; st.total = 0;
        k.classList.add("down");
        holdLater();
      };
      const move = e => {
        if (st.last === null || st.key) return;
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
        const key = st.key; if (key) { key.classList.remove("down"); st.key = null; }
        const v = speed(), zone = st.zone, rotating = st.rotating, held = st.held; flush();
        if (!rotating && !held && (key || zone === "center" || !ringless())) ref.invokeMethodAsync("Zone", zone).catch(() => { });
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
      st.down = down; st.move = move; st.up = up; st.keyDown = keyDown; st.device = device;
      wheel.addEventListener("pointerdown", down); device.addEventListener("pointerdown", keyDown); addEventListener("pointermove", move); addEventListener("pointerup", up); addEventListener("pointercancel", up);
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
    // room for the panel beside the stage (what ipodInit answers for the iPod; the other scenes ask this)
    wideScreen() { return innerWidth >= 860; },
    /** Mega Bass (the Discman scene): about +9 dB more on the bass band (on top of the EQ's own). Returns whether it is on. */
    bass(on) {
      on = !!on;
      if (on && !toneGraph()) return false;
      mega = on; toneApply(); return mega;
    },
    /** Whether Mega Bass is on (as asked: the gain follows a moment later). */
    megaOn() { return !!mega; },
    /** The bass band's gain now, in dB (0 while the music has never gone through the EQ). */
    bassLevel() { return eqF ? eqF[0].gain.value : 0; },
    /** The EQ (the iPod desk's preamp): five bands, -12..+12 dB each, kept for next time. The music goes through Web Audio only once a band is moved off
     *  flat (an element once routed cannot be taken back; flat is then 0 dB everywhere, nothing changes). Returns the bands as set. */
    eq(bands) {
      const b = EQ_HZ.map((_, i) => Math.max(-12, Math.min(12, Math.round(Number(bands && bands[i]) || 0))));
      if (b.some((g) => g) && !toneGraph()) return eqGains.slice();
      eqGains = b; toneApply(); eqSave(); return eqGains.slice();
    },
    eqGet() { return eqGains.slice(); },
    /** The band centres, in Hz. */
    eqBands() { return EQ_HZ.slice(); },
    /** The preamp's BASS and TREBLE knobs: the outer two bands. Returns [bass, treble] as set. */
    tone(low, high) { const b = eqGains.slice(); b[0] = Number(low) || 0; b[4] = Number(high) || 0; const r = this.eq(b); return [r[0], r[4]]; },
    toneGet() { return [eqGains[0], eqGains[4]]; },
    /** The bands' gains now, in dB: { low, high, bands }. */
    toneLevel() { const g = eqF ? eqF.map((f) => f.gain.value) : [0, 0, 0, 0, 0]; return { low: g[0], high: g[4], bands: g }; },
    /** The file's own sample rate, when known: the EQ's graph is opened at it (made later, it is not resampled on its way through). */
    toneRate(hz) { toneRateHint = Number(hz) || 0; },
    /** What the visualisers read: "playback" (the music itself, through the EQ: one download), "twin" (a silent second copy), or "" (nothing open). */
    vizSource() { return actx && actx === toneCtx ? "playback" : twin ? "twin" : ""; },
    /** The meter the preamp shows ("vu", "spec" or "eq"), kept for next time. */
    meter(m) {
      if (m === "vu" || m === "spec" || m === "eq") { try { localStorage.setItem("flacie.meter", m); } catch { } return m; }
      try { const v = localStorage.getItem("flacie.meter"); return v === "spec" || v === "eq" ? v : "vu"; } catch { return "vu"; }
    },
    /** The same scrolling for other screens (the listening scenes' displays): the lines matching sel under root. Returns { stop() }. */
    marqueeIn(root, sel) { return marquee(root, sel); },
    async ipodBattery() { try { const b = await navigator.getBattery(); return [Math.round(b.level * 100), b.charging]; } catch { return [100, false]; } },
    // a text box the server never rewrites while it is being typed in (see TextBox.razor): the browser debounces and tells the server once
    textBox(el, ref, value, ms) {
      if (!el) return;
      let t = null; const send = () => { clearTimeout(t); t = null; ref.invokeMethodAsync("OnText", el.value).catch(() => { }); };
      const input = () => { clearTimeout(t); t = setTimeout(send, ms); };
      // letters typed before the page went live (carried over from the server-drawn box, see textBoxCarry) or while this call was on its way are the person's: kept and told to the server
      const typed = (el._tbCarried || document.activeElement === el) && el.value !== value;
      el._tbCarried = false;
      if (typed) setTimeout(input, 0); else el.value = value;
      const key = e => { if (e.key === "Enter" && t !== null) send(); };
      const search = () => send();   // the x of a search box
      el.addEventListener("input", input); el.addEventListener("keydown", key); el.addEventListener("search", search);
      el._tb = { input, key, search, flush: () => { if (t !== null) send(); } };
    },
    textBoxSet(el, value) { if (el && document.activeElement !== el && el.value !== value) el.value = value; },
    textBoxOff(el) { if (el && el._tb) { el.removeEventListener("input", el._tb.input); el.removeEventListener("keydown", el._tb.key); el.removeEventListener("search", el._tb.search); el._tb = null; } },
    ipodDate() { return new Date().toLocaleDateString(undefined, { weekday: "long", month: "long", day: "numeric" }); },
    // the touch iPods' scrolling: a mouse can drag a list like a finger does (touch screens scroll by themselves); a drag is not a tap; the end of a long list asks for more rows.
    // A quick sideways swipe (a finger or a mouse, at least 40 points of the screen, mostly sideways) is told to the system (Swipe: +1 to the left, -1 to the right),
    // which pages the nanos' home screens and goes back; a swipe is not a tap either
    iosInit(root, ref) {
      window.flacie.iosDispose();
      let sc = null, x0 = 0, y0 = 0, top0 = 0, dragged = false, k = 1, t = 0, sw = null;
      const down = e => {
        if (e.button !== 0 && e.pointerType === "mouse") return;
        // the screen's own points per screen pixel (the fit's zoom and the screen's scale), and the way the device lies (a drag is measured along it)
        t = tiltOf(root) * Math.PI / 180;
        sw = { x: e.clientX, y: e.clientY, at: performance.now(), s: (root.getBoundingClientRect().width / (root.offsetWidth || 1)) / (Math.abs(Math.cos(t)) + Math.abs(Math.sin(t))) || 1 };
        if (e.pointerType !== "mouse") return;
        sc = e.target.closest(".ios-scroll"); if (!sc) return;
        k = zoomOf(root) || 1;
        x0 = e.clientX; y0 = e.clientY; top0 = sc.scrollTop; dragged = false;
      };
      const move = e => { if (!sc) return; const dy = -(e.clientX - x0) * Math.sin(t) + (e.clientY - y0) * Math.cos(t); if (!dragged && Math.abs(dy) > 5) dragged = true; if (dragged) sc.scrollTop = top0 - dy / k; };
      const up = e => {
        if (sw && e && e.clientX !== undefined && ref) {
          const ddx = e.clientX - sw.x, ddy = e.clientY - sw.y, dx = (ddx * Math.cos(t) + ddy * Math.sin(t)) / sw.s, dy = (-ddx * Math.sin(t) + ddy * Math.cos(t)) / sw.s;
          if (Math.abs(dx) >= 40 && Math.abs(dx) > Math.abs(dy) * 1.5 && performance.now() - sw.at < 900) { dragged = true; ref.invokeMethodAsync("Swipe", dx < 0 ? 1 : -1).catch(() => { }); }
        }
        sw = null; sc = null; setTimeout(() => { dragged = false; }, 0);
      };
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
      try { ipod.ro && ipod.ro.disconnect(); ipod.mo && ipod.mo.disconnect(); ipod.mq && ipod.mq.stop(); } catch { }
      removeEventListener("resize", ipod.fit); if (ipod.wheel) ipod.wheel.removeEventListener("pointerdown", ipod.down); if (ipod.device) ipod.device.removeEventListener("pointerdown", ipod.keyDown); ipod.stage.removeEventListener("keydown", ipod.keys);
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
    if (/^#[0-9a-f]{6}$/i.test(p.accent)) { r.style.setProperty("--accent", p.accent); r.style.setProperty("--accent-2", lighter(p.accent)); r.style.setProperty("--accent-fg", window.flacieAccentFg(p.accent)); }
    else { r.style.removeProperty("--accent"); r.style.removeProperty("--accent-2"); r.style.removeProperty("--accent-fg"); }
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

// textBoxCarry: the page is first drawn by the server and goes live a moment later, when Blazor draws it again with new elements. A person can already be typing in the first
// search box; its letters, the focus and the caret move to the new box with the same label as soon as it is put in (before the next key), so nothing typed is lost.
(() => {
  const key = el => el instanceof HTMLInputElement && !el._tb ? el.getAttribute("aria-label") : null;
  const typed = new Set(); let focused = null;
  document.addEventListener("input", e => { if (key(e.target)) typed.add(e.target); }, true);
  document.addEventListener("focusin", e => { focused = e.target; }, true);
  new MutationObserver(() => {
    for (const old of typed) {
      if (old.isConnected) continue;
      typed.delete(old);
      const label = key(old), now = label && [...document.querySelectorAll("input[aria-label]")].find(n => n !== old && n.getAttribute("aria-label") === label);
      if (!now || now._tb || now.value === old.value) continue;
      now.value = old.value; now._tbCarried = true;
      if (focused === old && (document.activeElement === document.body || !document.activeElement)) { now.focus(); try { now.setSelectionRange(old.selectionStart, old.selectionEnd); } catch { } }
    }
  }).observe(document.documentElement, { childList: true, subtree: true });
})();
