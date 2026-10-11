# Test run 2026-10-11, workstream F-fixes

Baseline: beta 49. Mark each checklist item pass / fail / blocked with evidence.


## F2: Jellyfin songs with no tags show their file name

What changed
- One cleaning function, written twice with the same rules and the same 50-case table: `android/app/src/main/java/com/ipodemu/library/TrackTitles.kt` and `ipodsync/src/FLACie.Core/TrackTitles.cs`.
- It only acts when the item's title equals the file's base name (case-insensitive, underscores = spaces). A tagged title is never rewritten ("99 Problems", "1979", "7 Rings", "22", "1-800-273-8255" stay).
- Reads: "06 - Title", "06. Title", "06 Title" (only zero-padded with a missing artist/album, or when 2+ other files in the folder are numbered, and never when the front part is a known artist), "06 - Artist - Title", "Artist - Title", "Artist - Album - 06 - Title", "1-06 Title", "1.15. Title", underscores, "[Hi-Res]" and "(www.site.com)" suffixes, "Title - Artist" when the right side is a known artist, "feat." kept in the credit. An empty or "Unknown"/"Various Artists" artist is filled from the name.
- Android wiring: `JellyfinDirectClient.allAudio` and `search`, `JellyfinConnect.streamTrack`, and the cached remote list on load (`Library.loadRemote`). `Track.origTitle` (new, persisted as "ot") keeps the old file-name title; `Library.resolve` uses it so older saved playlist/favourite entries still resolve (path match comes first anyway, and the Jellyfin stream path is unchanged).
- Web/Windows wiring: `JellyfinClient.AllAudioAsync` and `TrackAsync` call `TrackTitles.CleanAll`; `Track.OrigTitle` added; `Library.Resolve` falls back to it.
- `filePath`/`FileKey` are untouched, so the NAS copy of the same file is still recognised as the same file; `MatchKey` of a cleaned song equals the NAS copy's when it is named "Artist - Title".

Tests
- .NET: `cd ipodsync/tests/FLACie.Tests; dotnet test` : 23 passed (3 new in `TrackTitlesTests.cs`).
- Android: `TrackTitlesTest.kt` (same table). Run `./gradlew.bat :app:testDebugUnitTest --tests com.ipodemu.library.TrackTitlesTest` under the build lock. STATUS: the run was started but not confirmed finished when this session was wrapped up; run it first.

Known limits
- NAS scanning (`NasDirectClient.cleanTitle`, `NasClient.CleanTitle`) was not moved onto the shared function; it keeps its own rules.
- "Pt. 2 - Reprise" style names with no artist are read as "Artist - Title". "12 Title" style needs numbered siblings in the same folder.
- Titles are not re-cased; album is not filled from the name.
- Android: the library-level pass only learns known artists from properly tagged Jellyfin songs.

Verify
- Device: connect the Jellyfin library with an untagged file such as "06 - Jason Mraz - Everything Is Sound.mp3"; after a library refresh it shows title "Everything Is Sound", artist "Jason Mraz"; Info panel Location still shows the file name. Playlist entries saved earlier still play.
- Web: sign in, search the same song; same title and artist; no duplicate if the NAS has the same file.
## F3 first launch: sign-in before any system prompt

Branch fix/f3-firstlaunch. Problem: MainActivity.onCreate requested the music and notification permissions at cold start, so the system prompts covered the app before the user saw what it was.

What changed

- No permission is requested at launch. The sign-in / source screen is first and explains what FLACie will ask for and when. The rules are pure functions in PermissionPlan.kt (unit tested).
- Music permission (READ_MEDIA_AUDIO on Android 13+, READ_EXTERNAL_STORAGE below): asked only when the user picks Music on this device (the page shows "FLACie needs access to the music on this phone" first; buttons Allow access and start / Start without access). Existing guest installs that never answered get one reason page, then the system prompt. Server / NAS accounts are never prompted; Settings > Permissions offers it.
- If refused: Home (modern theme) shows a card with Open app settings; Settings > Permissions has the same row. Server sources keep working.
- Notification permission (Android 13+): a reason page ("Show playback controls?", Continue / Not now) the first time a track starts while the app is in front, never at launch or during sign-in. Not now is remembered (no nagging); Settings > Permissions lets the user change it.
- Already granted or already answered: no prompt again (prefs mediaAsked / notifAsked, plus a live check on every resume).
- Bluetooth: the app has no BLUETOOTH_CONNECT use or manifest entry, so nothing to defer.
- Gamepad: first choice on the sign-in screen starts focused, focus ring on every choice, A activates, B goes back; reason page focuses Continue.

Verify (do not pm clear on an owner-signed-in device; use pm revoke instead)

1. Clean device or emulator only: adb shell pm clear com.ipodemu, launch. Expect: sign-in screen with three choices and the "What FLACie will ask for" text, no system dialog. Pick Music on this device: page with the reason line, then Allow access and start shows the system prompt. Deny: Home shows the card with Open app settings.
2. Owner devices: adb shell pm revoke com.ipodemu android.permission.POST_NOTIFICATIONS (Android 13+), reopen, play a song: reason page, Continue, system prompt. Relaunch after allowing: no prompt. For music: pm revoke com.ipodemu android.permission.READ_MEDIA_AUDIO (READ_EXTERNAL_STORAGE on the RG, Android 12).
3. RG Rotate 720x720: D-pad up/down moves the ring, A selects, B backs out; nothing is cut off at largest font size (the page scrolls).
4. Fold: cover screen and unfolded: content is a centred column up to 520dp wide.

## F4 - list performance (branch fix/f4-listperf)

Status: code changes done and committed; NOT measured on a device (none available to this workstream). Compile and unit-test status is in the final report of the run.

### What changed and why
- library/ArtCache.kt: Compose cover loads (`load`) no longer run on Dispatchers.IO (up to 64 parallel JPEG decodes during a fling). Downloads (remote covers only) stay on IO, decodes go to a 2-thread decoder at background priority; a row that scrolled away is cancelled before it runs. Rows sharing one album cover re-check the memory cache before decoding. Local misses are remembered (no file stat per row). Decode/download threads run at THREAD_PRIORITY_BACKGROUND. New `thumbSample` (3 by default; App.kt sets 4 below 1.6 density, so 80 px thumbs on the 240 dpi RG Rotate, still sharper than a 44 dp row).
- App.kt: the old "art" folder delete moved off the main thread; thumbSample set from density.
- player/Widgets.kt: IpodRow no longer draws a transparent gradient shader behind every idle row, no longer uses the composed{} wheelTracked modifier, and uses one interaction collector instead of two coroutines per row. ArtImage only draws the placeholder gradient when there is no cover, remembers its shape, and rememberArt launches no coroutine for covers already in memory.
- player/PlayerUi.kt: contentType on song and group lists, per-row subtitle, duration and label strings remembered, fmtTime without String.format.
- library/Track.kt: `hiResNameC` and `searchGlueC` computed once per track (URL-decode per badge, SearchRank.words per song per keystroke).
- player/ExploreEngine.kt (new, pure) + ExploreUi.kt: Explore filters, text search and sorts run on Dispatchers.Default via produceState (150 ms debounce while typing, previous list stays up), with sort keys computed once per item instead of per comparison. Explore no longer reads userData.rev (every play or favourite recomposed the whole screen). Genre/decade choices are computed in the background.
- tools/measure-jank.ps1: reproducible gfxinfo measurement. Test: app/src/test/.../player/ExploreEngineTest.kt.

### Findings (onLoaded)
`ArtCache.onLoaded` is only set by the old IpodView (canvas iPod UI); the Compose screens use `load()` + per-image state, so a cover finishing only recomposes that one ArtImage. It is not the cause of whole-screen recomposition.

### Not done
- Home, Search, Playlists, Now Playing and Up Next were read but not changed beyond the shared row/art improvements. PlaylistsList still calls lib.byPath()/resolve per row on the main thread and DetailScreen builds its track list in composition.
- Library merge / AccountSync startup review not done.
- TrackRow still takes `snap` and an un-remembered onPlay lambda, so visible rows recompose when the playback snapshot changes.
- Scheme colours (sc.onBg etc.) are read in composition by every row: the 900 ms cover colour fade after a track change recomposes visible rows each frame. Reading them in a draw/graphicsLayer lambda would fix it.

### Measuring (before/after, RG Rotate and Fold)
Baseline, beta 49, RG Rotate, Explore > Songs (10,036 songs), 12 swipes: 463 frames, 22 janky (4.75 %), p50 17 ms, p90 18 ms, p95 21 ms, p99 38 ms, 19 slow UI thread, 5 missed vsync.

1. Install the build, open FLACie, go to Explore > Songs, wait for the list to settle.
2. `powershell -File android\tools\measure-jank.ps1 -Device <serial> -Swipes 12 -Runs 3 -Meminfo -Label before` (prints per-run and median figures plus meminfo).
3. Install the other build, same screen, same command with `-Label after`. Repeat on Home and other tabs (Albums, Artists, Playlists) by opening them first.
Success: janky % clearly below 4.75 %, p95/p99 down, fewer slow-UI-thread frames, Graphics/Java heap not higher.

### Risks
- Explore list now appears one frame later after a tab/filter change (blank until computed); unfiltered Songs is instant.
- 80 px thumbs on density <= 1.6 might look soft on large cover tiles that use thumb = true (grids); check Albums grid and Home shelves on the RG Rotate.
- Background-priority decode threads can delay covers while the CPU is busy.
