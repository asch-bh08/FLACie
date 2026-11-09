# Team log (newest at the top; see TEAM.md)

## 2026-11-09 Local: b124 release run
- Merged origin/claude/exciting-ride-2qa9ry (to d74b65c1) into ui/living-sleeve. Version 1.0 b124 / Android 139 in all six places.
- Server tests: 1999/1999 passed.

## 2026-11-10 Cloud: the 5 web failures, the phone's address (branch claude/exciting-ride-2qa9ry, from ui/living-sleeve b124)
- Note: the b124 merge missed Cloud's last batch (music pages design-j-music.css + 1z-music-fit, phone Playlists/Settings/Now Playing, docs/phone-overhaul-unbuilt.md): merged in again here. Please build the phone again (those three Kotlin commits are new to you).
- 1d-g2 "keyboard": failed here too. The test clicked the page title to move focus, but once the list scrolls the sticky search bar covers it and focus was on the song's row button; it now blurs whatever has focus. Test fix, not app.
- 1q-dj Aura-2, 1z-discman-fit 1024x768, 1z-dj-ask endpoint, 1z-dj-mic-twice: pass here alone (each), together (45 passed), and twice in a row on kept data. All four cover the newest changes, so the likely cause on the PC is a reused older server on WEB_PORT (Playwright reuses any server there: an earlier run, or the Windows app's own copy). global-setup.ts now refuses to run unless the server's flacie.js and app.css match this checkout byte for byte (checked against a stand-in old server). If the PC still fails with a fresh server, please paste the error lines into the log.
- Phone's FLACie Web address (HANDOFF §0a): `PublicAddress` (FLACie.App/PublicAddress.cs, data/public-address.json): FLACIE_PUBLIC_URL, else Server admin's new "FLACie's address" card, else the first tailnet address the page was opened at (*.ts.net or 100.64/10), kept; TopBar.ShareAddress writes only that (nothing while none is known; the Windows app without the service keeps the old way). Settings > About: "You opened this page at" + "Your phone uses". Tests: PublicAddressTests (5), 1z-public-address.spec.ts.
- Runs: server 1999/1999; web 1z-public-address + the five + 1z-settings together: 66 passed. Not released, not deployed (Local after 11 Nov 18:00).

## 2026-11-09 Cloud (night): web overhaul done, phone overhaul written (unbuilt)
- Web, all pushed on claude/exciting-ride-2qa9ry: design-j.css (frame, motion, Playlists tile, NP phone fit), design-j-settings.css, design-j-retro.css, design-j-music.css (Home, Explore tabs, Favourites, detail pages, empty states; geometry test 1z-music-fit), Discman fit (1z-discman-fit), plain volume slider, songs no longer download a second copy at start (1z-wave-waits). Key specs on the merge: 103 passed. Known: 1d-g2 "Space plays/pauses" fails on the base too.
- Phone, written and NOT BUILT: see docs/phone-overhaul-unbuilt.md (chips, Home, library lists, album/artist, Playlists grid with New playlist tile, Settings, Now Playing top bar on narrow phones, Up next). Plus the earlier unbuilt phone fixes (DjAsk album, MicGate, FirstFrameGate, search quick=1, Alexa row). Local: build, :app:testDebugUnitTest, emulator drivers, then deploy web + release.
- Open: narrow PCs 761-1100 px Now Playing tiles (not checked); search rows with only the label (need the page address); phone search covers timing; owner's call: round search/filter bar (1z-library pins its corners), empty Favourites still shows dimmed Play/Shuffle.

## 2026-11-09 Cloud (late): PHONE APP OVERHAUL IS LOCAL'S (owner: "do it all")
Owner wants the phone app overhauled to match the web (web done or in progress on claude/exciting-ride-2qa9ry; "before" screens of the phone are in docs/screens-phone-b123, the web ones in the gallery https://claude.ai/artifact/4Dhs2zaXLXtju8hT6XQQsV). Cloud can't build or run Android, so this is yours, page by page, each built, run on the emulator, screenshot before/after, folded and unfolded:
- Look to match the web: round pills in the sans for every chip/tab/segmented choice (no boxy mono chips), plain sentences in the sans (mono only for short labels and numbers), circles for artists, squares for covers, round buttons, fewer bare icons (a word under or beside the icon where there is room).
- Motion: rows and cards come in with a short rise and fade, covers lift a little on press, sheets spring; transform/alpha only, respects Animations off.
- Pages: Home (and its covers already there at start: FirstFrameGate/ArtCache.keepReady from this branch, please build and check), Library/Albums/Artists/Songs, Album, Artist, Playlists (a "New playlist" tile first in the grid, as on the web), Search, Settings (sections as cards, two columns unfolded), Now Playing (cover fits under the top bar on short screens: no overlap with the DJ/mic/EQ/Retro buttons), Up next (arrange buttons less loud), mini player, the Retro screens (panel as round pills, Back/Options with words, the device never cut off or overlapping).
- Every "hangs off / overlaps" fix gets a geometry check (instrumented test or the drivers), like the web's 1z-discman-fit.spec.ts.
- Also from this branch, not built yet: DjAsk.kt/Library.findSong album (Prodigy versions), DjMicLimits/MicGate (stuck mic), Settings Alexa row, search quick=1 saved playlists, FirstFrameGate. Please build, run :app:testDebugUnitTest and the emulator drivers first.

## 2026-11-09 Cloud (evening)
- Request for Local: phone search covers are slow (owner). Code looks right (CoverLookup.small asks Deezer/iTunes for 250 px, RemoteArt fetches 8 at a time with a disk cache), so it needs the device: run a search on the emulator/phone with `adb logcat -s FLACie FLACieCover` and the diag file on frank (data/diag/<user>.jsonl), and see whether the time goes to the catalog search itself, the cover downloads (timeouts? the CDN?) or the row drawing.
- Test-order failures fixed (Sonnet agent, merged): 1-pages Quick picks (reopens Home; checks the artist's songs are in the row, a weaker claim than "come first": owner/Local please confirm), 1x-new-look (shapes stored first; frame tests wait for drawing), 1z-dj-ask (stand-in replaces unprefixed SpeechRecognition; waits), 1q-dj phone layout (DJ+mic in top bar). Real app fix: flacie.js seek-frame bars of a silent song draw as short dashes (min 4.5). 1q-dj "Aura-2 voices" never failed here. Combined run of 1-pages, 1q-dj, 1x-new-look, 1z-dj-ask, 1z-dj-bar, 1z-dj-mic-twice: 70 passed.
- Also: Now Playing on phone-width web: the cover fits under the top bar (design-j.css). Retro/iPod agent still running.

## 2026-11-09 Cloud (overhaul started; state if this session stops)
- Done + pushed (claude/exciting-ride-2qa9ry): design-j.css (frame + music pages: motion, quieter row badges, Up next arrange buttons on hover, Home hero without a cover), Explore pages keep the side menu (MainLayout), phone-width bottom bar = 4 places (rest in avatar menu), Server admin lists only connected pages, MeloTTS removed.
- In progress: Playlists page (new playlist as the first tile; Playlists.razor + design-j.css), tests running. Two agents in worktrees: Sonnet = test-order failures; Opus = settings-type pages (design-j-settings.css). Their commits are NOT on the branch until merged.
- Next: Retro/iPod overhaul (agent when one frees up), Search, Now Playing, phone pages (Kotlin; needs Local to build).
- Gallery of "before" screens: https://claude.ai/artifact/4Dhs2zaXLXtju8hT6XQQsV

## 2026-11-09 Cloud (later): MeloTTS retired (before 20 Nov)
- Request for Local (owner, 9 Nov: "every page, every iPod, every retro, every Discman... and mobile"): the web gallery is at https://claude.ai/artifact/4Dhs2zaXLXtju8hT6XQQsV (owner's account). Cloud can't run the emulator: please screenshot every phone app screen (each tab, Now Playing and its tabs, every sheet and menu, Settings sections, the Retro/iPod screens, folded and unfolded widths) and put them where the owner can see them, so the phone overhaul has its "before" set too.
- Done: `CloudflareVoice` has no MeloTTS: unknown voices fall back to Aura-1 Helios (`NormVoice`); `ConfigVersion` 3 moves a saved "melotts" once (old default or chosen) to `kokoro:am_liam` when Kokoro's /healthz says "ok" on this server (1.5 s), else to Aura-1 Helios, and writes dj.json. Dashboard: MeloTTS gone from the Voice picker and the test buttons. MeloTTS-only branches dropped in DjClips/DjService. `@cf/qwen/qwq-32b` removed from `DjBudget.Weighed`. 7 test files updated + new migration tests; server 1994/1994.
- Ready to ship with the next web deploy. After deploy, check frank's dj.json voice and the log line "DJ voice settings moved to version 3".

## 2026-11-09 Cloud (taking over from Local, branch claude/exciting-ride-2qa9ry from ui/living-sleeve 9b9fc19)
- A done: DJ "play Out of Space by the Prodigy" picks the album version. `DjPick.VersionRank` (album 0 < edit < live < remix < demo, from title brackets/" - " note and album name) ranks first, before plays; versions grouped by `DjPick.VersionKey` (brackets and " - " dropped); an artist-named ask is ranked too (it used to take the matcher's first hit and keep nothing for "the other one"). "The other one" says which: "Here's the live version of Out of Space, by The Prodigy."
- Phone: answers now carry each song's album; `DjAskSong.album`, `Library.findSong(title, artist, album)` picks the copy on that album (the live and album "Out of Space" share title+artist). NOT BUILT here (no Android SDK): Local, please run :app:testDebugUnitTest (DjAskTest.theAlbumTellsSameNamedVersionsApart).
- Tests: DjVersionTests (12). Server suite 1991/1991 (SDK 10 + DOTNET_ROLL_FORWARD=Major). Ready to ship with the next web deploy + APK.
- B done: DJ mic stuck on "listening". Web (DjMic.razor, flacie.js): stops when music starts in a follow-up turn, 15 s cap in the recogniser, 20 s page watchdog, 30 s answer timeout, an older recogniser/recorder session can no longer end a new one (`_gone`), new `flacie.micAbort`. Test 1z-dj-mic-twice.spec.ts (4 pass, incl. press twice, music back, error, time limit). Phone: `MicGate`/`DjMicLimits` (new DjMicLimits.kt, MicState moved there) wired into DjMicUi.kt + stale recogniser callbacks ignored; DjMicLimitsTest 6/6 run here with a standalone kotlinc. DjMicUi.kt NOT compiled here: Local, please build + run dj-android.mjs.
- Found: 1z-dj-ask "the mic on Now Playing listens…" fails also without my change (first press never shows listening there); not fixed yet.
- Web tests here need the .NET 9 runtime (blazor.web.js 404 on 10): installed from NuGet runtime packs into /usr/lib/dotnet/shared.
- Owner (9 Nov): search shows saved playlists at once: `/api/made?quick=1` and `ForSearchAsync(quick)` (saved lists only, no source asked); web Search.razor and phone PlayerUi show it, then the full answer. Test A_quick_search_... Server 1992/1992.
- Owner (9 Nov): Home covers "flash load" on a cold start, "make them already there like Spotify"; Home also changes (newer playlists/recs kept for next open). Phone: `FirstFrameGate` (tested 4/4 with standalone kotlinc), first frame held ≤400 ms in MainActivity until on-disk Home covers are in memory, row covers decoded on the spot from disk in the first 3 s (`ArtCache.decodeDisk`), newer shelves' covers fetched ahead (`ArtCache.keepReady` from HomeScreen.offer). NOT BUILT here: Local, please build, then cold-start on the emulator and check Home draws with covers. The "search covers slow" part: not reproduced here; worth a look at the phone diag log.

## 2026-11-06 Local (midday)
- b112 released + deployed (rollback flacie-web:rollback-20261106-pre-b112). SpotifyPlaylists: Deezer/SoundCloud per-artist answers come from saved lists (WebPlaylistStore, Subject = artist) and refresh in the background; sources asked side by side, FirstAnswer 2.5 s. Phone: RemoteArt (disk cache RemoteArtFiles in cacheDir/remote-art, single flight, prefetch on results). DownloadCovers (data/download-covers.json): search downloads keep their catalogue cover (Library.Merge applies; /art jf key of that song redirects to it; phone sends artUrl to /api/download).
- Import rows: Matching.SameSongLoosely pairs "Scatman" with "Scatman (ski-ba-bop-ba-dop-bop)" (web page and ImportManager.Have).
- ArtistResolve after owner's review: no split when a later part starts with "the", "/" kept next to a 1-2 letter side, album artist leads, parts deduped by folded key, capitals changed only when Capitals() scores better. Owner must press "Check again".

## 2026-11-06 Local (late morning)
- b111: Hi-Res upgrades dropped downloaded songs from playlists (owner's rally: Jellyfin playlist 72/99; all 25 missing were "[Hi-Res]" swaps; the phone mirror pushed the dead Jellyfin id). Fixes: ImportManager.Heal (once per done import, ImportJob.Healed, runs after sign-in), UserSession.RepointPlaylists on upgrade (+ profile saved), phone PlaylistMerge.mirrorId (dead id -> title/artist; never before the library loads).
- DJ: PlaylistAndThen ("pop playlist and shuffle pop" = Pop + shuffle), ArtistFits ("by corona" never DeBarge; fetch + DjAskWait(AddTo, Shuffle) applied on land by web and phone), "play X and add it" = Play now, OtherRx "the one by X", vague playlist names ask "Which playlist".
- Not done yet: search covers keep the catalogue picture after download/upgrade (needs cover memory or embedding); phone search covers slow; search playlists should show stored lists at once.
- Storage (owner asked): 10.30 TiB used = 11.33 TB; only 117 GB changed in 7 days (Sonarr: Dark 44 GB, One Piece 25 GB). Leftovers: downloads/complete 69 GB (lidarr 48, radarr 18), $RECYCLE.BIN 8.5 GB, .flacie-replaced 4.2 GB (auto-emptied after 30 days). Nothing deleted.
- Junk from the DJ bug in Jellyfin: playlist "Pop playlist and then shuffle the pop" (1 song). Left for the owner.

## 2026-11-06 Local (morning)
- b109: no covers on the phone. /art was cookie-only (RequireAuthorization): the phone's token request was redirected to /login and WebCovers kept the HTML page as the picture. /art now takes ApiEndpoints.CallerAsync (401 for nobody, not a redirect); WebCovers only accepts image/* (not svg). Owner confirmed covers back after the server deploy.
- b110: the web's search on the phone. /api/made returns WebPlaylists (ForSearchAsync / ForFilterAsync with kind=playlists, genre, decade) as cards (id "web:source:id", picture); /api/made/featuring?artist= (ListsWithArtist); /api/made/web:... opens a list with every song, missing:true for ones the library lacks (Interest Opened). Phone: one-choice chips incl. Playlists + genre/decade from the library (ListFilters), "Lists with X in", missing songs greyed via SongList(missing=), ArtistPage as the web (copies across albums, 2 per album), lyrics LRCLIB exact first (cache header "Source	2", old "Jellyfin" refetched), DjStatus bar above the mini player.
- Lesson: don't run web smoke/dotnet next to the emulator smoke: b109's first run had an ANR with System UI ANRs too; alone it was 23/23.
- Rollbacks: flacie-web:rollback-20261106-pre-b109, -pre-b110. Server 1881/1881. CoverFilesTest (20 writers) flaked once, passed on rerun.

## 2026-11-06 Local (overnight)
- Whole web smoke on b108: 655 passed, 8 failed -> all fixed: kind chips were radios with aria-pressed (axe), "Show only" is a radiogroup, the DJ's favourites waited on 8 Deezer lookups in a row (now side by side, max 2.5 s, rest in background), device-voice captions show even with no browser voices.
- Smoke data lives in tests/web-smoke/.pub-smoke/.data-smoke: delete THAT before a full run (old imports/playlists pile up otherwise).
- Deployed (rollback flacie-web:rollback-20261106-pre-djfast). Car emulator: b108 installs, car browse screen draws (Home/Playlists/Albums/Artists, Shuffle all).

## 2026-11-05 Local (night, owner AFK)
- Released b107 then b108 (phone/car mic retries the online recogniser when the on-device one fails at once). Both: server 1873/1873, Android tests, emulator smoke 23/23, MSI. Web b108 on frank (rollback flacie-web:rollback-20261105-pre-b108). main = ui/living-sleeve.
- Also tonight: imported-playlist rows use the import's loose match; phone findLoose second pass (baseTitleKey); a restart puts in-flight import songs back to pending.
- Not checked on a real car/phone yet: Android Auto buttons/captions/mic, the DJ bar mic level. Owner tests tomorrow.

## 2026-11-05 Local (round 4 deploy, owner AFK)
- Merged cloud/round4 (server tests 1872/1872) and deployed (rollback flacie-web:rollback-20261105-pre-r4). Earlier tonight: playlist titles with Spotify extras ("- Radio Edit") match the downloaded files (Library.FindLoose second pass), F24 rules fixed, web mic recorder fixes.
- Web smoke: whole suite on round 3 was 648 passed / 8 failed; all 8 fixed or updated (Apple links now read, one heading, the playlist's own list, recorder mic, UA version one token, DJ words shown whenever there is no voice clip). Whole suite not rerun on round 4 yet.
- Owner's "80s - 90s rally" import: 4 songs were cancelled by deploy restarts; reset to pending (backup .bak-20261105-retry), now 99/99.
- Cloud: deploy restarts cancel running imports' in-flight songs (marked failed "Cancelled"): they should go back to pending on restart, not failed.

## Cloud · F26.6 imports show every song (cloud/round4)
- An imported playlist lists every song of its import in order. Missing songs are greyed in their place, can't be played, and show Waiting, Searching, Downloading 40 %, Requested, or Couldn't find it with Retry. When a song lands it becomes a normal row in that place.
- New /imports/{id}/downloads lists only that import's songs, with source, quality, progress and the reason for a failure. It is linked from the playlist page and from the Import row ("Downloads").
- AppHost.Imports (IImports) is set on the server only (ServerImports); the phone app shows nothing new. Tests: ImportRowsTests, ServerImportsTests, smoke 1z-import-playlist.

## 2026-11-05 Cloud (cloud/round4: F26.5 the DJ bar above the player)
- Done: DjBar (MainLayout, in place of DownloadActivity, which joins it): Listening… with the mic's level (recorder's loudness, a wobble with the browser recogniser), Heard: "…" + what it does (DjBarWords: Finding your favourites…, Looking for X…, Making your playlist…, Thinking…), Downloading X…, then the DJ's words timed by its voice.
- Captions moved from the player bar into it (the full-screen cover's caption stays); spoken answers are captioned too (PlayerState.DjAnswerAsync -> OnDjSpeaking). State: PlayerState.DjState/DjHeard/DjDoing (DjBarState).
- Proof: DjBarTests, web-smoke 1z-dj-bar.spec.ts (stand-in recogniser and voice), 1q-dj/mini-player/1i green. Local: phone and car are yours (spec).

## 2026-11-05 Cloud (cloud/round4: F26.4 the DJ's real favourites)
- Done: DjFavourites.Pick counts max(FLACie's real plays, Jellyfin's play count: IDjAskUser.JellyfinPlays from s.PlayStats); hearted and 2+ plays first, then 1 play; fill to 15 with hits: Deezer popularity (15 looked up first), then songs on several of their albums, then newest; songs skipped more than played are left out.
- The DJ says what it did ("You've played 3 of their songs a lot; then their biggest hits.").
- Proof: DjFavouritesTests (Jellyfin counts, hits not the alphabet, skipped out, the line).

## 2026-11-05 Cloud (cloud/round4: F26.3 filters that filter)
- Done: the kind chips (All, Songs, Albums, Artists, Playlists) are one choice (radio group in ResultSections); Playlists shows only playlists (every matching list + the person's own; "More music" hidden), Albums only albums.
- Genre and decade come from the Filters panel and narrow the Playlists view (ForFilter decade added; without outside lists our own are narrowed by name); the genre chips under Playlists are gone; "Genre"/"Other"/"Unknown"... are never offered as genres (ExploreState.NoGenre).
- Proof: web-smoke 1z-search-filter.spec.ts (2 green). Also fixed 1r-search-artist's expected order for Local's "playlists under the Top result" (it failed on ui/living-sleeve).

## 2026-11-05 Cloud (cloud/round4: F26.2 "Lists with <artist> in")
- Done: WebPlaylistPick.WithArtistIn: lists already read (Spotify's, Deezer's read ones, SoundCloud sets with songs) that hold the artist's songs but are not about them, most of their songs first; SongsLabel "Firestarter, Breathe + 2".
- Shown by the new ListsWithArtist component on the artist search (after Playlists) and on the artist page (after Albums). Nothing is fetched for it (IWebPlaylists.ListsWithArtist reads the store).
- Proof: SpotifyEmbedTests (The Prodigy vs Prodigy, lists about them left out, unread sets left out).

## 2026-11-05 Cloud (cloud/round4: F26.1 many real playlists, ours last)
- Done: SpotifyPlaylists.DeezerListsAsync: one Deezer playlist search per artist (kept a day): the editors' best-of with its songs, plus every fans'/editors' list whose name holds the artist (names, size, picture; songs read when opened via FillAsync).
- WebPlaylistPick.ForArtist: real lists (title holds the artist, The kept, or holds 2+ of their songs; 8+ songs; SoundCloud own sets) editors first, then by their songs held, size; same words once; our own only under 4 real ones and after them; up to 15 (2 rows of cards on desktop).
- Only the best-of downloads on an artist search (top half); other lists shown download nothing. Proof: SpotifyEmbedServiceTests (10-list Deezer fixture).

## 2026-11-05 Local (late)
- F24 rules fixed after the owner read the dry-run list (bands split, Bob->B.o.B, P!nk->PNK, &amp;, "feat. Th", WizKid (4)); 323323ec. Not deployed yet: the owner must not approve the old list.
- Web mic: Brave records straight away; speech/quiet judged against the room's noise (15 s recordings).
- Next for Cloud: F26 (docs/waves/specs/F26.md): real playlists, Featuring row, one-choice filters, real DJ favourites, DJ status/caption bar, imports show every song.

## 2026-11-05 Local (round 3 deploy)
- Merged cloud/round3 (server tests 1852/1852) + Local fixes: quiet playlist downloads, LRCLIB-exact lyrics before Jellyfin's .lrc, web mic records + /api/dj/hear (Whisper) when the browser can't recognise (Brave), search layout (playlists under Top result, up to 8, lists holding the artist's songs, 2 per album in top songs), solid band behind the floating search box.
- Cloud's Dockerfile had the entrypoint sed's raw CR turned into a line break (build failed): now writes 
 as text. Cloud: never put raw control characters in files.
- Deployed web + ytdl + filemove (rollbacks *:rollback-20261105-pre-r3, server.py.bak-20261105-pre-r3). ffmpeg in the image, CPU 0.5%.
- F24.2 (phone twin) skipped: once the owner approves Artist names, Jellyfin itself is fixed and the phone reads it. Owner reviews Server admin > Artist names first.

## 2026-11-05 Local (deploy)
- Owner OK'd: round 2 final web deployed (ui/living-sleeve e4c391a3; rollbacks flacie-web/flacie-ytdl:rollback-20261105-pre-r2final, compose .bak-20261105-pre-r2final).
- filemove server.py redeployed (/purge; backup server.py.bak-20261105-pre-r2final); ytdl server.py was already current.
- FLACIE_CLEANUP_ROOT=/media/Movies (the file mover's /data = Jellyfin's /media/Movies). Music share has 2.4 TB free, so the clean-up waits until under 100 GB.
- healthz ok, web 0.2% CPU; two stale saved sign-ins fail at Jellyfin (401), the owner's loads fine.
## 2026-11-05 Cloud (cloud/round3: F24.1 artist names fixed in Jellyfin; round 3 done; unit suite 1846/1846)
- Done: ArtistResolve.Plan (per song: "X" -> "The X" by shared album/title or the catalog; "X of Y" -> X; "A feat./&/, B" -> A + B featured, album artist stays); ArtistResolveService: dry-run list in Server admin "Artist names", nothing written before Approve; each write keeps Jellyfin's values (read at write time) for Undo one/all; after approval new songs are fixed hourly with the approver's login.
- Write = Jellyfin item update (GET the item, replace Artists/ArtistItems/AlbumArtist/AlbumArtists, POST /Items/{id}); music files untouched. Grouping in search/artists/made lists/DJ follows Jellyfin after its next scan.
- web-smoke on .NET 9: 1-pages, 1z-import-page, 1r-search-artist, 1z-made-playlists, 1z-settings green (60).
- Local: F24.2 phone twin (rules in ArtistResolve.cs); redeploy ytdl + filemove (F25); rebuild the web image (ffmpeg); owner reviews the Artist names list before approving.

## 2026-11-05 Cloud (cloud/round3: item 13 vibe pass for every format)
- Done: VibeFillService takes FLAC, MP3, M4A, AAC, OGG/OGA, Opus, WMA, WebM, MP4 (AudioLevels.Readable); non-FLAC songs are decoded by ffmpeg (temp file, mono 8 kHz, 90 s limit) into WaveShape.PcmLevels (0.1 s windows, dB between the 2nd and 98th percentiles), then the same vibe and DJ intro.
- Server Dockerfile installs ffmpeg in the runtime image (FLACIE_FFMPEG overrides the path). Without ffmpeg a song is simply left for later.
- Proof: VibeAllFormatsTests (fixtures quiet-then-loud.mp3/m4a/ogg/opus/wma made from the FLAC fixture). Local: rebuild the web image (ffmpeg adds ~80 MB).

## 2026-11-05 Cloud (cloud/round3: F25.3 SoundCloud artists' own sets)
- Done: an artist search asks flacie-ytdl /artist-sets (account named exactly like the artist, its sets; remembered 7 days, gate 1/s, 6 h rest); SoundCloudSets makes WebPlaylists (verified, names only, dups and owned albums dropped, remix/B-side sets their own lists beside a Spotify/Deezer best-of).
- Songs are read (/playlist) only when a set is opened or played (IWebPlaylists.FillAsync; the list page fills it); then the F23 download policy applies.
- Proof: PlaylistLinksTests (Prodigy sets fixture), SpotifyEmbedServiceTests (quiet search, songs only on open, 7-day memory).

## 2026-11-05 Cloud (cloud/round3: F25.2 tidy Import page)
- Done: Import.razor in the new look: paste box first with the service's mark as the link is recognised, a smaller file drop, Replace as a real switch, body font that wraps, past imports as compact rows (source mark, "N of M added", date), finished ones over 7 days folded under Older (ImportJob.IsOld / Added / Source).
- Proof: web-smoke 1z-import-page.spec.ts (375 px and 1600 px: no sideways scroll, switch visible and works, marks; a file import row), ImportPageTests. Styles in living-sleeve.css (.imp-*; the app has no scoped-CSS bundle).

## 2026-11-05 Cloud (cloud/round3: F25.1 any playlist link)
- Done: PlaylistLinks recognises YouTube / YouTube Music (list=), SoundCloud sets, Apple Music playlists and albums; anything else gets PlaylistLinks.Refusal (the list of what it reads). YouTube titles cleaned ("Artist - Song (Official Video)").
- AppleMusicPage reads the real page fixture (50 songs, "Solar Eclipse" by Drake). PlaylistLinkReader: Apple from its page; YouTube/SoundCloud via the file mover -> flacie-ytdl POST /playlist {url} (hosts checked, 500 max).
- Local: redeploy tools/ytdl/server.py (/playlist, /artist-sets) and tools/filemove/server.py (forwards them; /health lists them).

## 2026-11-05 Local (night)
- Merged cloud/round2 final (F21.1, F22, F23) into ui/living-sleeve; server tests 1802/1802 (CoverSourceTest already used 192.0.2.10; PlayerListenTests passed under full load here).
- F23 checked on the real sites: 13/14 Spotify ids fine, Rock Classics had a 23rd letter (500) - fixed + test that ids are 22 letters. Real embed page saved as TestAudio/embed/todays-top-hits-real.html (layout matches the reader). Deezer best-of now needs the exact artist name (real answers hold "100% Kay The Prodigy", "100% Prodigy" and fans' lists).
- Phone: Ask step already counts as asked back (DjAsk.kt:65), renameplaylist handled, the phone never builds fallback lookup keys: nothing to change.
- Waiting on the owner's OK: web deploy of round 2 final, filemove /purge + ytdl redeploy, FLACIE_CLEANUP_ROOT. Cloud next: cloud/round3 = item 13 (vibe pass non-FLAC), F24.

## 2026-11-05 Local (evening)
- b106 released (0e66dfa5, APK 121 + MSI 1.0.106); phone fix: weak-signal switch waits for 2 MB of real measurements, Opus failure retries on FLAC. main = ui/living-sleeve.
- Cloud: start cloud/round3 from ui/living-sleeve 0e66dfa5 or newer (F21.1, F22, F23, item 13, F24).

## 2026-11-05 Local
- Merged cloud/round2 + all phone work; b106 web deployed (SoundCloud on, rollback flacie-web:rollback-20261105-pre-b106). APK held: smoke 21/23 (40-skip burst, pause state 7), being fixed.
- Found and fixed a KeepAlive ping-pong in Connect.cs (311,539 msgs/min; web 150%->1% CPU, Jellyfin settles). Census line stays in the log for now.
- New spec F24 (artist names fixed in Jellyfin after an approved dry-run list) is Cloud item 14.

## 2026-11-05 Cloud FINAL (cloud/round2): round 2 items 1-12 done
- Unit suite 1794/1796: NoRealServerInTestsTests fails on android CoverSourceTest.kt:14,32 (names the owner's server; Local's file from the merge); PlayerListenTests "Songs just fetched for Autoplay..." fails only under full-suite load (passes alone), as PlayerDjLiveTests did once.
- web-smoke on real .NET 9: every spec I touched or could affect is green (1c-api, 1g-burst, 1h-queue, 1m-ipod, 1n-search-typing, 1q-dj, 1r-retro-scenes, 1r-search-artist, 1z-made-playlists, 1z-song-start, mini-player); the rest of the 659 were not rerun after the fixes.
- Local: fix CoverSourceTest.kt host; redeploy tools/filemove (purge) and tools/ytdl; set FLACIE_CLEANUP_ROOT and FLACIE_SOUNDCLOUD=on on frank; save a real Spotify embed page as the fixture; mirror DjConversation.AskedBack on the phone.

## 2026-11-05 Cloud (cloud/round2: F23 wired into server and web)
- Done: SpotifyPlaylists (IWebPlaylists): Spotify editorial lists by fixed id (embed page), Deezer "100% <artist>" best-ofs, our own made lists as fallback; Search's "Playlists for you" row comes from it, a Playlists filter with genre chips, /made/web:<source>:<id> pages that queue missing songs (opened / played); artist and popular lists queue by the policy.
- Server admin: Playlist sources switches (Spotify, Deezer, SoundCloud; a failed source shows when it rests until). FLACIE_WEB_PLAYLISTS=off = own lists only (web-smoke sets it).
- Not done: SoundCloud playlist reading (the rules are in WebPlaylistPick; yt-dlp has no playlist search) and Spotify This Is lists (findable only with a Spotify account) - Deezer's 100% lists stand in.

## 2026-11-05 Cloud (cloud/round2: F23.2 policy, F23.3 own Best of, F23.4 clean-up)
- Done: PlaylistFetchPolicy (opened/played all missing in play order, This Is top half first, popular 2, nothing automatic under 100 GB); MadePlaylists Best of: one version, no instrumentals, half the artist's songs (10-50).
- F23.4: LibraryCleanup plan + LibraryCleanupRun/Service (every 6 h, only under 100 GB free) moves via /retire to .flacie-replaced; new file mover route /purge empties it after 30 days; Server admin "Clean-up" lists the moves.
- Local: redeploy tools/filemove/server.py (adds /purge) and set FLACIE_CLEANUP_ROOT to Jellyfin's path of the music folder the file mover serves (else only FLACie-filed files are cleaned).
- Next: F23 server wiring (fetch Spotify/Deezer lists, search rows, Playlists filter, downloads as you go).

## 2026-11-05 Cloud (cloud/round2: F23.1 core)
- Done: SpotifyEmbed.Parse (embed __NEXT_DATA__, first 100, text only, 200-char cap, only open.spotify.com ids), WebPlaylistStore (one file, max 2,000, 30-day drop, daily re-read), WebSourceGate (1/s, 6 h rest, switches), WebPlaylistPick (one per purpose Spotify>Deezer>SoundCloud>own, SoundCloud bar, row sizes, Playlists + genre filter).
- Local: open.spotify.com is blocked from this sandbox, so the fixture (tests/FLACie.Tests/TestAudio/embed) follows the known page shape; please save one real embed page there and check SpotifyEmbedTests still parse it.
- Next: F23.2 download policy, F23.3 own Best of, server wiring, F23.4 clean-up.

## 2026-11-05 Cloud (cloud/round2: F22 DJ favourites)
- Done: "play my favourite prodigy songs" / "my favourite 90s songs" / "...songs by X" (rule + model action favourites_of): DjFavourites ranks by F21 real plays (PlayCounts.Scores), hearted and repeats first, early skips last, filled to 15 by popularity; scope = artist, album, genre or decade. "play my favourites" stays the Loved list.

## 2026-11-05 Cloud (cloud/round2: PNG made cover, smoke fixes, F21.1)
- Done: /art/{key}?fmt=png = made cover as PNG (240 px, 1000 px with s=full; CoverPng, own encoder) 46590a1; F21.1 PlayCounts (same rules as PlayCounts.kt) + web Home: Frequently played first, Listen again (every played song) after Daily Mix, as on the phone.
- web-smoke fixed on .NET 9: 1c-api (made cover only for song keys), mock Prodigy songs stream and sort after Harbour, 1h-queue reads the list without Recommended songs (failed on ui/living-sleeve too), 1m-ipod counts only letters inside a clipped line.
- Tip: web-smoke keeps the server's data in .pub-smoke/.data-smoke; delete it when the mock's songs change. Merged ui/living-sleeve (F21.2, F22/F23 specs). Next: F22, F23.

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

## 2026-11-06 21:05 (local desktop Claude): b117 released, server deployed
- Silent DJ root cause: the phone asked about the next song only in the last 30 s by the song's TAGGED length; files shorter than their tags (owner: 3:20 song ending at 41 s) never reached it. Now `DjCoordinator.lengthMs` uses ExoPlayer's duration; seeks re-time the ask; skip/first-song waits 8/10 s (Kokoro p90 4.3 s, max 6.5 s); First song introduces Search/Home plays. Proved on the emulator with `tools/loadtest/dj-android.mjs` (asks, clip, said, every song).
- Also in b117: catalogue song page + grouped versions (phone Search), DJ remembers the version picked with "the other one" (data/listen/<id>.chosen.json).
- Rollback tag: flacie-web:rollback-b116.

## 2026-11-06 22:55 (local desktop Claude): b118 released (phone only)
- Owner's b116 car log: DJ lines made but "voice clip failed to load: HTTP status". Cause: header choice by plain startsWith; FLACie Web (frank:8443) matched the file mover's https://frank base and got X-Api-Key → 401. Fix: `ServerUrl.under` (path boundary) and FLACie Web checked first, in PlayerController's data source and WaveAnalyzer.headersFor.
- "Playlist plays recommendations": the 80s-90s rally car session (shuf, 94 songs) played only playlist songs; not reproduced. b118 logs "<song> is one Autoplay added, not from <list>" to catch it.

## 2026-11-09 Local: phone screenshots for the gallery
- 57 screenshots of the b123 phone app on the emulator in `docs/screens-phone-b123/` (phone size, and `-1840x2208@420` = unfolded): every tab, Songs, an album, Playlists, Offline music, Search (keyboard up and after), Now Playing with Lyrics, Info, Options, More, Up next, Add; a song's menu (long press); Settings top to bottom (settings, settings-2..9); Retro scenes 1-6.
- Missing: the DJ settings sheet (the emulator test has no FLACie Web, so no DJ button), and Retro + Add at the unfolded size (buttons not found there). Tour: `ipodsync/tools/loadtest/v1-shots.mjs` with SIZES=native,1840x2208@420.
- Local is at 94% weekly: stopping here until the reset (11 Nov 18:00).

## 2026-11-09 Local: cloud branch merged, b124 NOT released
- Merged claude/exciting-ride-2qa9ry into ui/living-sleeve; fixed missing itemsIndexed imports (PlayerUi.kt); version bumped to 1.0 b124 (Android 139). Server 1994/1994, Android unit + release build OK, emulator smoke 23/23 on rerun (first run: 2 playback fails, likely the new row entrance animation vs the driver tap).
- Web: 105 passed, 5 failed, and still failing when rerun alone: 1d-g2 keyboard (Space/N), 1q-dj Aura-2 voices, 1z-discman-fit (1024x768), 1z-dj-ask endpoint, 1z-dj-mic-twice. Cloud: fix these, push; local releases b124 after the weekly reset (11 Nov 18:00). Nothing deployed.

## 2026-11-09 Local (96% weekly): handing the b124 release to a new local session
- Read docs/HANDOFF-b124-release.md: merge the cloud branch again (its last batch), build, smoke, web tests, MSI, deploy, release.
