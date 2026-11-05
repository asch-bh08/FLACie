// F19: the mini player (Document Picture-in-Picture), built like the full player: the cover's colours, the song's loudness as the seek bar with times,
// shuffle, repeat, heart, DJ and mic, a "Next:" line and the line being sung. A window wider than it is tall becomes one bar with the cover on the left.
// The controls press the player bar's own buttons in the page (data-pip="..."), so the mini player always does exactly what the bar does, and their state
// (on/off, the heart) is read back from them. Styles: mini-player.css (inlined into the window, which has no stylesheet of its own).
window.flaciePip = (() => {
  const I = (d, s = 20) => `<svg viewBox="0 0 24 24" width="${s}" height="${s}" fill="currentColor" aria-hidden="true">${d}</svg>`;
  const ICON = {
    prev: I('<path d="M6 5h2v14H6zM20 5v14L9 12z"/>', 22), next: I('<path d="M16 5h2v14h-2zM4 5v14l11-7z"/>', 22),
    play: I('<path d="M7 4.5v15a1 1 0 0 0 1.5.86l12-7.5a1 1 0 0 0 0-1.72l-12-7.5A1 1 0 0 0 7 4.5z"/>', 26),
    pause: I('<rect x="6" y="4" width="4" height="16" rx="1"/><rect x="14" y="4" width="4" height="16" rx="1"/>', 26),
    shuffle: I('<path d="M17 3l4 4-4 4V8h-2.6l-3 3.3-1.4-1.5L13.6 6H17zM3 6h4.6l9.8 11H17v-3l4 4-4 4v-3h-2.4L4.8 8H3zm6.1 6.7l1.4 1.5L7.6 17H3v-2h3.7z"/>'),
    repeat: I('<path d="M7 7h10v3l4-4-4-4v3H5v6h2zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2z"/>'),
    heart: I('<path d="M12 21l-1.4-1.3C5.4 15 2 11.9 2 8.1 2 5 4.4 2.6 7.5 2.6c1.7 0 3.4.8 4.5 2.1 1.1-1.3 2.8-2.1 4.5-2.1C19.6 2.6 22 5 22 8.1c0 3.8-3.4 6.9-8.6 11.6zM7.5 4.6C5.5 4.6 4 6.1 4 8.1c0 2.8 2.9 5.5 8 10.1 5.1-4.6 8-7.3 8-10.1 0-2-1.5-3.5-3.5-3.5-1.5 0-3 1-3.6 2.4h-1.8C10.5 5.6 9 4.6 7.5 4.6z"/>'),
    heartOn: I('<path d="M12 21l-1.4-1.3C5.4 15 2 11.9 2 8.1 2 5 4.4 2.6 7.5 2.6c1.7 0 3.4.8 4.5 2.1 1.1-1.3 2.8-2.1 4.5-2.1C19.6 2.6 22 5 22 8.1c0 3.8-3.4 6.9-8.6 11.6z"/>'),
    dj: I('<path d="M12 3a9 9 0 0 0-9 9v5a3 3 0 0 0 3 3h2v-8H5a7 7 0 0 1 14 0h-3v8h2a3 3 0 0 0 3-3v-5a9 9 0 0 0-9-9z"/>'),
    mic: I('<path d="M12 14a3 3 0 0 0 3-3V5a3 3 0 0 0-6 0v6a3 3 0 0 0 3 3zm5-3a5 5 0 0 1-10 0H5a7 7 0 0 0 6 6.9V21h2v-3.1a7 7 0 0 0 6-6.9z"/>'),
  };
  const fmt = (s) => { s = Math.max(0, Math.floor(s || 0)); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`; };
  const bar = () => document.querySelector('section.player');
  const ctl = (name) => bar()?.querySelector(name === 'mic' ? '.dj-mic' : `[data-pip="${name}"]`);

  /** Builds the window's content; [audio] is the page's player, [onPrev]/[onNext] go to the .NET player. Returns the view, painted by paint(). */
  function build(win, audio, css, onPrev, onNext) {
    const d = win.document;
    const st = d.createElement('style'); st.textContent = css; d.head.append(st);
    d.title = 'FLACie';
    d.body.innerHTML = `<main class="mp" data-testid="mini-player">
      <div class="mp-art"><img alt=""></div>
      <div class="mp-side">
        <div class="mp-text"><b class="mp-title"></b><span class="mp-artist"></span><span class="mp-lyric" aria-live="off"></span></div>
        <div class="mp-seek"><span class="mp-pos">0:00</span><canvas class="mp-wave" aria-hidden="true"></canvas><input type="range" min="0" max="1" step="1" value="0" aria-label="Seek"><span class="mp-dur">0:00</span></div>
        <div class="mp-ctl">
          <button class="mp-b" data-do="shuffle" aria-label="Shuffle">${ICON.shuffle}</button>
          <button class="mp-b" data-do="prev" aria-label="Previous">${ICON.prev}</button>
          <button class="mp-b mp-play" data-do="play" aria-label="Play">${ICON.play}</button>
          <button class="mp-b" data-do="next" aria-label="Next">${ICON.next}</button>
          <button class="mp-b" data-do="repeat" aria-label="Repeat">${ICON.repeat}</button>
        </div>
        <div class="mp-extra">
          <button class="mp-b" data-do="heart" aria-label="Add to favourites">${ICON.heart}</button>
          <button class="mp-b" data-do="dj" aria-label="DJ">${ICON.dj}</button>
          <button class="mp-b" data-do="mic" aria-label="Talk to the DJ">${ICON.mic}</button>
        </div>
        <p class="mp-next"></p>
      </div></main>`;
    const q = (s) => d.querySelector(s);
    const v = { d, img: q('.mp-art img'), title: q('.mp-title'), artist: q('.mp-artist'), lyric: q('.mp-lyric'), pos: q('.mp-pos'), dur: q('.mp-dur'),
      seek: q('.mp-seek input'), wave: q('.mp-wave'), play: q('.mp-play'), next: q('.mp-next'), shape: null, shapeFor: '', lyrics: null, art: '' };
    d.querySelectorAll('[data-do]').forEach((b) => b.addEventListener('click', () => {
      const what = b.dataset.do;
      if (what === 'prev') onPrev(); else if (what === 'next') onNext();
      else if (what === 'play') { if (audio.paused) audio.play().catch(() => { }); else audio.pause(); }
      else ctl(what)?.click();
      setTimeout(() => paint(v, audio), 150);
    }));
    v.seek.addEventListener('input', () => { audio.currentTime = +v.seek.value; paint(v, audio); });
    // one bar with the cover on the left when the window is wider than it is tall
    const shape = () => { d.body.classList.toggle('wide', win.innerWidth > win.innerHeight * 1.25); paint(v, audio); };
    win.addEventListener('resize', shape); shape();
    // the bar's buttons change (shuffle on, the heart, a new song's next line): repaint
    if (bar()) { const mo = new MutationObserver(() => paint(v, audio)); mo.observe(bar(), { subtree: true, attributes: true, childList: true, characterData: true }); win.addEventListener('pagehide', () => mo.disconnect()); }
    return v;
  }

  function drawWave(v, played) {
    const c = v.wave, dpr = v.d.defaultView?.devicePixelRatio || 1, w = Math.round(c.clientWidth * dpr), h = Math.round(c.clientHeight * dpr);
    if (!w || !h) return;
    if (c.width !== w || c.height !== h) { c.width = w; c.height = h; }
    const g = c.getContext('2d'), n = Math.max(24, Math.floor(c.clientWidth / 4)), bw = Math.max(1, w / n * 0.6), cs = getComputedStyle(v.d.body);
    const on = cs.getPropertyValue('--np-accent').trim() || '#e8774f', off = 'rgba(244,239,230,.25)';
    g.clearRect(0, 0, w, h);
    const s = v.shape; let lo = 0, hi = 255;
    if (s) { const sorted = [...s].sort((a, b) => a - b); lo = sorted[Math.floor(sorted.length * .03)]; hi = Math.max(lo + 1, sorted[Math.floor(sorted.length * .97)]); }
    for (let i = 0; i < n; i++) {
      let m = 0;
      if (s) { const a = Math.floor(i / n * s.length), b = Math.max(a + 1, Math.floor((i + 1) / n * s.length)); for (let k = a; k < b; k++) m = Math.max(m, s[k]); }
      const lvl = s ? .15 + .85 * Math.min(1, Math.max(0, (m - lo) / (hi - lo))) : .35, bh = Math.max(2 * dpr, lvl * h);
      g.fillStyle = (i + .5) / n <= played ? on : off;
      g.fillRect(i * w / n + (w / n - bw) / 2, (h - bh) / 2, bw, bh);
    }
  }

  function paint(v, audio, meta) {
    if (!v) return;
    if (meta) {
      v.title.textContent = meta.title || ''; v.artist.textContent = meta.artist || '';
      if (meta.art !== v.art) { v.art = meta.art; v.img.src = meta.art || ''; v.img.style.visibility = meta.art ? 'visible' : 'hidden'; if (meta.art) window.flacie?.npAccent(v.d.body, meta.art); }
    }
    const b = bar();
    const path = b?.dataset.pipPath || '';
    if (path && path !== v.shapeFor) {
      v.shapeFor = path; v.shape = null;
      window.flacie?.npWaveGet('/api/wave?p=' + encodeURIComponent(path)).then((a) => { if (v.shapeFor === path && Array.isArray(a) && a.length) { v.shape = a; drawWave(v, audio.currentTime / (audio.duration || 1)); } }).catch(() => { });
    }
    const nxt = b?.dataset.pipNext || '';
    v.next.textContent = nxt ? 'Next: ' + nxt : ''; v.next.hidden = !nxt;
    v.play.innerHTML = audio.paused ? ICON.play : ICON.pause; v.play.setAttribute('aria-label', audio.paused ? 'Play' : 'Pause');
    const dur = isFinite(audio.duration) ? audio.duration : 0;
    v.seek.max = Math.max(1, Math.floor(dur)); v.seek.value = Math.floor(audio.currentTime || 0);
    v.pos.textContent = fmt(audio.currentTime); v.dur.textContent = fmt(dur);
    drawWave(v, dur > 0 ? audio.currentTime / dur : 0);
    // the buttons follow the bar's: on/off, the repeat mode, the heart
    for (const name of ['shuffle', 'repeat', 'heart', 'dj', 'mic']) {
      const src = ctl(name), mine = v.d.querySelector(`[data-do="${name}"]`);
      if (!mine) continue;
      mine.hidden = !src;
      if (!src) continue;
      const on = src.classList.contains('on') || src.getAttribute('aria-pressed') === 'true';
      mine.classList.toggle('on', on); mine.setAttribute('aria-pressed', on ? 'true' : 'false');
      if (name !== 'mic') mine.setAttribute('aria-label', src.getAttribute('aria-label') || name);
      if (name === 'heart') mine.innerHTML = on ? ICON.heartOn : ICON.heart;
      if (name === 'repeat') mine.classList.toggle('one', /one/i.test(src.getAttribute('aria-label') || ''));
    }
    // the line being sung (synced lyrics only)
    const ly = v.lyrics; let line = '';
    if (ly && ly.length) { const t = (audio.currentTime || 0) * 1000; for (const l of ly) { if (l.t <= t) line = l.text; else break; } }
    v.lyric.textContent = line; v.lyric.hidden = !line;
  }

  return { build, paint, setLyrics(v, lines) { if (v) { v.lyrics = Array.isArray(lines) ? lines : null; } } };
})();
