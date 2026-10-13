# FLACie: pick-up guide for the next dev session (written 2026-10-12, after beta 55)

Public-safe. Private details (server names, addresses, how the owner's server is deployed to) are in `docs/private/HANDOFF-2026-10-12.md` (git-ignored; also in the handoff folder on the Pi share). Read `docs/test-run/OVERNIGHT-SUMMARY-b52-b55.md` first: it says what happened since beta 51 and what is NOT done.

## State
- **Update 2026-10-13: v1.0.0-beta59** (Android 74, MSI 0.59.0): the media session hides the queue from Bluetooth/Auto/lock screen (SessionWindow). Not tested on a real Bluetooth device.
- **Update 2026-10-13: v1.0.0-beta58 is released** (Android 73, MSI 0.58.0): Settings fix in the iPod Player skin, regression tests for beta 57, see `docs/OVERNIGHT-LOG.md` and the by-hand list `docs/BY-HAND-CHECKS.md`. Specs: `docs/spec/player-redesign.md`, `ipod-mode-redesign.md` (not built). Tests: Android 309 (2 skipped), .NET 257, Playwright 133.
- **Update 2026-10-12 (late): v1.0.0-beta56 is released** (Android 71, MSI 0.56.0, Web 1.0-beta56; cold start from a saved Home, covers that all arrive, wrong album covers vetted; see `docs/test-run/K-coldstart-covers.md`). The next work is the programme in `docs/spec/close-the-gaps.md` (spec only, tracked as GitHub issues with the label `programme`, milestones Phase 0-5). Tests at beta 56: Android 302 (2 skipped), .NET 257, Playwright 133.
- Previous: **v1.0.0-beta55** (Android 70, MSI 0.55.0, FLACie Web 1.0-beta55).
- Releases and notes: `gh release list`; `docs/release-notes-beta5x.md`; `CHANGELOG.md`.
- Tests at the end: .NET 250, Playwright 133, Android unit 280 (2 skipped). All pass.
- Branches `beta54/server` and `beta54/android` were merged; `beta52/*`, `fix/*` branches are history.

## Layout
- `android/` Kotlin app (Compose, Media3). `ipodsync/src/FLACie.Core` + `FLACie.Server` = web server + web UI (Blazor Server), embedded by the Windows app (`IpodSync.Maui`). `ipodsync/tests/FLACie.Tests` xunit; `ipodsync/tests/web-smoke` Playwright with a mock Jellyfin (`mock-jellyfin.mjs`). `ipodsync/tools/loadtest` the 10,000-song mock, the web/Android load drivers, `skip-playlist.mjs` (one Next press in a playlist, timed), `report.mjs`. `ipodsync/tools/filemove` the file mover service.
- Where the load work lives: `FLACie.Core/JellyfinGate.cs`, `JellyfinClient.cs` (listing, covers), `UserSession.cs` (delta sync), `FLACie.Server/LibrarySyncService.cs`, `CoverStore.cs`, `CacheService.cs`, `FormatProbeService.cs`; Android `library/JellyfinGate.kt`, `JellyfinNet.kt`, `JellyfinSync.kt`, `CoverStore.kt`, `ArtCache.kt`.
- Where the download tags live: web `FLACie.Core/DownloadTags.cs`, `FLACie.Server/DownloadManager.cs` (`StatusOf`, `POST /api/downloaded` in `ApiEndpoints.cs`), `Components/Shared/DownloadTag.razor`, `wwwroot/flacie.js` (`flacieDl`); Android `library/DownloadTags.kt`, `UpNextMerge.kt`, `DownloadReport.kt`, `player/DownloadTagUi.kt`.

## Build and test (Windows PowerShell; the dev PC has 16 GB: ONE heavy command at a time)
- Android: `cd android; $env:JAVA_HOME='<JDK 17>'; .\gradlew.bat :app:testDebugUnitTest :app:assembleRelease --console=plain` (about 5 min). `android\local.properties` needs `sdk.dir=C\:\\Users\\<you>\\AppData\\Local\\Android\\Sdk`. The release build is signed with `%USERPROFILE%\.android\debug.keystore`; its certificate SHA-256 must be `6a4eb335fd04b07b1d61610f60dc403c492f263517fc65143f03fbe31497a106` (check with apksigner) or in-place updates break.
- .NET: `cd ipodsync\tests\FLACie.Tests; dotnet test` (about 1 min; the suite runs serially on purpose).
- Browser: `cd ipodsync\tests\web-smoke; npm ci; npx playwright install chromium; npx playwright test` (about 8 min; delete `.data-smoke`, `.pub-smoke`, `test-results` first; uses ports 8196, 8197 (stand-in download stack) and 5290; needs no internet).
- MSI: delete `src\IpodSync.Maui\obj` and `bin`, then `powershell -File ipodsync\tools\build-installer.ps1 -Out <dir>` (needs the maui-windows workload; about 5 min; leaves a 100 MB .msi).
- Version bump touches 6 files: `android/app/build.gradle.kts`, `FLACie.Server/AppInfo.cs` and `.csproj`, `IpodSync.Maui.csproj`, `FLACie.Tests/G2Tests.cs`, `web-smoke/1d-g2.spec.ts`.
- Load drivers: see the header of `ipodsync/tools/loadtest/mock.mjs`, `run-web.mjs`, `README-android.md` (emulator driver), `report.mjs`.
- Test seams worth knowing: `FLACIE_CATALOG_REDIRECT` (loopback only; points Deezer/iTunes/MusicBrainz lookups at a stand-in), `FLACIE_CACHE_MIN_FREE_MB` / `_PERCENT`, `FLACIE_PARENT_PID` (what the Windows app sets: makes a server behave as the embedded copy).

## Deploying FLACie Web (owner's server; details in the private handoff)
Tarball of `ipodsync/src/FLACie.Core` + `FLACie.Server` (exclude bin, obj, .data-smoke) -> copy to the host -> make a rollback tag of the running image -> `docker build -f FLACie.Server/Dockerfile` -> `docker compose up -d --force-recreate` for that one service -> `/healthz` must say "ok". Extract with `tar --no-same-owner` when the tarball was made by Git Bash. Only the `flacie-web` container may be touched.

## Hard rules (unchanged) and lessons from this run
- No passwords/tokens typed or stored; sign-in is Quick Connect that the owner approves. Never touch other containers or the NAS data. Do not rename the Jellyfin client `ipodplayer`. YouTube downloading is allowed only as an opt-in source flagged "use at own risk" (owner decision 2026-10-12; off by default). Every feature on Android + web + Windows. Honest Tested / Not tested lists; you cannot hear audio.
- **Do not trust a mock for a Jellyfin field.** Beta 52 shipped a bug because the mock never shared an `AlbumId` between albums. Before trusting a field (AlbumId, tags, ids), look at the owner's real server read-only (item JSON, image endpoints are anonymous) or make the mock break the assumption.
- **Verify on screen, not only in unit tests.** The beta 54 round-1 emulator run found four real problems the unit tests could not. Use the emulator driver (`tools/loadtest/run-android.ps1`, `adbui.mjs`, `smoke-android.mjs`) with a stand-in slskd/file mover (see `1i-download-tags.spec.ts`).
- The owner's phone is the real test: they will report "wrong / slow / not as asked"; answer with what you measured and what you could not.
- Agents hit the account's session limit about every 5 hours: commit and push to a branch often; resume them with SendMessage.
- Claude Code's classifier may block production reads or `Remove-Item` text it misreads; run small scripts, never dodge a real denial.

## Open work, in priority order
1. **Measure on the real Jellyfin** (owner's server) what beta 55 costs it: CPU during a first sync and while scrolling; compare with `docs/test-run/I-load.md`; read `/api/admin/load`.
2. **Android media session** exposes the whole queue (10,000 items). Give external controllers a window around the current song (or no timeline). Verify lock screen, Bluetooth and Android Auto behaviour on a device.
3. **Phone first sync** is slower to complete (about 46 s vs 15 s for 10,000 songs on a fast server): consider fewer, bigger pages or fetching streams only for songs that are visible/played first.
4. **Prefetch**: web `pre=1` early read does nothing for a skip (different URL); remove it or make the stream use it. Phone: read only the start of the next song, not the whole file.
5. **Album-level cover sharing, safely** (group by album name + album artist and verify, never by AlbumId alone) to cut per-song cover requests.
6. Downloads: tag web downloads on the phone (`/api/downloads`), divider after restart, iPod canvas themes, the narrow pane's bare "Downloading", exercise Lidarr/open sources/yt-dlp, "Couldn't download" on a device, Undo for web queue removal.
7. Real-device checks: Fold, RG Rotate, MSI install/upgrade, by-ear (gapless, Hi-Res vs sound, EQ, headphone volume), first NAS scan jank, a long soak.
8. Older items: Android system light theme; `MEDIA_PLAY` after losing audio focus in the background; Windows iPod-sync tabs on the ungated client; undated Jellyfin edits.
