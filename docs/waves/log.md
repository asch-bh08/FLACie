# Overnight log (newest last)

- 22:45 Home + shorter menu committed and pushed (bafaab0). Usage: 5-hour window 49% (resets 00:10 local), weekly 7%.
- 22:50 Plan written (docs/waves/plan.md). Wave 1 starting: P1 bar (sonnet), P2 browse (sonnet), P6 search-box bug (opus).
- 22:55 Wave 1 agents started in their own worktrees: P1 bar (sonnet, ports 8410/5410/8411), P2 browse (sonnet, 8420/5420/8421), P6 search-box bug (opus, 8430/5430/8431). Watchdog check every 20 min (:07, :27, :47): usage, resume agents stopped by the limit, merge, next wave, log, push.
- 23:00 Owner: no mockups, only the full build in the morning. Agents told; plan updated.
- 23:10 P6 merged (4944b0b): the search box kept nothing typed before the page went live (the server-drawn box is replaced when Blazor connects, and the new box was set to ""). Fixed in flacie.js: typed text, focus and caret move to the new box. New test; 12/12 runs pass. Post-merge check 1n + 1y + 1x: 26/26.
- 23:15 P5 settings/server pages started (sonnet, 8450/5450/8451).
- 23:35 P2 browse merged (8469bc6): serif section titles, mono times and chips, rounded covers, top-result card. Its spec read styles before the page went live; fixed (747d873), 16/16 twice. Known: the /charts page (FLACie.Server) only has the shared styles; on a phone the source badge squeezes song titles.
- 23:50 P1 bar merged (a3b74e4): serif title, format chip, cover-coloured play, tinted pills; a floating card on a phone. It had hidden the "Downloaded · 4m ago" tag on phones (1i caught it): put back. 1i + 1z-bar 25/25. Known: the phone card hides Retro, the device picker and the mini player (all still in the full-screen player except device picker / PiP) to fit.
- 23:55 P3 detail pages and P4 playlists started (sonnet; 8460/5460/8461 and 8470/5470/8471). Running: P3, P4, P5. Check: usage 72%, resets 00:10.
- 00:00 P5 settings merged locally (e42a35e), its check run going; 1-pages 320px menu test fixed (same live-page race). P7 Android started (opus, emulator only). Running: P3, P4, P7. Check 00:33: usage 80%, resets 00:10 local... (shown as 36 min).
