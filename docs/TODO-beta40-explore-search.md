# Explore search upgrade (asked 2026-10-08)

**Status: shipped in beta 41 (web, Android, Windows).** Web done (typo-tolerant search, codec + bit rate + every sample rate filters, collapsible filter grid, nicer search box, Search hidden from the side rail while in Explore). Android and the Windows app still to do on the Android side: same filters in `ExploreUi.kt`, typo tolerance, nicer search field.

Asked mid-session; queued behind the beta 40 test/release work. Applies to web, Android and Windows (same release).

1. **Better search inside Explore**
   - Typo tolerance (fuzzy match: "beyonse", "metalica", transposed letters), on top of the existing punctuation/accent-blind matching.
   - New filters, all combinable with the existing ones: **audio codec** (FLAC, ALAC, MP3, AAC/M4A, OGG/Opus, WAV, WMA ...), **bit rate (kbps)** (ranges: up to 128, 129-256, 257-320, 321-999, 1000+), **sample rate** (already there: widen to every rate), **bit depth** (already there).
   - Data source: `FormatIndex` / `GET /api/audiofacts` (codec, rate, depth, kbps per song; see HANDOFF "Beta 20"). Local-only (guest) songs on Android have no facts yet: read them in `Scanner.kt` with MediaMetadataRetriever (sample rate / bit depth need API 31+, minSdk is 30).
2. **Remove Search from the side rail** where Explore has its own (web `MainLayout.razor` rail link `/search`; the top bar search stays on every other page, and Search still does "More music" downloads).
3. **Make the Explore search box look better** (web `ExploreBar.razor` / `app.css` `.x-bar`; Android `ExploreUi.kt` "Filter this list" pill): clear (x) button, result count, match highlight, calmer spacing, same look in both.

Found while testing (not yet done):
- Android: Hi-Res / quality filters and format badges never match device-only files (no audio facts for them; see item 1).
- Android: the "Local" source pill squeezes the artist/album line ("First Albu...") when every song is local; hide the pill when there is only one source.
- Android: the Quality filter sheet shows a list icon on unselected options (looks like a bug next to the tick).
- Web: Albums (1436) and Artists (781) draw every card at once; consider chunked rendering.
- Web: the Explore filters and A-Z letter are shared across the Songs/Albums/Artists/Genres tabs (can show "0 of 781 artists" after filtering Albums); whitespace-only search says "7514 of 7514 songs".

Still open from the list above: hide the Android source pill only when mixed (done), read format facts for device-only files (not done), chunked rendering of the big Albums/Artists grids (not done), Explore filters shared across tabs (not changed).

## Web filter UI refactor (2026-10-08, after beta 41; deployed to your-server, not yet in an MSI/APK release)
- Explore bar is one row (search, Sort, "Filters (N active)"); chosen filters are removable pills under it with "Clear all"; Genre / Decade / Quality are the main filters and Codec / Bit rate / Bit depth / Sample rate sit in an "Audio specs" drawer.
- Own dropdown component (`Components/Shared/Dropdown.razor`, `flacie.ddPlace` in flacie.js): 240 px max height with scrolling, flips upwards (and aligns right) when there is no room, accent-coloured selected row with a check, group headings. Codec is grouped Lossless / Lossy / Hi-Res (Hi-Res FLAC / ALAC / WAV).
- Typo notice is soft grey with "Did you mean ...?" (`Matching.Suggest`); "Nothing matches ..., not even closely" when there is nothing.
- Also: every boolean aria-expanded / pressed in the web UI now renders true/false.
- Android already has this shape (search + Filters + Sort, removable chips, a Filters sheet); no change made there.

## Web Search results layout (2026-10-08, after beta 41; deployed to your-server, not yet in an MSI/APK release)
- `/search` is now sectioned like Spotify: a Top result card (artist named exactly like the search, else an album named so, else the best song, with source / format / bit depth + sample rate / kbps badges and a play button) beside the five best Songs (Show all N songs below), then Artists (circular, "Artist - N songs"), Albums (cover, artist, year), Playlists, Genres chips, and the existing "More music" downloads.
- The same filter pills as Explore (compact: Filters button, pills, drawer) narrow every section, including the counts on the cards. The text is still the box at the top of the page.
- The two-column split follows the width of the page area (container query), stacking when narrow; Explore track rows get 16 px of right padding so the menus do not touch the A-Z bar.

## Explore Overview tab (2026-10-08, after beta 42; deployed to your-server, not yet in an MSI/APK release)
- The sectioned results are now one shared component (`Components/Shared/ResultSections.razor`) used by the new **Explore > Overview** tab (`/explore`, the first tab and where the side menu's Explore goes) and by `/search`. Stacked and full width: Top result banner (188 px cover, badges, play), Songs list (48 px thumbnails, 8 rows, "See all N songs" opens the flat Songs tab with the same search and filters), Albums in a row of big square covers (title, artist, year), Artists as round avatars, then Playlists and Genres chips. Without a search the Overview browses: newest songs and albums, biggest artists.
- The flat table stays on the Songs tab (`/songs`) only. Overview has its own Sort (Best match, Title, Artist, Recently added, Newest year, Longest) kept apart from the Songs tab's.
- Android still has its own Explore (Songs / Albums / Artists / Genres / Charts) with a Filters sheet; it does not have the Overview.
