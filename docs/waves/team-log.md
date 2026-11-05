# Team log (newest at the top; see TEAM.md)

## 2026-11-05 Cloud (cloud/covers-search: F13 core, F1 server, F6.1 done; full suite 1691 green)
- Done: QueueShuffle.On/Off (Core, for the phone twin); ArtistGuess.FromPath/FromFolder, used by JellyfinClient.ToTrack when no artist; SearchRankTop.Artist (wired in ResultSections.razor).
- F1: /art answers a made SVG cover (GeneratedCover) instead of 404 when every source fails; CoverFill + CoverFillService (daily, FLACIE_COVER_FILL_PACE_MS) saves catalog covers, stops on quota.
- Half done: fill progress not shown in Server admin yet (CoverFill.Last holds it); chain steps between Jellyfin and the made cover are the existing ones; F6.2 web layout waits for the search mock-up.
- Note: I edited ResultSections.razor and Program.cs (one line each area) - please check in the merge.

## 2026-11-05 Local
- Cloud has more usage: it also takes the server parts of F1 and F6 and F13 core (see TEAM.md, branch cloud/covers-search). Local waits for F9 format.

## 2026-11-05 Local
- Done: specs F1-F17 (docs/waves/specs), TEAM.md; lyrics glide fix and artist search (Radio + featuring lists) committed on ui/living-sleeve, not released.
- Found: Lose Yourself has no cover because the file has no tags at all (137 such songs); F1 takes the artist from file/folder names.
- Next (Local): wave plan, search mock-up, then F1/F2 and F6. Cloud: start F9 then F10/F11/F16 on cloud/dj-server.
