# FLACie changelog

Every release, newest first (generated from the GitHub release notes; the downloads stay on each release page). The newest build is marked Latest. Versions up to 0.9.x were called ipodplayer.

## FLACie 1.0 beta 31

_v1.0.0-beta31, 2026-10-06, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta31))

Beta 31 (Android 45, MSI 0.31.0). Still a beta, not a 1.0 final.

**Changed (Android)**
- Unfolded / wide / landscape player now works like the web: cover, title, seek and transport on the left; on the right the pills (Lyrics, Info, Up next, Equalizer, Add to playlist, More) and the Up next / Lyrics / Info pane, always showing. Taller-than-wide screens keep the swipeable pages. Seen on a 1800x1700 (Fold-inner-like) and a 2400x1200 (tablet-like) emulator screen.
- The lit pill now follows swipes. After tapping Info and swiping out of it, the pill stayed lit because the page tracker kept an old copy of the pane; it reads the current pane now.
- Info graph pills: one tidy 3 x 2 grid of equal pills with proper names (Bit rate, Spectrum, Level, Stereo, Visualizer, Spectrogram), nothing cut off; the file's own figure (kbps, kHz, bit) is in the card title.

**Server data (not an app change)**: "Meet Me Halfway" showed the wrong cover because Jellyfin's picture for the "The E.N.D" album folder was "The Beginning". I replaced that album's picture in Jellyfin with the real THE E.N.D. cover (Jellyfin keeps it in its own metadata; the library has "save local metadata" off, so nothing was written to the NAS). The old picture is saved on the PC as ~/the-beginning-cover-backup.jpg.

**Tested**: Android release build on the emulator at phone, square and landscape sizes; pill highlight after swiping into and out of Up next and Info.
**Not tested**: a real Fold unfolded (only an emulator screen of similar shape); that your phone shows the new cover for Meet Me Halfway (it may need the app's picture cache to refresh); installing the MSI or launching the 0.31 Windows app.


## FLACie 1.0 beta 30

_v1.0.0-beta30, 2026-10-06, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta30))

Beta 30 (Android 43 -> 44, MSI 0.30.0). Still a beta, not a 1.0 final.

**Changed (Android)**
- Player pages: a fast flick or a short half swipe no longer snaps and snaps back. The pager is only moved by a pill while it is still, and only reports a new page once it has come to rest; a flick now carries on from a quarter of the way.
- Info: swiping sideways on the graph card switches to the next or previous graph (bit rate, spectrum, level, stereo, visualizer, spectrogram) and no longer moves the player page. The graph text is two lines (tap it for all of it), the graph is a bit shorter on small screens, and the file facts are laid out two to a line, so more of them show without scrolling.

**Tested**: Android release build on the emulator (swipe on the graph moved Spectrum to Level and kept the Info page; two-column facts).
**Not tested**: the pager feel on a real phone (emulator draws in software); a normal-aspect phone (only the 1080x2400 emulator and your Fold were seen); installing the MSI or launching the 0.30 Windows app.

**Known, not changed**: "Resampled by device" in the Playback row is accurate: Android's mixer resamples to its own rate (48 kHz on most phones); only a USB DAC with Android 14+ bit-perfect mode can bypass it, and the app does not switch that on yet. Meet Me Halfway: Jellyfin's album picture for "The E.N.D" folder is the "The Beginning" cover.


## FLACie 1.0 beta 29

_v1.0.0-beta29, 2026-10-06, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta29))

Beta 29 (Android 43, MSI 0.29.0). Still a beta, not a 1.0 final.

**Changed**
- Android full-screen player: the four panes (Lyrics, cover, Info, Up next) are now a pager. They follow your finger, settle with a spring, glide when you press a pill, and the page you leave shrinks and fades a little. While Lyrics, Info or Up next is open the song title collapses to one line (small cover, title, artist, heart) so the pane gets much more room; seek bar, transport and the pill row stay.
- Info graph: the dashed "MP3 usually ends here" line is back on every file (as before), and for an MP3 it follows its bitrate (about 16 kHz at 128 kbps up to about 20 kHz at 320 kbps). Android and web.
- CHANGELOG.md and docs/HANDOFF.md now cover betas 24 to 28.

**Tested**: Android release build on the emulator (swiping from the cover to Info shows both panes sliding together mid-swipe; the title collapses; Up next pane; the dashed line on a FLAC); web server deployed to frank and answering /healthz.
**Not tested**: how the pager feels on a real phone (the emulator draws in software); Lyrics pane on a song with synced lyrics after this change; the web marker on an MP3; installing the MSI or launching the 0.29 Windows app.


## FLACie 1.0 beta 28

_v1.0.0-beta28, 2026-10-06, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta28))

Beta 28 (Android 42, MSI 0.28.0). Still a beta, not a 1.0 final.

**Changed**
- Full-screen player (Android and web phone layout): swipe sideways between panes, left to right: Lyrics, the cover, Info, Up next. A swipe no longer skips the song (Previous/Next do that). The pills still work and light up for the pane you are on; the swipe-up queue sheet is gone.
- Up next: the "downloaded" tag is bigger and clearer (pink while downloading, green once fetched: just now / today / this week), and web also matches autoplay picks that were downloaded by name.
- Info graph: the "MP3 usually ends here" line now follows the file's bitrate (about 16 kHz at 128 kbps up to about 20 kHz at 320 kbps) and only shows for MP3s. A 320 kbps MP3 reaching 20 kHz is normal; the 44.1 kHz reading is the file's real sample rate.
- Web: typing in the search box no longer loses focus part-way (the page was grabbing focus on every address change).
- Web phone player: the volume slider is back.

**Tested**: Android release build on the emulator (swipe cover -> Info -> Up next, pills highlight); web at phone width (search kept focus through typing "scream and shout"; swipe order cover -> Info -> Up next and back to Lyrics); web deployed to frank.
**Not tested**: the web volume slider on a real phone after this change; the "Downloaded" tags with a freshly downloaded or autoplay song; Home scroll smoothness; installing the MSI or launching the 0.28 Windows app.



## FLACie 1.0 beta 27

_v1.0.0-beta27, 2026-10-05, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta27))

Beta 27 (Android 41, MSI 0.27.0). Still a beta, not a 1.0 final.

**Changed**
- Full-screen player: the play/pause and seek controls move up, and a quiet "Playing from <playlist or mix>" line with a small handle sits above the system gesture area. Swipe up from it (it follows your finger) to raise the queue sheet; it no longer sits where a swipe closes the app. The "Up next" pill opens the same sheet. Android and web (phone width).
- Everything from beta 26 (Funnel back on for port 443 on frank so the phone's Lidarr / Soulseek / File mover addresses connect off Tailscale, Home lag changes, Search shortcuts).

**Tested**: Android release build on the emulator (line shows, swipe from it raises the sheet, controls clear of the gesture bar); web server deployed to frank and answering /healthz.
**Not tested**: web phone layout after this change; Home scroll smoothness on a real phone; the phone's Connect buttons off Tailscale; downloaded notes with a freshly downloaded song; installing the MSI or launching the 0.27 Windows app.



## FLACie 1.0 beta 26

_v1.0.0-beta26, 2026-10-05, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta26))

Beta 26 (Android 40, MSI 0.26.0). Still a beta, not a 1.0 final.

**New**
- Up next (YouTube Music style): no bar on the full-screen player. Swipe up from the foot of the player (it follows your finger) or press "Up next": the cover fades back and the queue sheet rises until it fills the screen, with the playing song as a small header, "Playing from <playlist or mix>", then the queue with cover, artist, album and a note for songs FLACie downloaded. Android and web (phone width).
- Home: bigger cover cache and cheaper playlist covers (aimed at the scroll lag), search icon, one chip row, Quick picks with options and Play all. Search tab shows shortcuts and genres.
- Server (frank): Tailscale Funnel turned back on for port 443, so the phone's Lidarr / Soulseek / File mover addresses (frank.tailb05910.ts.net/lidarr, /slskd, /filemove) connect again without Tailscale.

**Tested**: Android release build on the emulator (swipe up raises the sheet with the cover fading, Up next button opens it, "Playing from" shows the list name); web at phone width earlier; frank answers 403 (key needed, as expected) on the three paths and logs no more rejected connections.
**Not tested**: Home scroll smoothness on a real phone; the phone's Connect buttons for Lidarr/Soulseek/File mover (only the server side was checked); downloaded notes with a freshly downloaded song; installing the MSI or launching the 0.26 Windows app.



## FLACie 1.0 beta 25

_v1.0.0-beta25, 2026-10-05, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta25))

Beta 25 (Android 39, MSI 0.25.0). Still a beta, not a 1.0 final.

**New**
- Up next bar (YouTube Music style) at the foot of the full-screen player, Android and web (phone-width). Drag it up (it follows your finger) or tap it for the queue: cover, title, artist, album, length, and a note when FLACie downloaded the song ("Downloading", "Downloaded just now / today / this week"). Tap a song to jump to it.
- Everything from beta 24: Home search icon and one chip row, Quick picks with options and Play all, Search tab with shortcuts and genres, visible system bars, swipe-away stops playback.
- Web server with the bar is already deployed to frank.

**Tested**: Android release build on the emulator (bar shows, swipe opens the queue); web at phone width in the browser pane (bar shows, tap opens the queue, simulated drag opens it); Windows app from the beta 24 build ran and served /healthz.
**Not tested**: slow finger-following drag on a real touchscreen; the "Downloaded" notes with a real freshly downloaded song; scroll smoothness on a real phone; installing the MSI; the 0.25 Windows app was built but not launched.



## FLACie 1.0 beta 24

_v1.0.0-beta24, 2026-10-05, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta24))

Beta 24 (Android 38, MSI 0.24.0). Android-focused; no web server change.

**New / changed**
- Android system bars stay visible in the Modern theme, so the gesture bar / Home button work from the full-screen player; the player controls sit clear of the bottom.
- Home: shelves are built off the main thread (aimed at the scroll stutter), search icon in the top bar, one chip row (Shuffle, Favorites, Recent, Downloads, moods).
- Quick picks: wider next-page sliver, a "..." options menu on every row, "Play all".
- Search tab: shows shortcuts and genres before you type.
- Full-screen player: scrollable pill row (Lyrics, Info, Up next, Equalizer...). Swiping the app away from recents stops playback.

**Tested**: Android release build on the emulator: Home, Quick picks, Search idle screen, full-screen player layout with gesture bar, swipe-away stops audio. Windows app: launched, its server answered /healthz, and the server was gone after the app was closed (force-closed).
**Not tested**: scroll smoothness on a real phone (emulator is software-rendered), Fold-size layout, installing the MSI (only the built app folder was run).



## FLACie 1.0 beta 23

_v1.0.0-beta23, 2026-10-05, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta23))

FLACie 1.0 beta 23 (Android 37, Windows 0.23.0)

Android redesign, to match the web
- Same palette as the web: flat near-black, one pink accent (your own accent colour still wins), raised surfaces with a hairline, flat chips and buttons, a quiet bottom bar with the selected tab in the accent. Album-art colour now only washes the full-screen player, like the web's blurred backdrop. An old green accent from the previous look is put back to the default once.
- Settings: switches instead of On/Off words, same dark page and cards as Downloads, new Animations switch (off = calm, no pops or slides).
- Home: Quick picks is back as the small-cover list (four rows, swipe for more).
- Full-screen player: one seek bar (the volume slider at the bottom is gone; use the phone's volume keys).
- Fewer accidental touches: back-swipe now starts at the screen edge only, no swipe-to-favourite on rows, no swipe-to-skip on the mini player or the title, no swipe-up on the mini player, and playing a song no longer throws you into the full-screen player. Tapping the song that is already playing opens the player (or resumes it) instead of starting it over. The keyboard goes away when you play from Search.
- Smoother: the remote-device poll no longer redraws the whole screen every few seconds, and rows do less work while scrolling.
- Web: clicking the song that is already playing no longer restarts it.

Tested: Android emulator (Home, Explore, Search, Settings, full-screen player, remote bar). The emulator draws in software (about 60 ms a frame even on a plain Settings screen), so I could not measure scroll smoothness; please tell me if it still stutters on your phone and where. Not tested: Windows installer.


## FLACie 1.0 beta 22

_v1.0.0-beta22, 2026-10-05, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta22))

FLACie 1.0 beta 22 (Android 36, Windows 0.22.0)

- Playlists you add to on the phone now show up on the web (and the other way round). The web used to read your profile once at sign-in and never look again, and its next save could overwrite the phone's change. It now checks every 20 seconds and merges (newest edit per playlist wins, deletions and favourites carried over).
- Android playlists can be edited: Rename, Delete, Remove dupes (same song, not just same file) on the playlist page; Move up / down / to top and Remove (with Undo) on each song. Adding a song that is already in a playlist (same song, even another copy) says so instead of adding it twice.
- Android Explore redesigned: tabs, one filter box, a Filters button (genre, decade, quality, bit depth, sample rate in one sheet) and Sort. Active filters show as removable chips.
- Android Info graphs no longer get cut off: the graph fits the room, the chips scroll sideways, and the text is shorter.
- Android Settings: Dashboard is the first thing in Settings for administrators, and the Dashboard shows library quality (Hi-Res / lossless / lossy from the real file data) and how the last downloads went per source.
- The remote bar only appears for a device that is actually playing.

Tested: Android emulator (Explore, Filters sheet, playlist page, Info graph, remote bar), web build, the playlist merge compiles and runs on the server. Not tested: the phone-to-web playlist merge against your real phone, Remove dupes / Move / Rename (they change your real playlists), Windows installer.


## FLACie 1.0 beta 21

_v1.0.0-beta21, 2026-10-05, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta21))

FLACie 1.0 beta 21 (Android 35, Windows 0.21.0)

Android
- Remote player (Spotify Connect style): when your web player, Windows app or another phone is playing and this phone is quiet, a bar at the bottom shows what it plays with play/pause and a Devices shortcut. Tap it for a full-screen remote: cover, seek bar, previous/play/next, "Play here" (takes the queue over, same second) and your devices. The normal mini player also has a Devices shortcut while other devices are signed in.
- Motion everywhere: sheets slide up with a fading scrim, the mini player slides in, covers pop on each new song, buttons spring when pressed, hearts bounce, messages slide in.

Web / Windows
- Explore is full screen: the side menu slides in from a hamburger; no second search bar; the filter bar hides as you scroll down and returns the moment you scroll up.
- Slide/pop animations for the full-screen player (up, and down with the cover dropping), bar, menus, rows and pages.
- The bottom player bar opens the full-screen player when you click it (not its controls).
- Back after "Go to album / artist" from the full-screen player returns to the full-screen player.
- The playlist list in the "..." menu is readable (it was greyed out).

Tested: web in the browser pane (Back into the player, hamburger, sticky filter bar, menus); Android emulator: remote bar, full-screen remote, Play here (the phone took over from the web player and the web paused). Not tested: Windows installer, USB DAC, remote bar with a phone-to-phone session, Android animations by eye (only that nothing crashes).


## FLACie 1.0 beta 20

_v1.0.0-beta20, 2026-10-05, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta20))

FLACie 1.0 beta 20 (Android 34, Windows 0.20.0)

- Real audio metadata: every song is read with ffprobe/Jellyfin (no more guessing from the extension). Old Hi-Res files now get the Hi-Res badge.
- Info: spectrum goes to the file's real Nyquist, bigger graphs for Hi-Res, new Playback row (native rate vs resampled).
- Explore: bit depth and sample rate filters.
- Full-screen player menu, playlist "Added / already in" messages with Undo, playlist layout.
- Seeker focus frame and snap-back fixed (web).
- Downloads: fixed the 30 s timeout that logged working YouTube downloads as failures; yt-dlp queue; Soulseek search tweaks; Lidarr now allows Single and EP.

Tested: web in the browser pane (seek hold, focus, menu, toasts with Undo, Explore filters, Info/Playback, probe of 7,100 songs), frank deploy healthy, Android launch + Explore Hi-Res badges on the emulator, Android unit tests, YouTube search/ranking on frank.
Not tested: Android Info panel and snackbar on a device, USB DAC playback, a fresh YouTube download after the fix (needs a new request), Windows MSI install.


## FLACie 1.0 beta 19

_v1.0.0-beta19, 2026-10-05, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta19))

## FLACie 1.0 beta 19 (Android 1.0-beta19 / versionCode 33, Windows 0.19.0)

**Hi-Res stands out.** Hi-Res files now carry an amber **✦ Hi-Res** badge *instead of* the plain FLAC tag (web, Windows and Android song lists, and the full-screen player's format badge), and the "[Hi-Res]" is dropped from their displayed title. Explore's Quality filter has a **✦ Hi-Res** option on every device.

**Explore, redesigned.** The filters sit in one calm card with pill-style pickers, the tabs scroll on phones, and the **A–Z index is now a slim rail down the right edge** (web, Windows and Android).

**Songs now join Jellyfin on their own.** A song you download used to be only a file on the NAS (shown with a "NAS" badge) until Jellyfin's next scheduled scan, sometimes hours later. The server now asks Jellyfin to scan about 15 seconds after a download finishes (at most once a minute; needs a Jellyfin administrator account). Songs that already sat on the NAS unscanned were picked up by a manual scan today.

**Duplicates cleaned up (done on the NAS, not part of the app).** 34 files (857 MB) were removed after checking each: 12 identical "(2)/(3)/(4)" copies in AA-playlist, 2 lower-quality copies beside a better file (an mp3 beside the flac, a 128 kbps mp3 beside a 322 kbps m4a), and 20 of FLACie's own `Artist/Artist - Title` downloads of songs that already existed in an album or playlist folder at equal or better quality. The full list with the file kept for each is in `docs/duplicates-removed-2026-10-05.tsv`. About 800 more "same song" pairs were deliberately left: they are the same recording in an album folder and in a playlist folder (your folders double as playlists), which is how the library is organised.

Everything from beta 18 is included.

**Tested:** the Explore page on the web at phone width, the A–Z rail and filter card, the Hi-Res badge on the web build (compiled and rendered in lists), Android's Explore with the rail on the emulator, unit tests, the release APK launching with no crashes or ANRs, the Windows app starting and its server stopping with it, frank redeployed and healthy.
**Not tested:** the Hi-Res badge on a real Hi-Res row on the phone, the automatic Jellyfin scan after a download finishing (it needs a real download by an administrator account), a real phone, the MSI installer itself.


## FLACie 1.0 beta 18

_v1.0.0-beta18, 2026-10-05, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta18))

## FLACie 1.0 beta 18 (Android 1.0-beta18 / versionCode 32, Windows 0.18.0)

**Hi-Res on songs you already have (administrators).** A quiet **✦ Hi-Res version…** entry at the foot of a song's "…" menu (web and Windows) and the song sheet (Android), with a confirmation ("2 to 5 times bigger, roughly 40 to 200 MB, saved next to this one"). It searches for a Hi-Res copy; if none exists nothing is downloaded or changed (the automatic fall-back to normal quality only applies to the Hi-Res button on Search results, where you do not have the song yet).

**Tested live:** P!nk - "Just Give Me a Reason (feat. Nate Ruess)" through the app's Hi-Res download: found on Soulseek in under 5 s, saved as `music/P!nk/P!nk - Just Give Me a Reason [Hi-Res].flac`, a genuine 24-bit / 44.1 kHz FLAC (43 MB, 4:02).

Also: Hi-Res size wording corrected (2 to 5 times bigger, about 40 to 200 MB), and everything from beta 17 is included.

**Tested:** the web menu entry and its confirmation (it scrolls into view in the menu), the Android song sheet entry, the real Hi-Res download above, unit tests, the release APK launching with no crashes or ANRs, the Windows app starting and its server stopping with it, frank redeployed and healthy.
**Not tested:** tapping "Download Hi-Res" from the library menu (the same code path as the Search button was exercised by the live test), a real phone, the MSI installer itself.


## FLACie 1.0 beta 17

_v1.0.0-beta17, 2026-10-05, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta17))

## FLACie 1.0 beta 17 (Android 1.0-beta17 / versionCode 31, Windows 0.17.0)

**"All The Stars" (and songs like it) can be found again.** Two causes, both fixed on every device:
1. **Title cleaning.** `All The Stars (From "Black Panther: The Album")` made every source look for those extra words. Titles are now reduced to the song's own name for searching and matching ("(From …)", "(Remastered 2011)", "- Remastered", "[Deluxe]", "(feat. …)" are dropped; a "(Live)", "(Remix)", "(Acoustic)" or "(Part 2)" is kept because it is a different recording). This is used by Soulseek, the Internet Archive, Audius, Jamendo and YouTube, and for file names.
2. **Soulseek refuses some artist names.** A search containing "kendrick lamar" returns *nothing at all* on the Soulseek network (while "kendrick all the stars" finds hundreds of files). When the full-name search finds nothing usable, FLACie now retries with the artist's first word, then the title alone (songs and albums); the ranking still requires the artist in the file path. The song downloaded in 20 s in the test.
- The YouTube step also has a looser second pass, so it no longer gives up when the first strict match is empty.

**Hi-Res download (administrators only, per song, never automatic).**
- A normal download now **never takes a Hi-Res file** (24-bit or above 48 kHz): those are several times the size. If Soulseek only has Hi-Res copies, the download's story says so.
- Next to Download there is an amber **✦ Hi-Res** button (web, Windows and Android), only for administrators, with a confirmation first ("3 to 5 times bigger, roughly 80 to 200 MB, only this song"). It searches for a Hi-Res copy and saves it as a separate "Artist - Title [Hi-Res]" file next to the normal one. **If no Hi-Res copy exists it automatically downloads the normal quality instead** (same sources and order as a plain Download).

**Android Look and feel** (same as the web): Settings > Look and feel has an accent colour, compact lists and format/source badges on or off.

**Connection drops.** The "Can't reach FLACie. Still trying…" toast mostly appeared when the server was restarted (each update I deploy restarts it) and sometimes after a lost signal or a sleeping phone. The page now keeps its place for 30 minutes if the signal drops, retries patiently, and when the server was restarted it **reloads itself** as soon as the server is back instead of getting stuck on the toast.

Everything from beta 16 is included.

**Tested:** the title cleaning on a spread of real titles (C# and Kotlin unit tests), a live Soulseek search for the song (0 files for the full-name query, 244 for the first-word query, 84 normal and 25 Hi-Res candidates), the real download of "All The Stars" through the app's download code (20 s, 16-bit/44.1 kHz FLAC, 25 MB), the YouTube match for the same song (4.7 s), Hi-Res detection and ranking (unit tests), the web Hi-Res button and confirmation at phone width, the Android Hi-Res pill and Look and feel, the page reloading itself after a server restart, the release APK launching with no crashes or ANRs, the Windows app starting and its server stopping with it, frank redeployed and healthy.
**Not tested:** an actual Hi-Res download (the search and ranking were verified, but I did not put a Hi-Res file into your library), the automatic fallback from Hi-Res to normal as a live run, the Hi-Res button being hidden for a non-admin account, the auto-reload on frank itself, a real phone, the MSI installer itself.


## FLACie 1.0 beta 16

_v1.0.0-beta16, 2026-10-05, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta16))

## FLACie 1.0 beta 16 (Android 1.0-beta16 / versionCode 30, Windows 0.16.0)

**Downloads page on Android, the same as the web.** Settings > Music sources > Download log (and Dashboard > Download log). Sections Running / Failed / Soulseek / Lidarr / YouTube and open sources (filter chips along the top), each download with its source badge, exact time and "x min ago", how long it took, and when opened the step-by-step story (what each source said) and where the file went. It is a log kept across restarts and shows this phone's downloads plus the server's (FLACie Web searches, Autoplay, charts, imports) when FLACie Web is set up. The server gained `GET /api/downloads` for this.

**Dashboard (administrators) and a plain Settings for everyone, on web, Windows and Android.**
- **Dashboard** = the old Admin page plus everything server-wide that used to sit in Settings: download services (Soulseek, Lidarr, file mover), open sources, background downloads and daily charts, storage, notifications. Administrators only; on Android it is Settings > Server > Dashboard (it also keeps a phone's own download setup when FLACie Web is not set up).
- **Settings** is now just yours: playback, a new **Look and feel** section on the web (accent colour, cover colours in the player, compact lists, badges, animations; saved per browser) and, on Android, the existing appearance, playback and controls. Dashboard and Download log are one click away, and in the account menu.

**Quality of life (web and Windows)**
- The **Connect to a device** popup now closes when you click anywhere outside it (and on Escape), instead of only via its button.
- The **now-playing bar shows the cover** again for songs that were just downloaded (it was missing when the download had no cover of its own; one is now looked up).

Everything from beta 15 is included.

**Tested:** the Dashboard, Settings, Look and feel (accent change and saving) and the Devices popup (outside click, Escape, button) in the browser, the Android Downloads screen with sample entries (sections, badges, times, expanded stories), the Android Settings / Dashboard split on the emulator, the new server endpoint (401 without a token on frank), unit tests, the release APK launching with no crashes or ANRs, the Windows app starting and its server stopping with it, frank redeployed and healthy.
**Not tested:** the Android Downloads screen showing the server's log (frank only just got the endpoint and its log is empty), a real running download appearing live in the new screens or under a song in the results, a non-admin account seeing the plain Settings, the cover fix on a real freshly downloaded song, a real phone, the MSI installer itself. Android has no "Look and feel" section (its Settings already had appearance and playback tweaks); that is the one gap against the web.


## FLACie 1.0 beta 15

_v1.0.0-beta15, 2026-10-05, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta15))

## FLACie 1.0 beta 15 (Android 1.0-beta15 / versionCode 29, Windows 0.15.0)

**Downloads you can follow (FLACie Web and the Windows app)**
- New **Downloads** page (rail, the account menu and Settings > Download log). Sections: Running now, **Failed**, **Soulseek**, **Lidarr** and **YouTube and open sources**. Each download shows what it was, who asked for it (you / Autoplay / a playlist / daily charts), the exact time and "x min ago", how long it took, which source delivered it (coloured badge) and, when opened, the **step-by-step story** (what each source said, e.g. "Soulseek had nothing it could finish → Internet Archive had nothing → YouTube: ready to play") and where the file was saved. A filter box finds a song or user; admins can switch to everyone's downloads.
- It is a **log kept across restarts** (the newest 5000 per user), not just what is running now.
- **Admin** is also in the account menu and the Settings sections, not only the side rail.
- In search results (and the charts) each song and album now shows its **download progress right under the title** ("Searching Soulseek…", "Looking on YouTube…", "from Soulseek"), and the button says Downloading… / Try again, so you can see it however far down the list you are.

**Info panel**
- **Spectrum is now a choice** (the first graph used to be reachable only through the sample-rate chip, and that chip is missing for songs without that info).
- New **Came from** line in the File section: downloaded from Soulseek / Lidarr / YouTube / … and when, or "in your Jellyfin library, no download record". (Songs downloaded before this version have no record.)
- A song that was just downloaded and is played straight from your server used to be labelled **"Cloud"/"Download"**; it is now **"Streaming"** (with an explanation: it becomes a normal library song after the next Jellyfin scan), in the web app and on Android.
- Real file details for those songs: bit rate, sample rate, bit depth and size (it showed "0 MB" and no quality before), read by the file mover's new `/probe`.
- In the full-screen player the **top bar now takes the player's background** instead of a black strip.

**Server side:** the file mover (`/probe`, `/ytdl`) and the yt-dlp container were updated on frank; yt-dlp (YouTube) stays OFF until you switch it on in Settings.

**Tested:** the Downloads page with sample log entries at phone width (sections, badges, times, expanded step stories), the Info panel on a real song (Spectrum chip, Came from, size), `/probe` on a FLAC and an AAC file through the file mover (and its refusals), the build, frank redeployed and healthy, the release APK launching without crashes or ANRs, the Windows app starting and its server stopping with it.
**Not tested:** the live progress line under a song in the search results while a real download runs (the code is in, I did not run a live download for it), the new top-bar colour on a wide desktop window, Android's Downloads screen (Android keeps its own list; only the "Streaming" wording changed there), a real phone, the MSI installer itself.


## FLACie 1.0 beta 14

_v1.0.0-beta14, 2026-10-05, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta14))

## FLACie 1.0 beta 14 (Android 1.0-beta14 / versionCode 28, Windows 0.14.0)

**New: a last-resort source for songs nothing else has (YouTube, via yt-dlp).** Popular songs that Soulseek, the free sources and Lidarr all miss can now be fetched: the server finds the song's official audio (YouTube "Topic" and official-audio uploads first, title, artist and length checked, live/cover/remix/sped-up uploads skipped) and saves it to your library with tags. It is a fourth entry in Settings > Open sources (web, Windows app and Android), in the same list as the Internet Archive, Audius and Jamendo, so you can put it first or last. **It is OFF until you switch it on**: it takes audio from YouTube, which is against YouTube's terms of service, and the file is AAC (about 130 kbps, not lossless).
- It runs in its own small container next to the file mover on frank (`flacie-ytdl`), reached through the file mover so every device uses it the same way. yt-dlp updates itself whenever that container restarts.

Everything from beta 13 is included (Open sources settings: on/off, order, Jamendo client ID).

**Tested:** the service against real YouTube from frank (a popular song, "Doja Cat - Say So" and "The Weeknd - Blinding Lights", fetched in about 5 s, valid AAC with the right tags and length), the full path through the app's download code and the public file-mover address (found; a made-up song fails cleanly), the Android call to the same route (live test), the file mover refusing requests without the key and rejecting path escapes, the web Settings row and the Android Settings row, unit tests, the release APK launching with no crashes or ANRs, the Windows app starting and its server stopping with it, frank redeployed and healthy.
**Not tested:** a download started by tapping Download in the Android app or the web page with yt-dlp switched on (the pieces were tested, not that exact tap), a real phone, the MSI installer itself, how long YouTube keeps working before yt-dlp needs a restart/update.


## FLACie 1.0 beta 13

_v1.0.0-beta13, 2026-10-05, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta13))

## FLACie 1.0 beta 13 (Android 1.0-beta13 / versionCode 27, Windows 0.13.0)

**Open download sources are now configurable on every device.** Settings > Downloads (Android) and Settings > Open sources (FLACie Web and the Windows app, admin only like the other download settings) have, for the Internet Archive, Audius and Jamendo:
- an on/off switch each,
- an order (move up/down): the first source on the list that has the song is used. They still all look at once while Soulseek searches and only download if Soulseek found nothing, so a slow or disabled source never holds up Soulseek or Lidarr,
- a **Jamendo client ID** field (free from devportal.jamendo.com; Jamendo stays off without one). The web page has a "Check Jamendo ID" button; Android checks it against Jamendo before saving.

The setup lives in your account profile, so the phone, the Windows app and the web page share it. The Jamendo environment variable on the server still works as a fallback.

Everything from beta 12 is included.

**Tested:** the web page at phone width against the real account (check the ID, reorder, save, reload: all kept), the Android section on the emulator (toggle, reorder, Jamendo ID, values stored), unit tests for the order/on-off rules and the live-API finders, the release APK launching with no crashes or ANRs, the Windows app starting and its server stopping with it, frank redeployed and healthy.
**Not tested:** that the phone picks up a setting changed on the web (and the other way round) after a sync, a real phone, the MSI installer itself, an actual download that uses a reordered source.


## FLACie 1.0 beta 12

_v1.0.0-beta12, 2026-10-04, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta12))

## FLACie 1.0 beta 12 (Android 1.0-beta12 / versionCode 26, Windows 0.12.0)

*Assets re-uploaded after release with the Jamendo settings field (the version numbers did not change, so install over the earlier beta 12 if you already have it).*

**New: open download sources on every device.** "Download this" now also looks on the Internet Archive (only the Live Music Archive and netlabel collections), Audius, and Jamendo (only tracks whose artist allows downloads) while Soulseek searches. They only *find*; a file is downloaded from them only if Soulseek found nothing, and Lidarr stays the final fallback, so they cannot slow or block the main path. Files are filed through the file mover's new `/fetch` endpoint (live on frank). Downloads show their source ("archive", "audius", "jamendo"). Songs only; albums still use Soulseek then Lidarr.
- Android, the Windows app (it hosts the same server code) and FLACie Web on frank all have it.
- **Jamendo:** Android has Settings > Downloads > Open sources > "Jamendo client id" (checked against Jamendo before it is saved, synced with your account). On the web it is the `FLACIE_JAMENDO_CLIENT_ID` environment variable (already set on frank).

Everything from beta 11 is included.

**Tested:** the finders against the live APIs from the web server code and the Android code (Audius and Internet Archive hits, Jamendo hit and clean misses), the file mover refusing loopback/http/path-escape/no-key requests, real Audius and Jamendo files fetched and filed through frank's file mover, a real Audius download end to end through the web coordinator, the Jamendo field saving on the emulator, unit tests, the release APK launching on the emulator, the Windows app starting and its server stopping with it.
**Not tested:** an actual download started from the Android app or using Jamendo end to end, a real phone, the MSI installer itself, hgs notifications.


## FLACie 1.0 beta 11

_v1.0.0-beta11, 2026-10-04, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta11))

## FLACie 1.0 beta 11 (Android 1.0-beta11 / versionCode 25, Windows 0.11.0)

**Fixes**
- **Devices works again.** Beta 9/10 renamed the Jellyfin client, which split the sessions so other devices (and the web player) disappeared from Devices. Reverted; update every device to beta 11 (and FLACie Web is redeployed).
- **Much faster cold start on Android.** The last blended library is saved and shown straight away (about 3 s after launch on a slow emulator, instead of about 16 s); the full refresh runs in the background.
- **Swiping the app away from recents stops playback** and removes the notification.
- Graphs and dB meters (Android Info tab and FLACie Web) now follow the volume.
- Back from Up next returns to the full-screen player.

**Look**
- Android player: a scrollable row of pills above the seek bar (Lyrics, Info, Up next, Equalizer, Add to playlist, More), like YouTube Music. The sleep timer lives in Settings > Playback.
- Calmer Home bottom bar: more room around the mini player and the navigation.

**Tested** (Android emulator at 1080x2400, FLACie Web at 127.0.0.1 against the real Jellyfin, release APK smoke-tested): Devices in both directions (emulator <-> web), pills, Up next + Back, Info graphs shrinking with volume, swipe from recents (session and notification gone), cold start timing, Windows app starts, its server answers /healthz and stops with the app.
**Not tested:** a real phone, the Fold or RG Rotate, short/landscape screens for the new pill row, Bluetooth/lock-screen controls after swipe-away, the MSI installer itself (only the built app folder), iPod sync.

Android APK is signed with the dev PC's debug key (installs over earlier betas).


## FLACie 1.0 beta 10

_v1.0.0-beta10, 2026-10-04, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta10))

Pre-release. Android APK and Windows installer (the installer carries the full FLACie Web player and admin dashboard).

**New**
- **Full-screen player on phones, narrow windows and portrait tablets**, laid out like YouTube Music: the cover takes the free height and stays square, then title, seek and controls, and Up next / Lyrics / Info as icon tabs along the bottom. A tab opens a sheet over the cover; the sheet's header shows the album art and title and takes you back to the cover (as does the top chevron or the same tab again). The player covers the whole screen.
- **Mini player on a phone** now has previous, play/pause, next and close, centred in the bar (the close button used to drop out of the bar and the cover sat under the progress line).
- **Phone tab bar** is evenly spaced and every label fits down to 320 px wide.
- **Settings is admin protected**: Downloads (service keys), Background downloads (free-space limit, daily charts) and Storage are only shown to admins; the phone's server settings rows and the "change settings"/"run charts" calls are admin only too. Everyone else sees a note.
- Admin dashboard: Jellyfin-style device tiles that stay readable on phones, Info popup with direct play/transcoding, file and client details, every browser listed separately.
- Info tab opens at the top; the top bar is the plain background colour with a faint line.

**Tested**
- Web: every page at 320, 360, 390, 768 and 1300 px wide (nothing sticking out, mini player never over the tab bar); the player and all three sheets at 280–438 px; the admin lock with a non-admin account (sections hidden, `/admin` refused, API 403).
- Android: unit tests; installed over beta 9 on the emulator; Explore loads 6,836 songs; Settings shows the server rows to an admin.
- Windows: the installer's app starts its own player, which serves the new pages and stops with the app.

**Not tested / known**
- The Android player screen itself has its own layout and was not changed; the new player layout is the web player (and the Windows app).
- ntfy against your real server (it is down at the moment); a phone or transcoding session in the admin Info popup; weak-signal streaming; a full day of daily charts; the iPod manager button in the Windows app.
- The Android Explore shows "No songs yet" for a few seconds on a cold start while the library loads.


## FLACie 1.0 beta 9

_v1.0.0-beta9, 2026-10-04, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta9))

Pre-release. Android APK and Windows installer (the installer carries the full FLACie Web player).

**New on Android**
- **Explore** tab (replaces Library): Songs, Albums, Artists, Genres and Charts, with a text filter, A–Z, genre, decade, lossless or lossy, and a sort. Favorites, recents and sources are behind "Favorites, sources…".
- **Info** button on Now Playing: format and quality at a glance, then a graph for whatever you pick. The spectrum, bars, spectrogram (scrolling), loudness meter and stereo scope all move with the song as it plays; bit rate is a static comparison.
- Settings > Background downloads now has the server's own settings (keep free space, daily charts, which charts, songs per chart, Run now) and the music storage; a fuller Account page; an "Admin dashboard" row for server admins.
- The app now shows in Jellyfin as "FLACie" instead of "ipodplayer".

**New in FLACie Web and the Windows app**
- **Admin dashboard** (Jellyfin admins, or the names in `FLACIE_ADMINS`): every device across all accounts as small tiles (what is playing, pause/stop, an Info popup with direct play vs transcoding, file and client details), every open browser listed separately with its browser, system and address, server numbers you can tap (accounts, library, storage, downloads, imports), an activity feed, and **ntfy notifications** (set the address and topic yourself; nothing is sent until you turn it on).
- **Explore** (songs, albums, artists, genres, charts) with filters; Home that waits for your account before drawing; a faster library with a saved copy; Info graphs that are all live; compilation covers; new logo; a plain top bar.
- Info tab opens at its top instead of wherever the last tab was scrolled.

**Tested**
- Android: unit tests; installed over the previous beta on the emulator; Explore (filters, charts), every Info graph with a song playing, Settings server rows and Account against the live server.
- Windows: the installer's app starts its own player, which answers, serves the new pages and stops with the app.
- Web: admin dashboard, ntfy message format (against a stand-in server), all Info graphs, device Info popup.

**Not tested / known**
- ntfy against your real server (it is down at the moment) and notifications from a real import or charts run.
- A phone or a transcoding session in the admin Info popup; Brave/Edge/phone browser names.
- Weak-signal streaming and the lower-bitrate fallback; a full day of daily charts; the iPod manager button in the Windows app.
- The Android Explore shows "No songs yet" for a few seconds on a cold start while the library loads.
- Live graphs are off on iPhone/iPad. The Android admin dashboard opens in the browser.


## FLACie 1.0 beta 8

_v1.0.0-beta8, 2026-10-04, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta8))

Pre-release. Android APK and Windows installer (the installer now carries the full new FLACie Web player).

**New in FLACie Web / the Windows app**
- **Explore** replaces Songs, Albums, Artists and Charts in the sidebar: one place with tabs (Songs, Albums, Artists, Genres, Charts) and filters: text, A–Z, genre, decade, lossless or lossy, and sort.
- Home like YT Music with your playlists up front; Shuffle all at the top right; playing from Home opens the full-screen player.
- Info tab in the player: pick a graph from the format chips. Live spectrum, LED visualizer, scrolling spectrogram, loudness meter and stereo scope all move with the song; bit rate is a static comparison.
- Songs filed on a compilation ("Now That's What I Call Music") show their own album's cover and name.
- Playlist names lose the "(LAC)" tag. New FLACie logo; translucent top bar; layout fits any size and orientation.
- Much faster library: pages load in parallel and the last library is kept so a restart shows it at once. "Play here" resolves the queue in parallel. Nothing is saved to your account until it has been read (no empty profile can overwrite yours).
- Quick picks are chosen by a taste profile built from your plays, favourites and playlists.

**New in the Android app**
- Settings > Background downloads now shows the server's own settings (keep free space, daily charts, which charts, songs per chart, Run now) and the music storage.
- A fuller Account page: your counts, what is connected, FLACie Web address.

**Tested**
- Android: unit tests; installed over beta 7 on the emulator and checked Home (playlists without "(LAC)"), Settings with the server rows and the charts picker, and the Account page against the live server.
- Windows: installer built; the app starts its own player, which answers, serves the new pages and stops with the app.
- Web player: every Info graph checked with a song playing.

**Not tested / known**
- Weak-signal streaming and the lower-bitrate fallback on a real phone; a full day of daily charts; the iPod manager button in the Windows app; iPod engine on the emulator.
- The Android app does not yet have Explore, the Info graphs, the compilation-cover fix or the taste-based Quick picks (those are in FLACie Web and the Windows app).
- Live graphs are off on iPhone/iPad.


## FLACie 1.0 beta 7

_v1.0.0-beta7, 2026-10-04, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta7))

Pre-release. Android APK and Windows installer.

**New**
- Search ranks results by word (title, then artist, then album) and forgives typos, in the app and FLACie Web.
- Autoplay: when the queue ends it adds *different* songs (same artist and similar artists), never another release of a song just played, and fetches the next few you don't own.
- Home like YT Music: Listen again, Quick picks, an hourly Daily Mix, mood and genre rows, Albums for you; a Playlists tab on Android.
- Import playlists from a file (iTunes Library.xml, M3U, Spotify CSV, text) on FLACie Web or from the phone; the server does all the downloading. Optional "replace playlists with the same name".
- Daily charts: new songs from the charts are added to your library every day (Settings > Background downloads), stopping when free space is low. Nothing is ever deleted.
- FLACie Web: storage view, redesigned Settings and Account, Settings reachable from the full-screen player, tidier search bar and Devices strip.
- Android streaming: wake/Wi-Fi lock while playing, deep buffer and on-disk cache, next songs fetched in full, lower-bitrate fallback after repeated stalls (Settings > Streaming quality).

**Not verified**
- Streaming on a genuinely weak connection (the lower-bitrate fallback has not been triggered in testing).
- Charts have not run through a full day yet.


## FLACie 1.0 beta 6

_v1.0.0-beta6, 2026-10-01, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta6))

Android
- Connect and Jams now report properly when you sign in through a NAS or another device's saved account: the app mints its own Jellyfin token (by approving its own Quick Connect request with the saved one), so it shows up in Devices on the web and other apps with what it is playing.
- Taking over from, or sending to, another device works when you are deep in a long shuffle (the queue sent is a window around the current song, not the first 200).
- Covers: a song's own embedded picture is used before the album's shared folder picture, so unrelated songs no longer share one cover.

FLACie Web (ghcr.io/asch-bh08/flacie-web) in the same push
- YT Music style layout: search box on top, account menu top right, your playlists in the sidebar.
- Full-screen player with Autoplay, a real Up next, synced lyrics on the audio clock and file info (format, bit rate, sample rate); Back and Esc close it; queue survives reload.
- Listening on another device strip and a device picker (only your own Jellyfin account's sessions).
- Online search with downloads (Soulseek first, Lidarr fallback), keys kept on the server.

## FLACie 1.0 beta 5: FLACie Web

_v1.0.0-beta5, 2026-10-01, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta5))

## New: FLACie Web (self-hosted)
A web version of FLACie you run yourself in Docker. Sign in with Jellyfin (password or Quick Connect) or with a NAS (SMB share). Your playlists, favourites and services are the same profile the Android app uses, so they show up on both.

- Home, search (punctuation doesn't matter), albums, artists, songs, playlists, favourites, settings, player with seek and up next
- Plays Jellyfin and NAS music; covers from Jellyfin, the NAS album folder or the file itself
- Audio and covers go through your server, so the browser never sees a password or API key

Run it:
```
docker run -d -p 8080:8080 -v ./flacie-data:/data ghcr.io/asch-bh08/flacie-web:latest
```
See `ipodsync/src/FLACie.Server/docker-compose.example.yml` for options (FLACIE_JELLYFIN_URL, FLACIE_ALLOW_NAS_LOGIN, PUID/PGID).

## Android
Unchanged since beta 4 (the APK is attached for convenience).

Tested: NAS sign-in, library, covers, playback and seeking, in Docker. Not yet tested: Jellyfin sign-in on the web.

## FLACie 1.0 beta 4

_v1.0.0-beta4, 2026-10-01, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta4))

- Login screen: sign in with Jellyfin or a NAS, or continue as a local-only guest. Your profile (services, playlists, favourites) is kept in both accounts, so either one brings everything back
- Devices (Connect): see what your other FLACie/Jellyfin sessions play, control them, take over playback or send it to another device
- Jams: listen together with anyone on your Jellyfin server (Jellyfin SyncPlay; the user needs SyncPlay allowed in Jellyfin)
- NAS covers: uses the album folder's own cover.jpg/folder.jpg, then Deezer, then iTunes
- "Title - Artist" file names on the NAS credited the right way round

Connect and Jams are new and not yet tested between two real devices.

## FLACie 1.0 beta 3

_v1.0.0-beta3, 2026-10-01, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta3))

- Search ignores punctuation: "scream and shout will i am" finds Scream & Shout; karaoke and cover versions ranked lower
- No more duplicate songs between NAS, Jellyfin and fresh downloads
- Recently Added shows what actually arrived last
- Covers load reliably in search results
- Jellyfin/NAS/Plex settings open again when connected; Jellyfin can use your signed-in account or disconnect
- Settings reorganised

## FLACie 1.0 beta 2

_v1.0.0-beta2, 2026-10-01, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta2))

- Search works like YT Music: popular songs first, plus albums and EPs you can download whole
- Downloads: lossless whenever anyone has it, faster peer picking, stalled peers swapped out
- Fixed Soulseek search returning nothing
- Playlist covers and lock-screen art change with each song
- Fresh downloads survive an app restart

## FLACie 1.0 beta

_v1.0.0-beta, 2026-09-30, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta))

First beta of FLACie 1.0.

- Share a playlist with another person on your Jellyfin server (long-press it > Share with..., needs Jellyfin 10.9+)
- Recently Downloaded list (Library and a Home shortcut)
- Jellyfin/NAS songs show instantly at launch and stay put after rescans
- Seeking recovers from network hiccups; Play always resumes after an error
- Covers for Jellyfin, Plex, NAS and downloads; account playlists; accessibility and contrast pass

## FLACie 0.9.10

_v0.9.10, 2026-09-30_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v0.9.10))

Jellyfin/NAS songs show instantly at launch (cached) and no longer disappear after a rescan; seeking recovers from network hiccups and Play always resumes after an error; NAS seeks jump directly instead of reading through the file.

## FLACie 0.9.9

_v0.9.9, 2026-09-29_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v0.9.9))

Download button guesses the right artist; garbled Chinese names repaired; crash in Artists fixed; better de-duplication; tested a real Soulseek download end to end.

## FLACie 0.9.5

_v0.9.5, 2026-09-29_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v0.9.5))

Accessibility and contrast pass, keyboard-friendly forms, library de-duplication fix (songs on several albums), playlist fixes, Up Next opens on the current song.

## FLACie 0.9.4

_v0.9.4, 2026-09-29_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v0.9.4))

Drag-to-seek fixed on phones; cover art for Jellyfin, Plex, NAS and Soulseek downloads; Playlists shows your account's playlists only; EQ/sleep sheets and setup screens scroll in landscape.

## FLACie 0.9.3

_v0.9.3, 2026-09-28_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v0.9.3))

- Download on an "Albums you don't have" row now tries Soulseek first (Lidarr only if Soulseek can't find it).
- The bottom Download button uses the matching song's real artist and title (e.g. *Download "Out of Space" by The Prodigy*) instead of your search text, so the file is tagged properly and gets cover art.

## FLACie 0.9.2

_v0.9.2, 2026-09-28_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v0.9.2))

- Seeking works on phones (dragging the seek bar no longer counts as swipe-back; swipe back from the left edge on Now Playing).
- Download on a Soulseek search result goes to Soulseek first; Lidarr only if Soulseek can't find it.
- Search matches all your words in any order: "intro xx" finds "The xx – Intro".

## FLACie 0.9.1

_v0.9.1, 2026-09-28_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v0.9.1))

## FLACie
ipodplayer and ipodsync are now one project and one name: **FLACie**, with a new logo.

- **Android** — `FLACie-0.9.1.apk`: the app formerly called ipodplayer, renamed, with the new icon. It **updates in place** over ipodplayer (same app underneath), keeping your library, settings, account and playlists. Includes the built-in iPod engine from 0.9.0 (Sync mode → *iPod plugged into this device (USB)*, no second app needed).
- **Windows** — `FLACie-0.5.0-windows-x64.msi`: the desktop app formerly called ipodsync, renamed, with the new icon. Per-user install, no admin rights; unsigned, so SmartScreen asks once (*More info → Run anyway*). It installs as a new program (Programs\FLACie): uninstall the old *ipodsync* entry if you had v0.4.0 installed. Your iPod backups (Documents\ipodsync) are untouched.

Not new since 0.9.0 otherwise; a real write to an iPod plugged into a phone is still untested — try one small change first.

## ipodplayer 0.9.0 — one app

_v0.9.0, 2026-09-28_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v0.9.0))

## 0.9.0 — ipodplayer and ipodsync are one app
**Edit an iPod plugged straight into your phone or handheld, with nothing else installed.** ipodsync's verified iPod engine is now built into ipodplayer, so the separate ipodsync app is no longer needed (you can uninstall it).

- **Settings > Mode > Sync mode > iPod plugged into this device (USB)**. The first time, allow *All files access* (Android's permission for writing to USB storage) and, when writing, allow USB access to the iPod — that's how the app reads the iPod's serial number, which its database signature needs.
- Same safety as before: edits are staged, dry-run checked, and only written after you confirm; the iPod's database is backed up first (Documents/ipodsync/ipod-backups) and restored automatically if any check fails.
- Still works with an iPod plugged into a PC running ipodsync, too.
- 64-bit ARM devices only for the on-device engine (every recent phone, the RG Rotate). The APK is bigger (~43 MB) because it carries the engine.

**Not yet tested with a real iPod plugged into a phone.** Reading and dry runs were tested on the RG Rotate against a copy of an iPod's database, and a full write by the same engine was verified independently on a PC. Try one small change first.

## ipodplayer 0.8.2

_v0.8.2, 2026-09-28_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v0.8.2))

## 0.8.2
- **Sync mode works with the iPod plugged straight into this phone/handheld over USB** — no PC, no Wi-Fi. Needs the ipodsync Android app **v0.4.0+** (https://github.com/asch-bh08/ipodsync/releases/tag/v0.4.0) installed with *All files access* allowed; ipodplayer opens it automatically. Settings > Mode > Sync mode > *iPod plugged into this device (USB)*.
- Edits are staged, dry-run checked and only written after you confirm; ipodsync backs the iPod's database up first and restores it automatically if any check fails.
- Not yet tested: an actual write to a real iPod over USB from a phone (no iPod was attached during this build) — try one small change first.
- Includes 0.8.1's crash fix.

## ipodplayer 0.8.1

_v0.8.1, 2026-09-28_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v0.8.1))

## 0.8.1
- **Fixes a crash** when searching: a song with no cover art (and no Lidarr match) crashed the app when it was the top result.
- **Sync mode** (Settings > Mode): browse and edit an iPod through ipodsync on your PC -- rename songs, star ratings, create/rename/delete playlists, add/remove/reorder tracks. Edits are staged, dry-run checked by ipodsync, and only written after you confirm; ipodsync backs the iPod's database up first and restores it automatically if a check fails. Needs IpodSync.Web with the new `/api/apply-edits` (ipodsync main).
- Minified release build (signed with the debug key, installs over earlier builds).

## ipodplayer 0.8

_v0.8, 2026-09-28_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v0.8))

## 0.8
First minified release build (R8; signed with the debug key so it installs over earlier builds).

- **Modern theme by default** (YT Music/Spotify-style); iPod skins and click-wheel modes under Settings > Appearance > Theme
- **Safe-area aware** (camera cutouts/punch-holes) with adaptive navigation: bottom bar on phones/Fold cover, rail on wide/square screens
- **Account**: sign in with your Jellyfin user (Quick Connect or password) to save and restore all service connections, playlists and favourites on a new install; playlists also mirrored as real Jellyfin playlists
- **Now Playing** laid out per screen shape (portrait, square, landscape/wide with a lyrics pane)
- **Lyrics** for any song: Jellyfin, local .lrc, then LRCLIB; synced lines follow playback, tap to seek
- **No more NAS/Jellyfin duplicates**: same files recognised by path, credit-spelling tolerant matching; NAS section shows NAS-only files
- iPod theme usable on phones/foldables (thumb-sized click wheel on tall screens)
- Fixes: Add to playlist picker never opened; Theme setting could be flipped by a mis-tap

## ipodplayer 0.7

_v0.7, 2026-09-28_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v0.7))

## 0.7
Debug build (not release-signed). Search + NAS filing fixes:

- NAS scan no longer shows the same song twice: title-cleaning now strips "Artist - Album - Track - Title" filenames correctly (including when the album folder carries a trailing year the filename doesn't), and duplicates landing in the same scan batch now dedupe against each other, not just against tracks already merged in
- Search leads with a single ranked "Top result" card (YT Music-style) instead of a flat wall of rows; Songs/Albums cap at 4/3 with "Show more" to expand
- Owned tracks missing local art (NAS/Cloud sources) now pull a real cover from Lidarr's catalog instead of a placeholder icon; Soulseek catalog rows get a two-tier art fallback (album lookup, then artist poster)
- A song-style "Artist - Title" search no longer bleeds into the album catalog lookup, which could surface an unrelated single mislabeled as an "album" with no Songs section at all
- Fixed the real bug behind Soulseek searches silently coming back empty: slskd's search API doesn't fill in results progressively, so a 6s client timeout was racing its own completion time and losing the coin flip more often than not; bumped to 12s (search) / already 25s (actual download) and hardened the polling loop so one failed request can't abort an otherwise-winning search

## ipodplayer 0.6

_v0.6, 2026-09-26_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v0.6))

## 0.6
- Three explicit modes (Modern Player / iPod Emulator / Click Wheel Fullscreen) on one Appearance screen, with previews
- Socket-style tilted-iPod carousel gallery for all 23 models (faked 3D), L1/R1 to cycle
- Modern Player: For You shelves (daily mixes, suggested, on repeat, discover, genre/decade), Songs sorting, A-Z sections
- Fullscreen click wheel: artwork uses the freed space; body view grows and scrolls to stay readable
- Back/controller fixes (Search, Appearance Back pill, stale screen after mode switch), body never hidden by the hinge
- Release build: R8, baseline profile; jank ~52% -> ~5% on the RG Rotate


