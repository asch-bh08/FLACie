# Phone overhaul: written without a build (9 Nov)

Branch `phone-work`. None of this was compiled against Android: each file passed a Kotlin parse with no syntax errors, and every API it uses already appears elsewhere in the app. Build it, run the unit tests, then look at each screen on a phone (about 360 dp wide) and on the unfolded screen (1840x2208), with Animations on and then off.

| Commit | What changed for the person | Check on the emulator |
|---|---|---|
| Chips: every small tag is a round pill in the sans | Format and source chips on Now Playing, `FormatChip` (mini player, lists), search's Owned tag: round pills in Archivo, no longer mono boxes. The where-from pill at the top of Now Playing is fully round. Adds `rememberEntrance()` and `Modifier.entranceRise()` in Motion.kt | Now Playing (format and source chips under the artist), mini player, search with an owned result |
| Home: the chips wrap... | Home's mood and shortcut chips are a `FlowRow` (wrap, no sideways scroll); chips and shelves rise in when Home opens | Home with many moods: the chips wrap and none is cut off; scroll down and back: no shelf rises in again |
| Library lists... | Songs, Artists, Genres rows and the Albums grid rise in on open; Play / Shuffle / A-Z / count is a `FlowRow` | Songs at 360 dp: the count drops to its own line; Albums grid; Artists (round pictures) |
| Album and artist pages... | Cover, then the title, then Play/Shuffle rise in; the artist picture uses `circle = true`; the Download buttons show the ring-and-arrow glyph (DOWNLOADED) instead of the chevron, and a finished download a tick | An album, an artist, a playlist (Download chip), Songs with Offline only, a catalogue song and album page |
| Playlists: a grid... | Modern look: Import/Paste link on top, then a grid: New playlist tile (outlined square, round name field, Create), Favourites (heart cover), playlists, folder playlists. Long press still opens Share/Delete. iPod skins keep the old list | Make a playlist from the tile (keyboard Done and Create), tap the square (the cursor goes to the field), long press a playlist, the downloaded mark on a card |
| Settings: chips wrap... | The section chips and the DJ's Where choices are `FlowRow`s; each section's hint is sans 13.5 sp (up to 6 lines) instead of small mono | Settings at 360 dp: Account and About chips on screen; the DJ hint not cut off |
| Now Playing: the top bar fits... | Under 400 dp wide the DJ and mic buttons are 44 dp with smaller gaps (in a `BoxWithConstraints`), so Retro is not pushed off; Up next's ... is smaller and dimmer; the bare + is a "Save as playlist" pill | Now Playing with the DJ on at 360 dp (and with a sleep timer running): all five round buttons inside the screen. Up next pane: the pill fits beside "Playing from" |

## Unsure it compiles

- `androidx.compose.foundation.lazy.grid.itemsIndexed` (Albums grid, Playlists grid) is called fully qualified; only `grid.items` was imported before. It is in the same foundation library.
- `@OptIn(ExperimentalLayoutApi::class)` on a `FlowRow` statement at the start of `ShuffleHeader` and in two Settings rows: the same pattern as LivingSleeve's title chips.
- PlayerUi.kt now imports `androidx.compose.foundation.combinedClickable` (for `PlaylistCard`'s long press).
- `Modifier.weight(1f, fill = false)` in `PlaylistCard`'s Row, and `Modifier.weight(1f)` on the grid in `PlaylistsGrid`'s Column.
- Motion.kt `riseIn` gained a third parameter `riseDp` (default 28f): existing calls are unchanged.

## Not done

- Retro: the phone shows the web's Retro build (assets/retro from ipodsync/src/FLACie.RetroWasm), so its pills and Back/Options words come from design-j-retro.css with the next build; nothing to change in the app.
- Settings in two columns on the unfolded screen: not attempted (it is one LazyColumn; splitting it is a restructure).
- Now Playing's cover is laid out below the top bar in a Column (weight 1f, square of the room left), so it should never overlap; not otherwise changed.
- Sheets were already springing (ActionSheet, mini player); not touched.

## Cover memory (b126, unbuilt)

b125 kept Home's covers ready so they show at once, and memory at a warm start rose from 95 to 142 MB (first sync 120 to 136 MB). Three commits bring it back down without changing what Home's first screen shows: the decoded-cover cache is now sized by bytes, a twelfth of `ActivityManager.memoryClass` between 12 and 24 MB (`ArtBudget.lruBytes`, unit tested; it was a sixth of the largest heap, 42 MB and more, and kept-ready covers filled it); on dense screens a Jellyfin row cover drawn both as a song card and an album card is one bitmap instead of two (same 240 px sample, one key); and `ArtCache.warm` and `keepReady` decode only the first screen of Home (the first 8 covers of the first 4 shelves, `ArtBudget.firstView`), while the rest of each shelf, and each newer set of shelves after a sync, is only fetched to the disk and decodes as it is drawn. RGB_565 was not used (banding on cover gradients) nor HARDWARE bitmaps (LivingSleeve and DesignI read pixels for their tint). The local session should build, run `ArtBudgetTest`, then with `run-android.mjs` measure total PSS (`dumpsys meminfo app.flacie`, Graphics and Native Heap too) at a warm start and after the first sync against b125's 142 and 136 MB, and check by eye that Home's first screen still draws its covers on the first frame (cold and warm) and that scrolling a shelf sideways brings its covers in without a visible pop.
