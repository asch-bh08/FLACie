# Handoff: road to FLACie 1.0 (beta 100)

Updated after every step, so another session can take over at any point. Newest state first under "Now"; the night-by-night detail is in
`docs/waves/log.md`, the plan in `docs/superpowers/specs/2026-10-19-road-to-1.0-ux-design.md`.

## Now (2026-10-22)

- **Released:** beta 88 (web on frank, GitHub pre-release v1.0.0-beta88 with the APK, Android versionCode 103, Windows 0.88.0 / 73). Branch
  `ui/living-sleeve`, fast-forwarded to `main` at every release.
- **The owner said:** carry on towards beta 100 on your own; release betas as you go; skip the visual redesign (design I is done). Weekly usage limit is
  close, hence this file. Play Store is NOT part of 1.0 (GitHub only).
- **In progress:** checking that the Windows installer (MSI) still builds (none has been built since beta 71).
- **Next candidates** (in order): MSI build check → release-candidate prep (CHANGELOG 81-88, version 1.0 wording) → anything the owner reports.

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
