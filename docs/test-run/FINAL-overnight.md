> **Update 2026-10-12:** this report covers the night (beta 52). Betas 53, 54 and 55 followed the same day: see `OVERNIGHT-SUMMARY-b52-b55.md` (what happened, what is not done) and `../NEXT-SESSION-START-HERE.md`.

# Final report of the overnight run (2026-10-11 evening to 2026-10-12), beta 52

Branch `release/beta52` (merged into `main` and released as `v1.0.0-beta52`, see "Release" below). Log of every step: `docs/test-run/LOG-overnight.md`. Numbers: `docs/test-run/I-load.md`. Release notes (Tested / Not tested, owner checklist): `docs/release-notes-beta52.md`.

## What changed
**Load on Jellyfin (the owner's top priority), on Android, the web server and the Windows app together:**
1. One adaptive request gate for every Jellyfin request (starts at about 5 at once, grows after fast answers, halves on slow / 429 / 5xx / timeouts; at most 2 retries with jitter and `Retry-After`; circuit breaker; priorities playing > visible > background; cancellation of queued and running requests; identical requests shared).
2. Bulk + delta sync: stream facts in the paged listing (no per-song reads), `MinDateLastSaved` deltas, Jellyfin WebSocket `LibraryChanged` instead of polling, library + cursor + ids on disk (a restart = one small request), songs shown as pages arrive.
3. Covers fetched once per (album picture, image tag), one size (240 px WebP q80 rows, full only in Now Playing), ETag / 304, album picture shared by all songs of the album.
4. No fixed cache cap, free-space-based LRU eviction, Settings line with the cache size and "Clear cache" (Android Settings > Storage; web / Windows Settings, administrators).
5. Direct stream by default; web preloads the first MiB of the next song; abandoned streams are cancelled; the Windows app's embedded server no longer runs library-wide background passes.
6. The one Hi-Res rule is fed from the listing facts; an Android bug fixed (a stale decoder name could label an MP3 as FLAC / Hi-Res).
**Also (the "if time remains" items):** Up next editing (move up / down / top, remove) on Android (with Undo), web and Windows; "FLACie for Windows" client label instead of "Edge on Windows"; hardware PLAY / PAUSE keys in the Android click-wheel view; `MEDIA_PLAY` in the background after losing audio focus investigated (an Android media-button routing limit, documented, not changed).
**Tooling:** `ipodsync/tools/loadtest/` (10,000-song mock Jellyfin with Quick Connect, profiles and `/__stats`; browser driver; emulator driver; smoke script; report generator), `AssemblyConfig.cs` (xunit runs serially: a static-state race failed one test once).
**Version:** Android 1.0-beta52 (versionCode 67), MSI 0.52.0, FLACie Web 1.0-beta52.

## Test counts (final merged tree)
| Suite | Result |
|---|---|
| Android unit tests (`:app:testDebugUnitTest`) | 235 run, 2 skipped (as before), 0 failed (beta 51: 107) |
| .NET tests (`dotnet test`, FLACie.Tests) | 187 passed, 0 failed, three runs in a row (beta 51: 106) |
| Playwright (real server vs mock Jellyfin, one worker, fresh server) | 119 passed, 0 failed, 7.2 min (beta 51: 109) |
| Load runs (10,000-song mock) | web: 4 server behaviours x before/after; Windows mode: 2 x before/after; Android emulator: 4 x before/after |
| Emulator smoke test, release APK 1.0-beta52 | 22 of 23 checks passed; the failed one is the system Bluetooth process running out of memory in the emulator image during a skip burst (also with beta 51); the app did not crash or ANR |
| Builds | release APK built and signed (SHA-256 of the signer = the owner's key), MSI 0.52.0 built with WiX (not installed) |
No existing test was skipped, weakened or deleted; a few specs were adapted to deliberate behaviour changes (album-level covers, WebP covers, counters measured from the start of the test), listed in `docs/test-run/I-server-notes.md`.

## Before / after (mock Jellyfin, 10,000 songs; full tables in I-load.md)
| | beta 51 | beta 52 |
|---|---:|---:|
| Web, first sync, requests in 2 min, fast server | 10,074 | 67 |
| Web, busiest second | 1,682 | 15 |
| Web, first sync, slow server (300 ms) | 1,782 | 67 |
| Web, scrolling 10,000 rows: cover requests | 7,562 (500 px JPEG) | 829 (240 px WebP) |
| Web, 40 quick skips, slow server: requests / at once | 244 / 20 | 53 / 6 |
| Web, restart with library on disk, 60 s, slow server | 696 | 25 |
| Web, rate-limiting server: answers refused with 429 | 1,351 | 0 |
| Web, flaky server (10 % 503): library listed | never (0 songs) | all 10,000 in 4.8 s |
| Phone, scrolling: cover requests (fast / slow) | 628 / 430 | 103 / 105 |
| Phone, warm start, 60 s | 34 | 20 |
| Phone, flaky server | never listed | all listed after 42 s |
| Phone, first sync (fast): requests / time to all songs | 46 / 15 s | 69 / 46 s (worse, by design: stream facts now come with each page and it paces itself) |
Windows mode equals web (same code); beta 51's desktop copy ran the same background reads as the server (1,768 requests in the window).

## Bugs and gaps still open
- Whole 10,000-item queue is published to Android's media session; a system Bluetooth process ran out of memory in the emulator image reading it during a skip burst (beta 51 as well). Likely fix: expose a window around the current song (or no timeline) to external controllers. Not changed: it alters lock-screen / Bluetooth behaviour that cannot be checked here.
- Phone next-song read-ahead caches the whole next one or two songs, not just the start (background priority).
- Phone first sync takes longer to list everything (see table); first songs appear as fast as before.
- Phone skip burst on the rate-limiting server: 17 s vs 6.7 s to "ready" in a single emulator run (not repeated; possibly noise).
- `MEDIA_PLAY` from a headset in the background after another app took audio focus: Android routes it elsewhere.
- Web Up next removal has no Undo; no drag and drop.
- Undated in-place edits on Jellyfin are seen only after Clear cache + restart.
- The Windows iPod-sync tabs use the older, ungated client (user-driven).
- One intermittent Playwright failure (a 320 px bottom-bar spec measured a link 0 px wide right after navigation) was seen once in early runs; it did not recur in the final runs.

## NOT tested, and why
- Real Jellyfin (cannot be reached from here): all load numbers are from a mock; real `MinDateLastSaved`, `LibraryChanged`, `Retry-After`, image tags and WebP handling are from documentation.
- Real devices (Fold, RG Rotate): none available. New Settings rows, Up next menus, d-pad focus and long-press were unit-tested as pure logic and exercised on the emulator only through taps.
- Windows: the MSI was built but not installed / upgraded / uninstalled; WebView2 not driven.
- Anything audible (gapless, Hi-Res vs sound, EQ, headphone volume): cannot be heard here.
- Delta sync / WebSocket on Android against the mock (`/__touch`, `/__delete` exist, the Android driver did not use them; covered by unit tests), low-space eviction on a real full phone, data saver, Jams, NAS, long soak, first NAS scan on a fresh install.

## Release
Both conditions held: `gh` is logged in as asch-bh08 with admin rights on `asch-bh08/FLACie`, and the APK is signed with `%USERPROFILE%\.android\debug.keystore`, certificate SHA-256 `6a4eb335fd04b07b1d61610f60dc403c492f263517fc65143f03fbe31497a106` (checked with apksigner; this is the key the repo's release build uses and that signed beta 48 to 51, so it updates in place). `v1.0.0-beta52` was published with the APK and the MSI (see the last line of the log for the exact result). Nothing was deployed: the owner's server cannot be reached from here.

## What the owner must do next
1. Install the APK over beta 51 on the Fold and the RG Rotate; watch the first sync; `adb logcat -s FLACieGate` prints the gate counters every 10 s.
2. Install MSI 0.52.0 over 0.51.0 on Windows (not tested here).
3. Deploy FLACie Web 1.0-beta52 to the server with the recipe in `docs/private/HANDOFF-2026-10-11.md` (make the rollback tag first, check `/healthz`).
4. On the real Jellyfin, watch CPU during a first sync and while scrolling; compare with `I-load.md`; look at `/api/admin/load` on FLACie Web.
5. By ear: skip bursts, gapless, Hi-Res badge on a 48 kHz and a 24/96 file.
6. Decide about the media-session queue window (Bluetooth finding) and the whole-song read-ahead.
7. If anything is wrong: roll the server back with its rollback tag and reinstall beta 51 from the GitHub release page (same signing key).
