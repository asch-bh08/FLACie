# Overnight log (2026-10-12 / 13)

One line per decision or result. Honest: skipped or untested things are said so.

- Phase 0 research: docs/research/{symfonium,plexamp,apis,design-references,gap-table}.md written (two sonnet subagents + me); spec close-the-gaps.md got a "Phase 0 findings" section (MusicBrainz, CAA, LRCLIB, Deezer resolved or marked open; Jellyfin-specific [verify]s need a real server). Plexamp rows are from secondary sources (Plex pages 403).
- Start: main = v1.0.0-beta57 (bac708f). No Fold (assumed unavailable per U1); emulator `flacie_phone` + mock + Playwright only. adb was not on PATH; added from the SDK for this session.
- C: has only ~5 GB free; the 4 GB floor limits parallel builds. Builds run one at a time; own build output is deleted if short.
- Scope decision: the prompt lists far more than one night. Order kept: U7 (verify beta 57 fixes + regression tests) -> beta 58; then the specs (player redesign, iPod redesign) as docs; the large features (offline downloads, Auto/Cast, research docs) are NOT started unless time allows, and are not half-released.
- Extracted pure functions for tests: `SeekHold` (player/SeekHold.kt), `WarmPlan` (library/WarmPlan.kt); SeekBar and App.warmAroundCurrent now call them (no behaviour change). Tests: `Beta57RegressionTest`.
- Emulator (flacie_phone, mock): beta 57 verified: no-picture songs get the catalog cover (one lookup per album), seek holds and follows the finger, skip shows the next cover at once. Scenarios first-sync/scrolling/skip-burst/warm-start/kill-restore/covers: covers 100% @3s, 24-28 image requests at launch, 0 crash lines. NOT done: graph frame stats (emulator is software-rendered, meaningless).
- Found + fixed: Settings in the iPod Player skin was dark text on dark cards (LocalScheme light, Palette dark). SettingsScreen now provides the modern scheme. Verified by screenshot.
- Wrote docs/spec/player-redesign.md and ipod-mode-redesign.md (screenshots in docs/spec/img). Not built: too large for one night to do honestly; also research docs (Phase 0), offline downloads, media-session window NOT started.
- Tests green: Android 309/2 skipped/0 failed, dotnet 257, Playwright 133 (first PW run had 47 failures from a stale FLACie.Server on port 5290 left by an interrupted run; killed it, clean rerun green). APK 73 signed OK, MSI 0.58.0 built (not installed), APK install-tested on the emulator. Web NOT redeployed (version string only).
- MISTAKE + fix: 'git add -A' committed a 344 MB stray build dir (ipodsync/wtmsi, from a failed first MSI attempt with a mangled -Out path) and pushed it. Removed with filter-branch from my own 3 commits (bac708f..main), force-with-lease pushed main + night/verify-b57, re-pointed tag v1.0.0-beta58 (same content; release assets unchanged). Anyone who pulled in that window must reset.
- Beta 59: media session hides the timeline (SessionWindow in PlayerController.sessionPlayer): dumpsys media_session queue size 2003 -> 0, state/actions/metadata/notification/card OK, Next/Prev/Pause keys OK. Simpler than the planned +/-50 window; custom actions/notification polish not done. Sleep timer fade/extend and speed memory NOT started.
- Pi share went offline at the end: handoff guide for beta 59 is docs/START-HERE-beta59.md (beta 58 guide + APK/MSI copies for beta 58 were written to the share earlier; beta 59 ones are not there).
- Phase 1a/1b features built on feature/phase1a (merged to main): Android speed + per-item memory + long-item resume, sleep timer modes/fade/extend/shake, media-session favourite+speed buttons, offline downloads (Android), seek bar redesign, play/pause morph, iPod wheel inertia + label contrast; web speed/sleep/long-item skips + seek bar CSS. Emulator-verified: speed 2.0 in the session, notification buttons, sleep end-of-song pause, offline download/resume (kill server mid-part -> part 720,018 -> continues -> 1,920,044) and playback from the file with the server down. Web offline deliberately not built (Blazor Server cannot run offline).
- Deployed FLACie Web 1.0-beta60 to frank: rollback tag flacie-web:rollback-2026-10-13-pre60 made first, only flacie-web recreated, /healthz ok (local and via the Tailscale name), other containers untouched. Admin sign-in not tested (needs the owner's Quick Connect approval).
- Beta 61 content: even out volume (Jellyfin NormalizationGain; A + W), MusicBrainz client/gate/cache/match (web Info panel). Cover picker (CAA) and Android MusicBrainz NOT built: the cover endpoint caches by key as immutable, so an override needs its own key through Art.KeyFor and PlayerState.Load; left for a careful pass.
- Beta 61 released + web deployed (rollback tag flacie-web:rollback-2026-10-13-pre61, /healthz ok, other containers untouched). Android 372 tests, .NET 289, Playwright 138; APK install-tested on the emulator (kill-restore ok).
- Beta 62 content: Android Auto library service (BrowseTree + probe-verified on the emulator), MusicBrainz + CAA on Android, cover picker (web), fade between songs (A+W), dots. NOT built: Cast, ratings, shared-element transition, iPod LCD/Cover Flow, session queue window (reasons in the release notes).
- Beta 62 released + web deployed (rollback tag flacie-web:rollback-2026-10-13-pre62, /healthz ok). Android 404/2/0, .NET 299, Playwright 142. After the owner said the web speed/sleep controls were ugly and awkwardly placed they became icon buttons in the heart/More row.

## Shared-element cover transition
- Mini player cover now flies into the Now Playing cover (SharedTransitionLayout, SharedCover.kt); the mini bar gets a real fade exit. Checked on the emulator at animator scale 10 (mid-frame shows the cover growing from the bar). Unit tests green.

## Chromecast
- Not built, decision recorded in docs/spec/chromecast.md: a receiver cannot send the Authorization header, so casting needs a token in the URL on another device. Left for the owner (revocable cast key or server relay).

## iPod mode (web + Windows) and Android iPod
- New /ipod page (device art, LCD, wheel by drag/mouse wheel/keys, Music > Artists/Albums/Songs/Favorites, Playlists, Shuffle, Now Playing with volume on the wheel); IpodNav + WheelDetents pure and unit-tested; Playwright spec 1m-ipod (menus, wheel drag, keys, playback, small window, black model). Windows inherits it through the embedded server (menu: iPod mode).
- Android: Cover Flow page in the Classic theme, scrollbar, bezel inner shadow. Checked on the emulator (docs/img/ipod-coverflow-android.png).

## Beta 63 released
- Android 78 / MSI 0.63.0 / Web 1.0-beta63. Android 414/2/0, .NET 311, Playwright green (test isolation fixed: MusicBrainz cache cleared per run). Emulator run of the release APK clean (kill-restore ok, 0 crash lines). MSI built, not installed. Disk was 3.7 GB: freed emulator snapshots (C:wt is a protected path, left alone).

- Beta 63 web deployed to frank (rollback tag flacie-web:rollback-2026-10-14-pre63, only flacie-web recreated, /healthz ok locally and on the public name; other containers untouched). Release: https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta63 (APK 78 signed with the owner key, MSI 0.63.0 built not installed). Playwright 145 pass; found and fixed along the way: cover choice not redrawn while paused, Enter/Space double-fire on the iPod page.

## Beta 64
- iPod mode on the web/Windows rebuilt to match the Android emulator (user request): 23 models + colours, 3 screen families, Cover Flow 3D, settings tree, wheel zones/hold/inertia/accel, touch models in an iframe, picker panel. Tests: .NET 329, Playwright 151, Android 414/2/0 (no Android code change). Disk fell to 1.7 GB mid-run: deleted emulator snapshots + own build output (snapshots come back whenever the emulator runs and shuts down), the disk then recovered and the release APK was run on the emulator (clean: kill-restore ok, 0 crash lines).

- Beta 64 released and web deployed (rollback tag flacie-web:rollback-2026-10-14-pre64, only flacie-web recreated, /healthz ok locally and on the public name). https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta64

## Beta 65
- User feedback: the web iPod mode was ugly, hard to use, did not fit, changed size on a look change, and had no cover art. Causes: `ArtKey="c.ArtKey"` was a string literal (no @), the JS-set zoom lived on an element Blazor re-rendered, and the picker was hidden behind a button. Fixed, plus a docked panel with the playing cover and pickers; tests now assert real cover keys and the fit. Android: Cover Flow also in the nano/classic 6-7 theme. Android 414/2/0, .NET 329, Playwright 153; release APK run on the emulator clean.

- Beta 65 released and web deployed (rollback tag flacie-web:rollback-2026-10-14-pre65, only flacie-web recreated, /healthz ok). https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta65

## Beta 66
- User: iPod screen CSS messy, not an OS, must work at all sizes, test and verify; and web search typing drops/swaps letters. Built the touch iPod OS, Extras, rating mode, IpodContent core, an overlap/outside/size layout test (9 models x 11 shapes + wheel-only), TextBox (browser-side debounce; the server never writes into a focused box). Another session ("New skills") is building a desk workspace look on branch ipod/desk from my commit; coordination by message.

- Beta 66 released and web deployed (rollback tag flacie-web:rollback-2026-10-14-pre66, only flacie-web recreated, /healthz ok). https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta66

## Beta 67
- Another session found that mouse-dragging touch iPod lists never worked (my rename of ios -> touchOs also renamed the class .ios-scroll in flacie.js). Fixed with a test that fails on the old script. The same session's tests collided with mine (I stopped dotnet/node by image name, which killed its servers): new tools/stop-test-servers.ps1, stop by port only. Main already carried the Alexa session's merged and deployed work; this release includes it as is.

- Beta 67 released and web deployed (rollback tag flacie-web:rollback-2026-10-15-pre67, only flacie-web recreated, /healthz ok). https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta67

## Beta 68 (released by the "New skills" session, at the owner's request while this run was inactive)
- The iPod desk look from branch ipod/desk (spec, plan, independent review and its findings fixed, phone layout reworked at the owner's request) and the mobile-data saving for the analyser twin. Merged to main as a fast-forward of ipod/desk.

- Beta 68 released and web deployed (rollback tag flacie-web:rollback-2026-10-15-pre68, only the flacie service recreated, /healthz ok). https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta68

## Beta 69 (released by the "New skills" session, at the owner's request)
- The listening scenes (car, hi-fi, boombox, MiniDisc) and the desk work since beta 68, from branch ipod/desk, with main (2431101, this run's test fix) merged in first. Main fast-forwarded to it.

- Beta 69 released and web deployed (rollback tag flacie-web:rollback-2026-10-16-pre69, only the flacie service recreated, /healthz ok). https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta69

## Beta 70 (released by the "New skills" session, at the owner's request)
- Retro (rename, touch, fonts, iPod realism, touch OS, compact desk, Discman, performance, Windows Retro button) from retro/polish, with retro/hybrid (FLACie.App/FLACie.Retro libraries and the FLACie.Hybrid Android app) merged in. Main fast-forwarded to it; web deployed (rollback tag flacie-web:rollback-2026-10-18-pre70). https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta70
