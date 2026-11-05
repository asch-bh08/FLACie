# Two Claudes, one repo (read this first, every session)

Two Claude sessions build the 5 Nov list together: **Local** (the owner's PC, can reach frank, the phone and the emulator) and **Cloud** (a second
account in claude.ai/code on this GitHub repo; no frank, no phone, no emulator). They cannot message each other. **The repo is the shared memory.**

## Every session, both of you
1. At the start: `git fetch`, read this file and the newest entries of `docs/waves/team-log.md`. Nothing else is required; read a spec only when you work on it.
2. At the end of each piece of work (and before you run out of usage): add a short entry at the TOP of `docs/waves/team-log.md`, commit, push.
   Entry: date and who (Local/Cloud), what is done (commits), what is half done, what you need from the other. Five lines at most.
3. Keep it cheap: read only the files your spec names, run only the tests for what you changed while building, the full suite before you hand over.

## Who builds what (specs in docs/waves/specs/, list in docs/waves/features-2026-11-05.md)
- **Cloud**, branch `cloud/dj-server` from `ui/living-sleeve`: server-side work that needs no device:
  F9 (the step format in FLACie.Core and the web runner), F10, F11, F12 (server part), F15 (server part), F16, F7.
  Then, on branch `cloud/covers-search` from `ui/living-sleeve`: the server parts of F1 (ArtistGuess, cover chain, generated cover, fill pass)
  and F6 (SearchRank on the server, the web search page), and F13's FLACie.Core part (QueueShuffle).
  Then F18.1 (NetEase lyrics source). Spend usage on progress, not volume: the most useful unblocked work first, small commits, no repeated runs or re-reads.
- **Local**: F2-F5, the phone parts of F1/F6/F9/F12/F13/F15, F8, F14, F17, F18.2 (lyrics alignment on frank), F19 (mini player), F20 (Android Auto), merging, releases and deploys.
- Never edit a file the other side's specs own. If you must, write it in the log as a request instead.
- Local merges `cloud/dj-server` after reading its log entry and diff, then runs every test.

## Owner's rules (both)
- Never Haiku. Test first (TDD). `git add` named files only, never a directory; check `git show --stat HEAD` before pushing.
- Tests must never reach the real Jellyfin server or frank. Never print or write a token or password.
- Comments and messages in plain English, short; code reads like the code around it.
- Cloud never deploys, never releases, never pushes to `main` or `ui/living-sleeve`: only its own branch.
- The phone needs FLACie Web; the DJ and SoundCloud live on frank. The phone keeps covers it has seen and looks them up itself only when frank is unreachable.

## Round 2 (from 5 Nov afternoon): everything fully built
New split: **Cloud = all server and web work. Local = all Android and Android Auto work, frank deploys, device tests, merges, releases.**
Cloud works on branch `cloud/round2`, from `ui/living-sleeve` at or after "Merge cloud/covers-search" (485c6d41). Finish each item fully: no
placeholder values, no "the existing one" gaps. Order:
1. F11: real popularity. Deezer track `rank` from the catalog search, cached on disk on frank; combined with the person's plays. Test with
   fixtures: "rhythm of the night" picks Corona's The Rhythm of the Night over DeBarge's when neither was played.
2. F1: the full chain before the made cover: the folder picture (Jellyfin's image for the song's folder/album item), then the artist photo
   (Jellyfin artist image, then the catalog's artist picture). Tests per step.
3. F12 end to end: "remove this from the playlist" -> "are you sure?" -> "yes" removes, "no" keeps; "make a backroads playlist" -> made and
   named -> "call it Country Roads" renames; "not that one" across turns. The web runner runs every step kind and reopens the mic after an ask.
4. F13 web: the shuffle toggle button in the web player (uses QueueShuffle). F15 web: the first-song switch in the web DJ settings.
5. F6.2: the web search page: Top result on the left with that artist's top 5 songs beside it (no big empty card); then rows Popular,
   Albums, Singles & EPs (newest first), Playlists (Best of, Radio, decade and genre lists), Other artists with similar names (small).
   A song query keeps today's song-first layout. Playwright test for the "prodigy" page. Owner reviews it live afterwards.
6. F19: the mini player (spec F19). 7. F4.2: web preloads the next song. 8. F17 web part: captions shown when their audio plays.
9. /art?fmt=png (PNG made cover). 10. F21.1 Frequently played (server + web Home), Listen again keeps all played songs but moves further down Home. 11. F22 DJ favourites. 12. F23 Spotify playlists downloaded as you go (spec F23).
Log after each item (five lines), push `cloud/round2`.
