# I - Android: Jellyfin load reduction (beta 52 overnight run)

Branch `beta52/android`. Only `android/` and this file changed. Jellyfin is the only required source: the song list, the stream facts
(codec, bit depth, sample rate, bit rate) and the covers all come from Jellyfin; FLACie Web and the NAS stay optional.

## What changed

| Req | State | What |
|---|---|---|
| 1 gate | done | `JellyfinNet` (gate over one OkHttp client) now carries every Jellyfin HTTP request: listing/sync, covers, Quick Connect and sign-in, profile (DisplayPreferences) and playlist mirror in `AccountSync`, Connect (session calls, reports), Jam, lyrics (Jellyfin part), Quick Picks counts, "refresh Jellyfin" after a download. The player's stream opens take a gate permit (`withPermitBlocking`) around `DefaultHttpDataSource.open`; the prefetcher has its own data source at Background priority. Not through the gate: the live WebSocket (one long connection), the bytes of a stream after it is open, LRCLIB/iTunes/Lidarr/Plex/NAS/FLACie Web (other servers). `GET /Users/Me` was never called by the app (no hit in the code). Max 2 retries (GET only), jittered back-off, `Retry-After`, breaker, AIMD; Background waits while the screen is touched (`UserActivity`) or the average answer is slow. |
| 2 priorities / cancel | done | Playing song (cover of the current song, stream opens, Jam) = PLAYING; rows on screen = VISIBLE; sync, prefetch, taste data = BACKGROUND. Compose rows load covers in `LaunchedEffect` (cancelled with the composable, the gate drops the queued/in-flight request). The canvas themes (iPod views) cannot cancel: `ArtCache.get` now runs the request in a coroutine and drops it when later draws stopped asking for that key (`ScrollWatch`). Identical in-flight GETs are coalesced by the gate. |
| 3 bulk + delta | done | `JellyfinSync`: first pass pages 500 at a time with `Fields=...MediaSources,MediaStreams,DateLastSaved`; songs appear while pages arrive (no partial publish if an older list is already shown); then only `MinDateLastSaved` deltas (the inclusive newest item is not treated as a change); song count compared at most every 30 min or after a LibraryChanged message (one small request); on mismatch ids are compared, deleted songs dropped, missing ones fetched. Jellyfin's socket `LibraryChanged` removes `ItemsRemoved` at once and starts one debounced delta; `UserDataChanged` starts nothing (the list does not depend on it; our own plays would otherwise cause a request per song). Socket reconnects with 2/4/8..60 s jittered back-off and a catch-up delta after a gap. With the socket down: a delta every 15 min. Cursor, count time and library are persisted: a cold start shows the saved list at once and asks for one delta (plus the count only if 30 min passed). |
| 4 fetch once | done | Covers saved exactly as sent, keyed by item, with the image tag and ETag in `CoverIndex` (persisted). Requested again only when the listing's tag changed (then `If-None-Match`, 304 costs no bytes). 404 is remembered per tag (retried after a day or when a tag appears). One size per use: 240 px WebP q80 for rows/tiles; 960 px only for the Now Playing / Remote big cover (`ArtImage(full = true)`, row-size picture shows meanwhile). **Cover identity per album** (coordinator addendum): `/Items/<AlbumId>/Images/Primary?...&tag=<AlbumPrimaryImageTag>` for every song of an album; the song's own picture only on compilations ("Various Artists", VA, Compilations) or when the album has none. Grep: the only `maxWidth` is in `JellyfinCovers`. |
| 5 cache | done | No fixed cap: covers are deleted least-recently-used only when free space is under max(10 %, 1.5 GB), never what was used in the last 30 min. The audio replay cache cap is now a quarter of free space (768 MB - 6 GB) instead of a fixed 768 MB. Settings > Storage: "Cache" (covers + song list/facts/cursor + audio kept) and "Clear cache". Clear cache removes every cover + the CoverIndex + the audio kept for replay; it KEEPS the song list, facts, cursor, sign-in and playlists (re-reading the list would cost the server a full pass; covers return one by one for what is on screen). Rows are normal `IpodRow`s: wheel/d-pad focus works like the other settings. |
| 6 streams | done / partly | Songs stream with `static=true` (original file). Transcoding only for: Settings > Streaming quality = Data saver, or Automatic after two stalls in two minutes (existing owner feature, left as is), or formats the phone cannot decode. The next two songs are cached ahead by the existing `Prefetcher` (now Background priority, so not while the screen is touched or the server is slow). It reads whole songs, not just a start: kept, since ExoPlayer needs them anyway. A skip burst opens at most two streams (`SkipBurst`, extracted from `PlayerController` so it is testable); abandoned opens leave the gate queue and give the slot back (tests). |
| 7 Hi-Res | done | One rule (`QualityRule`) for badge, Info panel and spectrogram axis; Jellyfin's facts from the listing feed `AudioFacts`. Fixed: `buildAudioInfo` let a renderer mime type name the codec before the file's own facts (a stale decoder could label an MP3 as FLAC); the facts now decide. `pcm_*`, `dsd*`, `wmalossless`, `truehd` count as lossless. Unknown stays neutral (extension-based name, never Hi-Res). |
| 9 tests | done | see below |

Manifest: `usesCleartextTraffic="true"` already allows `http://10.0.2.2:<port>` and LAN Jellyfin; unchanged. No version files touched.

## Tests

`:app:testDebugUnitTest`: **193 tests, 2 skipped (as before), 0 failed** (was 129 at the start of this run: 107 old + 22 gate). New: 64.
`:app:assembleDebug`: builds. (No release build; the coordinator does that on the merged tree.)

New files: `JellyfinSyncTest` (11: progressive first pass, delta since cursor, nothing-new delta, count cadence, deleted dropped, missing fetched, `removed`, again-flag, failure, empty library), `CoverStoreTest` (16: same tag no fetch, changed tag one refetch, 304, Now Playing entries, no-picture memory, persistence, clear, urls/one size, fetcher If-None-Match, webp->jpeg fallback, eviction oldest first/only when low/recent protected/thresholds), `JellyfinListingTest` (15: real JSON shapes: FLAC 24/96, CD FLAC, missing BitDepth, MP3, ALAC with a video stream, top-level streams, no streams; listing url; album-shared covers, compilation, fallbacks), `JellyfinGatePermitTest` (5: cancelled open, queued opens of skipped songs, 40 opens never above the limit, interrupted player thread, reconnect delays), `SkipBurstTest` (5: 40 skips -> 2 opens (+1 at settle), badge from the song's own facts), `ScrollWatchTest`, `ServerChangeTest`, `QualityRuleTest`, `CacheLabelTest`. No existing test was changed or weakened.

## Open issues / decisions

- Prefetch reads whole next songs (existing behaviour), only gated at open; it is not a "start of the next song" read.
- Stream opens by ExoPlayer for the next playlist item run at PLAYING priority (the data source cannot tell current from next); the prefetcher is the Background one.
- A deletion and an addition that happen together between two count checks and leave the count equal are noticed only by the socket's `ItemsRemoved` or the next mismatch.
- `AudioFacts.of` still prefers FLACie Web's table when present, then Jellyfin's own facts.
- Plays/profile requests to FLACie Web (`PlaysReporter`, `FlacieWebClient`) are not Jellyfin requests and are not gated.
- The old size-capped (768 MB) audio cache is now free-space based, but SimpleCache still evicts by its own LRU beyond that.

## Only an emulator / device can verify

- `adb logcat -s FLACieGate` (10 s line: queued/inflight/peak/limit/breaker/total/kinds): peak <= limit+2, cover/list counts for first sync, a scroll, a 40-skip burst, cold vs warm start (expect 1 list request + 0-1 count).
- First sync against the 10,000-song mock: songs appear per page, scrolling stays smooth, touching the screen pauses pages.
- Real Jellyfin: that `AlbumPrimaryImageTag` and `MediaStreams` arrive with the listing as assumed, WebP output is accepted (else the JPEG fallback shows `format=jpg`), 304 on a second run, socket LibraryChanged after a scan.
- Hi-Res badge on real 24-bit/96 kHz and CD files while skipping; Now Playing cover sharpness (960 px); lock-screen art.
- Settings > Storage on the RG Rotate small screen with the d-pad; Clear cache then scrolling.
- By ear: nothing here was heard (gapless, EQ, headphone level unchanged and untested).
