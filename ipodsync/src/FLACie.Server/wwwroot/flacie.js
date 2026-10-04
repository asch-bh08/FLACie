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
  // live equaliser view of what is playing (Info tab): the audio runs through an analyser; low sounds are drawn on the left, high on the right
  let actx = null, an = null, scopeOn = false, peaks = null;
  const scopeDraw = () => {
    if (!scopeOn) return;
    requestAnimationFrame(scopeDraw);
    const cv = document.getElementById("spectrum"); if (!cv || !an) return;
    const dpr = window.devicePixelRatio || 1, w = Math.round(cv.clientWidth * dpr), h = Math.round(cv.clientHeight * dpr);
    if (!w || !h) return;
    if (cv.width !== w || cv.height !== h) { cv.width = w; cv.height = h; }
    const g = cv.getContext("2d"), nyq = actx.sampleRate / 2, bins = an.frequencyBinCount;
    const data = new Uint8Array(bins); an.getByteFrequencyData(data);
    const N = Math.max(24, Math.min(64, Math.floor(w / (9 * dpr)))), lo = 40, hi = Math.min(nyq, 20000);
    if (!peaks || peaks.length !== N) peaks = new Float32Array(N);
    const css = getComputedStyle(document.documentElement), accent = css.getPropertyValue("--accent").trim() || "#ff4d8d", dim = css.getPropertyValue("--faint").trim() || "#888";
    g.clearRect(0, 0, w, h);
    const bottom = h - 18 * dpr, top = 4 * dpr, span = bottom - top, slot = w / N, bw = Math.max(2 * dpr, slot * 0.68);
    const grad = g.createLinearGradient(0, top, 0, bottom); grad.addColorStop(0, accent); grad.addColorStop(1, "rgba(255,255,255,.12)");
    for (let i = 0; i < N; i++) {
      // each bar is one slice of the pitch range (low to high, evenly spaced by ear), showing its loudest part
      const f0 = lo * Math.pow(hi / lo, i / N), f1 = lo * Math.pow(hi / lo, (i + 1) / N);
      const b0 = Math.floor(f0 / nyq * bins), b1 = Math.max(b0 + 1, Math.ceil(f1 / nyq * bins));
      let m = 0; for (let k = b0; k < b1 && k < bins; k++) m = Math.max(m, data[k]);
      const v = Math.pow(m / 255, 1.6); peaks[i] = Math.max(v, peaks[i] - 0.01);
      const x = i * slot + (slot - bw) / 2, bh = Math.max(2 * dpr, v * span);
      g.fillStyle = grad; g.beginPath(); g.roundRect(x, bottom - bh, bw, bh, Math.min(bw / 2, 4 * dpr)); g.fill();
      g.fillStyle = "rgba(255,255,255,.55)"; g.fillRect(x, bottom - peaks[i] * span - 3 * dpr, bw, 2 * dpr);
    }
    g.font = `${11 * dpr}px system-ui, sans-serif`; g.fillStyle = dim; g.textBaseline = "alphabetic"; g.textAlign = "center";
    for (const [f, t] of [[100, "100 Hz"], [1000, "1 kHz"], [10000, "10 kHz"]]) {
      if (f > hi) continue;
      const x = Math.log(f / lo) / Math.log(hi / lo) * w; g.fillText(t, Math.min(Math.max(x, 24 * dpr), w - 24 * dpr), h - 3 * dpr);
    }
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
      audio.src = url;
      if (startAt > 0) audio.addEventListener("loadedmetadata", () => { audio.currentTime = startAt; }, { once: true });
      if ("mediaSession" in navigator) navigator.mediaSession.metadata = new MediaMetadata({ title, artist, album, artwork: art ? [{ src: art, sizes: "500x500", type: "image/jpeg" }] : [] });
      document.title = title ? `${title} · ${artist}` : "FLACie";
      if (autoplay) audio.play().catch(() => send("OnState", false)); else { audio.pause(); send("OnState", false); }
    },
    scope() {
      try {
        if (!an) {
          actx = new (window.AudioContext || window.webkitAudioContext)();
          const src = actx.createMediaElementSource(audio);
          an = actx.createAnalyser(); an.fftSize = 4096; an.smoothingTimeConstant = 0.82;
          src.connect(an); an.connect(actx.destination);
        }
        if (actx.state === "suspended") actx.resume();
        if (!scopeOn) { scopeOn = true; requestAnimationFrame(scopeDraw); }
      } catch { }
    },
    scopeStop() { scopeOn = false; },
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
