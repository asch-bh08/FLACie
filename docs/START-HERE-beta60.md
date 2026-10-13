# FLACie: pick-up guide after beta 60 (2026-10-13)

Supersedes START-HERE-beta59.md (read its "Gotchas"). Also read `docs/OVERNIGHT-LOG.md`, `docs/BY-HAND-CHECKS.md`, `docs/spec/close-the-gaps.md`, `docs/research/gap-table.md`.

## State
- **Released: v1.0.0-beta60** (Android 75, MSI 0.60.0, Web 1.0-beta60). main = release. Tests: Android 367 run / 2 skipped / 0 failed, .NET 270, Playwright 136. APK signed with the debug key (SHA-256 6a4eb335...97a106), install-tested on the emulator. MSI built, not installed. Web not redeployed (live site still beta 57: this build changes the server, so a redeploy is now worthwhile: rollback tag first, only `flacie-web`, /healthz "ok"; recipe in the private handoff).
- Built since beta 59: speed 0.5-3x with per-item memory, spoken-word default and 20 min+ resume (A, web); sleep timer modes, 30 s fade, extend by tap or shake (A, web); media-session favourite + speed buttons (A); offline downloads (A only; web/Windows not applicable, see notes); seek bar redesign + play/pause morph (A), seek bar CSS + spoken position (web); iPod wheel inertia + label contrast (A); Phase 0 research docs and gap table.
- Nothing was tried on a phone. See `docs/BY-HAND-CHECKS.md` (Beta 60 section).

## Next, in priority order
1. **Redeploy FLACie Web** (beta 60 changed the server: speed/sleep/seek).
2. Rest of Phase 1: custom favourite/speed done; **notification polish**, **windowed queue** (currently the session shows only the current song), loudness normalisation + crossfade (needs the real Jellyfin to check `NormalizationGain`), synced lyrics via LRCLIB (see `docs/research/apis.md`: no published terms), ratings/stats.
3. **Android Auto** (MediaLibraryService browse tree; DHU testing needs an AVD with the Google APIs/Play image) and **Cast**: not started.
4. Phase 2 MusicBrainz + cover picker (Deezer terms are non-commercial only: keep the catalog lookup a cached fallback).
5. Player redesign: the parts listed under "Not built" in `docs/spec/player-redesign.md`; iPod redesign: everything except wheel inertia/contrast.
6. Verify the web offline decision with the owner (Blazor Server cannot run offline).

## Lessons from this stretch
- **A start-up order bug hid behind a `catch (_: Exception) {}`**: `restoreQueue()` runs before `userData`/`offline` exist; the emulator's kill-and-restart scenario (`kill-restore`) caught it. Run it after any change that touches App.onCreate, the player's `item()`, or PlaybackService.
- Git Bash mangles `adb shell ls /sdcard/...` (set `MSYS_NO_PATHCONV=1`); heredocs with quotes in the tool break: write scripts with the file tool. Mixed CRLF/LF in Kotlin files: read, normalise to LF, edit, write back with the original ending.
- Never `git add -A` after a build (see START-HERE-beta59.md).
