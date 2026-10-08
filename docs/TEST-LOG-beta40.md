# Beta 40 test and fix log

Started 2026-10-06. Each entry: what was seen, steps, fix, re-test result.

## Reported
1. Small window / split screen: layout breaks (side-by-side layout chosen by aspect ratio only).
2. Artist name truncated beside the buttons although there is room.
3. Seek bar thumb clipped at the start/end of a track.

## Fixed so far (Android)
- Bug 1 (small window / split screen): the two-column player layout was chosen by aspect ratio alone, so a 400dp-wide split window got two squashed columns. Now two columns need >= 600dp of width; a short window (< 560dp tall) uses a single scrolling column with song, seek and transport first (cover and pages below); landscape phone keeps two columns with a fixed small cover. Verified at 1080x1155@420 (split), 2400x1080@420 (landscape phone), 720x1280@320, 2560x1600@280.
- Bug 2 (artist cut off with room to spare): the Equalizer / Add / More buttons sat beside the title and heart and squeezed the title block (title wrapped, artist ellipsised). They now have their own row under the badges; the artist may use two lines.
- Bug 3 (seek knob clipped at the start/end): the knob was drawn centred on the track end inside a canvas that clips, so half of it vanished at 0:00 and at the end. The track is now inset by the knob radius at both ends (touch mapping the same).
- Nav rail clipped the Settings item on a landscape phone (about 400dp tall): items now share the available height (50-68dp) and still scroll if needed.
- Devices page redesigned (web + Android): cards with cover, device kind (Browser / Phone / Computer, guessed from the device name), state chips (This device, Playing, Paused, Idle), progress line, controls; "ipodplayer" no longer shown.
- Web touch targets: every button/link chip >= 44px at <= 1024px wide (buttons, subnav, mood chips, avatar, burger, search input, side rail links, swatches, reorder arrows, import file picker). My first CSS version broke two things (the remote strip's "Play here" button reappearing and squeezing the song name to 64px at 320px wide; the logo centred and cut off): both fixed and re-swept.
- Web remote "Listening on ..." strip at <= 400px: previous (and next at <= 340px) are hidden so the song name has room.
- Web tab bar labels no longer run into each other at <= 400px.
- Android avatar letter matched to the name the Account screen shows (a NAS-primary account showed H from the Jellyfin name but G on the Account page).
- Web sweep (iframes, 15 pages x 9 sizes: 320x568, 360x640, 390x844, 768x1024, 1024x768, 1280x720, 1920x1080, 2560x1080, 800x360): no horizontal overflow, nothing clipped, no small targets left except the A-Z letter rail (a scrubber: 27 letters cannot all be 44px tall).

## Downloads (real downloads on the dev server against the real services; nothing deleted)
- Failed list on the account (6): four of them (People You Know, Blue Moon, Without Me, Blush) ran on 5 Oct BEFORE the YouTube source was on (no YouTube step in their trail); Valhalla Calling is genuinely obscure.
- Soulseek songs downloaded 6 Oct as tests: Taylor Swift - Cruel Summer (arrived as a .wav), Olivia Rodrigo - vampire (flac), Kevin MacLeod - Sneaky Snitch (flac, via the album route), Northveil - VALHALLA CALLING (flac, album route). All filed under music/<artist>/ on the NAS. They are real songs added to the library; remove them by hand if unwanted.
- YouTube fallback tested end to end on frank (file mover -> yt-dlp service): Beyonce - Halo came back as m4a in 5.9 s (wrong-ish channel "BeyonceSasha1" but right duration); an obscure song correctly answered "no matching upload" in 2 s. The test file is at <file mover root>/_flacie_test_beta40/Beyonce - Halo.m4a (not under music/, so Jellyfin does not index it). Not deleted (rule).
- Bug (found in the trails): Soulseek finished a file, "Filing into the library..." then "had nothing it could finish" (Blue Moon, People You Know). Possible cause: slskd announces completion a moment before it moves the file into its downloads folder, so the file mover's first /move finds nothing. FileAsync now retries "source not found" for up to ~16 s and the trail now says why a filing failed. NOT reproduced here (all four test downloads filed on the first try), so this fix is unconfirmed.
- Bug: Lidarr flow gave up two minutes after the grab even while the download was in Lidarr's queue, so a song that arrived later (Blue Moon is in the library now) was logged as "Not found". The wait now follows the queue (up to 30 min).

## Web playlists and import (tested live in the browser pane, dummy playlists removed afterwards)
- Create a playlist: works. Add songs (from the song menu, with confirmation toast and Undo): works from playlists/search, but the Explore > Songs rows had NO favourite or "..." menu at all, so a song could not be added to a playlist from the main library page. Added both (play next, add to queue, add to playlist).
- Rename a playlist: the web had no rename (the session method existed but no UI). Added a Rename button with an inline form. Tested: renamed ok.
- Reorder: the web had no reorder. Added Move up / Move down to the song menu on a playlist page (moves the real entry, so songs not in this library keep their place). Tested: up, down ok.
- Remove from playlist, Delete (with confirm): ok.
- Import: an M3U with only "#EXTINF" lines (no file lines) reports "No songs found" (expected for a malformed M3U); a normal M3U imported "2 of 3 done" (the third was a made-up song, it went on to search Soulseek/Lidarr/YouTube), Stop worked ("cancelled"). Dummy playlists "ZZ beta40 test/renamed" and "zz-beta40-import" deleted.

## NOT done or NOT tested in this pass (be honest)
- Android: only the player at 6 window sizes, devices, accent colour and nav rail were exercised on the emulator; not walked through: Explore filters, Search options, playlist editing on the phone, Downloads page on the phone, charts, Settings fields one by one, Jams, notifications (ntfy server still unreachable), Admin from the phone.
- Web: Jams, charts page behaviour, notifications, the admin dashboard actions, the Windows app build, and "dead code removal" were not done. No startup-time / memory measurement on Android was taken.
- The Soulseek "filing" retry and the Lidarr wait change are fixes for failures seen in the logs, not reproduced here.
- This is released as beta 39.5, not beta 40: the full pass was cut short.

## Session 2026-10-07/08 (new PC: toolchain proved, web re-test, Android on an emulator, release)

Toolchain: JDK 17, Android SDK 34 + emulator (Pixel 6 AVD `flacie_phone`, WHPX works), .NET 9 SDK + maui-windows/android workloads, WiX 5, ffmpeg (for tagged test music). Release APK builds (~4-7 min), `dotnet build FLACie.Server` clean, MSI builds (~3 min), Android unit tests pass.

Web bugs found and fixed (all in the CHANGELOG): player under the Explore filter bar (z-index), "Playing from" run together, song menus not closing on outside click / Escape, aria-expanded/pressed/selected/checked rendered empty, "1 songs". The Explore song list looks empty while the browser pane is hidden (Virtualize waits for the viewport): a test artefact, not an app bug.

Android (emulator, guest mode, 8 generated tagged files in /sdcard/Music from C:\FLACie_out\music3): fixed the Rename dialog saying "New playlist / Create", and the queue saying "Playing from Your queue" for Explore songs. Seen, not fixed: Hi-Res filter and format badges never match device-only files (no audio facts); the "Local" pill squeezes the artist line when everything is local; unselected options in the Quality sheet show a list icon.
Measured on the emulator (software GPU, 8 songs): cold start 2.2-2.4 s, idle ~49 MB PSS. Not a phone figure.

NOT done: Android signed in to Jellyfin (Quick Connect needs the owner to approve the code), so Downloads / charts / Jams / remote / Admin on Android are still unwalked; ntfy; a real phone; installing the MSI (the unpacked app was launched, /healthz ok, closing it stopped the server).
Test files left on the emulator only; nothing was written to the NAS, an iPod or any real library.

## Betas 41 and 42 (2026-10-08)
Beta 41: typo-tolerant Explore search and Codec / Bit rate / Sample rate filters on web, Windows and Android; Android source pill only when mixed; wrapped empty-state text. Beta 42: finer bit-rate bands (up to above 5,000 kbps), the one-row Explore filter bar with pills and an Audio specs drawer, custom dropdowns, "Did you mean", and the sectioned Search results page (web / Windows). Details and the Tested / Not tested lists are in CHANGELOG.md. Android was signed in to the owner's Jellyfin through Quick Connect on the emulator from beta 41 on; the emulator cannot resolve the Tailscale name (frank.tailb05910.ts.net), so anything needing FLACie Web (Charts, audio facts, downloads) cannot be verified there. frank.tailb05910.ts.net has no public DNS record at the moment (NXDOMAIN at Tailscale's nameservers); the Funnel config on frank is correct, so it needs checking in the Tailscale admin console.
