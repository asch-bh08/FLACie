# Test run 2026-10-11, workstream B-fold (Galaxy Z Fold 7, SM-F966B, Android 16, wireless adb)

Baseline beta 49 (versionCode 64). Tested on the cover screen only (fold state CLOSED, 1080x2520, 420 dpi); inner screen (1968x2184) not tested.
The run was stopped early on the coordinator's request (usage limit): the 30 minute memory loop did NOT complete (see Memory) and many items are "not run".
The owner was also using the phone during the run (WhatsApp, Jellyfin mobile launched at 11:20-11:25). After the app crashed (BUG 1) a few scripted taps landed on the
launcher (a search field got "E3" typed); no other app was opened and no other app's content was read. Scripts were guarded afterwards (act only while com.ipodemu has focus).
No device settings were changed, no playlists or downloads were created, nothing needs restoring. Playback was left paused.
Screenshots: C:\FLACie_run\B\ (not committed).

## BUGS

### BUG 1 (high): app crashes (ArrayIndexOutOfBoundsException in the Compose runtime) when scrolling Home while music plays locally
- Repro (6 of 6 scripted attempts, roughly every 2 minutes): start a song on the phone (tap Pump It in Quick picks), tap the Home tab, then swipe up on Home
  (`input swipe 300 1800 300 700 300`). The process dies within 0.5 s of the swipe and Android shows the launcher.
- Seen at 11:38:49, 11:41:46, 11:43:13, 11:45:01, 11:46:43, 11:48:32; always right after "Home tab + swipe up". The same swipes on Search, Playlists, Explore, Settings did not crash.
  No crash with Home swipes at 11:33 (no local playback) or at 11:42 (not playing).
- Log:
  `E AndroidRuntime: FATAL EXCEPTION: main`, `java.lang.ArrayIndexOutOfBoundsException: length=1280; index=-5`,
  `at D.p.t(SourceFile:110)`, `at D.p.x(SourceFile:360)`, `at L.a.j`, `at D.r.a`, `at t3.k.e`, `at u.l.e(SourceFile:34)`, `at C2.x.j(SourceFile:39)`, ... `at w.D.run(SourceFile:147)`, `at android.os.Handler.handleCallback`.
- Suspected: using android/app/build/outputs/mapping/release/mapping.txt, `D.p` = androidx.compose.runtime.ComposerImpl, `u.l` = LazyListItemProviderImpl,
  `w.D` = LazyLayoutPrefetcher, `C2.x` = com.ipodemu.player.AppRootKt$PlayerRoot$6. So a LazyColumn/LazyRow item is composed during lazy-layout prefetch on Home
  (player/AppRoot.kt PlayerRoot) while the playback state changes (rows with now-playing indicator, Autoplay queue growth). Compose BOM 2024.06.00 / compiler 1.5.14
  (android/app/build.gradle.kts) has known ComposerImpl prefetch crashes; candidates: bump the Compose BOM, or look at Home items with unstable keys or a changing item count.
- A proper `retrace` needs the mapping of the exact beta 49 build (the file in the tree is from 11:13 today and may be a later build).

### BUG 2 (medium): seeking while paused does not update the seek bar or elapsed time
- Repro: Now Playing, pause, tap or drag the seek bar. The media session reports the new position (e.g. 1:44, or 4:33 at the far right) but the bar and the "0:11" label stay put.
  Pressing play then jumps (at the far right the song ends at once and skips).
- Cause: player/PlayerUi.kt `rememberPosition(player, active)`: `LaunchedEffect(active)` re-reads `player.positionMs` only when `active` changes and loops only while active,
  so a seek while paused changes neither. Fix: also key it on a seek counter, or read the position right after `seekTo`.

### BUG 3 (low, gap): the Modern "Up next" drawer cannot remove, reorder or clear queue items
- player/UpNextDrawer.kt `QueuePanel`: rows only `skipTo`; it shows `upNext(30)`; no drag handle, no swipe to remove, no Clear. Remove (swipe + X) and Clear exist only in the
  iPod-mode `QueueScreen` (PlayerUi.kt ~line 1228). Play next / Add to queue exist in the song menus. Swipe-left and long-press on a drawer row did nothing (q2.png).

### BUG 4 (low): notification permission is re-requested on every launch
- `MainActivity.ensurePermission()` calls `requestPermissions` in every `onCreate` while POST_NOTIFICATIONS is denied. On this phone it is USER_FIXED denied, so each cold start
  creates and instantly finishes `GrantPermissionsActivity` (wm_create_activity / wm_finish_activity at 11:33:18, 11:33:21, 11:33:25, 11:33:28, 11:39:54). READ_MEDIA_AUDIO is also asked
  for Jellyfin-only users, with no rationale. POST_NOTIFICATIONS is denied for FLACie on the owner's phone (dumpsys package).

### Minor observations
- Info panel spectrum caption says "MP3 usually ends here" although the file is AAC (a WMA source transcoded by the server).
- Some Up next rows (for example "God Didn't Let Me Break") show no duration.
- First open of the Playlists tab: 54 frames, 42% janky, 99th percentile 250 ms (first composition with covers); later tabs fine.
- `FLAG_KEEP_SCREEN_ON` is set for the whole activity (MainActivity.onCreate), so the screen never times out while the app is in front, even when paused.

## Results

| Item | Result | Evidence |
|---|---|---|
| Home scrolls smoothly (no local playback) | pass | gfxinfo 909 frames, 2.3% janky, p50 5 ms, p90 7 ms, p99 17 ms |
| Home scroll while music plays | FAIL | BUG 1 |
| Tab navigation Home/Search/Playlists/Explore/Settings | pass | t_Playlists.png, t_Explore.png, t_Settings.png; Explore 974 frames 2.5% janky p99 12 ms; Settings 3.6% p99 17 ms |
| Back stack | partial | BACK from the full-screen player returns to the page; deeper stacks not run |
| Pull to refresh | not run | out of time |
| Orientation, dark/light, max font and display scaling, split-screen | not run | out of time (manifest: RESIZE_MODE_RESIZEABLE, configChanges handles fold/unfold without recreate) |
| Explore Songs (9164 songs, A-Z index, Hi-Res and Jellyfin chips, sort, Filters) | pass (visual) | t_Explore.png |
| Artists, albums, folders, search, charts, quality filter | not run | out of time |
| Play a song from Explore and Home | pass | media_session PLAYING, mini player |
| Play/pause (UI and media keys) | pass | KEYCODE_MEDIA_PAUSE/PLAY toggled PAUSED/PLAYING |
| Seek accuracy | pass | tap at 50% -> 78.8 s of 153 s; 25% -> 41.8 s; 75% -> 115.9 s; drag to far left -> 0:00 |
| Seek thumb at both ends | partial | left end fully visible (sk_mid.png); right end not shown because of BUG 2 |
| Skip 20 songs quickly (20 taps, 0.25 s apart) | pass | app alive, next song PLAYING within 3 s, no ANR or FATAL, gfx 580 frames 7.8% janky p99 48 ms, sk1.png |
| Next/previous, shuffle (was on), repeat | partial | next/previous pass; shuffle/repeat state shown, not toggled |
| Queue view (Up next, Autoplay toggle, jump) | pass | q1.png |
| Queue remove and reorder | FAIL | BUG 3 |
| Gapless | needs ear check | song end advanced to the next track without UI gap |
| Full-screen player, Lyrics pane, Info pane | pass | lyr1.png, info1.png, info2.png |
| Info panel: LIVE spectrum, chips, metadata, Location, Playback row | pass | info2.png (resampling explained: "AAC 44.1 kHz ... mixer 48.0 kHz"); "Came from" not present for this Jellyfin song |
| Hi-res tags | pass | "Hi-Res" chip on Blue Oyster Cult rows in Explore |
| Covers: missing art, filename-style title ("Track  9", "Track 1") | pass | np1.png shows note placeholder, title kept |
| Sleep timer, Equalizer pane, volume limit | not run | out of time |
| Playlists tab list (Favorites 5, 2026 29, AA 353, ...) | pass | t_Playlists.png |
| Playlist create/rename/add/undo/remove/reorder/delete/import | not run | out of time; no TEST-B playlists were created |
| Downloads (request, status, log, notifications) | not run | out of time |
| Settings fields | partial | page renders (t_Settings.png); nothing toggled |
| Sign out, Quick Connect | blocked | needs the owner (as instructed) |
| Jams / Connect | deferred | orchestrator; the "Listening on FLACie Web" remote bar appeared correctly while another device played |
| Permission prompt flow | described | BUG 4 (data cannot be cleared) |
| Cutout, gesture nav | pass (visual) | punch-hole and status bar clear of the header, gesture pill not overlapped |
| 3-button nav | not run | |
| Background playback, lock-screen and notification controls | partial | media session com.ipodemu present with metadata and actions; notification shade not inspected (POST_NOTIFICATIONS denied) |
| Headphone / media buttons | pass | KEYCODE_MEDIA_PLAY/PAUSE honoured |
| Audio focus, screen off, kill and restore, offline, Wi-Fi/mobile switch, rotating while playing | not run | out of time |
| Edge cases: unicode (umlaut), very long title (ellipsised), missing artwork | pass (visual) | Home and Explore screenshots |
| MP3 / FLAC / AAC chips | pass | MP3 192 kbps, FLAC 16-bit/44.1 kHz, AAC 256 kbps shown correctly |
| Empty library, 10,036 song playlist, server unreachable, expired session, slow server | not run | |
| Startup (cold, `am start -W`) | pass | TotalTime 242, 150, 121, 126 ms; hot 31 ms |
| ANR / OOM / StrictMode / skipped frames in logcat | pass except crash | only the BUG 1 FATAL EXCEPTION lines |
| 30 minute memory loop | NOT COMPLETED | see below |

## Memory (incomplete, not a valid 30 minute run)
The loop kept being interrupted by BUG 1 (process restarted about every 2 minutes), so no long-lived series exists. `dumpsys meminfo com.ipodemu` TOTAL PSS (KB):
- Long-lived process (about 6 min of playback, 20 skips, tab browsing, Info and Lyrics): 382,319 total, Java heap 145,492, native 134,592, graphics 58,432 (11:38); 359,023 a minute earlier.
- A later process (pid 9008, about 1.5 min): 452,268 total with graphics 194,828 (GPU/EGL spike, probably cover bitmaps on Home).
- Fresh processes after a crash: 213,470 / 251,450 / 233,836 / 240,588.
Reading: the earlier 243 MB figure matches a fresh process; after a few minutes of browsing and playback the process sits at 360-450 MB (Java heap 120-145 MB, native about 135 MB,
graphics 58-195 MB). Whether it keeps growing over 30 minutes is NOT established. `am send-trim-memory` not run.

## Counts
pass 20, fail 3 (Home scroll while playing = crash, queue remove/reorder, seek while paused), partial 5, blocked 1, deferred 1, needs ear check 1, not run about 16.
