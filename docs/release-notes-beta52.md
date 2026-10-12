Beta 52 (Android 67, MSI 0.52.0, FLACie Web 1.0-beta52). Still a beta, not a 1.0 final. This release is about one thing: FLACie no longer overloads Jellyfin, and stays instant. Everything below ships on Android, web and Windows together unless it says otherwise.

**What changed for Jellyfin (the load work)**
- **One gate for every request to Jellyfin** (Android, web server, Windows app). A limited number run at once (it starts at about 5 and adapts: it grows after a run of fast answers and halves when Jellyfin is slow, answers 429 or 5xx, or times out), the rest wait in a queue. A request that fails for a passing reason is retried at most twice, after a randomised wait that honours `Retry-After`. A server that keeps failing trips a circuit breaker: requests fail at once for a short while instead of piling up, then one probe goes through.
- **Priorities and cancellation.** The song that is playing goes first, what is on screen next, background work (library sync, format reads, read-ahead, charts) only when nothing more important waits, nobody is streaming and the server is not slow. A cover for a row that scrolled away is cancelled; the same request asked twice at once is one request; a stream the player gave up on (skips) is cut and its slot returned.
- **Bulk and delta sync.** The library arrives in pages of 500 songs that already carry each song's stream data (codec, bit depth, sample rate, bit rate), so there are no one-song format reads any more (beta 51 made 10,000 of them for 10,000 songs, and retried unreadable ones forever). Songs show up as the pages arrive. After the first sync only songs saved since the last one are asked for (`MinDateLastSaved`), and Jellyfin's WebSocket (`LibraryChanged`) starts that delta instead of polling. The library, its cursor and the set of ids are kept on disk, so a restart costs one small request. A deleted song disappears.
- **Covers are fetched once, at one size.** Rows and tiles use one picture size (240 px WebP, quality 80), only the Now Playing cover uses the full picture. The picture is the album's, shared by all its songs (one request per album, not per song; compilations keep a picture per song). A saved picture is asked for again only when Jellyfin's image tag for it changed (ETag / 304).
- **No fixed cache cap.** Everything is kept while there is room; the oldest unused covers are removed only when the free space of the device or server volume is low (below 10 % or 1.5 to 2 GB). Settings shows the cache size and has a **Clear cache** button (Android: Settings > Storage; web and Windows: Settings, administrators).
- **Streams.** Direct stream by default (transcoding only for formats a player cannot play and, on Android, with the data saver). The web player preloads the first MiB of the next song; the phone caches the next song at background priority.
- **The Windows app's embedded server** no longer runs the library-wide background passes; it only does the delta sync and answers the song someone is looking at.
- **Hi-Res badge** (one rule, as in beta 51) now also comes from the facts in the listing, so Jellyfin alone is enough. An Android bug was fixed: a stale decoder name could label an MP3 as FLAC (and so Hi-Res) before the file's own facts were known.

**Also new**
- **Up next can be edited** (web, Windows and Android): move a song up, down or to the top ("Play next") and remove it. The song that plays stays current; on Android a removal can be undone. The order of a shuffled or rearranged queue survives a restart.
- **Windows:** the desktop app's own player is labelled "FLACie for Windows" on the dashboard and Devices pages instead of "Edge on Windows" (the Jellyfin device name stays "FLACie Web"; the client id stays `ipodplayer`).
- **Click-wheel view (Android):** the hardware PLAY and PAUSE keys now work there too (before, only the combined play/pause key was handled).
- Version numbers: Android 1.0-beta52 (67), Windows 0.52.0, FLACie Web 1.0-beta52 (Settings > About).

**Measured** (details and every number: `docs/test-run/I-load.md`; mock Jellyfin with 10,000 songs, not a real server, not a real phone)
- Web server, empty start, first two minutes: 10,074 requests before, 67 after (fast server); 1,782 before, 67 after (300 ms server). The busiest second went from 1,682 requests to 15.
- Scrolling the whole 10,000-song list: 7,562 cover requests (500 px JPEG) before, 829 (240 px WebP) after. Phone: 628 before, 103 after.
- A restart with the library on disk: 696 requests before, 25 after (slow server). Phone: 34 before, 20 after.
- 40 quick skips on a slow server: 244 requests (20 at the same moment) before, 53 (6 at once) after.
- A rate-limiting server (429 above 20 requests per second): 1,351 refused answers before, none after. A flaky server (10 % 503): beta 51 never listed the library, beta 52 lists all 10,000 songs in about 5 s (web).
- Not better, said plainly: the phone's first sync makes more (and bigger) requests than before (69 vs 46) and takes longer to list everything (about 45 s vs 15 s on a fast server) because stream facts now come with each page and the sync paces itself; the first songs still show after the same time. See I-load.md.

**Tested** (this build; run and passed unless marked)
- .NET unit tests: 187 pass (106 in beta 51; 81 new). One of them failed once in a parallel run on the merged tree because test classes overwrote each other's process-wide "current" instance; the suite now runs serially and passed three times in a row. Browser tests (Playwright, the real server against a mock Jellyfin): 119 pass (109 in beta 51; 10 new), one worker, fresh server, 7.2 minutes, on the final merged tree. Android unit tests: 235 run, 2 skipped (as before), 0 failed.
- New tests: the gate (concurrency limit, priority order, back-off with Retry-After, at most two retries, cancellation of queued and running requests, circuit breaker open / half-open / closed, request sharing, background held while streaming or slow); delta sync (changed, new and removed songs, cursor and ids persisted, restart cost, socket removals); cover cache (nothing deleted while there is room, oldest removed only when low, fresh covers protected, cache report and Clear cache); the Hi-Res rule from listing facts; queue editing arithmetic (exhaustive for small queues, on Android and web); a 40-skip burst with a stable badge and bounded streams; Settings cache line and Clear cache; "a cover is fetched once at 240 px WebP, a second browser asks for none".
- Load runs against the 10,000-song mock on the web server, the Windows-mode server and the Android emulator, four server behaviours each (fast, slow, flaky, rate-limiting): `I-load.md`.
- Android emulator (release APK 1.0-beta52, signed with the owner's key; mock Jellyfin): __EMU__
- Windows MSI 0.52.0 built from this tree (WiX); the installer itself was not run (see below).

**Not tested**
- **Real Jellyfin.** All load numbers are from a mock. Real behaviour of `MinDateLastSaved`, `LibraryChanged`, `Retry-After`, image tags and album pictures, WebP covers, and how slow a page of 500 songs with streams is, was taken from documentation, not observed.
- **Real devices:** the Fold (cover and inner screens), the RG Rotate and any phone. The new Settings rows, the Up next menus, the long-press and the d-pad focus in them were compiled and unit-tested (pure logic) but not used on a device.
- **Windows:** the MSI was built but not installed, upgraded or uninstalled; the "FLACie for Windows" label was tested only as a unit and in the web server, not in the real WebView2 window.
- **Anything audible:** gapless transitions, the Hi-Res badge compared with the sound, the equalizer, headphone volume, whether a read-ahead glitches a stream. No by-ear checks were made.
- First NAS scan jank on a fresh install (needs a NAS), and the notification-permission timing on Android 13+.
- Long soak (hours), memory over time with the new caches, a full disk.

**Known problems and open items**
- The phone's read-ahead caches the whole next one or two songs, not only the start (background priority, skipped while the server is slow). Starting only the first part of the next song is not done.
- `MEDIA_PLAY` from a headset in the background after another app took audio focus is still not resumed: Android routes the media button elsewhere in that state (explained in `docs/test-run/I-android-features-notes.md`). Play/Pause and the in-app keys work.
- Web: removing a song from Up next has no Undo (Android has one); no drag and drop (buttons only).
- Jellyfin server edits that are not dated (an in-place change without a new `DateLastSaved`) are seen only after Clear cache and a restart.
- Windows pages that use the older shared client (the iPod sync tabs) are not behind the gate; they are user-driven, not background load.
- The player's own retry (three tries, then skip) walks through a queue quickly while the breaker is open.
- Android still does not follow the system light theme (unchanged).

**Owner checklist** (things only you can do or hear)
1. Install the APK over beta 51 on the Fold and the RG Rotate (same signing key, so it updates in place). Sign in is kept. Watch the first sync: songs should appear within seconds and the rest fill in; `adb logcat -s FLACieGate` prints the request counts every 10 s.
2. On the real Jellyfin: watch its CPU during a first sync and while scrolling Explore with covers loading; open the dashboard (admin > Dashboard / `/api/admin/load` on FLACie Web) for the request counts per minute. Compare with the numbers in `I-load.md`.
3. Settings > Storage (Android) and Settings > Cache (web): the size looks sensible; Clear cache empties covers and the library comes back without a full re-sync.
4. By ear: skip bursts on real songs, gapless between albums, the Hi-Res badge on a 48 kHz and a 24/96 file, and that the next song starts at once.
5. Up next: move, play next and remove on the Fold, on the RG Rotate (d-pad) and in the web player; check the playing song never changes by itself.
6. Windows: install MSI 0.52.0 over 0.51.0 (upgrade closes the running app), check the dashboard label "FLACie for Windows" and that the app idles quietly.
7. Deploy FLACie Web 1.0-beta52 with the recipe in `docs/private/HANDOFF-2026-10-11.md` (rollback tag first; `/healthz` must say "ok"); sign in again with Quick Connect if the session was lost.
