# Test run 2026-10-11, workstream G-rg (RG Rotate, USB adb, beta 50)

Build under test: `flacie-release.apk` from `C:\FLACie_out50\apk` (versionName 1.0-beta50), installed with `adb install -r` over beta 49 (sign-in kept).
Device: RG Rotate, Android 12, 720x720, 240 dpi (1 dp = 1.5 px). Screenshots and logs: `C:\FLACie_run\G\` (not committed).
Test data: playlist `TEST-G-rg` created, renamed to `TEST-G-rg-renamed`, deleted again. Nothing else created, no data cleared, no sign-out.
Device settings changed and restored: font_scale (back to 1.0), night mode (back to yes), accelerometer/user rotation (back to 1/0), Wi-Fi (re-enabled, verified),
the "Compact lists" toggle (hit by accident, switched back off), shuffle (switched off for the test, switched back on). Left paused: music.
Note: other testers' leftover playlists exist in the account (`TEST-A-import`, `TEST-A-renamed`, `TEST-G-win renamed ...`); not touched.
Method note: `uiautomator dump` fails with "could not get idle state" while music is playing (animating progress); focus checks were done while paused, otherwise by screenshot.

## Result table

| # | Item | Result | Evidence / notes |
|---|---|---|---|
| 1 | Rotation | not run | Device ignores rotation: `user_rotation` 0..3 all leave `mCurRotation=1` (panel is natively landscape, square). Nothing to test. |
| 2 | Dark theme | pass | All screens dark and legible: Home, Explore, Playlists, Settings, Now Playing (start.png, ex1.png, np1.png). |
| 3 | Light theme | partial | App has no light theme and ignores system night mode: `cmd uimode night no` leaves every screen dark (light1/2/3.png). Settings > Mode is Modern/iPod/Sync, not a theme. |
| 4 | Font scale 1.3 | pass | fs1.3_home/explore/np/np2/set.png: no overlap or clipping; titles truncate harder; chip rows scroll. |
| 5 | Font scale 0.85 | pass | fs0.85_explore.png and others: fine. Reset to 1.0 verified (`settings get system font_scale` = 1.0). There is no Settings > Display page in the app; the OS value was used. |
| 6 | Folders browser | not run | No folder browser exists in the Android app (no Folders tab: Explore has Songs/Albums/Artists/Genres/Charts; no Folders UI in source). |
| 7 | Lyrics | pass | Synced lyrics, current line highlighted, for a NAS song and a Jellyfin song (ly1.png, inf0.png). |
| 8 | Sleep timer | pass | Settings > Playback > Sleep timer cycles Off, 15, 30, 60, 90, 120 on each tap. Armed 15 min at 15:31:35, playback paused at 15:46:31 (log `state=2`). See BUG-7 for UX gaps. |
| 9 | Shuffle | pass | Toggle off/on visible (sh1.png, fin1.png). |
| 10 | Repeat | pass | Cycles all -> one (icon shows 1) -> off -> all (rp1..rp3.png). |
| 11 | Gapless/track transition state | pass | Track end to next: state stayed 3, item id advanced, position restarted at 15 ms, no state 6 in log (logcat 15:43:39-41). Cannot hear gaps. |
| 12 | Playlist create | pass | NP > Add to playlist > New playlist... > Enter; toast `Added "White Man" to TEST-G-rg` with Undo (ap3.png). |
| 13 | Playlist add (+confirmation, undo) | pass | Second add of a song to the renamed playlist worked (ad1.png, ud3.png). |
| 14 | Playlist remove + Undo | partial | Remove works with toast + Undo. Undo restored the song once (ud3.png) but once it did not (rm4/rm5/rm6.png: count stayed 0). Possibly a timing issue (tap about 2 s after the toast). |
| 15 | Playlist rename | pass | Rename button, dialog, Enter, toast `Renamed to ...` (rn2.png). |
| 16 | Playlist delete | pass | Delete button > Delete this playlist? > Delete; gone from list (del1/del2.png). Long-press on a list row also offers Delete playlist (lp1.png). See BUG-6 (no buttons when empty). |
| 17 | Downloads screen | pass | Settings > Dashboard > Download log: Running 0, Failed 52, Soulseek 197 filters, rows with reason, time, "took 2 min" (dl1/dl2.png). |
| 18 | Settings page | pass | Every row reachable by scrolling (set1..set9.png): Dashboard, Mode, album colour, accent, compact lists, badges, animations, autoplay, fetch ahead, streaming quality, EQ, volume limit, sleep timer, sources (Jellyfin, Plex, NAS), download log, headphone test, iPod sync, rescan, swipe/face button options, account, sync now, version 1.0-beta50. No Notifications or Permissions rows. |
| 19 | Dashboard page | pass | Counts (songs 9,292; Hi-Res 1,374; lossless 4,052; lossy 3,281; not read yet 585), last 100 downloads, services, import, free space, charts (dash1..dash5.png). |
| 20 | Headphone / volume behaviour | partial | Volume keys 24/25 change STREAM_MUSIC (range 0..25) (`cmd media_session volume`). Headphone test page opens, starts at -46 dB, warns to start low (hp1/hp2.png). Opening it paused the music (focus) and it did not resume. No headphones/Bluetooth connected, so plug/unplug behaviour not tested. |
| 21 | Media keys | partial | PLAY_PAUSE 85, PLAY 126, PAUSE 127, NEXT 87, PREVIOUS 88, HEADSETHOOK 79, REWIND 89 (-5 s), FAST_FORWARD 90 (+15 s), STOP 86 (state 0, 85 resumes from same position) all pass, also with the screen off. But BUG-5: PLAY 126 does not resume after another app took audio focus. |
| 22 | Gamepad keys | pass | L1/L2 previous, R1/R2 next, X (99) play/pause, Y (100) opens Now Playing, START (108) play/pause, DPAD + back navigate. SELECT (109) no visible effect; MODE (110) and THUMBL (106) are handled by the system (launcher/Google), not the app. |
| 23 | Kill and restore | fail | `force-stop` then relaunch: Home shown, no mini player, no media session (`dumpsys media_session` empty), queue and position lost (k0/k1.png). See BUG-3. Relaunch took 637 ms. |
| 24 | Offline behaviour | partial | `svc wifi disable`: no banner, toast or indicator anywhere in the app (only the OS Wi-Fi icon disappears, off1.png). Playing song kept playing from buffer; Next to a queued song worked; library (Explore/Playlists) stays browsable; Search typed offline showed nothing visible behind the keyboard (off4.png). Wi-Fi re-enabled and playback fine. See BUG-9. |
| 25 | Expired session | not run | Not possible without signing out or invalidating the token (forbidden). |
| 26 | Startup time (`am start -W`) | pass | Cold: 786 ms, 475 ms, 637 ms (TotalTime). After Home key (process alive): 972 ms (slower than cold; the activity is evidently recreated). |
| 27 | D-pad navigation through main screens | partial | Rail, Home chips, Play all, Home cards, Playlists rows, Settings rows, Now Playing buttons and chips are reachable. BUG-1: Explore search field traps focus. Focus can end on the mini player with no ring (BUG-2). |
| 28 | Focus ring on every focusable row | fail | Quick picks rows: none (confirmed, BUG-2). Mini player strip: none. Now Playing play button: white ring on white button, not visible. Playlists/Settings rows: only a faint grey fill. Chips, Play all, cards (pink), Next button: visible. |
| 29 | Tap/focus targets under 44 dp | fail (known, confirmed) | Home Play all about 27 dp tall (pill y 569-609 px at 720 px); Now Playing chips 38 dp tall; seek bar touch area 654x63 px = 42 dp (dump); Explore A-Z rail letters about 8 px. |
| 30 | Seek bar while PAUSED | pass | Paused, tap at 50%: bar and time jump to 3:15 and position 195.7 s; drag to 4:29 (269 s); three REWIND presses (-15 s) 4:29 -> 4:14, bar follows (sk0..sk4.png). |
| 31 | F1: NAS songs real tags | pass | Explore > Songs sorted Recently added: NAS badge rows show real titles ("White Man", "Teo Torriatte"), artist and album ("Queen - A Day At The Races"), durations (4:59), cover art, Hi-Res chip; Now Playing shows 24-bit/96 kHz, 3019 kbps (ex3/ex4/pl1/np1.png). |
| 32 | F2: Jellyfin file-name titles cleaned | pass | Search "06": 15 hits, none with an `NN - Artist - Title` style title; "Mraz": 17 songs titled "5-6", "93 Million Miles", "Be Honest", "Frank D. Fixer" (cover placeholders only). Titles starting with a number ("02 Cornish Acid") are genuine titles. (f2t*/q06/mraz1.png) |
| 33 | F3 first-launch screens | not run | Needs cleared app data / fresh install (forbidden, owner signs in). |
| 34 | F3 Settings entry for permissions explanation | not run | No such row in Settings on this device (Android 12: notification permission does not exist, nothing pending). Could not verify the entry exists on other versions. |
| 35 | Skip burst 5 | pass | 5 x Next (`input keyevent 87`): state 3 about 1.3 s and 4.4 s after the last press (two runs). |
| 36 | Skip burst 20 | partial | state 3 after 1.3 s, 2.9 s (first run), 6.6 s; varies. Beta 49 was 38 s. |
| 37 | Skip burst 45 | fail (improved) | state 6 until 7.5 s, 18.1 s and about 24 s (three runs; beta 49 about 25-30 s). UI never froze, no ANR. Target about 3 s. See BUG-4. |
| 38 | Background playback, screen off, 2 min | pass | Screen off (keyevent 26) 118 s: position 10.7 s -> 129.4 s, state 3; NEXT/PAUSE/PLAY keys worked while asleep; media notification present (category transport, 3 actions, public); lock screen is a custom clock face (no media controls shown, not app-related). |
| 39 | Audio focus | partial | YouTube video started: FLACie paused at once (state 2) and the focus stack shows YouTube on top. FLACie stays paused after YouTube is closed (correct for a permanent loss). Ducking not exercised (YouTube takes full focus). BUG-5: PLAY key does not resume afterwards, PLAY_PAUSE does. |
| 40 | Jams / Connect | partial | Devices page opens from mini player and Now Playing: lists "This phone" (this RG, Playing) and "FLACie Web" (Paused, with transport buttons, Play here / Send here). The Fold was not listed (not active), so no control/hand-over done. Jam page opens ("Start a Jam", "No Jams right now"); no Jam started (would show on the server for other testers). |
| 41 | Jank (Explore > Songs, `measure-jank.ps1 -Runs 5 -Label g-rg`) | pass | See table below: median 1.03 % janky (beta 49 baseline 5.7 %). One outlier run at 47.97 %. |
| 42 | `logcat -b crash` and main log at the end | pass | Crash buffer empty; 0 matches for FATAL EXCEPTION / ANR in / OutOfMemoryError. |

### Tally

PASS: 2, 4, 5, 7, 8, 9, 10, 11, 12, 13, 15, 16, 17, 18, 19, 22, 26, 30, 31, 32, 35, 38, 41, 42 = 24
PARTIAL: 3, 14, 20, 21, 24, 27, 36, 39, 40 = 9
FAIL: 23, 28, 29, 37 = 4
NOT RUN: 1, 6, 25, 33, 34 = 5
Total 42.

### Jank table (Explore > Songs, 12 swipes x 5 runs, y 533 -> 130, beta 50)

| Run | frames | janky | janky % | p50 | p90 | p95 | p99 | slow UI | missed vsync |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 293 | 3 | 1.02 | 17 ms | 17 | 17 | 22 | 3 | 0 |
| 2 | 286 | 6 | 2.10 | 17 | 17 | 18 | 48 | 5 | 1 |
| 3 | 293 | 2 | 0.68 | 17 | 17 | 17 | 23 | 1 | 0 |
| 4 | 292 | 3 | 1.03 | 17 | 17 | 17 | 25 | 2 | 0 |
| 5 | 296 | 142 | 47.97 | 19 | 31 | 32 | 32 | 23 | 0 (slowDraw 138) |
| median | | | **1.03** | | 17 | 17 | 25 | 3 | 0 |

(An earlier invalid attempt, where the search field had keyboard focus and the swipes typed into it, was discarded.)
Memory at the end (`dumpsys meminfo`): PSS 281,734 KB after about 30 min of use, 305,370 KB after the jank runs; Java heap 123-126 MB.

## Bugs

**BUG-1 (high on RG/D-pad): Explore search field is a D-pad focus trap and pops up the soft keyboard.**
Repro: Explore > Songs. With the D-pad: Right (Songs tab), Down. Focus lands in "Search songs", the on-screen keyboard opens and covers half of the 720x720 screen. Up/Down/Left/Right then do nothing (focus bounds stay `[207,226][418,298]`), Back only hides the keyboard, the focus stays. Only TAB escapes. The song list below can never be reached with the D-pad (the app's main use on this device).
Suggested fix: do not request the keyboard on focus (only on click/Enter/select); in the field's key handler pass DPAD_DOWN/UP/LEFT/RIGHT to `focusManager.moveFocus`; or add `focusProperties { next/previous }` so Down goes to Filters/the list.

**BUG-2 (medium, RG D-pad): focus is invisible on several controls.**
Quick picks rows on Home (focus moves, no ring, confirmed again), the mini player strip (a dump shows it focused, no ring), the Now Playing play button (white ring on the white button), and only a faint grey fill on Playlists/Settings rows. Repro: Home, Right, Down repeatedly (dpc01..06.png).
Suggested fix: one shared `Modifier.focusRing()` (2 dp white/accent border, also on `clickable` rows) applied to QuickPicks rows (`ForYouUi.kt`), the mini player and list rows; use the accent ring on the white play button.

**BUG-3 (medium): queue and position are not restored after the process is killed.**
Repro: play a song, `adb shell am force-stop com.ipodemu`, relaunch. Home shows no mini player and no media session; the song and position are gone (k1.png). A player normally resumes paused at the old position.
Suggested fix: persist queue ids, index and position (on pause and every few seconds) and restore them paused on start; or state clearly in the release notes that this is by design.

**BUG-4 (medium): playback still starts late after a long skip burst.**
Repro: playing from a queue, `adb shell "for i in $(seq 45); do input keyevent 87; done"`, then poll `dumpsys media_session` until state=3. 45 presses: 7.5 s, 18.1 s, about 24 s (20 presses: 1.3 to 6.6 s). Better than beta 49 (about 25-38 s) but not the 3 s target and very variable. Suspect abandoned opens/prefetch for skipped items still occupying the connection (see C-rg BUG-1); on a quiet server the NAS items are quicker than Jellyfin ones.
Suggested fix: cancel/abort the in-flight DataSource open for every skipped item (not only the prefetch), debounce the player prepare to the last Next, and collapse N queued Next presses into one seek to index+N.

**BUG-5 (medium): MEDIA_PLAY (126) does not resume after another app took audio focus.**
Repro: play in FLACie, start YouTube (`am start -a android.intent.action.VIEW -d <youtube url> com.google.android.youtube`), FLACie pauses (correct), `am force-stop com.google.android.youtube`, then `input keyevent 126` twice: state stays 2. `input keyevent 85` (PLAY_PAUSE) resumes. KEYCODE_MEDIA_PLAY works normally in other situations (after 127).
Suggested fix: handle PLAY in the session callback the same way as the toggle; check that the player is not left in a "paused by focus loss, ignore play" state.

**BUG-6 (low-medium): an empty playlist has no Rename / Delete / Play buttons; Undo after removal is unreliable.**
Repro: playlist with one song, row menu > Remove from playlist: the page switches to "Nothing here yet" with no Rename/Delete (delete only via long-press in the Playlists list, which is not discoverable). Tapping Undo about 2 s after the toast once did not restore the song (count 0); a second try restored it.
Suggested fix: keep the header action buttons in the empty state; make the Undo snackbar last at least 5 s and keep the removal pending until it is gone.

**BUG-7 (low): sleep timer gaps.** No countdown or "sleep in N min" anywhere (not in Settings, not in Now Playing); the timer is not kept across a process kill (after force-stop the setting reads Off); it is only reachable in Settings (no quick access from Now Playing); every tap changes it with no confirmation. Suggested fix: show remaining time on the Settings row and in Now Playing, add a Now Playing entry, add "end of song".

**BUG-8 (low): the Headphone test takes audio focus and the music is not resumed afterwards.** Expected for tones, but returning from the page leaves the player paused with no hint.

**BUG-9 (low): no offline indicator.** With Wi-Fi off the app looks normal; Search shows nothing useful (no "offline" message); Jellyfin covers/rows silently stall. Suggested fix: small "Offline" chip on Home/Search and an "offline: showing your library" empty state.

**BUG-10 (low): tap targets (confirmed, known).** Home Play all about 27 dp, Now Playing chips 38 dp, seek bar 42 dp, A-Z rail letters about 6 dp. Suggested fix: minimum 44 dp via `minimumInteractiveComponentSize` / extra touch padding.

**BUG-11 (low): song rows truncate titles heavily on 720 px.** "Teo Torria...", "Somebod..." because the Hi-Res + source badges take the width; at font scale 1.3 even short titles truncate. Suggested fix: hide the source badge on narrow widths or move badges under the title.

**BUG-12 (low, info): app ignores the system light mode; no theme choice at all.** (Not a defect if dark-only is intended; state it in the notes.) Also the first-launch Settings entry for permissions explanation could not be found.

**Observations (not bugs):** Hot start after Home (972 ms) was slower than a cold start (475 ms); a playlist item added from a NAS song ("White Man") showed the Jellyfin badge in the playlist (the same song exists on both sources, resolved to the Jellyfin copy); the Downloads page copy still says "YouTube (if switched on)" and the Dashboard still shows "from YouTube" counts from earlier runs (owner rule: no YouTube source); Explore search matches by words, so a trailing "-" is ignored; MODE and THUMBL gamepad keys leave the app (system behaviour); the first jank attempt was invalidated by my own typing into the search field (see above).

## Not tested and why
- Rotation (device does not rotate), light theme (none), expired session (needs sign-out), F3 first-launch screens (needs cleared data), folders browser (feature absent on Android).
- Hardware headphone/Bluetooth plug, volume limiter and tone playback by ear, gapless gaps by ear (cannot hear).
- Jam start and Connect hand-over to the Fold (Fold not listed; FLACie Web belongs to another tester, so no remote control was sent).
- Wi-Fi to mobile switching (the RG has no mobile data).
