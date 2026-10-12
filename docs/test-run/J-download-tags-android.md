# J - Android: "new download" wording, download status tags, phone to web report (beta 54)

Branch `beta54/android` (from `release/beta52` at beta 53). Only `android/` and this file changed. No version files touched.
Contract: `C:\FLACie-handoff\DOWNLOAD-TAGS-SPEC.md` (shared with the web side). Nothing here was run on a device or the emulator:
JVM unit tests and a debug build only (see "Not tested").

## What the user gets

| Where | Before | Now |
|---|---|---|
| Song-list badge for a just-downloaded (Cloud) song | `Streaming` | `New download` |
| Info > Source, Now Playing source pill / one-line tags | `Streaming (a new download, played from your server)` / `Cloud` | `Streaming from new download` |
| Source filter chip in Songs | `Cloud (n)` | `New download (n)` |
| Under the artist: mini player (both styles), Home's Now Playing card, Now Playing (all layouts), Up next pane, Up Next screen, every song list (Songs, Albums, Artists, Genres, playlists, favourites, Recent, search results, top result, chart rows, Home shelf cards and Quick picks) | an old "Downloaded this week / Downloaded" chip in the Up next pane only (never expired) | **`Downloading · <stage>`** while a job runs, **`Recently downloaded · 12s ago`** for 60 minutes after it finished, **`Couldn't download`** after a failed fetch (within the same hour). Nothing otherwise. |
| Search result row (catalog) | `Searching Soulseek...` | `Downloading · Searching Soulseek...`; once the song is in the library the row carries the Recently downloaded tag |

Age text: `just now` under 5 s, `Ns ago` under a minute, `N min ago` under an hour; at 60 minutes the tag is gone (a week-old download never says "recently").
Wording is identical to the web: the strings live in `SourceLabel` and `DownloadTags.text`.

### Behaviour details

- **One lookup** (`DownloadTags.resolve`, pure): a running job > a finished or failed one inside the window (a failure only wins when it is the later of the two) > a Cloud song's own added time (`Track.mtime`) inside the window > nothing. The key is `matchKey(title, artist)` like the rest of the app. A finish time more than a minute in the future is not believed.
- **Sources of the data** (`DownloadTagStore`, owned by `Library.tags`): fed by every download status the coordinator sends (so also Autoplay's background fetches and Hi-Res downloads), seeded at start from the persisted `UserData.downloads` history (a restart inside the hour keeps the tag), and the Cloud track's `mtime` (persisted with the library). Albums tag every song they filed when they finish.
- **Live age, one ticker** (`TagClock`): a single coroutine ticking once a second, started by the first composed tag that shows an age and stopped by the last one leaving composition (reference counted in `TickerRefs`). Only the tag text composable recomposes; a row is not rebuilt. A tag schedules its own expiry once, which gives the row its line back.
- **Layout**: the tag is a third line (one line, ellipsis) under the artist. A tagged row is taller (56 -> 74 dp, 58 -> 74, 64 -> 78, 60 -> 76) so nothing overflows on the compact/RG Rotate screens; an untagged row is exactly as before. The tag is plain text: not focusable, no new d-pad / gamepad targets.
- **iPod canvas themes** (the faithful device bodies) are not changed: they draw their own fixed-height rows.

### Phone reports a finished download to FLACie Web

- `Library.onStatus` (DONE) -> `DownloadReport.itemsFor` -> `FlacieWebClient.reportDownloaded` -> `POST /api/downloaded`, auth exactly like `/api/played` (`X-Emby-Token`, `X-Jellyfin-Server`).
- Body: `{ artist, title, album, durationMs, path, source, finishedAtMs }`; `path` is the Track path exactly as the phone has it (`<file mover url>/file?path=...`) and is left out for a Lidarr grab (it arrives through Jellyfin); `source` is one of `soulseek|lidarr|ytdl|archive|audius|jamendo` (anything else is left out); `durationMs` left out when unknown. An album is reported song by song (at most 40 per download; the server takes 200 an hour).
- Only when a FLACie Web host is configured and the account is signed in (`FlacieWebClient.available`), on its own coroutine (IO): never blocks the download, the status or playback. One retry after 4 s for a network error or 5xx; 4xx (400, 401, 403, 404, 429) is final. Failures are silent; the only log line is `download report to FLACie Web: n of m accepted` (no address, token or title).

## Files

- New: `library/DownloadTags.kt` (`SourceLabel`, `DownloadTag`, `DownloadTags`, `DownloadTagStore`, `TickerRefs`), `library/DownloadReport.kt`, `player/DownloadTagUi.kt` (`TagClock`, `DownloadTagLine`, `isTagged`).
- Changed: `Library.kt` (store, report), `FlacieWebClient.kt` (`reportDownloaded`), `App.kt` (seed), `PlayerUi.kt` (badge wording, filter chips, `TrackRow`, queue screen, `TrackCard`, `WebRow`, top result), `InfoPanel.kt`, `NowPlayingModern.kt`, `NowPlayingUi.kt`, `UpNextDrawer.kt` (the old never-expiring `downloadNote` / `NoteChip` removed), `ForYouUi.kt` (Quick picks), `ExploreUi.kt` (chart rows).

## Tests (JVM)

RESULTS_PLACEHOLDER

## Not tested / not done

- No emulator or device run: the Compose layout (row heights, truncation on the RG Rotate's square screen, the ticking text, tag expiry while a row stays on screen) is reasoned, not seen. Please look at a freshly downloaded song in Songs, Up next and the mini player.
- Not against a real or mock FLACie Web: `POST /api/downloaded` is exercised only through the JVM fake sender (body shape, retry, refusals). The server side (path validation, 200/hour limit) is the other agent's.
- Songs the *server* downloaded (web search, web Autoplay) do not get the Recently downloaded tag on the phone: the phone would have to read `/api/downloads`; only phone-started downloads and Cloud songs are tagged. They show as ordinary library songs once Jellyfin lists them.
- iPod canvas themes (see above) show no tag.
- A "Couldn't download" tag can only appear on a song that is in the library or in a chart / catalog row (a failed autoplay suggestion is never queued); it lasts the same 60 minutes.
