Beta 59 (Android 74, MSI 0.59.0, FLACie Web 1.0-beta59). Still a beta, not a 1.0 final. Android only: the web and Windows get the version bump.

**Fixed (Android): the media session no longer publishes the whole queue**
- The system media session (Bluetooth head units and earbuds, Android Auto, the lock screen and the quick-settings media card) was handed the entire play queue, up to 10,000 songs, and that list is what ran the phone out of memory over Bluetooth. The session now exposes only the current song (title, artist, artwork, position, duration) with play/pause, previous/next, seek, shuffle and repeat. Next and Previous still go through the same skip handling as the app's own buttons.
- Measured on the emulator with `dumpsys media_session`: queue size 2003 (before) -> 0 (after); playback state, actions (play, pause, skip both ways, seek) and metadata unchanged; the system media card shows title, artist, artwork colour, seek bar, previous/next/play; media Next/Previous/Pause keys work; no crash lines.
- This is a simpler form of the "windowed queue" in the plan (current +/- 50): controllers see one item rather than 101. A head unit that wants to browse the upcoming songs cannot; the in-app Up next is unchanged. The plan's custom actions (favourite, speed) and notification polish are NOT done.

**Tested** (this build)
- Android unit tests: 311 run, 2 skipped (as before), 0 failed (new: SessionWindowTest). .NET tests: 257 pass. Browser tests: 133 pass.
- Release APK versionCode 74, signed with the owner's key (SHA-256 6a4eb335...97a106); installed over a clean emulator, signed in to the mock with Quick Connect, played, skipped, killed and restarted (queue restored, 0 crash lines); skip burst on a 3,000-song mock opened 6 streams (2 aborted), 100 requests in the run (beta 58 run: 84; run-to-run noise on a software-rendered emulator, no per-song requests).
- Covers on the emulator after a fresh install: 100 % of visible rows at 3 s in two of three runs; in the first run 20 % at 3 s and 100 % at 10 s (cold emulator); beta 58 reached 100 % at 3 s in all three.
- The MSI (0.59.0) was built, not installed. FLACie Web was not redeployed (version string only).

**Not tested**
- **No Bluetooth device, car, Android Auto or lock screen on a real phone**: the actual out-of-memory case was not reproduced on the emulator (it has no Bluetooth head unit). Please try the 10,000-song queue over Bluetooth first (docs/BY-HAND-CHECKS.md).
- Everything in beta 57/58 "Not tested" still applies. The MSI was built, not installed. The RG Rotate was not used.
