// iPod mode › Scene: small helpers for the listening scenes (desk.js runs their moving parts: the analyser, the needles, the bands).
window.retro = (() => {
  let ctx = null;
  // a knob or dial turned by the wheel (data-wheel) keeps the page from scrolling under it: Blazor's own wheel listener is passive, so it can't
  document.addEventListener('wheel', (e) => { if (e.target instanceof Element && e.target.closest('[data-wheel]')) e.preventDefault(); }, { passive: false, capture: true });
  let mq = null;
  // a knob (data-knob) turned by dragging up or down: every 12 px is one step, sent to the knob as an arrow key, so the knob's own key handler does the rest
  let drag = null;
  document.addEventListener('pointerdown', (e) => {
    const k = e.target instanceof Element && e.target.closest('[data-knob]'); if (!k || e.button !== 0) return;
    drag = { k, y: e.clientY }; k.focus(); try { k.setPointerCapture(e.pointerId); } catch { } e.preventDefault();
  });
  document.addEventListener('pointermove', (e) => {
    if (!drag) return;
    let d = drag.y - e.clientY;
    while (Math.abs(d) >= 12) {
      const up = d > 0; drag.k.dispatchEvent(new KeyboardEvent('keydown', { key: up ? 'ArrowUp' : 'ArrowDown', bubbles: true }));
      drag.y -= up ? 12 : -12; d = drag.y - e.clientY;
    }
  });
  const end = () => { drag = null; };
  document.addEventListener('pointerup', end); document.addEventListener('pointercancel', end);
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
      try { mega = f.bassLevel() - bands[0] > 4.5; } catch { }
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
