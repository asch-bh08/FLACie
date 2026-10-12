# Overnight log (2026-10-12 / 13)

One line per decision or result. Honest: skipped or untested things are said so.

- Start: main = v1.0.0-beta57 (bac708f). No Fold (assumed unavailable per U1); emulator `flacie_phone` + mock + Playwright only. adb was not on PATH; added from the SDK for this session.
- C: has only ~5 GB free; the 4 GB floor limits parallel builds. Builds run one at a time; own build output is deleted if short.
- Scope decision: the prompt lists far more than one night. Order kept: U7 (verify beta 57 fixes + regression tests) -> beta 58; then the specs (player redesign, iPod redesign) as docs; the large features (offline downloads, Auto/Cast, research docs) are NOT started unless time allows, and are not half-released.
- Extracted pure functions for tests: `SeekHold` (player/SeekHold.kt), `WarmPlan` (library/WarmPlan.kt); SeekBar and App.warmAroundCurrent now call them (no behaviour change). Tests: `Beta57RegressionTest`.
- Emulator (flacie_phone, mock): beta 57 verified: no-picture songs get the catalog cover (one lookup per album), seek holds and follows the finger, skip shows the next cover at once. Scenarios first-sync/scrolling/skip-burst/warm-start/kill-restore/covers: covers 100% @3s, 24-28 image requests at launch, 0 crash lines. NOT done: graph frame stats (emulator is software-rendered, meaningless).
- Found + fixed: Settings in the iPod Player skin was dark text on dark cards (LocalScheme light, Palette dark). SettingsScreen now provides the modern scheme. Verified by screenshot.
- Wrote docs/spec/player-redesign.md and ipod-mode-redesign.md (screenshots in docs/spec/img). Not built: too large for one night to do honestly; also research docs (Phase 0), offline downloads, media-session window NOT started.
- Tests green: Android 309/2 skipped/0 failed, dotnet 257, Playwright 133 (first PW run had 47 failures from a stale FLACie.Server on port 5290 left by an interrupted run; killed it, clean rerun green). APK 73 signed OK, MSI 0.58.0 built (not installed), APK install-tested on the emulator. Web NOT redeployed (version string only).
