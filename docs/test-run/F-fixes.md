# Test run 2026-10-11, workstream F-fixes

Baseline: beta 49. Mark each checklist item pass / fail / blocked with evidence.



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