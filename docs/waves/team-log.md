# Team log (newest at the top; see TEAM.md)

## 2026-11-05 Local (evening)
- b106 released (0e66dfa5, APK 121 + MSI 1.0.106); phone fix: weak-signal switch waits for 2 MB of real measurements, Opus failure retries on FLAC. main = ui/living-sleeve.
- Cloud: start cloud/round3 from ui/living-sleeve 0e66dfa5 or newer (F21.1, F22, F23, item 13, F24).

## 2026-11-05 Local
- Merged cloud/round2 + all phone work; b106 web deployed (SoundCloud on, rollback flacie-web:rollback-20261105-pre-b106). APK held: smoke 21/23 (40-skip burst, pause state 7), being fixed.
- Found and fixed a KeepAlive ping-pong in Connect.cs (311,539 msgs/min; web 150%->1% CPU, Jellyfin settles). Census line stays in the log for now.
- New spec F24 (artist names fixed in Jellyfin after an approved dry-run list) is Cloud item 14.

## 2026-11-05 Cloud PROGRESS (cloud/round2): all 8 round 2 items done; PNG made cover in progress
- Done: F11 337b2af, F1 chain b9b52cf, F12 6528b38, F13+F15 web 39f3f63, F6.2 1d100ec, F19 64d3e25, F4.2 d7d9293, F17 web c5ca927; fix: made cover only for song keys (1c-api) after them.
- In progress: /art/{key}?fmt=png (row and ?s=full) as a PNG of the made cover, for the phone.
- Unit suite: 1762/1763 in one full run (PlayerDjLiveTests "...speaks within a few songs" failed once under load, passes 3/3 alone). web-smoke full run on .NET 9 still going: 1h-queue "saves as a playlist" failing (Autoplay songs in the saved list), checking if it is mine.

## 2026-11-05 Cloud (cloud/round2: F17 web captions)
- Done: web captions start when the DJ's voice is really heard: flacie.js calls PlayerState.OnDjSpeaking on the clip's first "playing" (after the cue) or the device voice's onstart; no guessed lead, and a voice that never plays shows nothing.
- Proof: DjClearTests (3 rewritten/new), web-smoke 1q-dj (8 green; a stand-in speech voice for headless Chromium).

## 2026-11-05 Cloud (cloud/round2: F4.2 web preload)
- Done: the page's early read of the next song now covers its first 30 s (size x 30 s / length, 1-8 MiB; 4 MiB when unknown), kept by the server (SongHeadCache, 48 MiB for six songs), dropped when the queue moves on.
- Proof: CacheTests (sizes, ranges), web-smoke 1z-song-start + 1g-burst green (the mock counts any bounded early read now).

## 2026-11-05 Cloud (cloud/round2: F19 mini player)
- Done: flacie-pip.js + mini-player.css: cover colours, loudness seek bar with times, shuffle/repeat/heart/DJ/mic (they press the bar's own data-pip buttons and mirror their state), "Next:" line, the sung line (PlayerBar sends synced lyrics while it is open), one bar with the cover left when wide.
- Proof: web-smoke mini-player.spec.ts (2 green), 1r-search-artist + 1z-made-playlists green too. Tip: web-smoke needs a .NET 9 runtime for the published app (on .NET 10 blazor.web.js is missing), which is why the websocket waits failed for me before.

## 2026-11-05 Cloud (cloud/round2: F6.2 web search page)
- Done: an artist search (ResultSections) = Top result + their top 5 songs beside it, then Popular, Albums, Singles & EPs (newest first), Playlists (made + own), Other artists (small chips). Song searches unchanged.
- ArtistPage.Of in Core (tested); Playwright 1r-search-artist.spec.ts (3 green); mock-jellyfin.mjs gained The Prodigy (12) and Prodigy (3), ids "p..".
- Local: please run the whole web-smoke suite once on frank's box (here Playwright 1.49 never sees the Blazor websocket event, so specs that wait for it time out; PW_CHROMIUM picks a local Chromium).
- Also fixed a flaky step order in DjConversationWebTests.

## 2026-11-05 Cloud (cloud/round2: F13 + F15 web)
- F13: the web shuffle buttons (player bar, Now Playing, iPod) use QueueShuffle: on shuffles only after the current song, off restores the queue order.
- F15: "Announce the first song" switch is in the web DJ settings; off, a shuffled playlist start asks for no line. A plain Play stays quiet (owner rule pinned in PlayerDjTests).

## 2026-11-05 Cloud (cloud/round2: F12 end to end on the web)
- Done: DjConversationWebTests drive the real PlayerState + UserSession: remove asks, "no" keeps, "yes" removes; "make a backroads playlist" then "call it Country Roads"; "not that one" across turns; every step kind runs.
- DjConversation.AskedBack(answer) / After(answer, turn): an answer with an Ask step listens again with the long window (DjMic uses it). Local: same rule in the phone's DjConversation.kt.

## 2026-11-05 Cloud (cloud/round2: F1 full cover chain)
- Done: a song without its own picture: catalog (song, then album) -> folder picture (Jellyfin ParentPrimaryImage, else the folder item) -> artist photo on Jellyfin -> Deezer artist photo -> made cover.
- The lookup key carries the fallbacks after the catalog part ("jf<id>~<tag>" | "jd<folder>" | "ja<artist>", "|" between); covers are saved under the key without them (LookupBase).
- Local: if the phone builds or parses lookup keys itself, part 4 can now hold several fallbacks.

## 2026-11-05 Cloud (cloud/round2: F11 real popularity)
- Done: WebCatalog.SongRankAsync (Deezer track rank for the same title and lead artist); DjPickPopularity keeps ranks in data/dj/popularity.json on frank.
- The DJ looks up the versions of a title before ranking (own plays first, then Deezer rank, then title); a lookup Deezer could not answer is not kept.

## 2026-11-05 Local
- Merged F2 (phone covers via /art, own lookup only when the web is down), F3, F17.2 (adaptive DJ lead).
- REQUEST for Cloud: /art/{key}?fmt=png must answer a PNG of the made cover (both sizes); the phone sends ?fmt=png and cannot draw SVG. Add it to cloud/round2.

## 2026-11-05 Local
- Merged phone rounds: F9 runner, F12 mic reopen, F14 mic pre-warm+beep, F15 switch, F17 captions by clip position, F13 toggle, F20 car, F4.1, F5. Android build+tests green.
- Next Local: F2/F3 phone covers, F17.2 voice early, F6.3 phone search after Cloud F6.2.

## 2026-11-05 Local
- Merged cloud/dj-server and cloud/covers-search into ui/living-sleeve; full suite 1743 green on real .NET 9.
- New split (TEAM.md Round 2): Cloud does all server and web work on cloud/round2; Local does Android, Android Auto, frank deploys, devices.

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
