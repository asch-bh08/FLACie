Beta 57 (Android 72, MSI 0.57.0, FLACie Web 1.0-beta57). Still a beta, not a 1.0 final. A quick follow-up to beta 56 from a first session on the Fold.

**Fixed (Android; the web and Windows get the version bump only)**
- **A song with no cover.** Some songs have no picture on the Jellyfin server at all (not for the song, not for its album): a placeholder note was all they could show. They now get a cover from the public catalog (the same lookup the web already used), one lookup per album, so an album's songs share one download. Your saved library is listed once more in the background so existing songs pick this up.
- **Seek bar.** After you let go, the knob used to jump back to the old position for up to a quarter of a second before moving; now it stays where you let go until playback gets there, and the time under the bar follows your finger while you drag. (Measurements on the Fold showed no dropped frames while dragging; this fix targets the snap-back, the most likely cause of the "laggy" feel. Tell us if it still feels bad.)
- **Skipping.** The covers of the previous song and the next four are made ready in the background each time a song starts, so a skip shows the next cover at once instead of the placeholder for a moment.
- **Info tab graphs.** The sound analysis ran on the screen's main thread every frame (120 a second on the Fold) and could wait on the audio thread after each new song; it now runs on a background thread about 40 times a second, and the audio is only tapped while a graph is on screen (before, every sample of every song was converted even with the tab closed). The drawing is unchanged.

**Tested** (this build)
- Android unit tests: 302 run, 2 skipped (as before), 0 failed. .NET tests: 257 pass. Browser tests: 133 pass. The release APK is signed with the owner's key (versionCode 72).
- Checked against the live Jellyfin (read-only, anonymous image endpoint) that "Out of Space" (The Prodigy) has no picture for its album or any of its 15 songs.
- On the Fold: the seek-bar frame statistics (1 % janky frames while dragging, median 6-9 ms) and the missing cover reproduced by screenshot.

**Not tested**
- The three fixes above were built and unit-tested but **not verified on the phone by us**: the new cover for the no-picture song (a song already in the restored queue keeps its old data until it is played again), the seek bar feel, the skip, and the graph on a new song. The owner is the judge of feel.
- The graph fix is a likely cause found by reading the code; the stutter itself was never reproduced or measured.
- The Windows MSI was built, not installed. The RG Rotate was not used.
- Everything in beta 56's "Not tested" still applies.
