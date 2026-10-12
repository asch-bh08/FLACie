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

* `FLACie.Core/DownloadTags.cs`: the pure rule. `RecentWindowMs` = 60 minutes; `AgeLabel(nowMs, finishedMs, compact)` returns null when the download finished
  an hour ago or longer (same as the phone) or lies in the future by more than a minute, "just now" under 5 s, "Ns ago" under a minute, "N min ago" (compact: "Nm ago")
  under an hour. `Resolve` decides the status:
  a running download (its stage message) beats a finished one (newest finish, or for a "new download" song the time it was added), which beats a failure (a
  failure never hides a success).
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

## Beta 54 verification follow-up (after the deploy)

1. **Doubled word.** The one rule, `DownloadTags.DownloadingText(msg)`: a message that already starts with "Downloading" is shown as it is, otherwise
   `"Downloading · " + msg`; no message gives "Downloading". Seen on screen: `Downloading 33%...` (not "Downloading · Downloading 33%...") in the player card and in
   the autoplay rows. Tested in xunit (9 cases, also that the compact form does not touch a running stage) and in the spec.
2. **Autoplay lists.** A suggestion that is being fetched is not in the library, so no list could hold it and nothing said it was coming (the same finding as on the
   phone). Now `PlayerState.AutoplayFetching` (suggestions of the playing song that have a running download and are not in the library, via
   `AutoplayPlanner.Fetching` / `DownloadManager.RunningSongs`) gives each a row of its own:
   * in the "Autoplay · up next after this" list (queue ran out), under the songs already there;
   * in the autoplay part of the queue ("Autoplay · similar to what you were playing"), at its end;
   * the row reads title, artist · album, `Downloading · <stage>` (live), duration; when it lands the row is replaced by the ordinary row, tagged
     `Recently downloaded · Ns ago` (ticking). Now Playing redraws only when the set of fetching songs changes.
   Spec: the stand-in catalog (`FLACIE_CATALOG_REDIRECT`, see below) suggests three songs; asserted: 3 placeholder rows, each `Downloading · ...`, then
   `Downloading · Waiting for a peer...`, then `Downloading 33%...`, then none left and each song's row has `Recently downloaded · ...`; then Next, the queue's autoplay
   part shows the earlier songs with their tags and the next suggestions as placeholder rows at its end, which also disappear when they land.
3. **Narrow places.** The text for a tag is the same pure function in `flacie.js` (`flacieDl.tagText(ms, compact)`) and in C# (`DownloadTagStatus.Text(now, compact)`,
   `DownloadTags.AgeLabel(.., compact)`), tested against the phone's strings: `Downloaded · just now`, `Downloaded · 12s ago`, `Downloaded · 4m ago`; the full form is
   `Recently downloaded · 4 min ago`. Compact is used in cards always, in the player bar up to 900 px window width, and in every other list up to 560 px.
   The markup is `dl-pre` (the words) + ` · ` + `dl-age`: when the column is too small the words get the ellipsis, the age never does (checked at 420 px: the age element
   is whole inside the column).
4. **"New download" older than an hour.** The list badge is `SourceLabel.Short(Cloud)` whatever the age, and the tag lookup returns nothing for a Cloud song added
   more than an hour ago (xunit: `A_new_download_song_older_than_an_hour_keeps_its_badge_and_has_an_empty_tag_line`). It cannot be aged inside a browser spec (a phone report
   is clamped to ten minutes back), so the DOM side is covered by the badge rendering from `Source` alone, not by waiting an hour.

Also: the Search "More music" rule (a song already playable is "In your library" and not offered again) is now driven in a spec through the stand-in catalog: a song is
offered, the phone reports it without the page being reloaded, the same row turns into a disabled "In your library" with the tag, and a fresh search lists it among the
library's songs with the "New download" badge.

**Test seam.** `FLACIE_CATALOG_REDIRECT=http://127.0.0.1:<port>` (loopback only, unset in production) makes the "catalog" HTTP client ask Deezer / iTunes /
MusicBrainz / ListenBrainz at `<address>/<their host><path>` (`CatalogRedirectHandler`). `playwright.config.ts` sets it to the stand-in in `1i-download-tags.spec.ts`
(port 8197, refused while that spec is not running), which also makes every spec independent of the internet.

## Also fixed on the way

* Now Playing showed the previous song's file facts (a Hi-Res badge, sampling, kbps) for a moment after a skip, before the new song's were read. The full-screen
  player now shows file facts only for the song they were read for. (The beta 52 burst spec caught it once the tags made renders a little slower.)
* The Playwright dashboard step has to wait for the page's live connection before typing in bound fields (an event sent before it opens is lost): the spec waits
  for the `_blazor` socket.

## Results (this branch)

* `dotnet test`: 250 passed (187 before + 63 new).
* Playwright, whole suite on a fresh server: 130 passed in total, including the 10 new ones in `1i-download-tags.spec.ts` (the handoff counted 119 before; the other 1 is whatever that count missed, nothing was skipped or removed).
  One earlier full run had the 320 px bottom-bar spec fail once (width 0) and passed when run again; not reproduced.

## Not tested / notes

* Search "More music" (an online catalog) was not driven in a spec: it needs the public catalog on the test machine. The rule behind it ("In your library" instead of
  Download for a song that is already playable) is a render-time lookup; the library result list is covered.
* Autoplay-fetched songs in the two Autoplay lists use the same component and lookup as Up next (covered); the lists themselves need the online catalog and were
  not forced in a spec.
* Phone widths: the tag is one nowrap line with an ellipsis; it is hidden in the short landscape player bar. Not checked on a real phone or on the RG Rotate.
* The Windows app was not built here; it embeds this server unchanged.
