# FLACie: pick-up guide after beta 59 (2026-10-13, overnight run)

(Kept in the repo because the Pi handoff share (`\Raspberrypi\cs-1\FLACie-handoff`) went offline at the end of the night; copy this file there as `START-HERE-beta59.md`, and `C:\wt\apk\FLACie-1.0-beta59.apk` / `C:\wt\msi\FLACie-0.59.0-windows-x64.msi` to `out\` if still wanted: the same files are on the GitHub release.) The beta 58 guide on the share was written before beta 59.

Read: this file, `docs/OVERNIGHT-LOG.md`, `docs/BY-HAND-CHECKS.md`, `docs/spec/close-the-gaps.md`, `docs/NEXT-SESSION-START-HERE.md`, then the private handoff.

## Say first
- Mistake: a 344 MB stray build folder (`ipodsync/wtmsi`) was committed and pushed by `git add -A`; removed with filter-branch from my own commits and force-pushed (main, night/verify-b57, tag v1.0.0-beta58 re-pointed, same content). If a clone fetched in that window: `git fetch && git reset --hard origin/main`. Never `git add -A` after a build; pass Windows paths to PowerShell scripts from PowerShell, not Git Bash (it turned `C:\wt\msi` into `C:wtmsi`).
- No device was available: nothing was tried on the Fold. Emulator + mock + Playwright only.

## State
- **Released: v1.0.0-beta59** (Android 74, MSI 0.59.0, Web 1.0-beta59) and beta 58 before it. main = beta 59.
- Beta 58: Settings readable in the iPod Player skin (was dark text on dark cards) + regression tests for beta 57 (`SeekHold`, `WarmPlan`, catalog key) + emulator verification of beta 57's seek / skip / no-picture fixes.
- Beta 59: the media session publishes only the current song, not the whole queue (`SessionWindow`, `PlayerController.sessionPlayer`). Emulator: `dumpsys media_session` queue size 2003 -> 0; state, actions, metadata, notification/media card, Next/Prev/Pause keys intact.
- Green at beta 59: Android unit 311 run / 2 skipped / 0 failed; .NET 257; Playwright 133. APK signed with the debug key (SHA-256 6a4eb335...97a106), install-tested on the emulator. MSI built, NOT installed. FLACie Web NOT redeployed (version string only): the live site says beta 57.
- Docs: `docs/spec/player-redesign.md`, `docs/spec/ipod-mode-redesign.md` (+ screenshots in `docs/spec/img`).

## NOT done
Phase 0 research docs; sleep timer fade/extend, speed with per-item memory, media-session custom actions (favourite/speed) and notification polish (the rest of Phase 1a); offline downloads; building the player and iPod redesigns; Auto/Cast; MusicBrainz. The graph (spectrograph) fix was never measured. Bluetooth over a real device was never tried.

## Try on the phone first
1. 10,000-song queue over Bluetooth / earbuds / car: should no longer run out of memory; the head unit shows one song and no queue list. 2. Seek bar feel. 3. Skip feel / covers. 4. "Out of Space" cover. 5. Settings in the iPod Player look. 6. Info graph on a new song. Full list: `docs/BY-HAND-CHECKS.md`.

## Gotchas
- Playwright reuses whatever listens on 5290: an interrupted run leaves `dotnet FLACie.Server.dll`; kill it (and `node`) before a rerun or ~47 bogus failures appear. Do not run the emulator and Playwright at once.
- `build-installer.ps1 -Out` needs a real Windows path; a failed run leaves `ipodsync/wtmsi` and `tools/installer/wtmsi`: delete, never commit. The MSI build needs ~1 GB scratch; C: has only ~4-5 GB free.
- `MOCK_EXTERNAL=1 node run-android.mjs ...` uses a mock you started (`node mock.mjs --port 8296 --host 127.0.0.1 --items 2000 --wavsec 180`); the mock now has three songs with no server picture (`--nopic 0` removes them). adb is at `%LOCALAPPDATA%\Android\Sdk\platform-tools`. Seek test: `adb shell "input motionevent DOWN x y; input motionevent MOVE ..."`, screenshot, `UP`.
- `Player.Commands` cannot be built in plain JVM unit tests (SparseBooleanArray): test pure int rules, check the rest on the emulator.
