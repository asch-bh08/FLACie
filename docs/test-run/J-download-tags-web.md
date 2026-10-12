# J: "New download" labels and download tags, web and Windows (beta 54)

Server and web UI part of `C:\FLACie-handoff\DOWNLOAD-TAGS-SPEC.md`. The phone part is in `J-download-tags-android.md`. The Windows app embeds the same web
server, so it gets everything here.

## What changed

### Wording (shared with the phone)

| Where | Old | New |
| --- | --- | --- |
| list badge (`SourceLabel.Short(Cloud)`) | Streaming | **New download** |
| source line in Now Playing, its pill and the Info panel (`SourceLabel.PlayingFrom(Cloud)`) | your server (new download) / Streaming | **Streaming from new download** |
| line above the full-screen player (new `SourceLabel.PlayingFromLine`) | Playing from your server (new download) | **Streaming from new download** (other sources: "Playing from NAS") |
| status tag | "Downloaded just now / today / this week", "Downloading", "Couldn't be downloaded" (queue only) | **Recently downloaded · 1s ago**, **Downloading · Searching Soulseek...**, **Couldn't download** |

The Now Playing source pill is now a quiet neutral pill like the format pills (it used to be a green pill). The tooltip text (`StreamingHint`) is unchanged.

### The tag

* `FLACie.Core/DownloadTags.cs`: the pure rule. `RecentWindowMs` = 60 minutes; `AgeLabel(nowMs, finishedMs)` returns null when the download finished more than
  an hour ago or lies in the future by more than a minute, "just now" under 5 s, "Ns ago" under a minute, "N min ago" under an hour. `Resolve` decides the status:
  a running download (its stage message) beats a finished one (newest finish, or for a "new download" song the time it was added), which beats a failure (a
  failure never hides a success). Exactly 60:00.000 still reads "60 min ago" (only "more than the window" is dropped).
* `DownloadManager.StatusOf(session, artist, title, track?)` / `StatusOfKey`: the one lookup. Songs are matched by `Matching.MatchKey(title, artist)`. State per
  user: the running song jobs, and the last finish / failure of each song within the window; it is filled from finished jobs, from songs that arrived with an album
  download, from the phone's reports and, once per session, from the last hour of the download log (so downloads from before a restart count and a record from two
  hours ago does not). Album jobs are never matched to a song of the same name. A cancelled fetch is not shown as a failure.
* `Components/Shared/DownloadTag.razor` (parameters Session, Artist, Title, optional Track, Class): used in the player bar card, Now Playing (header, the playing row,
  Up next, both Autoplay lists), `TrackList` (albums, playlists, favourites, genres, search results), Explore/Songs, Home (quick picks, mood picks, song shelves),
  Charts (songs already in the library), the playlist's "Not in your library" rows and the Search "More music" rows (a failed try still shows its reason there).
  It follows the downloads by itself and draws again only when what it says changes.
* `wwwroot/flacie.js` (`flacieDl`): once a second it moves the age on (from the age the server drew) and hides the tag when it reaches an hour, without the page being
  drawn again; the line is one fixed line (nowrap, tabular numbers), so ticking does not move anything. Style is the existing `.track-dl` status line (quiet green
  for done, accent while running, red for failed).
* Search "More music": a song that is already in the library (a download that just finished, or one the phone reported) is not offered again: its button reads
  "In your library" and is disabled.
* `DownloadManager.Succeeded` (the "fetched lately, do not fetch again" memory) was never filled before; it now is (finished jobs, album songs, phone reports).

### `POST /api/downloaded`

Same auth as `/api/played` (Jellyfin token in `X-Emby-Token`, `X-Jellyfin-Server` unless the server has a fixed Jellyfin).

Request JSON: `{ "artist", "title", "album"?, "durationMs"?, "path"?, "source"?, "finishedAtMs"? }`.

* 401: no / wrong token. 400: body not a JSON object, or artist / title missing, empty or not a string. 413: body over 16 KB. 429: more than 200 reports in an
  hour for this user (per user, in memory).
* 200 `{ "ok": true, "known": <bool> }`. `known` is true when the song is playable here now: `path` was accepted, or the library already has the song.
* `path` is used only when it is exactly the user's configured file mover address + `/file?path=` + a relative file path (no other host or endpoint, no extra
  query parameters, no raw `&` `#` `?` or spaces, no control characters, no absolute path, no drive letter, no `..`). An unaccepted path is ignored (the report
  still counts, `known` is false); nothing is ever fetched from it.
* `finishedAtMs` is clamped to [now - 10 min, now]; missing = now. `source` must be one of soulseek, lidarr, ytdl, archive, audius, jamendo, else it is dropped.
  artist / title / album are cut at 200 characters.
* Effects: a finished record in the download log (Kind "Phone", outcome done, Source = source, File = the relative file) so the Downloads page and the tag see it
  and it survives a restart; the song is marked as fetched (Autoplay / charts do not fetch it again); with an accepted path `AddDownloaded(track)` adds it to the
  session as a playable "New download" song; a library scan is requested (the existing once-a-minute rate-limited refresh). Nothing is logged except the log
  record; tokens and keys are never written.
* The Downloads page names Kind "Phone" as "Downloaded on your phone". No "Dad downloaded ..." notification is sent for phone reports (not asked for).

## Tests

* xunit `DownloadTagTests` (44 new): age label boundaries, window, future times, exact strings, status order, clamp, source labels, the lookup on a session
  (running beats finished, a failure only inside the hour, restart seeding, old records show nothing, a "new download" song counts from its AddedMs, a library song
  never does), the phone report (log record, playable, not fetched again), the 200 per hour limit, path validation table.
* Playwright `1i-download-tags.spec.ts` (runs before `2-login`) with a stand-in slskd + file mover on port 8197 and finished downloads of known ages written into the
  account's log by `global-setup.ts`: `/api/downloaded` 401 / 400 / 413 / path rules / clamp / source / limit; the owner's story ("P" by JME reported, found by Search,
  "New download" badge, tag, no Download offered); player bar, Now Playing header, pill, Info panel; the age ticking while paused on the same element and not moving
  the line; the shared label function and the hour expiry in the browser; a 20 minutes old download tagged and 2 hours / a week old ones not, in an album and in Up
  next; a real Hi-Res download of a library song driven through the dashboard settings and the mock slskd: "Downloading · Waiting for a peer..." in the list, Up next
  and the player card, then "Recently downloaded · just now".
* Changed existing tests: `1c-api.spec.ts` lists `POST /api/downloaded` among the calls that are 401 without a token. No strings asserted by existing specs or
  tests changed (none asserted the old labels).
