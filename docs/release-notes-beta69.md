Beta 69 (Android 84, MSI 0.69.0, FLACie Web 1.0-beta69). Still a beta, not a 1.0 final.

**New (web and Windows): listening scenes.** In iPod mode, the panel's new Scene row swaps the iPod for another way to listen. Every scene shows the song playing and drives the real player: play/pause, next, previous, and the media lying about play an album coming up or one of your playlists. Switching scenes never stops the music, and the panel's "How to use" explains the scene on show.
- **Car:** a night drive behind a rain-streaked windshield with a sweeping wiper. A CD head unit with an orange dot-matrix display that tunes to a station on each new song, a CD slot (eject slides the disc out), a volume knob (scroll or arrow keys; press to mute), six presets (your playlists, then the albums coming up) and climate dials.
- **Hi-fi:** a 1970s living room. The record spins at the speed the pitch slider sets, and the tonearm follows the song. Click the record to drop the needle further in, with a quiet crackle on each new song. Flip through a crate of sleeves. A silver receiver has green meters on the music and a tuning knob that seeks.
- **Boombox:** a workbench and a dual-cassette boombox with stickers. Deck A's reels turn and its counter runs. The woofers and EQ sliders move with the music. Stop/eject drops the door open, and Deck B holds what comes next.
- **MiniDisc:** a 2000s desk and a brushed-aluminium micro system. Eject pops the disc up out of the top-loading slot. A blue backlit LCD scrolls the title, and the jog dial skips (scroll or arrow keys) and plays or pauses from its centre. A fan of MiniDiscs on the desk holds the albums coming up and your playlists.
- The scenes fit desktops, laptops with the panel open, tablets and phones, and hold still with reduced motion.

**Changed: the iPod desk.**
- **Desk options** in the panel: earbuds or studio headphones (coiled cable and adapter), a VU meter, a cassette, MiniDiscs, a 30-pin dock and a pencil, each on or off.
- **A tidier desk:** the iPod sits in the middle, the notebook and VU meter on the left, the CDs, cassette and MiniDiscs on the right, and the coffee in the corner. The cables never lie across anything.
- **The notebook** has two pages that turn when they fill up. On a phone it's a pad of sticky notes, and a full one peels off.
- **Lyrics** are timed in the browser, so they're no longer late. A long line wraps and is written in row by row.
- **The open CD** is the album playing.
- **A queue card** shows the song playing with its year, then what comes next.
- **The earbuds** react more strongly, and their cable runs smoothly. The coffee ripples on the bass, read from the same stream as the earbuds, so nothing is downloaded again.
- **iPod mode** opens on Now Playing when a song is playing.
- **iPod screens:** long titles scroll across, as on a real iPod, instead of being cut off. With reduced motion they end in "…".

**Tested** (this build)
- Android unit tests: 414 run, 2 skipped (as before), 0 failed (no Android code changed). .NET tests: 686 pass (one timing test, "ten thousand songs build fast", failed once while the Android build ran alongside it and passed alone and in the full rerun). Browser tests: the full suite, 219 of 220 passed on the branch. The one failure was the new scenes click test, which was too strict about a fanned stack of five MiniDiscs. It was corrected and passes with five discs on the desk. After the merge and the version bump: the About-version, iPod and scenes specs (1d-g2, 1m-ipod, 1r-retro-scenes), 55 of 55.
- Every scene and the desk were checked at 1600x900, 1024x768 and 390x844 in Chromium: no console errors, no sideways scrolling, and play/pause works. A new test presses every control in every scene at four sizes and checks that the press lands on it. It found, and now guards, the car's climate dials covering its keys and the hi-fi's receiver covering the turntable at tablet sizes.
- Release APK versionCode 84, signed with the owner key (SHA-256 6a4eb335...97a106). Installed on a clean emulator and run against the 10,000-song mock: 23 of 23 checks passed (Quick Connect sign-in, 10,000 songs after 42 s, scrolling, play, a 40-skip burst, pause, kill and relaunch with the queue restored and not auto-playing, Settings > Storage and Clear cache, Up next Play next / Move up / Move down / Remove, no crash, no ANR).
- The MSI (0.69.0) was built and its app started from the build folder: the server answered /healthz "ok" and stopped when the app closed. Not installed.
- FLACie Web was deployed to the server (rollback tag first); /healthz "ok", and it serves the scene files with the final fixes.

**Not tested**
- The scenes and the desk on a real phone or tablet (checked at phone, tablet and laptop sizes in Chromium), and with real music (the test server's songs are 3 seconds of silence, so the meters, woofers and coffee were checked against a synthetic signal).
- **Not built:** Chromecast (docs/spec/chromecast.md).
