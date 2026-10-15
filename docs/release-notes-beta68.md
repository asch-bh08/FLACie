Beta 68 (Android 83, MSI 0.68.0, FLACie Web 1.0-beta68). Still a beta, not a 1.0 final.

**New (web and Windows): the iPod desk.** In iPod mode, Look › Desk lays the iPod emulator on a sunlit oak desk, seen from above in a warm film-photo look. It works with all 23 models and their colours, and every button and the wheel work as on the stage.
- **Journal:** the playing song's lyrics are written in by hand as they're sung.
- **CDs:** the next album lies open. Its disc carries the release's own disc picture when you matched it on MusicBrainz, otherwise a print made from the cover. The two albums after it sit in a stack, with an "Up next" note. Click a CD, or Tab to it and press Enter, to play that album; while shuffling it plays the whole album, then the shuffle carries on.
- **Earbuds:** they're plugged into the iPod and act as the visualiser, with sound arcs, a ring on each beat, and current running down the cable.
- **Around the iPod:** a Polaroid of the cover, a coffee, blinds light and dust in the sun.
- **Phones:** the frame is a closer look at the same desk, so everything keeps its size next to the iPod and runs off the edges like a photo.
- **Reduced motion:** with the system setting on, the desk holds still.

**New: no song is downloaded twice on mobile data.** The Info tab's live graphs and the desk's earbuds read a silent second copy of the song. On a phone on mobile data, or with Data Saver on, the desk keeps a calm pulse instead, and the live graphs wait for a "Show anyway" button. On Wi-Fi and on a computer nothing changes. The second copy also stops while the tab is in the background (it used to keep downloading).

**Changed (tests):** every browser test now honours the test-server port settings, so two checkouts can run the whole suite at the same time on different ports.

**Tested** (this build)
- Android unit tests: 414 run, 2 skipped (as before), 0 failed (no Android code changed). .NET tests: 663 pass. Browser tests: the full suite, 185 passed, 0 failed, on the branch with the same code; after the version bump the About-version spec (1d-g2) passed again, 22/22. During the earlier full runs one load test ("skipping 20 songs opens almost no streams") opened 11 streams against a limit of 10 while another session was building on this machine; it passed in the next two full runs.
- Release APK versionCode 83, signed with the owner key (SHA-256 6a4eb335...97a106); installed on a clean emulator and run against the 10,000-song mock: 23 of 23 checks passed (Quick Connect sign-in, 10,000 songs after 38 s, scrolling, play, a 40-skip burst, pause, kill and relaunch with the queue restored and not auto-playing, Settings > Storage and Clear cache, Up next Play next / Move up / Move down / Remove, no crash, no ANR).
- The MSI (0.68.0) was built and its app started from the build folder: the server answered /healthz "ok" and stopped when the app closed. Not installed.
- FLACie Web was deployed to the server (rollback tag first); /healthz "ok", and it serves the desk files.

**Not tested**
- The desk on a real phone or tablet (checked at phone, tablet and laptop sizes in Chromium), real MusicBrainz disc pictures (the test server has none), real lyrics from LRCLIB (the tests use the mock server's lyrics), and the mobile-data switch on a real phone (tested by emulating Chrome's connection report).
- **Not built:** Chromecast (docs/spec/chromecast.md).
