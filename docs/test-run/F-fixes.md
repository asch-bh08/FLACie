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
