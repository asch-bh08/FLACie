# J - Android download tags: emulator verification (beta 54, versionCode 69)

Verifier run on the real release APK (`1.0-beta54`, versionCode 69, built from `release/beta52` HEAD `ba8d568`), AVD `flacie_phone` (1080x2400, 420 dpi, userdebug), mock Jellyfin (300 songs, Quick Connect) and a stand-in download stack
(slskd + file mover + a FLACie Web that records `POST /api/downloaded`), all on this PC. Songs came from the app's real online catalog (the emulator had internet): the owner's case, "P" by JME, was used.
No app code was changed. Screenshots are kept locally in `C:\wt\verify\shots\` (not committed); the stand-in is `C:\wt\verify\standin.mjs` (not in the repo).

Setup: sign-in with Quick Connect; the services (slskd, file mover, FLACie Web at `.../web`) came to the phone through the Jellyfin DisplayPreferences profile, exactly as for a real install. Autoplay and "Fetch ahead" were on (defaults).
The stand-in could hold a transfer at 0 % or at a chosen percentage, so the running stage could be looked at.

## Verdict per item

| # | What | Result |
|---|---|---|
| 1 | Tag while a download runs | PARTLY. Shown, but the wording is doubled ("Downloading · Downloading 55%...") and a song that is not in the library cannot show it in the player/queue (see below) |
| 2 | After it finished: "Recently downloaded · Ns ago", "New download", "Streaming from new download" | PASS for the tag and the source line; the list badge only shows on cards, never in a phone's song rows |
| 3 | Autoplay part of the queue | PARTLY. Fetched songs carry the tag once they have landed; songs still being fetched are not in the queue and show nothing anywhere |
| 4 | Older than 60 minutes shows nothing | PASS (device clock set 61 minutes ahead; tags and the extra row height disappear). Tags came back after the clock was restored and the app restarted |
| 5 | Report to FLACie Web | PASS: 12 downloads, 12 POSTs, none duplicated |
| 6 | Colours | Differ from the web, see below (this is the "blue") |
| 7 | `adb logcat -b crash -d` | PASS: empty (0 bytes); no ANR of com.ipodemu |

## 1. While a download runs

Strings seen (exact):

- Search result row of a catalog song (`05-dl-search-1.png`, `05-dl-search-5.png`, `06-dl-search-40pct.png`): `Downloading · Searching Soulseek...`, `Downloading · Waiting for a peer...`, `Downloading · Downloading 40%...`, with `40%` at the right. The row's second line, which normally says "JME · Famous?", is replaced, so artist and album are hidden while it runs. It is grey (#A9A6B3), not the accent colour.
- A song that is in the library and is downloaded again (Hi-Res copy of "P", `19-hires-running-explore.png`, `20-hires-nowplaying.png`, `21-hires-upnext.png`): the tag is under the artist in the Songs list row, the mini player, Now Playing and the Up next row: `Downloading · Waiting for a peer...`, then `Downloading · Downloading 55%...`, in #FF4D73 (the app's accent).

Problems:

- **Doubled word**: "Downloading · Downloading 55%..." / "Downloading · Downloading 40%...". The coordinator's own message for the transfer already starts with "Downloading", and the tag prefixes it again (`DownloadTags.text`).
- A song that is only a catalog result (the owner's "P" before it landed) has no library entry, so it is not in the mini player, Now Playing or Up next while it downloads. Only the search row (and chart rows) can show it. The tag in the player card exists only for a library song that is downloaded again.
- In the Up next pane the tag truncates because the duration and "..." button take the width: `Downloading · Downloading 55…`.

## 2. After it finished

- Search: Top result card says `Recently downloaded · just now` (`07-finished-search-2.png`), and the card's badge says `New download`. The same song's row under Songs: `Recently downloaded · 23s ago`, button "Play" (`08-playing-from-search.png`).
- The age ticks on its own: seen 23s, 36s, 52s, 1 min ago (`09-nowplaying-a.png`, `10-upnext-a.png`, `11-upnext-b.png`). The row does not jump.
- Mini player, Now Playing, Up next row, Explore > Songs rows, Home "Listen again" cards and Quick picks all carry the tag.
- Now Playing pill: `Streaming from new download`; Info > File > Source: `Streaming from new download` (`14-info-source.png`). No "Streaming" or "(a new download...)" anywhere any more.
- **List badge**: `New download` appears in the Top result card and on Home cards (`16-search-after.png`, `28-after-restart-clock-restored.png`). It does **not** appear in song rows (Explore > Songs, Up next, search rows) on a phone: `SourceBadge` is hidden below 520 dp width (`PlayerUi.kt` line 362 `if (!narrow)`), so on the owner's phone the badge is never next to a song in a list.- Home "Listen again" cards are too narrow: the tag reads `Recently downloaded · …` and the age is never visible (`28-after-restart-clock-restored.png`).

## 3. Autoplay

Seed "P": the app asked the catalog for similar songs and fetched five (JME 96 F**kries, Skepta London, Wiley Heatwave, Skream & Benga Gameshow, Giggs Lock Doh). They joined the end of Up next after they landed, each with `Recently downloaded · 41s ago` etc., ticking (`11-upnext-c.png`, `12-upnext-d.png`). The songs the app took from the library (Song 30, Song 129 ...) correctly have no tag.

With the stand-in holding the transfers (seed "Heatwave" by Wiley, `24-autoplay-inflight-a/b/c.png`): four suggestions were being fetched (Boasty, Fester Skank, Photo Finish, Top Of My Spine, confirmed in the stand-in's slskd queue). **None of them is in the queue and nothing in the app says they are coming**; there is no "Downloading" tag for an autoplay song at all. After the hold was released they appeared at the end of the queue, tagged `Recently downloaded · 13s ago` (`25-autoplay-landed.png`). So "every suggestion that is being fetched ... shows the tag" is not true on Android: only landed ones do. By design (the note in `J-download-tags-android.md` says a failed suggestion is never queued), but it is why the owner sees no tag on autoplay songs: the song only appears once it is finished.

Also: there is no "Autoplay · similar to what you were playing" divider in the Up next pane; downloaded suggestions come after a handful of random library songs.

## 4. 60 minutes

`adb root`, `settings put global auto_time 0`, `date 101217362026.00` (61 minutes ahead; the songs were 1 to 7 minutes old). Within 5 seconds all tags in Explore > Songs and the mini player were gone and the rows were back to their normal height (`26-explore-before-clock.png` vs `27-explore-clock-plus61.png`). With the clock restored and the app force-stopped and started again, the tags were back with the right ages (`28-after-restart-clock-restored.png`): the history survives a restart inside the hour.

## 5. The report to FLACie Web

12 downloads in this run (1 catalog download, 1 Hi-Res re-download, 10 autoplay fetches). The stand-in's `/web/api/downloaded` log has 12 POSTs (`standin.log`), the phone's log has 12 lines `download report to FLACie Web: 1 of 1 accepted`. No song was reported twice. Every request had `Content-Type: application/json`, the Jellyfin token in `X-Emby-Token` and `X-Jellyfin-Server` (token not recorded). Keys of every body: `album, artist, durationMs, finishedAtMs, path, source, title` and nothing else. The first one:

`{"artist":"JME","title":"P","album":"Famous?","finishedAtMs":1791818934519,"durationMs":220000,"path":"<file mover url>/file?path=JME%2FJME+-+P.flac","source":"soulseek"}`

It arrived 1.3 s after `finishedAtMs`. The Hi-Res copy was reported with its own path (`...JME+-+P+%5BHi-Res%5D.flac`). Every `source` was `soulseek` (the only source the stand-in served).

## 6. Colours (what is on screen)

| Element | Android | Web |
|---|---|---|
| "Recently downloaded · ..." text | #4EE0A1 mint green | `--ok` #6fd6a3 green |
| "Downloading · ..." text (library song) | #FF4D73 = the app accent. Becomes **blue** when the user's Settings > Accent colour is Blue (seen: `36-blue-accent-nowplaying.png`) or when the iPod skins are used (their accent is iPod blue #2F7BE8) | `.track-dl` accent #ff4d73 (pink), not user dependent |
| "Downloading · ..." in a search row | grey #A9A6B3 | accent |
| "New download" badge | #B27CE0 lilac/purple text on a 16 % tint of itself | green: `.badge.cloud` #14261c background, `--ok` text |
| Now Playing source pill "Streaming from new download" | accent #FF4D73 on an accent tint (blue with a Blue accent) | quiet grey pill (`--surface-2` / `--dim`) |

So the owner's "blue" is the accent-coloured pill and "Downloading" tag on a phone with a blue accent or skin; the badge is always lilac, never the web's green. The one element that matches the web is the green "Recently downloaded" text.

## 7. Small screen

`am start ... --es fake 720x720 --es fakedp 480` (the RG Rotate shape): no overflow. Mini player (three lines: title, artist, tag) fits, Now Playing fits (title wraps to two lines, tag and pill below it, `33-rgrotate-nowplaying.png`), Explore row fits (`32-rgrotate-explore.png`). `fakedp 240` is a 240 dp wide screen, not the RG Rotate, and is not usable (the content area is a few rows high) so it was not judged. On the 411 dp phone the Up next row truncates the age (`Recently downloaded · 4 min a…`, `24-autoplay-inflight-a.png`).

## Not tested

- "Couldn't download" (failed tag): not seen on a device. Its only route on a library song is the Hi-Res menu, which was missing from the track sheet after the app was restarted (cause not investigated), so only the JVM tests cover it.
- Real Soulseek, Lidarr, the open sources, ytdl: the stand-in only served Soulseek.
- Landscape phone, tablet, Fold layouts, iPod canvas themes (no tag by design).
- Nothing about audio was judged.

## Suggested fixes (not done, no code changed)

1. Drop the doubled word: show the stage as is when it already starts with "Downloading".
2. Make the lilac "New download" badge and the Now Playing pill use the web's colours (green badge, quiet grey pill) and keep the green "Recently downloaded"; keep "Downloading" on the accent.
3. Show the badge (or at least the tag) in phone-width song rows, or accept that the tag is the only marker there.
4. Autoplay: put a "Downloading..." placeholder row for a suggestion that is being fetched, or list them in the queue as they are requested.
5. Home cards: let the tag wrap or show only the age.

---

# Round 2 (APK built from `release/beta52` HEAD `a301480`, versionCode 69)

Same method as round 1: fresh emulator (AVD `flacie_phone`, 1080x2400 at 420 dpi), the release APK installed, mock Jellyfin, the same stand-in slskd / file mover / FLACie Web, the same case ("P" by JME, then autoplay songs from the real online catalog). The stand-in got one extra switch, "make these searches find nothing", to force a failed fetch. No app code was changed. Screenshots: `C:\wt\verify\r2shots\` (not committed).

## Round 2 result

| # | What | Result |
|---|---|---|
| 1 | No doubled "Downloading" | PASS |
| 2 | Autoplay rows for songs being fetched (Up next pane and Up Next screen), then normal rows, no duplicate; failure then disappears | PASS, with two observations (below) |
| 3 | Colours; none follows the accent, with Pink AND Blue accent | PASS |
| 4 | Narrow places: Home cards, top result, Up next pane on the 411 dp phone | PASS |
| 5 | Cloud song older than 60 min shows "New download" on a phone row | PASS |
| 6 | Round 1 PASS items | PASS (report POST, 60 minute expiry, crash buffer, 720x720 at 480 dp) |

### 1. No doubled word - PASS
- Search row of the catalog song while it runs: `Downloading · Waiting for a peer...` (`03-dl-waiting.png`), then `Downloading 40%...` with `40%` at the right (`04-dl-40pct.png`). Round 1 said "Downloading · Downloading 40%...".
- Autoplay rows: `Downloading 40%...` (`08-upnext-pending-40pct.png`), `Downloading 60%...` (`30-rg-queue-screen-60pct.png`).

### 2. Autoplay rows - PASS
Up next pane, phone (`07-upnext-pending-a.png`, `-b`, `-c`, `08-upnext-pending-40pct.png`):
- With the playing song "P", the divider `Autoplay · up next after this` sits above the first queued Autoplay song (library songs "Song 4", "Song 191", ...).
- Below the library songs, the fetched-ahead songs are display-only rows (no "..." button): `96 F**kries / JME · Integrity> / Downloading`, then London, Heatwave (feat. Ms. D), Gameshow, Lock Doh. With the transfers held at 40 %: `Downloading 40%...` on each.
- After the transfers finished (`09-switch-1.png`): the rows turn into queue rows with the "..." button and `Downloaded · just now`, then `Downloaded · 13s ago` (`09-switch-end.png`). The same five songs, no duplicate row at the switch, none missing.
- Up Next screen (720x720 at 480 dp, where "Up next" opens the screen; `29-rg-queue-screen-a.png`, `-b`, `30-rg-queue-screen-end.png`, `30-rg-queue-screen-60pct.png`, `31-rg-switch-1.png`, `33-rg-final-bottom.png`): `Autoplay · up next after this` divider above Song 6; below the library songs the fetched-ahead rows `Hide & Seek / Stormzy · Hide & Seek / Downloading 60%...`, `24 Hours`, `Big For Your Boots`, `Wiley Flow`; on landing `Downloaded · just now` then `Downloaded · 1m ago`; final list has each song once (Hide & Seek, Wiley Flow, 24 Hours, Vossi Bop and Big For Your Boots).
- Failure: with the stand-in finding nothing for two of the suggestions, they showed `Couldn't download` (Boasty, Fester Skank; `10-fail-c.png`) in reddish text, next to rows that were still `Downloading`. They stay while within the hour and were gone with the clock at +61 minutes (`20-upnext-clock-plus61.png`: Boasty and Fester Skank no longer listed).

Observations (not failures of the listed expectations):
- On the 411 dp phone the pane shows only `Downloading` for a pending row while it waits ("Waiting for a peer...", "Searching Soulseek..." are not shown); the stage text appears only once it starts with "Downloading" (`Downloading 40%...`). Wider places show the stage (`Downloading · Waiting for a peer...` in the search row).
- The divider needs the app to have added the song in the same run. After the app was restarted (force-stop, start) the restored queue shows the downloaded songs with their tag but no divider (`23-rg-queue-a.png`, `23-rg-queue-c.png`).

### 3. Colours - PASS (pixel values read from the screenshots)
Checked with Settings > Accent colour Default (pink #FF4D73) and Blue (#4D9BFF).

| Element | Pink accent | Blue accent | Web |
|---|---|---|---|
| "Recently downloaded · 3 min ago" / "Downloaded · 4m ago" | #4EE0A1 green | #4EE0A1 | green |
| "Downloading ..." (pending row, search row) | #A9A6B3 grey, secondary text | #A9A6B3 | dim |
| "New download" tag on a song row (older than an hour) | grey, secondary | grey | - |
| "Couldn't download" | #FF9C9C reddish | #FF9C9C | danger red |
| Pill "Streaming from new download" | grey text #A9A6B3 on a grey tint (`13-np-pink.png`) | same (`16-np-blue.png`) | quiet grey pill |
| "New download" badge (Home cards, top result) | #6FD6A3 green on a green tint (`14-home-pink.png`) | #6FD6A3 (`15-home-blue.png`) | `--ok` #6fd6a3 on #14261c |

The accent itself is visibly blue in the same screens ("Up next" chip #4D9BFF, play ring, selected tab) and none of the tags moved with it. Round 1's lilac badge and accent-coloured pill are gone.

### 4. Narrow places - PASS
- Home "Listen again" cards: `Downloaded · 4m ago`, `Downloaded · 5m ago` in full, and the green `New download` badge under it (`14-home-pink.png`).
- Top result card: `Downloaded · 25s ago` and the `New download` badge (`06-playing.png`); the Songs row under it: `Recently downloaded · 25s ago`.
- Up next pane on the 411 dp phone: `Downloaded · 39s ago` (playing row, `07-upnext-pending-a.png`), `Downloaded · 13s ago` (`09-switch-end.png`); no cut age (round 1 had "4 min a...").
- Quick picks and mini player keep the full wording `Recently downloaded · 4 min ago`; the mini player and the 480 dp screen fit on one line.

### 5. Cloud song older than 60 minutes - PASS
Clock set 61 minutes ahead. Explore > Songs (`19-explore-clock-plus61.png`) shows each downloaded song's row with `New download` in the tag line (grey), same row height as a tagged row; the mini player says `New download`; the green "Recently downloaded" tags are gone (compare `18-explore-before-clock.png`). Up next at +61 min (`20-upnext-clock-plus61.png`) shows the mini player's `Streaming from new download · WAV ...` with no age tag.

### 6. Previous PASS items
- Report to FLACie Web: 12 downloads in this run, 12 POSTs to `/web/api/downloaded` in the stand-in log, 12 `download report to FLACie Web: 1 of 1 accepted` lines; the six POSTs read since the last reset were six different songs, body keys `album, artist, durationMs, finishedAtMs, path, source, title` only. The first one, "P" by JME, arrived within a second of its finish.
- 60 minute expiry: PASS (as above, `19`/`20`).
- `adb logcat -b crash -d`: 0 bytes. No ANR of com.ipodemu.
- 720x720 at 480 dp: no overflow in the mini player, Now Playing (`22-rg-np.png`: title two lines, `Recently downloaded · 6 min ago` and the grey pill below), Home, search and the Up Next screen (`29`-`33`).

## Round 2 not tested
- Landscape phone, tablet and Fold; iPod canvas themes.
- Real Soulseek / Lidarr / open sources (stand-in served Soulseek only).
- The failure tag was produced by an empty search (stand-in), and the clock jump at +61 min also made three still-held downloads give up at once, so their `Couldn't download` at +61 min (`20-upnext-clock-plus61.png`) is an artefact of the clock change, not evidence of the 60 minute rule for failures; the rule is shown by Boasty and Fester Skank disappearing.
