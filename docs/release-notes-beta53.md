Beta 53 (Android 68, MSI 0.53.0, FLACie Web 1.0-beta53). A fix release for one serious bug in beta 52. Still a beta, not a 1.0 final.

**Fixed: wrong cover art (Android, web, Windows)**
- **What went wrong in beta 52:** to cut down on cover requests, beta 52 used one picture per album, taken from Jellyfin's `AlbumId` / `AlbumPrimaryImageTag`. On a real Jellyfin `AlbumId` is not reliable: songs of many different albums share one, so dozens of songs showed the same wrong picture (in a playlist: 1989, Bangerz, The Blueprint 3, PRISM and others all got one cover). The mock Jellyfin used for the beta 52 tests never did this, so no test caught it. This was a regression of my change, not something older.
- **Now:** a song's own picture (its Primary image and that picture's tag) always wins; the album's picture is used only for a song that has none. The picture is still fetched once, kept on disk, at one small size (240 px WebP) and revalidated by tag, so the load reduction stays: what is given up is the sharing of one picture between songs of an album, because that sharing was wrong.
- **Old data:** the saved library on the web server and on the phone held the wrong cover keys, so both are discarded once and the library is listed again on the next start (a few minutes of background work; the songs stay on screen). Nothing of yours is lost: playlists, favourites and sign-in are untouched.

**Tested** (this build)
- .NET unit tests: 187 pass (the beta 52 test that asserted the wrong rule was replaced by one that asserts the right one, with songs of different albums sharing an `AlbumId`). Browser tests: 119 pass (one spec that counted pictures by the wrong assumption was corrected, and the 40-skip spec now makes its clicks inside the page so its timing no longer depends on the test runner). Android unit tests: 236 run, 2 skipped (as before), 0 failed (the same replacement, plus a fallback test).
- FLACie Web 1.0-beta53 was deployed to the owner's server and checked in a browser on a playlist: every song shows its own picture (distinct cover keys per song, the playlist header is its first song's cover).
- Measured live on the same server, muted: after pressing Next in a playlist the new title is on the bar in 11 to 68 ms, covers answer in under 25 ms, and a song's stream starts in 29 to 266 ms (one 1.2 s start in eight skips).

**Not tested**
- The phone app on a real phone: the fix is unit-tested and the APK is built and signed with the owner's key, but it was not installed on a device or the emulator in this release.
- The Windows MSI was built but not installed.
- Real Jellyfin for the phone's picture requests (the same URLs were checked from the server against the owner's Jellyfin: 200, WebP / JPEG).

**Known problems**
- The web player's early read of the next song (`pre=1`) uses a different URL from the real stream, so the browser cannot reuse it; it only adds a second stream open per song on Jellyfin. It does not make skips faster and may be removed in the next build. (Beta 51 did not have it.)
- Everything listed under beta 52's "Known problems" still applies (whole queue exposed to Android's media session, read-ahead of whole songs on the phone, no Undo for web queue removal, `MEDIA_PLAY` in the background after losing audio focus).
