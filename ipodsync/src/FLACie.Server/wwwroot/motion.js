// ==== Motion (owner, overhaul 10 Nov: "peak 2026" motion everywhere). The styles are in design-j-motion.css; this does the parts CSS cannot:
//  - a page change cross-fades (the browser's view transitions), and an album card's cover flies into the album page's header;
//  - Now Playing opens with the pressed cover (a row's, a card's, the player bar's) flying into the big cover, and closes with it flying back;
//  - menus and pop-ups spring open from the button that opened them and fade out when they close;
//  - the chosen pill in tabs and segmented buttons slides to the new choice;
//  - rows that move, appear or go (Up next, playlists, favourites) glide to their new place instead of jumping;
//  - the play / pause and heart icons pop as they change;
//  - Retro's scenes, looks and models cross-fade with a slide.
// Settings → Animations off (:root.pref-calm) or a computer asking for less motion: none of it runs, everything is instant.
// Speed: only transform and opacity move; positions are all read first and then all written (no back-and-forth that makes the browser lay out twice).
(() => {
  const root = document.documentElement;
  const reduce = window.matchMedia ? matchMedia("(prefers-reduced-motion: reduce)") : { matches: false };
  const on = () => !root.classList.contains("pref-calm") && !reduce.matches;
  const vtOK = () => typeof document.startViewTransition === "function" && on();
  const spring = "cubic-bezier(.34, 1.4, .64, 1)", ease = "cubic-bezier(.2, .8, .2, 1)";
  // flacieMotion.runs counts the view transitions started (the tests read it); .active is true while one runs
  const M = window.flacieMotion = { runs: 0, last: null, active: false, on };

  // ---- small helpers ----
  const named = new Set();
  const name = (el, n) => { if (!el) return; el.style.viewTransitionName = n; named.add(el); };
  const unname = el => { if (!el) return; el.style.viewTransitionName = ""; named.delete(el); };
  const visible = el => { if (!el || !el.isConnected) return false; const r = el.getBoundingClientRect(); return r.width > 4 && r.height > 4 && r.bottom > 0 && r.top < innerHeight; };
  // resolves when test() is true (checked on each change to the page), or after ms
  const until = (test, ms) => new Promise(res => {
    if (test()) return res(true);
    const mo = new MutationObserver(() => { if (test()) end(true); });
    const end = v => { mo.disconnect(); clearTimeout(t); res(v); };
    mo.observe(document.body, { childList: true, subtree: true, attributes: true });
    const t = setTimeout(() => end(test()), ms);
  });
  // resolves once the area has changed and then stayed still for `quiet` ms; if nothing changes within `first` ms, or after `max` ms, it resolves anyway
  const settle = (el, quiet, first, max) => new Promise(res => {
    let qt = 0, seen = false;
    const mo = new MutationObserver(() => { seen = true; clearTimeout(qt); qt = setTimeout(end, quiet); });
    const end = () => { mo.disconnect(); clearTimeout(qt); clearTimeout(cap); clearTimeout(ft); res(); };
    mo.observe(el, { childList: true, subtree: true, characterData: true, attributes: true });
    const cap = setTimeout(end, max), ft = setTimeout(() => { if (!seen) end(); }, first);
  });

  // a view transition of one kind; the class vt-<kind> on <html> lets the styles name the parts that take part, only while it runs
  const run = (kind, update) => {
    if (!vtOK()) return null;
    root.classList.add("vt-" + kind);
    let vt;
    try { vt = document.startViewTransition(update); } catch { root.classList.remove("vt-" + kind); return null; }
    M.runs++; M.last = kind; M.active = true;
    const clean = () => {
      root.classList.remove("vt-" + kind);
      for (const el of [...named]) unname(el);
      M.active = false;
      // a transition that ended without Now Playing open leaves nothing behind
      if (!document.querySelector(".np")) root.classList.remove("np-morph", "np-hide");
    };
    vt.finished.then(clean, clean);
    vt.ready.catch(() => { }); vt.updateCallbackDone.catch(() => { });
    return vt;
  };

  // ---- page changes: around every change of address (a link, NavigationManager, Back / Forward), not the #now-playing that only opens the player ----
  let path = location.pathname, heroFrom = null, heroAt = 0;
  const pageMove = () => {
    const hero = heroFrom && performance.now() - heroAt < 1500 && visible(heroFrom) ? heroFrom : null;
    heroFrom = null;
    if (!vtOK() || document.querySelector(".np")) return;
    name(hero, "m-hero");
    run("page", async () => {
      unname(hero);   // the old page is in the snapshot; the same name must not be on two things in the new one
      const main = document.querySelector("main.content") || document.body;
      await settle(main, 70, 450, 700);
      if (hero) { const to = [...document.querySelectorAll(".detail-head .cover")].find(visible); if (to) name(to, "m-hero"); }
    });
  };
  for (const k of ["pushState", "replaceState"]) {
    const orig = history[k];
    history[k] = function (...a) {
      const before = location.pathname, r = orig.apply(this, a);
      path = location.pathname;
      if (k === "pushState" && path !== before) pageMove();
      return r;
    };
  }
  addEventListener("popstate", () => { if (location.pathname !== path) { path = location.pathname; pageMove(); } });

  // ---- Now Playing: the pressed cover flies into the big one. [data-np] marks what opens the player (the player bar's song, the current row, Home's picks) ----
  const npCover = () => [...document.querySelectorAll(".np .ls-cover .cover, .np .ls-hero .cover")].find(visible) || null;
  const barCover = () => document.querySelector(".player .bar-song .cover");
  const npOpen = src => {
    if (!vtOK() || document.querySelector(".np")) return;
    let from = src.querySelector(".cover") || (src.closest(".player") ? barCover() : null);
    if (!visible(from)) from = null;
    name(from, "m-cover");
    root.classList.add("np-morph");
    run("np", async () => {
      await until(() => !!document.querySelector(".np"), 900);
      unname(from);
      const to = npCover();
      if (from && to) name(to, "m-cover");
    });
  };
  // flacie.js calls this as the player starts to close (Back, Esc, the ⌄): the big cover flies back to the player bar
  M.npClosing = () => {
    if (!vtOK() || !document.querySelector(".np") || M.active) return;
    const from = npCover();
    name(from, "m-cover");
    root.classList.add("np-morph");
    run("np", async () => {
      await until(() => !document.querySelector(".np:not(.closing)"), 600);
      root.classList.add("np-hide");
      unname(from);
      const to = barCover();
      if (from && visible(to)) name(to, "m-cover");
    });
  };

  // ---- Retro: a new scene, look, model or colour cross-fades with a slide (the panel stays) ----
  const sceneMove = btn => {
    const stage = document.querySelector(".ipod-stage");
    if (!stage || btn.classList.contains("on") || !vtOK()) return;
    const was = ["data-scene", "data-look", "data-model", "data-color"].map(a => stage.getAttribute(a)).join("|");
    run("scene", async () => {
      await until(() => { const s = document.querySelector(".ipod-stage"); return !s || ["data-scene", "data-look", "data-model", "data-color"].map(a => s.getAttribute(a)).join("|") !== was; }, 600);
      await new Promise(r => requestAnimationFrame(() => r()));
    });
  };

  // ---- what was pressed: for the pop-up's spring point, the flying covers, and the rows' places before a change ----
  const POP = ".menu, .account-menu, .devpop, .dd-menu, .dl-menu";
  const LIST = ".upnext, .tracks:not(.virtual), .grid, .row-scroll, .pl-list";
  let pressed = null, pressedAt = 0, snap = null;
  const keyOf = el => (el.dataset.key || el.textContent || "").replace(/\s+/g, " ").trim().slice(0, 240);
  const snapList = list => {
    if (!on()) return;
    const kids = [...list.children].slice(0, 80);
    snap = { list, at: performance.now(), items: kids.map(k => ({ key: keyOf(k), r: k.getBoundingClientRect() })) };
  };
  const press = e => {
    const t = e.target instanceof Element ? e.target : null;
    if (!t) return;
    pressed = t.closest("button, a, [role=button], [tabindex]") || t; pressedAt = performance.now();
    const list = t.closest(LIST);
    if (list) snapList(list);
  };
  document.addEventListener("pointerdown", press, true);
  document.addEventListener("keydown", e => { if (e.key === "Enter" || e.key === " ") press(e); }, true);
  document.addEventListener("click", e => {
    const t = e.target instanceof Element ? e.target : null;
    if (!t) return;
    if (!snap || performance.now() - snap.at > 400) { const list = t.closest(LIST); if (list) snapList(list); }
    const np = t.closest("[data-np]");
    if (np) { npOpen(np); return; }
    const a = t.closest("a[href]");
    if (a && /\/(album|artist|playlist)\//.test(a.getAttribute("href") || "")) {
      const c = (a.closest(".card") || a).querySelector(".cover");
      if (c) { heroFrom = c; heroAt = performance.now(); }
    }
    const scene = t.closest(".scene-chips button[data-scene], .ipod-dock button[data-look], .dock-models button[data-model], .ipod-dock .swatch");
    if (scene) sceneMove(scene);
  }, true);

  // ---- pop-ups: the spring point is the button that opened it ----
  const popIn = pops => {
    const opener = pressed && performance.now() - pressedAt < 1500 && pressed.isConnected ? pressed : null;
    if (!opener) return;
    const o = opener.getBoundingClientRect();
    const reads = pops.map(p => [p, p.getBoundingClientRect()]);
    for (const [p, r] of reads) {
      if (!r.width) continue;
      const x = Math.max(0, Math.min(r.width, o.left + o.width / 2 - r.left)), y = Math.max(0, Math.min(r.height, o.top + o.height / 2 - r.top));
      p.style.setProperty("--m-o", `${Math.round(x)}px ${Math.round(y)}px`);
    }
  };
  // a pop-up taken out of the page: a copy stays a moment in its place and fades (inert, hidden from screen readers, no ids or test ids)
  const popOut = (node, parent, next) => {
    if (!parent.isConnected) return;
    const g = node.cloneNode(true);
    for (const el of [g, ...g.querySelectorAll("[id], [data-testid]")]) { el.removeAttribute("id"); el.removeAttribute("data-testid"); }
    g.classList.add("m-ghost"); g.setAttribute("aria-hidden", "true"); g.inert = true;
    try { parent.insertBefore(g, next && next.parentNode === parent ? next : null); } catch { return; }
    const done = () => g.remove();
    g.addEventListener("animationend", done, { once: true });
    setTimeout(done, 260);
  };

  // ---- rows glide (FLIP: where each row was, where it is now, a transform from the old place to the new) ----
  const flip = () => {
    const s = snap; snap = null;
    if (!s.list.isConnected) return;
    const old = new Map();
    for (const it of s.items) { const q = old.get(it.key) || []; q.push(it.r); old.set(it.key, q); }
    const kids = [...s.list.children].slice(0, 80);
    const reads = kids.map(k => { const q = old.get(keyOf(k)); return [k, q && q.length ? q.shift() : null, k.getBoundingClientRect()]; });
    for (const [k, was, now] of reads) {
      if (now.bottom < 0 || now.top > innerHeight) continue;
      if (!was) { k.animate([{ opacity: 0, transform: "scale(.96)" }, { opacity: 1, transform: "none" }], { duration: 260, easing: ease }); continue; }
      const dx = was.left - now.left, dy = was.top - now.top;
      if (Math.abs(dx) < 1 && Math.abs(dy) < 1) continue;
      k.animate([{ transform: `translate(${dx}px, ${dy}px)` }, { transform: "none" }], { duration: 380, easing: spring });
    }
  };

  // ---- the chosen pill slides from the old choice to the new one ----
  const SEG = ".seg, .ls-seg, .mood-chips, .sr-kinds, .hp-tabs, .graph-tabs, .tabs, .dock-seg, .subnav";
  const selNow = el => el.getAttribute("aria-selected") === "true" || el.getAttribute("aria-pressed") === "true" || el.getAttribute("aria-checked") === "true" || /\b(on|sel|active)\b/.test(el.className || "");
  const selWas = (el, attr, old) => {
    const v = a => a === attr ? old : el.getAttribute(a);
    return v("aria-selected") === "true" || v("aria-pressed") === "true" || v("aria-checked") === "true" || /\b(on|sel|active)\b/.test((attr === "class" ? old : el.className) || "");
  };
  const slide = (group, from, to) => {
    const g = group.getBoundingClientRect(), a = from.getBoundingClientRect(), b = to.getBoundingClientRect();
    const cs = getComputedStyle(to), gs = getComputedStyle(group);
    const bg = cs.backgroundColor;
    if (!b.width || !a.width || !bg || bg === "transparent" || /rgba\(.*,\s*0\)$/.test(bg)) return;
    const live = gs.position === "static";
    const pill = document.createElement("span");
    pill.className = "m-pill"; pill.setAttribute("aria-hidden", "true");
    const ox = group.clientLeft - group.scrollLeft, oy = group.clientTop - group.scrollTop;
    Object.assign(pill.style, { width: b.width + "px", height: b.height + "px", left: (b.left - g.left - ox) + "px", top: (b.top - g.top - oy) + "px", background: bg, borderRadius: cs.borderRadius });
    if (live) group.classList.add("m-seg-live");
    to.classList.add("m-pill-to");
    group.appendChild(pill);
    const an = pill.animate([{ transform: `translate(${a.left - b.left}px, ${a.top - b.top}px) scale(${a.width / b.width}, ${a.height / b.height})` }, { transform: "none" }], { duration: 340, easing: spring });
    const done = () => { pill.remove(); to.classList.remove("m-pill-to"); if (live) group.classList.remove("m-seg-live"); };
    an.finished.then(done, done);
  };

  // ---- icons that change (play ↔ pause, the heart) pop into place ----
  const ICON = ".play, .ls-play, .fav, .ls-heart, .ls-dock-heart";
  const pop = svg => svg.animate([{ transform: "scale(.55) rotate(-14deg)", opacity: .2 }, { transform: "scale(1.12)", opacity: 1, offset: .6 }, { transform: "none", opacity: 1 }], { duration: 340, easing: ease });

  // one watcher for all of it; each change list is gone through once, reads before writes
  new MutationObserver(muts => {
    if (root.classList.contains("np-morph") && !M.active && !document.querySelector(".np")) root.classList.remove("np-morph", "np-hide");
    if (!on()) { snap = null; return; }
    const pops = [], svgs = new Set(), segs = new Map();
    let listTouched = false;
    for (const m of muts) {
      const t = m.target;
      if (snap && !listTouched && snap.list.contains(t)) listTouched = true;
      if (m.type === "childList") {
        for (const n of m.addedNodes) if (n.nodeType === 1 && !n.classList.contains("m-ghost") && !n.classList.contains("m-pill") && n.matches(POP)) pops.push(n);
        for (const n of m.removedNodes) if (n.nodeType === 1 && !n.classList.contains("m-ghost") && n.matches(POP) && ![...m.addedNodes].some(a => a.nodeType === 1 && a.matches(POP))) popOut(n, t, m.nextSibling);
        const svg = t.nodeType === 1 ? (t.closest ? t.closest("svg") : null) : null;
        if (svg && svg.parentElement && svg.parentElement.closest(ICON)) svgs.add(svg);
      } else if (m.type === "attributes" && t.nodeType === 1 && t.parentElement && t.parentElement.matches(SEG)) {
        const g = t.parentElement, e = segs.get(g) || {};
        if (selNow(t) && !selWas(t, m.attributeName, m.oldValue)) e.to = t;
        else if (!selNow(t) && selWas(t, m.attributeName, m.oldValue)) e.from = t;
        segs.set(g, e);
      }
    }
    if (listTouched && snap && performance.now() - snap.at < 2500) flip();
    if (pops.length) popIn(pops);
    for (const [g, e] of segs) if (e.to && e.from && e.to !== e.from) slide(g, e.from, e.to);
    // (an icon's first drawing is not a change: only later ones pop)
    for (const s of svgs) { if (s.__mSeen) pop(s); s.__mSeen = true; }
  }).observe(document.documentElement, { childList: true, subtree: true, characterData: true, attributes: true, attributeOldValue: true, attributeFilter: ["class", "aria-selected", "aria-pressed", "aria-checked"] });
})();
