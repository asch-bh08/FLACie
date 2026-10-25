# Handoff: road to FLACie 1.0 (beta 100)

Updated after every step, so another session can take over at any point. Newest state first under "Now"; the night-by-night detail is in
`docs/waves/log.md`, the plan in `docs/superpowers/specs/2026-10-19-road-to-1.0-ux-design.md`.

## Now (2026-10-25, night)

- **Beta 90 released** (web on frank, rollback tag rollback-20261025-pre90; GitHub v1.0.0-beta90 with APK + MSI; Android versionCode 105).
- **Working on beta 91: the owner's speed overhaul** (owner AFK overnight, release b91 if optimised): 3-4 s gap between songs, covers not
  instant, Home 1-2 s, Listen again ~3 s with no VU, weak-signal / fast-skip / search smoothness, light on resources but never at
  the cost of speed. Also: long lyric lines lose their last word. Measure first (loadtest tools), fix, measure again.
  Done (committed, not released): SongHeadCache (server keeps the start of upcoming songs; Next 1526 -> ~50 ms with a 1.5 s first byte);
  pointer-rest warming of Home songs (data-warm); covers on screen load eagerly, big cover over the small one; Home shelves 530 -> ~120 ms
  (Track keys remembered) and reused per account; VU shape of the song picked first (look-ahead fetches known shapes only, known=1).
  Mock has /__stream?first=ms (first-byte delay). Lyrics cut-off fixed (web + Android). Beta 91 versions bumped (code 106, Windows 0.91.0/76),
  notes in docs/private/release-bodies/v1.0.0-beta91.md; releasing after the full suite.
  Android measuring: run-android.mjs (first-sync, skip-burst) can no longer find the Songs list on the current screens ("songs 0", "playback never
  started"): its navigation needs updating before phone speed can be measured. Covers part works: slow profile (300 ms) 0% at 1 s, 100% at 3 s.
  Next speed ideas for the phone: covers on a cold cache, search typing on a slow link.
- Android Automotive emulator `flacie_car` (Android 14 automotive x86_64) works for car testing: sign in to the mock at 10.0.2.2
  (toggle Wi-Fi if no route), then `am start -a android.car.intent.action.MEDIA_TEMPLATE --es android.car.intent.extra.MEDIA_COMPONENT
  com.ipodemu/com.ipodemu.playback.PlaybackService`. Screenshots need `-d <display id>` (dumpsys SurfaceFlinger --display-id).

## Earlier (2026-10-22)

- **Released:** beta 88 (web on frank, GitHub pre-release v1.0.0-beta88 with the APK, Android versionCode 103, Windows 0.88.0 / 73). Branch
  `ui/living-sleeve`, fast-forwarded to `main` at every release.
- **The owner said:** carry on towards beta 100 on your own; release betas as you go; skip the visual redesign (design I is done). Weekly usage limit is
  close, hence this file. Play Store is NOT part of 1.0 (GitHub only).
- **Done since:** the Windows installer builds again (`ipodsync/tools/build-installer.ps1 -Out <folder>`, ~4 min, needs the maui-windows workload);
  `FLACie-0.88.0-windows-x64.msi` (111 MB) is attached to the beta 88 release. It was built, not installed: a clean install on Windows is still
  the owner's check. Add the MSI to every release from now on (step 5b of the recipe).
- **2026-10-22 phone checks (owner's Fold7 over wireless adb, beta 88):** Retro Now Playing and menus fine; screen-reader labels fine.
  Bug found and fixed (committed, not released yet): the launch sync failed once and never retried, so Playlists/Favourites showed 0 and Retro
  said None. `SyncRetry` now retries it; verified on the phone (10 playlists). Also checked on the phone, all fine: seek frame filling in,
  sharing (link /s/…), multi-select. Beta 89 (versionCode 104, Windows 0.89.0/74) being released with this fix; web unchanged, not redeployed.
  Small gaps for a later beta: the phone's select bar has no Share (the web's does), and its Cancel chip is off-screen until the bar is scrolled.
- **2026-10-24, beta 90 in progress:** committed: bar buttons no longer overlap (1z-bar-fit), Windows app's extra Retro tab removed. Fresh Windows app
  tested with the owner's real library (Quick Connect, FLACIE_DATA in scratch): lyrics, VU ring, no freezing all fine. Android Auto tested on the
  Desktop Head Unit (`%LOCALAPPDATA%\Android\Sdk\extras\google\auto\desktop-head-unit.exe`, `adb forward tcp:5277 tcp:5277`, owner starts the head
  unit server; start the DHU with Start-Process, not from bash). Bug: the car kept "No items" when it connected before FLACie had loaded; fix written
  (PlaybackService tells the car to reload BrowseTree.LIVE on library/userData change), not yet built/tested: run after the web suite.
  Root cause found: `library.start()` was only called by MainActivity/IpodView, so a car-only start never loaded the library; PlaybackService now
  calls it. Owner asked for a much better Android Auto look: BrowseTree reshaped (tabs Home/Playlists/Albums/Artists with icons `drawable/ic_car_*`,
  Home = Shuffle all + Favourites/Downloaded + 12 recent songs under headings, grids of covers, playlist covers + "N songs", "artist · album",
  downloaded tick) with tests in BrowseTreeTest (written first, not yet run). Check on the DHU with FLACie force-stopped.
  Open: pressing Play on a Home "Listen again" card started a different song (Price Tag -> I Gotta Feeling), not investigated.
- **2026-10-24 later (owner: don't release yet, keep improving; test builds go straight to the phone, no suite):** committed, unreleased:
  Android Auto (cover grids work only when folders are not playable; Play all/Shuffle rows; Home = Shuffle all, all playlists as covers,
  Your music, Recently played; artists merged by primaryArtist; covers fetched while the car waits + logo placeholder; every opened list
  refreshed after load). Recently Played merges the web's profile "history" both ways (HistorySync). Web Home song buttons keyed by song
  (wrong-song race; 1z-home-pick). Phone select bar: Cancel first, Share added. Lyrics in Android Auto: not possible (Google's templates).
  The owner's car (2014 Clio) has no Android Auto; an aftermarket unit is coming. DHU at 1280x720 via ~/.android/headunit.ini.
  Beta 90 = all of this + bar-buttons + Windows Retro tab; full web suite not yet run on it.
  Then: car search fixed (onSearch must notifySearchResultChanged; lists playlists/artists/albums first), voice plays a playlist/artist/
  album by exact name (BrowseTree.searchPlay, voiceId), and car list requests wait for library.loaded (notifyChildrenChanged did not
  refresh an open playlist). Voice not tried on the DHU (needs a microphone).
  Then (owner: keep going with every feature; the phone will not be connected, use the emulator): CarBrowseTest added; car Shuffle/Repeat
  buttons; Shuffle all / Play all / Shuffle rows now actually play (BrowseTree.isPlayId); Play on the car's card resumes last time's queue.
- **Changelog** is up to date through beta 88 (generated from docs/private/release-bodies; keep adding each release).
- **What is left for 1.0 is mostly the owner's** (below). Everything on the plan's Foundations / Flows / Parity / accessibility / budget lists that can be
  built and tested here is done and released. Next session: ask the owner for the by-hand results, fix what they find, and release betas until they
  are happy; then 1.0 itself.

## The 1.0 release itself (when the owner says go)

- Versions: Android versionName "1.0" (versionCode +1), Windows ApplicationDisplayVersion 1.0.0; the GitHub release `v1.0.0` is NOT a pre-release
  (`gh release create v1.0.0 --latest ...`), with the APK and the MSI; web deployed as usual with a rollback tag.
- Release notes: a short "what FLACie is" for new people plus the changes since the last beta.

## Waiting on the owner (do not do without them)

- The checks in `docs/BY-HAND-CHECKS.md` (latest sections): Retro and sharing on a real phone, the seek frame filling in, home-network sign-in,
  Windows media keys (switch set in Platforms/Windows/App.xaml.cs, unchecked), TalkBack, multi-select.
- README screenshots from the owner's real library (test-data shots look wrong on a front page).
- `FLACIE_ADMINS` on frank: offered to set it to the owner only; not asked for yet. Today admin = Jellyfin administrator.
- The dad's account still holds favourites merged from the owner's phone before beta 86; beta 87's Clear all (with Undo) lets him tidy them.

## How to release (the recipe that works)

1. Bump `android/app/build.gradle.kts` (versionCode +1, versionName "1.0-betaN") and `ipodsync/src/IpodSync.Maui/IpodSync.Maui.csproj`
   (ApplicationDisplayVersion 0.N.0, ApplicationVersion +1).
2. Full web suite, alone on the machine (not while Gradle builds: the test server then fails to start):
   `cd ipodsync/tests/web-smoke && MOCK_PORT=8296 WEB_PORT=5390 FM_PORT=8297 timeout 5000 ./node_modules/.bin/playwright test --reporter=line`
   ~45 min, ~511 tests. Known long-run flakes have one retry (1e-hires, 1i-download-tags, 1m-ipod, 1g-burst, 1f-load, 1v-retro-touch); anything
   else failing: rerun that file alone before believing it.
3. `cd ipodsync && dotnet test tests/FLACie.Tests` (749; the Alexa 10k-song build test is timing-sensitive: run it on a quiet machine).
4. Android: `cd android && JAVA_HOME="/c/Program Files/Eclipse Adoptium/jdk-17.0.20.101-hotspot" ./gradlew :app:testDebugUnitTest :app:assembleRelease`
5. Emulator smoke (23/23): `powershell -NoProfile -File C:\FLACie-handoff\heavy.ps1 -Dir C:\FLACie\ipodsync\tools\loadtest -Cmd "powershell -NoProfile -File run-android.ps1 -Apk C:\FLACie\android\app\build\outputs\apk\release\app-release.apk -Label betaN -Driver smoke-android.mjs -Port 8297"`
5a. Stand-in car (what Android Auto is given; emulator only, never the owner's phone): boot `flacie_phone`, then
   `ANDROID_SERIAL=emulator-5554 ./gradlew :app:connectedDebugAndroidTest -PskipRetro` (CarBrowseTest, 8 checks). The emulator has no Android Auto
   (no Play Store), so the Desktop Head Unit needs the owner's phone.
5b. Windows installer: `cd ipodsync && powershell -NoProfile -File tools/build-installer.ps1 -Out <scratch folder>` → attach
   `FLACie-0.N.0-windows-x64.msi` to the release with the APK.
6. Commit named files only (NEVER `git add` a directory: test data folders hold tokens and keys), check `git show --stat HEAD`, push the branch,
   `git push origin HEAD:main`.
7. Web: `ssh root@frank 'docker tag flacie-web:local flacie-web:rollback-<date>-preN'`, then from `ipodsync/src`:
   `tar --exclude=bin --exclude=obj -czf - FLACie.Core FLACie.Retro FLACie.App FLACie.Server | ssh -o BatchMode=yes root@frank 'rm -rf /tmp/fw && mkdir /tmp/fw && tar --no-same-owner -xzf - -C /tmp/fw && cd /tmp/fw && docker build -q -f FLACie.Server/Dockerfile -t flacie-web:local . && cd /home/hms/docker/flacie-web && docker compose up -d --force-recreate'`,
   then `curl https://frank.tailb05910.ts.net:8443/healthz` must say "ok".
8. Notes in `docs/private/release-bodies/v1.0.0-betaN.md` (git-ignored, plain words, owner-facing), then
   `gh release create v1.0.0-betaN --prerelease --target main --title "FLACie 1.0 beta N" --notes-file ... FLACie-1.0-betaN.apk`.
9. One line in `docs/waves/log.md`, and update this file.

## Rules the owner set (keep them)

- Never type the owner's passwords; emulator only, never their phone; stop only processes you started (by port or PID); never use Haiku.
- Never two Playwright runs on the same ports at once (it clobbered results twice). Never commit `.data-real*`, `.pub-real*`, `run-real*.cmd`.
- Web Info graphs keep their method. TDD: a failing test first. Plain-words release notes.
- Specs that sign in many times must sort before `2-login` (its wrong passwords lock the address out for 15 minutes).

## Where things are

- Logo: `logo/flacie-master.png` (the owner's Gemini design, centred by eye); sizes in `wwwroot/icon-*.png`, `android/.../mipmap-*/ic_launcher_art.png`,
  `IpodSync.Maui/Resources/AppIcon/appicon.png`.
- Sharing: `FLACie.Core/ShareStore.cs`, `/api/share` (MediaEndpoints, browser or phone token via `ApiEndpoints.CallerAsync`), page `/s/{id}`.
- Favourites sync: `FLACie.Core/FavoriteSync.cs` and `android/.../library/FavoriteSync.kt` (same rule: latest of favouriting and removing wins).
- Android per-account device data: `UserData.clearPersonal`, `Prefs.dataOwner`.
