# FLACie: pick-up guide after beta 64 (2026-10-14)

**Update: v1.0.0-beta64 is released and deployed** (Android 79, MSI 0.64.0, Web 1.0-beta64; rollback tag `flacie-web:rollback-2026-10-14-pre64`). The web/Windows iPod mode now has all 23 models and colours, the three screen families, Cover Flow, Genres, the full settings tree and the wheel feel settings (docs/release-notes-beta64.md). Android code unchanged from 63. Tests: Android 414/2/0, .NET 329, Playwright 151.

**Update: v1.0.0-beta63 is released and deployed** (Android 78, MSI 0.63.0, Web 1.0-beta63; rollback tag `flacie-web:rollback-2026-10-14-pre63`). Adds star ratings (A + W + Jellyfin write), the session queue window (lock screen / Bluetooth / Auto), the shared cover transition, iPod mode on the web and Windows (/ipod), Cover Flow + scrollbar + scrub diamond on the Android Classic. Chromecast NOT built on purpose: docs/spec/chromecast.md. Tests: Android 414/2/0, .NET 311, Playwright green.

**Update: v1.0.0-beta62 is released and deployed** (Android 77, MSI 0.62.0, Web 1.0-beta62; rollback tag `flacie-web:rollback-2026-10-13-pre62`). Adds the Android Auto/Bluetooth media library service (probe-verified on the emulator, never in a car/DHU), MusicBrainz + Cover Art Archive on Android, the web cover picker, fade between songs (A+W), and the web speed/sleep/sound controls as icon buttons beside the heart and More. Tests: Android 404 / 2 skipped / 0 failed, .NET 299, Playwright 142. **Not built:** Chromecast (needs a device and a player refactor), ratings (Jellyfin write unchecked), shared-element cover transition, iPod LCD renderer/Cover Flow, a queue window for the system session. Beta 61 text follows.

**Update: v1.0.0-beta61 is released and deployed** (Android 76, MSI 0.61.0, Web 1.0-beta61; rollback tag `flacie-web:rollback-2026-10-13-pre61`). Adds even out volume (Jellyfin NormalizationGain; A + W; off by default) and the MusicBrainz match in the web Info panel (own 1 req/s gate, cache, User-Agent; choice kept beside the library). Tests: Android 372 / 2 skipped / 0 failed, .NET 289, Playwright 138. **Not built:** cover picker (Cover Art Archive; needs an override key through Art.KeyFor and PlayerState.Load because /art caches by key as immutable), MusicBrainz on Android, Android Auto, Cast, crossfade, windowed queue, ratings. Everything below is the beta 60 text.

Supersedes START-HERE-beta59.md (read its "Gotchas"). Also read `docs/OVERNIGHT-LOG.md`, `docs/BY-HAND-CHECKS.md`, `docs/spec/close-the-gaps.md`, `docs/research/gap-table.md`.

## State
- **Released: v1.0.0-beta60** (Android 75, MSI 0.60.0, Web 1.0-beta60), **web deployed to frank** (rollback tag `flacie-web:rollback-2026-10-13-pre60`, /healthz ok; admin sign-in not tested). main = release. Tests: Android 367 run / 2 skipped / 0 failed, .NET 270, Playwright 136. APK signed with the debug key (SHA-256 6a4eb335...97a106), install-tested on the emulator. MSI built, not installed. Web not redeployed (live site still beta 57: this build changes the server, so a redeploy is now worthwhile: rollback tag first, only `flacie-web`, /healthz "ok"; recipe in the private handoff).
- Built since beta 59: speed 0.5-3x with per-item memory, spoken-word default and 20 min+ resume (A, web); sleep timer modes, 30 s fade, extend by tap or shake (A, web); media-session favourite + speed buttons (A); offline downloads (A only; web/Windows not applicable, see notes); seek bar redesign + play/pause morph (A), seek bar CSS + spoken position (web); iPod wheel inertia + label contrast (A); Phase 0 research docs and gap table.
- Nothing was tried on a phone. See `docs/BY-HAND-CHECKS.md` (Beta 60 section).

## Next, in priority order
1. (done) FLACie Web beta 60 is deployed; ask the owner to open the site once and confirm admin sign-in.
2. Rest of Phase 1: custom favourite/speed done; **notification polish**, **windowed queue** (currently the session shows only the current song), loudness normalisation + crossfade (needs the real Jellyfin to check `NormalizationGain`), synced lyrics via LRCLIB (see `docs/research/apis.md`: no published terms), ratings/stats.
3. **Android Auto** (MediaLibraryService browse tree; DHU testing needs an AVD with the Google APIs/Play image) and **Cast**: not started.
4. Phase 2 MusicBrainz + cover picker (Deezer terms are non-commercial only: keep the catalog lookup a cached fallback).
5. Player redesign: the parts listed under "Not built" in `docs/spec/player-redesign.md`; iPod redesign: everything except wheel inertia/contrast.
6. Verify the web offline decision with the owner (Blazor Server cannot run offline).

## Lessons from this stretch
- **A start-up order bug hid behind a `catch (_: Exception) {}`**: `restoreQueue()` runs before `userData`/`offline` exist; the emulator's kill-and-restart scenario (`kill-restore`) caught it. Run it after any change that touches App.onCreate, the player's `item()`, or PlaybackService.
- Git Bash mangles `adb shell ls /sdcard/...` (set `MSYS_NO_PATHCONV=1`); heredocs with quotes in the tool break: write scripts with the file tool. Mixed CRLF/LF in Kotlin files: read, normalise to LF, edit, write back with the original ending.
- Never `git add -A` after a build (see START-HERE-beta59.md).
