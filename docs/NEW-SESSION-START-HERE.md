# FLACie: start here (a fresh machine, a fresh session, no memory)

You are a new Claude Code session on a computer that has **never built this project**. You have no memory of earlier work: this repo and this file are all the context there is. Read this file fully, then `docs/HANDOFF.md` (newest sections first), `CHANGELOG.md` (what each release did) and `docs/TEST-LOG-beta40.md` (the latest test pass and what is still untested).

## 0. What this project is

FLACie is a music player that exists in three front ends that must stay in step (standing rule from the owner: **every feature ships on Android, web and Windows in the same release, or the release notes say why not**):

| Part | Where | Tech |
|---|---|---|
| Android app | `android/` (applicationId `com.ipodemu`) | Kotlin, Jetpack Compose, Media3/ExoPlayer. Two looks: "Modern" (YT-Music-like) and an iPod click-wheel look |
| Web app (FLACie Web) | `ipodsync/src/FLACie.Server` + shared logic in `ipodsync/src/FLACie.Core` | .NET 9 Blazor Server, one cookie-auth app, runs on the owner's server and inside the Windows app |
| Windows app | `ipodsync/src/IpodSync.Maui` | hosts FLACie.Server as a child process; installer via WiX (`ipodsync/tools/build-installer.ps1`) |
| iPod sync engine | `ipodsync/src/IpodSync.Engine` (+ `IpodSync.Core`) | NativeAOT .NET library used by the Android app; prebuilt `.so` is already committed under `android/app/src/main/jniLibs/arm64-v8a`. Only rebuild if Core changes (needs WSL + NDK, see `ipodsync/tools/build-engine-android.sh`; you will almost certainly not need it) |

Backends, all on the owner's home server **frank** (Tailscale name `frank`, reach it with `ssh root@frank` once your key is authorised; plain `frank` is refused): Jellyfin (library, accounts, sessions; `http://100.114.148.48:8096` over Tailscale), slskd (Soulseek), Lidarr, a Python "file mover" (`/home/hms/docker/filemove/server.py`), a yt-dlp service (`flacie-ytdl`), and our own container `flacie-web`. The music lives on a NAS share (`\\192.168.1.183\Movies`). Public web: `https://frank.tailb05910.ts.net:8443`.

## 1. This machine has nothing installed. Install this first

Before anything else, check what is missing (`java -version`, `dotnet --list-sdks`, `git --version`, `gh --version`, `adb version`) and install it all (winget works on Windows 11):

1. **Git** and the **GitHub CLI** (`gh auth login`: the owner must approve the browser login; never ask them for a token).
2. **JDK 17** (Eclipse Temurin 17). Set `JAVA_HOME` to it. Gradle in this repo needs 17.
3. **Android SDK**: easiest is **Android Studio** (it installs the SDK, `platform-tools`/adb, the emulator and an AVD manager). You need: Platform **34** (compileSdk; check `android/app/build.gradle.kts` for the exact `compileSdk`/`targetSdk`), Build-Tools matching it, `platform-tools`, `emulator`, and one system image (**android-34 google_apis x86_64**). Create an AVD named `flacie_phone` (Pixel 6). Then create `android/local.properties` containing `sdk.dir=C:\\Users\\<you>\\AppData\\Local\\Android\\Sdk` (this file is git-ignored). Enable Windows Hypervisor Platform so the emulator is fast.
4. **.NET 9 SDK** (`winget install Microsoft.DotNet.SDK.9`). For the Windows installer also: **WiX 5** (`dotnet tool install --global wix --version 5.*`) and the WiX UI/Util extensions the script asks for; the MAUI Windows workload may be requested by `IpodSync.Maui` (`dotnet workload restore`).
5. Nice to have: **ffmpeg/ffprobe** (checking audio files), **Tailscale** (logged in to the owner's tailnet: without it you cannot reach Jellyfin, frank or the NAS; the owner must approve the device), an **SSH key** for `root@frank` (generate one; the owner adds the public key to frank; do not try passwords).
6. If you will deploy: `ssh root@frank` must work. If it does not, build and test locally and say that deploying was not possible.

Then build once to prove the toolchain: `cd android && ./gradlew.bat :app:assembleRelease` (first run downloads Gradle and dependencies, several minutes) and `cd ipodsync && dotnet build src/FLACie.Server`.

> **Where this lives:** the owner copied the repo to the network share `Z:lacie` (`\raspberrypi-1lacie`) so it can be picked up from another pc. building on a network share is slow and git may refuse it ("dubious ownership": run `git config --global --add safe.directory '*'`). faster: `git clone https://github.com/asch-bh08/flacie.git c:lacie`, copy `z:lacie_machine-files` next to it, and work there. always push to github when you finish: that is the source of truth.

## 2. files that are not in git (the previous machine had them)

They are in `_machine-files/` inside this repo folder (git-ignored, see the README there):

- **`debug.keystore`**: the Android build is signed with the previous PC's *debug* key. The owner's phone has the app installed with that signature. **If you build with a different key, "install over the top" fails (signature conflict) and the phone would need an uninstall, which wipes its local data.** Copy it to `%USERPROFILE%\.android\debug.keystore` BEFORE the first Android build.
- **`webdata/`** (`users`, `keys`, `device-id`): a local web-server data folder with the owner's Jellyfin sign-in already approved, so the dev server can sign in through `/debug/login` without a new Quick Connect approval. Copy it to `~/flacie-dev/webdata`. Treat it like a password: never commit it or paste it anywhere.
- `local.properties` is machine specific: write your own.

## 3. Rules from the owner (they apply to you, with no exceptions)

- **Never type the owner's real passwords or API keys anywhere.** Sign-in to Jellyfin is by Quick Connect and the owner approves the code. Never create accounts for them.
- **Never write to a real iPod.** Never delete files on the NAS (additive only; downloads are fine, deletions are not). Test files you create are listed in `docs/TEST-LOG-beta40.md`; leave them.
- On frank, only touch **our own `flacie-web` container** (redeploy command below) and, if needed, Tailscale Funnel port 8443/443 after telling the owner. Do not restart or reconfigure the other services. The Tailscale serve config backup is `/root/tailscale-serve.bak-2026-10-05.json` on frank.
- Commit and push to `main` as you go. Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. Publish releases only after building and testing. **Say plainly what was and was not tested.** Never claim a deploy or release that did not happen.
- Releases are marked `--latest` (not pre-release) but are named and versioned as betas ("FLACie 1.0 beta N"); the owner asked for that. Every release has an APK and an MSI attached and honest "Tested / Not tested" notes; `CHANGELOG.md` gets the same notes added at the top (edit the file in the repo root, not `android/`).
- Do not change identifiers that key saved data: the Jellyfin client name `ipodplayer` (DisplayPreferences key `ipodplayer.profile` AND the auth header). Renaming it once split Jellyfin sessions and broke the Devices page.
- Be token-efficient. The owner often sends short messages with typos and goes away for hours ("don't ask me questions"): finish, verify, release, report honestly. Things needing their password or account (ntfy server, indexer accounts, qBittorrent) are reported as blocked, not faked.

## 4. What the owner cares about (how work is judged)

They judge by looking at their phone (a Samsung **Galaxy Z Fold 7**: cover screen, unfolded ~square screen, split-screen windows) and at the browser. The reference looks are **YouTube Music** and the **Jellyfin dashboard**. Recurrent complaints: cramped, messy, clipped, truncated text with room to spare, tags not on one line, laggy scrolling, things that "snap back" when swiped. Prefer fewer, bigger, calmer elements; put detail behind an Info button. Test visually at several sizes (see below), not just "it compiles".

## 5. Day-to-day commands

Run from the repo root unless stated. Use Git Bash or PowerShell as you prefer; in Git Bash set `MSYS_NO_PATHCONV=1` for adb paths.

- **Android build**: `cd android; $env:JAVA_HOME='<jdk17>'; ./gradlew.bat :app:assembleRelease` then `adb install -r app/build/outputs/apk/release/app-release.apk`. Do not pipe gradle through `grep -q`/`-q` until you have seen it succeed once, and check the APK timestamp (a failed build leaves a stale APK).
- **Emulator**: `emulator -avd flacie_phone -no-snapshot -gpu swiftshader_indirect -no-audio`; it is software-rendered (~60 ms per frame), so it cannot tell you about smoothness. It is signed in to the owner's REAL Jellyfin account: do not tap destructive things (Remove dupes, Delete). Window-size testing: `adb shell wm size 1080x1155; adb shell wm density 420` (split screen) and `wm size reset; wm density reset` afterwards.
- **Web dev**: `bash ipodsync/tools/dev/restart-web.sh` (publishes, runs on `http://127.0.0.1:5255`; **a published build is required, `dotnet run` serves an empty stylesheet**). Sign in with `http://127.0.0.1:5255/debug/login` (needs the webdata folder from section 2). `FLACIE_DEBUG=1` is set by the script and enables `/debug/token`, `/debug/jf?path=`, `/debug/library`. Quick compile check: `dotnet build ipodsync/src/FLACie.Server`.
- **Responsive sweep (web)**: `ipodsync/tools/dev/sweep.js` and the iframe method in `docs/TEST-LOG-beta40.md` (create iframes of each route at each window size from one same-origin page and measure overflow and tap-target sizes).
- **Deploy web to frank** (from `ipodsync/src`): `tar --exclude=bin --exclude=obj -czf - FLACie.Core FLACie.Server | ssh root@frank 'rm -rf /tmp/fw && mkdir /tmp/fw && tar --no-same-owner -xzf - -C /tmp/fw && cd /tmp/fw && docker build -q -f FLACie.Server/Dockerfile -t flacie-web:local . && cd /home/hms/docker/flacie-web && docker compose up -d --force-recreate'`, then `curl https://frank.tailb05910.ts.net:8443/healthz` must say `ok`.
- **Windows installer**: `powershell -ExecutionPolicy Bypass -File ipodsync/tools/build-installer.ps1 -Out <dir>` (about 3 minutes; run in the background; **wait until the log prints `msi:` and the file stops growing before uploading it**: once an upload started while the MSI was still being written and failed). Smoke test: launch `<dir>/app-windows/IpodSync.Maui.exe`, `curl` its `FLACie.Server` port `/healthz`, close the app and check the server process went away.
- **Version bump** (every release): `android/app/build.gradle.kts` (`versionCode`, `versionName`) and `ipodsync/src/IpodSync.Maui/IpodSync.Maui.csproj` (`ApplicationDisplayVersion` and the integer `ApplicationVersion`, which must only ever go up). Current: Android versionCode 54 / `1.0-beta39.5`, MSI 0.39.5 / ApplicationVersion 40. The next real release is **beta 40**: use versionCode 55, MSI 0.40.0 and ApplicationVersion 41.
- **Release**: copy the APK as `FLACie-1.0-betaN.apk`, then `gh release create v1.0.0-betaN --latest --target main --title "FLACie 1.0 beta N" --notes-file notes.md <apk> <msi>`; add the notes to the top of `CHANGELOG.md`; push.

## 6. State right now (2026-10-07)

Latest release: **v1.0.0-beta39.5** (Android 54, MSI 0.39.5). The web side of it is deployed on frank. `main` is clean and pushed.

Unfinished: the owner's **beta 40 brief** was only partly done. Remaining from it, in the owner's words, "test everything, down to the last CSS overlap": the full feature test on Android and web (Explore with the quality filter, Search options, every Settings field, charts, Jams, notifications, Admin, playlist import, the Downloads page), startup time and memory on Android, removing dead code / duplicate UI / unused settings, a smoke test of each build, then release beta 40 and redeploy frank. `docs/TEST-LOG-beta40.md` lists what was done and what was not. Known gaps and ideas:

- ntfy notifications: the owner's ntfy host (tailnet node `hgs`, "pib") has never answered from this network; FLACie's side (Admin > Notifications) is built; blocked on the owner.
- Android Explore/Search/Settings/Downloads/charts/Jams have not been walked through in the latest test pass.
- Android Home scroll smoothness is unmeasured on the real phone (the emulator cannot show it).
- The Soulseek "filing" retry and the longer Lidarr wait (in `FLACie.Core/Downloads.cs`) fix failures seen in the logs but were not reproduced.
- "Resampled by device" in the Info Playback row is accurate: Android's mixer resamples; only a USB DAC with Android 14+ bit-perfect mode avoids it, and the app does not switch that on.
- The A-Z letter rail on web Explore is smaller than 44 px per letter by nature (a scrubber).
- Android player layouts live in `android/app/src/main/java/com/ipodemu/player/NowPlayingModern.kt` (single column, short window, side-by-side) and `UpNextDrawer.kt` (swipeable pages, queue, tabs); the web player is `ipodsync/src/FLACie.Server/Components/Shared/NowPlaying.razor` and `wwwroot/app.css` / `flacie.js`.

## 7. Gotchas that cost time before

- `sed` and heredocs mangle `$` and quotes in Kotlin/C#: prefer your editor tool for code edits. A greedy regex once ate half a file; always check `git diff` after scripted edits.
- Jellyfin keys a session by client name + device id. Check `/Sessions` after touching auth headers.
- Git on Windows prints LF/CRLF warnings on every commit: harmless.
- The browser pane (if you have one) resets emulated sizes between turns; the javascript tool times out at ~45 s.
- Deezer's chart/top endpoints return empty; Soulseek `searchTimeout` is in milliseconds (15 ends every search instantly); WMA/APE/WV files need a server-side conversion; ALAC may have no decoder on some phones (the player falls back to the server's conversion).
