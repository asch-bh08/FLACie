Beta 58 (Android 73, MSI 0.58.0, FLACie Web 1.0-beta58). Still a beta, not a 1.0 final. A small follow-up to beta 57: one visible bug fixed, the beta 57 fixes checked on the emulator, and regression tests added. No new features.

**Fixed (Android; web and Windows get the version bump only)**
- **Settings was unreadable in the "iPod Player" look** (light iPod skin): the labels were dark text on the dark cards. Settings now always uses its own dark palette for its text.

**Checked on the emulator (the beta 57 fixes; the owner has not yet judged these on the Fold)**
- A song the server has no picture for ("Out of Space" style, three added to the test mock) gets one catalog lookup per album and all songs of the album show the picture (list, mini player, Now Playing).
- Seek bar: while the finger is down the knob and the time follow it; after lifting, the knob stays and playback lands at the target (1:41 target, player reported 101.6 s).
- Skip (media Next key): the next song's cover is on screen within the first screenshot after the key, no placeholder.
- Request budgets on the 10,000-song mock (fast profile): first sync 24 item pages and 37 image requests; scrolling 580 image requests for 108 swipes with 3 repeats; 28 requests in the first 60 s of a warm start; covers 100 % of visible rows at 3 s; no crash lines in the crash buffer.

**Tests added**
- `Beta57RegressionTest` (7 tests): seek hold/scrub position rules (`SeekHold`), cover warm order and range (`WarmPlan`), catalog key rules (one key per album, album artist wins, title fallback, no artist = no key). The seek bar and the warm-up now call these pure functions; behaviour is unchanged.
- Test mock: three songs with no picture (`--nopic`), configurable song length (`--wavsec`); the Android driver can use a mock started by hand (`MOCK_EXTERNAL=1`).

**Docs**: player redesign spec and iPod mode redesign spec (nothing built yet), `docs/BY-HAND-CHECKS.md` (what only a phone/ears can judge), `docs/OVERNIGHT-LOG.md`.

**Not tested**
- Everything in beta 57's "Not tested" still applies: no Fold, no Bluetooth/lock screen, no sound. The graph (spectrograph) fix was not measured: the software-rendered emulator says nothing about smoothness, and no before/after frame statistics were taken.
- The Windows MSI was built, not installed. The RG Rotate was not used.

**Tested** (this build, 2026-10-13)
- Android unit tests: 309 run, 2 skipped (as before), 0 failed. .NET tests: 257 pass. Browser tests: 133 pass (a first run showed failures only because a leftover server from an interrupted run held the port; the clean rerun passed all 133).
- Release APK: versionCode 73, signed with the owner's key (certificate SHA-256 6a4eb335...97a106), installed over the emulator, signed in to the mock with Quick Connect, played, skipped, killed and restarted (queue restored, 0 crash lines), covers 100 % of visible rows at 3 s.
- The MSI (0.58.0) was built, **not installed** on this PC. FLACie Web was **not redeployed** (the server only changed its version string); the live site still reports beta 57.
