Beta 47 (Android 62, MSI 0.47.0). Still a beta, not a 1.0 final. A follow-up to beta 46's headphone test.

**Headphone test: clearer, and a much wider range (web, Windows, Android)**
- **1 Hz to 44 kHz.** The frequency slider now starts at 1 Hz (below hearing) and goes up to 44 kHz (above it). A line under the number says what the frequency is: infrasound, sub-bass, bass, mids, presence, treble, or ultrasonic. Tones are made at a 96 kHz sample rate so 44 kHz can exist; your sound card may still cut off lower (the page says what rate it got). Tones below 20 Hz are played quieter on purpose, to protect the drivers.
- **Lower bass tests.** The bass steps now start at 5 Hz (5, 10, 15, 20, 25, 31, 40 … 125), and there are new deep sweeps: 1 → 100 Hz and 1 → 200 Hz.
- **Easier to follow.** Three short steps at the top (start quiet, pick a test, raise the level slowly). The level slider, a Stop button and what is playing stay on screen at the top while you scroll. The tests are in tabs (Left / right, Tone generator, Bass, Treble & clarity, 3D & phase, Amp tips), and each test is one row: a round play button (a square while it plays; press it again to stop), its name, and one line on what you should hear. On the tone generator there are also −1 Hz / +1 Hz / half / double buttons and, on the web, a box to type an exact Hz. The sliders now always show their knob and how far they are set.

**Tested**
- Web (browser test): tabs switch, tones start and stop, a real signal appears in the spectrum, the Hz readout and its description follow 5 Hz, 440 Hz and 30 kHz and the half button, the 3D test runs, pressing a playing test stops it. All web smoke tests pass.
- Android emulator: the new screen opens from Settings > Headphone test, tabs switch and a tone starts and stops.

**Not tested**
- Hearing it, or any of it on real headphones or an amp. Whether anything above about 20 kHz comes out at all depends on your sound card and headphones; tones from 1 to 15 Hz will be mostly inaudible on any headphones.
- The mini window (picture in picture) in a real desktop browser, a real phone, and installing the MSI.
