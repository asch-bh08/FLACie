# I-load: what FLACie asks of Jellyfin, beta 51 (before) against beta 52 (after)

Written 2026-10-12 by the overnight run. Raw numbers: `docs/test-run/load-data/*.json`. Tools: `ipodsync/tools/loadtest/` (`mock.mjs`, `run-web.mjs`, `run-android.ps1`, `report.mjs`; how to re-run is at the end).

## In one paragraph
With a 10,000-song library, beta 51 asked Jellyfin for the format of every song one by one (10,000 requests in the first two minutes on the web server, 1,682 requests in a single second), asked for covers at 500 px (web) or 640 px (phone) JPEG, one request per song, and on a flaky server it never showed the library at all. Beta 52 puts every Jellyfin request through one adaptive gate (limited concurrency, priorities, cancellation, back-off, circuit breaker), reads the stream facts inside the library pages, syncs only changes afterwards, asks for each cover once at one size (240 px WebP, the album picture shared by all songs of an album) and keeps everything on disk. On the web server a first sync went from 10,074 to 67 requests, scrolling the whole list from 7,562 to 829 cover requests, a warm start from 696 to 25 requests (slow server), and 1,351 answers of "429 too many requests" to none. Everything below was measured against a **mock** Jellyfin, not a real one (see "What these numbers are not").

## How it was measured
- **Server under test**: `ipodsync/tools/loadtest/mock.mjs`, a stand-in Jellyfin with 10,000 songs in about 830 albums (CD FLAC, 24-bit FLAC, MP3, AAC), Quick Connect, a library WebSocket, and a `/__stats` page that counts every request (by kind, peak at the same moment, peak per second, picture sizes, repeated pictures, streams opened/abandoned). Four behaviours, switched with `/__profile`:
  - **fast**: answers at once.
  - **slow**: 300 ms (plus up to 100 ms) per answer, and the server works on 8 requests at a time (a small busy Jellyfin: more parallel requests only wait longer).
  - **flaky**: 10 % of answers are 503.
  - **rate-limiting**: more than 20 requests in one second get 429 with `Retry-After: 1`.
  Audio streams take 1.5 s to send, so a stream the client abandons is seen as abandoned.
- **Web (and Windows)**: `run-web.mjs` starts a fresh mock and a fresh FLACie Web data folder per run, signs in with the browser (mock account), lists Explore, and then: (1) *first sync*: watches the 120 s after sign-in; (2) *scrolling*: flings through the whole 10,000-row list in about 40 s; (3) *40 quick skips*: plays a song and presses Next 40 times, 60 ms apart; (4) *warm start*: restarts the server on the same data folder. "Windows app mode" is the same server started with `FLACIE_PARENT_PID` set, which is how the desktop app starts its embedded copy; the Windows shell itself (WebView2) was not driven.
- **Android**: `run-android.ps1` drives the release APK on the emulator (`flacie_phone`) with adb: Quick Connect sign-in (auto-approved by the mock; no password is typed anywhere), then the same four steps with swipes and `KEYCODE_MEDIA_NEXT`. The emulator is software rendered: use the request counts, not the times.
- **Builds**: before = `origin/main` (beta 51: web server published from commit 7dab8ee; Android = the released APK `v1.0.0-beta51`). After = `release/beta52` (web server published from the merge before the queue-editing change, which does not touch loading; Android = the release APK 1.0-beta52 built from 8c1cdfa).
- One run per cell, 120 s window. Not a statistical benchmark: differences of a few requests or a second are noise; differences of 10x are not.

## How to read the tables
- "Jellyfin requests" counts what the mock saw, whoever sent it (including background work). "Peak at the same moment" is meaningless on the *fast* server (answers come back before the next request is sent), so look at the slow server for concurrency.
- "Last song ready after" includes the roughly 5 s it takes the driver to press Next 40 times.
- "Picture sizes asked for": before = one size per song and use (web 500 px quality 90, phone 640 px), after = one size for rows (240 px WebP quality 80) plus the full picture only in Now Playing.

## Reading of the results
1. **Format reads are gone.** Web first sync: 10,000 single-song reads before, none after; the facts come inside 20 library pages that also carry the stream data. Requests in the first two minutes: 10,074 to 67 (fast), 1,782 to 67 (slow), 3,453 to 68 (rate-limiting).
2. **Covers: asked once, at one size.** Web scrolling: 7,562 to 829 requests (fast); the 12 songs of an album now share one picture. Phone: 628 to 103 (fast), 430 to 105 (slow). The repeated asks that remain (phone: about 25 per run; web slow: 58) are covers that were cancelled when the row scrolled away and asked for again when it came back, which is what cancellation means; they are not re-downloads of a saved picture.
3. **Rate limiting**: beta 51 collected 1,351 answers of 429 on the web server and showed only 1 of 23 covers on screen after scrolling; beta 52 got none and showed all 23.
4. **Flaky server**: beta 51 never showed a library (web: 0 songs after two minutes, phone: 0 songs): one 503 on a library page ended the sync. Beta 52 retries at most twice with a jittered back-off and shows all 10,000 songs after 4.8 s (web) / 42 s (phone).
5. **Warm start** (library already on disk): requests in the first 60 s: web 696 to 25 on the slow server (33 to 26 fast); phone 34 to 20.
6. **Skips**: on the slow server a 40-skip burst went from 244 requests (peak 20 at once) to 53 (peak 6) on the web. On a fast server beta 51 was already cheap (54 vs 55): there was nothing to gain.
7. **Windows app mode** matches the web server (same code). Beta 51's desktop copy did the same background reads as the always-on server (1,768 requests in the window); beta 52's does not run the format pass or the 15-minute timer, only the delta sync and the socket.

## Where beta 52 is not better, said plainly
- **Phone first sync is heavier on the request count and takes longer to complete**: 46 to 69 requests and 15 s to 46 s to list all 10,000 songs on a fast server. The reason is deliberate: beta 51 listed songs without their streams and got format facts only from FLACie Web or the NAS tags (a Jellyfin-only user had none); beta 52 pages 500 songs at a time *with* the stream data, so Jellyfin is enough, and it paces itself (it waits while the screen is touched or the server is slow). The first songs appear after the same 14 s as before. On the web the time to the first songs is the same (2.9 s), the time to all of them 3.0 s to 4.9 s (fast), 5.1 s to 7.1 s (slow).
- **Phone skip burst, rate-limiting server**: 6.7 s to 17 s to be ready and 13 streams opened (7 before). One run on a software-rendered emulator: it may be noise, but it was not repeated; a likely cause is the next-song read-ahead (below), which opens streams of its own, so do not read "streams opened" as "streams played".
- **Next-song preload on the phone caches the whole next one or two songs** (at background priority) instead of "just the start"; on a 10,000-song library over mobile data that is more bytes than the owner's requirement asked for. Preloading only the start is on the open list. (On the web the preload is the first 1 MiB only.)
- The 40 quick skips still open a handful of streams (web 4-6, phone 8-13): one for the song that finally plays, and read-aheads. Abandoned ones are cut (the mock counts 1-3 per run as abandoned).

## What these numbers are not
- Not a real Jellyfin. Real servers differ in how they answer `MinDateLastSaved` (inclusive, handled), image tags, 429 headers and how slow a library page with streams is; a real library has thousands of albums with uneven art. Re-measure on the real server (owner checklist in the release notes).
- Not a real phone: emulator, software rendered, one run per cell.
- Nothing about sound, gapless playback or smoothness.

## Tables
Every cell: requests counted by the mock; "n/a" = the build does not make that kind of request (or the run did not reach that step); "not reached" = the step never completed within the run (the flaky server under beta 51).

### FLACie Web (server, browser client)
#### Server: fast (no delay)
| | before (beta 51) | after (beta 52) |
|---|---:|---:|
| **First sync, empty start** | | |
| songs shown at the end of the 120 s window | 10,000 | 10,000 |
| time until the first songs are listed | 3.0 s | 2.9 s |
| time until all songs are listed | 3.0 s | 4.9 s |
| Jellyfin requests in the 120 s after sign-in | 10,074 | 67 |
| &nbsp;&nbsp;of which one-song info reads | 10,000 | n/a |
| &nbsp;&nbsp;of which library pages | 9 | 20 |
| peak requests at the same moment | 1 | 1 |
| peak requests in one second | 1,682 | 15 |
| answers that were 429 / 5xx | 0 / 0 | 0 / 0 |
| **Scrolling the whole list (about 40 s)** | | |
| cover requests | 7,562 | 829 |
| &nbsp;&nbsp;distinct pictures / repeated asks | 7,562 / 0 | 829 / 0 |
| &nbsp;&nbsp;picture sizes asked for | 500px-default-q90 | 240px-webp-q80 |
| peak requests at the same moment | 1 | 1 |
| covers on screen that had loaded when scrolling stopped | 23 of 23 | 23 of 23 |
| **40 quick skips** | | |
| Jellyfin requests | 54 | 55 |
| audio streams opened / abandoned before the end | 5 / 3 | 6 / 3 |
| peak requests at the same moment | 2 | 2 |
| last song ready after (from the first skip) | 6.5 s | 6.4 s |
| **Warm start (library already on disk)** | | |
| songs listed after | 2.3 s | 2.2 s |
| Jellyfin requests in the first 60 s | 33 | 26 |

#### Server: slow (about 300 ms per answer, 8 at once at most)
| | before (beta 51) | after (beta 52) |
|---|---:|---:|
| **First sync, empty start** | | |
| songs shown at the end of the 120 s window | 10,000 | 10,000 |
| time until the first songs are listed | 5.1 s | 3.0 s |
| time until all songs are listed | 5.1 s | 7.1 s |
| Jellyfin requests in the 120 s after sign-in | 1,782 | 67 |
| &nbsp;&nbsp;of which one-song info reads | 1,708 | n/a |
| &nbsp;&nbsp;of which library pages | 9 | 20 |
| peak requests at the same moment | 7 | 6 |
| peak requests in one second | 20 | 10 |
| answers that were 429 / 5xx | 0 / 0 | 0 / 0 |
| **Scrolling the whole list (about 40 s)** | | |
| cover requests | 2,030 | 886 |
| &nbsp;&nbsp;distinct pictures / repeated asks | 2,030 / 0 | 828 / 58 |
| &nbsp;&nbsp;picture sizes asked for | 500px-default-q90 | 240px-webp-q80 |
| peak requests at the same moment | 19 | 8 |
| covers on screen that had loaded when scrolling stopped | 23 of 23 | 23 of 23 |
| **40 quick skips** | | |
| Jellyfin requests | 244 | 53 |
| audio streams opened / abandoned before the end | 5 / 3 | 4 / 1 |
| peak requests at the same moment | 20 | 6 |
| last song ready after (from the first skip) | 8.0 s | 6.4 s |
| **Warm start (library already on disk)** | | |
| songs listed after | 2.6 s | 2.2 s |
| Jellyfin requests in the first 60 s | 696 | 25 |

#### Server: flaky (10 % of answers are 503)
| | before (beta 51) | after (beta 52) |
|---|---:|---:|
| **First sync, empty start** | | |
| songs shown at the end of the 120 s window | 0 | 10,000 |
| time until the first songs are listed | not reached | 2.8 s |
| time until all songs are listed | not reached | 4.8 s |
| Jellyfin requests in the 120 s after sign-in | 51 | 79 |
| &nbsp;&nbsp;of which one-song info reads | n/a | n/a |
| &nbsp;&nbsp;of which library pages | 9 | 23 |
| peak requests at the same moment | 4 | 4 |
| peak requests in one second | 14 | 17 |
| answers that were 429 / 5xx | 0 / 7 | 0 / 11 |
| **Scrolling the whole list (about 40 s)** | | |
| cover requests | n/a | 826 |
| &nbsp;&nbsp;distinct pictures / repeated asks | n/a / n/a | 734 / 0 |
| &nbsp;&nbsp;picture sizes asked for | n/a | 240px-webp-q80 |
| peak requests at the same moment | n/a | 4 |
| covers on screen that had loaded when scrolling stopped | n/a of n/a | 17 of 23 |
| **40 quick skips** | | |
| Jellyfin requests | n/a | 55 |
| audio streams opened / abandoned before the end | n/a / n/a | 4 / 1 |
| peak requests at the same moment | n/a | 2 |
| last song ready after (from the first skip) | not reached | 6.1 s |
| **Warm start (library already on disk)** | | |
| songs listed after | not reached | 1.9 s |
| Jellyfin requests in the first 60 s | 34 | 27 |

#### Server: rate-limiting (more than 20 requests in a second get 429)
| | before (beta 51) | after (beta 52) |
|---|---:|---:|
| **First sync, empty start** | | |
| songs shown at the end of the 120 s window | 10,000 | 10,000 |
| time until the first songs are listed | 2.9 s | 2.8 s |
| time until all songs are listed | 2.9 s | 4.8 s |
| Jellyfin requests in the 120 s after sign-in | 3,453 | 68 |
| &nbsp;&nbsp;of which one-song info reads | 3,379 | n/a |
| &nbsp;&nbsp;of which library pages | 9 | 20 |
| peak requests at the same moment | 7 | 4 |
| peak requests in one second | 42 | 17 |
| answers that were 429 / 5xx | 1,351 / 0 | 0 / 0 |
| **Scrolling the whole list (about 40 s)** | | |
| cover requests | 7,565 | 828 |
| &nbsp;&nbsp;distinct pictures / repeated asks | 634 / 0 | 807 / 0 |
| &nbsp;&nbsp;picture sizes asked for | 500px-default-q90 | 240px-webp-q80 |
| peak requests at the same moment | 12 | 4 |
| covers on screen that had loaded when scrolling stopped | 1 of 23 | 23 of 23 |
| **40 quick skips** | | |
| Jellyfin requests | 396 | 51 |
| audio streams opened / abandoned before the end | 2 / 1 | 4 / 1 |
| peak requests at the same moment | 8 | 3 |
| last song ready after (from the first skip) | 6.2 s | 6.0 s |
| **Warm start (library already on disk)** | | |
| songs listed after | 2.0 s | 1.9 s |
| Jellyfin requests in the first 60 s | 1,376 | 25 |

### Windows app mode (the same server code started the way the desktop app starts it)
#### Server: fast (no delay)
| | before (beta 51) | after (beta 52) |
|---|---:|---:|
| **First sync, empty start** | | |
| songs shown at the end of the 120 s window | 10,000 | 10,000 |
| time until the first songs are listed | 2.9 s | 0.8 s |
| time until all songs are listed | 2.9 s | 2.8 s |
| Jellyfin requests in the 120 s after sign-in | 10,074 | 67 |
| &nbsp;&nbsp;of which one-song info reads | 10,000 | n/a |
| &nbsp;&nbsp;of which library pages | 9 | 20 |
| peak requests at the same moment | 1 | 1 |
| peak requests in one second | 2,154 | 14 |
| answers that were 429 / 5xx | 0 / 0 | 0 / 0 |
| **Scrolling the whole list (about 40 s)** | | |
| cover requests | 7,569 | 829 |
| &nbsp;&nbsp;distinct pictures / repeated asks | 7,569 / 0 | 829 / 0 |
| &nbsp;&nbsp;picture sizes asked for | 500px-default-q90 | 240px-webp-q80 |
| peak requests at the same moment | 1 | 1 |
| covers on screen that had loaded when scrolling stopped | 23 of 23 | 23 of 23 |
| **40 quick skips** | | |
| Jellyfin requests | 50 | 51 |
| audio streams opened / abandoned before the end | 3 / 1 | 4 / 1 |
| peak requests at the same moment | 2 | 2 |
| last song ready after (from the first skip) | 6.0 s | 6.1 s |
| **Warm start (library already on disk)** | | |
| songs listed after | 1.9 s | 1.9 s |
| Jellyfin requests in the first 60 s | 33 | 25 |

#### Server: slow (about 300 ms per answer, 8 at once at most)
| | before (beta 51) | after (beta 52) |
|---|---:|---:|
| **First sync, empty start** | | |
| songs shown at the end of the 120 s window | 10,000 | 10,000 |
| time until the first songs are listed | 5.1 s | 3.0 s |
| time until all songs are listed | 5.1 s | 7.0 s |
| Jellyfin requests in the 120 s after sign-in | 1,768 | 67 |
| &nbsp;&nbsp;of which one-song info reads | 1,695 | n/a |
| &nbsp;&nbsp;of which library pages | 9 | 20 |
| peak requests at the same moment | 7 | 6 |
| peak requests in one second | 19 | 8 |
| answers that were 429 / 5xx | 0 / 0 | 0 / 0 |
| **Scrolling the whole list (about 40 s)** | | |
| cover requests | 2,025 | 886 |
| &nbsp;&nbsp;distinct pictures / repeated asks | 2,025 / 0 | 828 / 58 |
| &nbsp;&nbsp;picture sizes asked for | 500px-default-q90 | 240px-webp-q80 |
| peak requests at the same moment | 16 | 7 |
| covers on screen that had loaded when scrolling stopped | 23 of 23 | 23 of 23 |
| **40 quick skips** | | |
| Jellyfin requests | 241 | 53 |
| audio streams opened / abandoned before the end | 3 / 1 | 4 / 1 |
| peak requests at the same moment | 22 | 5 |
| last song ready after (from the first skip) | 7.9 s | 6.3 s |
| **Warm start (library already on disk)** | | |
| songs listed after | 2.2 s | 2.2 s |
| Jellyfin requests in the first 60 s | 709 | 25 |

### Android (emulator, software rendered; request counts only)
#### Server: fast (no delay)
| | before (beta 51) | after (beta 52) |
|---|---:|---:|
| **First sync, empty start** | | |
| songs shown at the end of the 120 s window | 10,000 | 10,000 |
| time until the first songs are listed | 15 s | 14 s |
| time until all songs are listed | 15 s | 46 s |
| Jellyfin requests in the 120 s after sign-in | 46 | 69 |
| &nbsp;&nbsp;of which one-song info reads | n/a | n/a |
| &nbsp;&nbsp;of which library pages | 5 | 23 |
| peak requests at the same moment | 1 | 1 |
| peak requests in one second | 11 | 7 |
| answers that were 429 / 5xx | 0 / 0 | 0 / 0 |
| **Scrolling the whole list (about 40 s)** | | |
| cover requests | 628 | 103 |
| &nbsp;&nbsp;distinct pictures / repeated asks | 627 / 1 | 75 / 28 |
| &nbsp;&nbsp;picture sizes asked for | 640px-default-q90 | 240px-webp-q80 |
| peak requests at the same moment | 3 | 1 |
| covers on screen that had loaded when scrolling stopped | n/a of n/a | n/a of n/a |
| **40 quick skips** | | |
| Jellyfin requests | 68 | 61 |
| audio streams opened / abandoned before the end | 15 / 2 | 10 / 2 |
| peak requests at the same moment | 4 | 2 |
| last song ready after (from the first skip) | 19 s | 8.7 s |
| **Warm start (library already on disk)** | | |
| songs listed after | 11 s | 10 s |
| Jellyfin requests in the first 60 s | 34 | 20 |

#### Server: slow (about 300 ms per answer, 8 at once at most)
| | before (beta 51) | after (beta 52) |
|---|---:|---:|
| **First sync, empty start** | | |
| songs shown at the end of the 120 s window | 10,000 | 10,000 |
| time until the first songs are listed | 14 s | 14 s |
| time until all songs are listed | 14 s | 41 s |
| Jellyfin requests in the 120 s after sign-in | 44 | 67 |
| &nbsp;&nbsp;of which one-song info reads | n/a | n/a |
| &nbsp;&nbsp;of which library pages | 5 | 23 |
| peak requests at the same moment | 4 | 5 |
| peak requests in one second | 10 | 6 |
| answers that were 429 / 5xx | 0 / 0 | 0 / 0 |
| **Scrolling the whole list (about 40 s)** | | |
| cover requests | 430 | 105 |
| &nbsp;&nbsp;distinct pictures / repeated asks | 426 / 0 | 78 / 27 |
| &nbsp;&nbsp;picture sizes asked for | 640px-default-q90 | 240px-webp-q80 |
| peak requests at the same moment | 9 | 3 |
| covers on screen that had loaded when scrolling stopped | n/a of n/a | n/a of n/a |
| **40 quick skips** | | |
| Jellyfin requests | 63 | 58 |
| audio streams opened / abandoned before the end | 6 / 1 | 8 / 2 |
| peak requests at the same moment | 9 | 7 |
| last song ready after (from the first skip) | 7.2 s | 8.3 s |
| **Warm start (library already on disk)** | | |
| songs listed after | 11 s | 11 s |
| Jellyfin requests in the first 60 s | 33 | 20 |

#### Server: flaky (10 % of answers are 503)
| | before (beta 51) | after (beta 52) |
|---|---:|---:|
| **First sync, empty start** | | |
| songs shown at the end of the 120 s window | 0 | 10,000 |
| time until the first songs are listed | not reached | 15 s |
| time until all songs are listed | not reached | 42 s |
| Jellyfin requests in the 120 s after sign-in | 35 | 74 |
| &nbsp;&nbsp;of which one-song info reads | n/a | n/a |
| &nbsp;&nbsp;of which library pages | 3 | 28 |
| peak requests at the same moment | 2 | 2 |
| peak requests in one second | 8 | 9 |
| answers that were 429 / 5xx | 0 / 2 | 0 / 6 |
| **Scrolling the whole list (about 40 s)** | | |
| cover requests | n/a | 128 |
| &nbsp;&nbsp;distinct pictures / repeated asks | n/a / n/a | 80 / 35 |
| &nbsp;&nbsp;picture sizes asked for | n/a | 240px-webp-q80 |
| peak requests at the same moment | n/a | 2 |
| covers on screen that had loaded when scrolling stopped | n/a of n/a | n/a of n/a |
| **40 quick skips** | | |
| Jellyfin requests | n/a | 62 |
| audio streams opened / abandoned before the end | n/a / n/a | 8 / 0 |
| peak requests at the same moment | n/a | 2 |
| last song ready after (from the first skip) | not reached | 8.6 s |
| **Warm start (library already on disk)** | | |
| songs listed after | not reached | 11 s |
| Jellyfin requests in the first 60 s | 19 | 20 |

#### Server: rate-limiting (more than 20 requests in a second get 429)
| | before (beta 51) | after (beta 52) |
|---|---:|---:|
| **First sync, empty start** | | |
| songs shown at the end of the 120 s window | 10,000 | 10,000 |
| time until the first songs are listed | 14 s | 15 s |
| time until all songs are listed | 14 s | 38 s |
| Jellyfin requests in the 120 s after sign-in | 46 | 75 |
| &nbsp;&nbsp;of which one-song info reads | n/a | n/a |
| &nbsp;&nbsp;of which library pages | 5 | 23 |
| peak requests at the same moment | 4 | 6 |
| peak requests in one second | 8 | 8 |
| answers that were 429 / 5xx | 0 / 0 | 0 / 0 |
| **Scrolling the whole list (about 40 s)** | | |
| cover requests | 181 | 92 |
| &nbsp;&nbsp;distinct pictures / repeated asks | 161 / 0 | 68 / 24 |
| &nbsp;&nbsp;picture sizes asked for | 640px-default-q90 | 240px-webp-q80 |
| peak requests at the same moment | 5 | 2 |
| covers on screen that had loaded when scrolling stopped | n/a of n/a | n/a of n/a |
| **40 quick skips** | | |
| Jellyfin requests | 67 | 64 |
| audio streams opened / abandoned before the end | 7 / 2 | 13 / 2 |
| peak requests at the same moment | 5 | 3 |
| last song ready after (from the first skip) | 6.7 s | 17 s |
| **Warm start (library already on disk)** | | |
| songs listed after | 9.7 s | 11 s |
| Jellyfin requests in the first 60 s | 34 | 20 |


## Re-running
```
# the mock alone, e.g. for a real device on the LAN (point the app at http://<this-pc>:8196)
node ipodsync/tools/loadtest/mock.mjs --port 8196 --items 10000 --streamms 1500
curl "http://127.0.0.1:8196/__profile?name=slow" ; curl http://127.0.0.1:8196/__stats

# web / Windows mode: publish a FLACie.Server build to a folder, then
node ipodsync/tools/loadtest/run-web.mjs --server <publish dir> --label after --shell web|desktop --profile fast|slow|flaky|ratelimit --window 120

# phone (emulator running, APK path): see README-android.md
powershell -File ipodsync/tools/loadtest/run-android.ps1 -Apk <apk> -Label after -Profile fast

# tables
node ipodsync/tools/loadtest/report.mjs docs/test-run/load-data
```
