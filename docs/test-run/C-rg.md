# Test run 2026-10-11, workstream C-rg (RG Rotate, USB adb)

Baseline: beta 49 (versionCode 64), 720x720, density 240 (1dp = 1.5px, 44dp = 66px). Session cut short by the coordinator: partial run.
Screenshots in C:\FLACie_run\C\ (not committed). No device setting was changed, no playlists/downloads created, nothing to restore.
Note: the mini player flips to "Listening on FLACie Web / SM-F966B" whenever another workstream plays (remote session shown); local playback was started explicitly.

| Item | Result | Evidence |
|---|---|---|
| Home scrolls, sections render (Listen again, Albums for you, Quick picks, Your playlists) | pass | home2/home3.png; 8 swipes. Jank on Home scroll 10.4% of 202 frames (covers loading), see perf |
| Left rail nav Home/Search/Playlists/Explore/Settings (touch) | pass (Settings not opened) | search0, plist0, ex0 |
| Back stack (Search -> Back -> Home; back closes keyboard first) | pass | dp1.png |
| Search (type "love", results, keyboard) | pass, UX note | search1/2.png; with IME up only ~70px of results visible on 720x720 |
| Explore Songs/Albums/Artists/Genres/Charts tabs | pass | ex_alb, ex_gen, ex_charts |
| Explore quality filter (Codec FLAC: 5615 of 9287) | pass | ex4.png |
| Explore list scroll jank | pass | 8 flings, 356 frames, 1.97% janky (baseline 4.75%), p90 17ms |
| Play from Explore, mini player, full player | pass | play1, np1; AudioTrack state:started in dumpsys audio (needs ear check) |
| Seek thumb at both ends, no clipping | pass | seek_end.png (start), seek_99.png (end) |
| Next 20 skips quickly (BUTTON_R2 x20, then 25 taps on Next) | partial: app/UI stays alive, no ANR/FATAL, but see BUG-1 | queue index 2 -> 22 -> 47 |
| Info panel (spectrum live, real metadata, Location, Playback row, Hi-Res chips) | pass | info1/info2/skip2 |
| Hi-res tags | pass, note | 24-bit/96 kHz shows Hi-Res; 48 kHz with unknown bit depth is labelled HI-RES (AudioFacts.kt:18 rate > 44100) |
| Up next panel + Autoplay toggle, tap row = skip to it | pass | q1/q2 |
| Queue reorder / remove | fail (not present) | no reorder/remove UI in Up next on Android (only Play next / Add to queue / Clear); web/Fold parity? |
| Track sheet (Play next, Add to queue, favorites, playlist, album, artist, Hi-Res version) | pass (opened, Add-to-playlist list shown; no item executed) | sheet1/2, pl1 |
| D-pad focus: rail, cards, Play all | pass | dp3, dp5 (pink/white ring) |
| D-pad focus: Quick picks rows | fail | BUG-2 |
| Tap targets | partial | see BUG-3 |
| Startup `am start -W`, hrs of loop, 15 min memory loop | not run (cut short) | memory: PSS 191,680 KB at start -> 282,980 KB after ~25 min of browse + ~100 skips (growth +91 MB, loop not the specified one) |
| Logcat FATAL / ANR / OOM | pass | 0 matches in 25 min capture (C\logcat_full.txt). One `FLACieConnect post /Sessions/Playing -> HTTP 500` at 11:47:06 |
| Orientation/rotation, dark/light, font scale | not run |
| Folders, Lyrics, sleep timer, shuffle/repeat semantic, gapless | not run |
| Covers (placeholders seen for NAS/Local songs and "Albums for you", Listen again Jellyfin "06 - Jason Mraz - ...") | pass as known issue | home0.png |
| Playlists create/rename/add/undo/remove/reorder/delete/import, iPod sync | not run (Playlists page opened only) | plist0.png |
| Downloads, notifications | not run |
| Settings fields, Dashboard, Headphone test | not run |
| Media buttons: KEYCODE_BUTTON_R2/L2 next/prev | pass | skip tests; MEDIA_* keys not run |
| Background / screen off / audio focus / kill-restore / offline | not run |
| Jams / Connect | deferred (orchestrator) |

## Bugs

**BUG-1 (medium): after a burst of Next presses playback takes tens of seconds to start.**
Repro: playing a Jellyfin FLAC queue, press Next (BUTTON_R2 or the Next button) N times fast. Measured time from last press until position advances past 1.5s: 1 skip 5s, 3 skips 5s, 5 skips 20s, 10 skips ~20s, 20 skips 38s, 45 skips ~25-30s. State stays 6 (buffering) the whole time; during the stall wlan0 rx is flat (456 MB, 16 s with no bytes) and logcat from the app is silent, so the wait is for server/connection responses, not bandwidth or decoder work. The UI never freezes (old freeze is gone). Expected: the last selected song starts in ~3 s. Suspect: abandoned HTTP opens for skipped items still occupy the server / socket (DefaultHttpDataSource blocking open, 15s connect and 20s read timeouts in PlayerController.kt MultiServerDataSource) and Prefetcher.fetch (StreamSupport.kt, single thread, cancel(true) does not interrupt a blocking CacheWriter.cache()). Other workstreams were also hitting the same Jellyfin, so a server-load contribution is possible; re-test on a quiet server.

**BUG-2 (medium, RG D-pad): no visible focus indicator on Home "Quick picks" rows.**
Repro: Home, D-pad Right to enter content, Down past "Play all": focused row (dump shows focused=true, bounds [144,483][678,579]) has no ring or highlight (dp8/dp9.png). Cards and Play all do show a ring. Cause: ForYouUi.kt QuickPicks rows and the "..." button use plain `.clickable` with no focus styling. Likely the same on other `.clickable` rows; not swept.

**BUG-3 (low): small tap targets (px at 240 dpi):**
- Home "Play all" pill 35px = 23dp tall (bounds [528,553][702,588]) and it sits directly against the mini player.
- Now Playing page chips Lyrics / Info / Up next / Equalizer 57px = 38dp tall.
- Seek bar touch area 63px = 42dp.
- Explore A-Z fast-scroll index letters about 8-9px = 6dp each (width 48px).

**BUG-4 (low, UX on 720x720): Explore header is pinned and takes ~360px** (title, tabs, search/filters, filter chip, Shuffle/count), leaving 1.5-3 song rows, or one row of huge album tiles, visible (ex4/ex5/ex_alb.png). Consider collapsing the header on scroll.

**BUG-5 (low, accessibility): Now Playing overlay leaves the page beneath in the accessibility tree** (uiautomator dump shows Explore "More options for ..." nodes alongside Now Playing; they are reachable by focus/TalkBack while covered).

**NOTE:** "Equalizer" chip on Now Playing is permanently pink (looks selected) even when Info or Up next is the active pane; the active chip is ambiguous. Selected-rail-item focus is indistinguishable from selection (Home focus invisible while Home selected).

## Not done
Everything marked "not run": rotation/dark/font scale, folders, lyrics, sleep timer, shuffle/repeat/gapless, playlists CRUD, downloads, notifications, Settings and Headphone test, media keys other than R2/L2, screen off/audio focus/kill-restore/offline, startup time, the 15 min memory loop, gamepad A/B/X/Y mapping sweep.
