Beta 50 (Android 65, MSI 0.50.0). Still a beta, not a 1.0 final.

**Android: fixes**
- **NAS songs now show real tags.** Title, artist, album, track, duration, sample rate, bit depth and the embedded cover are read from the files (MP3, FLAC, M4A/ALAC, Ogg/Opus, WAV, AIFF) and remembered, so the Hi-Res badge and durations are right. The first scan of a big NAS runs in the background at low priority and waits while you use the screen; later starts use what was remembered.
- **Jellyfin songs without tags** that were listed by their file name ("06 - Artist - Title") are shown with a cleaned title (Android and web use the same rules and the same 50 test cases).
- **First launch** explains what the app needs and asks you to sign in before any system permission prompt; media and notification permissions are asked for only when needed.
- **Lists are smoother** on the RG Rotate (Explore with 10,000 songs: 5.7 % janky frames in beta 49, about 1 % now once the NAS scan is finished).
- **A crash while saving a very large library** (out of memory) is fixed: the library is now written as a stream.
- **A freeze after pressing Next many times** is much shorter: abandoned downloads are cancelled at once and skips are collapsed (45 quick presses now start playback in about 4 s on the RG Rotate, was 25 to 40 s; one slow Jellyfin song can still take much longer).
- **Seek bar while paused** now follows a seek.
- **Now Playing on small square screens (RG Rotate)**: the cover and the controls sit side by side and nothing is cut off; the Info graph and its axis fit; Lyrics, Info and Up next use the full width. Several other screens got the same treatment (Explore header, search, mini player, dialogs hidden behind the keyboard, Headphone test tabs).
- **D-pad / gamepad**: the Explore search box no longer traps focus or pops up the keyboard; every control now has a visible focus ring (Quick picks, mini player, rows, play button); touch targets are at least 44 dp.
- **Queue and position are restored after the app is killed** (paused, with the mini player). Empty playlists keep their Rename/Delete buttons; Undo after removing a song lasts 6 seconds. The sleep timer shows the time left (Settings and Now Playing) and survives a restart. An Offline chip shows on Home and Search without a connection. Song rows on narrow screens keep more of the title.
- Compose libraries updated (BOM 2024.09.03) after a crash inside the Home list; see "Known problems".

**Windows app (MSI 0.50.0)**
- The embedded web view keeps its data in your user folder (so uninstall cleans up and sign-in survives upgrades), only one copy can run, installing over a running app closes it first, licences ship with the installer, no debug files. The leftover folder of the old version is removed on first start.
- The desktop app no longer runs the daily charts download itself (the always-on server does); charts also skip songs fetched in the last hours.

**Web (FLACie Web)**
- The playlist importer refuses binary or non-text files (400) while plain text and m3u lists still work; malformed requests such as `/art/..` and bad settings bodies get clean 4xx answers instead of error pages; 7 server bugs found by the audit are fixed (duplicate keys on concurrent profile saves, unbounded memory in the play tracker, a pasted token injecting header lines, range requests on empty files, a lost download-log line, import cancel by another user, oversize chunked imports).
- Downloads: a "(Piano Version)" request no longer takes the original recording; Autoplay and Daily charts do not fetch songs fetched a moment ago.
- Layout: full-screen Now Playing, the bottom navigation (scrolls, 11 items for admins), the A-Z rail, the header on short windows and phones; the play button shows a spinner while a song is loading; skipping through many songs opens only the stream of the song you stop on.
- New: an "About" section in Settings (version, server, data folder, licences), a proper "page not found" page inside the app, case-insensitive artist and album links, keyboard shortcuts (Space, Left/Right = 5 s, N, P).

**Tested** (this build; everything below was run and passed unless marked)
- .NET unit tests: 87 pass. Android unit tests: 99 run, 0 failed (2 skipped, as before). Browser tests: 107 pass (one new test was racing the player bar animation and was fixed, not loosened).
- RG Rotate (real device, release build): about 40 checks, see docs/test-run/G-rg.md: dark theme, font scale 0.85 and 1.3, lyrics, sleep timer, shuffle/repeat, playlist create/add/remove/rename/delete, Dashboard and download log, all media and gamepad keys, kill and restore, offline, cold start 475 to 786 ms, screen-off playback for 2 minutes, audio focus with YouTube, Explore jank (median 1.0 %, five runs), no crash or ANR in the log; then every screen re-checked after the layout fixes.
- Windows 0.50.0: install, upgrade from 0.49 over a running app, single instance, licences present, no .pdb, uninstall closes the app; the signed-in app swept page by page through its debug port (46 checks, docs/test-run/G-win.md), then the fixes were re-tested in the browser suite.
- Emulator: the Home scroll-with-Next loop that crashed the Fold survived 700 and 1500 iterations on debug and on release builds, and a 30 minute memory loop stayed flat (docs/test-run/G-emu.md).
- Several songs were downloaded to the NAS during testing (real downloads, nothing deleted).

**Not tested**
- **The Z Fold 7**: it was withdrawn from the test run, so the crash fix (below) was NOT re-tested on the device that crashed, nor the inner screen, split screen, 3-button navigation or a Fold memory loop.
- **Known problems**: the Home list crash seen on the Fold (a Compose internal error while scrolling Home with music playing and pressing Next) was not reproduced on the emulator or the RG Rotate after the changes (Compose update, no early return in the Home list, Quick picks without BoxWithConstraints), but is not proven fixed on the Fold. If it happens again, tell us what you were doing.
- **First scan of a big NAS** on a fresh install: the pause-while-touching logic is unit-tested, but jank during a first scan was not measured (needs a fresh sign-in).
- The removal of the old version's leftover web-view folder at first start of the Windows app (written, not run).
- First-launch screens on a device (needs cleared app data), the Android notification permission flow, expired session, sign-out, server unreachable, real headphones and Bluetooth, Jam / Connect hand-over between devices (only the device list was opened), hardware media keys on Windows, the Windows iPod manager (no iPod; nothing was written to an iPod), the MSI on a clean machine other than this PC.
- **Audio by ear**: gapless transitions, the Hi-Res badge on a 48 kHz file, the equalizer, headphone volume and a skip burst on a real song were checked only by state, not by listening.
- **Known gaps / not included**: queue reordering and removing songs from Up next (Android and web) is a feature and is not in this build; Android does not follow the system light theme; `MEDIA_PLAY` does not resume playback if the app is in the background after another app took audio focus (Android routes it elsewhere; Play/Pause works); the YouTube fallback and charts downloads configured on your account remain as set.
