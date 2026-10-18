// The Android app plays the music, not this page. window.FLACieAndroid is the app's interface (RetroBridge.kt): call(name, arg) answers at once.
// The visualisers (the desk's earbuds and preamp, the scenes' meters) read flacie.vizFrame(); here its frames come from the app's player, made
// from the decoded sound, in the same shape: freq (1024 bins, 0-255), rate, playing. The song's clock (the journal's lyrics) comes from the app too.
(() => {
  const app = window.FLACieAndroid;
  window.flacieAndroid = {
    call: (name, arg) => { try { return app ? String(app.call(name, arg == null ? "" : String(arg))) : ""; } catch (e) { return ""; } },
    close: (path) => { try { if (app) app.call("close", path || "/"); else history.back(); } catch (e) { } },
  };
  const f = window.flacie;
  if (!f) return;
  const frame = { freq: new Uint8Array(1024), playing: false, rate: 44100, fileRate: 44100 };
  const clock = [0, 0, true];
  let at = 0, have = false;
  function pull() {
    const now = performance.now();
    if (now - at < 30) return;
    at = now;
    const s = window.flacieAndroid.call("frame", "");
    if (!s) return;
    // "rate|playing|pos|dur|b0,b1,..."
    const parts = s.split("|");
    if (parts.length < 5) return;
    frame.rate = frame.fileRate = +parts[0] || 44100;
    frame.playing = parts[1] === "1";
    clock[0] = (+parts[2] || 0) / 1000; clock[1] = (+parts[3] || 0) / 1000; clock[2] = !frame.playing;
    const bins = parts[4].length ? parts[4].split(",") : [];
    for (let i = 0; i < frame.freq.length; i++) frame.freq[i] = i < bins.length ? +bins[i] : 0;
    have = true;
  }
  f.vizFrame = () => { pull(); return have ? frame : null; };
  f.clock = () => { pull(); return clock; };
  f.viz = () => false;
  f.vizStop = () => { };
  f.vizRelease = () => { };
})();
