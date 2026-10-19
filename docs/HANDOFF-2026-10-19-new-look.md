# Handoff: FLACie's new look (2026-10-19)

For the next Claude Code session or account. Read this first, then the design files. The owner is Ashley ("the owner"): short plain replies, fast, ship pieces as they finish, never Haiku, agents allowed (2-3 at once, Sonnet for well-specified jobs, Opus for hard design).

## The goal (brief)
FLACie is heading to a 1.0 release at beta 100 (spec: `docs/superpowers/specs/2026-10-19-road-to-1.0-ux-design.md`). The owner wants a FULL UI overhaul: not a reskin. The old app's buttons, chips and layouts must not show through. It starts with the full-screen Now Playing, then the whole app (Home, library, album/artist, playlists, search, settings, the now-playing bar) on web, Windows (same web code) and Android.

## The approved design ("I")
Files: `docs/superpowers/specs/design-2026-10-19/` (mockup HTML with inline styles; `{{accent}}`, `{{ground}}`, `{{tone}}`, `{{tonefg}}` stand for colours taken from the cover). Live canvas (the owner's, private): https://claude.ai/artifact/NTuDBmMTNLBrF8EYZXi67Z (bottom row "Your mix").
- `I-NowPlaying.dc.html` (phone): APPROVED ("that's the one"). Cover full-bleed across the top ~520px fading into a ground colour from the cover; big Instrument Serif title with the last word in italics; artist in Archivo bold + a DM Mono format chip; tinted circle heart; controls: plain shuffle, tinted pill previous, a RING seek bar (the song's loudness as radial bars around the play button, played part in the accent colour) with a rounded-square accent play button inside, tinted pill next, plain repeat; DM Mono time under; outlined Lyrics and Up next buttons. Top bar: close, DM Mono "ALBUM · TRACK n" pill, Retro.
- `I-Home.dc.html` (phone Home): drawn, not reviewed yet.
- `I-Desktop.dc.html` (PC full-screen player): NOT settled. Latest version: cover card + title left, a wide panel right (Up next / Lyrics / Info: it must hold the Info tab's live graphs: spectrum, VU, spectrogram, visualiser, and the lyrics), and a now-playing bar along the bottom with the controls and a smaller ring. The owner said "idk" to it: build it for real and let them try it.
- `I-Bar.dc.html`: the app-wide now-playing bar (PC), drawn, not reviewed.
- `rejected/`: earlier attempts the owner rejected (for reference of what NOT to do). H02 (full-bleed cover fading into colour) and H05 (Material You tonal buttons) are the ones whose parts were chosen.

## Owner's rules for the design (from feedback)
- No main playback controls in the bottom ~100px of a phone screen (home-gesture area).
- No lyrics on the main Now Playing screen; a Lyrics button opens them.
- PC Now Playing must have everything the old one has: cover, title/artist/album, source/format/sampling/bitrate (can live in Info), heart, rating, speed, sleep, sound, more, transport, seek, volume, Up next (Playing from, Autoplay switch, per-row move up/down, play next, remove), Lyrics, Info with live graphs. Features can be reorganised, not dropped. Nothing cramped, nothing overlapping, no abrupt cover cut-off.
- The seek bar is STATIC: the song's own loudness shape, not a live meter. It must not take seconds to appear: compute the next songs' shapes in the background.
- Fonts (approved, downloaded, OFL): Instrument Serif, Archivo, DM Mono in `ipodsync/src/FLACie.Server/wwwroot/fonts/ls/fonts.css`.

## Where the code is
Branch `ui/living-sleeve` (pushed). Not deployed, not merged.
- Web full-screen player today: `ipodsync/src/FLACie.App/Components/Shared/NowPlaying.razor` (root classes `np ls`) + `ipodsync/src/FLACie.Server/wwwroot/living-sleeve.css`. This is the previous attempt ("Living Sleeve"), which the owner rejected as a reskin. Its LOGIC is good (queue editing, lyrics with synced highlight, Info, rating/speed/sleep, cover override key, colours from the cover); rewrite the markup + CSS to design I.
- `flacie.js`: `npAccent` (cover colours), `npWave` / `npWaveMake` (loudness shape: GET/POST `/api/wave`, computed in the browser because the owner's Jellyfin can't transcode; stored on the server under data/waves). Change drawing to the ring; add next-song prefetch.
- Server: `MediaEndpoints.cs` `/api/wave` and `/stream?wave=1` (quiet full read, not a play).
- Android: `android/app/src/main/java/com/ipodemu/player/LivingSleeve.kt` (+ SleeveModel.kt, WaveAnalyzer.kt): the previous attempt in Compose; rebuild to design I after the web one is accepted.
- Tests: `ipodsync/tests/web-smoke/1w-living-sleeve.spec.ts` and `.ls-*` selectors across specs. Run with `MOCK_PORT=8296 WEB_PORT=5390 FM_PORT=8297 npx playwright test ...` from `ipodsync/tests/web-smoke`. Before trusting failures check `netstat -ano | grep -E ':(5390|8296|8297) .*LISTEN'`: a stale mock left on 8296 once caused fake failures; stop only by PID. Flaky under load: 1i "Recently downloaded", 1o preamp, 1r hifi.
- Local preview against the owner's real Jellyfin: `.claude/launch.json` "flacie-real" runs `ipodsync/tests/web-smoke/run-real.cmd` (publish to `.pub-real` first: `dotnet publish ipodsync/src/FLACie.Server -c Release -o ipodsync/tests/web-smoke/.pub-real`). The owner signs in themselves; never type their password.

## Tasks, in order
1. Build design I on the web (phone exactly as approved; PC from the latest `I-Desktop` + `I-Bar`), TDD, screenshots at 390x844, 1280x720, 1440x900 compared with the mockups. Show the owner in the browser pane with real music. Iterate on their feedback.
2. Ring shape ready instantly (prefetch the next 2 songs' shapes while one plays; not on mobile data).
3. The rest of the web app in the same style (Home per `I-Home`, the bar per `I-Bar`, then library/album/artist/playlists/search/settings): draw a mockup first for each new screen, get a yes.
4. Android to match (emulator only: never connect to the owner's phone).
5. Release: owner's go-ahead needed for each deploy (it restarts the server: tell them to reload) and for releases. Recipe: memory file ipod-desk.md / docs/private/HANDOFF.md.

## Also done today (for context)
- Beta 71 released (one APK with Retro inside FLACie, MSI 0.71.0), web deployed.
- Audit: `docs/audit-2026-10-19.md` (fixes for debug pages pushed; frank has FLACIE_JELLYFIN_URL set).
- Web fixes deployed without the new UI from branch `fix/web-72` (rollback tag flacie-web:rollback-2026-10-19-pre-fix72).
