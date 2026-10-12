# K: cold start and covers on the Z Fold (beta 56), 2026-10-12

Device: Samsung Z Fold (SM-F966B, Android 16), wireless adb, the owner's real Jellyfin library (10,325 songs, read-only: nothing was changed on the server).
Build under test: release APK 1.0-beta56 (versionCode 71), signed with the owner's key (SHA-256 `6a4eb335…a106`). "Before" = beta 55 plus only the timing probes (behaviour identical to beta 55).
Probes: `adb logcat -s FLACieBoot FLACieProbe FLACieCover` (added in this beta; times are ms since process start). Scripts: `ipodsync/tools/loadtest/run-android.mjs` scenario `covers` (emulator), the Fold runs were `am force-stop` + `am start` loops (5 runs each).
`am start -W` (first frame of an empty shell) was 160-176 ms before and is not the number that matters: the owner sees songs only when Home's shelves are drawn.

## 1. Cold start (cover cache and library cache warm, 5 runs each)
| Run | Before: Home shelves drawn | Before: library loaded | After: Home shelves drawn | After: first frame | After: library loaded |
|---|---:|---:|---:|---:|---:|
| 1 | 895 ms | 1141 | 70 ms | 99 | 642 |
| 2 | 857 | 1128 | 73 | 104 | 625 |
| 3 | 1074 | 1444 | 64 | 92 | 583 |
| 4 | 1060 | 1408 | 66 | 92 | 580 |
| 5 | 822 | 1085 | 69 | 96 | 588 |
| **median** | **895 ms** | **1141 ms** | **69 ms** | **96 ms** | **588 ms** |

With an empty cover cache (what a first launch looks like), shelves drawn: before 822-1236 ms (median 1113), after 62-72 ms (median 67).

## 2. Where the time went (before) and what changed
Timeline of one "before" run: Application.onCreate 28 ms; showSavedLibrary 50 ms; `loadRemote` 670 ms (parse 100 ms, **`TrackTitles.cleanAll` 484 ms**, facts, restore 45 ms); `HomeShelves.build` 739 ms while competing with that; shelves drawn at 866 ms; then **five more full `HomeShelves.build` passes (~300-470 ms each)** as the library finished loading.
- Home is now drawn from a saved snapshot (`home.json`, about 30 KB: the shelves exactly as last drawn, read in 1-2 ms).
- The library load starts after the first frame (it competed with it for the CPU).
- The saved Jellyfin list is stamped with the title-cleaning rules version; cleaning it again at every launch is skipped (484 ms of CPU).
- Home works its shelves out again only after the whole library has loaded and once per burst of changes (was: after every step).
- The account sync at launch is background priority (17 requests used to queue ahead of the covers).
- The first screens' covers are decoded into memory before the first frame (`ArtCache.warm`; at most 12 are fetched from the server when the disk lacks them).

## 3. Covers
Visible cover slots (every `ArtImage` in composition) that have their picture. "Empty cache" = `am start --ez clearcovers true` each time. 14 of 15 is the best possible on this screen: one album has no picture on the server (verified: the Jellyfin image endpoint answers 404 for the album and for its songs).
| | Before | After |
|---|---|---|
| Warm cache, after 1 s / 3 s / 10 s | 100 / 100 / 100 % | 93 (14 of 15) / 93 / 93 % from 250 ms on |
| Empty cache, after 0.25 s | n/a | 0-46 % |
| Empty cache, after 0.5 s | n/a | 0-93 % |
| Empty cache, after 1 s | **0 % in all 5 runs** | **93 % in all 5 runs** |
| Empty cache, after 3 s | 0-100 % (median 46) | 93 % in all 5 runs |
| Empty cache, after 10 s | 73-100 % (median 100; two runs stuck at 73) | 93 % in all 5 runs |
| Jellyfin requests in the first 10 s, warm cache | n/a (probe had no counter) | 17 |
| Jellyfin requests in the first 10 s, empty cache | 23-32 | 28-41 (all pictures are fetched now instead of some being given up on) |

### Root causes (all found on the Fold with the real library)
1. **A race on one temporary file.** Songs of one album share one cover key; their rows fetched the same picture at the same moment; every writer used the same temp name and deleted the target before renaming, so a finished picture could vanish, or be read half written, under the row about to decode it. That row kept its placeholder for good (a run showed `file exists=false` / a 9,692-byte file that did not decode). Fix: `CoverFiles.saveAtomically` (own temp name, one-step replace); a saved picture that does not decode is removed and fetched again.
2. **A failure was final for three minutes.** One failed answer parked the cover for 3 minutes and nothing asked again until the row was drawn anew. Fix: `retryTransient`, growing pause 0.4 s ... 15 s with jitter, for as long as the row is on screen (background prefetch gives up after 3 attempts and rests 30 s).
3. **Slow covers shrank the gate.** A server resizing pictures for the first time is slow; the adaptive limit halved on slow answers and fell to 1-2, serialising the visible covers (`limit=1 queued=12` in the logs). Fix: the time a picture takes no longer counts against the limit or holds background work back (errors still count); visible rows may always use 4 slots.
4. **The account sync queued ahead of the covers** at the same priority (17 requests). Fix: background priority at launch.
5. **Wrong covers (a different bug, found while checking "every cover matches its song").** On the real library 5,889 of 10,325 songs have no picture of their own and use their album's, and 1,807 of them share an AlbumId with songs of other albums (one id was used by 286 songs of many albums, others by 32, 36, 49 ...), so they showed another album's picture. Fix: `CoverVetting`: an album picture is kept only for songs whose album name is the one name all songs pointing at it share; the rest show no cover (Android) or the catalog lookup (web). A song's own picture is never touched (4,333 songs; each own key is used by exactly one song). The same rule is in FLACie Web (`CoverVetting.cs`, applied in `Library.Merge`).
Items checked against the live server (anonymous image endpoint): The Fame Monster (album picture exists, 24 songs use it: kept, vetting leaves it alone), What Evil Lurks and Sexy Bitch (404 for album and songs: no picture on the server).

## 4. Emulator, throttled and flaky mock (10,000 songs, `covers` scenario, 3 runs each, empty cover cache)
(The software-rendered emulator is slow: its absolute times are not the phone's. The mock now serves a PNG Android can decode; its earlier hand-made JPEG did not.)
| Profile | Build | Home drawn | Covers 1 s / 3 s / 10 s |
|---|---|---:|---|
| slow (300 ms, capacity 8) | before | 2.9-3.1 s | 0 / 0 / 100 % |
| slow | after | 123-180 ms | 0-33 / 0-100 / 100 % |
| flaky (10 % 5xx) | before | 2.6-2.8 s | 0 / 44-88 / 100 % |
| flaky | after | 134-206 ms | 0 / 100 / 100 % |
The "after" runs here are from a build that still asked the server for 48 covers at launch (56 image requests); the final build asks for 12 (see §6).

## 5. Tests
- Android unit: 302 run, 2 skipped (as before), 0 failed (beta 55: 280). New: `CoverReliabilityTest` (CoverVetting, retry/backoff, gate floor and cover-time rules, saved Home, cover warm order) and `CoverFilesTest` (20 writers + a reader never see a missing or half-written file; it failed 275 times against the old write and passes 3 of 3 runs).
- One existing test configuration changed: `JellyfinGateTest` uses a single slot, so its config now sets `visibleFloor = 1` (production default 4); no assertion was weakened.
- .NET: 257 pass (beta 55: 250): `CoverVettingTests` (7).
- Browser (Playwright): see the release notes for the count.

## 6. Not tested / known
- The final 12-cover launch prefetch was measured on the Fold only (the emulator matrix ran the 48-cover build).
- Real-Jellyfin load was not measured beyond the Fold's own request counts; no CPU numbers from the server.
- The web server's vetting was unit-tested, not looked at on the live site's covers before the deploy.
- Songs whose album picture was removed by the vetting show a placeholder on Android (no catalog lookup, on purpose: no new external requests); the web shows the catalog lookup.
- No by-ear checks apply. RG Rotate not touched.
