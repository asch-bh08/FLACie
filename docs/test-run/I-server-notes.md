# I-server: FLACie Web / Windows (C# side) of the beta 52 load work

Branch `beta52/server`, based on `release/beta52` (which already held the unverified `fix/server-load` WIP). Everything below was reviewed against
requirements 1-7 and 9 of the owner's list and built, run and tested here (mock Jellyfin only: no real Jellyfin, no real devices, nothing by ear).

## What changed

### 1. One gate for every request to Jellyfin (JellyfinGate)
- Already in the WIP and kept: AIMD limit (5 to start, 1 to 8), circuit breaker (open / half-open / closed), jittered back-off with `Retry-After`, at most 2 retries,
  coalescing of identical GETs, `GatedHandler` for audio and covers (only the wait for the response headers is gated).
- Fixed: the first shrink of the limit was skipped (`now - long.MinValue` overflowed); a request that had just finished could answer a later identical one
  (stale coalescing); a page of 500 songs with streams no longer counts as "Jellyfin is slow" (own yardstick, `slowMs`); a held background waiter no longer blocks a
  prefetch behind it; the cancellation callback of a granted waiter is disposed.
- New priority `Prefetch` (= background work that is allowed to run while a song streams, but is held while Jellyfin is slow, while the breaker is open or while a
  Playing/Visible request waits; one at a time). The web player's early read of the next song uses it.
- Every Jellyfin call of this server goes through `JellyfinClient.Send` (gated, GET retried and coalesced) or `JellyfinClient.Authorized` + `GatedHandler`
  (audio, covers, ALAC range reads). Checked by grep: PlayTracker, ImportManager, ChartsService, Lyrics, Connect (REST and sessions), Jam, Search, Home/PlayStats,
  DisplayPreferences (profile), `/Users/Me`, StorageService. The only direct sockets are Connect's WebSocket (one per user, now with a growing reconnect wait up to
  about 40 s instead of every 5 s) and the other-service HTTP clients (Deezer, LRCLIB, the download services: not Jellyfin).
- `/Users/Me`: cached by `TokenCache` (45 s), dropped on sign-out and now on ANY 401 Jellyfin gives for that token (`JellyfinClient.TokenRefused`).
- `GET /api/admin/load` now also returns the gate's metrics (queued, in flight, peak, limit, breaker, retries, coalesced, fast fails) and the cache report.

### 2. Priorities and cancellation
- Playing: `/stream` of the song, its Info read (`InfoService.GetAsync`), the full-size Now Playing cover. Visible: rows, tiles, covers, lyrics, profile.
  Background: library listing (also the FIRST sync: songs still show as pages arrive), delta sync, reconcile, format probe, charts, play counts, the
  15-minute check. Held while a stream is open or Jellyfin is slow.
- `/stream` and `/art` cancel their Jellyfin call when the browser connection is aborted (`RequestAborted`). Covers: the shared lookup (`SingleFlight`) is now
  reference counted: when the LAST tile that wanted a cover is gone the lookup is cancelled (still queued: never sent; in flight: stopped). Info reads of the Now
  Playing panel are cancelled when the song changes and wait 300 ms first if the facts are not known (a burst of skips no longer reads every song it passes).
- The breaker open: `/stream` answers 503 + `Retry-After`, `/art` answers 503 and does NOT remember the cover as "missing" (it used to, for 3 minutes).
- Device pickers / Devices page / remote Now Playing pollers share one `/Sessions` read per 1.5 s however many tabs; the dashboard asks Jellyfin at most every 6 s.

### 3. Bulk + delta
- First listing carries `MediaSources,MediaStreams` (already in the WIP): no per-song info call (the 1f spec and `SyncTests` check it).
- Delta: `MinDateLastSaved` is inclusive in Jellyfin, so the cursor is moved on by one tick and a song saved exactly at the cursor that the library already
  has is dropped client-side (works with an inclusive or a strict server). The newest saved date is compared as a time, not as text.
- The cursor AND the set of Jellyfin ids seen are persisted next to the library cache (`library/<id>.json`, `.cursor`, `.ids`): after a restart only a delta, a
  count and (only when the count differs) an ids-only listing are asked for; removed songs are found from the ids (no relist). The old WIP compared Jellyfin's
  count with the merged library, which differs whenever the library de-duplicates songs: every start would have been a full listing. A failing or slow delta
  keeps the saved library (no relist against a struggling server); a 400 from Jellyfin starts over with a full listing.
- Socket `LibraryChanged`/`UserDataChanged` -> delta after 8 s of quiet (already in the WIP). Fixed: after sign-out/in the new live connection was never
  listened to again. The 15-minute check now uses `ReconcileAsync` (count, then ids) instead of a full reload.
- The play counts for Quick picks are no longer read at every library load (two big requests): the Home page asks for them (1 h, Background).
- The format index writes itself a few seconds after a change (the Windows app's copy has no probe loop and used to lose every fact it was handed at the next start).

### 4. Fetch once
- Cover identity: a track whose item has `AlbumId` + `AlbumPrimaryImageTag` (and is not on a compilation) uses the ALBUM picture
  `jf<AlbumId>~<tag>`: the tracks of an album share one cached file and one request (coordinator addendum). Fallback: own tag, then catalog lookup, as before.
  Connect sessions and the dashboard use the same key rule (`JellyfinClient.ItemArtKey`): no more untagged `jf<id>` keys from them.
- Sizes: rows and tiles `maxWidth=240&quality=80&format=Webp`, Now Playing `s=full` (original). Nothing else exists in `FLACie.Server` or the Windows shell
  (grep of `maxWidth`, `/art/`). One browser URL per cover (`?v=3` everywhere: the dashboard and the media session used `?v=2` / none).
- Disk: `CoverStore` keyed by (item, tag, variant); ETag + Last-Modified (304), `immutable` for tagged keys; the library listing is kept on disk and shown at once.

### 5. No fixed cap, LRU only when the disk is low
- Covers: LRU eviction only when free space < max(2 GB, 10 % of the volume) (WIP), now also: a cover touched in the last 5 minutes is never evicted (the 25th
  save on a small disk used to delete the file it was about to serve: HTTP 500), thresholds configurable (`FLACIE_CACHE_MIN_FREE_MB`,
  `FLACIE_CACHE_MIN_FREE_PERCENT`), `CacheJanitor` checks every 10 minutes and also frees lyrics (oldest first) after the covers.
- Other caches: lyrics have no cap (a "no lyrics" answer is retried after 7 days: kept, it is one request per song per week), the format index has none (its
  per-song back-off 1 h/6 h/24 h/7 d is to stop re-asking), the library cache none. The in-memory ALAC layout cache cleared everything at 400 entries: now
  least recently used quarter at 1500. There is no ALAC/WAV file cache on the server (the WAV is decoded on the fly); that exists on Android only.
- Settings: a "Cache" line (`#cache-size`: covers + library + lyrics + format index, with the parts) and a **Clear cache** button (administrators; the cache is
  shared by everyone on the server and in the Windows app the user is the administrator) on the Settings page and in the dashboard's Storage section.
  Clear removes covers, lyrics and the saved library listings (not the format index: rebuilding it costs Jellyfin requests).

### 6. Streams
- Direct stream (`static=true`) always; the only transcode is Jellyfin's MP3 conversion of formats no browser plays (WMA, APE ...). **The web UI has no
  data-saver setting; none was invented** (Android has its own).
- Next-song preload: the page's second audio element (already there) now asks `/stream?p=...&pre=1`. The server treats it as `Prefetch` priority, not a play
  (no "music played", not counted as an open stream) and holds its Range to the first MiB (`bytes=0-1048575`), a 503 while the breaker is open. (A first version
  bound `pre` as bool and answered 400 to `pre=1`: found by the Playwright spec and fixed.)
- Abandoned streams: browser abort -> `RequestAborted` -> the Jellyfin call is cancelled (verified with the mock: `streamAborted`).

### 7. Hi-Res
- One rule (`MediaInfo.IsHiRes`), unchanged and covered by `HiResTests`. The bulk facts feed the same `FormatIndex` and the Info cache (`FactsSink`), so badge,
  Info panel and spectrogram agree; `FormatIndex.Set` keeps the badge in step with the Info panel's "codec OR container is lossless" for raw PCM variants.
  Unknown stays the plain extension / "FLAC" until real facts exist. `1e-hires.spec.ts` passes unchanged.

## Tests
- `dotnet test` (ipodsync/tests/FLACie.Tests): **168 passed, 0 failed** (128 before: +40 new in `GateTests`, `SyncTests`, `CacheTests`):
  gate: concurrency limit, priority order, 429 + Retry-After, max 2 retries, jitter and cap, non-server failures not retried, queued cancel, in-flight cancel
  (neutral for the limit), breaker open / fast fail / half-open / probe / close, queued requests failed at once, coalescing and its cancellation (one asker
  leaves / all leave), background held while streaming and while slow, prefetch rules, AIMD grow/shrink/floor, big-answer yardstick, ambient priority.
  sync: first sync (3 pages, facts, no per-song calls, files written), pages visible while the rest is on the way, restart = one delta (changed replaced, new
  added, removed dropped via ids, cursor persisted and moved on by one tick), warm start with nothing changed, duplicates do not force a relist, socket
  removals, reconcile, failing Jellyfin keeps the cache, cursor tick, album cover identity, remote-session art key.
  cache: no deletion with room, LRU only when low and only as much as needed, fresh covers protected, report and Clear, lyrics eviction, single-flight
  cancellation, prefetch Range, format index self-flush, badge/Info agreement for PCM.
- Playwright (`ipodsync/tests/web-smoke`, the whole suite, one worker): **118 passed, 0 failed** (7.2 min; the last full run was made before the final one-line
  `ToTrack` robustness change, which `dotnet test` covers). New `1g-burst.spec.ts` (6 tests): 40 skips with a
  bounded stream count and a Hi-Res badge that is never wrong and stable once settled; abandoned streams are cut on the Jellyfin side (mock `streamAborted`);
  the early read is a 1 MiB Range; Settings cache line + Clear cache; a cover is fetched once at 240 px WebP q80 (11 albums in the mock: at most 11 requests,
  none repeated) and a second browser costs Jellyfin 0 image requests; only Now Playing asks for the full picture.
- Existing specs adapted (nothing deleted or weakened): `1c-api.spec.ts` `/art/jfs1`: content type is "an image" (the type Jellyfin gave, a WebP in real life) and
  an ETag exists, instead of the fixed `image/jpeg` and `max-age=604800` of beta 51; `1f-load.spec.ts` counts what happens from the start of the test (the
  specs before it had already read their songs); `playwright.config.ts` sets the cache thresholds to "never evict" for the test server (a small test disk is
  always "low").
- Mock (`tests/web-smoke/mock-jellyfin.mjs`): items carry `AlbumPrimaryImageTag` and `DateLastSaved`, `MinDateLastSaved` (inclusive), `/__images`,
  `/__stream?delay=ms`, `/__touch?id=&name=`, counters `streamAborted`, `streamCapped`, `imageRepeats`, `imageFull`, `imageRow`.

## Measured here (FLACie Web published build against `tools/loadtest/mock.mjs`, 10,000 songs, 300 ms latency, one run each, not a benchmark)
- Cold first sync: 20 library pages (each 500 songs with their streams), 27 requests in all (2 `/Users/Me`, 4 other, 1 socket), peak 2 in flight, about 12 s
  until all 10,000 songs showed; 0 per-song info calls.
- Warm restart (same data folder): 10 requests (profile, `/Users/Me` x2, one delta, one count, the socket, a few small ones), 0 full listing pages; songs on
  screen from the saved listing before the delta answered. `/api/admin/load` showed gate limit 6, peak 2, no retries, breaker closed.

## Open issues / not done
- The Windows app's other pages that talk to Jellyfin through `IpodSync.Core`/`IpodSync.Shared` (the iPod-sync tabs) use their own, older client and are NOT
  behind this gate (they are user-driven, not background load).
- `Reload library` (Settings) is a delta + reconcile, not a forced full listing; an in-place metadata edit that Jellyfin does not date (no DateLastSaved change)
  is not seen until a restart with Clear cache.
- The player's own error handler (three retries, then skip) will walk through a queue while the breaker is open (each 503 is fast). Not changed.
- Real Jellyfin behaviour (inclusive MinDateLastSaved, WebSocket LibraryChanged, 429 headers, image tags) is from its documented behaviour and the mock; none of it
  was run against a real server.
