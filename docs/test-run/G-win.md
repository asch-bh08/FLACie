# Test run 2026-10-11, workstream G-win (installed Windows desktop app 0.50.0)

Target: `%LOCALAPPDATA%\Programs\FLACie\IpodSync.Maui.exe` (MSI-installed, per user, FileVersion 0.50.0.0), the MAUI/WebView2 shell that starts its own
`FLACie.Server.exe` on `127.0.0.1:5287` and shows FLACie Web. Machine: Windows 11 Pro 10.0.26200, window 1400x900 (min size the app allows: 900x600).
Signed in as the owner (admin) through the existing Quick Connect session; the session survived every kill/relaunch below.
Driven with Playwright over CDP (`--remote-debugging-port=9333` via `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`), native screenshots with the window-only
`PrintWindow` helper. Screenshots are in the session scratchpad (not committed). Audio cannot be heard: playback was verified through the page's
`<audio>` object (queried with CDP `Runtime.queryObjects`): `currentTime` advancing, `paused`, `buffered`, `duration`, `readyState`, `error`.

Data created or changed: playlist `TEST-G-win` (created, renamed, reordered, deleted); one favourite added and removed again; Settings toggles flipped and
restored (Fetch ahead was OFF for most of the run to stay within the download cap, ON again at the end); notification chip "Server started" off/saved/on/saved.
Downloads: see "Downloads and side effects" below. No iPod was connected (iPod manager tab only read, nothing pressed).
End state: debug-port instance killed, app relaunched normally (no env var), running.

## Results

| # | Item | Result | Evidence |
|---|------|--------|----------|
| 1 | Installed folder: LICENSE, THIRD-PARTY-NOTICES.md, licenses\ (Apache-2.0, GPL-3.0, LGPL-2.1, LGPL-3.0), no `.pdb` | pass | files present, 0 `.pdb`. `workloads*.json` still shipped (harmless). Legacy `IpodSync.Maui.exe.WebView2` profile (185 files) left over from the 0.49 install (BUG 7) |
| 2 | Launch, embedded server | pass | `/healthz` 200 in 2.5 s after start; server listens on 127.0.0.1 only; two pages visible to CDP (FLACie web view and the iPod-manager hybrid page) |
| 3 | Signed in (Quick Connect) kept after kill, graceful close and relaunch | pass | `flacie` cookie is persistent; profile now lives in `%LOCALAPPDATA%\FLACie\webview2` (old BUG 1 fix confirmed) |
| 4 | Graceful close stops the server child | pass | both processes gone within 5 s of `CloseMainWindow` |
| 5 | Second launch (single instance) | pass | second `IpodSync.Maui.exe` exits, still one app and one server (old BUG 3 fixed) |
| 6 | First-run Windows prompts / firewall / startup entries | pass | no firewall prompt (loopback only), no new rules (only the old dev rules `IpodSync.Web` x2), no Run key, no scheduled task; Start menu `FLACie.lnk`; ARP entry FLACie 0.50.0 (no InstallLocation, cosmetic) |
| 7 | Tray icon / native toasts | not present | the app has no tray icon and shows no native notifications; ntfy is the only notification path |
| 8 | Window: min size, resize, maximise | pass with BUG 4 | native min size is 900x600 (SetWindowPos to 200x300 is clamped); at 900x600 the logo overlaps the search box |
| 9 | Home (sections, mood chips, carousels, Shuffle all button present) | pass | mood chip "Relax" loads a mood shelf; carousels scroll; no JS errors |
| 10 | Search (as-you-type, typo, no result, empty, `<script>` text, remote "More music") | pass | URL follows the text; escaped output; "Nothing matches" copy; Clear search works |
| 11 | Explore overview, Songs, Albums, Artists, Genres, Charts, quality Filters, alphabet rail | pass with BUG 5, BUG 11 | counts match Settings; 1925 album cards render in 1.3 s; A-Z rail is covered by the player bars |
| 12 | Explore/Charts layout (no sidebar, hamburger menu) | note | `app full` layout by design, but the page looks different from the rest at desktop width |
| 13 | Charts: Top Songs + 7 genres, "42 of 50 in library", Download/Play | pass | |
| 14 | Playlists page, playlist page, create, add (toast + Undo), duplicate refused, reorder (Move up/Move down), remove, rename (unicode), persist after reload, delete (inline "Delete / Keep it") | pass | all persisted after reload; remove has no undo (known) |
| 15 | Favourites page, favourite and unfavourite from a playlist row | pass | list 5 -> 6 -> 5 after reload; restored |
| 16 | Recent / Listen again | pass | Home "Listen again" shelf; there is no `/recent` page (404) |
| 17 | Folders | not present | no folder browser in the app (`/folders` 404) |
| 18 | Downloads from the Search result and from the song menu | pass with BUG 2 | "Searching Soulseek... > Waiting for a peer... > Downloading n% > ready to play" in 28 s; menu Download fell back to YouTube on its own (see side effects) |
| 19 | Download from Charts | pass with BUG 3 | BAD TIMES in 12 s, saved as `music/<artist>/<artist> - BAD TIMES.flac` |
| 20 | Downloads page (Running/Failed/Soulseek/Lidarr/YouTube counts, per-job log, filter, Everyone's downloads), Search page "Downloads" panel + Clear finished | pass | |
| 21 | Streaming (not yet scanned) song plays | pass | MP3 320, "your server (new download)", audio `currentTime` 29 > 35 s over 6 s |
| 22 | Play, pause, resume | pass | paused flag true and time frozen for 3 s, resumed |
| 23 | Seek (slider) and keyboard (ArrowLeft on the slider) | pass | seek to 200 s landed at 202 s; ArrowLeft -1 s |
| 24 | Next, Previous (restart after >3 s, back to previous song under 3 s) | pass | |
| 25 | Next song starts: Jellyfin FLAC/MP3 song | pass with BUG 6 | first sound after about 7 s (readyState 0, no buffered ranges until then) |
| 26 | Auto advance at the end of a song | pass | seeked to end-4 s: next song at t=0.5 s within 1.5 s |
| 27 | Shuffle on/off, Repeat off > all > one, Repeat one loops | pass | at the end of the song `currentTime` went 225 > 1.28 s on the same song |
| 28 | Mute / unmute, volume slider | pass | volume 1 > 0 > 1 |
| 29 | Queue: Play next, Add to queue, Up next list, tap to jump | pass | order correct; no reorder/remove in the queue (not present on web) |
| 30 | Skip 20 songs in 3.8 s (old freeze) | pass | page responsive (1 ms evaluate), no page errors, next song playing 1 s after the burst |
| 31 | Full-screen Now Playing: title, artist, album, cover, chips, time, seek, Escape closes | pass at 1400x900 | fails at short heights, BUG 1 |
| 32 | Now Playing tabs: Up next, Lyrics (synced, LRCLIB, line highlight), Info (chips, spectrum, level, stereo, visualizer, spectrogram, file facts, "Came from", Playing row) | pass | spectrum drew live data, so the decoder path works |
| 33 | Now Playing "..." menu (Play next, Add to queue, Add to playlist, Go to artist/album, Download, Song info) | pass | |
| 34 | Media keys / Windows media overlay | not tested | `mediaSession` handlers exist (play/pause/next/previous/seekto) but `playbackState` stays `"none"`; hardware keys not sent |
| 35 | Keyboard shortcuts | fail (enhancement) | only Escape (close player / menu). Space, arrows, N/P/M do nothing |
| 36 | Sleep timer, equalizer, crossfade, speed | not present | absent from the web player |
| 37 | Mini player: PiP button, remote "Listening on RG Rotate" bar, Play here | pass (not clicked) | the remote bar controls the owner's RG Rotate, so no button on it was pressed |
| 38 | Devices, Jam, Import, Headphone test pages (UI only) | pass | Jam: "No Jams going on right now"; Headphone test not played (tones through the owner's speakers) |
| 39 | Dashboard (devices, server tiles, activity, sections), Settings, Account | pass | settings toggles apply classes `pref-noartbg/-compact/-nobadges/-calm`, restored |
| 40 | Notifications settings: master, ntfy address/topic/token fields, 7 chips + "Also plays through Jellyfin", Save persists across reload | pass | this local server's ntfy is OFF and empty (separate from the web container's); "Send a test" not pressed |
| 41 | About / version / licences inside the app | fail (minor) | no About page; no version anywhere in the UI (BUG 9) |
| 42 | Missing items: album, artist, playlist, song, unknown route | pass with BUG 8 | friendly "isn't in your library / isn't there any more"; unknown routes show the bare browser 404 page |
| 43 | Responsive 1400, 900, 600, 375, 320 (CDP emulation) | pass with BUG 4, BUG 10 | no page-level horizontal overflow; chip rows scroll sideways by design; bottom nav labels collide |
| 44 | iPod manager tab (read only) | pass (read only) | "No iPod connected"; nothing pressed; no iPod volume present |
| 45 | Console / page errors during two full sweeps | pass | 0 page errors; only `/art/...` 404s for missing covers |
| 46 | Resource use | note | after about 45 min: FLACie.Server 324 MB, IpodSync.Maui 194 MB, 15 `msedgewebview2` processes 760 MB combined (includes any other WebView2 apps on the PC) |

Totals: 46 rows. Pass 36 (11 of them with a bug attached), fail 2 (35, 41), not present 4 (7, 12 note, 17, 36), not tested 1 (34), notes 3 (12, 44 read-only, 46).

## Downloads and side effects (read this)

Songs I asked for (4, all on the NAS now, nothing deleted):
1. Diamonds (Remix) - Rihanna (Soulseek, search result button) `music/Rihanna/Rihanna - Diamonds (Remix).mp3`
2. BAD TIMES - Imael Angel (Soulseek, Charts button) `music/Imael Angel/Imael Angel - BAD TIMES.flac`
3. All The Stars (From "Black Panther: The Album") - Kendrick Lamar: NOT requested by me, it is Autoplay "Fetch ahead" triggered by playing a song. I then turned Fetch ahead off.
4. Diamonds (Extended Radio Mix) - Radio City DJ's (song menu "Download"). **The server found nothing on Soulseek and fell back to YouTube by itself**
   ("Looking on YouTube... ready to play, from YouTube"). I did not choose a YouTube source; the server's configured fallback chain did. The file is on the NAS.

Not mine, but caused by relaunching the app: at 15:34 the embedded server's **"Daily charts"** background job downloaded 8 chart songs
(Patient Zero (Piano Version), BAD TIMES again, I Can't Love You Anymore, Don't Mind If I Do, Draw You Out, Sign of the Times, UNETHICAL, Ted Lasso Theme)
plus "James 1:17 (Jesus Is King)" via YouTube. So the real count of new files from this session is about 12, not 4. See BUG 2 and BUG 3.

## Bugs

Severity: high / medium / low. Suggested fixes name the likely place; none are confirmed in code unless stated.

### BUG 1 (medium): full-screen Now Playing breaks on short windows
Repro: window 900x700, 1280x600 or 1400x700 (a laptop screen with the taskbar gives about 700-750 px), play a song, open Now Playing.
Actual: the cover is square and sized by width, so it grows over the top bar: "PLAYING FROM ..." is partly hidden under it, the chevron is half covered, and the title block, artist and chips run behind the player bar (artist line cut at 1280x600, chips hidden at 1400x700). At 1400x900 and 1384x808 it is fine.
Fix: cap the cover by height (`max-height: calc(100vh - header - bar - title block)` / `aspect-ratio` with `min()`), and let the left column scroll or shrink the title block under about 760 px height.

### BUG 2 (high for owners, config/design): automatic YouTube fallback and Daily charts downloads on the desktop server
Repro: press Download on a song Soulseek does not have (e.g. "Diamonds (Extended Radio Mix)" by Radio City DJ's from the Search "More music" menu); relaunch the app.
Actual: the job silently falls back to YouTube after Soulseek; the Downloads page labels it "YouTube" but the button gave no warning. On relaunch the embedded server's Daily charts job fetched 9 songs (2 from YouTube) into the owner's NAS, including songs that are already downloaded (BUG 3).
Fix: make YouTube fallback opt-in per request or per user with a clear confirmation, and do not run Daily charts (or any background download) in the desktop shell by default, because frank's server already runs the same job against the same NAS (duplicates, two servers racing on one library).

### BUG 3 (medium): Daily charts and Fetch ahead download songs that were just downloaded
Evidence: "BAD TIMES" downloaded at 15:24 (my request) and again at 15:34 (Daily charts, "took 5 s"); "Patient Zero (Piano Version)" again resolved to `Taylor Swift - Patient Zero.flac` (the original, same file as A-web BUG 1) and was "filed" a second time. "All The Stars" was fetched ahead although it already exists on the NAS (A-web BUG 2).
Cause: the "in library" test uses the Jellyfin library, which only knows a file after the next scan; recently succeeded downloads are not remembered.
Fix: keep a "recently downloaded" set (artist+title key, 24 h) and consult it in the chart job, Prefetch and the Download buttons; reject a candidate whose title lacks the "(Piano Version)" qualifier.

### BUG 4 (low): at the minimum window size the logo and search box overlap, and the content area is tiny
Repro: resize the window to the smallest size (900x600). The "FLACie" wordmark is overlapped by the search field, the left rail icon column is cut at the bottom, and the two player bars (own plus "Listening on <device>") take about 140 px of the 540 px content height.
Fix: hide the wordmark under about 1000 px width (keep the mark), raise the minimum window height, and collapse the remote bar into the local bar.

### BUG 5 (low): the A-Z jump rail is covered by the player bars
Repro: Explore > Albums (or Artists, Songs, Genres) at 1384x808 with a song loaded and another device playing: letters from about W are behind the bars (`elementFromPoint` on the Z target returns `aside.remote-now`).
Fix: end the rail above the bottom bars (`bottom: var(--bars-height)`) or size it with `max-height`.

### BUG 6 (low/medium): a Jellyfin song takes about 7 seconds to start
Repro: press Next onto a Jellyfin MP3/FLAC (We Found Love): `readyState` 0 and no buffered range for 7 s, then it plays. Local streaming songs start instantly; the following songs (prefetched twin) start within 1.5 s.
Cause (likely): the server's `/stream` proxies Jellyfin over Tailscale and waits for the first bytes (A-web OBS 1: Jellyfin answering slowly).
Fix: show a buffering spinner on the play button immediately (there is none, the pause icon shows as if playing), and start prefetching the next song earlier.

### BUG 7 (low): the 0.49 WebView2 profile stays in the install folder after the upgrade
`%LOCALAPPDATA%\Programs\FLACie\IpodSync.Maui.exe.WebView2` (185 files) was left by the previous version; the new profile is correct (`FLACie\webview2`), but uninstall will not remove the old folder (the old BUG 1 remains for upgraders), and the sign-in cookie is duplicated there.
Fix: a one-time cleanup of the legacy folder at startup or a `RemoveFolderEx` in the MSI.

### BUG 8 (low): unknown URLs show the raw Microsoft Edge 404 page, with no way back
Repro: open `/folders`, `/recent`, `/about`, `/song/xyz`, `/playlist/` (any route that does not exist). The window shows "This 127.0.0.1 page can't be found, HTTP ERROR 404, Microsoft Edge" without the FLACie shell. In the desktop app there is no address bar or back button, so the user is stuck until restart.
Fix: `UseStatusCodePagesWithReExecute` to a Blazor "not found" page inside the layout, and a `NavigationManager` fallback for unmatched routes.

### BUG 9 (low): no version, About or licences screen in the app
The window title is only "FLACie" and no page shows the version (0.50.0) or the third-party notices that the MSI now ships. Support questions need it.
Fix: Settings > About (version, build, link to `THIRD-PARTY-NOTICES.md`, server URL, data folder).

### BUG 10 (low, known A-web BUG 4): bottom navigation labels collide below about 650 px
At 600 px "Headphone test" is clipped, at 375 and 320 px "Downloads", "Dashboard" and "Headphone test" overlap ("DownloadsDashboardadphone te"). The desktop window cannot go below 900 px, so this only matters if the min width is lowered, but it is the same server code as the phone browser.
Fix: ellipsis plus `min-width:0` on the rail items or horizontal scroll for 8 items.

### BUG 11 (low): artist route is case-sensitive
`/artist/queen` says "This artist isn't in your library" while `/artist/Queen` works. Links typed or remembered in another case fail.
Fix: look the artist up by the normalised key.

### Observations (not filed)
- O1: no keyboard shortcuts (Space for play/pause, arrows to seek, N/P) - an enhancement, desktop users expect Space.
- O2: the genre list includes entries named `???` and `<Unknown>` (1 song each); artist list includes tag-junk entries such as "[500] Days Of Summer - Music From The ..." (library tagging).
- O3: Explore, Charts and the lists use a different layout (hamburger instead of the sidebar) than Home/Search/Playlists at the same width.
- O4: `navigator.mediaSession.playbackState` stays `"none"` while playing (Windows media overlay may show the wrong state; not testable here).
- O5: this desktop server's Dashboard lists a device "Edge on Windows" (127.0.0.1) next to the Jellyfin clients, while the Devices page calls the same machine "FLACie Web". The infra watcher ignores the device name "FLACie Web", so check whether desktop plays are announced twice (not verifiable from here, no ntfy access).
- O6: the song duration shown in lists (4:47) differs from the file's real duration (4:53) for downloaded MP3s until the next scan.

## Not tested, with reasons

- Hardware media keys, Windows media overlay / SMTC, lock screen: no key injection into the app without disturbing the owner's desktop.
- Real audio output, volume loudness, gapless quality, Headphone test tones: no ears; Headphone test not played to avoid tones on the owner's speakers.
- Notifications actually arriving (ntfy): the desktop server has ntfy disabled and empty; "Send a test" not pressed; owner phone not visible.
- Controls on the remote "Listening on RG Rotate" bar and Jam/Connect between devices: would control the owner's RG Rotate.
- Sign-out, sign-in, expired session, switching accounts, non-admin view: forbidden or needs the owner.
- Server unreachable / offline error handling: must not break the owner's server; only missing-item pages were checked.
- iPod manager actions (sync, restore, format, add files, backups): no iPod connected, rules forbid writing; tab text only.
- Install, upgrade, repair, uninstall of the MSI: covered by workstream D (not repeated); only the installed tree was inspected.
- Folders, sleep timer, equalizer, queue reorder/remove, pull to refresh, orientation: not present in this app.
- Light theme: the app is dark only. Display scaling above 100 % and multi-monitor: not changed on the owner's machine.
- 30 minute memory loop and startup performance beyond the healthz timing: not run (only one snapshot taken).
