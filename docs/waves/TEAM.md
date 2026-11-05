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
  and F6 (SearchRank on the server, the web search page), and F13's FLACie.Core part (QueueShuffle). Its usage resets at 03:00 on 6 Nov: use it all.
- **Local**: F2-F5, the phone parts of F1/F6/F9/F12/F13/F15, F8, F14, F17, merging, releases and deploys.
- Never edit a file the other side's specs own. If you must, write it in the log as a request instead.
- Local merges `cloud/dj-server` after reading its log entry and diff, then runs every test.

## Owner's rules (both)
- Never Haiku. Test first (TDD). `git add` named files only, never a directory; check `git show --stat HEAD` before pushing.
- Tests must never reach the real Jellyfin server or frank. Never print or write a token or password.
- Comments and messages in plain English, short; code reads like the code around it.
- Cloud never deploys, never releases, never pushes to `main` or `ui/living-sleeve`: only its own branch.
- The phone needs FLACie Web; the DJ and SoundCloud live on frank. The phone keeps covers it has seen and looks them up itself only when frank is unreachable.
