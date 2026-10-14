Beta 63 (Android 78, MSI 0.63.0, FLACie Web 1.0-beta63). Still a beta, not a 1.0 final.

**New: star ratings (Android and web)**
- A star button beside the heart: tap it for five to one stars, or clear. Ratings are kept in your FLACie profile (so every device sees them, and the newest choice wins when two devices disagree) and written to Jellyfin as the song's own rating (stars x 2, 0 to 10). Songs from other sources keep the rating in the profile only.
- Android: also "Rate..." in the song menu.

**New: the queue in the system media card, Bluetooth and Android Auto (Android; not tried in a car)**
- The lock screen, the quick-settings card and Bluetooth now see a window of your queue (the song that plays, 20 before and 80 after, in play order, shuffle included) instead of the one song, and "next", "previous" and picking a song from that list go through the same player as the app (skip bursts and history behave as in the app). 10,000 songs are never handed over at once.

**New: the cover flies from the mini player into Now Playing (Android)**
- Opening the full player moves the small cover up into the big one instead of both just sliding.

**New: iPod mode on the web and in the Windows app**
- Account menu > "iPod mode" (`/ipod`): an iPod on the page. Drag around the wheel (or use the mouse wheel / arrow keys), press the middle to select, MENU to go back, previous/next/play on the wheel. Music > Artists, Albums, Songs, Favorites; Playlists; Shuffle Songs; Now Playing (the wheel is the volume there). The list slides in and out, with a scrollbar like the hardware. White model, `?c=black` for the black one. It fits any window size. Windows has it too (the app shows the same page).

**Fixed (web):** choosing a cover from the Cover Art Archive (or going back to the original) did not show until the next time the player redrew, which never happened while paused; it now shows at once. (Found because a test paused the song.)

**Changed: iPod emulator on Android**
- Classic: **Cover Flow** (a row of album covers in perspective with reflections, turned with the wheel; the middle one opens), a **scrollbar** in lists, a **scrub diamond** on Now Playing, and a shadow under the screen bezel.

**Not built, on purpose: Chromecast.** A Cast receiver cannot send the Jellyfin `Authorization` header, so casting would mean putting your session token in a URL handed to another device. That is your decision (a revocable cast-only key, or a relay in the server): see `docs/spec/chromecast.md`.

**Tested** (this build)
- Android unit tests: 414 run, 2 skipped (as before), 0 failed. .NET tests: 311 pass (new: iPod menu and wheel rules, ratings). Browser tests: 145 pass, 0 fail (one full run, nothing else running); new: iPod mode (menus, wheel drag, keys, playback, small window, black model), ratings. Two tests were made more robust (the MusicBrainz tests no longer see answers an earlier run cached; the About version moved to beta63).
- Release APK versionCode 78 signed with the owner's key (SHA-256 6a4eb335...97a106); installed on a clean emulator and run against the 10,000-song mock: first sync 10,003 songs (first songs after 16 s), scrolling 146 swipes with 880 requests, skip-burst of 40 in 3 s with the next song ready after 5 s and 66 requests, warm start 9.6 s, kill and restore: queue restored, 0 crash lines.
- On the emulator: the session queue window through a separate test app (a different Linux user, as a car would be), the shared cover transition (mid-animation frame at 10x slow motion), Cover Flow, the scrollbar and the scrub diamond (screenshots in `docs/img`).
- The MSI (0.63.0) was built, not installed.

**Not tested**
- Nothing on a phone or in a car; nothing with a real Jellyfin write of a rating (tested against the mock, which records the call). The cover transition and Cover Flow were looked at in still frames on a slow emulator: smoothness, wheel feel and haptics are yours to judge.
- iPod mode on Windows was not run in the installed app (the MSI embeds the same page, tested in the browser).
- **Not built (decisions, not oversights):** Chromecast (above); the iPod LCD at an integer-scaled logical resolution with a bitmap font (the Classic already draws at its logical size; no licensed font was added) and a title cross-fade; Cover Flow on the other iPod themes.
