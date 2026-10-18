// iPod mode › Scene: small helpers for the listening scenes (desk.js runs their moving parts: the analyser, the needles, the bands).
window.retro = (() => {
  let ctx = null;
  // a knob or dial turned by the wheel (data-wheel) keeps the page from scrolling under it: Blazor's own wheel listener is passive, so it can't
  document.addEventListener('wheel', (e) => { if (e.target instanceof Element && e.target.closest('[data-wheel]')) e.preventDefault(); }, { passive: false, capture: true });
  let mq = null;
  // a control turned by a finger or the mouse, its drag sent to it as arrow keys so its own key handler does the rest (nothing is captured until the pointer has
  // really moved, so a tap still clicks what is under it): a knob (data-knob) by dragging up or down, every 12 px a step (ArrowUp / ArrowDown); a dial (data-dial)
  // by going round it, every 30 degrees a step (ArrowRight clockwise, ArrowLeft back); a swipe (data-swipe) by dragging sideways, every 40 px a step (ArrowRight
  // / ArrowLeft). data-step sets the step (px, or degrees for a dial). After a drag the click that would follow is dropped (a knob that mutes when pressed
  // must not mute when it was turned).
  let drag = null;
  const STEP = { knob: 12, dial: 30, swipe: 40 };
  const angle = (d, e) => Math.atan2(e.clientY - d.cy, e.clientX - d.cx) * 180 / Math.PI;
  const press = (k, key) => k.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
  document.addEventListener('pointerdown', (e) => {
    const k = e.target instanceof Element && e.target.closest('[data-knob], [data-dial], [data-swipe]'); if (!k || (e.pointerType === 'mouse' && e.button !== 0)) return;
    const kind = k.hasAttribute('data-dial') ? 'dial' : k.hasAttribute('data-swipe') ? 'swipe' : 'knob', r = k.getBoundingClientRect();
    drag = { k, kind, step: +k.dataset.step || STEP[kind], id: e.pointerId, x: e.clientX, y: e.clientY, cx: r.left + r.width / 2, cy: r.top + r.height / 2, acc: 0, moved: false };
    drag.a = angle(drag, e);
    k.focus({ preventScroll: true }); if (e.pointerType === 'mouse') e.preventDefault();
  });
  document.addEventListener('pointermove', (e) => {
    if (!drag || e.pointerId !== drag.id) return;
    const d = drag;
    if (!d.moved) { if (Math.hypot(e.clientX - d.x, e.clientY - d.y) < 5) return; d.moved = true; try { d.k.setPointerCapture(e.pointerId); } catch { } }
    if (d.kind === 'dial') { let da = angle(d, e) - d.a; d.a = angle(d, e); if (da > 180) da -= 360; if (da < -180) da += 360; d.acc += da; }
    else { const v = d.kind === 'knob' ? d.y - e.clientY : e.clientX - d.x; d.acc += v; d.x = e.clientX; d.y = e.clientY; }
    while (Math.abs(d.acc) >= d.step) {
      const fwd = d.acc > 0; d.acc -= fwd ? d.step : -d.step;
      press(d.k, d.kind === 'knob' ? (fwd ? 'ArrowUp' : 'ArrowDown') : (fwd ? 'ArrowRight' : 'ArrowLeft'));
    }
  });
  const end = (e) => {
    const d = drag; if (!d || e.pointerId !== d.id) return; drag = null;
    if (d.moved) { const stop = (c) => { c.stopImmediatePropagation(); c.preventDefault(); }; document.addEventListener('click', stop, { capture: true, once: true }); setTimeout(() => document.removeEventListener('click', stop, true), 80); }
  };
  document.addEventListener('pointerup', end); document.addEventListener('pointercancel', end);
  // a fader under a finger: its cap (a fifth of the slider's height, riding four of its own heights) goes to where the finger is and follows it, wherever on
  // the slider it first touched (the range input's own thumb is only a few pixels, too small to catch with a finger). The mouse keeps the input's own drag.
  let fing = null;
  const faderAt = (f, e) => {
    const r = f.i.getBoundingClientRect(), lo = +f.i.min || 0, hi = +f.i.max || 100, st = +f.i.step || 1;
    const v = Math.min(1, Math.max(0, 1 - (e.clientY - r.top - 0.1 * r.height) / (0.8 * r.height)));
    const val = Math.min(hi, Math.max(lo, Math.round((lo + v * (hi - lo)) / st) * st));
    if (String(val) !== f.i.value) { f.i.value = String(val); f.i.dispatchEvent(new Event('input', { bubbles: true })); }
  };
  document.addEventListener('touchstart', (e) => { if (e.target instanceof Element && e.target.closest('[data-fader]')) e.preventDefault(); }, { passive: false, capture: true });
  document.addEventListener('pointerdown', (e) => {
    const i = e.pointerType !== 'mouse' && e.target instanceof Element && e.target.closest('[data-fader]'); if (!i) return;
    fing = { i, id: e.pointerId }; try { i.setPointerCapture(e.pointerId); } catch { } faderAt(fing, e);
  });
  document.addEventListener('pointermove', (e) => { if (fing && e.pointerId === fing.id) faderAt(fing, e); });
  const lift = (e) => { if (fing && e.pointerId === fing.id) { fing.i.dispatchEvent(new Event('change', { bubbles: true })); fing = null; } };
  document.addEventListener('pointerup', lift); document.addEventListener('pointercancel', lift);
  // a fader (a range input marked data-fader, see-through over the slider drawn under it): its cap (--v, 0 at the bottom to 1 at the top, on the input's
  // parent) follows the pointer at once; the server draws it again as the player took it a moment later
  document.addEventListener('input', (e) => {
    const i = e.target; if (!(i instanceof HTMLInputElement) || !i.matches('[data-fader]') || !i.parentElement) return;
    const lo = +i.min || 0, hi = +i.max || 100, v = (i.valueAsNumber - lo) / Math.max(1, hi - lo);
    i.parentElement.style.setProperty('--v', Math.min(1, Math.max(0, v)).toFixed(3));
  });
  return {
    /** The player's EQ as set (five bands, dB) and whether MEGA BASS is on (on, the bass band runs about 9 dB above its own setting). */
    eqState() {
      const f = window.flacie; if (!f || !f.eqGet) return { bands: [0, 0, 0, 0, 0], mega: false };
      const bands = f.eqGet(); let mega = false;
      try { mega = f.megaOn ? f.megaOn() : f.bassLevel() - bands[0] > 4.5; } catch { }
      return { bands, mega };
    },
    /** A scene is on show: its display lines (.rt-mq) scroll like the iPod's, from their first letter, only when too long (flacie.js marquee). */
    start(root) { if (mq) mq.stop(); mq = root && window.flacie && flacie.marqueeIn ? flacie.marqueeIn(root, ".rt-mq") : null; },
    stop() { if (mq) mq.stop(); mq = null; },
    /** An element's own width in CSS pixels (a click's offset is measured in them, whatever turns or tilts the element). */
    size(sel) { const e = document.querySelector(sel); return e ? Math.round(e.offsetWidth) : 300; },
    /** The needle dropping into the groove: a soft thump and a short crackle, made here (Web Audio, quiet; never with reduced motion). */
    crackle() {
      if (matchMedia('(prefers-reduced-motion: reduce)').matches) return;
      try {
        const AC = window.AudioContext || window.webkitAudioContext; if (!AC) return;
        ctx = ctx || new AC(); if (ctx.state === 'suspended') ctx.resume().catch(() => { });
        const rate = ctx.sampleRate, n = Math.floor(rate * 0.5), buf = ctx.createBuffer(1, n, rate), d = buf.getChannelData(0);
        for (let i = 0; i < n; i++) {
          const t = i / rate, env = Math.exp(-t * 6);
          const crackle = Math.random() < 0.015 ? (Math.random() * 2 - 1) * 0.9 : (Math.random() * 2 - 1) * 0.06;
          const thump = t < 0.05 ? Math.sin(2 * Math.PI * 55 * t) * (1 - t / 0.05) * 0.8 : 0;
          d[i] = crackle * env + thump;
        }
        const src = ctx.createBufferSource(); src.buffer = buf;
        const lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 7000;
        const g = ctx.createGain(); g.gain.value = 0.1;
        src.connect(lp); lp.connect(g); g.connect(ctx.destination); src.start();
      } catch { }
    },
  };
})();
