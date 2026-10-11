# G-emu: crash and memory loops on the Android emulator (fake Jellyfin, ~300 songs), 2026-10-11

The Fold was withdrawn from testing, so the gating Compose crash was re-tested on the emulator `flacie_phone`.
Loop: Home tab, local playback, swipe up/down, Next every 3rd iteration, pid checked each iteration, crash buffer captured on death.

| Build | Variant | Iterations | Result |
|---|---|---|---|
| debug | 2 s sleep, 150 ms swipes | 700 | survived |
| debug | fast (no sleep) | 1500 | survived |
| release (R8) | 2 s sleep, 150 ms swipes | 700 | survived |
| release (R8) | fast (no sleep) | 1500 | survived |

Memory (release, 30 min wall clock, one sample per minute, `G-emu-memory.csv`): total PSS 92 to 113 MB with no upward trend, Java heap 17 to 32 MB, native 38 to 44 MB, same pid throughout, no crash.

Caveat: the emulator has a small fake library and no real Jellyfin latency. The original Slot-table crash on the Fold (real 9,000-song library) appeared after about 5 to 15 minutes; this run does NOT prove it is gone on that device. It shows the candidate fixes (Compose BOM 2024.09.03, no early return in the Home lazy item, no BoxWithConstraints in Quick picks) did not make things worse and that the loop that crashed before runs clean here.