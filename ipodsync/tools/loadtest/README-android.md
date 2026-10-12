# Android load measurement (emulator + 10,000-song mock Jellyfin)

Unattended, repeatable: the same scenarios as `run-web.mjs`, driven on the headless Android emulator (`flacie_phone`) with `adb`, `uiautomator dump` and the
mock's `/__stats`. No password is ever typed: sign-in is Jellyfin **Quick Connect**, which `mock.mjs` approves by itself.
The emulator is software rendered and slow: **count requests, do not judge smoothness.**

## Rerun

```powershell
# one profile (boots the emulator, installs the APK, runs every scenario, shuts the emulator and the mock down)
powershell -NoProfile -File C:\FLACie-handoff\heavy.ps1 -Dir C:\FLACie\ipodsync\tools\loadtest `
  -Cmd "powershell -NoProfile -File run-android.ps1 -Apk C:\wt\apk\FLACie-1.0-beta51.apk -Label before -Profile fast"

# all four profiles, each one under the heavy lock; results land in docs\test-run\load-data\android-<label>-<profile>.json
powershell -NoProfile -File run-android-matrix.ps1 -Apk C:\wt\apk\FLACie-1.0-beta51.apk -Label before
powershell -NoProfile -File run-android-matrix.ps1 -Apk <merged build apk> -Label after
```

`run-android.ps1 -Apk <path> -Label before|after -Profile fast|slow|flaky|ratelimit [-Window 120] [-Port 8296] [-Items 10000] [-Out file.json]
[-Scenarios first-sync,scrolling,skip-burst,warm-start,kill-restore]`. A run takes about 8 to 10 minutes. It boots the emulator, so it must hold the
machine-wide lock (`heavy.ps1`); it refuses to start when an emulator is already online (`-Reuse` drives that one instead).
Needs: Android SDK (`%LOCALAPPDATA%\Android\Sdk`: platform-tools, emulator, AVD `flacie_phone`), Node 18+, the APK (any build of `com.ipodemu`).
Screenshots go to `C:\wt\android-shots\<label>-<profile>\` (not committed).

Driving the driver by hand (emulator already up and the app installed): `node run-android.mjs --label dev --profile fast --window 40 --port 8297 --scenarios scrolling`,
and `node adbui.mjs dump|tap <text>|shot <png>|media` to look at what uiautomator sees. `run-android.ps1 -HoldOnly -StopFile <f>` boots and installs, then waits
for the file (a development session).

## Scenarios (one fresh mock per run, `pm clear` before the first)

| Scenario | What it does | Main numbers |
|---|---|---|
| `first-sync` | cold start, "Sign in with Jellyfin", server `http://10.0.2.2:<port>`, Quick Connect; Explore > Songs; idle until `-Window` s after the sign-in tap | time to first songs / all songs (from the tap; uiautomator polled every ~3 s, so +-3 s), `server.firstItemsPageMs`/`lastItemsPageMs` (mock clock), total requests, by kind, peak in flight, peak per second, images (distinct / repeats / variants), items pages, `requestsAfterSec` 5/10/30/60/120, PSS |
| `scrolling` | `cmd input swipe` in one on-device loop for ~40 s (about 3 flings per second) in the Songs list | swipes, image requests (distinct, repeats), peak in flight, total |
| `skip-burst` | plays a song, puts the app in the background (as with headset / lock-screen keys: the software-rendered emulator then does not draw a 10,000-row list per skip), then 40 x `KEYCODE_MEDIA_NEXT` one after the other about 70 ms apart (`keySpacingMs`; `--skip-in-app` keeps the app in front, `--skip-parallel` issues them without waiting) | streams opened / aborted / completed, total requests, `queueStep` (how far the queue moved; the Songs list is Title A-Z), `lastSongReadyMs` = burst start until PLAYING again on the song 40 places on (`readyBy` says whether that or the fallback was used) |
| `warm-start` | `am force-stop`, launch again with the library on disk, Explore > Songs | time to songs, requests in the first 60 s |
| `kill-restore` | plays a song, `am force-stop`, relaunch | `queueRestored`, `adb logcat -b crash -d` lines (0 = clean) |

## Result JSON

Same shape as `run-web.mjs` (`label`, `shell: "android"`, `profile`, `scenarios.{first-sync,scrolling,skip-burst,warm-start}` each with the `pick()` fields of the mock stats:
`total`, `byKind`, `peakInFlight`, `peakPerSecond`, `images`, `imageUrls`, `imageRepeats`, `imageVariants`, `streamsOpened/Aborted/Completed`, `itemsPages`, ...), plus
`kill-restore`, `versionName`, `crashBufferAtEnd` and `gate.<scenario>` = the last 40 `FLACieGate` logcat lines (present in builds that have the request gate).

Notes for reading the numbers
- `byKind.stream` counts every `/Audio/...` request, including `/Audio/<id>/Lyrics`; the audio itself is `streamsOpened` (`/stream`, range requests count again).
- Songs in the mock are 3 s long, so a playing queue advances by itself; `titleBefore`/`titleAfter` of the skip burst therefore move by more than 40 entries.
- The sd card of the emulator holds old test music: the driver keeps the app's music-file permission revoked so those files never join the 10,000 songs.
- A "System UI isn't responding" dialog (a slow emulator) is dismissed with Wait automatically (counted in `anrDialogsDismissed`).
