# Test run 2026-10-11, workstream A-web

Baseline: beta 49, the live web app at the public web address, signed in as the owner (admin). Driven through the built-in browser pane (DOM/JS plus screenshots).
Screenshots are in `C:\FLACie_run\A\` (not committed). Audio cannot be heard by the agent: audio items say "needs ear check".
The pane is shared and was hidden/resized by other agents at times, so some screenshots are cropped; results below were read from the DOM.

Test data: playlists TEST-A-alpha (renamed TEST-A-renamed) and TEST-A-import were created and deleted. One download: "Patient Zero (Piano Version)" by Taylor Swift, via Charts, saved by the file mover as `music/Taylor Swift/Taylor Swift - Patient Zero.flac` (27.3 MB, left on the NAS, nothing deleted). Many "Fetched ahead for Autoplay" downloads were also triggered by playing/skipping (see BUG 2). Settings flipped and restored: Settings toggles (all), notification chip "Server started" (off, saved, on, saved). One ntfy test sent.

## Results

| Item | Result | Note / evidence |
|---|---|---|
| Home loads, sections, mood chips, nav, back stack (home > search > artist > album, back x2) | pass | back returned to /artist then /search?q= |
| Pull to refresh | not run | desktop browser, n/a |
| Orientation / split-screen | not run | n/a on web; resized to 375x812, 768x1024, 320x568 instead |
| Dark / light | pass (dark only) | app is dark only (`color-scheme: dark`, no prefers-color-scheme rules); light OS scheme leaves it dark |
| Responsive 375x812, 768x1024, 320x568 (max zoom stand-in) | pass with BUG 4 | no page-level horizontal overflow; 09-mobile-375-home-bottom-nav-overlap.jpg |
| Artists / albums / songs / genres lists | pass | alphabet jump, counts match |
| Folders | not run | no folder browser exists on the web |
| Search (normal, typo suggestion, emoji, unicode, script tag, SQL-ish, 400 chars) | pass with BUG 6 | no errors, escaped output |
| Explore: overview, quality filter (Any/Lossless/Lossy/Hi-Res), tabs | pass | filter chips show "Filters (1 active)" |
| Charts: Top Songs + 7 genres, counts, Download/Play | pass | 05-charts-top-songs.jpg |
| Play / pause / seek / next / previous | pass | seek to 100 s and 158 s, Prev restarts, song ends and advances (needs ear check for audio) |
| **Skip 20 songs quickly (beta 49 freeze fix)** | pass | 20 clicks at 100-120 ms, twice (once with Info tab open): page stayed alive, no JS errors in console, title advanced each time. See OBS 1 (stream start delayed ~20 s after the burst while Jellyfin was slow) |
| Shuffle on/off, repeat off/all/one | pass | aria-labels cycle correctly |
| Queue: play next, add to queue | pass | toast shown. Reorder / remove inside the queue: not present on web (only tap to jump); sleep timer: not present on web |
| Full-screen player and its menu (play next, add to queue, add to playlist, artist, album, download, song info) | pass | 01-fullscreen-player.jpg; menu is see-through over the cover (07-more-menu-translucent-over-cover.jpg, cosmetic) |
| Lyrics | pass | plain lyrics appear after ~8 s lookup |
| Info panel: chips, spectrum/visualizer/spectrogram/stereo, real metadata, Came from, Playback row, hi-res tag | pass | 03-info-recovered.jpg, 04-info-spectrum-streaming-song.jpg. Hi-Res 24/44.1 FLAC, MP3 320, Streaming song all correct |
| Covers: missing | pass | placeholder note shown (404 from /art, many red console 404s, OBS 2) |
| Covers: filename-style titles | fail | BUG 3 |
| "Streaming" songs before Jellyfin scan | pass | label shown, plays, Info says "Came from Soulseek ... streaming until next scan" |
| Playlists: create, add (toast + Undo), duplicate refused ("already in"), reorder (Move up/down), remove, rename, persist after reload, delete (confirm dialog) | pass | remove has no undo toast (OBS 3) |
| Playlist import (text file, 3 of 3 added, "Open" link) | pass | |
| iPod playlist sync | not run | not present in the web app |
| Downloads: request from Charts, per-song status, Downloads page, per-job log, Failed/Retry list | pass with BUG 1 and BUG 2 | Soulseek job took 4 s |
| Notifications settings (Dashboard): master + 8 chips incl. "Someone plays music", "Someone downloads music", "Also plays through Jellyfin"; Save persists after reload; Send a test ("Test sent") | pass | all toggles restored |
| Notifications actually arriving on the phone | not run | cannot see the phone; Last: "Sent · just now" |
| Settings page: Autoplay, Fetch ahead, Cover colours, Compact, Badges, Animations toggles | pass | classes pref-noartbg / pref-compact / pref-nobadges / pref-calm applied, all restored |
| Account page, Dashboard (stat tiles drill down, devices, activity), Download log link | pass | |
| Headphones page UI (tabs, test-level, left ear play/stop) | pass (UI) | audio: needs ear check. Sticky level bar overlaps the tab chips when scrolled (cosmetic) |
| Mini player / bar: play, next, heart, mute, PiP button, close | pass | remote "Listening on ..." bar appears for other devices |
| Sign-out / sign-in page | not run | signing out needs Quick Connect by the owner; login page fetched without cookie shows Quick Connect + password form |
| Expired session behaviour | not run | cannot expire the session without signing out |
| Unicode / emoji / long titles | pass | emoji + accents in playlist name, search; long query fine |
| Jams / Connect between devices | deferred | |
| Console errors / failed requests | pass with notes | only 404s from /art covers (placeholder fallback); no JS exceptions |

## Bugs

**BUG 1 (medium): requesting "X (Piano Version)" can download the original song, and the Charts/Search row then reports the request as done.**
Repro: Explore > Charts > Top Songs, press Download on row 1 "Patient Zero (Piano Version)" by Taylor Swift (not in library, the original "Patient Zero" 3:45 is).
Expected: the Piano Version, or "not found". Actual: job "ready to play" in 4 s, log says `Saved as music/Taylor Swift/Taylor Swift - Patient Zero.flac`, the new "Streaming" entry has length 3:45, the same as the original in the library (looks like a duplicate of the original named as the piano version). Evidence: Downloads page job log, Info panel Location.
Suspect: version/variant check in the Soulseek matcher (`ipodsync/src/FLACie.Core/Downloads.cs`, candidate scoring around the `hit.Filename` handling) accepting a file whose title lacks the "(Piano Version)" qualifier. Not confirmed in code.

**BUG 2 (medium): Autoplay fetch-ahead downloads the same song again and again.**
Downloads log (Soulseek list) shows "All The Stars (From "Black Panther: The Album") - Kendrick Lamar" fetched three times (10:59, 11:18, 11:33), and from the earlier YouTube list "Ainsi Bas La Vida (Hoodtrap)" and "Jane! (Hoodtrap)" twice in one evening. Repro: play with Autoplay + Fetch ahead on and skip through the queue.
Cause (code): `AutoplayPlanner.Prefetch` only filters `RecentlyFailed`; `DownloadManager.FetchAsync` only de-duplicates songs currently in flight (`j.InFlight`), not songs already downloaded and still "Streaming" (not yet in Jellyfin) or whose catalogue title differs from the library title. Needs a "recently succeeded" check in Prefetch.

**BUG 3 (low/medium): songs with no tags (title is the filename) have empty artist and never get a cover.**
Repro: search "Everything Is Sound", play "06 - Jason Mraz - Everything Is Sound" (/Jason Mraz/Singles/..., MP3).
Actual: artist empty (Info: Artist blank), list row has empty subtitle, play button aria-label ends in "by ", the browser tab title is "06 - Jason Mraz - Everything Is Sound ·" with a trailing dot, no cover is looked up (placeholder only; the /art online lookup needs artist+title). Expected: parse "NN - Artist - Title" (or use the folder name) for display and the cover lookup, and no dangling "·". Suspect: `Art.KeyFor` / page title composition in `Components/Shared/NowPlaying.razor`, TrackList row.

**BUG 4 (low): mobile bottom navigation labels collide for admins.**
Repro: width 375 (and worse at 320), signed in as admin: bottom bar shows 8 items at 47 px each; "Downloads", "Dashboard", "Headphone test" overlap/clip ("DownloadsDashboardadphone te"). 09-mobile-375-home-bottom-nav-overlap.jpg. Suspect: `.rail` CSS in `wwwroot/app.css` (no overflow scroll / ellipsis at 8 items).

**BUG 5 (low): a long burst of skips makes the next song start ~20 s late and leaves Info "Reading the file…" and a black cover meanwhile** (02-info-stuck-reading-after-skip-burst.jpg). Page stayed alive and recovered, so the freeze fix holds; the delay coincided with Jellyfin answering progress/Sessions calls in 5-20 s (frank log). Likely each skipped song still opens a /stream and play report (dashboard Activity shows "Started X in the web player" for all 20 skipped songs). Consider not reporting/streaming a song that is skipped within a second.

**BUG 6 (low): symbol-only searches match every playlist.** Searching "🎵🔥", "%" or "\\" lists all 9 playlists under "Playlists" (the normalised key is empty and matches everything). Also the typo hint for "scream and shout will i am" offers "Did you mean scream and shout william i am?", which is worse than the query.

## Observations (not filed as bugs)

1. Server log on frank (read only) shows the server polling Jellyfin `/Sessions` regularly and getting 401 responses with sub-ms latency (one stream of requests with no/invalid token, probably another workstream or the watcher). Worth a look by workstream E.
2. Every missing cover is a red console 404 (dozens per page); consider 204 or a tiny placeholder so the console stays clean.
3. Removing a song from a playlist has no confirmation or undo, unlike adding.
4. Favourites lists "Meet Me Halfway" twice (rows 2 and 5, identical) - may be two library copies, not verified. Adding either to a playlist says "already in".
5. Unauthenticated login page HTML embeds the Jellyfin server address (private tailnet IP) in its script.
6. "Play next" puts the song under the "Autoplay - similar to what you were playing" divider; the queue does not separate user-queued from autoplay songs.
7. "1 Accounts on" tile while 5 devices were connected (same account), correct per its own description.

## Not run / left undone

Sign-out/sign-in, expired session, Jams/Connect (deferred), Pull to refresh, folders and sleep timer and queue reorder/remove (not present on web), notification arrival on the phone, gapless and all audio (needs ear check), non-admin account view (only the admin session was available), huge playlist (10,036 songs) scrolling, slow-server and server-unreachable behaviour, real light mode (none exists).
