// The Android app plays songs in its media service, not in this page. The visualisers (the desk's earbuds and preamp, the scenes' meters) read
// flacie.vizFrame(); here its frames come from the app, made from the decoded sound (NativeOutput pushes about thirty a second), in the same shape:
// freq (1024 bins, 0-255), rate, playing. The song's clock (the journal's lyrics) comes with them. The Info tab's live graphs need the page's own audio: off here.
(() => {
  const f = window.flacie;
  if (!f) return;
  let frame = null;
  const clock = [0, 0, true];
  f.nativeFrame = (bins, rate, playing, pos, dur) => {
    if (!frame) frame = { freq: bins, playing, rate, fileRate: rate };
    else { frame.freq = bins; frame.playing = playing; frame.rate = rate; frame.fileRate = rate; }
    clock[0] = pos; clock[1] = dur; clock[2] = !playing;
  };
  f.vizFrame = () => frame;
  f.clock = () => clock;
  f.viz = () => false;
  f.vizStop = () => { };
  f.vizRelease = () => { };
})();
