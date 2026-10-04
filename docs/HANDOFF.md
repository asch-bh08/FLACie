# FLACie handoff (written 2026-10-04, evening)

Repo: `C:\ipodemu` (GitHub asch-bh08/FLACie). `android/` = Kotlin/Compose/Media3 app. `ipodsync/` = .NET 9: `src/FLACie.Core`, `src/FLACie.Server` (FLACie Web, Blazor Server), `src/IpodSync.Maui` (Windows host: runs FLACie.Server in a WebView + iPod manager), `src/IpodSync.Engine` (NativeAOT iPod engine).
This file lives on branch `wip/beta11-fixes`. `main` = what beta 10 was built from (commit a74b9d2). The working tree of this branch has UNTESTED fixes on top (see "In the working tree").

## The prompt to give the next Claude Code session

> You are picking up FLACie work in `C:\ipodemu`. Read `docs/HANDOFF.md` on branch `wip/beta11-fixes` first (check that branch out), then your memory files (`C:\Users\Ashle\.claude\projects\C--ipodemu\memory\MEMORY.md` and what it links, especially `flacie-session-log-2026-10-04`, `flacie-working-style`, `flacie-web-testing`, `ipodsync-repo-and-release`, `homelab-ntfy-hgs`). Do the "Next steps" in order. The user is present, impatient, and judges by what they SEE on their phone and browser: test every change visually (browser pane / emulator) before saying it works, say plainly what was and was not tested, and never claim a release or deploy that you did not do. Standing rules: never type the user's real passwords/API keys anywhere (Quick Connect only, they approve codes); never write to a real iPod; never delete files on the NAS; do not restart or reconfigure services on frank other than our own `flacie-web` container; commit/push to main and publish releases only once something is verified; commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`; be token-efficient.

## State of the world

- **Releases (all GitHub pre-releases):** v1.0.0-beta7 (assets were re-uploaded over), beta8, beta9, beta10 (`https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta10`). Android versionCode 24 / "1.0-beta10"; Windows MSI 0.10.0 (`ApplicationVersion` 10). Beta 10 = commit a74b9d2.
- **frank** (`https://frank.tailb05910.ts.net:8443`, container `flacie-web`, redeployed many times; last deploy = commit aa80fe1 content). **frank currently has the Devices bug below.**
- **Emulator** AVD `flacie_phone` is signed in with the user's account (NAS guest + Jellyfin profile; admin = `hms`). adb needs `MSYS_NO_PATHCONV=1` and `C:/` paths in Git Bash. Screenshots: `adb exec-out screencap -p > file.png`; the screenshot is 900x2000 displayed for a 1080x2400 screen (multiply by 1.2 for taps).

## URGENT bug: Devices tab shows no other devices (caused by me)

Cause: in commit a928a99 I changed the Jellyfin auth header `Client="ipodplayer"` to `Client="FLACie"` (server `JellyfinClient.Auth` and three Android headers: `AccountSync.kt`, `Jam.kt`, `JellyfinConnect.kt`) so Jellyfin's dashboard would not say "ipodplayer". Jellyfin keys a session by client name + device id: after the rename the playback reports go to a NEW session ("FLACie") that has no websocket, so it is not remote-controllable (`SupportsRemoteControl=false`) and the Devices page (`Connect.LoadSessionsAsync`, `ControllableByUserId`) filters it out; the old "ipodplayer" session (which has the websocket) shows "Nothing playing". Evidence: `/Sessions` showed paired entries `FLACie Web / FLACie / false` and `FLACie Web / ipodplayer / true`.
Fix (in the working tree, not deployed, not tested end to end): `JellyfinClient.ClientName = Client` and the Android headers back to `$CLIENT` / `${AccountSync.CLIENT}`.
Consequences: Jellyfin's own dashboard keeps saying "ipodplayer" for our devices. Do NOT try to rename it again by changing the header on existing tokens. The only safe way is to use the new name at sign-in time (token creation) and remember it per account. Installed beta 9/10 phones send "FLACie" and will have split sessions until they update to beta 11.

## In the working tree of this branch (compiles; NOT verified)

1. The client-name revert above (server + 3 Android files).
2. `ipodsync/src/FLACie.Server/wwwroot/flacie.js`: the web live graphs/dB meters now tap the sound AFTER the volume (`outGain.connect(a/up)`), so they react to the volume (user: the dB readout "is fake because it doesn't change when I turn the volume down"). Earlier I had deliberately made them volume independent.
3. Android `LiveAudio.kt` (`LiveAnalysis(live, gain)`) + `InfoPanel.kt`: graphs scaled by `player.volume * music stream volume / max`.
4. Android Now Playing (`NowPlayingModern.kt`): Sleep timer button removed from the round-button row (it still exists in Settings > Playback); `Controls` has 16 dp spacing; action row `SpaceEvenly`.
5. `PlayerNav.openQueueFromPlayer()` (PlayerUi.kt, Modern.kt, NowPlayingModern.kt, NowPlayingUi.kt): Back from the Up next screen returns to the full-screen player instead of Home.
6. `ipodsync/tools/dev/restart-web.sh` and `sweep.js` (dev helpers, see below).

## Next steps, in order

1. Verify and ship the Devices fix: run `ipodsync/tools/dev/restart-web.sh`, sign in via `/debug/login`, play on 2 browser tabs and the emulator, open `/devices` and `/admin`: other devices must appear with now playing + working controls. Then deploy to frank (command below).
2. Android Now Playing, requested by the user and NOT started: make the action buttons like YouTube Music's: a **horizontally scrollable row of pills above the seek bar and play/pause** (Lyrics, Info, Up next, Equalizer, Add to playlist, More), replacing the round buttons under the transport. Keep it uncluttered. Test on the emulator at 1080x2400 and a short screen.
3. Android **Home screen bottom buttons feel cramped/awkward** (bottom nav `BottomNav`/`NavItem` in `player/Modern.kt` and the mini player at the bottom of `PlayerUi.kt`): give them breathing room, consistent sizes, YT Music feel. Look at screenshots, not just code.
4. **Swiping the app away from recents must stop playback.** `playback/PlaybackService.kt` `onTaskRemoved` currently stops only if nothing is playing ("Swiping the app away keeps music going" was a deliberate comment). Change it to pause, release/stop the player and `stopSelf()` (the user explicitly wants it to stop). Check the notification disappears and that Bluetooth/lock-screen controls do not resurrect it.
5. Verify items 2 to 5 of "In the working tree" on the emulator (dB meters shrink when the volume goes down; Back from Up next returns to the player; no sleep timer button).
6. Release **beta 11** (version bump spots below) only after the above is verified on the emulator, and say what was not tested.
7. ntfy: the user's ntfy (host "pib" = tailnet node `hgs`, `hgs.tailb05910.ts.net`, LAN 192.168.1.159) is down; see memory `homelab-ntfy-hgs`. Needs `docker ps -a | grep -i ntfy` and `tailscale serve status` output from that machine (we have no login there). FLACie's side is done: Admin page > Notifications.

## Features shipped this session (where the code is)

- **Search ranking**, autoplay with different songs + fetch-ahead, YT-Music style Home (Listen again, Quick picks via taste profile `FLACie.Core/Recommender.cs`, Daily Mix, moods), background playlist import (`ImportManager`, `/api/import`), daily charts (`ChartsService`), storage view, playlists cleaned (no "(LAC)"), compilation album covers (`Art.OnCompilation`, `MediaEndpoints.OnlineAlbum`).
- **Explore** (web `Pages/Songs|Albums|Artists|Genres|Charts.razor` + `ExploreBar`, `ExploreState`; Android `player/ExploreUi.kt`): filters, A-Z, charts tab via `/api/charts/{id}`.
- **Info graphs** (web `Shared/TrackInfoPanel.razor` + `wwwroot/flacie.js`: live spectrum, LED visualizer, scrolling spectrogram, loudness, stereo; Android `player/InfoPanel.kt` + `playback/LiveAudio.kt` using a `TeeAudioProcessor` in `PlayerController`'s renderers factory).
- **Admin dashboard** `/admin` (`Pages/Admin.razor`, `AdminAccess`, `ClientRegistry`/`WebClient` registered from `PlayerBar`, `ActivityLog`, `Notifier` for ntfy). Admin = Jellyfin admin or the names in env `FLACIE_ADMINS`. Settings storage/downloads/charts and the phone's `/api/settings` writes are admin only.
- **Web library speed**: parallel Jellyfin paging + saved copy `data/library/<id>.json`; Home waits for the profile; nothing is saved to the account until the profile has been read (`UserSession.ProfileLoaded`).
- **Web player layout** (`wwwroot/app.css` bottom sections): phone/portrait layout like YT Music with tabs that open a sheet; landscape/short-window rules; tab bar; mini player.
- **Android**: server settings + Account page via `FlacieWebClient`, Explore, Info panel, Admin dashboard row (opens the web page).

## Gotchas learned

- `dotnet run` serves an empty app.css; always use a published build (`restart-web.sh`). Static assets: when launching the published exe by hand, set the working directory to its folder.
- Browser pane viewport emulation: after `resize_window` the pane may reset on its own; `javascript_tool` calls time out after about 45 s, so split long sweeps. `Blazor.navigateTo(url)` navigates without reload (keeps injected helper functions).
- Do not `scrollIntoView` inside `.np` (it scrolls overflow:hidden containers); the np container is `overflow: clip`.
- CSS name clashes bit me: `.pick` (Home quick picks, width 100%), `.play` (the big white play button), `.stat`, `.meter`; prefix new classes.
- `sed` and heredocs mangle apostrophes and `$`: write files with the Write tool.
- Git on Windows warns about LF/CRLF on every commit; harmless.
- Playlists: the phone mirrors profile playlists to Jellyfin; the profile must win (`mergeJellyfin` only fills empty playlists).

## How to build, test, deploy, release

- **Web dev**: `bash ipodsync/tools/dev/restart-web.sh` (add `FLACIE_ADMINS=nobody` to test as a non-admin), then `http://127.0.0.1:5255/debug/login`; `/debug/token` returns the account token (loopback) for `/api/*` calls; `/debug/jf?path=...` proxies Jellyfin. Responsive sweep: paste `ipodsync/tools/dev/sweep.js` into the console.
- **Deploy to frank** (from `ipodsync/src`): `tar --exclude=bin --exclude=obj -czf - FLACie.Core FLACie.Server | ssh root@frank 'rm -rf /tmp/fw && mkdir /tmp/fw && tar --no-same-owner -xzf - -C /tmp/fw && cd /tmp/fw && docker build -q -f FLACie.Server/Dockerfile -t flacie-web:local . && cd /home/hms/docker/flacie-web && docker compose up -d --force-recreate'` then `curl https://frank.tailb05910.ts.net:8443/healthz`.
- **Android**: `cd android; JAVA_HOME="/c/Program Files/Eclipse Adoptium/jdk-17.0.20.101-hotspot" ./gradlew :app:testDebugUnitTest :app:assembleRelease` -> `app/build/outputs/apk/release/app-release.apk` (signed with this PC's debug key, so it installs over earlier builds). Emulator: `"$LOCALAPPDATA/Android/Sdk/emulator/emulator.exe" -avd flacie_phone -no-snapshot -gpu swiftshader_indirect -no-audio`.
- **Windows MSI**: `powershell -ExecutionPolicy Bypass -File ipodsync/tools/build-installer.ps1 -Out <dir>` -> `FLACie-<ApplicationDisplayVersion>-windows-x64.msi` (about 3 minutes). Test by launching `<dir>/app-windows/IpodSync.Maui.exe`, finding the `FLACie.Server` process port, curling `/healthz`, then closing the app (the server must stop with it).
- **Version bump** (every release): `android/app/build.gradle.kts` (`versionCode`, `versionName`), `ipodsync/src/IpodSync.Maui/IpodSync.Maui.csproj` (`ApplicationDisplayVersion`, `ApplicationVersion`).
- **Release**: copy the APK as `FLACie-1.0-betaN.apk`, write honest notes (what was tested / not tested), `gh release create v1.0.0-betaN --prerelease --title "FLACie 1.0 beta N" --notes-file notes.md <apk> <msi>`.

## Known gaps (from the beta 10 notes)

ntfy not reachable; no phone/transcoding session seen in the admin Info popup; weak-signal streaming and the lower-bitrate fallback; a full day of daily charts; the iPod manager button in the Windows app; the iPod engine on the emulator; Android Explore says "No songs yet" for a few seconds on a cold start; live graphs are off on iPhone/iPad; the web player's new layout is not in the Android player screen (which is its own design); the Android admin dashboard just opens the web page.
