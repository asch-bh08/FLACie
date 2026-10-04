# FLACie handoff (updated 2026-10-04, late evening)

Repo: `C:\ipodemu` (GitHub asch-bh08/FLACie). `android/` = Kotlin/Compose/Media3 app. `ipodsync/` = .NET 9: `src/FLACie.Core`, `src/FLACie.Server` (FLACie Web, Blazor Server), `src/IpodSync.Maui` (Windows host), `src/IpodSync.Engine` (NativeAOT iPod engine). `main` = beta 11 plus the open-sources commit; `wip/beta11-fixes` is merged and can be deleted.

## State of the world

- **Released:** v1.0.0-beta12 (Android versionCode 26, MSI 0.12.0) = beta 11 plus the open download sources on Android and Windows. Beta 11 = Devices fix, Android player pill row, calmer bottom bar, swipe-away stops playback, graphs follow volume, Back from Up next, fast cold start (files/merged.json).
- **frank** runs the web code of commit db56b34 (includes the open sources). The file mover (`/home/hms/docker/filemove/server.py`, bind-mounted, container `filemove`) now has `POST /fetch` (copy in `ipodsync/tools/filemove/server.py`, previous version `server.py.bak-2026-10-04` beside it). Test file left behind on the NAS: `/media/Movies/_flacie_fetchtest/Kevin MacLeod - Sneaky Snitch.mp3` (5 MB, delete it by hand; we never delete NAS files).
- **Open sources** (web only so far): `FLACie.Core/OpenSources.cs` (Internet Archive restricted to `collection:etree OR netlabels`, Jamendo only `audiodownload_allowed`, Audius no key) wired into `DownloadCoordinator.DownloadAsync` (Downloads.cs). They only FIND while Soulseek searches; they download (via file mover `/fetch`) only if Soulseek found nothing, with a 4 s grace; then Lidarr. Jamendo is off until env `FLACIE_JAMENDO_CLIENT_ID` is set on the flacie-web container (needs a free Jamendo client id, we cannot create the account). Verified with real calls: Audius (Kevin MacLeod - Sneaky Snitch, filed in 2.4 s), Internet Archive (Grateful Dead - Dark Star found), misses fail cleanly. Jamendo NOT tested (no client id). Album downloads do not use the open sources. The Android DownloadCoordinator now has it too (Android OpenSources.kt, FileMoverClient.fetch, pref jamendoclientid with no UI yet); a real download started from the phone app was not tested.

## Not done / open

1. **ntfy on hgs (pib)**: could not diagnose. `ssh hgs@192.168.1.159` and `hgs@hgs.tailb05910.ts.net` both answer "Permission denied (publickey,password)" from this PC and from frank; this PC's key is not authorised there (the user believed it was). Needs the user to add `~/.ssh/id_rsa.pub` (or run `docker ps -a | grep ntfy` and `tailscale serve status` themselves). The three FLACie notification types (now playing, download requested, completed) could therefore not be confirmed to arrive.
2. **Lidarr audit (read-only, nothing changed)**: 5 Prowlarr indexers (NZBgeek usenet prio 50; BitSearch, MixtapeTorrent, RuTracker.RU, The Pirate Bay torrent prio 25). Lidarr health warns BitSearch and RuTracker have been failing for over 6 hours. Download clients qBittorrent + SABnzbd. Quality profiles Any / Lossless (lossless only) / Standard (lossy only). Metadata profile "Standard" allows only primary type Album, so Singles/EPs are never wanted: likely why single-song requests find nothing. No release profiles. Not done: adding indexers (needs the user's Usenet/tracker accounts), qBittorrent tuning (WebUI needs the user's password; only port 6881 is mapped, router forwarding unknown), real download benchmarks.
3. Android: a settings field for the Jamendo client id; test a real in-app download of an open-source song (it writes a real file to the NAS music folder).
4. Cold start: the first screen now shows from `merged.json` about 3 s after launch on the slow emulator (was ~16 s); the full rebuild still takes ~10 s in the background there. Real phone not measured.
5. Known gaps from earlier: no phone/transcoding session in the admin Info popup; weak-signal streaming; iPod manager button in the Windows app; live graphs off on iPhone/iPad; Android admin dashboard just opens the web page. Info panel spectrum card is slightly clipped at the bottom on the 1080x2400 emulator (axis labels at the edge).

## Gotchas learned

- `dotnet run` serves an empty app.css; use a published build (`ipodsync/tools/dev/restart-web.sh`). To reuse an already approved account, run the published server with `FLACIE_DATA` pointing at an old data folder that has `users/` (the one in the earlier session's scratchpad `webdata`); a fresh data folder needs a Quick Connect approval from the user.
- Browser pane: `resize_window` can reset itself; `javascript_tool` times out at ~45 s; `Blazor.navigateTo(url)` keeps injected helpers. The browser pane can be closed by the user; `preview_start` with a url reopens it.
- adb in Git Bash needs `MSYS_NO_PATHCONV=1`; emulator screenshots are 1080x2400 shown at 900x2000 (x1.2 for taps). Back from the full-screen player twice leaves the app.
- Jellyfin keys a session by client name + device id; never change the auth header name on existing tokens.
- `sed` mangles `$` and quotes in Kotlin/C# edits: prefer the Edit tool.
- Git on Windows warns about LF/CRLF on every commit; harmless.

## How to build, test, deploy, release

- **Web dev**: `bash ipodsync/tools/dev/restart-web.sh`, then `http://127.0.0.1:5255/debug/login`; `/debug/token`, `/debug/jf?path=` for inspection.
- **Deploy to frank** (from `ipodsync/src`): `tar --exclude=bin --exclude=obj -czf - FLACie.Core FLACie.Server | ssh root@frank 'rm -rf /tmp/fw && mkdir /tmp/fw && tar --no-same-owner -xzf - -C /tmp/fw && cd /tmp/fw && docker build -q -f FLACie.Server/Dockerfile -t flacie-web:local . && cd /home/hms/docker/flacie-web && docker compose up -d --force-recreate'`, then `curl https://frank.tailb05910.ts.net:8443/healthz`.
- **Android**: `cd android; JAVA_HOME="/c/Program Files/Eclipse Adoptium/jdk-17.0.20.101-hotspot" ./gradlew :app:testDebugUnitTest :app:assembleRelease`. Emulator: `"$LOCALAPPDATA/Android/Sdk/emulator/emulator.exe" -avd flacie_phone -no-snapshot -gpu swiftshader_indirect -no-audio`.
- **Windows MSI**: `powershell -ExecutionPolicy Bypass -File ipodsync/tools/build-installer.ps1 -Out <dir>` (about 2 min); smoke test by launching `<dir>/app-windows/IpodSync.Maui.exe`, curl the `FLACie.Server` port `/healthz`, close the app (server must stop).
- **Version bump** (every release): `android/app/build.gradle.kts` (`versionCode`, `versionName`), `ipodsync/src/IpodSync.Maui/IpodSync.Maui.csproj` (`ApplicationDisplayVersion`, `ApplicationVersion`).
- **Release**: copy the APK as `FLACie-1.0-betaN.apk`, honest notes (Tested / Not tested), `gh release create v1.0.0-betaN --prerelease --target main --title "FLACie 1.0 beta N" --notes-file notes.md <apk> <msi>`.
