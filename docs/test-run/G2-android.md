# Test run 2026-10-11, workstream G2-android (RG Rotate fixes and layout audit)

Branch `fix/android-g2` (from `release/beta50`). Device: RG Rotate over USB, 720x720 px at 240 dpi. Note for later work: that is 480 x 480 dp
(the Android config says `sw480dp w480dp h432dp` with the status bar), not 360 x 360 dp as the brief said. All short-screen rules therefore
key on the height (< 500 dp) and the narrow-row rule on the width (< 520 dp). Android-only fixes (web and Windows do not have these screens).
Screenshots were kept outside the repo. Test data: playlist `TEST-G2` (created, one song added and removed, deleted again). Settings touched
and restored: font scale (1.3, back to 1.0), Wi-Fi off/on, sleep timer (15 min, back to Off). One Settings toggle (Animations) was hit by
accident while scrolling and switched back on straight away.

## Bugs from G-rg

| Bug | Result | How it was checked |
|---|---|---|
| BUG-1 Explore search D-pad trap + keyboard on focus | fixed (and the same fix on the Search tab field) | New `SearchPill` (Focus.kt): the text field cannot take focus until Enter/centre/tap; Up/Down always leave, Left/Right leave at the ends of the text; the keyboard opens only on click/Enter. On the device: Down from the field lands on Shuffle, then on the song rows, no keyboard until Enter. Unit test `FocusAndRemovalTest` (direction logic). |
| BUG-2 focus ring | fixed | One shared ring (`FocusRingWidth`, `ringWhen`, `focusRing`): `IpodRow` (every list/settings/explore row), `IconAction`, rail items, mini player (local and remote), search pill; Now Playing play disc gets an inset accent ring. Checked by D-pad on the device (rows, rail, mini player, close button, play disc). Also found and fixed: Down on Now Playing walked into the page underneath (the covered page, sheets and dialogs are now not focusable). |
| BUG-5 MEDIA_PLAY (126) after another app took focus | fixed for the app in front, not for the app in the background | Reproduced with YouTube: after YouTube was closed, 126 did nothing while 85 worked. Cause: the activity handled PLAY_PAUSE itself but not PLAY/PAUSE; the system sent PLAY to YouTube's dead receiver (`dumpsys media_session`: "Last MediaButtonReceiver: youtube", "Media button session is null"). The activity now handles 126/127 too: resumes. With the app in the background (home screen) the system still routes the key to the closed app's receiver; an AudioManager workaround did not change that and was removed. That is Android routing, not fixable from the app. |
| BUG-6 empty playlist / Undo | fixed | An emptied playlist keeps Play, Shuffle, Rename, Remove dupes, Delete. Removing a song from a playlist page is now pending: the page hides it, Undo (snackbar now 6 s) cancels it, the data is changed 6.5 s later (or when another playlist edit happens). Verified on the device: remove, Undo after 2 s restores it (1 song); remove and wait 8 s leaves 0 songs. |
| BUG-3 queue lost after kill | fixed | `QueueStore` (JSON of the queue window of up to 300 songs, index, position) written on pause, on queue changes and every 5 s while playing; restored paused at start (mini player appears, nothing plays, no history entry); stale local files dropped; the service/session is started so media buttons reach it; a media button receiver was added so a headset button works after the app was closed. Swiping the app away still stops the music but keeps the queue. 10 unit tests (`QueueStoreTest`). Device: pause, `force-stop`, relaunch: same song and position, paused. |
| BUG-4 skip burst | improved, outliers remain | Next/Previous presses within 400 ms only move a target (the UI shows it at once); the player jumps once 250 ms after the last press. Media buttons and the notification go through the same logic (`ForwardingPlayer`). 45 presses (`input keyevent 87` loop): 3.4, 3.4, 3.7, 3.8, 4.3, 9.5 s, and three runs of 20 to 40 s. The slow runs were one particular Jellyfin song that the server answers very slowly (data arrived at about 2.5 Mbit/s after a long wait, state 6 until then, no error in logcat); the same behaviour without a burst. Not a client queue problem, not fixed. |
| BUG-7 sleep timer | fixed | Settings row shows "N min left" (opens the choices), Now Playing shows a clock chip with the time left in the header (tap = choices), Now Playing "More" has a Sleep timer entry, and the timer carries on after the app is killed (wall-clock stored). Device: set 15 min, kill, relaunch: "15 min left"; set back to Off. `SleepTimerTest`. |
| BUG-9 offline | fixed | "Offline" chip on Home and Search (ConnectivityManager callback). Device: Wi-Fi off showed the chip on both; Wi-Fi back on removed it. |
| BUG-11 narrow song rows | fixed | Below 520 dp width the source badge is dropped and the Hi-Res badge sits next to the artist line, so titles use the room ("'39 (Live At Earl's Court)" instead of 7 letters). |
| Hot start slower than cold | not reproduced | Hot 72-83 ms, cold 600-660 ms (one 2.2 s cold outlier right after hot starts). Nothing to change. |
| BUG-8 headphone test pauses music | not changed | By design (tones need the focus); not in the list of fixes. |
| BUG-12 no light theme | not changed | Dark only is a product decision. |

## Layout audit (every screen at font scale 1.0 and 1.3 where it matters)

| Screen | 1.0 | 1.3 | Finding / fix |
|---|---|---|---|
| Home (shelves, quick picks, remote bar) | ok | ok | mini player was a fifth of the screen: 56 dp and 4 dp margins on short screens; offline chip |
| Search idle / typing / results | ok | ok | title bar dropped on short screens when Search is a tab (room for results); field is D-pad safe |
| Explore Songs | ok | ok | A-Z rail (28 letters of 6 dp) is gone on short screens, "Starts with" is in the Filters sheet; Hi-Res badge under the artist |
| Explore Albums / Artists / Genres | ok | n/a | same rail change; no clipping |
| Explore Charts | ok | n/a | no change |
| Album / artist / genre detail | ok | n/a | no change |
| Playlists, a playlist, empty playlist | ok | n/a | empty playlist keeps its buttons (BUG-6) |
| Now Playing, cover | fixed | ok | the controls column is centred in the height, four round buttons (Lyrics, Info, Up next, More) instead of a row that scrolled off the edge; Equalizer, Add to playlist, Sleep timer are in More |
| Now Playing, Lyrics / Info / Up next | fixed | ok | the pane takes the whole width (it was a 190 dp column: chips and graph titles were cut, the graph was below the fold) and the controls are one strip under it; the Info chips scroll in one row, the graph (with its axis) is on screen, explanation text on tap |
| Up next (queue pane) | fixed | ok | same full-width pane |
| Track menu sheet, Sort / Filters sheets | ok | n/a | scroll; first entry has focus |
| Equalizer sheet, Sleep timer sheet | ok | n/a | no graph exists, a list; scrolls |
| Headphone test | fixed | n/a | the tab row scrolls away with the content on short screens (more room for the tests) |
| Dashboard | ok | n/a | no graphs on Android, lists only; no clipping |
| Settings (all rows) | ok | ok | sleep timer row now a sheet |
| Download log | ok | n/a | removed the YouTube wording from the copy |
| Devices page, Jam page | ok | n/a | no change |
| New / rename playlist dialog | fixed | n/a | Cancel and Create were hidden behind the keyboard on this screen; the dialog now sits at the top, is tighter and scrolls |
| Sources (Library list) | ok | n/a | no change |

Not visited: Sync mode and the iPod skin screens (not used on the RG), first-launch screens (needs cleared app data), Plex/NAS/Jellyfin
setup forms and Lidarr setup (they contain credentials; only the layout code was read, they use the same scrolling forms).

## Tests

Unit tests: 99 run, 0 failed, 2 skipped (the 2 skips were already skipped). New: `QueueStoreTest` (10), `FocusAndRemovalTest` (6), `SleepTimerTest` (3).
Release build installed on the RG (same debug key, sign-in kept).
