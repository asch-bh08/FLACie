Beta 46 (Android 61, MSI 0.46.0). Still a beta, not a 1.0 final.

**Headphone test (web, Windows, Android)**
- A new **Headphone test** page (web and Windows: Tools > Headphone test; Android: Settings > Headphone test). Test tones are made on the device, nothing is downloaded or sent anywhere, and any music stops while you test.
  - **Left and right**: left only, right only, both, alternating, to check each side is clear, equally loud and on the right side.
  - **Frequency slider**: one steady tone from 20 Hz to 20 kHz on a log slider (type an exact Hz on the web), presets (40 Hz to 16 kHz), left / both / right, sine / triangle / square / saw, and full, bass and treble sweeps. The web page also draws a live spectrum with a marker on the frequency being played.
  - **Bass test**: steps from 20 to 125 Hz, and a kick drum loop to judge depth, punch and how cleanly the bass stops.
  - **Treble and clarity**: steps from 4 to 20 kHz, clicks, hi-hat, “sss” sibilance, two close tones that should wobble slowly (4 and 12 Hz apart), and pink and white noise.
  - **3D and stereo image**: the sound circles around you, sweeps across the front, goes front to back and low to high (the web uses the browser's head-related 3D panner; Android uses a time-and-level model of the two ears), with a picture of where it is; plus an in-phase and out-of-phase noise check.
  - Safety: the level starts low and is capped, every start and stop fades, **Stop** or Escape ends a test. A short note on setting up an amp for 250 Ω headphones is at the bottom.

**Mini player (picture in picture) (web only)**
- A new button in the player bar opens a small always-on-top window with the cover, title, artist, a seek bar and previous / play-pause / next, which stays visible when you switch tabs or apps (Chrome and Edge: a real mini window; other browsers: a video picture-in-picture of the cover with the browser's own play / previous / next buttons). Chrome can also open it on its own when you switch tabs while music plays.

**Downloads fix**
- Retry all no longer starts everything at once (three at a time), a source that errors or times out is treated as "had nothing" so the next source is tried, and the Failed list shows one row per song, hiding songs a later try already downloaded.

**Tested**
- Web: the headphone page opens, the buttons start tones, the Hz slider and presets follow, a real signal shows in the live spectrum (browser test), the 3D test moves, Stop stops; the mini-player button appears in Chrome. Android: compiles, unit tests and the release build pass; see the list below for what ran on the emulator.

**Not tested**
- Hearing any of it: the tones, the sweeps and the 3D effect were checked as signals and on-screen state, not with ears or on a DT 990 Pro. How believable the 3D test is depends on your ears and headphones.
- The mini window itself (opening and its buttons) in a real desktop Chrome, Firefox or Safari; Chrome's automatic opening on tab switch.
- Android on a real phone; the Windows MSI installer itself.
