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
  // Live equaliser for the Info tab. The audio is routed through an analyser from the first song on (set up before any sound plays, so
  // switching the Info tab on or off never interrupts it). Not done on iOS, where routing through Web Audio can stop playback in the background.
  let actx = null, an = null, vizOn = false, vh = null, vp = null, vizStyle = "curve", cp = null;
  window.flacieAudio = audio; // read by insight.js (the whole-song graphs), which never touches the audio path
  // the live spectrum: how loud each pitch is right now, 0 Hz on the left to 22 kHz on the right, with a slowly falling peak line
  const drawCurve = (g, w, h, dpr, data, nyq, bins, accent) => {
    const maxHz = Math.min(nyq, 22050), use = Math.floor(bins * maxHz / nyq), bottom = h - 18 * dpr, top = 6 * dpr, span = bottom - top;
    if (!cp || cp.length !== use) cp = new Float32Array(use);
    g.font = `${11 * dpr}px system-ui, sans-serif`; g.textBaseline = "alphabetic"; g.lineWidth = dpr;
    for (let k = 0; k <= maxHz; k += 5000) {
      const x = Math.min(w - 1, k / maxHz * w);
      g.strokeStyle = "rgba(255,255,255,.08)"; g.beginPath(); g.moveTo(x, top); g.lineTo(x, bottom); g.stroke();
      g.fillStyle = "#8a8a94"; g.textAlign = k === 0 ? "left" : "center"; g.fillText(k === 0 ? "0" : (k / 1000) + " kHz", k === 0 ? 2 : Math.min(x, w - 22 * dpr), h - 3 * dpr);
    }
    if (maxHz > 16000) { // where MP3s are usually cut off
      const x = 16000 / maxHz * w; g.setLineDash([4 * dpr, 4 * dpr]); g.strokeStyle = "rgba(255,255,255,.3)"; g.beginPath(); g.moveTo(x, top); g.lineTo(x, bottom); g.stroke(); g.setLineDash([]);
      g.fillStyle = "#8a8a94"; g.textAlign = "left"; g.fillText("MP3 usually ends here", x + 5 * dpr, top + 10 * dpr);
    }
    const pts = [], pk = [];
    for (let i = 0; i < use; i++) {
      const v = data[i] / 255; cp[i] = Math.max(v, cp[i] - 0.004);
      pts.push([i / (use - 1) * w, bottom - Math.pow(v, 1.15) * span]); pk.push([i / (use - 1) * w, bottom - Math.pow(cp[i], 1.15) * span]);
    }
    const path = (p) => { g.moveTo(p[0][0], p[0][1]); for (let i = 1; i < p.length - 1; i++) { const mx = (p[i][0] + p[i + 1][0]) / 2, my = (p[i][1] + p[i + 1][1]) / 2; g.quadraticCurveTo(p[i][0], p[i][1], mx, my); } };
    const grad = g.createLinearGradient(0, top, 0, bottom); grad.addColorStop(0, accent); grad.addColorStop(1, "rgba(255,255,255,.02)");
    g.beginPath(); path(pk); g.strokeStyle = "rgba(255,255,255,.28)"; g.lineWidth = 1.2 * dpr; g.stroke();
    g.beginPath(); path(pts); g.lineTo(w, bottom); g.lineTo(0, bottom); g.closePath(); g.fillStyle = grad; g.globalAlpha = .85; g.fill(); g.globalAlpha = 1;
    g.beginPath(); path(pts); g.strokeStyle = accent; g.lineWidth = 2 * dpr; g.stroke();
  };
  const ios = /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  const ensureGraph = () => {
    if (an || ios) return;
    try {
      actx = new (window.AudioContext || window.webkitAudioContext)();
      const src = actx.createMediaElementSource(audio);
      an = actx.createAnalyser(); an.fftSize = 2048; an.smoothingTimeConstant = 0.7;
      src.connect(an); an.connect(actx.destination);
    } catch { an = null; }
  };
  audio.addEventListener("play", () => { if (actx && actx.state === "suspended") actx.resume().catch(() => { }); });
  const vizDraw = () => {
    if (!vizOn) return;
    requestAnimationFrame(vizDraw);
    const cv = document.getElementById("viz"); if (!cv || !an) return;
    const dpr = window.devicePixelRatio || 1, w = Math.round(cv.clientWidth * dpr), h = Math.round(cv.clientHeight * dpr);
    if (!w || !h) return;
    if (cv.width !== w || cv.height !== h) { cv.width = w; cv.height = h; }
    const g = cv.getContext("2d"), nyq = actx.sampleRate / 2, bins = an.frequencyBinCount, data = new Uint8Array(bins);
    an.getByteFrequencyData(data);
    const accent0 = getComputedStyle(document.documentElement).getPropertyValue("--accent").trim() || "#ff4d8d";
    if (vizStyle === "curve") { g.clearRect(0, 0, w, h); drawCurve(g, w, h, dpr, data, nyq, bins, accent0); return; }
    const N = Math.max(18, Math.min(48, Math.floor(w / (13 * dpr)))), lo = 35, hi = Math.min(nyq, 18000);
    if (!vh || vh.length !== N) { vh = new Float32Array(N); vp = new Float32Array(N); }
    const accent = getComputedStyle(document.documentElement).getPropertyValue("--accent").trim() || "#ff4d8d";
    g.clearRect(0, 0, w, h);
    // classic LED columns: each column is one slice of pitch (low to high, evenly spaced by ear), lit up to its loudness
    const seg = 7 * dpr, gap = 2.5 * dpr, rows = Math.max(6, Math.floor(h / (seg + gap))), slot = w / N, bw = Math.max(3 * dpr, slot * 0.7);
    for (let i = 0; i < N; i++) {
      const f0 = lo * Math.pow(hi / lo, i / N), f1 = lo * Math.pow(hi / lo, (i + 1) / N);
      const b0 = Math.floor(f0 / nyq * bins), b1 = Math.max(b0 + 1, Math.ceil(f1 / nyq * bins));
      let m = 0; for (let k = b0; k < b1 && k < bins; k++) m = Math.max(m, data[k]);
      // the highs carry less energy in real music, so they are lifted a little to be seen
      const v = Math.min(1, Math.pow(m / 255, 1.5) * (1 + 0.7 * i / N));
      vh[i] = Math.max(v, vh[i] - 0.035); vp[i] = Math.max(vh[i], vp[i] - 0.008);
      const lit = Math.round(vh[i] * rows), peak = Math.min(rows - 1, Math.round(vp[i] * rows)), x = i * slot + (slot - bw) / 2;
      for (let r = 0; r < rows; r++) {
        const y = h - (r + 1) * (seg + gap) + gap;
        if (r < lit) { g.globalAlpha = 0.45 + 0.55 * (r / rows); g.fillStyle = accent; }
        else if (r === peak && peak > 0) { g.globalAlpha = 0.9; g.fillStyle = "#fff"; }
        else { g.globalAlpha = 0.06; g.fillStyle = "#fff"; }
        g.beginPath(); g.roundRect(x, y, bw, seg, Math.min(2 * dpr, seg / 2)); g.fill();
      }
    }
    g.globalAlpha = 1;
  };
  let npRef = null, npPushed = false;
  const npClosed = () => { const r = npRef; npRef = null; npPushed = false; document.body.classList.remove("np-open"); if (r) r.invokeMethodAsync("Closed").catch(() => { }); };
  window.addEventListener("popstate", () => { if (npRef) npClosed(); else npPushed = false; });
  document.addEventListener("keydown", e => { if (e.key === "Escape" && npRef) window.flacie.npBack(); });
  // a reload keeps the #now-playing in the address but not the player; drop it
  if (location.hash === "#now-playing") history.replaceState(null, "", location.pathname + location.search);
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
      vizStyle = style === "leds" ? "leds" : "curve";
      ensureGraph();
      if (!an) return false;
      if (actx.state === "suspended") actx.resume().catch(() => { });
      if (!vizOn) { vizOn = true; requestAnimationFrame(vizDraw); }
      return true;
    },
    vizStop() { vizOn = false; },
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
    npReset() { npRef = null; document.body.classList.remove("np-open"); },
    stash(key, json) { try { localStorage.setItem(key, json); } catch { } },
    unstash(key) { try { return localStorage.getItem(key); } catch { return null; } },
    ready() { return audio.readyState >= 3; },
    pause() { audio.pause(); },
    // the X on the bar: stop and let go of the file
    stop() { audio.pause(); audio.removeAttribute("src"); audio.load(); document.title = "FLACie"; if ("mediaSession" in navigator) navigator.mediaSession.metadata = null; },
    seek(s) { if (isFinite(s)) audio.currentTime = s; },
    volume(v) {
      if (v === undefined || v === null) return audio.volume;
      audio.volume = Math.min(1, Math.max(0, v));
      try { localStorage.setItem("flacie.volume", String(audio.volume)); } catch { }
      return audio.volume;
    },
  };
})();
