// iPod mode › Scene: small helpers for the listening scenes (desk.js runs their moving parts: the analyser, the needles, the bands).
window.retro = (() => {
  let ctx = null;
  // a knob or dial turned by the wheel (data-wheel) keeps the page from scrolling under it: Blazor's own wheel listener is passive, so it can't
  document.addEventListener('wheel', (e) => { if (e.target instanceof Element && e.target.closest('[data-wheel]')) e.preventDefault(); }, { passive: false, capture: true });
  return {
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
