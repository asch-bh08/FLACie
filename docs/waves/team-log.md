# Team log (newest at the top; see TEAM.md)

## 2026-11-05 Cloud (cloud/covers-search: + F18.1, F1 admin; full suite 1695 green)
- Done: F18.1 LyricsNetease (after LRCLIB when it has no synced lyrics; same artist, length within 8 s); Server admin shows the cover pass (Covers section).
- Still waiting: F6.2 web search layout needs the approved search mock-up (none in the repo yet).

## 2026-11-05 Cloud (cloud/covers-search: F13 core, F1 server, F6.1 done; full suite 1691 green)
- Done: QueueShuffle.On/Off (Core, for the phone twin); ArtistGuess.FromPath/FromFolder, used by JellyfinClient.ToTrack when no artist; SearchRankTop.Artist (wired in ResultSections.razor).
- F1: /art answers a made SVG cover (GeneratedCover) instead of 404 when every source fails; CoverFill + CoverFillService (daily, FLACIE_COVER_FILL_PACE_MS) saves catalog covers, stops on quota.
- Half done: fill progress not shown in Server admin yet (CoverFill.Last holds it); chain steps between Jellyfin and the made cover are the existing ones; F6.2 web layout waits for the search mock-up.
- Note: I edited ResultSections.razor and Program.cs (one line each area) - please check in the merge.

## 2026-11-05 Cloud (cloud/dj-server: F7 done; full suite 1714 green)
- Done: RemixMatch (Core/YtdlMatch.cs), SourceRank + SoundCloud.Best (Core/SoundCloud.cs); tools/ytdl/server.py: remix-aware rank, "source": "soundcloud" (scsearch, -f download/bestaudio).
- SoundCloud entrant races beside YouTube only with FLACIE_SOUNDCLOUD=on (an old yt-dlp service ignores "source"); files go to "Artist - Title (SoundCloud).ext"; lossy goes on the upgrade list as before.
- Local: redeploy tools/ytdl/server.py on frank, then set FLACIE_SOUNDCLOUD=on. I edited Downloads.cs (not in any spec) for the entrant, please check in the merge.

## 2026-11-05 Cloud (cloud/dj-server: F9 F16 F10 F11 F12-server F15-server done)
- Done: F16 oldest line reused; F10 'play X from my Y playlist' / 'add X to Y (and play it next)'; F11 DjPick (own plays > IDjAskUser.Popularity > title) + 'not that one'/'the X one' for 1 min.
- F12: remove asks 'are you sure?' (Ask step, Do=None, mic listens again) and removes on 'yes'; 'make a backroads playlist' = createplaylist+play+ask; reply 'call it X' -> new step do:"renameplaylist" {playlist: old, title: new}.
- F15: DjSettings.FirstSong (json "firstSong", default true, web switch); off = no "Shuffling Pop..." line. Rare keeps its half chance (DjCarDriveTests).
- Local: phone runner needs renameplaylist + "firstSong" switch; SessionAskUser.Popularity is 0 until a real source is wired (DjAskHost.cs, not mine).

## 2026-11-05 Cloud (F9 step format, for Local's phone runner)
- Done: F9.1 on cloud/dj-server. /api/dj/ask JSON keeps the old fields and adds "steps": [{do, paths?, songs?, title?, shuffle?, replace?, playlist?, say?}], run in order.
- do: play | upnext | next | pause | resume | volumeup | volumedown | djon | djoff | addtoplaylist | removefromplaylist | createplaylist | ask | shuffle.
- songs per step = [{path, file, title, artist}] as before; playlist = name (made if missing); ask = say the "say" question, reopen mic; shuffle = turn on/off.
- No "steps" = old single answer (one step from "do"). Note: dotnet 9 not installable here, built with SDK 10 + DOTNET_ROLL_FORWARD=Major.

## 2026-11-05 Local
- Cloud has more usage: it also takes the server parts of F1 and F6 and F13 core (see TEAM.md, branch cloud/covers-search). Local waits for F9 format.

## 2026-11-05 Local
- Done: specs F1-F17 (docs/waves/specs), TEAM.md; lyrics glide fix and artist search (Radio + featuring lists) committed on ui/living-sleeve, not released.
- Found: Lose Yourself has no cover because the file has no tags at all (137 such songs); F1 takes the artist from file/folder names.
- Next (Local): wave plan, search mock-up, then F1/F2 and F6. Cloud: start F9 then F10/F11/F16 on cloud/dj-server.
