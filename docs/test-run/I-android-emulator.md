# I-android-emulator: Android load measurement and smoke test on the emulator (beta 51 against beta 52)

Run on 2026-10-12 on the headless emulator `flacie_phone` (Android 14 / API 34, google_apis x86_64, 2 cores, 2 GB, software GPU) against the
10,000-song mock Jellyfin (`ipodsync/tools/loadtest/mock.mjs`, Quick Connect auto-approved, so no password is typed anywhere).

| | Build | versionCode | Signing certificate SHA-256 (verified with apksigner) |
|---|---|---|---|
| BEFORE | released beta 51 (`FLACie-1.0-beta51.apk`, GitHub release asset) | 66 | 6a4eb335...97a106 |
| AFTER | `FLACie-1.0-beta52-8c1cdfa.apk` (release build) | 67 | 6a4eb335...97a106 |

The emulator is software rendered and slow: **only request counts, concurrency and order of magnitude of waits mean anything here, not smoothness.**
Audio was not listened to (cannot be); "playing" means the media session reports PLAYING.

Driver: `ipodsync/tools/loadtest/run-android.ps1` (+ `run-android.mjs`, `adbui.mjs`, `run-android-matrix.ps1`, `smoke-android.mjs`, `README-android.md`).
Result files: `docs/test-run/load-data/android-{before,after}-{fast,slow,flaky,ratelimit}.json` (shape of the web results), extra BEFORE runs
`android-before-fast-run2.json`, `android-before-flaky-run1-nolibrary.json`, `android-before-flaky-run2.json`, the crash comparison
`android-crashcheck-{before,after}.json` and the smoke result `android-smoke-after.json`. Screenshots were kept out of the repository.

## 1. Numbers (one run per cell, each from a fresh mock, `pm clear` and a freshly booted emulator)

Scenarios: first sync = sign-in tap then idle until 120 s have passed; scrolling = about 40 s of `cmd input swipe` flings in the Songs list;
skip burst = 40 `KEYCODE_MEDIA_NEXT` one after the other, app in the background (like headset or lock-screen keys), keys about 70 ms apart;
warm start = `am force-stop`, launch again with the library on disk, 60 s.

### Server: fast (no delay)
| | BEFORE beta 51 | AFTER beta 52 |
|---|---:|---:|
| songs listed at the end of the first sync | 10,000 | 10,000 |
| first songs on screen (first poll showing songs, from the sign-in tap) | 14.6 s (10,000 at once) | 13.8 s (3,500 of them) |
| all 10,000 listed | 14.6 s | 46.0 s (22 pages, shown as they arrive) |
| Jellyfin requests in the 120 s after sign-in | 46 | 69 |
| &nbsp; of which library pages / covers | 5 / 10 | 23 / 15 |
| peak requests at once / peak per second | 1 / 11 | 1 / 7 |
| scrolling: cover requests (distinct / repeated asks) | 628 (627 / 1), 112 flings | 103 (75 / 28), 149 flings |
| scrolling: total requests, peak at once | 660, 3 | 111, 1 |
| 40 skips: total requests | 68 | 61 |
| 40 skips: streams opened / abandoned | 15 / 2 | 10 / 2 |
| 40 skips: queue moved by / ready after (from the first key) | 41 / 19.0 s | 40 / 8.7 s |
| warm start: all songs listed after / requests in the first 60 s | 10.7 s / 34 | 10.4 s / 20 |
| kill and relaunch: queue back, crash lines of the app | yes (2nd run) / 0 | yes / 0 |
| app memory (PSS) after first sync / after warm start | 121 / 179 MB | 86 / 131 MB |

### Server: slow (about 300 ms per answer, 8 at once at most)
| | BEFORE | AFTER |
|---|---:|---:|
| first songs / all songs | 14.5 s / 14.5 s | 14.3 s / 41.2 s |
| requests in 120 s (pages / covers) | 44 (5 / 10) | 67 (23 / 14) |
| peak at once | 4 | 5 |
| scrolling: cover requests (distinct / repeats), peak at once | 430 (426 / 0), 9 | 105 (78 / 27), 3 |
| 40 skips: total / streams opened / abandoned / peak | 63 / 6 / 1 / 9 | 58 / 8 / 2 / 7 |
| 40 skips: ready after | 7.2 s | 8.3 s |
| warm start: all songs / requests in 60 s | 11.0 s / 33 | 10.9 s / 20 |

### Server: flaky (10 % of answers are 503)
| | BEFORE | AFTER |
|---|---:|---:|
| songs listed after the 120 s window | **0 in both runs** ("No songs yet") | 10,000 |
| first songs / all songs | never | 15.1 s / 42.2 s |
| requests in 120 s | 35 and 9 (1 to 3 library pages, 2 and 3 answered 503) | 74 (28 library pages, 6 answers 503, 24 retries) |
| scrolling, skip burst | not possible (no library) | 128 cover requests (80 / 35); 62 requests, 8 streams, 0 abandoned |
| warm start | run 1: no library either; run 2: library listed after 10.2 s (the relaunch loaded it), 32 requests in 60 s | 11.2 s, 20 requests |

### Server: rate limiting (more than 20 requests in a second get 429)
| | BEFORE | AFTER |
|---|---:|---:|
| first songs / all songs | 14.3 s / 14.3 s | 15.2 s / 38.2 s |
| requests in 120 s, peak per second, answered 429 | 46, 8, 0 | 75, 8, 0 |
| scrolling: cover requests (distinct / repeats) | 181 (161 / 0) | 92 (68 / 24) |
| 40 skips: total / streams opened / abandoned | 67 / 7 / 2 | 64 / 13 / 2 |
| 40 skips: ready after | 6.7 s | 17.1 s |
| warm start: all songs / requests in 60 s | 9.7 s / 34 | 11.0 s / 20 |

Note: on Android neither build ever went over 20 requests in a second, so this profile did not actually rate-limit anybody; the cells above only
show how the two builds behave on a server that answers with 30 ms of latency.

### Reading the numbers honestly
- Beta 51 on Android was already light on the first sync (4 big library pages, 46 requests in 120 s). Beta 52 asks for **more** requests in the first
  120 s (69 to 75: the library comes in 22 pages of 500 so the Songs list fills as pages arrive, plus 15 to 22 covers) and takes **about three times
  longer to have all 10,000 songs** (38 to 46 s against 14 s), but the first 3,500 songs are on screen after the same 14 s and nothing waits for the whole
  library. That is the intended gentle first sync, paid for with a longer total. It is not a reduction of first-sync requests on Android.
- The clear Android wins are scrolling (cover requests 628 to 103, 430 to 105, 181 to 92: one cover per album at one size, kept on disk) and the warm
  start (34 to 20 requests in the first 60 s, no per-start re-reading), with lower memory. The "repeated asks" column of AFTER (24 to 35) is not
  zero: the same cover URL was asked again after it had been evicted or by two lists; they are cheap revalidations, not new pictures.
- The flaky result is the most important difference: beta 51 never showed a library within 120 s in two out of two runs because one failed library page was
  not retried (only a restart loaded it); beta 52 retried (24 retries, adaptive limit 1) and listed all songs.
- The skip burst on the slow and rate-limit profiles got slower to "ready" (7.2 to 8.3 s, 6.7 to 17.1 s) while using fewer or equal requests: the
  emulator settles a 10,000-item queue after the jump, and the 17 s case was one slow run among several (fast 19.0 s before, 8.7 s after); one run per cell
  is not enough to rank them. Do not read more into it than "same order of magnitude".
- One run per cell. Spread between repeats was not measured except fast (BEFORE ran twice: 46 requests both times; kill-restore of run 1 was marked "not restored"
  by an early, too strict criterion and was fixed, see below).

## 2. Smoke test of the AFTER build (`smoke-android.mjs`, fast profile, fresh install, `android-smoke-after.json`)

22 of 23 checks passed. The one that did not is not a crash of the app (see 3).

| Check | Result | Detail |
|---|---|---|
| installed build | PASS | versionName 1.0-beta52 |
| first-launch screens: Sign in with Jellyfin | PASS | |
| Quick Connect sign-in against the mock | PASS | no password typed |
| library loads | PASS | 10,000 songs listed 30 s after sign-in |
| scrolling the Songs list | PASS | Song 1 to Song 1061 after 12 flings |
| a song plays | PASS | media session PLAYING |
| 40-skip burst (app in front) | PASS | 40 places on, PLAYING again after 13.6 s, 72 requests, 15 streams opened / 11 abandoned |
| pause | PASS | |
| force-stop and relaunch | PASS | mini player shows the paused song, not auto-playing (state 0 = paused/restored) |
| Settings > Storage cache line | PASS | "Cache 8.3 MB" |
| Clear cache | PASS | cache 7.6 MB afterwards (covers and saved audio), no "Clearing" left |
| library stays after Clear cache | PASS | 10,000 songs still listed |
| Up next opens with More buttons | PASS | 6 rows |
| row sheet: Play next, Move up, Move down, Remove from queue | PASS | |
| Move down changes the order, playing song stays current | PASS | Song 100 and Song 1000 swapped; Song 1 stays the current song |
| Move up (back), playing song stays current | PASS | |
| Remove from queue takes the song out, playing song stays current | PASS | |
| no ANR of com.ipodemu | PASS | no "ANR in com.ipodemu" in logcat |
| no crash of com.ipodemu in the crash buffer | PASS | |
| crash buffer completely clean | **FAIL** | the system Bluetooth process (`com.google.android.bluetooth`) died with an OutOfMemoryError, see 3 |

The smoke test also ran four times while the driver was being corrected (earlier runs stopped on driver mistakes: the mini player coordinates, the Clear cache
row being off screen, the Up next row index); the table is the last full run (08:03 local time). The 16 `FLACieGate` lines of that run are in the JSON field
`gateLines` (full text was kept next to the screenshots, not committed).

## 3. Findings and gaps

1. **The system Bluetooth process dies with an OutOfMemoryError during a 40-skip burst, in both beta 51 and beta 52** (`android-crashcheck-*.json`; 4 of 4 bursts with the app
   in front; the burst with the app in the background did not show it in the matrix runs, but the matrix clears the crash buffer between scenarios so that cannot
   be said). The stack is in `android.media.session.MediaSession$QueueItem` / `IPlaybackConfigDispatcher` parceling inside the emulator image's Bluetooth service:
   the app publishes the whole 10,000-song queue to its media session (`dumpsys media_session` shows `size=10000`) and a controller process that tries to read it runs
   out of its 192 MB heap. The app's own process did not crash and did not ANR. Whether a real phone's Bluetooth stack behaves the same is NOT KNOWN: it
   deserves a look (cap the queue exposed to the session, for example to a window around the current song) before it is dismissed as an emulator image quirk.
2. Beta 51 does not recover from a failed library page on a flaky server within the 120 s window (2 of 2 runs); beta 52 does (section 1).
3. Not measured here: smoothness; whether covers on screen had loaded when scrolling stopped (the web driver measures that, this one does not); real device and real
   Jellyfin behaviour; by-ear checks; the delta sync and WebSocket (the mock has `/__touch` and `/__delete`; the Android driver did not use them); the low-space eviction of the
   cache; data-saver transcoding; Jam, downloads, NAS. The Up next test used the pane's row sheet (Move up/down/Remove), not drag and drop or swipe-to-remove.
4. Driver/emulator notes: the emulator sometimes boots with no default route ("Jellyfin is not answering right now"); `run-android.ps1` now pings the host and toggles
   Wi-Fi when needed. A "System UI isn't responding" dialog is dismissed with Wait (`anrDialogsDismissed`; 0 in all but one AFTER ratelimit run, where 1 was dismissed).
   The skip burst of the matrix runs is done with the app in the background (so the software renderer is not drawing a 10,000-row list per skip); the smoke test and
   the crash comparison run it with the app in front. Time-to-songs has a resolution of about 3 s (uiautomator polling); the mock-side clock is in `server.firstItemsPageMs`
   and `server.lastItemsPageMs` of each first-sync result.
5. Beta 51 kill-restore of the first BEFORE matrix said "not restored" because the check compared the song title at kill time (the 3 s mock songs keep advancing); the
   criterion was changed to "the app comes back with a queue" and `sameSong` / `queueDelta` are recorded. In the stored BEFORE fast file that cell still reads false;
   `android-before-fast-run2.json` has the corrected value (true).
