# Handoff: road to FLACie 1.0 (beta 100)

Updated after every step, so another session can take over at any point. Newest state first under "Now"; the night-by-night detail is in
`docs/waves/log.md`, the plan in `docs/superpowers/specs/2026-10-19-road-to-1.0-ux-design.md`.

## Now (2026-10-22)

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
