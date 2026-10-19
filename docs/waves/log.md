# Overnight log (newest last)

- 22:45 Home + shorter menu committed and pushed (bafaab0). Usage: 5-hour window 49% (resets 00:10 local), weekly 7%.
- 22:50 Plan written (docs/waves/plan.md). Wave 1 starting: P1 bar (sonnet), P2 browse (sonnet), P6 search-box bug (opus).
- 22:55 Wave 1 agents started in their own worktrees: P1 bar (sonnet, ports 8410/5410/8411), P2 browse (sonnet, 8420/5420/8421), P6 search-box bug (opus, 8430/5430/8431). Watchdog check every 20 min (:07, :27, :47): usage, resume agents stopped by the limit, merge, next wave, log, push.
- 23:00 Owner: no mockups, only the full build in the morning. Agents told; plan updated.
- 23:10 P6 merged (4944b0b): the search box kept nothing typed before the page went live (the server-drawn box is replaced when Blazor connects, and the new box was set to ""). Fixed in flacie.js: typed text, focus and caret move to the new box. New test; 12/12 runs pass. Post-merge check 1n + 1y + 1x: 26/26.
