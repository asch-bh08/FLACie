Beta 67 (Android 82, MSI 0.67.0, FLACie Web 1.0-beta67). Still a beta, not a 1.0 final.

**Fixed (web and Windows): dragging a touch iPod's list with the mouse did nothing.** Beta 66 said "a mouse can drag the lists like a finger", and that was not true: a rename I made in the page script also changed the name of the list class it looks for, so the drag never found the list. It scrolls now, and a drag is not taken for a tap on the row under it. A browser test drags the Songs list of three touch iPods and fails with the old script (checked). Found by another Claude session while it was working on the iPod desk look.

**Also in this build (not mine):** the Alexa work that another session merged to main on 2026-10-14 (the "ask FLACie" custom skill: pairing, voice matcher, signed stream links, Echo in the device picker; **off until switched on in Settings > Alexa**). It was already deployed to the live server before this release; see `docs/alexa/STATUS.md` for what it was and was not tested against (no real Echo, no real Amazon signatures). Nothing in this release turns it on.

**Changed (tooling):** `ipodsync/tools/stop-test-servers.ps1` stops the test servers by the port they listen on (5290, 8196, 8197), never by image name: stopping every `dotnet.exe` by name also stops another session's tests.

**Tested** (this build)
- Android unit tests: 414 run, 2 skipped (as before), 0 failed (no Android code changed). .NET tests: 636 pass (about 300 of them are the Alexa tests). Browser tests: one full run, 153 passed, 3 failed and 2 did not run; the three that failed (the 320 px bottom bar, "skipping 20 songs opens almost no streams", and "Autoplay lists ... Downloading") passed when run again, alone and together, while another session was running its own tests on this machine at the same time and had stopped my servers once, so I treat them as load-related and not as bugs; the Autoplay one has now failed in several full runs for no cause I could find, so it is the one to watch.
- Release APK versionCode 82 signed with the owner key (SHA-256 6a4eb335...97a106); installed on a clean emulator and run against the 10,000-song mock: first sync 10,003 songs (first songs after 14 s), scrolling 84 swipes with 471 requests, a skip-burst of 40 in 2.5 s with the next song ready after 4.6 s and 64 requests, warm start 9.8 s, kill and restore: queue restored, 0 crash lines (a first try on another mock port failed to sign in; that was the port, not the app).
- The MSI (0.67.0) was built, not installed. FLACie Web was deployed.

**Not tested**
- Finger feel on a touch screen; dragging was tested with a mouse. Windows was not run in the installed app. Alexa: see its own status file.
- **Not built:** Chromecast (docs/spec/chromecast.md).
