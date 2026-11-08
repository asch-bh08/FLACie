# Overnight 7–8 Nov 2026: the owner's list, and progress

The owner is away overnight and wants a beta in the morning with fixes and new things, **on web, Android and Windows**. Watch the limits: hand off at 96% weekly. Log progress here as you go, so anyone can pick it up.

## The owner's list (7 Nov, as asked)

1. **Equalizer back** (web Now Playing lost it from view).
2. **Even playback controls**: the DJ button on the left so both sides balance, on web like the phone.
3. **Downloads line above Now Playing** ("downloading 5 now"): admins only.
4. **Search: playlists that contain the song.** Searching "dreamer" should show the playlists the song is in (the owner's own included), the same for "with me", "eminem", "aura". Recommended playlists too.
5. **Search: a label or small symbol on every result** (Song / Album / Playlist / Artist), like Spotify, so you can tell which result is the song you want. A screenshot showed result rows with only the label and no title: check that.
6. **Full UI overhaul** (phone and web): menus too complicated, too many unexplained icons, everything square (use circles etc. where right: artists, round buttons), smoother and more fluid animations (not just fades), the sidebar, the settings, the Home page's menus and style, and the pages that never got much attention.
7. Beta in the morning, on web, Android and Windows.

## Done

- 7 Nov evening, before this list: b121 out; the web search preview; the volume limit on web; the artist-names fixes approved; frank's disk freed (61 GB).
- **ntfy: no play notices for hms.** This covers FLACie (Dashboard › Notifications › "Don't announce plays by"), and the Jellyfin app plays: `/opt/ntfy-digest` config `jellyfin.quiet_users` with a small code change. Backups are `*.bak-20261107`.

## Progress log

(newest last)
- 23:20 — the session stopped at the owner's request (limits). Handoff: docs/HANDOFF-2026-11-07-night.md. None of items 1–7 above are started yet.
- 23:55 — **Item 1 done (EQ on web).** Now Playing's bottom bar has an "EQ" pill (phone: in Options) with presets as words (Flat, Rock, Pop, Jazz, Classical, Vocal, Bass boost) and five sliders; same EQ as the Retro scenes. Tests: SceneEqTests 7/7, web 1z-eq + 1j-speed-sleep 8/8. The phone already had its own Equalizer sheet.
- 00:20 — **Item 2 done (even controls).** Web and phone: DJ · shuffle · prev | PLAY | next · repeat · mic. DJ button moved out of the top bar. On 1101–1440 px the PC bar's volume is the mute button only, so nothing sits under the controls. Web 1q-dj, 1z-dj-ask, 1x-new-look pass (one 1q-dj Dashboard flake passed on rerun). **Not ours:** `1x-new-look` "the next two songs get their shapes…" fails the same way on the commit before tonight's work.
- 00:20 — **Item 3 done (downloads line admins only).** Web: AdminAccess check. Phone: the server's downloads only for admins; offline saving (the person's own) still shows. Android unit test added.
- 00:20 — **Item 4 done (search: your playlists holding the song).** FLACie.Core PlaylistHits + Kotlin twin (tests both), card says "Has “Dreamer”" / "2 songs by Eminem". Web 1z-search-lists + search tests 7/7. Phone: "Your playlists" row in Search.
- 00:25 — **Item 5 done (kind labels).** Web: SONG/ALBUM/ARTIST/PLAYLIST tags; phone: "Song · …", "Album · …". Couldn't reproduce the owner's "label only, no title" rows in web search (all results, artist layout, Playlists kind, 390 and 1280 px) or the phone search; 1z-search-tags now fails if any result row or card lacks a visible title. Emulator: dj 3/3, search 9/9 (debug build).
- 00:35 — **Web deployed to frank** with items 1–5 (rollback tag `rollback-b121web`; kept 3 rollbacks, pruned). healthz ok. Owner: reload the web page.
- 00:55 — **Item 6, part 1 (menus):** song menus grouped on web and phone (Now Playing 13 → 9; playlists behind one Add to playlist; Not for me ›, The DJ and this song ›; phone rarer choices under More…). Part 2: web bar's Retro and Mini player moved into a worded More menu. Web tests touching menus updated and passing (75+39); the known 1x-new-look wave-shape failure is unchanged (fails before tonight too).
- 01:15 — **Item 6, part 3–5:** web motion (springy menus, rows/cards flow in, press-in covers; transform/opacity only, once each, reduce-motion respected); Settings' Volume limit row styled, Devices/Jam sentences in sans, smaller idle spectrum on Headphone test (phone); phone Home without the duplicate Settings cog. Artists were already circles on web and phone; phone sheets already spring. Two timing flakes under load (1w-living-sleeve seek shape, 1y-home menu, 1z-settings Dashboard) all passed on rerun.
- 01:15 — Starting the b122 release checks on the final build (server tests, changed web tests, Android unit, four emulator drivers, screenshots).
- 02:50 — **b122 released** (pre-release, APK + Windows MSI): https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-b122 at d812b66c. Checks on the final build: server 1,951/1,951; Android unit 1,001/1,001; web 313/319 in one run (the mini-player test was updated for the moved button and passes; 1i-download-tags passed on a rerun; 1-pages "Quick picks" + "bottom bar 375/320" and 1q-dj "Aura-2 voices" fail the same way on 93b7663a when run after other tests, pass alone: test-order problems, not this beta); emulator release smoke 23/23, search 9/9, DJ 3/3; debug playlists 23/23 + car 9/9; Windows app starts, /healthz ok, closes cleanly. Nothing reverted. Screenshots looked at: web NP/EQ/menus/search/settings/devices; phone NP with and without DJ, search, up next.
- 02:52 — **Web deployed to frank** (rollback `rollback-b122a`; kept 3). healthz ok. Owner: reload the web page.
- 08 Nov — Session handed over at the owner's request: summary of done / not done at the top of docs/HANDOFF-2026-11-07-night.md (§0).
