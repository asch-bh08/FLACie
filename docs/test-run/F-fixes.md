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
