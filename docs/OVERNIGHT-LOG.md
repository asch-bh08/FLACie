# Overnight log (2026-10-12 / 13)

One line per decision or result. Honest: skipped or untested things are said so.

- Start: main = v1.0.0-beta57 (bac708f). No Fold (assumed unavailable per U1); emulator `flacie_phone` + mock + Playwright only. adb was not on PATH; added from the SDK for this session.
- C: has only ~5 GB free; the 4 GB floor limits parallel builds. Builds run one at a time; own build output is deleted if short.
- Scope decision: the prompt lists far more than one night. Order kept: U7 (verify beta 57 fixes + regression tests) -> beta 58; then the specs (player redesign, iPod redesign) as docs; the large features (offline downloads, Auto/Cast, research docs) are NOT started unless time allows, and are not half-released.
- Extracted pure functions for tests: `SeekHold` (player/SeekHold.kt), `WarmPlan` (library/WarmPlan.kt); SeekBar and App.warmAroundCurrent now call them (no behaviour change). Tests: `Beta57RegressionTest`.
