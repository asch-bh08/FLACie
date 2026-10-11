# G2 web fixes (branch fix/web-g2, from release/beta50)

## Changes (G-win.md numbering)
- BUG 1: full-screen Now Playing on wide/short windows: the cover now takes only the height that is left (container query, like the phone layout), so it never overlaps the header, title block, chips or the player bars; the left column scrolls as a last resort. 360x360 portrait: chips and volume hidden, cover shrinks, title, seek and controls stay visible. Checked by screenshots at 900x700, 1280x600, 1400x700, 375x667, 360x360 and by bounding-box tests.
- BUG 4: wordmark hidden under 1000 px (mark stays). The "Listening on <device>" bar slims down at heights <= 760 px (no cover, one text line, smaller buttons). Window minimum height was not changed (shell, not done).
- BUG 5: the A-Z rail ends above the player bar and above the remote strip (`--remote`); hidden under 480 px height (letters were unreadable).
- BUG 6: `html.buffering` is set from the moment a song is asked for until it has data (readyState < 3, waiting/stalled/loadstart events); the play buttons show a spinner ring. Earlier prefetch of the next song was not changed (the audio element is already `preload=auto`; the twin prefetch is untouched).
- BUG 7: IpodSync.Maui (Platforms/Windows/App.xaml.cs): once the new profile folder exists, the legacy `IpodSync.Maui.exe.WebView2` folder under AppContext.BaseDirectory is deleted (that exact name only, best effort). Not built or run here (MAUI Windows build not part of this task).
- BUG 8: unknown addresses answer 404 with a FLACie "Page not found" page (NotFoundPanel: inside the layout when signed in, standalone when signed out) and a Go home / Search link. In-app links to unknown routes use the Router NotFound. Only HTML GET requests outside /api, /art, /stream, /auth, /debug, /_framework are re-executed, so API 401/404 stay bodyless. `UseRouting()` now sits after the status page middleware (needed for re-execution).
- BUG 9: Settings > About: version, server URL, data folder, link to /licenses. `/licenses` serves THIRD-PARTY-NOTICES.md (linked into the server build/publish from the repo root when present; a pointer text otherwise, e.g. in Docker). Version `1.0-beta50` from the assembly informational version (`FLACIE_VERSION` env overrides), class `FLACie.Server.AppInfo`.
- BUG 11: artist and album routes compare case-insensitively (genre already did).
- O1: Space play/pause, Left/Right seek 5 s, N next, P previous. Ignored while typing (input/textarea/select/contenteditable), with Ctrl/Alt/Meta, and Space on buttons/links.
- O5: no change. The Jellyfin device name is already "FLACie Web" (JellyfinClient default; client name `ipodplayer` untouched). "Edge on Windows" on the Dashboard is the client registry's name derived from the browser user agent (browser + OS), not what is sent to Jellyfin, so it is not a one-line change.
- General sweep: no page overflows sideways at 1400x900, 768x1024, 375x667, 320x568 (19 pages, tested). Cover art, tables and nav looked fine in the screenshots I took; I did not do a pixel-by-pixel look at every page at every size.

## Tests
- .NET: 87 passed (was 84; new tests/FLACie.Tests/G2Tests.cs: version string, licences text, shortcuts and wordmark rule in static files).
- Playwright: 107 passed (was 85; new tests/web-smoke/1d-g2.spec.ts: 6 unknown routes + in-app unknown link + signed-out 404, case-insensitive artist, About/licences, Now Playing geometry at 5 sizes, 900x600 wordmark and A-Z rail, buffering spinner (delayed stream), keyboard shortcuts, 4 overflow sweeps).

## Not done / notes
- BUG 2, 3, 10 and the other bugs in G-win.md are not part of this task (BUG 10 was fixed in G1).
- Windows shell cleanup (BUG 7) and the minimum window height were not exercised here.
- Seek shortcuts (Left/Right) are not asserted in a test (the mock songs are 2 s long); Space and N are.
- One 320px bottom-bar test (G1) failed once in a full run and passed on rerun and in the next full run (timing on a cold server).