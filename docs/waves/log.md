# Overnight log (newest last)

## Morning summary (03:00, 2026-10-20)

**The whole app is in design I, web and Android, on `ui/living-sleeve` (pushed). Not deployed, not merged to main.** The build is running in the browser pane on your real Jellyfin (http://127.0.0.1:5396).

- **Web:** the full-screen player; Home; a shorter menu (Explore is part of Search, Import is on Playlists, Downloads is in the Dashboard for admins); the now-playing bar (a floating mini player on a phone); search and browse; album, artist and genre pages; playlists and favourites; settings, account, dashboard, downloads, import, devices, jam, login; charts.
- **Android** (emulator only, never your phone): Now Playing rebuilt to design I, plus Home, the mini player, library, detail pages, playlists, search and settings.
- **Bugs fixed along the way:**
  - Songs that finished downloading at the same moment could drop out of the library.
  - The search box lost letters typed just after a page loaded.
- **Tests:** full web suite 449 passed and 5 failed, then fixed; those 5 now pass on rerun (2 are timing-sensitive and on the known-flaky list). .NET unit tests 712/712. Android unit tests 443, 0 failures.
- **Screenshots:** `docs/screens-new-look/final-*` (web, 390/1280/1440) and `android-*`.

Things to know:
- Home's top picture is now a soft blur of the last cover rather than a sharp picture: a sharp one needs the full-size cover from Jellyfin, which the app saves for the full-screen player only.
- On a phone, the bar is play and next only (previous, Retro and the mini-player button are in the full-screen player); Retro is still in the account menu.
- The /charts rows and the Android "search" screenshot weren't seen on real data.
- The Android mini player's white ring in two screenshots is the D-pad focus ring (the screenshot script presses keys); worth a look by touch.

Next steps need your go-ahead: deploy (restarts the server) and a release.

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
- 00:45 P5 gate: 94 passed, 1 failed (Dashboard 6px too wide at 320px: the subnav's negative margins). Fixed (f378426). P4 playlists merged (61c848f, its run 61/61) and P3 album/artist/genre merged (98b6cee, 75/75). Their specs wrote screenshots into docs on every run, which blocked merges: now to test-results/shots. Gate for P3+P4+P5 running.
- 00:51 Check: usage 92% (resets in 18 min): nothing new started until the reset. P7 Android running; gate for P3+P4+P5 running.
- 01:00 P7 Android merged (680bebc): Now Playing rebuilt to design I in Compose (full-bleed cover fading into its colour, serif title with italic last word, mono pill and chips, ring seek with drag, time under the ring, outlined Lyrics/Up next/More, nothing in the bottom 92dp); fonts with their OFL licences; 436 unit tests, 0 failures; emulator screenshots in docs/screens-new-look/android-*.png. Emulator only.
- 01:20 Gate for P3+P4+P5: 105 passed, 2 failed. Dashboard still 6px wide at 320px: a button row with a hint that would not wrap (fixed, passes). 1x frame-seek setup timed out 1 run in 3: the song was clicked before the page went live; 1x now waits for the live page (17/17 three times).
- 01:25 Pushed 9ee8f56. Usage reset: 0% (next reset 05:10). Started P8 polish (sonnet, 8480/5480/8481): /charts, artist page gap and round picture on a phone, source badge squeezing titles on a phone, device picker on the phone bar. Started P9 Android rest of the app (opus, emulator only): Home, mini player, library, detail, playlists, settings.
- 01:45 Check: usage 0% after reset. 02:00 P9 merged (a46c2ac): Android Home hero, serif titles, mono labels, outlined chips, detail headers, cover-tinted mini player; shared DesignI.kt; 443 unit tests, 0 failures. Its "search" screenshot actually shows Home (the script missed the screen). The mini player shows a white focus ring in two shots: the ring is D-pad/keyboard focus, and the script uses key presses, so likely correct; worth checking by touch.
- 02:10 Check: usage 20%, resets 05:10. P8 polish still running. dotnet test on the merged branch: 712 passed, 0 failed.
- 02:40 P8 polish merged (9f61748): /charts styled, artist/album gap on a phone fixed, round artist picture, the source badge under the title on a phone, the device picker back on the phone bar. It had shrunk the phone bar's buttons to 36px; changed to the design's mini player (play and next, previous in the full-screen player) with every button at 44px (b5ffe23). 1z-bar + 1z-polish + 1i: 31/31. All phases merged. Final gate: full web-smoke suite running.

## Road to 1.0, overnight 2026-10-20/21 (owner away; go-ahead to work through the list and release betas)

- 22:20 Beta 74 released (web dock, centred page bar, folding sidebar; Android bottom row, seam blend).
- 22:55 Beta 75 released: Retro on Android shows Now Playing and your playlists, asks again for a library still loading; four bottom tabs (Home, Search, Library, Devices); Lyrics and Info as cover-sized cards with the controls kept.
- 23:40 Beta 76 released (Android): top-bar buttons back at the right edge; the seek frame fills in round the cover as the song downloads; Retro lyrics (web requests through the app).
- 00:45 Beta 77 released: reconnect after a server restart keeps playing; plain-words errors (FriendlyError); Up next Undo and Save as playlist (web and Android); recent searches (web and Android); / and ? shortcuts; one-time welcome on Home; Retro shuffle/repeat in step both ways; Android Clear Up next with Undo. A full-suite run hung after a second run clobbered its results folder: never run two Playwright runs on the same ports.
- 01:00-02:10 Accessibility: axe (WCAG 2.1 A/AA) on every main page and the full-screen player, phone and PC, all pass after fixes (text on the accent turns dark or white, sidebar labels, playing title, eq mark); reduced motion turns every animation off; every Tab stop on Home shows focus. Memory budget test: 9.5 MB after a browse-and-play walk (limit 300). Share on album/artist/genre pages. Settings: 1.0 group names on web and Android, Find a setting box.
- 02:15 Beta 78: full web suite + Android build/tests running.
