# Smart downloads: quickest copy first, Hi-Res later

Owner's brief (1 Nov 2026), in their words where it matters: when someone searches or downloads a song, fetch and play "the quickest possible
solution" (most of the time YouTube), "but if it's only a small difference in speed then choose Hi-Res". A song that arrived as anything less
than Hi-Res is upgraded in the background "no matter how long it takes", keeping the old file "until Hi-Res is verified and downloaded". Songs
fetched ahead (queue, Autoplay) that are "late in the queue" go straight for Hi-Res; skipping to a song that isn't downloaded yet takes the
quickest copy, then upgrades it. The player keeps showing the quality it already shows (Hi-Res, FLAC, MP3, AAC), and that changes when the
upgrade lands. No release or deploy until the owner says so.

## Decisions

- **The server does it.** FLACie Web runs every download, the race and the upgrades (it keeps going with the phone off, one set of rules, one
  log). The phone hands its downloads to FLACie Web.
- **The phone keeps its own download code as a backup**: used when no FLACie Web is set up, or when it can't reach it. A quiet switch in the
  phone's settings ("Download on this phone when FLACie Web can't": on by default) turns the phone's own downloads off.
- **Grace for Hi-Res: 7 seconds.** When the quick copy is ready, a Hi-Res copy wins if it is estimated to be playable within 7 s more (owner:
  YouTube 10 s and Hi-Res 15 s → Hi-Res; Hi-Res 20 s → YouTube now, upgrade later). The estimate is the Hi-Res transfer's remaining bytes at
  its measured speed plus the filing time, so a small Hi-Res file on a fast peer wins; it is never waited on longer than the 7 s.

## 1. The race (FLACie.Core `Downloads`, server)

One song, **quick mode** (a click on Download, Alexa, a skip to an undownloaded song, the next song in the queue):
1. Start at once and side by side: the Soulseek search (one search, ranked into Hi-Res candidates and normal candidates), YouTube where it is
   switched on, and the open sources' finders.
2. Download from every source that has a match: up to three normal Soulseek peers (PeerRace), up to two Hi-Res peers, YouTube.
3. The first copy that is filed and playable is held; if a Hi-Res transfer is under way whose estimate is within the grace, wait for it (at
   most the grace); otherwise the quick copy plays and the others are cancelled.
4. If the copy that plays is not Hi-Res, the song goes on the upgrade list.

**Hi-Res mode** (fetch ahead for a song at least 3 places down the queue, Autoplay's fetch ahead, imports and charts): Hi-Res candidates only,
no race; if none exists or none delivers within the normal PeerRace limits, fall back to quick mode. The explicit "Hi-Res download" button
keeps its meaning.

Labels: the job's source and the file's own format, as today ("YouTube", "Soulseek", FLAC / Hi-Res / MP3 / AAC chips from the file).

## 2. The upgrade list (server, new `Upgrades`)

- Saved in the data folder (`upgrades.json`): song (artist, title, album, length), the file it has now (path, format), when it was added,
  attempts, the next try. Added by the race (anything not Hi-Res), never twice for one song.
- One worker, one song at a time, while downloads are allowed and the music storage has room (the existing free-space rule).
- Order: songs in someone's queue first, then the most recently played, then the oldest; a song played again moves up.
- Each try: Soulseek Hi-Res candidates, no time limit on a peer that is sending (a peer that never starts still gives up, PeerRace). Nothing
  found: try again later (1 h, 6 h, 1 day, then weekly), for ever unless the song is gone from the library. For a YouTube or lossy copy with no
  Hi-Res anywhere after the first try, a lossless copy is taken (and that stays on the list for Hi-Res).
- **Verify before swapping**: the transfer completed with the full byte count, the file is filed, its length matches the old one within 3 s
  (the file mover's /probe), it is really Hi-Res (bit depth above 16 or sample rate above 48 kHz).
- **Swap**: the new file is filed next to the old (same "Artist - Title" name, its own extension); only then is the old file retired: moved by
  the file mover into `.flacie-replaced/` in the music folder (new `/retire` route: nothing is deleted; the host can empty it). Jellyfin is
  asked to rescan. FLACie playlists keep finding the song (they resolve by path, then title and artist).
- Never upgrades a file the user did not download through FLACie (only paths in the download log).

## 3. The phone

- `FlacieWebClient`: start a download (`POST /api/download` with artist, title, album, length, mode `quick` or `hires`) and follow it
  (`GET /api/download/{id}`: stage, message, source, done, the track's path and format). Progress and messages show as they do now.
- Used whenever FLACie Web is set up and answers. Unreachable or not set up → the phone's own DownloadCoordinator, unless the quiet switch is
  off (then "Downloads need FLACie Web").
- Fetch ahead on the phone asks for `hires` for songs 3 or more places ahead, `quick` otherwise.

## 4. Server API

- `POST /api/download` (signed in, downloads allowed): `{artist, title, album, durationMs, mode}` → `{id}`; the same rules as the web's Download
  button (who may, rate limits).
- `GET /api/download/{id}` → `{stage, message, source, done, failed, path, format}` (the person's own jobs only).

## Testing

- Pure rules with unit tests on both sides where they exist: the grace decision (estimate vs quick time), the mode for a queue position, the
  upgrade order, the retry schedule, verification (length and Hi-Res checks).
- Server: the race against fake sources (a fast YouTube and a slower Hi-Res: Hi-Res inside the grace wins, outside loses and is queued); the
  upgrade worker against a fake file mover (old file retired only after the new one verifies; a failed verify keeps the old file).
- Playwright: the web Download button still finishes (mock services), the job's label.
- Android: FlacieWebClient parsing, the fallback choice (server set up / unreachable / switch off).

## Not in this

The file mover's `/retire` route needs the file mover redeployed on frank (owner's go-ahead), and FLACie Web's deploy needs it too. Until
then the upgrade worker keeps its list but doesn't download (a file mover without `/retire` would leave two copies in the library).
