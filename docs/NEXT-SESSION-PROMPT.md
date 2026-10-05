# Prompt for the next Claude Code session (paste this whole file)

You are continuing work on **FLACie** in `C:\ipodemu` (GitHub asch-bh08/FLACie, branch `main`). Read `docs/HANDOFF.md` first (top sections = newest), then the memory files in `C:\Users\Ashle\.claude\projects\C--ipodemu\memory\` (start with `MEMORY.md`).

## What it is
A music player in three front ends that must stay in step (standing user rule: every feature ships on Android, web and Windows, or say why not):
- `android/` Kotlin/Compose/Media3 app (Modern theme + an iPod "wheel" theme).
- `ipodsync/src/FLACie.Server` Blazor Server web app (also hosted inside the Windows app, `IpodSync.Maui`), `FLACie.Core` shared logic.
- Backends on the homelab `frank` (ssh `root@frank`): Jellyfin (library, sessions), slskd + file mover + yt-dlp service (downloads), Lidarr. Web is deployed with the tar|ssh|docker command in `docs/HANDOFF.md` ("Redeploy to frank"). Latest release: **v1.0.0-beta23** (Android 37, MSI 0.23.0).

## Build / test (gotchas that cost time)
- Android: `cd android; export JAVA_HOME="/c/Program Files/Eclipse Adoptium/jdk-17.0.20.101-hotspot"; ./gradlew.bat :app:assembleRelease` (then `adb install -r app/build/outputs/apk/release/app-release.apk`; emulator `emulator-5554` is signed in to the user's REAL Jellyfin account, so never tap destructive things like Remove dupes/Delete). Run gradle WITHOUT piping through grep until you have seen it succeed once; check the APK timestamp. Do not combine `export JAVA_HOME=... MSYS_NO_PATHCONV=1` in one export. The emulator renders in software (~60 ms/frame even on Settings): it cannot tell you about jank.
- Web: `ipodsync/tools/dev/restart-web.sh` publishes and runs on :5255; reuse an approved data folder (`~/flacie-dev/webdata`), sign in at `/debug/login` in the browser pane. `dotnet build src/FLACie.Server` to check compile.
- Release: bump `versionCode/versionName` in `android/app/build.gradle.kts` and `ApplicationDisplayVersion/ApplicationVersion` in `ipodsync/src/IpodSync.Maui/IpodSync.Maui.csproj`; `ipodsync/tools/build-installer.ps1 -Out <dir>` for the MSI (about 3 min, run in background); `gh release create vX --prerelease --target main --notes-file ... apk msi` with honest Tested / Not tested notes. `CHANGELOG.md` lists every release (regenerate it from `gh release view` after a release).
- Rules from the user: never type real passwords/keys, never write to a real iPod, never delete NAS files, only restart `flacie-web` on frank, say plainly what was not tested, commit and push as you go, no questions unless truly blocked.

## State right now (uncommitted work was committed with this file)
Done and released through beta 23: real audio facts index (`FormatIndex`, `/api/audiofacts`), Nyquist spectrum + native-rate graph twin + Playback row, playlist profile merge web<-phone (`ProfileSyncService`), Android playlist editing, Explore redesign + filter sheet, remote player (`RemoteUi.kt`), Android redesign to the web palette (`Palette` in `Modern.kt`, flat near-black, pink accent, switches, Animations toggle), safer touch defaults, Quick picks list.

Written after beta 23 and NOT yet released or tested on a device (compiles):
1. **Home scroll lag fix**: `HomeScreen` in `PlayerUi.kt` now builds shelves in the background (`HomeData`/`HomeCache`) instead of shuffling/sorting the whole library on the main thread (that was the likely stutter on the user's phone, a Galaxy Fold `SM-F966B`).
2. **Quick picks layout** (`QuickPicks` in `ForYouUi.kt`): width follows the screen (1 column with a sliver of the next page, 2-3 pages side by side on wide screens), snap fling.
3. **System bars** (`MainActivity.applySystemBars`): the Modern theme now keeps the status and navigation bars visible (it used to be fully immersive, so there was no gesture bar/Home button to swipe out of the full-screen player); only the click-wheel iPod view stays full-bleed. NowPlaying gets 20 dp extra bottom space. TODO: call `activity.applySystemBars()` when the theme changes (a `LaunchedEffect(ui.uiTheme, ui.viewMode, ui.model)` in `AppRoot.kt`), check insets look right on the emulator and a Fold-size layout, then verify nothing sits under the nav bar in the full-screen player, mini player and bottom nav.

## User's open complaints (verify each on a build, then release as beta 24)
- Home still lagged when scrolling on their phone (see 1; if it persists, profile with `adb shell dumpsys gfxinfo` on a real device or add Compose tracing; check `AlbumCard`/`MixCard` images, nested LazyRows, `TrackRow` work).
- Quick picks spacing (see 2).
- Full-screen player buttons too close to the bottom; must be able to swipe out / use Home button (see 3).
- Not reproduced: "album for Meet Me Halfway is wrong" (Jellyfin tag "The E.N.D" matches its folder; ask which screen if it comes up again).
- Windows installer never test-installed.

## First steps
1. `git pull`, read `docs/HANDOFF.md`, build the Android release APK, install on the emulator, screenshot Home / full-screen player / Settings.
2. Finish item 3, build, test, release beta 24 (APK + MSI), deploy web if Server changed, update `docs/HANDOFF.md` and `CHANGELOG.md`, push.
