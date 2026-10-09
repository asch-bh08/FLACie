// Headphone test for FLACie Web: test tones, left / right, bass, treble and clarity checks, a frequency slider and a 3D (spatial) check, all made
// in the browser with the Web Audio API (nothing is downloaded or sent anywhere). Everything goes through one master level that starts low and
// is capped, with a short fade on every start and stop, because test tones are easy to make far too loud on a good amplifier.
window.hp = (() => {
  let ctx = null, master = null, analyser = null, root = null, running = [], timers = [], raf = 0, level = 0.12;
  let chan = "LR", wave = "sine", hz = 1000;
  const $ = (sel) => root && root.querySelector(sel);
  const fillOf = (el) => { if (el) el.style.setProperty("--fill", ((el.value - el.min) / (el.max - el.min) * 100) + "%"); };
  const MIN_HZ = 1, MAX_HZ = 44000;                                     // the slider's whole range: below hearing up to above it
  const logHz = (v) => MIN_HZ * Math.pow(MAX_HZ / MIN_HZ, v / 1000);    // slider 0..1000 -> 1 Hz..44 kHz
  const sliderOf = (f) => Math.round(1000 * Math.log(Math.max(MIN_HZ, f) / MIN_HZ) / Math.log(MAX_HZ / MIN_HZ));
  const zoneOf = (f) => f < 20 ? "Infrasound: felt as pressure at best, not heard. Most headphones cannot make it; kept quieter to protect the drivers"
    : f < 60 ? "Sub-bass: the deep rumble under the kick drum" : f < 250 ? "Bass: kick drum and bass guitar" : f < 500 ? "Low mids: warmth, the body of voices"
    : f < 2000 ? "Mids: voices and most instruments" : f < 4000 ? "Upper mids: presence and edge of voices" : f < 8000 ? "Presence and sibilance: “s” sounds, cymbal bite"
    : f <= 20000 ? "Treble and air: sparkle, the top of cymbals" : "Ultrasonic: above human hearing. Nobody hears it; it only shows what your device can output";
  const gainOf = (l) => Math.pow(l, 2) * 0.35;                         // 0..1 slider -> a gentle curve, never above 0.35
  const fmt = (f) => (f >= 1000 ? (f / 1000).toFixed(f >= 10000 ? 1 : 2).replace(/\.?0+$/, "") + " kHz" : (f < 100 ? f.toFixed(1).replace(/\.0$/, "") : Math.round(f)) + " Hz");

  function audio() {
    if (!ctx) {
      const Ctx = window.AudioContext || window.webkitAudioContext;
      // 96 kHz when the browser allows it, so tones up to 44 kHz can exist; the sound card may still cut them lower
      try { ctx = new Ctx({ latencyHint: "interactive", sampleRate: 96000 }); } catch { ctx = new Ctx({ latencyHint: "interactive" }); }
      const r = document.getElementById("hp-rate"); if (r) r.textContent = `Output ${ctx.sampleRate / 1000} kHz: tones above ${Math.round(ctx.sampleRate / 2000)} kHz cannot exist, and your sound card may cut off lower than that.`;
      master = ctx.createGain(); master.gain.value = gainOf(level);
      analyser = ctx.createAnalyser(); analyser.fftSize = 4096; analyser.smoothingTimeConstant = 0.8;
      master.connect(analyser); analyser.connect(ctx.destination);
    }
    if (ctx.state === "suspended") ctx.resume();
    return ctx;
  }

  // a stereo gate for a source: fades in and out, and puts the sound in the left ear, the right ear or both
  function out(side) {
    const c = audio(), env = c.createGain(); env.gain.value = 0;
    const merger = c.createChannelMerger(2);
    env.connect(merger, 0, side === "R" ? 1 : 0);
    if (side === "LR") env.connect(merger, 0, 1);
    merger.connect(master);
    const t = c.currentTime; env.gain.setValueAtTime(0, t); env.gain.linearRampToValueAtTime(1, t + 0.05);
    return env;
  }
  function track(n) { running.push(n); return n; }
  function every(ms, fn) { const id = setInterval(fn, ms); timers.push(id); return id; }
  function later(ms, fn) { const id = setTimeout(fn, ms); timers.push(id); return id; }

  function stop() {
    const c = ctx; const t = c ? c.currentTime : 0;
    timers.forEach((id) => { clearInterval(id); clearTimeout(id); }); timers = [];
    running.forEach((n) => { try { if (n.gain) { n.gain.cancelScheduledValues(t); n.gain.setValueAtTime(n.gain.value, t); n.gain.linearRampToValueAtTime(0, t + 0.06); } if (n.stop) n.stop(t + 0.08); else setTimeout(() => { try { n.disconnect(); } catch { } }, 150); } catch { } });
    running = [];
    setMarker(null); status("Stopped"); document.querySelectorAll("[data-on]").forEach((b) => b.removeAttribute("data-on")); document.querySelectorAll(".hp-test.playing").forEach((r) => r.classList.remove("playing"));
    const dot = $("#hp-dot"); if (dot) dot.setAttribute("opacity", "0");
  }

  function status(t) { const s = $("#hp-status"); if (s) s.textContent = t; }
  function setMarker(f) { marker = f; }
  let marker = null;

  // ---- sources ----
  const trimOf = (f) => (f < 20 ? 0.35 : 1);
  function osc(f, type, side) {
    const c = audio(), o = c.createOscillator(); o.type = type; o.frequency.value = f; const env = out(side);
    const trim = c.createGain(); trim.gain.value = trimOf(f); o.connect(trim); trim.connect(env); o._trim = trim; o.start(); track(o); track(env); return o;
  }
  function noiseBuf(kind) {
    const c = audio(), len = c.sampleRate * 4, b = c.createBuffer(1, len, c.sampleRate), d = b.getChannelData(0);
    if (kind === "white") for (let i = 0; i < len; i++) d[i] = Math.random() * 2 - 1;
    else { let b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0; for (let i = 0; i < len; i++) { const w = Math.random() * 2 - 1; b0 = 0.99886 * b0 + w * 0.0555179; b1 = 0.99332 * b1 + w * 0.0750759; b2 = 0.969 * b2 + w * 0.153852; b3 = 0.8665 * b3 + w * 0.3104856; b4 = 0.55 * b4 + w * 0.5329522; b5 = -0.7616 * b5 - w * 0.016898; d[i] = (b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362) * 0.11; b6 = w * 0.115926; } }
    return b;
  }
  function noise(kind, node) { const c = audio(), s = c.createBufferSource(); s.buffer = noiseBuf(kind); s.loop = true; s.connect(node); s.start(); track(s); return s; }

  // a short burst that comes and goes: used for clicks, pulses and drum hits
  function burst(env, atTime, dur, peak) { env.gain.cancelScheduledValues(atTime); env.gain.setValueAtTime(0.0001, atTime); env.gain.linearRampToValueAtTime(peak, atTime + 0.004); env.gain.exponentialRampToValueAtTime(0.0001, atTime + dur); }

  const acts = {
    // ---- left / right / both ----
    side(btn) {
      const side = btn.dataset.side; stop(); btn.dataset.on = "1";
      if (side === "alt") {
        osc(1000, "sine", "L"); osc(1000, "sine", "R");
        let left = true; const flip = () => { const t = ctx.currentTime; for (const [env, on] of [[running[1], left], [running[3], !left]]) { env.gain.cancelScheduledValues(t); env.gain.setValueAtTime(env.gain.value, t); env.gain.linearRampToValueAtTime(on ? 1 : 0, t + 0.04); } status(left ? "Left ear" : "Right ear"); left = !left; };
        flip(); every(1200, flip); return;
      }
      osc(1000, "sine", side); status({ L: "Left ear only", R: "Right ear only", LR: "Both ears" }[side] + " (1 kHz)");
    },
    // ---- the tone generator with the Hz slider ----
    tone(btn) {
      stop(); btn.dataset.on = "1"; const o = osc(hz, wave, chan); live = o; status(fmt(hz) + " " + wave); setMarker(hz);
    },
    preset(btn) { setHz(+btn.dataset.hz); },
    nudge(btn) { setHz(hz * (+btn.dataset.mul || 1) + (+btn.dataset.add || 0)); },
    tab(btn) {
      root.querySelectorAll("[data-act=tab]").forEach((b) => b.setAttribute("aria-selected", b === btn ? "true" : "false"));
      root.querySelectorAll("[data-panel]").forEach((p) => { p.hidden = p.dataset.panel !== btn.dataset.to; });
    },
    sweep(btn) {
      stop(); btn.dataset.on = "1";
      const from = +btn.dataset.from, to = +btn.dataset.to, secs = +btn.dataset.secs, c = audio();
      const o = osc(from, "sine", chan); const t0 = c.currentTime + 0.05;
      o.frequency.setValueAtTime(from, t0); o.frequency.exponentialRampToValueAtTime(to, t0 + secs);
      const tick = () => { const p = Math.min(1, Math.max(0, (c.currentTime - t0) / secs)); const f = from * Math.pow(to / from, p); setMarker(f); status("Sweep " + fmt(f)); const rng = $("#hp-hz"); if (rng) rng.value = sliderOf(f); const num = $("#hp-num"); if (num) num.value = Math.round(f * 10) / 10; if (p >= 1) { stop(); status("Sweep finished"); } };
      every(60, tick);
    },
    steps(btn) {
      stop(); btn.dataset.on = "1";
      const list = btn.dataset.list.split(",").map(Number); let i = 0; const c = audio();
      const o = osc(list[0], "sine", chan); const env = running.at(-1);
      const next = () => {
        if (i >= list.length) { stop(); status("Done"); return; }
        const f = list[i++], t = c.currentTime; o.frequency.setValueAtTime(f, t); env.gain.cancelScheduledValues(t); env.gain.setValueAtTime(0, t); env.gain.linearRampToValueAtTime(1, t + 0.05); env.gain.setValueAtTime(1, t + 1.7); env.gain.linearRampToValueAtTime(0, t + 1.95);
        setMarker(f); status(`${fmt(f)}  (${i} of ${list.length}) – do you hear it, and is it steady?`);
      };
      next(); every(2200, next);
    },
    noise(btn) { stop(); btn.dataset.on = "1"; const env = out(chan); track(env); noise(btn.dataset.kind, env); status((btn.dataset.kind === "pink" ? "Pink" : "White") + " noise"); setMarker(null); },
    // ---- bass: a synthetic kick drum on repeat, to judge how deep, tight and clean the low end is ----
    kick(btn) {
      stop(); btn.dataset.on = "1"; const c = audio(); const env = out("LR"); const o = c.createOscillator(); o.type = "sine"; o.connect(env); o.start(); track(o); track(env);
      const hit = () => { const t = c.currentTime + 0.02; o.frequency.cancelScheduledValues(t); o.frequency.setValueAtTime(160, t); o.frequency.exponentialRampToValueAtTime(42, t + 0.18); burst(env, t, 0.55, 0.9); };
      env.gain.cancelScheduledValues(0); hit(); every(900, hit); status("Kick drum: should thump low and stop cleanly, not boom or buzz"); setMarker(60);
    },
    // ---- clarity: sharp clicks, sibilance (sss), hi-hat ticks and two tones close together ----
    clicks(btn) {
      stop(); btn.dataset.on = "1"; const c = audio(); const env = out("LR"); track(env);
      const click = () => { const t = c.currentTime + 0.01, b = c.createBuffer(1, 64, c.sampleRate); b.getChannelData(0)[0] = 1; b.getChannelData(0)[1] = -0.6; const s = c.createBufferSource(); s.buffer = b; s.connect(env); s.start(t); };
      env.gain.cancelScheduledValues(0); env.gain.value = 1.6; click(); every(450, click); status("Clicks: each should be one crisp tick, with no ringing or smear"); setMarker(null);
    },
    sss(btn) {
      stop(); btn.dataset.on = "1"; const c = audio(); const env = out("LR"); track(env);
      const bp = c.createBiquadFilter(); bp.type = "bandpass"; bp.frequency.value = 7000; bp.Q.value = 1.2; const g = c.createGain(); g.gain.value = 0; bp.connect(g); g.connect(env); noise("white", bp); track(g);
      const hit = () => burst(g, c.currentTime + 0.01, 0.35, 0.8); hit(); every(1000, hit); status("“sss” sibilance: bright and clear, not harsh or painful"); setMarker(7000);
    },
    hat(btn) {
      stop(); btn.dataset.on = "1"; const c = audio(); const env = out("LR"); track(env);
      const hp = c.createBiquadFilter(); hp.type = "highpass"; hp.frequency.value = 8000; const g = c.createGain(); g.gain.value = 0; hp.connect(g); g.connect(env); noise("white", hp); track(g);
      let n = 0; const hit = () => { burst(g, c.currentTime + 0.01, n % 4 === 3 ? 0.22 : 0.07, n % 2 ? 0.45 : 0.8); n++; }; hit(); every(250, hit); status("Hi-hat: a clean tick and a longer open hat, each with a clear edge"); setMarker(10000);
    },
    close(btn) {
      stop(); btn.dataset.on = "1"; const c = audio(); const a = osc(1000, "sine", "LR"), b = osc(1000 + (+btn.dataset.diff || 8), "sine", "LR");
      running[1].gain.setValueAtTime(0.5, 0); running[3].gain.setValueAtTime(0.5, 0); status(`1000 Hz + ${1000 + (+btn.dataset.diff || 8)} Hz: you should hear a slow wobble (${btn.dataset.diff || 8} beats a second)`); setMarker(1000);
    },
    // ---- phase: both ears the same, or one of them upside down (a wrongly wired driver sounds hollow in the first test) ----
    phase(btn) {
      stop(); btn.dataset.on = "1"; const c = audio(); const inPhase = btn.dataset.mode === "in"; const buf = noiseBuf("pink"), s = c.createBufferSource(); s.buffer = buf; s.loop = true;
      const l = c.createGain(), r = c.createGain(); l.gain.value = 1; r.gain.value = inPhase ? 1 : -1; const env = c.createGain(); env.gain.value = 0; env.gain.linearRampToValueAtTime(1, c.currentTime + 0.05);
      const merger = c.createChannelMerger(2); s.connect(l); s.connect(r); l.connect(merger, 0, 0); r.connect(merger, 0, 1); merger.connect(env); env.connect(master); s.start(); track(s); track(env);
      status(inPhase ? "In phase: the noise should sit in the middle of your head" : "Out of phase: it should sound wide and hollow, outside the middle"); setMarker(null);
    },
    // ---- 3D: the sound moves around you with a head-related transfer function ----
    spin(btn) {
      stop(); btn.dataset.on = "1"; const c = audio(); const path = btn.dataset.path, secs = +btn.dataset.secs || 10;
      const p = c.createPanner(); p.panningModel = "HRTF"; p.distanceModel = "inverse"; p.refDistance = 1; p.rolloffFactor = 0;
      const env = c.createGain(); env.gain.value = 0; env.gain.linearRampToValueAtTime(1, c.currentTime + 0.05); env.connect(p); p.connect(master); track(env); track(p);
      const pulse = c.createGain(); pulse.gain.value = 0; const bp = c.createBiquadFilter(); bp.type = "bandpass"; bp.frequency.value = 2500; bp.Q.value = 0.4; noise("pink", bp); bp.connect(pulse); pulse.connect(env); track(pulse);
      const beat = () => burst(pulse, c.currentTime + 0.01, 0.28, 1.0); beat(); every(350, beat);
      const t0 = c.currentTime; const R = 2;
      const move = () => {
        const q = ((c.currentTime - t0) % secs) / secs; let az = 0, el = 0, txt = "";
        if (path === "circle") { az = q * 2 * Math.PI; txt = "Around your head, clockwise from the front"; }
        else if (path === "lr") { const k = q < 0.5 ? q * 2 : 2 - q * 2; az = (-80 + 160 * k) * Math.PI / 180; txt = "Sweeping across the front, left to right and back"; }
        else if (path === "fb") { const k = q < 0.5 ? q * 2 : 2 - q * 2; az = k * Math.PI; txt = "Front to back along the middle (it should pass over the top)"; }
        else if (path === "ud") { const k = q < 0.5 ? q * 2 : 2 - q * 2; el = -Math.PI / 3 + k * (2 * Math.PI / 3); txt = "Low to high in front of you"; }
        p.positionX.value = R * Math.sin(az) * Math.cos(el); p.positionZ.value = -R * Math.cos(az) * Math.cos(el); p.positionY.value = R * Math.sin(el);
        const dot = $("#hp-dot"); if (dot) { dot.setAttribute("opacity", "1"); dot.setAttribute("cx", (60 + 44 * Math.sin(az) * Math.cos(el)).toFixed(1)); dot.setAttribute("cy", (60 - 44 * Math.cos(az) * Math.cos(el) - 18 * Math.sin(el)).toFixed(1)); }
        status(txt);
      };
      move(); every(40, move);
    },
    stop() { stop(); },
  };
  let live = null;

  function setHz(f, fromSlider) {
    hz = Math.min(MAX_HZ, Math.max(MIN_HZ, f));
    const rng = $("#hp-hz"), num = $("#hp-num"), lab = $("#hp-hz-label"), zone = $("#hp-zone");
    if (rng && !fromSlider) rng.value = sliderOf(hz); fillOf(rng); if (num && document.activeElement !== num) num.value = Math.round(hz * 10) / 10; if (lab) lab.textContent = fmt(hz); if (zone) zone.textContent = zoneOf(hz);
    if (live && running.includes(live)) { live.frequency.setTargetAtTime(hz, ctx.currentTime, 0.02); if (live._trim) live._trim.gain.setTargetAtTime(trimOf(hz), ctx.currentTime, 0.05); setMarker(hz); status(fmt(hz) + " " + wave); }
  }

  // ---- the little spectrum picture ----
  function draw() {
    raf = requestAnimationFrame(draw);
    const cv = $("#hp-scope"); if (!cv || !analyser) return;
    const w = cv.width = cv.clientWidth * (devicePixelRatio || 1), h = cv.height = cv.clientHeight * (devicePixelRatio || 1), g = cv.getContext("2d");
    g.clearRect(0, 0, w, h); const bins = analyser.frequencyBinCount, data = new Uint8Array(bins); analyser.getByteFrequencyData(data); const nyq = ctx.sampleRate / 2;
    const top = Math.min(48000, nyq), xOf = (f) => (Math.log(Math.max(20, f) / 20) / Math.log(top / 20)) * w;
    g.strokeStyle = "#ffffff18"; g.fillStyle = "#ffffff77"; g.font = `${11 * (devicePixelRatio || 1)}px system-ui`; g.lineWidth = 1;
    for (const f of [50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 40000].filter((f) => f < top)) { const x = xOf(f); g.beginPath(); g.moveTo(x, 0); g.lineTo(x, h); g.stroke(); g.fillText(f >= 1000 ? f / 1000 + "k" : f, x + 3, h - 4); }
    g.beginPath(); g.moveTo(0, h);
    for (let x = 0; x < w; x += 2) { const f = 20 * Math.pow(top / 20, x / w), i = Math.min(bins - 1, Math.round(f / nyq * bins)); g.lineTo(x, h - (data[i] / 255) * (h - 18)); }
    g.lineTo(w, h); g.closePath(); g.fillStyle = "#ff4f7b55"; g.fill(); g.strokeStyle = "#ff4f7b"; g.lineWidth = 1.5; g.stroke();
    if (marker) { const x = xOf(Math.min(top, Math.max(20, marker))); g.strokeStyle = "#fff"; g.lineWidth = 2; g.beginPath(); g.moveTo(x, 0); g.lineTo(x, h); g.stroke(); }
  }

  function onClick(e) {
    const b = e.target.closest("[data-act]"); if (!b || !root.contains(b)) return;
    const wasOn = b.hasAttribute("data-on"), a = b.dataset.act;
    if (wasOn && !["tab", "preset", "nudge", "stop"].includes(a)) { stop(); return; }   // pressing a playing test again stops it
    (acts[a] || (() => { }))(b);
    const row = b.closest(".hp-test"); if (row && b.hasAttribute("data-on")) row.classList.add("playing");
  }
  function onInput(e) {
    const t = e.target;
    if (t.id === "hp-hz") { setHz(logHz(+t.value), true); fillOf(t); }
    else if (t.id === "hp-num") { const v = parseFloat(t.value); if (isFinite(v) && v > 0) setHz(v); }
    else if (t.id === "hp-level") { level = +t.value / 100; if (master) master.gain.setTargetAtTime(gainOf(level), ctx.currentTime, 0.03); const l = $("#hp-level-label"); if (l) l.textContent = levelText(); fillOf(t); }
    else if (t.name === "hp-chan") { chan = t.value; if (live && running.includes(live)) { acts.tone(root.querySelector("[data-act=tone]")); } }
    else if (t.id === "hp-wave") { wave = t.value; if (live && running.includes(live)) live.type = wave; }
  }
  const levelText = () => { const db = 20 * Math.log10(Math.max(1e-4, gainOf(level))); return level === 0 ? "off" : (db >= 0 ? "+" : "") + db.toFixed(0) + " dB"; };
  function onKey(e) { if (e.target.id === "hp-num") return; if (e.key === "Escape") stop(); }

  return {
    // the loudest point in the live spectrum right now (0..255): a test can tell sound is really being made
    peak() { if (!analyser) return 0; const d = new Uint8Array(analyser.frequencyBinCount); analyser.getByteFrequencyData(d); return Math.max(...d); },
    mount() {
      root = document.getElementById("hp-root"); if (!root) return;
      window.flacie && window.flacie.pause && window.flacie.pause();   // the test tones should not play over a song
      root.addEventListener("click", onClick); root.addEventListener("input", onInput); document.addEventListener("keydown", onKey);
      window.addEventListener("pagehide", stop);
      const lv = $("#hp-level"); if (lv) { lv.value = Math.round(level * 100); $("#hp-level-label").textContent = levelText(); fillOf(lv); }
      setHz(hz); cancelAnimationFrame(raf); draw();
    },
    unmount() {
      try { stop(); } catch { }
      cancelAnimationFrame(raf);
      if (root) { root.removeEventListener("click", onClick); root.removeEventListener("input", onInput); }
      document.removeEventListener("keydown", onKey); root = null;
      if (ctx) { try { ctx.close(); } catch { } ctx = null; master = null; analyser = null; }
    },
  };
})();
