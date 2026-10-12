Beta 56 (Android 71, MSI 0.56.0, FLACie Web 1.0-beta56). Still a beta, not a 1.0 final.

**Faster start, and covers that actually all arrive (Android; the wrong-cover fix is also on the web and in the Windows app)**

Measured on a Z Fold against the real library (full numbers: `docs/test-run/K-coldstart-covers.md`).
- **Home is on screen about 70 ms after the app starts** (median of 5 launches; beta 55: about 900 ms, and 1.1 s with an empty cover cache). Home is drawn from a small saved copy of the shelves as they were last time; the library then loads behind it (about 0.6 s instead of 1.1 s).
- **Covers:** with an empty cover cache, 14 of 15 visible covers are there after 1 second in every run (beta 55: none after 1 s, and two of five runs never got past 73 % after 10 s). The 15th is an album Jellyfin really has no picture for.
- **Why covers went missing** (all fixed): rows sharing one album cover raced on one temporary file and deleted each other's finished picture; one failed answer left a cover empty for three minutes; slow first-time pictures made the request limiter shrink to one at a time; the sign-in sync queued ahead of the covers. A cover that fails is now asked for again with a growing pause for as long as it is on screen.
- **Wrong covers fixed (Android, web, Windows).** On the owner's library 1,807 of 10,325 songs showed another album's picture: Jellyfin gives songs of unrelated albums the same album id, and a song without its own picture borrowed the album's. An album picture is now used only when every song pointing at it is of that one album; other songs show no cover (Android) or a catalog lookup (web). A song's own picture is never touched.
- The account sync at launch is background work; the first screens' covers are loaded before the first frame.
- For testers: `adb logcat -s FLACieBoot FLACieProbe FLACieCover` prints launch timings and how many on-screen cover slots have their picture; `am start --ez clearcovers true` starts with an empty cover cache.

**Tested** (this build)
- Android unit tests: 302 run, 2 skipped (as before), 0 failed. .NET tests: 257 pass. Browser tests: 133 pass. The release APK is signed with the owner's key (versionCode 71).
- On the Z Fold (real library, 5 launches warm and 5 with an empty cover cache): launch time and cover completeness as above; the Jellyfin library was only read.
- On the emulator against the 10,000-song mock with a slow and a flaky server: covers complete in every run; Home drawn in about 0.15 s.
- A read-only check of the real library's cover keys (no song's own picture shared, 1,807 songs found with a doubtful album picture) and of three albums against the live Jellyfin image endpoint.

**Not tested**
- The Windows MSI was built, not installed.
- The RG Rotate was not used.
- Nothing measured on the server side under load (no CPU numbers from the real Jellyfin).
- The final build's smaller launch prefetch (12 covers) was run on the Fold only, not on the emulator matrix.
- Songs whose album picture the vetting removed show a placeholder on the phone; whether the phone should look them up in a public catalog like the web does is undecided.
- Everything in beta 55's "Not tested" still applies.
