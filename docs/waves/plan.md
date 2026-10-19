# Wave plan: design I across the app (overnight, 2026-10-19 → 20)

Approved by the owner in chat ("send agents and use wave planning … I'll be AFK overnight … commit/push at milestones"). Branch `ui/living-sleeve`.
Done before this plan: the full-screen player (web), the shape prefetch, Home and the shorter menu (bafaab0).

## Rules every phase follows
- Its own git worktree and branch; commits there; never pushes. The orchestrator merges, runs the suite and pushes.
- Mockup first: `docs/superpowers/specs/design-2026-10-19/J-<screen>.html` (static, the look of J-Home-pc.html / J-Home-phone.html), then build to it. The owner reviews in the morning.
- Design I: Instrument Serif titles (last word in italics), Archivo text, DM Mono small labels, outlined chips with the chosen one filled, tinted pill buttons, rounded covers with soft shadows. Nothing dropped: features may be reorganised, never removed.
- CSS only in the phase's own stylesheet (already linked in App.razor). TDD with Playwright specs on the phase's own ports. Never Haiku.

## Waves

| Wave | Phase | Owns | Model | Why |
|---|---|---|---|---|
| 1 | P1 player bar (I-Bar) | PlayerBar.razor, VolumeControl.razor, design-i-bar.css, new spec 1z-bar | sonnet | drawn design, one component |
| 1 | P2 browse: Search results, Explore tabs, Songs/Albums/Artists/Genres/Charts lists, TrackList rows | ResultSections, ExploreBar, MusicTabs, TrackList, AlbumGrid, Songs/Albums/Artists/Genres pages, design-i-library.css, spec 1z-library | sonnet | restyle, well specified |
| 1 | P6 search box loses letters typed before the page is live (1n flaky) | TopBar.razor, TextBox.razor, the textBox code in flacie.js, 1n spec | opus | real bug, needs diagnosis |
| 2 | P3 album, artist, genre pages | Album/Artist/Genre pages, design-i-detail.css | sonnet | restyle |
| 2 | P4 playlists, a playlist, favourites | Playlists/Playlist/Favorites pages, design-i-playlists.css | sonnet | restyle |
| 2 | P5 settings, account, dashboard, downloads, import, devices, jam, login | those pages (FLACie.Server/Components/Pages), design-i-settings.css | sonnet | restyle, many small pages |
| 3 | P7 Android: Now Playing to design I (emulator only, never the owner's phone) | android/app/.../LivingSleeve.kt, SleeveModel.kt, WaveAnalyzer.kt | opus | Compose rebuild, hard |
| 4 | Gate | — | orchestrator | merge, full suite, screenshots, push |

Waves 1 and 2 split only to keep three agents at a time (usage). The critical path is P2 → P3 (P3 styles TrackList rows inside pages that P2 defines).

## Gates
- After each merge: the phase's spec plus 1x, 1y, 1d, 1-pages on the orchestrator's ports; `dotnet test` when C# outside the pages changed.
- Wave 4: the full web-smoke suite (known flaky: 1i "Recently downloaded", 1o preamp, 1r hifi, 1f load) and the 712+ unit tests; screenshots at 390x844, 1280x720, 1440x900.

Log: `docs/waves/log.md`.
