# FLACie changelog

Every release, newest first (generated from the GitHub release notes; the downloads stay on each release page). The newest build is marked Latest. Versions up to 0.9.x were called ipodplayer.

## v1.0.0-beta88

**Accessibility in Retro**
- Retro now meets the same accessibility standard (WCAG 2.1 AA) as the rest of FLACie, checked automatically on a phone and a PC.
- A screen reader announces the click wheel's MENU, next, previous and play buttons by name.
- The MiniDisc's jog dial and its play button are announced separately.
- The iPod's side panel can be reached and scrolled with the keyboard.

**Under the hood**
- A test that failed only late in long test runs is fixed. It was checking the age of a pretend download against a fixed limit, so it went out of date as the run went on.

## v1.0.0-beta87

**Favourites stay removed**
- Removing a favourite now sticks on all your devices. Before, a song you unfavourited came back from any other device that still had it, because devices just added each other's favourites together. Now whichever happened last, adding or removing, wins, on the web and Android.
- Favourites has **Clear all** (it asks first, then offers Undo). Use it to tidy an account that picked up someone else's favourites; every device follows.

**Android**
- Sign-in and sync errors are in plain words, saying what went wrong and what to try. You'll no longer see "HTTP 404" or "Unable to resolve host".

## v1.0.0-beta86

**Privacy fixes**
- Favourites, playlists, history and ratings no longer pass from one account to another on the same Android phone. Signing out now clears them from the phone; they're still in your account and come back when you sign in. Signing in as a different person starts clean.
  - Before, the phone merged them into whoever signed in next, which is how another account ended up with your favourites. Anything already merged that way is still in that account: remove it there, or ask and I'll help clear it.
- Devices now shows only your own account's devices, on the web and Android. As a Jellyfin admin you used to see everyone's playback there as if it were yours. Everyone's playback, with the account name, is on the Dashboard (admins only).

**Also**

- Web: removing a song from a playlist offers Undo, which puts it back in the same place. Android already had this.
- The README describes FLACie as it is for 1.0.
- The self-hosting guide covers the shared-links folder in backups, the renamed Library settings, and how the phone learns your web server's address.

## v1.0.0-beta85

**The 1.0 logo**
- A neon ribbon play triangle with an F inside it, glowing cyan to violet, with a calm sound wave behind it.
- It replaces the old pink F everywhere: the Android launcher icon, the web's tab and install icons, the web's top bar, the Windows app icon and the README.

Nothing else changed since beta 84.

## v1.0.0-beta84

**Fixed**
- Share on Android sent only the song's name. The server was turning away the phone's request for a link; it now accepts it, so Share sends a link that opens in FLACie.
- The phone now always learns your FLACie Web address (it's passed through your profile), even on an account that never had it set. If the phone still can't make a link, it says why.
- The phone can also fetch seek-bar shapes from the server again. The same sign-in problem had been blocking those.
- Android Home: after Got it, the welcome card stays gone. It came back when you scrolled or switched tabs.

- Info on a wide screen: the long File card spans two columns and cards are only as tall as their content, so there is less to scroll.

**For sharing to work from your phone:** open FLACie on the web once after updating, so the phone learns the address.

## v1.0.0-beta83

**Share songs and playlists by link**
- **Where:** Share is in a song's menu, the player's More menu, playlists, and album, artist and genre pages, on the web and on Android. It makes a short link on your FLACie server (`/s/...`) and opens your share sheet, or copies the link on a PC.
- **For the person who gets it:**
  - They open it and sign in to FLACie as themselves, and come straight back to the link.
  - They see who shared it and play the songs from their own library.
  - They can save it as one of their playlists, or download the songs they don't have.
- **What goes with the link:** only the songs' names and artists, nothing from your account.
- **Without a FLACie server:** Android shares the song's name as text instead.

## v1.0.0-beta82

- Fixed: turning shuffle on mid-song could leave only a few songs to come, so playback stopped early. The song playing now goes first in a fresh shuffle, with every other song after it.
- Retro shows the queue in the order it will actually play, so its "x of y" and Up next match the app.

## v1.0.0-beta81

**Android**
- Retro follows the app's queue whenever it changes (Play next from the notification, Autoplay, an edit in Up next). It takes the queue in the app's order and doesn't shuffle it again. Known: the iPod's "x of y" then counts in the app's queue order.
- Offline: By album shows the space each downloaded album takes, biggest first, with a tap to remove one.
- Songs: Offline only shows just the songs kept on the phone.

**Docs**
- The changelog covers betas 79 and 80.

## v1.0.0-beta80

**Android**
- A screen reader names every button on Home, Search, Library, Devices, song lists and the player (the account button, the search box and album cards were unnamed).
- Home: a short one-time welcome showing where Home, Search, Library and Retro are.
- Search: Show only All, Songs or Albums.

**Web**
- Search results: Show only All, Songs, Albums, Artists or Playlists.
- When the library can't be read, the message has a Try again button.

**Windows app**
- The keyboard's media keys and Windows' media panel are turned on for the player. Not yet checked on a Windows PC.

**Docs**
- New by-hand checks for betas 74 to 79: what needs a real phone, your home network or Windows.

## v1.0.0-beta79

**Web**
- Home: a Carry on button with the song's name. It resumes the paused song, or plays the last one again.
- Lists remember their sort, and Back returns to where you were in a list.
- Playlists with none yet explains how to make one.

**Android**
- Back returns to the same place in a list.
- Text and icons on the cover's colour (Shuffle all, the play button, the Hi-Res chip) turn dark or white so they always read.
- The bar for choosing several songs is solid. Empty Favourites, Recently played, Downloads and playlists say what to do.
- "Favourites" is spelled the same everywhere.

**Docs**
- The changelog covers betas 72 to 78.

## v1.0.0-beta78

**Accessibility (the 1.0 bar)**
- Every main page and the full-screen player pass automated WCAG 2.1 AA checks on a phone and a PC.
- Text on cover colours turns dark or white so it can always be read.
- With the system's "reduce motion" setting on, nothing animates.
- Every keyboard stop shows where the focus is.

**Web**
- Choose several songs at once: Select in a song's menu, or Shift-click a row. Then Play next, Add to queue or Add to a playlist.
- Share on album, artist and genre pages: your phone's share sheet, or the link is copied on a PC.
- Settings: the 1.0 group names, and a Find a setting box.
- A test now checks the page's memory stays under the 1.0 limit of 300 MB. It uses about 10 MB.

**Android**
- Choose several songs at once: Select in a song's menu, then tap to tick more.
- Sign-in finds Jellyfin servers on your home network; tap one to fill in the address.
- Offline downloads: Pause downloads.
- Settings: the same group names as the web.

## v1.0.0-beta77

**Web**
- After a server update the page reloads by itself, and a song that was playing carries on from where it was.
- Errors are in plain words: sign-in, settings, import and the admin pages say what went wrong and what to try.
- Up next: removing a song offers Undo, and Save turns the queue into a new playlist.
- Search: your recent searches show as chips when nothing is typed. They're kept with your account.
- Keyboard: / jumps to search, and ? lists the shortcuts.
- Home: a short one-time welcome.

**Android**
- Up next: Save turns the queue into a playlist, and clearing Up next offers Undo.
- Search: recent searches.
- Retro: shuffle and repeat stay in step with the app both ways. Now Playing opens by itself only once, when Retro starts.

## v1.0.0-beta76

- Player: the Options and Retro buttons sit at the right edge of the top bar again.
- Seek frame: the bars fill in round the cover as the song downloads, instead of appearing all at once at the end.
- Retro: lyrics work. The Retro screen's web requests now go through the app.

## v1.0.0-beta75

**Android**
- Retro: "Now Playing" now appears for the song already playing, and your playlists and favourites are in the iPods' menus. If the library is still loading when Retro opens, it fills in once it's ready.
- Bottom bar: four roomy tabs (Home, Search, Library, Devices). Library holds Favourites, Playlists, Jam and the rest of your collection.
- Player: Lyrics and Info open as a card the size of the album art. The title, the controls and a seek line with the time at each end stay on screen.

**Web**
- Retro: "Now Playing" appears as soon as a song starts.

## v1.0.0-beta74

**Android**
- Player: the old sideways panes are back (Lyrics, cover, Info, Up next), restyled in the new look.
- Bottom row: five equal buttons with More in the middle (Lyrics, Up next, More, Info, Add to playlist).
- More and Options menus are clean sheets.
- Covers show instantly from any smaller copy.
- The seek frame shows straight away and stays still, with no live VU. Its start and end blend at 12 o'clock.
- Fixed a garbled dot in Up next rows.

**Web**
- Full-screen player: one now-playing bar along the bottom (heart, More, rating, speed and sleep, volume). The controls sit centred above it.
- Player bar: the controls are centred over the seek bar, with the time at each end.
- The ☰ button folds the side menu to icons and remembers it.
- Seek frame: rough shapes come from the server straight away, and the ends blend.

## v1.0.0-beta73

**Android, redone to match the web**
- Every screen follows its web counterpart: Home (cover across the top, greeting, Shuffle all, moods), Search (with Explore and Songs/Albums/Artists/Genres chips), the bottom bar (Home, Search, Favourites, Playlists, Devices, Jam), album and artist pages (big cover in its own colours, serif title, facts line, Play and Shuffle), Playlists (New playlist / Create / Import), Settings (the web's sections, with chips to jump to each), the Dashboard's number tiles.
- Now Playing: the seek bar is a frame of the song's loudness round the cover; press or drag on it to seek, tap the cover to play or pause. Play is a rounded square between the previous and next pills, the time under the frame.
- Info is one tap away (Lyrics, Up next, Info, More), rebuilt like the web's: the live graph first (spectrum, visualizer, spectrogram, loudness, stereo, bit rate), then the file's facts.
- Every feature of the old screens has a button or menu entry: rating, speed, sleep timer, sound/EQ, heart, add to playlist, play next, add to queue, download, go to album or artist, share, song info, lyrics, Jam, Devices, Retro, queue editing, Autoplay, shuffle and repeat. Explore is a chip on Search and Settings a gear on Home.

**Web**
- On a phone, the seek bar is now the same frame round the cover as on a PC, with the time under it, and an Info button beside Lyrics and Up next.
- A song row's menu opens above the player bar near the bottom of the window (it used to open behind it).

**The seek bar's shape, fixed**
- Shapes now show the song's rises and falls even on loudly mastered songs (each song's shape is stretched between its own quiet and loud parts).
- The server works out a rough shape for any FLAC song nobody has measured yet, so phones get one at once; a computer that plays the song replaces it with the exact one.
- Android: the app read the decoded sound in the wrong byte order (every song looked like noise, so the bars were flat) and asked the server for shapes without signing in. Both fixed; the app also uses the server's shapes and sends back the ones it works out.
- The stereo graph no longer collapses to a dot at a low volume.

**Tested**
- Web: the full browser test suite, 445 passed; the 2 that failed (and the 12 after them in their groups) were made reliable and pass. .NET unit tests 714/714. Rough shapes checked against the real library (0.5 to 2.4 s the first time, then kept).
- Android: all unit tests pass; the release APK on the emulator: 23/23 smoke checks (sign-in, 10,000-song library, scrolling, playing, a 40-skip burst, pause, the queue after a relaunch, Settings > Storage, Up next editing, no crash or ANR).

**Not tested**
- The Android app on a real phone, and on a tablet-sized screen.
- The Windows app (no new MSI; version bumped to 0.73.0 for the next one).

## v1.0.0-beta72

The whole app has a new look ("design I"), on the web and in the Android app. Nothing was removed; some things moved.

**New look**
- The full-screen player: on a phone the cover runs across the top and fades into its own colours, with a serif title (last word in italics), a ring seek bar (the song's own loudness round the play button) and outlined Lyrics / Up next buttons. On a PC: two halves, the cover framed by the seek bar on the left, Up next / Lyrics / Info on the right.
- The Info tab: the graph first (spectrum, visualizer, spectrogram, loudness, stereo, bit rate, all redrawn in the cover's colours), then the file's facts in one card.
- Home: the last song's cover across the top, a greeting, Shuffle all, mood chips and serif shelves.
- Search, browsing, album, artist and genre pages, playlists, favourites, settings, account, dashboard and login all match.
- The player bar takes the playing cover's colours; on a phone it is a floating mini player (play and next; the rest is in the full-screen player).
- The seek bar's shape for the next two songs is prepared while one plays (not on mobile data), so it appears at once.

**Moved**
- Explore is now part of Search (an empty Search shows what Explore did; /explore still works).
- Import is a button on the Playlists page.
- Downloads is in the Dashboard, for administrators.

**Fixed**
- Songs that finished downloading at the same moment could drop out of the library (seen as an Autoplay suggestion vanishing from Up next).
- Letters typed into the search box just after a page loaded were lost.

**Tested**
- Web: the full browser test suite: 444 passed, 3 failed, 12 did not run (they come after a failure in the same group). The 3 that failed and the 12 passed when rerun on their own (load-sensitive tests; the Android build was running at the same time). .NET unit tests 712/712.
- Android: 443 unit tests; one load-sensitive cover-file test failed while the web tests ran and passed 3/3 on its own. The new screens were checked on an emulator.

**Not tested**
- The Android app on a real phone.
- The Windows app (no new MSI in this release; it uses the same web code, so the next Windows build gets the new look).

## v1.0.0-beta71

Beta 71 (Android 86, MSI 0.71.0, FLACie Web 1.0-beta71). Still a beta, not a 1.0 final.

**Retro is now inside FLACie on Android, as on the web.** There is one Android app again: `FLACie-1.0-beta71.apk` is the FLACie app you already have, updated in place, with your library, accounts and settings kept. Retro (the iPods, the desk and the listening scenes) opens from a Retro button with a cassette in Now Playing, from Settings (a Retro row, and in Mode), and Back brings you back to FLACie. The native click-wheel iPod mode is still there, unchanged.
- Retro runs from the app's own files, offline: no FLACie Web server is needed.
- FLACie's own player plays the music, so everything carries on as before: the queue, the notification, headphones and lock-screen controls, playing in the background. Retro opens on the song FLACie is playing, picks up skips and pauses made anywhere else, and a song chosen on the iPod plays in FLACie.
- Retro shows your whole FLACie library (music on the phone, Jellyfin, Plex, the NAS) with its covers, and the desk's meters and earbuds follow the music without the microphone.
- The separate "FLACie with Retro" app from beta 70 is withdrawn. If you installed it, you can uninstall it: this app does everything it did.

**Fixed: tapping the click wheel on phones.** On some Android WebViews, taps on the ring (MENU, previous, next, play/pause) did nothing; they now work everywhere.

**Not synced yet between Retro and FLACie on Android**
- Shuffle and repeat set in Retro don't change FLACie's own shuffle and repeat (Retro simply follows whatever FLACie plays).
- Retro's level volume, crossfade and sleep-timer fade aren't used there: FLACie's own settings for those apply.
- Lyrics in Retro are fetched by Retro itself, not from FLACie's lyrics.
- When the queue has finished, Play in Retro does nothing until you pick a song (FLACie's player behaves the same way).

**Tested** (this build)
- Android release APK: versionCode 86, signed with the owner key (SHA-256 6a4eb335...97a106, the same as beta 70). On an Android 14 emulator: beta 70 installed and set up with music on the device, then beta 71 installed over it as an update: still signed in, library there. Retro opened from Settings; choosing Shuffle Songs on the iPod played in FLACie; a tap on the click wheel's play/pause paused FLACie's player.
- Debug build on the same emulator: Retro opens on the song FLACie plays, skips made with the media keys show on the iPod, the hi-fi scene's meters move with the music, the desk renders, Back returns to FLACie. Taps on all four ring zones (MENU, previous, next, play/pause) act once each.
- Android unit tests: 414 run, 0 failed. .NET tests: 711 pass. Browser tests for the iPod wheels, marquee and Retro touch (1q, 1s, 1v): 36 of 36 pass.
- The MSI (0.71.0) was built; not installed.

**Not tested**
- On a real phone. The emulator's software graphics are too slow for the desk (taps there arrive late and count as holds); with the emulator's host graphics the desk ran without trouble.
- Retro with a large library or a Jellyfin library on the phone (the emulator had three local songs, without covers).

## v1.0.0-beta70

Beta 70 (Android 85, new Android app "FLACie" with Retro 1.0-beta70, MSI 0.70.0, FLACie Web 1.0-beta70). Still a beta, not a 1.0 final.

**New: an Android app with the whole of FLACie and Retro inside.** A second Android download, `FLACie-Retro-1.0-beta70-arm64.apk`, is the full FLACie app (home, search, explore, favourites, playlists, the player, settings) running on the phone itself, with Retro (the iPods, the desk and the listening scenes) built in. It talks straight to your Jellyfin server, so it doesn't need a FLACie Web server. Music plays through Android's own player in the background, with the notification and headset controls, and the desk's meters follow the music without the microphone. It installs alongside the existing Android app (it shows as a second FLACie icon), so you can try it without losing anything. It's for 64-bit ARM phones (almost every phone from the last several years).

**Changed: iPod mode is now Retro** (web, Windows and the new Android app).
- It lives at /retro (/ipod still works) and opens from a Retro button in the player bar and the full-screen player, as well as the account menu. On Windows, the desktop app has a Retro button beside FLACie and iPod manager, and Retro runs from the app's own server, offline-safe.
- Retro opens straight on your saved iPod, look and scene, with no other iPod flashing first. iPod settings go with your account.

**Touch.** The desk and every scene (car, hi-fi, boombox, MiniDisc, Discman) work with a finger: knobs turn by dragging, buttons press, and nothing needs a mouse wheel or keyboard.

**iPods that look and read like the real ones.**
- Real fonts: Chicago on the 1st-4th generation and mini, the 5th generation, classic and colour nano fonts, and Helvetica and San Francisco stand-ins on the iPod touch and nano 6/7. Rows are sized to the font, so nothing overlaps.
- The iPod touch 1-7 each have their own system, from the glossy iPhone OS 1 to iOS 6's brushed metal and the flat iOS 8-12 Music app; the nano 6 and 7 have their square and flat systems.
- Real controls: the 1st/2nd generation ring of buttons, the 3rd generation's touch buttons, the mini 2nd generation's coloured lettering, flat click wheels, the nano 5th generation's taller screen, the nano 2nd generation colours and the red U2.
- Long titles scroll a whole pixel at a time, cut at the screen's edge, as on a real iPod.

**The desk.**
- The CDs are the queue (an acrylic stand for what comes next), with a Now Playing card and a preamp with VU, spectrum and 5-band EQ meters. One download, even with the EQ on.
- The notebook looks like real paper (cloth cover, sewn gutter, ribbon), its page turn folds over the spine, and the sticky note peels off with a curl. Handwriting sits on the rules.
- The iPod is a fifth bigger and sharp, and the desk takes its colour in a few quiet places.
- A compact desk for small windows and phones: the iPod as big as it can be, the Now Playing card and the lyrics on a sticky note.

**The scenes.** Every scene is under the desk's light and film, with its own mood and real surfaces. The car, hi-fi, boombox, MiniDisc and the new Discman (with Mega Bass and a wired remote) show the file's format and the line being sung, and have working EQ, tone and volume controls.

**Faster.** The desk and the scenes use about half the graphics power they did. Scripts load under versioned names, so an update never runs with an old cached copy.

**Tested** (this build)
- .NET tests: 711 pass. Android unit tests: 414 run, 2 skipped (as before), 0 failed.
- Browser tests: all 30 spec files were run in groups after merging the Android app's work in. Everything passed except: two Retro settings checks that still expected all eight Settings rows (and seven nanos) on a 4th generation screen, which now shows six rows like the real one in the Chicago font; the test was corrected and the whole iPod spec passes (13 of 13). Two download-tag tests failed only on reruns against a server already holding downloads from the earlier runs, and passed in the first clean run; one MusicBrainz and one catalog test failed because the local test server was started pointed at the wrong stand-in port, and passed once that was fixed.
- Kotlin app: release APK versionCode 85, signed with the owner key (SHA-256 6a4eb335...97a106). Installed on an emulator and opened beside the new app; no crash. Its code hasn't changed since beta 69, and the full 10,000-song emulator run was not repeated.
- New Android app: release APK (64-bit ARM, trimmed, 24 MB), signed with the same owner key, versionCode 70. On an Android 14 emulator: signed in to the test server, played a song (Android's player reported playing, then moved on to the next ones), and Retro opened from the player and showed the song on the iPod.
- The MSI (0.70.0) was built and its app started from the build folder: the server answered /healthz "ok", /retro answered, and the server stopped when the app closed. Not installed.

**Not tested**
- Either Android app on a real phone; the new app's lock-screen cover art against a real Jellyfin server (on the test server the cover is refused).
- The new Android app has no crossfade yet, and the Info tab's live graphs are off in it. Long titles wrap on its phone Now Playing screen.
- **Not built:** Chromecast (docs/spec/chromecast.md).

## v1.0.0-beta69

Beta 69 (Android 84, MSI 0.69.0, FLACie Web 1.0-beta69). Still a beta, not a 1.0 final.

**New (web and Windows): listening scenes.** In iPod mode, the panel's new Scene row swaps the iPod for another way to listen. Every scene shows the song playing and drives the real player: play/pause, next, previous, and the media lying about play an album coming up or one of your playlists. Switching scenes never stops the music, and the panel's "How to use" explains the scene on show.
- **Car:** a night drive behind a rain-streaked windshield with a sweeping wiper. A CD head unit with an orange dot-matrix display that tunes to a station on each new song, a CD slot (eject slides the disc out), a volume knob (scroll or arrow keys; press to mute), six presets (your playlists, then the albums coming up) and climate dials.
- **Hi-fi:** a 1970s living room. The record spins at the speed the pitch slider sets, and the tonearm follows the song. Click the record to drop the needle further in, with a quiet crackle on each new song. Flip through a crate of sleeves. A silver receiver has green meters on the music and a tuning knob that seeks.
- **Boombox:** a workbench and a dual-cassette boombox with stickers. Deck A's reels turn and its counter runs. The woofers and EQ sliders move with the music. Stop/eject drops the door open, and Deck B holds what comes next.
- **MiniDisc:** a 2000s desk and a brushed-aluminium micro system. Eject pops the disc up out of the top-loading slot. A blue backlit LCD scrolls the title, and the jog dial skips (scroll or arrow keys) and plays or pauses from its centre. A fan of MiniDiscs on the desk holds the albums coming up and your playlists.
- The scenes fit desktops, laptops with the panel open, tablets and phones, and hold still with reduced motion.

**Changed: the iPod desk.**
- **Desk options** in the panel: earbuds or studio headphones (coiled cable and adapter), a VU meter, a cassette, MiniDiscs, a 30-pin dock and a pencil, each on or off.
- **A tidier desk:** the iPod sits in the middle, the notebook and VU meter on the left, the CDs, cassette and MiniDiscs on the right, and the coffee in the corner. The cables never lie across anything.
- **The notebook** has two pages that turn when they fill up. On a phone it's a pad of sticky notes, and a full one peels off.
- **Lyrics** are timed in the browser, so they're no longer late. A long line wraps and is written in row by row.
- **The open CD** is the album playing.
- **A queue card** shows the song playing with its year, then what comes next.
- **The earbuds** react more strongly, and their cable runs smoothly. The coffee ripples on the bass, read from the same stream as the earbuds, so nothing is downloaded again.
- **iPod mode** opens on Now Playing when a song is playing.
- **iPod screens:** long titles scroll across, as on a real iPod, instead of being cut off. With reduced motion they end in "…".

**Tested** (this build)
- Android unit tests: 414 run, 2 skipped (as before), 0 failed (no Android code changed). .NET tests: 686 pass (one timing test, "ten thousand songs build fast", failed once while the Android build ran alongside it and passed alone and in the full rerun). Browser tests: the full suite, 219 of 220 passed on the branch. The one failure was the new scenes click test, which was too strict about a fanned stack of five MiniDiscs. It was corrected and passes with five discs on the desk. After the merge and the version bump: the About-version, iPod and scenes specs (1d-g2, 1m-ipod, 1r-retro-scenes), 55 of 55.
- Every scene and the desk were checked at 1600x900, 1024x768 and 390x844 in Chromium: no console errors, no sideways scrolling, and play/pause works. A new test presses every control in every scene at four sizes and checks that the press lands on it. It found, and now guards, the car's climate dials covering its keys and the hi-fi's receiver covering the turntable at tablet sizes.
- Release APK versionCode 84, signed with the owner key (SHA-256 6a4eb335...97a106). Installed on a clean emulator and run against the 10,000-song mock: 23 of 23 checks passed (Quick Connect sign-in, 10,000 songs after 42 s, scrolling, play, a 40-skip burst, pause, kill and relaunch with the queue restored and not auto-playing, Settings > Storage and Clear cache, Up next Play next / Move up / Move down / Remove, no crash, no ANR).
- The MSI (0.69.0) was built and its app started from the build folder: the server answered /healthz "ok" and stopped when the app closed. Not installed.
- FLACie Web was deployed to the server (rollback tag first); /healthz "ok", and it serves the scene files with the final fixes.

**Not tested**
- The scenes and the desk on a real phone or tablet (checked at phone, tablet and laptop sizes in Chromium), and with real music (the test server's songs are 3 seconds of silence, so the meters, woofers and coffee were checked against a synthetic signal).
- **Not built:** Chromecast (docs/spec/chromecast.md).

## v1.0.0-beta68

Beta 68 (Android 83, MSI 0.68.0, FLACie Web 1.0-beta68). Still a beta, not a 1.0 final.

**New (web and Windows): the iPod desk.** In iPod mode, Look › Desk lays the iPod emulator on a sunlit oak desk, seen from above in a warm film-photo look. It works with all 23 models and their colours, and every button and the wheel work as on the stage.
- **Journal:** the playing song's lyrics are written in by hand as they're sung.
- **CDs:** the next album lies open. Its disc carries the release's own disc picture when you matched it on MusicBrainz, otherwise a print made from the cover. The two albums after it sit in a stack, with an "Up next" note. Click a CD, or Tab to it and press Enter, to play that album; while shuffling it plays the whole album, then the shuffle carries on.
- **Earbuds:** they're plugged into the iPod and act as the visualiser, with sound arcs, a ring on each beat, and current running down the cable.
- **Around the iPod:** a Polaroid of the cover, a coffee, blinds light and dust in the sun.
- **Phones:** the frame is a closer look at the same desk, so everything keeps its size next to the iPod and runs off the edges like a photo.
- **Reduced motion:** with the system setting on, the desk holds still.

**New: no song is downloaded twice on mobile data.** The Info tab's live graphs and the desk's earbuds read a silent second copy of the song. On a phone on mobile data, or with Data Saver on, the desk keeps a calm pulse instead, and the live graphs wait for a "Show anyway" button. On Wi-Fi and on a computer nothing changes. The second copy also stops while the tab is in the background (it used to keep downloading).

**Changed (tests):** every browser test now honours the test-server port settings, so two checkouts can run the whole suite at the same time on different ports.

**Tested** (this build)
- Android unit tests: 414 run, 2 skipped (as before), 0 failed (no Android code changed). .NET tests: 663 pass. Browser tests: the full suite, 185 passed, 0 failed, on the branch with the same code; after the version bump the About-version spec (1d-g2) passed again, 22/22. During the earlier full runs one load test ("skipping 20 songs opens almost no streams") opened 11 streams against a limit of 10 while another session was building on this machine; it passed in the next two full runs.
- Release APK versionCode 83, signed with the owner key (SHA-256 6a4eb335...97a106); installed on a clean emulator and run against the 10,000-song mock: 23 of 23 checks passed (Quick Connect sign-in, 10,000 songs after 38 s, scrolling, play, a 40-skip burst, pause, kill and relaunch with the queue restored and not auto-playing, Settings > Storage and Clear cache, Up next Play next / Move up / Move down / Remove, no crash, no ANR).
- The MSI (0.68.0) was built and its app started from the build folder: the server answered /healthz "ok" and stopped when the app closed. Not installed.
- FLACie Web was deployed to the server (rollback tag first); /healthz "ok", and it serves the desk files.

**Not tested**
- The desk on a real phone or tablet (checked at phone, tablet and laptop sizes in Chromium), real MusicBrainz disc pictures (the test server has none), real lyrics from LRCLIB (the tests use the mock server's lyrics), and the mobile-data switch on a real phone (tested by emulating Chrome's connection report).
- **Not built:** Chromecast (docs/spec/chromecast.md).

## v1.0.0-beta67

Beta 67 (Android 82, MSI 0.67.0, FLACie Web 1.0-beta67). Still a beta, not a 1.0 final.

**Fixed (web and Windows): dragging a touch iPod's list with the mouse did nothing.** Beta 66 said "a mouse can drag the lists like a finger", and that was not true: a rename I made in the page script also changed the name of the list class it looks for, so the drag never found the list. It scrolls now, and a drag is not taken for a tap on the row under it. A browser test drags the Songs list of three touch iPods and fails with the old script (checked). Found by another Claude session while it was working on the iPod desk look.

**Also in this build (not mine):** the Alexa work that another session merged to main on 2026-10-14 (the "ask FLACie" custom skill: pairing, voice matcher, signed stream links, Echo in the device picker; **off until switched on in Settings > Alexa**). It was already deployed to the live server before this release; see `docs/alexa/STATUS.md` for what it was and was not tested against (no real Echo, no real Amazon signatures). Nothing in this release turns it on.

**Changed (tooling):** `ipodsync/tools/stop-test-servers.ps1` stops the test servers by the port they listen on (5290, 8196, 8197), never by image name: stopping every `dotnet.exe` by name also stops another session's tests.

**Tested** (this build)
- Android unit tests: 414 run, 2 skipped (as before), 0 failed (no Android code changed). .NET tests: 636 pass (about 300 of them are the Alexa tests). Browser tests: one full run, 153 passed, 3 failed and 2 did not run; the three that failed (the 320 px bottom bar, "skipping 20 songs opens almost no streams", and "Autoplay lists ... Downloading") passed when run again, alone and together, while another session was running its own tests on this machine at the same time and had stopped my servers once, so I treat them as load-related and not as bugs; the Autoplay one has now failed in several full runs for no cause I could find, so it is the one to watch.
- Release APK versionCode 82 signed with the owner key (SHA-256 6a4eb335...97a106); installed on a clean emulator and run against the 10,000-song mock: first sync 10,003 songs (first songs after 14 s), scrolling 84 swipes with 471 requests, a skip-burst of 40 in 2.5 s with the next song ready after 4.6 s and 64 requests, warm start 9.8 s, kill and restore: queue restored, 0 crash lines (a first try on another mock port failed to sign in; that was the port, not the app).
- The MSI (0.67.0) was built, not installed. FLACie Web was deployed.

**Not tested**
- Finger feel on a touch screen; dragging was tested with a mouse. Windows was not run in the installed app. Alexa: see its own status file.
- **Not built:** Chromecast (docs/spec/chromecast.md).

## v1.0.0-beta66

Beta 66 (Android 81, MSI 0.66.0, FLACie Web 1.0-beta66). Still a beta, not a 1.0 final.

**Changed (web and Windows): the iPod mode is now an iPod OS, not a Now Playing in a shell.**
- **The touch iPods have their own system.** iPod touch 1 to 7 and nano 6 and 7 used to show the FLACie website in their glass. They now run an iPod-style OS: a home screen with app icons and a dock, the **Music** app (bottom tab bar: Playlists, Artists, Songs, Albums, More; navigation bar with a back button; lists with covers; long lists load as you scroll), **Now Playing** (big cover, stars, scrubber, previous / play-pause / next, volume), **Settings** (the playback options as a grouped table, about), **Clock** and **Stopwatch**. The Home button of the device goes home; Esc goes back. A mouse can drag the lists like a finger.
- **The wheel iPods got the iPod OS extras.** Extras now holds a **Clock**, a **Stopwatch** (middle button starts and stops, the previous button resets) and Voice Memos; Now Playing has a third wheel mode, the **star rating** (middle button: volume, scrub, stars; the wheel sets the stars, kept in your profile and written to Jellyfin). The nano/classic main menu has Extras like the others (Voice Memos moved into it).
- **The screens can no longer be disturbed by the site's styles** (their text, rows and buttons are reset to what an iPod screen needs), the status-bar title is only as wide as its text, and the tall nano's Now Playing was redone (title, one line for artist and album, the bar below; the count sits in the title bar). The mono iPods no longer show cover thumbnails in lists (they never had them). A touch screen that is short (nano 6) keeps the cover, title, bar and buttons on Now Playing.
- **Pressing Esc only goes back** (it used to close the side panel first).

**Fixed (web): typing in the search boxes lost and swapped letters.** The top search box, the Explore search and the Downloads filter sent every key to the server and the server wrote the text back; when typing was quicker than the answer, an older answer went over newer letters. The text now stays in the browser, the browser waits for a short pause and tells the server once, and the server never writes into a box you are typing in (it only fills it when it is not focused: a cleared search, a shared link). A new test types uneven, fast text into all three boxes and checks that the text only ever grows by the letters typed and is intact after the searches come back. **That test cannot slow the server down, so it proves the box keeps its text and the guard works, not that your connection no longer causes it: please type in the search box and tell me.**

**Tested** (this build; this is where the "works at every size" claim comes from)
- A browser test opens **9 models** (the 1st gen with its buttons, a mini, the iPod video, nano 2, classic 6, the tall nano 4, nano 6, touch 5, nano 7) at **11 window shapes** (1920x1080, 1366x768, 1024x768, 768x1024, 390x844, 360x640, 320x568, 2560x1080, 1200x400, 900x300, 500x500) and checks that the device lies inside the free area, that **every piece of text lies inside the screen**, that **no two pieces of text overlap**, and that nothing scrolls sideways; at three of the sizes it does the same with a song playing and on the Settings pages. It also runs the wheel-only look for three models at all 11 shapes. This test found and made me fix: the tall nano's album line over its bar, the nano 6's Now Playing running off the glass, a screen scale that was up to a pixel off at a big zoom, and the title bar's boxes overlapping.
- Android unit tests: 414 run, 2 skipped (as before), 0 failed. .NET tests: 334 pass (new: the library menus and what a press on a row opens or plays). Browser tests: 158 pass, 0 fail in the last full run. **One honest note:** the test "Autoplay lists ... Downloading" (download tags) failed in two earlier full runs of this build and passed alone and in the last full run; I did not find a cause (it is a long, timing-based test), so treat it as intermittent.
- Release APK versionCode 81 signed with the owner key (SHA-256 6a4eb335...97a106); installed on a clean emulator and run against the 10,000-song mock: first sync 10,003 songs (first songs after 16 s), scrolling 121 swipes with 229 requests, a skip-burst of 40 in 2.6 s with the next song ready after 6 s and 71 requests, warm start 10.1 s, kill and restore: queue restored, 0 crash lines (no Android code changed since beta 65).
- The MSI (0.66.0) was built, not installed. FLACie Web was deployed.

**Not tested**
- Finger feel on a touch screen (a mouse and a keyboard in a browser were used), the clicker sound and haptics on a phone, and a real iPod's pixel-for-pixel look: the screens follow the Android emulator's, not a real device. Windows was not run in the installed app.
- **Not built:** Chromecast (docs/spec/chromecast.md); Games and Contacts/Calendar in Extras; on the touch OS, the Videos, Photos and App Store icons (there is nothing behind them in FLACie).

## v1.0.0-beta65

Beta 65 (Android 80, MSI 0.65.0, FLACie Web 1.0-beta65). Still a beta, not a 1.0 final.

**Fixed (web and Windows): the iPod mode's cover art was missing everywhere.** The covers asked for a picture called "ArtKey" instead of the song's own, so every cover stayed the grey note. They now show in the lists, the preview pane, Now Playing and Cover Flow, and a test fails if any cover asks for something that is not a real picture.

**Changed (web and Windows): the iPod mode is easier to use and fits the window.**
- **It always fits.** The device is scaled to the free space beside the panel and re-fitted whenever the window, the model or the look changes. Before, changing "Wheel only" and back to "Device" lost the fit and the iPod shrank to a small fixed size. A test now checks that both looks fill the height and that going back gives exactly the first size.
- **A panel beside the device** (it opens by default on a wide window, behind the sliders button on a narrow one): what is playing with its **big cover**, previous / play-pause / next, the model picker by group (Classic, mini, nano, touch) with year, the colours as swatches, Look (Device / Wheel only), and a short "how to use". Picking something in the panel starts the iPod again at its main menu.
- **Easier controls**: a click anywhere on the iPod area gives it the keys; tooltips on the ring ("Menu (Esc)", "Next (right arrow)", ...); the mouse wheel and trackpads scroll one row per notch (they used to fly through the list); the panel's buttons never take the keys or act twice.
- The Now Playing screens of the nano and classic models were drawn too low (the time was cut off); fixed.

**New (Android): Cover Flow on the nano and classic 6/7 screens too** (it was only in the Classic menus): "Cover Flow" in their main menu, the same row of covers in perspective.

**Tested** (this build)
- Android unit tests: 414 run, 2 skipped (as before), 0 failed. .NET tests: 329 pass. Browser tests: 153 pass, 0 fail (one full run, nothing else running); the iPod spec now has 10 tests: the 8 of beta 64 plus one for the covers (every cover asks for a real picture and gets one: the lists, the pane, Now Playing, Cover Flow and the panel) and one for the fit (both looks fill the height, going back gives the first size, the panel is open on a wide window and closed on a narrow one, the keys survive a click on the panel).
- Release APK versionCode 80 signed with the owner key (SHA-256 6a4eb335...97a106); installed on a clean emulator and run against the 10,000-song mock: first sync 10,003 songs (first songs after 13 s), scrolling 103 swipes with 611 requests, a skip-burst of 40 in 2.6 s with the next song ready after 5 s and 66 requests, warm start 10.4 s, kill and restore: queue restored, 0 crash lines.
- On the emulator: the nano 3rd gen main menu shows Cover Flow and opens it without a crash (the emulator had no library, so no covers were shown there; the same drawing code showed covers on the Classic in beta 63).
- The MSI (0.65.0) was built, not installed. FLACie Web was deployed.

**Not tested**
- Finger feel on a touch screen, the clicker sound and haptics, and the look on the owner's own screen: only a browser at 1366x768 and phone-size windows were used. Windows was not run in the installed app.
- **Not built:** Chromecast (a decision for the owner, docs/spec/chromecast.md); a title cross-fade and a bitmap-font LCD on Android; Cover Flow on the mono web screens (the real mono iPods had none).

## v1.0.0-beta64

Beta 64 (Android 79, MSI 0.64.0, FLACie Web 1.0-beta64). Still a beta, not a 1.0 final.

**New: the iPod mode on the web and in the Windows app now has everything the Android iPod emulator has**
- **All 23 iPods**, in the same four groups (iPod / classic: 1st gen to classic 7th; mini 1st and 2nd; nano 1st to 7th; touch 1st to 7th), each with its real proportions and **every colour** (nano 4th/5th gen: nine; mini: five; and so on). Pick them on the device itself (Settings > Appearance > Model, Colour, Look, Display) or with the small sliders button at the top right of the page (also the only way back from a touch model).
- **Three screens, drawn at the real device's size** (160x128 mono, 320x240, 240x320 for the tall nanos) and scaled into the bezel: the **mono** LCD of the 1st to 4th generations and the minis (green-grey, black selection bar, no preview), the **colour video** menus (iPod video, nano 1st/2nd: white list, blue bar, cover and details on the right for songs, albums and artists), and the **nano / classic** menus (a preview tile beside the main menu, the cover pane, a dark screen for the black models, big rows on the tall nanos).
- **Cover Flow** (in real 3D with reflections), **Genres**, **Playlists**, **Artists** (with an "All Songs" row and the albums when there are several), **Albums**, **Songs** (Shuffle on top), **Shuffle Songs**, **Now Playing** (the middle button switches the wheel between volume and scrubbing; the time bar, scrub knob and volume blocks as on the device).
- **Wheel**: drag round the ring, tap the zones (MENU, previous, next, play/pause, the middle), hold next/previous to seek, hold MENU to go home, a flick that coasts, a speed-up on long lists; mouse wheel and keys too. The 1st and 2nd generations have their four separate buttons.
- **Settings** as on Android: Playback (shuffle, repeat, speed, even out volume, fade, volume limit, sleep timer), Controls (Wheel Speed, Wheel Accel, Clicker: sound and/or haptic, Click Volume), Main Menu (hide entries), About, Reset Settings; plus Account and FLACie Settings. All kept in your browser.
- **Touch models** (nano 6/7, iPod touch 1 to 7) show the FLACie player in the screen of the device.
- The Windows app has it through the same page.

**Fixed (web, found while building this):** after a mouse click on the wheel's middle button, Enter also clicked it (and Space, the arrows and the page's own shortcuts acted twice); the iPod page's styles no longer clash with the player's (they shared a few class names).

**Tested** (this build)
- Android unit tests: 414 run, 2 skipped (as before), 0 failed (no Android code changed since beta 63; only the version is bumped). .NET tests: 329 pass (new: all 23 models and colours, settings, wheel feel, menus per family). Browser tests: 151 pass, 0 fail (one full run, nothing else running); the iPod spec now has 8 tests: every one of the 23 models loads and its picker lists the right number of colours; each family's main menu and preview; songs, the art pane, Now Playing with volume and scrub; Cover Flow; settings (model, colour, look, controls, main menu, reset, kept after a reload); the wheel (drag, taps, speed, a coasting flick, holding MENU); the picker panel and touch models; small windows. Screenshots in `docs/img/ipod-web-*.png`.
- Release APK versionCode 79 built and signed with the owner key (SHA-256 6a4eb335...97a106). Installed on a clean emulator and run against the 10,000-song mock: first sync 10,003 songs (first songs after 15 s), scrolling 128 swipes with 452 requests, a skip-burst of 40 in 2.8 s with the next song ready after 5.7 s and 69 requests, warm start 9.9 s, kill and restore: queue restored, 0 crash lines.
- The MSI (0.64.0) was built, not installed. FLACie Web was deployed.

**Not tested**
- The wheel on a real touch screen, the clicker's sound and the haptics on a phone, and how Cover Flow feels: only a mouse in a browser was used. Windows was not run in the installed app.
- **Not on the web (they are phone things):** Voice Memos (the row is there, "No Memos"), the Android equalizer, Face Buttons (game pad), Rescan Library and the Sync / Jellyfin / Plex / NAS / Lidarr setup rows (use FLACie Settings and Account).
- Chromecast is still not built (docs/spec/chromecast.md).

## v1.0.0-beta63

Beta 63 (Android 78, MSI 0.63.0, FLACie Web 1.0-beta63). Still a beta, not a 1.0 final.

**New: star ratings (Android and web)**
- A star button beside the heart: tap it for five to one stars, or clear. Ratings are kept in your FLACie profile (so every device sees them, and the newest choice wins when two devices disagree) and written to Jellyfin as the song's own rating (stars x 2, 0 to 10). Songs from other sources keep the rating in the profile only.
- Android: also "Rate..." in the song menu.

**New: the queue in the system media card, Bluetooth and Android Auto (Android; not tried in a car)**
- The lock screen, the quick-settings card and Bluetooth now see a window of your queue (the song that plays, 20 before and 80 after, in play order, shuffle included) instead of the one song, and "next", "previous" and picking a song from that list go through the same player as the app (skip bursts and history behave as in the app). 10,000 songs are never handed over at once.

**New: the cover flies from the mini player into Now Playing (Android)**
- Opening the full player moves the small cover up into the big one instead of both just sliding.

**New: iPod mode on the web and in the Windows app**
- Account menu > "iPod mode" (`/ipod`): an iPod on the page. Drag around the wheel (or use the mouse wheel / arrow keys), press the middle to select, MENU to go back, previous/next/play on the wheel. Music > Artists, Albums, Songs, Favorites; Playlists; Shuffle Songs; Now Playing (the wheel is the volume there). The list slides in and out, with a scrollbar like the hardware. White model, `?c=black` for the black one. It fits any window size. Windows has it too (the app shows the same page).

**Fixed (web):** choosing a cover from the Cover Art Archive (or going back to the original) did not show until the next time the player redrew, which never happened while paused; it now shows at once. (Found because a test paused the song.)

**Changed: iPod emulator on Android**
- Classic: **Cover Flow** (a row of album covers in perspective with reflections, turned with the wheel; the middle one opens), a **scrollbar** in lists, a **scrub diamond** on Now Playing, and a shadow under the screen bezel.

**Not built, on purpose: Chromecast.** A Cast receiver cannot send the Jellyfin `Authorization` header, so casting would mean putting your session token in a URL handed to another device. That is your decision (a revocable cast-only key, or a relay in the server): see `docs/spec/chromecast.md`.

**Tested** (this build)
- Android unit tests: 414 run, 2 skipped (as before), 0 failed. .NET tests: 311 pass (new: iPod menu and wheel rules, ratings). Browser tests: 145 pass, 0 fail (one full run, nothing else running); new: iPod mode (menus, wheel drag, keys, playback, small window, black model), ratings. Two tests were made more robust (the MusicBrainz tests no longer see answers an earlier run cached; the About version moved to beta63).
- Release APK versionCode 78 signed with the owner's key (SHA-256 6a4eb335...97a106); installed on a clean emulator and run against the 10,000-song mock: first sync 10,003 songs (first songs after 16 s), scrolling 146 swipes with 880 requests, skip-burst of 40 in 3 s with the next song ready after 5 s and 66 requests, warm start 9.6 s, kill and restore: queue restored, 0 crash lines.
- On the emulator: the session queue window through a separate test app (a different Linux user, as a car would be), the shared cover transition (mid-animation frame at 10x slow motion), Cover Flow, the scrollbar and the scrub diamond (screenshots in `docs/img`).
- The MSI (0.63.0) was built, not installed.

**Not tested**
- Nothing on a phone or in a car; nothing with a real Jellyfin write of a rating (tested against the mock, which records the call). The cover transition and Cover Flow were looked at in still frames on a slow emulator: smoothness, wheel feel and haptics are yours to judge.
- iPod mode on Windows was not run in the installed app (the MSI embeds the same page, tested in the browser).
- **Not built (decisions, not oversights):** Chromecast (above); the iPod LCD at an integer-scaled logical resolution with a bitmap font (the Classic already draws at its logical size; no licensed font was added) and a title cross-fade; Cover Flow on the other iPod themes.

## v1.0.0-beta62

Beta 62 (Android 77, MSI 0.62.0, FLACie Web 1.0-beta62). Still a beta, not a 1.0 final.

**New: Android Auto and car/Bluetooth browsing (Android; not tried in a car)**
- FLACie is now a media library as well as a player: a head unit, Android Auto or a voice command can browse **Recently played, Favourites, Playlists, Albums, Artists and Downloaded** (a few songs at a time), search, and play from there; playing a song queues the list it came from. Covers reach them through a read-only content provider; the app is registered with Android Auto as a media app.
- **Tested in-app, not in a car:** a built-in probe (`am start --ez browseprobe true`) connects to the service the way Android Auto does and was run on the emulator: the root and its six folders, 10 albums and artists with cover addresses, search ("song 1" found 10), and playing a song from an album by its browse id (the song started, the queue was built). **Not tested with the Desktop Head Unit or a real car**: it needs the Android Auto app, which the emulator image does not have. Voice commands ("play X") are handled in code and unit-tested through the tree, not tried by voice.
- The system media card, lock screen and Bluetooth still show the current song only (not the whole queue), as in beta 59.

**New: MusicBrainz match and covers from the Cover Art Archive (Android too; the cover choice on the web)**
- **Android:** song menu > "MusicBrainz match...": a list of recordings with a percentage; pick one and the menu offers "Use the cover from the Cover Art Archive", "Use the original cover" and "Forget this match". Same gate, cache and User-Agent as the web (Settings has no contact field yet; the project page is used).
- **Web:** Info tab > MusicBrainz > "Choose a cover...": the pictures the Cover Art Archive holds for the matched release; "Use this cover" replaces the cover shown for that song and its album in FLACie only, "Use the original cover" puts it back. A replacement gets a new key (browsers keep the old picture for good), the original and the song files and Jellyfin are never touched.
- On Android a replaced cover shows from the next screen you open (the lists are not redrawn at once).

**New: fade between songs (Android and web)**
- Settings > Fade between songs (Android), Fade (web full-screen player): the end of a song fades out and the next song fades in over 2 to 12 seconds. It is a fade, not an overlap (a true crossfade needs two players at once). The tracks of one album that follow each other are not faded, so gapless albums stay gapless. Off by default.

**Changed (web): the speed, sleep timer, volume and fade controls are redesigned.** They were a row of labelled form boxes; they are now three small icon buttons in the same row as the heart and More (Speed, Sleep timer, Sound), each opening a short menu (speeds as a grid; Sound holds "Even out volume" and the fade choices). The icon turns accent-coloured when the setting is not at its default; the tooltip says the value. Skip back 10 / forward 30 are small buttons in that row when they apply.

**Small:** shuffle and repeat show a dot under the icon when on (not colour alone).

**Tested** (this build)
- Android unit tests: 404 run, 2 skipped (as before), 0 failed (about 40 new: browse tree, MusicBrainz and Cover Art Archive, fade rules). .NET tests: 299 pass. Browser tests: 142 pass (new: MusicBrainz match, cover choice, icon controls, volume levelling, fade).
- Release APK versionCode 77, signed with the owner's key (SHA-256 6a4eb335...97a106); installed over a clean emulator, signed in to the mock with Quick Connect, played, skipped, killed and restarted (queue restored), 0 crash lines, covers 100 % by 3 s in two of three runs and by 10 s in all.
- On the emulator: the Android Auto library service through the in-app probe (see above), and the MusicBrainz sheet against the real MusicBrainz ("Charly" by The Prodigy: 7 ranked candidates with percentages; it was a single lookup).
- The MSI (0.62.0) was built, not installed. FLACie Web was deployed.

**Not tested**
- Nothing on a phone or in a car. The fade was tested by its rules (unit tests) and on the web in the browser; not by ear on Android. The Cover Art Archive and MusicBrainz were tested against stand-ins (and the one real call is yours to try).
- **Not built (decisions, not oversights):** Chromecast (needs a Cast device to check and a change to how the player is swapped; the theme the Cast button needs is not the app's), ratings (stars; Jellyfin's rating write is unchecked), the shared-element cover transition, the iPod LCD renderer / Cover Flow, and a queue window for the system media session (browsing covers the head-unit case).

## v1.0.0-beta61

Beta 61 (Android 76, MSI 0.61.0, FLACie Web 1.0-beta61). Still a beta, not a 1.0 final.

**New: even out volume (Android and web)**
- Jellyfin measures each song's loudness when it scans the library and gives the gain that brings it to a common level. With **Settings > Even out volume** (Android) or **Volume > Even out** in the full-screen player (web), loud songs are turned down (up to 15 dB) and quiet ones up (up to 6 dB, never past full volume). Off by default. If your Jellyfin gave no gain for a song, it plays as it is.
- Not checked against your real Jellyfin: whether it actually sends `NormalizationGain` for your songs is the first thing to look at (turn it on and see if loud and quiet songs even out).

**New: MusicBrainz match (web)**
- Info tab > MusicBrainz > **Find on MusicBrainz** lists the recordings MusicBrainz knows for the song, best fit first with a percentage; **Use this match** keeps your choice beside the library and links to it. Nothing is asked until you press the button (not at launch, not when a song or the Info tab opens). MusicBrainz is asked at most once a second on its own gate, with a User-Agent that names FLACie and its GitHub page (`FLACIE_MB_CONTACT` changes the contact), and answers are kept on disk (30 days; "nothing found" 7 days).
- It never changes your files, your tags or Jellyfin. Not built yet: the cover picker (Cover Art Archive), the same on Android.

**Tested** (this build)
- Android unit tests: 372 run, 2 skipped (as before), 0 failed. .NET tests: 289 pass (19 new: MusicBrainz client/gate/cache/matching, loudness). Browser tests: 138 pass (2 new: volume levelling, MusicBrainz match).
- Release APK versionCode 76, signed with the owner's key (SHA-256 6a4eb335...97a106); installed over a clean emulator, signed in to the mock with Quick Connect, played, skipped, killed and restarted (queue restored), 0 crash lines. Covers: 100 % of visible rows by 10 s in all three runs; by 3 s in two of three (the emulator started right after the MSI build and was slow in one run).
- The MusicBrainz test uses a stand-in for MusicBrainz and checks: no request before the button, the User-Agent text, the query, the order of results, that the choice survives a reload, and that asking again is answered from the cache.
- The MSI (0.61.0) was built, not installed. FLACie Web was deployed (see the notes on the release).

**Not tested**
- Nothing on a phone. The loudness gain was only tested with the mock (a gain of -6 dB lowers the audio element's volume by half) and unit tests; MusicBrainz was tested against a stand-in, not the real service.
- Everything in beta 60's "Not tested" still applies. The MSI is built, not installed.

## v1.0.0-beta60

Beta 60 (Android 75, MSI 0.60.0, FLACie Web 1.0-beta60). Still a beta, not a 1.0 final. This is the first build of the "close the gaps" programme: speed, a better sleep timer, offline downloads, and the first part of the player redesign.

**New: playback speed (Android and web)**
- 0.5x to 3x with the pitch kept. The speed is remembered per item, and on spoken word (audiobook, podcast, speech, lecture ... genres) the speed you used last is the default for the next one. Items of 20 minutes or more remember where you stopped and carry on from there (Android and web).
- Now Playing shows a Speed pill; for spoken word, long items and any changed speed there are also "Back 10 s" and "Forward 30 s" buttons. The notification, lock screen and Bluetooth/Auto controls get a speed button (taps cycle 1x, 1.25x ... 3x, 1x) and a favourite button whose heart follows the song (Android).

**New: sleep timer (Android and web)**
- Besides minutes: "End of this song" and "End of this album". The volume fades out over the last 30 seconds, then playback pauses. In the last two minutes you can add 15 minutes from the sheet ("Add 15 minutes"), or on Android by shaking the phone.
- The web had no sleep timer; it now has the same choices in the full-screen player.

**New: offline downloads (Android only)**
- Song menu > "Download for offline". Settings > Offline and storage shows what is on the phone, what is on its way, free space, and lets you remove one or all. Downloads are resumable (the bytes so far stay in a .part file and the next try continues from there), saved atomically, and go through the Jellyfin gate at background priority (playback and the screen go first). Wi-Fi only by default. "Keep favourites offline" (off by default, with a size limit, 5 GB) downloads your favourites and lets them go when you unfavourite them; songs you downloaded yourself are never removed by it.
- A downloaded song plays from its file: no network, no data. Apple Lossless songs that this phone cannot decode are still converted by the server (not played from the file).
- **Not on the web or Windows:** the web player is a Blazor Server page that cannot run without its server, so there is nothing for a browser to do offline; the Windows app already keeps a disk cache of played audio. This is platform-specific, not skipped.

**New: player redesign, first part (Android; web gets the bar)**
- Seek bar: 8 dp track that grows to 10 dp under the finger, a bigger knob, a lighter stretch for what is already loaded, a pill with the time under your finger, a haptic tick every tenth, the time left on the right (tap it for the total). Screen readers get "Seek, 1 minute 37 seconds of 5 minutes 2 seconds" and "Back 10 / Forward 30 seconds" actions. The play/pause button morphs and springs when pressed. The web's full-screen player gets the thicker bar and the spoken position.
- The spec for the rest (cover transition, colour clamps, bigger transport targets, ...) is `docs/spec/player-redesign.md`; those parts are not built.

**New: iPod wheel (Android)**
- After a fast flick the wheel coasts on and slows down (any touch stops it). The wheel's labels are kept readable on every colourway.

**Tested** (this build)
- Android unit tests: 367 run, 2 skipped (as before), 0 failed (about 65 new: speed and sleep rules, offline model, seek bar text and ticks, wheel coast, contrast). .NET tests: 270 pass (13 new). Browser tests: 136 pass (3 new: speed, sleep timer, end-of-song). The .NET and browser suites ran before the last Android-only fix below; nothing on the web or server changed after.
- Release APK versionCode 75, signed with the owner's key (SHA-256 6a4eb335...97a106); installed over a clean emulator, signed in to the mock with Quick Connect, played, skipped, killed and restarted, covers 100 % of visible rows at 3 s, 0 crash lines.
- Emulator, by screenshot and logs: the new seek bar mid-drag (pill 1:31, time left -2:22); speed 2x (the media session reports speed 2.0 and the position runs fast); notification with Favourite and Speed 2x buttons; "End of this song" pauses at the end; offline: a 120 s file downloaded exactly (1,920,044 bytes), a download cut by killing the server at 720,018 bytes resumed from there and finished, a failed one retried, and with the server down a freshly started app played the downloaded song.
- A bug found by the kill-and-restart scenario and fixed before release: while building this, the saved queue silently failed to come back after a restart (the player asked the offline library about songs before it existed). The start-up order is fixed and the scenario passes again.

**Not tested**
- No phone: nothing here was tried on the Fold (seek feel, haptics, the shake gesture, the wheel's coast, lock-screen and Bluetooth buttons, anything audible: fades, pitch at 3x). The emulator has no accelerometer shake and no Bluetooth.
- Downloads were tested on the emulator with the mock Jellyfin (a 120 s WAV; kill the server mid-download and bring it back, kill the server and play from the file). Not tried against the real Jellyfin, with very large files, or with the phone on mobile data.
- Resume of long items (20 min+) and the spoken-word default are unit-tested only.
- Everything in earlier "Not tested" lists still applies. The MSI is built, not installed. Android Auto and Chromecast are not built.

## v1.0.0-beta59

Beta 59 (Android 74, MSI 0.59.0, FLACie Web 1.0-beta59). Still a beta, not a 1.0 final. Android only: the web and Windows get the version bump.

**Fixed (Android): the media session no longer publishes the whole queue**
- The system media session (Bluetooth head units and earbuds, Android Auto, the lock screen and the quick-settings media card) was handed the entire play queue, up to 10,000 songs, and that list is what ran the phone out of memory over Bluetooth. The session now exposes only the current song (title, artist, artwork, position, duration) with play/pause, previous/next, seek, shuffle and repeat. Next and Previous still go through the same skip handling as the app's own buttons.
- Measured on the emulator with `dumpsys media_session`: queue size 2003 (before) -> 0 (after); playback state, actions (play, pause, skip both ways, seek) and metadata unchanged; the system media card shows title, artist, artwork colour, seek bar, previous/next/play; media Next/Previous/Pause keys work; no crash lines.
- This is a simpler form of the "windowed queue" in the plan (current +/- 50): controllers see one item rather than 101. A head unit that wants to browse the upcoming songs cannot; the in-app Up next is unchanged. The plan's custom actions (favourite, speed) and notification polish are NOT done.

**Tested** (this build)
- Android unit tests: 311 run, 2 skipped (as before), 0 failed (new: SessionWindowTest). .NET tests: 257 pass. Browser tests: 133 pass.
- Release APK versionCode 74, signed with the owner's key (SHA-256 6a4eb335...97a106); installed over a clean emulator, signed in to the mock with Quick Connect, played, skipped, killed and restarted (queue restored, 0 crash lines); skip burst on a 3,000-song mock opened 6 streams (2 aborted), 100 requests in the run (beta 58 run: 84; run-to-run noise on a software-rendered emulator, no per-song requests).
- Covers on the emulator after a fresh install: 100 % of visible rows at 3 s in two of three runs; in the first run 20 % at 3 s and 100 % at 10 s (cold emulator); beta 58 reached 100 % at 3 s in all three.
- The MSI (0.59.0) was built, not installed. FLACie Web was not redeployed (version string only).

**Not tested**
- **No Bluetooth device, car, Android Auto or lock screen on a real phone**: the actual out-of-memory case was not reproduced on the emulator (it has no Bluetooth head unit). Please try the 10,000-song queue over Bluetooth first (docs/BY-HAND-CHECKS.md).
- Everything in beta 57/58 "Not tested" still applies. The MSI was built, not installed. The RG Rotate was not used.

## v1.0.0-beta58

Beta 58 (Android 73, MSI 0.58.0, FLACie Web 1.0-beta58). Still a beta, not a 1.0 final. A small follow-up to beta 57: one visible bug fixed, the beta 57 fixes checked on the emulator, and regression tests added. No new features.

**Fixed (Android; web and Windows get the version bump only)**
- **Settings was unreadable in the "iPod Player" look** (light iPod skin): the labels were dark text on the dark cards. Settings now always uses its own dark palette for its text.

**Checked on the emulator (the beta 57 fixes; the owner has not yet judged these on the Fold)**
- A song the server has no picture for ("Out of Space" style, three added to the test mock) gets one catalog lookup per album and all songs of the album show the picture (list, mini player, Now Playing).
- Seek bar: while the finger is down the knob and the time follow it; after lifting, the knob stays and playback lands at the target (1:41 target, player reported 101.6 s).
- Skip (media Next key): the next song's cover is on screen within the first screenshot after the key, no placeholder.
- Request budgets on the 10,000-song mock (fast profile): first sync 24 item pages and 37 image requests; scrolling 580 image requests for 108 swipes with 3 repeats; 28 requests in the first 60 s of a warm start; covers 100 % of visible rows at 3 s; no crash lines in the crash buffer.

**Tests added**
- `Beta57RegressionTest` (7 tests): seek hold/scrub position rules (`SeekHold`), cover warm order and range (`WarmPlan`), catalog key rules (one key per album, album artist wins, title fallback, no artist = no key). The seek bar and the warm-up now call these pure functions; behaviour is unchanged.
- Test mock: three songs with no picture (`--nopic`), configurable song length (`--wavsec`); the Android driver can use a mock started by hand (`MOCK_EXTERNAL=1`).

**Docs**: player redesign spec and iPod mode redesign spec (nothing built yet), `docs/BY-HAND-CHECKS.md` (what only a phone/ears can judge), `docs/OVERNIGHT-LOG.md`.

**Not tested**
- Everything in beta 57's "Not tested" still applies: no Fold, no Bluetooth/lock screen, no sound. The graph (spectrograph) fix was not measured: the software-rendered emulator says nothing about smoothness, and no before/after frame statistics were taken.
- The Windows MSI was built, not installed. The RG Rotate was not used.

**Tested** (this build, 2026-10-13)
- Android unit tests: 309 run, 2 skipped (as before), 0 failed. .NET tests: 257 pass. Browser tests: 133 pass (a first run showed failures only because a leftover server from an interrupted run held the port; the clean rerun passed all 133).
- Release APK: versionCode 73, signed with the owner's key (certificate SHA-256 6a4eb335...97a106), installed over the emulator, signed in to the mock with Quick Connect, played, skipped, killed and restarted (queue restored, 0 crash lines), covers 100 % of visible rows at 3 s.
- The MSI (0.58.0) was built, **not installed** on this PC. FLACie Web was **not redeployed** (the server only changed its version string); the live site still reports beta 57.

## v1.0.0-beta57

Beta 57 (Android 72, MSI 0.57.0, FLACie Web 1.0-beta57). Still a beta, not a 1.0 final. A quick follow-up to beta 56 from a first session on the Fold.

**Fixed (Android; the web and Windows get the version bump only)**
- **A song with no cover.** Some songs have no picture on the Jellyfin server at all (not for the song, not for its album): a placeholder note was all they could show. They now get a cover from the public catalog (the same lookup the web already used), one lookup per album, so an album's songs share one download. Your saved library is listed once more in the background so existing songs pick this up.
- **Seek bar.** After you let go, the knob used to jump back to the old position for up to a quarter of a second before moving; now it stays where you let go until playback gets there, and the time under the bar follows your finger while you drag. (Measurements on the Fold showed no dropped frames while dragging; this fix targets the snap-back, the most likely cause of the "laggy" feel. Tell us if it still feels bad.)
- **Skipping.** The covers of the previous song and the next four are made ready in the background each time a song starts, so a skip shows the next cover at once instead of the placeholder for a moment.
- **Info tab graphs.** The sound analysis ran on the screen's main thread every frame (120 a second on the Fold) and could wait on the audio thread after each new song; it now runs on a background thread about 40 times a second, and the audio is only tapped while a graph is on screen (before, every sample of every song was converted even with the tab closed). The drawing is unchanged.

**Tested** (this build)
- Android unit tests: 302 run, 2 skipped (as before), 0 failed. .NET tests: 257 pass. Browser tests: 133 pass. The release APK is signed with the owner's key (versionCode 72).
- Checked against the live Jellyfin (read-only, anonymous image endpoint) that "Out of Space" (The Prodigy) has no picture for its album or any of its 15 songs.
- On the Fold: the seek-bar frame statistics (1 % janky frames while dragging, median 6-9 ms) and the missing cover reproduced by screenshot.

**Not tested**
- The three fixes above were built and unit-tested but **not verified on the phone by us**: the new cover for the no-picture song (a song already in the restored queue keeps its old data until it is played again), the seek bar feel, the skip, and the graph on a new song. The owner is the judge of feel.
- The graph fix is a likely cause found by reading the code; the stutter itself was never reproduced or measured.
- The Windows MSI was built, not installed. The RG Rotate was not used.
- Everything in beta 56's "Not tested" still applies.

## v1.0.0-beta56

Beta 56 (Android 71, MSI 0.56.0, FLACie Web 1.0-beta56). Still a beta, not a 1.0 final.

**Faster start, and covers that actually all arrive (Android; the wrong-cover fix is also on the web and in the Windows app)**

Measured on a Z Fold against the real library (full numbers: `docs/test-run/K-coldstart-covers.md`).
- **Home is on screen about 70 ms after the app starts** (median of 5 launches; beta 55: about 900 ms, and 1.1 s with an empty cover cache). Home is drawn from a small saved copy of the shelves as they were last time; the library then loads behind it (about 0.6 s instead of 1.1 s).
- **Covers:** with an empty cover cache, 14 of 15 visible covers are there after 1 second in every run (beta 55: none after 1 s, and two of five runs never got past 73 % after 10 s). The 15th is an album Jellyfin really has no picture for.
- **Why covers went missing** (all fixed): rows sharing one album cover raced on one temporary file and deleted each other's finished picture; one failed answer left a cover empty for three minutes; slow first-time pictures made the request limiter shrink to one at a time; the sign-in sync queued ahead of the covers. A cover that fails is now asked for again with a growing pause for as long as it is on screen.
- **Wrong covers fixed (Android, web, Windows).** On the owner's library 1,807 of 10,325 songs showed another album's picture: Jellyfin gives songs of unrelated albums the same album id, and a song without its own picture borrowed the album's. An album picture is now used only when every song pointing at it is of that one album; other songs show no cover (Android) or a catalog lookup (web). A song's own picture is never touched.
- The account sync at launch is background work; the first screens' covers are loaded before the first frame.
- For testers: `adb logcat -s FLACieBoot FLACieProbe FLACieCover` prints launch timings and how many on-screen cover slots have their picture; `am start --ez clearcovers true` starts with an empty cover cache.

**Tested** (this build)
- Android unit tests: 302 run, 2 skipped (as before), 0 failed. .NET tests: 257 pass. Browser tests: 133 pass. The release APK is signed with the owner's key (versionCode 71).
- On the Z Fold (real library, 5 launches warm and 5 with an empty cover cache): launch time and cover completeness as above; the Jellyfin library was only read.
- On the emulator against the 10,000-song mock with a slow and a flaky server: covers complete in every run; Home drawn in about 0.15 s.
- A read-only check of the real library's cover keys (no song's own picture shared, 1,807 songs found with a doubtful album picture) and of three albums against the live Jellyfin image endpoint.

**Not tested**
- The Windows MSI was built, not installed.
- The RG Rotate was not used.
- Nothing measured on the server side under load (no CPU numbers from the real Jellyfin).
- The final build's smaller launch prefetch (12 covers) was run on the Fold only, not on the emulator matrix.
- Songs whose album picture the vetting removed show a placeholder on the phone; whether the phone should look them up in a public catalog like the web does is undecided.
- Everything in beta 55's "Not tested" still applies.

## v1.0.0-beta55

Beta 55 (Android 70, MSI 0.55.0, FLACie Web 1.0-beta55). A small follow-up to beta 54. Still a beta, not a 1.0 final.

**Changed: a new download's source reads just "Streaming", in blue (Android, web, Windows)**
- A song that was just downloaded and is played straight from your server before Jellyfin has scanned it used to say "Streaming from new download" (the source pill, the Info panel, the "Playing from" line) and "New download" (the list badge). All of those now say **"Streaming"**, in a fixed **blue** (not the accent colour): the list badge, the Now Playing pill, the Info source line, and the small marker a phone row keeps for such a song after the 60-minute window.
- Unchanged from beta 54: the status line under the artist ("Downloading · ..." in grey, "Recently downloaded · 1s ago" in green for 60 minutes, "Couldn't download" in red), the autoplay rows, and the phone's report of finished downloads to FLACie Web.
- Beta 54 had a rule that the word "Streaming" alone was never a badge; a test asserted it. That test now asserts the reverse (only a new download is called "Streaming"); no other test was changed or removed.

**Tested** (this build)
- .NET unit tests: 250 pass. Browser tests: 133 pass (the download-tags spec now also checks the badge text and its blue colour). Android unit tests pass; the release APK is signed with the owner's key (versionCode 70).
- FLACie Web 1.0-beta55 deployed to the owner's server; the live stylesheet gives the badge and the Now Playing pill `rgb(109, 179, 255)` on `rgb(18, 35, 59)`.

**Not tested**
- The phone on a device or the emulator for this change: the new colour and word are constants covered by unit tests and a compile, not looked at on screen (the beta 54 emulator run covered the layout they sit in).
- The Windows MSI was built, not installed.
- Everything in beta 54's "Not tested" and "Known problems" still applies.

## v1.0.0-beta54

Beta 54 (Android 69, MSI 0.54.0, FLACie Web 1.0-beta54). Still a beta, not a 1.0 final.

**New downloads are labelled the same everywhere, and you can see what is downloading (Android, web, Windows)**
- **One wording.** A song that was just downloaded and is played from your server before Jellyfin has scanned it says **"Streaming from new download"** (web: Now Playing source pill, "Playing from" line and Info; Android: the same). The list badge says **"New download"** (it used to say "Streaming"). The web's green "your server (new download)" pill is gone; the pill is quiet grey on both.
- **A status line under the artist**, in the player card, Now Playing, the queue / Up next, every autoplay song and every list that shows songs (Explore, albums, artists, genres, playlists, favourites, search, Home):
  - **"Downloading · Searching Soulseek..."** (the live stage; a stage that already starts with "Downloading", such as "Downloading 55%...", is shown as it is) while a song is being fetched,
  - **"Recently downloaded · 1s ago"** (ticks live: just now, Ns ago, N min ago) for **60 minutes** after it finished, then it disappears: a song downloaded a week ago says nothing,
  - **"Couldn't download"** for a failed fetch, for the same 60 minutes.
  In narrow places (cards, the phone's Up next pane, the web player bar on a narrow window) it reads "Downloaded · 4m ago"; the age is never the part that is cut. Colours are the same on both: green for recently downloaded, quiet grey for downloading and for "New download", reddish for failed; none follows your accent colour.
- **Autoplay songs are marked, including ones still downloading.** On the web and on Android, "Autoplay · up next after this" lists the songs being fetched ahead as rows with "Downloading · <stage>", and each becomes a normal queue row with "Recently downloaded · Ns ago" when it lands. Those pending rows are display-only (no tap, no remove).
- **A song older than 60 minutes that Jellyfin has not scanned yet** keeps a small "New download" marker (on the phone's list rows, whose width hides the badge).

**Fixed: a download made on the phone was invisible to the web**
- Beta 53 and older: you downloaded "P" by JME on the phone (it played as streaming), then searched for it on the web and it was not there, so you downloaded it again. The phone's download never reached the web server.
- Now the phone **reports every finished download to FLACie Web** (when a FLACie Web address is set; Jellyfin stays the only required thing): `POST /api/downloaded`. The web then shows it as recently downloaded, adds it to your session as a playable new download, offers "In your library" instead of Download in Search (it does not download it a second time), and asks Jellyfin for a scan. The server only accepts a file location that starts with your own file mover address, never fetches anything from what the phone sends, limits reports to 200 an hour per user and clamps the reported time to the last 10 minutes.
- The Downloads page lists such a song as "Downloaded on your phone".
- Not done: a download made on the web does not get the "recently downloaded" tag on the phone (the phone would have to read the web's download list).

**Also**
- Web: Now Playing shows a song's file facts (Hi-Res badge, sampling, kbps) only for the song they were read for, never the previous song's for a moment after a skip.
- Version numbers: Android 1.0-beta54 (69), Windows 0.54.0, FLACie Web 1.0-beta54.

**Tested** (this build)
- .NET unit tests: 250 pass (187 in beta 53). Browser tests: 133 pass (119 in beta 53), including a new spec with a stand-in slskd and file mover that drives a real download through the dashboard and checks the tag in the player card, Up next, a list and the autoplay lists, a 2-hour-old and a week-old record showing no tag, the phone report (401, 400, 413, path rules, 429), Search turning into "In your library", and the narrow form. Android unit tests: 280 run, 2 skipped (as before), 0 failed (236 in beta 53).
- Emulator, release APK, against the mock Jellyfin and a stand-in download stack (the owner's case "P" by JME plus autoplay songs): the status line and its ticking in the mini player, Now Playing, Up next and lists; "Streaming from new download"; autoplay pending rows turning into normal rows without a duplicate; the 60-minute rule with the device clock moved ahead; colours with the pink and the blue accent; the Home cards and the narrow Up next pane; 12 downloads gave 12 reports, none twice; no crash, no ANR; the small 720x720 screen shape without overflow (`docs/test-run/J-android-verify.md`).
- FLACie Web 1.0-beta54 deployed to the owner's server and checked in a browser (labels, live tag text); the new tags themselves were exercised on a local copy against the mock, not by creating a fake download on the real server.

**Not tested**
- A real phone, the Fold, the RG Rotate: only an emulator with a 720x720 shape.
- Lidarr, the open sources and yt-dlp downloads: only Soulseek was served by the stand-in.
- The Windows MSI was built but not installed; the Windows app embeds the same server code.
- The web's autoplay rows with the real online catalog (the specs use a stand-in).

**Known problems**
- On the phone's narrow Up next pane a pending row shows only "Downloading" until the download actually starts (the stage text does not fit).
- After the app is killed and restarted the restored queue keeps its tags but loses the "Autoplay · up next after this" divider.
- On the Android Up next pane the divider's text is slightly clipped at the top edge.
- Everything listed under beta 52 and 53 "Known problems" still applies (the web's early read of the next song uses a different URL from the real stream, the whole queue is exposed to Android's media session, read-ahead of whole songs on the phone, no Undo for web queue removal, `MEDIA_PLAY` in the background after losing audio focus).

## v1.0.0-beta53

Beta 53 (Android 68, MSI 0.53.0, FLACie Web 1.0-beta53). A fix release for one serious bug in beta 52. Still a beta, not a 1.0 final.

**Fixed: wrong cover art (Android, web, Windows)**
- **What went wrong in beta 52:** to cut down on cover requests, beta 52 used one picture per album, taken from Jellyfin's `AlbumId` / `AlbumPrimaryImageTag`. On a real Jellyfin `AlbumId` is not reliable: songs of many different albums share one, so dozens of songs showed the same wrong picture (in a playlist: 1989, Bangerz, The Blueprint 3, PRISM and others all got one cover). The mock Jellyfin used for the beta 52 tests never did this, so no test caught it. This was a regression of my change, not something older.
- **Now:** a song's own picture (its Primary image and that picture's tag) always wins; the album's picture is used only for a song that has none. The picture is still fetched once, kept on disk, at one small size (240 px WebP) and revalidated by tag, so the load reduction stays: what is given up is the sharing of one picture between songs of an album, because that sharing was wrong.
- **Old data:** the saved library on the web server and on the phone held the wrong cover keys, so both are discarded once and the library is listed again on the next start (a few minutes of background work; the songs stay on screen). Nothing of yours is lost: playlists, favourites and sign-in are untouched.

**Tested** (this build)
- .NET unit tests: 187 pass (the beta 52 test that asserted the wrong rule was replaced by one that asserts the right one, with songs of different albums sharing an `AlbumId`). Browser tests: 119 pass (one spec that counted pictures by the wrong assumption was corrected, and the 40-skip spec now makes its clicks inside the page so its timing no longer depends on the test runner). Android unit tests: 236 run, 2 skipped (as before), 0 failed (the same replacement, plus a fallback test).
- FLACie Web 1.0-beta53 was deployed to the owner's server and checked in a browser on a playlist: every song shows its own picture (distinct cover keys per song, the playlist header is its first song's cover).
- Measured live on the same server, muted: after pressing Next in a playlist the new title is on the bar in 11 to 68 ms, covers answer in under 25 ms, and a song's stream starts in 29 to 266 ms (one 1.2 s start in eight skips).

**Not tested**
- The phone app on a real phone: the fix is unit-tested and the APK is built and signed with the owner's key, but it was not installed on a device or the emulator in this release.
- The Windows MSI was built but not installed.
- Real Jellyfin for the phone's picture requests (the same URLs were checked from the server against the owner's Jellyfin: 200, WebP / JPEG).

**Known problems**
- The web player's early read of the next song (`pre=1`) uses a different URL from the real stream, so the browser cannot reuse it; it only adds a second stream open per song on Jellyfin. It does not make skips faster and may be removed in the next build. (Beta 51 did not have it.)
- Everything listed under beta 52's "Known problems" still applies (whole queue exposed to Android's media session, read-ahead of whole songs on the phone, no Undo for web queue removal, `MEDIA_PLAY` in the background after losing audio focus).

## v1.0.0-beta52

Beta 52 (Android 67, MSI 0.52.0, FLACie Web 1.0-beta52). Still a beta, not a 1.0 final. This release is about one thing: FLACie no longer overloads Jellyfin, and stays instant. Everything below ships on Android, web and Windows together unless it says otherwise.

**What changed for Jellyfin (the load work)**
- **One gate for every request to Jellyfin** (Android, web server, Windows app). A limited number run at once (it starts at about 5 and adapts: it grows after a run of fast answers and halves when Jellyfin is slow, answers 429 or 5xx, or times out), the rest wait in a queue. A request that fails for a passing reason is retried at most twice, after a randomised wait that honours `Retry-After`. A server that keeps failing trips a circuit breaker: requests fail at once for a short while instead of piling up, then one probe goes through.
- **Priorities and cancellation.** The song that is playing goes first, what is on screen next, background work (library sync, format reads, read-ahead, charts) only when nothing more important waits, nobody is streaming and the server is not slow. A cover for a row that scrolled away is cancelled; the same request asked twice at once is one request; a stream the player gave up on (skips) is cut and its slot returned.
- **Bulk and delta sync.** The library arrives in pages of 500 songs that already carry each song's stream data (codec, bit depth, sample rate, bit rate), so there are no one-song format reads any more (beta 51 made 10,000 of them for 10,000 songs, and retried unreadable ones forever). Songs show up as the pages arrive. After the first sync only songs saved since the last one are asked for (`MinDateLastSaved`), and Jellyfin's WebSocket (`LibraryChanged`) starts that delta instead of polling. The library, its cursor and the set of ids are kept on disk, so a restart costs one small request. A deleted song disappears.
- **Covers are fetched once, at one size.** Rows and tiles use one picture size (240 px WebP, quality 80), only the Now Playing cover uses the full picture. The picture is the album's, shared by all its songs (one request per album, not per song; compilations keep a picture per song). A saved picture is asked for again only when Jellyfin's image tag for it changed (ETag / 304).
- **No fixed cache cap.** Everything is kept while there is room; the oldest unused covers are removed only when the free space of the device or server volume is low (below 10 % or 1.5 to 2 GB). Settings shows the cache size and has a **Clear cache** button (Android: Settings > Storage; web and Windows: Settings, administrators).
- **Streams.** Direct stream by default (transcoding only for formats a player cannot play and, on Android, with the data saver). The web player preloads the first MiB of the next song; the phone caches the next song at background priority.
- **The Windows app's embedded server** no longer runs the library-wide background passes; it only does the delta sync and answers the song someone is looking at.
- **Hi-Res badge** (one rule, as in beta 51) now also comes from the facts in the listing, so Jellyfin alone is enough. An Android bug was fixed: a stale decoder name could label an MP3 as FLAC (and so Hi-Res) before the file's own facts were known.

**Also new**
- **Up next can be edited** (web, Windows and Android): move a song up, down or to the top ("Play next") and remove it. The song that plays stays current; on Android a removal can be undone. The order of a shuffled or rearranged queue survives a restart.
- **Windows:** the desktop app's own player is labelled "FLACie for Windows" on the dashboard and Devices pages instead of "Edge on Windows" (the Jellyfin device name stays "FLACie Web"; the client id stays `ipodplayer`).
- **Click-wheel view (Android):** the hardware PLAY and PAUSE keys now work there too (before, only the combined play/pause key was handled).
- Version numbers: Android 1.0-beta52 (67), Windows 0.52.0, FLACie Web 1.0-beta52 (Settings > About).

**Measured** (details and every number: `docs/test-run/I-load.md`; mock Jellyfin with 10,000 songs, not a real server, not a real phone)
- Web server, empty start, first two minutes: 10,074 requests before, 67 after (fast server); 1,782 before, 67 after (300 ms server). The busiest second went from 1,682 requests to 15.
- Scrolling the whole 10,000-song list: 7,562 cover requests (500 px JPEG) before, 829 (240 px WebP) after. Phone: 628 before, 103 after.
- A restart with the library on disk: 696 requests before, 25 after (slow server). Phone: 34 before, 20 after.
- 40 quick skips on a slow server: 244 requests (20 at the same moment) before, 53 (6 at once) after.
- A rate-limiting server (429 above 20 requests per second): 1,351 refused answers before, none after. A flaky server (10 % 503): beta 51 never listed the library, beta 52 lists all 10,000 songs in about 5 s (web).
- Not better, said plainly: the phone's first sync makes more (and bigger) requests than before (69 vs 46) and takes longer to list everything (about 45 s vs 15 s on a fast server) because stream facts now come with each page and the sync paces itself; the first songs still show after the same time. See I-load.md.

**Tested** (this build; run and passed unless marked)
- .NET unit tests: 187 pass (106 in beta 51; 81 new). One of them failed once in a parallel run on the merged tree because test classes overwrote each other's process-wide "current" instance; the suite now runs serially and passed three times in a row. Browser tests (Playwright, the real server against a mock Jellyfin): 119 pass (109 in beta 51; 10 new), one worker, fresh server, 7.2 minutes, on the final merged tree. Android unit tests: 235 run, 2 skipped (as before), 0 failed.
- New tests: the gate (concurrency limit, priority order, back-off with Retry-After, at most two retries, cancellation of queued and running requests, circuit breaker open / half-open / closed, request sharing, background held while streaming or slow); delta sync (changed, new and removed songs, cursor and ids persisted, restart cost, socket removals); cover cache (nothing deleted while there is room, oldest removed only when low, fresh covers protected, cache report and Clear cache); the Hi-Res rule from listing facts; queue editing arithmetic (exhaustive for small queues, on Android and web); a 40-skip burst with a stable badge and bounded streams; Settings cache line and Clear cache; "a cover is fetched once at 240 px WebP, a second browser asks for none".
- Load runs against the 10,000-song mock on the web server, the Windows-mode server and the Android emulator, four server behaviours each (fast, slow, flaky, rate-limiting): `I-load.md`.
- Android emulator (release APK 1.0-beta52, signed with the owner's key; mock Jellyfin): 22 of 23 checks passed (details `docs/test-run/I-android-emulator.md`): fresh install, first-launch screens, Quick Connect sign-in, 10,000 songs listed, scrolling, a song plays, 40-skip burst, pause, kill and relaunch restores the paused queue, Settings > Storage cache line and Clear cache (the library stays), Up next with Play next / Move up / Move down / Remove (the playing song stays current), no ANR and no crash of the app. The one failed check is the emulator's crash buffer: the system Bluetooth process (`com.google.android.bluetooth`) ran out of memory during the 40-skip burst; the same happened with beta 51 (4 of 4 bursts, both builds), so it is not new, but see Known problems.
- Windows MSI 0.52.0 built from this tree (WiX); the installer itself was not run (see below).

**Not tested**
- **Real Jellyfin.** All load numbers are from a mock. Real behaviour of `MinDateLastSaved`, `LibraryChanged`, `Retry-After`, image tags and album pictures, WebP covers, and how slow a page of 500 songs with streams is, was taken from documentation, not observed.
- **Real devices:** the Fold (cover and inner screens), the RG Rotate and any phone. The new Settings rows, the Up next menus, the long-press and the d-pad focus in them were compiled and unit-tested (pure logic) but not used on a device.
- **Windows:** the MSI was built but not installed, upgraded or uninstalled; the "FLACie for Windows" label was tested only as a unit and in the web server, not in the real WebView2 window.
- **Anything audible:** gapless transitions, the Hi-Res badge compared with the sound, the equalizer, headphone volume, whether a read-ahead glitches a stream. No by-ear checks were made.
- First NAS scan jank on a fresh install (needs a NAS), and the notification-permission timing on Android 13+.
- Long soak (hours), memory over time with the new caches, a full disk.

**Known problems and open items**
- **The whole queue is published to Android's media session.** With a 10,000-song queue the system media session holds all 10,000 items, and on the emulator image a system Bluetooth process (a controller reading that queue) ran out of memory during a skip burst, in beta 51 and in beta 52 alike. The app itself did not crash. Whether a real phone's Bluetooth / car / watch controllers are affected is not known. The likely fix (not made in this build because it changes lock-screen and Bluetooth behaviour that could not be checked on a device): expose only a window around the current song, or no timeline, to external controllers.
- The phone's read-ahead caches the whole next one or two songs, not only the start (background priority, skipped while the server is slow). Starting only the first part of the next song is not done.
- `MEDIA_PLAY` from a headset in the background after another app took audio focus is still not resumed: Android routes the media button elsewhere in that state (explained in `docs/test-run/I-android-features-notes.md`). Play/Pause and the in-app keys work.
- Web: removing a song from Up next has no Undo (Android has one); no drag and drop (buttons only).
- Jellyfin server edits that are not dated (an in-place change without a new `DateLastSaved`) are seen only after Clear cache and a restart.
- Windows pages that use the older shared client (the iPod sync tabs) are not behind the gate; they are user-driven, not background load.
- The player's own retry (three tries, then skip) walks through a queue quickly while the breaker is open.
- Android still does not follow the system light theme (unchanged).

**Owner checklist** (things only you can do or hear)
1. Install the APK over beta 51 on the Fold and the RG Rotate (same signing key, so it updates in place). Sign in is kept. Watch the first sync: songs should appear within seconds and the rest fill in; `adb logcat -s FLACieGate` prints the request counts every 10 s.
2. On the real Jellyfin: watch its CPU during a first sync and while scrolling Explore with covers loading; open the dashboard (admin > Dashboard / `/api/admin/load` on FLACie Web) for the request counts per minute. Compare with the numbers in `I-load.md`.
3. Settings > Storage (Android) and Settings > Cache (web): the size looks sensible; Clear cache empties covers and the library comes back without a full re-sync.
4. By ear: skip bursts on real songs, gapless between albums, the Hi-Res badge on a 48 kHz and a 24/96 file, and that the next song starts at once.
5. Up next: move, play next and remove on the Fold, on the RG Rotate (d-pad) and in the web player; check the playing song never changes by itself.
6. Windows: install MSI 0.52.0 over 0.51.0 (upgrade closes the running app), check the dashboard label "FLACie for Windows" and that the app idles quietly.
7. Deploy FLACie Web 1.0-beta52 with the recipe in `docs/private/HANDOFF-2026-10-11.md` (rollback tag first; `/healthz` must say "ok"); sign in again with Quick Connect if the session was lost.

## v1.0.0-beta51

Beta 51 (Android 66, MSI 0.51.0). Still a beta, not a 1.0 final.

**Hi-Res badge fixed (Android, web, Windows)**
The badge used to flip between "Hi-Res" and "FLAC" for the same song while skipping, and could say Hi-Res for a normal CD-quality file. Causes found and fixed:
- **One rule everywhere:** a file is Hi-Res only when it is lossless AND its real metadata says more than 16 bits OR more than 44.1 kHz. Not the bit rate, not the file extension, not "[Hi-Res]" in a file name, not a hint from a server. A value that is not known yet never makes a file Hi-Res; until the real values are known you see the plain format (for example "FLAC").
- **Android, Now Playing and Info:** the badge was taken from the audio player's live output format, which still belongs to the previous song while the next one starts, and it was never refreshed. It now comes from the file's own facts (from FLACie Web or the NAS tags), and the player's format is used only while that very song is playing; it refreshes while the song is on screen. The spectrum axis uses the same values as the badge.
- **Android song lists:** the "[Hi-Res]" file-name guess is gone.
- **Web and Windows:** the song-list badge also guessed from "[Hi-Res]" in the file name (a CD-quality file filed by the Hi-Res downloader showed Hi-Res); gone. A failed or partial format read was cached forever (so a hiccup during a skip burst could stick); only real reads are cached now and failures are retried. A later partial read can no longer overwrite a complete one. List rows, Now Playing, the Info panel and the Explore filter all read the same facts.
- The Hi-Res download search still uses its bit-rate hint to choose which file to fetch; that only affects which file is downloaded, never a badge.

**Also**
- Version numbers: Android 1.0-beta51 (66), Windows 0.51.0, FLACie Web 1.0-beta51 (shown in Settings > About).
- A debug log line (tag FLACieBadge) records each song's badge values in Now Playing, to help diagnose any flicker.

**Tested** (this build; run and passed unless marked)
- .NET unit tests: 106 pass (new: the Hi-Res rule table, name guess removed, partial reads, no caching of failed reads). Browser tests: 109 pass (new: skipping 40 songs quickly across a set of FLAC/MP3 files with different bit depths and rates keeps each song's badge single-valued and equal to its real metadata, in the list, Now Playing and the Info panel). Android unit tests: 107 pass (new: the rule, the stale-player-format case, "[Hi-Res]" in a name with no facts).
- RG Rotate (real device, release build): 180 skips (60 forward, 60 back, bursts) over 61 lossless songs with the badge logged per song: no song ever showed two different real values; four songs briefly showed an unknown (no codec) state before their real value arrived and then settled.
- Real Fold 7 (beta 50 release build, cover screen): a 780-iteration loop of scrolling Home up and down with local playback and pressing Next every third round, 35 minutes, no crash and no Compose error (the crash seen earlier did not reproduce), and a memory curve that stayed flat (Java heap 33 to 79 MB, limit about 192 MB, total about 200 to 240 MB). This confirms the Compose crash fix on the device that crashed.
- RG Rotate after clearing app data: the first-launch screen asks where your music comes from (Jellyfin, NAS or this device) before any system prompt; Jellyfin Quick Connect sign-in worked and the library loaded.
- Fold inner screen (unfolded by the owner): Home looked right (left rail, Quick picks in two columns, mini player); a read-only screenshot, not a full test.
- Windows: the same server code as the web tests; the desktop app was rebuilt as MSI 0.51.0 but the Windows install of this exact version was not repeated (the 0.50.0 install, upgrade and uninstall checks apply).

**Not tested**
- The rest of the Fold test run was stopped: the full inner and cover screen sweeps, display scaling, split screen, 3-button navigation, orientation, the Hi-Res skip check on the Fold, audio focus, Jams and Connect hand-over, offline. The Fold has the Hi-Res fix installed but it was not re-checked there.
- First NAS scan jank on a fresh install, and the notification permission being asked only when needed (the RG is Android 12, which has no such permission): not measured.
- Anything audible: gapless transitions, the Hi-Res badge compared with the sound, the equalizer, headphone volume were not tested by this process (the owner reports testing audio by ear).
- The Jellyfin real-server paths of the web fix: the facts reader was tested against a mock Jellyfin. A Jellyfin FLAC that lacks BitDepth at 44.1 kHz stays non-Hi-Res.

**Known problems and work in progress**
- **Jellyfin load.** During testing the home Jellyfin ran at about 250 % CPU and FLACie Web at 100 %, and covers loaded slowly. Part of it was the testing, part is how FLACie behaves: format reads for the whole library are retried every few minutes, and every API call asks Jellyfin who you are. A fix is NOT in this build; it exists as unfinished work on the branches `fix/server-load` (central adaptive request gate, bulk and delta sync, cover cache, load-test mock with 10,000 songs) and `fix/android-gate` (the same for the phone: gate, Jellyfin-only facts and covers at one fixed size, cache size in Settings). Both may not compile yet.
- Queue reordering, Android system light theme and the Android background MEDIA_PLAY resume after audio-focus loss are unchanged from beta 50 (see its notes).

## v1.0.0-beta50

Beta 50 (Android 65, MSI 0.50.0). Still a beta, not a 1.0 final.

**Android: fixes**
- **NAS songs now show real tags.** Title, artist, album, track, duration, sample rate, bit depth and the embedded cover are read from the files (MP3, FLAC, M4A/ALAC, Ogg/Opus, WAV, AIFF) and remembered, so the Hi-Res badge and durations are right. The first scan of a big NAS runs in the background at low priority and waits while you use the screen; later starts use what was remembered.
- **Jellyfin songs without tags** that were listed by their file name ("06 - Artist - Title") are shown with a cleaned title (Android and web use the same rules and the same 50 test cases).
- **First launch** explains what the app needs and asks you to sign in before any system permission prompt; media and notification permissions are asked for only when needed.
- **Lists are smoother** on the RG Rotate (Explore with 10,000 songs: 5.7 % janky frames in beta 49, about 1 % now once the NAS scan is finished).
- **A crash while saving a very large library** (out of memory) is fixed: the library is now written as a stream.
- **A freeze after pressing Next many times** is much shorter: abandoned downloads are cancelled at once and skips are collapsed (45 quick presses now start playback in about 4 s on the RG Rotate, was 25 to 40 s; one slow Jellyfin song can still take much longer).
- **Seek bar while paused** now follows a seek.
- **Now Playing on small square screens (RG Rotate)**: the cover and the controls sit side by side and nothing is cut off; the Info graph and its axis fit; Lyrics, Info and Up next use the full width. Several other screens got the same treatment (Explore header, search, mini player, dialogs hidden behind the keyboard, Headphone test tabs).
- **D-pad / gamepad**: the Explore search box no longer traps focus or pops up the keyboard; every control now has a visible focus ring (Quick picks, mini player, rows, play button); touch targets are at least 44 dp.
- **Queue and position are restored after the app is killed** (paused, with the mini player). Empty playlists keep their Rename/Delete buttons; Undo after removing a song lasts 6 seconds. The sleep timer shows the time left (Settings and Now Playing) and survives a restart. An Offline chip shows on Home and Search without a connection. Song rows on narrow screens keep more of the title.
- Compose libraries updated (BOM 2024.09.03) after a crash inside the Home list; see "Known problems".

**Windows app (MSI 0.50.0)**
- The embedded web view keeps its data in your user folder (so uninstall cleans up and sign-in survives upgrades), only one copy can run, installing over a running app closes it first, licences ship with the installer, no debug files. The leftover folder of the old version is removed on first start.
- The desktop app no longer runs the daily charts download itself (the always-on server does); charts also skip songs fetched in the last hours.

**Web (FLACie Web)**
- The playlist importer refuses binary or non-text files (400) while plain text and m3u lists still work; malformed requests such as `/art/..` and bad settings bodies get clean 4xx answers instead of error pages; 7 server bugs found by the audit are fixed (duplicate keys on concurrent profile saves, unbounded memory in the play tracker, a pasted token injecting header lines, range requests on empty files, a lost download-log line, import cancel by another user, oversize chunked imports).
- Downloads: a "(Piano Version)" request no longer takes the original recording; Autoplay and Daily charts do not fetch songs fetched a moment ago.
- Layout: full-screen Now Playing, the bottom navigation (scrolls, 11 items for admins), the A-Z rail, the header on short windows and phones; the play button shows a spinner while a song is loading; skipping through many songs opens only the stream of the song you stop on.
- New: an "About" section in Settings (version, server, data folder, licences), a proper "page not found" page inside the app, case-insensitive artist and album links, keyboard shortcuts (Space, Left/Right = 5 s, N, P).

**Tested** (this build; everything below was run and passed unless marked)
- .NET unit tests: 87 pass. Android unit tests: 99 run, 0 failed (2 skipped, as before). Browser tests: 107 pass (one new test was racing the player bar animation and was fixed, not loosened).
- RG Rotate (real device, release build): about 40 checks, see docs/test-run/G-rg.md: dark theme, font scale 0.85 and 1.3, lyrics, sleep timer, shuffle/repeat, playlist create/add/remove/rename/delete, Dashboard and download log, all media and gamepad keys, kill and restore, offline, cold start 475 to 786 ms, screen-off playback for 2 minutes, audio focus with YouTube, Explore jank (median 1.0 %, five runs), no crash or ANR in the log; then every screen re-checked after the layout fixes.
- Windows 0.50.0: install, upgrade from 0.49 over a running app, single instance, licences present, no .pdb, uninstall closes the app; the signed-in app swept page by page through its debug port (46 checks, docs/test-run/G-win.md), then the fixes were re-tested in the browser suite.
- Emulator: the Home scroll-with-Next loop that crashed the Fold survived 700 and 1500 iterations on debug and on release builds, and a 30 minute memory loop stayed flat (docs/test-run/G-emu.md).
- Several songs were downloaded to the NAS during testing (real downloads, nothing deleted).

**Not tested**
- **The Z Fold 7**: it was withdrawn from the test run, so the crash fix (below) was NOT re-tested on the device that crashed, nor the inner screen, split screen, 3-button navigation or a Fold memory loop.
- **Known problems**: the Home list crash seen on the Fold (a Compose internal error while scrolling Home with music playing and pressing Next) was not reproduced on the emulator or the RG Rotate after the changes (Compose update, no early return in the Home list, Quick picks without BoxWithConstraints), but is not proven fixed on the Fold. If it happens again, tell us what you were doing.
- **First scan of a big NAS** on a fresh install: the pause-while-touching logic is unit-tested, but jank during a first scan was not measured (needs a fresh sign-in).
- The removal of the old version's leftover web-view folder at first start of the Windows app (written, not run).
- First-launch screens on a device (needs cleared app data), the Android notification permission flow, expired session, sign-out, server unreachable, real headphones and Bluetooth, Jam / Connect hand-over between devices (only the device list was opened), hardware media keys on Windows, the Windows iPod manager (no iPod; nothing was written to an iPod), the MSI on a clean machine other than this PC.
- **Audio by ear**: gapless transitions, the Hi-Res badge on a 48 kHz file, the equalizer, headphone volume and a skip burst on a real song were checked only by state, not by listening.
- **Known gaps / not included**: queue reordering and removing songs from Up next (Android and web) is a feature and is not in this build; Android does not follow the system light theme; `MEDIA_PLAY` does not resume playback if the app is in the background after another app took audio focus (Android routes it elsewhere; Play/Pause works); the YouTube fallback and charts downloads configured on your account remain as set.

## v1.0.0-beta49

Beta 49 (Android 64, MSI 0.49.0). Still a beta, not a 1.0 final.

**Notifications: "someone is listening" and "someone downloaded" (web, Windows, Android)**
- **Music played, live.** With notifications on (Settings > Notifications, an ntfy server and topic), FLACie announces each song a person really listens to, as it starts: the user, the song, the playlist or album it plays from, and the device. A song counts after 20 seconds of listening, so skipping through songs says nothing; a repeat of the same song within a minute counts once. After ten quiet minutes there is one "finished" summary (tracks and minutes). Every song has its own listening clock, so a long track played after a short one is never credited to it.
- **No doubles.** A play that goes through Jellyfin is announced by FLACie Web only when the Jellyfin side of your notification setup ignores the FLACie Web device (the new toggle "Also plays through Jellyfin" is on by default; turn it off if your Jellyfin watcher already announces those plays). Plays from the NAS or from a fresh download that Jellyfin has not scanned yet are always announced by FLACie, because nothing else sees them.
- **Android** reports NAS and fresh-download plays to FLACie Web (start, a heartbeat every ten seconds, stop), so those are announced too. Plays of Jellyfin songs on the phone are left to the Jellyfin side, as before.
- **Downloads.** "<user> downloaded <song>" is sent live for what a person fetched with FLACie; a big burst becomes one summary ("47 more": the first three are live, the rest are one message after a quiet minute). Nothing is sent when nothing downloads.
- Every FLACie message now starts with "FLACie ·" so it is easy to tell from the messages of other tools. A refused token reads "ntfy rejected the token (needs write access to this topic)". New toggles: "Someone plays music", "Someone downloads music".
- The access token is stored encrypted, never written to logs or shown again.

**Web fixes**
- **Skipping a song could freeze the page until a reload.** The Info panel read a value that the skip had just cleared, and the error ended the whole page connection. Fixed, and that code can no longer end the connection.
- **Missing or hanging covers.** A cover lookup that stalled held its slot for ever and held up the other covers; every lookup now has a 15 second limit. A cover that was not found used to be remembered by the browser for a week, so a fixed cover stayed missing; a miss is now remembered for 30 seconds. A cover search that matched neither the album nor the song no longer takes another release's artwork (no cover is better than a wrong one).

**Also in this build (from betas 46 and 47, if you have not updated since):** the headphone test (tone and frequency sweep, 1 Hz to 44 kHz, with a level that is kept safe) on Android and web, and the mini player on web.

**Tested**
- 20 new unit tests (.NET): a Jellyfin-routed play says nothing when switched off; a NAS run announces each song once and one summary after ten idle minutes; skipping through 40 songs a second each says nothing; a replay within 60 seconds counts once; two users have independent runs; a short song then a 20 minute one is credited its own length; a refused token reads as above; an unreachable ntfy server does not delay the caller and the token is not in the log or the stored file; downloads are live then summarised.
- Web: all browser tests pass (37). Android: unit tests pass, the release APK builds and is signed with the same key as beta 48.
- The web fixes and notification settings were deployed to the owner's server before this release; a test message from the settings page arrived on a phone.

**Not tested**
- A real song played from a NAS or an unscanned download, end to end to a phone (the logic is covered by unit tests; no NAS was used).
- The Android reporting on a real phone, and the MSI install.
- The cover fixes were reviewed and built but not measured against a large library; the freeze was reproduced from the server log, not on screen.
- Listening to the headphone test with real headphones.

## v1.0.0-beta48

Beta 48 (Android 63, MSI 0.48.0). Still a beta, not a 1.0 final.

**Apple Lossless (the HoodTrap playlist) now plays: FLACie decodes it itself (web, Windows, Android)**
- The songs in a "(LAC)" folder are Apple Lossless (ALAC) in .m4a. Chrome, Edge and Firefox cannot play ALAC at all, and many Android phones have no decoder for it, so the playlist played silence. FLACie Web now **reads and decodes ALAC itself** (a decoder written for FLACie in C#, so no ffmpeg and no Jellyfin conversion is involved) and sends WAV, which every browser plays and can seek in. Other .m4a files (AAC) are sent as they are.
- Windows gets it with the web player. **Android**: on a phone with no ALAC decoder the app plays the song from FLACie Web (it needs FLACie Web set up, as for downloads); with no FLACie Web it falls back to Jellyfin's conversion as before. A phone that has an ALAC decoder still plays the original.
- The decoder was checked **bit for bit against ffmpeg** on 35 real songs from the HoodTrap(LAC) folder and 6 more (24-bit, mono, 44.1 kHz, silence, small frames): identical output on all 41.

**Quick picks: songs you actually listen to (web, Windows, Android)**
- Quick picks used to be mostly random songs from your genres. They now learn from **everything Jellyfin has counted for your account in any app** (plays, favourites, when you last played), plus FLACie's own history, favourites and playlists, and bring forward: songs you play most, the other songs of albums you play, and **the best-known songs of the artists you play** (looked up by artist name only). A song you have never played only gets in when it is tied to something you play (its artist, its album, or one of that artist's hits); a song that just shares a genre no longer is. Roughly three in five are songs you already love and two in five are new to you but related; a song you played in the last six hours sits out. Still steady within the hour and different the next.
- On your own library and Jellyfin history, the old row had 85% never-played songs and 20% well-known songs of your artists; the new one has 40% never-played and 70% well-known (for example Teenage Dream, Bad Romance, Love The Way You Lie, Tik Tok, Imma Be). "Listen again" also shows what Jellyfin says you played last when FLACie has no history yet.

**Tested**
- Web: all browser tests pass, including an Apple Lossless song played end to end (the server decodes it to WAV and the browser plays it, with a mid-file Range request), and Quick picks following the plays a Jellyfin account reports. Against real Jellyfin 12.2 and 10.10.7: four Apple Lossless songs (including a 24-bit one) play on both, byte ranges are exact, and one of them is byte-identical to the ffmpeg-checked decode.
- Android (emulator, Jellyfin 12.2): a 24-bit Apple Lossless song plays through FLACie Web's decoder (the app asks `/api/alac`, the clock runs); 8 new unit tests for the Quick picks rules pass with the rest.

**Not tested**
- A real phone with no ALAC decoder: the emulator has one, so the app was forced through the server's decoder with a developer switch (an empty file named force-own-alac in the app's external files folder). Quick picks on a real phone with your real Jellyfin history (checked on the web side with your real data, and by unit tests on Android). The Deezer lookups of best-known songs ran against Deezer for the web check only. The mini window (picture in picture) in a desktop browser, the Windows MSI install, and listening to any of it with ears.


## v1.0.0-beta47

Beta 47 (Android 62, MSI 0.47.0). Still a beta, not a 1.0 final. A follow-up to beta 46's headphone test.

**Headphone test: clearer, and a much wider range (web, Windows, Android)**
- **1 Hz to 44 kHz.** The frequency slider now starts at 1 Hz (below hearing) and goes up to 44 kHz (above it). A line under the number says what the frequency is: infrasound, sub-bass, bass, mids, presence, treble, or ultrasonic. Tones are made at a 96 kHz sample rate so 44 kHz can exist; your sound card may still cut off lower (the page says what rate it got). Tones below 20 Hz are played quieter on purpose, to protect the drivers.
- **Lower bass tests.** The bass steps now start at 5 Hz (5, 10, 15, 20, 25, 31, 40 … 125), and there are new deep sweeps: 1 → 100 Hz and 1 → 200 Hz.
- **Easier to follow.** Three short steps at the top (start quiet, pick a test, raise the level slowly). The level slider, a Stop button and what is playing stay on screen at the top while you scroll. The tests are in tabs (Left / right, Tone generator, Bass, Treble & clarity, 3D & phase, Amp tips), and each test is one row: a round play button (a square while it plays; press it again to stop), its name, and one line on what you should hear. On the tone generator there are also −1 Hz / +1 Hz / half / double buttons and, on the web, a box to type an exact Hz. The sliders now always show their knob and how far they are set.

**Tested**
- Web (browser test): tabs switch, tones start and stop, a real signal appears in the spectrum, the Hz readout and its description follow 5 Hz, 440 Hz and 30 kHz and the half button, the 3D test runs, pressing a playing test stops it. All web smoke tests pass.
- Android emulator: the new screen opens from Settings > Headphone test, tabs switch and a tone starts and stops.

**Not tested**
- Hearing it, or any of it on real headphones or an amp. Whether anything above about 20 kHz comes out at all depends on your sound card and headphones; tones from 1 to 15 Hz will be mostly inaudible on any headphones.
- The mini window (picture in picture) in a real desktop browser, a real phone, and installing the MSI.


## v1.0.0-beta46

Beta 46 (Android 61, MSI 0.46.0). Still a beta, not a 1.0 final.

**Headphone test (web, Windows, Android)**
- A new **Headphone test** page (web and Windows: Tools > Headphone test; Android: Settings > Headphone test). Test tones are made on the device, nothing is downloaded or sent anywhere, and any music stops while you test.
  - **Left and right**: left only, right only, both, alternating, to check each side is clear, equally loud and on the right side.
  - **Frequency slider**: one steady tone from 20 Hz to 20 kHz on a log slider (type an exact Hz on the web), presets (40 Hz to 16 kHz), left / both / right, sine / triangle / square / saw, and full, bass and treble sweeps. The web page also draws a live spectrum with a marker on the frequency being played.
  - **Bass test**: steps from 20 to 125 Hz, and a kick drum loop to judge depth, punch and how cleanly the bass stops.
  - **Treble and clarity**: steps from 4 to 20 kHz, clicks, hi-hat, “sss” sibilance, two close tones that should wobble slowly (4 and 12 Hz apart), and pink and white noise.
  - **3D and stereo image**: the sound circles around you, sweeps across the front, goes front to back and low to high (the web uses the browser's head-related 3D panner; Android uses a time-and-level model of the two ears), with a picture of where it is; plus an in-phase and out-of-phase noise check.
  - Safety: the level starts low and is capped, every start and stop fades, **Stop** or Escape ends a test. A short note on setting up an amp for 250 Ω headphones is at the bottom.

**Mini player (picture in picture) (web only)**
- A new button in the player bar opens a small always-on-top window with the cover, title, artist, a seek bar and previous / play-pause / next, which stays visible when you switch tabs or apps (Chrome and Edge: a real mini window; other browsers: a video picture-in-picture of the cover with the browser's own play / previous / next buttons). Chrome can also open it on its own when you switch tabs while music plays.

**Downloads fix**
- Retry all no longer starts everything at once (three at a time), a source that errors or times out is treated as "had nothing" so the next source is tried, and the Failed list shows one row per song, hiding songs a later try already downloaded.

**Tested**
- Web: the headphone page opens, the buttons start tones, the Hz slider and presets follow, a real signal shows in the live spectrum (browser test), the 3D test moves, Stop stops; the mini-player button appears in Chrome. Android: compiles, unit tests and the release build pass; see the list below for what ran on the emulator.

**Not tested**
- Hearing any of it: the tones, the sweeps and the 3D effect were checked as signals and on-screen state, not with ears or on a DT 990 Pro. How believable the 3D test is depends on your ears and headphones.
- The mini window itself (opening and its buttons) in a real desktop Chrome, Firefox or Safari; Chrome's automatic opening on tab switch.
- Android on a real phone; the Windows MSI installer itself.


## v1.0.0-beta45

Beta 45 (Android 60, MSI 0.45.0). Still a beta, not a 1.0 final.

**Jellyfin 12 works (web, Windows, Android)**
- Jellyfin 12.x refuses the old `X-Emby-Token` header and `api_key=` link parameter (HTTP 401) and only accepts the `Authorization: MediaBrowser ... Token="..."` header (or `ApiKey=` in a link). Every Jellyfin call in the Android app, the Windows app's Jellyfin tab and FLACie Web now uses those; Connect (remote control) and the live socket use `ApiKey=`. Jellyfin 10.10 accepts all of them, so it keeps working.

**Sign-in lock-out fixed (web)**
- Wrong guesses at a user name no longer lock that user out. Sign-ins are refused only per client address (8 wrong tries in 15 minutes); wrong guesses at one name just make further tries at that name wait 1, 2, 4, up to 8 seconds.

**Android: Jellyfin music source switches on by itself**
- Signing in with Jellyfin now also connects the library (no trip to Settings > Music sources > "Use my account").

**Downloads (web, Windows)**
- When YouTube could not be asked because its service was unreachable, the failure says so ("YouTube was never asked (its service could not be reached); try again in a minute") instead of only "Not found".
- New setting `FLACIE_SERVICE_ROUTES` (`public=>internal` pairs): this server reaches a download service on its own machine directly, so a problem with the public name (it was the DNS name of the Tailscale funnel) cannot stop downloads. The saved address, and so the phone, are unchanged.
- Failed downloads have a **Retry** button each and a **Retry all** at the top of the Failed list (songs only, not whole albums).
- In the queue, a small line under a song says "Downloading" or "Downloaded just now / today / this week" (and "Couldn't be downloaded") for songs Autoplay fetched; it now follows downloads as they change.

**Also**: a sign-in cookie whose Jellyfin token Jellyfin has dropped is signed out to the sign-in page (beta 44 follow-up); on a server locked to one Jellyfin, the admin check no longer depends on which address the account was saved under; MIT licence, security notes, self-hosting guide and a Playwright web smoke test run on every push.

**Tested**
- Against real Jellyfin 12.2.0 and 10.10.7 (Docker): FLACie Web signs in, lists the library, plays, shows the dashboard to the admin, and Jellyfin lists FLACie Web as an active remote-controllable session on both; the Android release APK signs in with Quick Connect, shows the library without a manual step, plays a song, and Jellyfin lists the app as remote-controllable (12.2) / plays (10.10.7); SyncPlay list and create calls succeed on both with the new header. Web smoke test (32 tests) and Android unit tests pass; Windows app starts, serves its pages and stops with the app.
- Frank runs this web build (rollback image kept); the local file-mover address answers.

**Not tested**
- Retry and Retry all and the YouTube service route with a real failing download (no download was run); two real devices in a Jam; the Windows MSI installer itself; a real phone; the queue note while a real autoplay download runs.


## FLACie 1.0 beta 44

_v1.0.0-beta44, 2026-10-08, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta44))

Beta 44 (Android 59, MSI 0.44.0). A quick fix on top of beta 43. Still a beta, not a 1.0 final.

**Fixed: the Devices popup was hidden behind the full-screen player (web, Windows)**
- With the full-screen player open, pressing Devices in the bottom bar opened "Connect to a device" underneath the player, so it looked broken. This was my own regression: in beta 41 I raised the full-screen player above Explore's filter bar, which also put it above the popups that open upwards from the bottom bar. The bottom bar now sits above the player (the player stops where the bar starts, so nothing else moves). The Explore filter bar is still under the player.

**Tested**
- Web in the browser: with the full-screen player open on Explore, the Devices popup opens and is on top (three points across it all hit the popup, and it lists "This browser" and the other player with Play here / Send here). Your server runs this web code and the public address serves the new stylesheet.
- Windows: installer build and app smoke test (starts, /healthz ok, the new stylesheet is inside it, closing the app stops the server). Android: unit tests and release build; no Android code changed.

**Not tested**
- The Devices popup on phone widths (there the full-screen player covers the bottom bar entirely, as before); the rest of the beta 43 not-tested list still applies (see the beta 43 notes): the Android app signed in against server data, Jams, notifications, a real phone or Fold, and installing the MSI itself.


## FLACie 1.0 beta 43

_v1.0.0-beta43, 2026-10-08, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta43))

Beta 43 (Android 58, MSI 0.43.0). Still a beta, not a 1.0 final. Mostly web work, which the Windows app contains; the Android app has no feature change (version bumped to keep the three in step). Tested / not-tested lists below. Notes: docs/TODO-beta40-explore-search.md and docs/NEW-SESSION-START-HERE.md.

**Explore opens on a new Overview tab (web, Windows)**
- Explore is now Overview, Songs, Albums, Artists, Genres, Charts, and the side menu's Explore goes to Overview. It is stacked and full width: a Top result banner (artist, album or song, with a 188 px cover and the Jellyfin / FLAC / bit depth and sample rate / kbps badges), Songs as a list with 48 px thumbnails, Albums as a row of big square covers (title, artist, year), Artists as round avatars ("Artist - N songs"), then Playlists and Genres chips. With nothing typed it browses: newest songs and albums and your biggest artists.
- One compact bar: search, Sort (Best match, Title, Artist, Recently added, Newest year, Longest) and "Filters (N active)", with removable pills under it; the pills narrow every section. "See all N songs" opens the flat Songs tab with the same search and filters. The flat table stays on the Songs tab only.
- Search uses the same stacked layout, with the "More music" downloads below.
- The Top result banner: clicking the banner plays the song and opens Now Playing; the round play button only plays (and becomes pause while it plays); neither restarts a song that is already playing (the button pauses / resumes, the banner just opens the player). The cover no longer overlaps the title.

**Fixed: empty playlists, recommendations and admin after signing in (web)**
- The web server kept one session per Jellyfin user holding the token of whichever sign-in reached it first. After a redeploy an old browser token (already dropped by Jellyfin) came first, the profile load failed, and a later sign-in reused that session, so playlists, recommendations and the admin check stayed empty and Jellyfin was asked with a dead token every few seconds. A fresh sign-in now replaces the cached session, and a session Jellyfin refuses is no longer kept.

**Public address**
- `https://your-server.example.ts.net:8443` stopped working off the tailnet because Tailscale is not publishing a public DNS name for your-server (the funnel itself is configured correctly; it needs a look in the Tailscale admin console). The web app is now also published through the Caddy on your-proxy-host, which does have a public name: **https://your-proxy.example.ts.net:10000**, and **https://your-proxy.example.ts.net/flacie** redirects to it. The app treats any `*.ts.net` address as https. The your-server name still works on devices running Tailscale. A phone app that uses the your-server address keeps working while the phone runs Tailscale.

**Also**: the Bit rate filter bands, typo-tolerant search and the one-row filter bar with the Audio specs drawer and new dropdowns from beta 41 and 42 are in the Windows installer too.

**Tested**
- Web, in the browser against your real library: the Overview in browse mode and for an artist, an album-style and a song search, the banner click and play button (play, pause, resume without restart, banner click while playing), "See all" to the Songs tab with the filters kept, a filter narrowing the page, Search using the same layout, sizes (188 px cover, 48 px thumbnails), and the cover-overlap fix. The public addresses answer over the real public path (health check, login page, /flacie redirect, the live-update channel), and Jellyfin on 443 and Jellyseerr on 8443 still answer after the Caddy changes. Your server runs this web code; after the session fix its profile requests succeed with no 401s.
- Windows: installer build and app smoke test (starts, /healthz ok, closing it stops the server).
- Android: unit tests and the release build; no code change since beta 42.

**Not tested / not done**
- The new session fix was checked by its effect on your live session; a deliberate dead-token sign-in was not reproduced.
- The Overview on phone widths; a search that matches a playlist name; the Windows app signed in; installing the MSI itself.
- The Android app has no Overview tab and keeps its own Explore; the Android bit-rate band results against real server data (the emulator cannot reach FLACie Web); Charts and downloads on the emulator; Jams; notifications (ntfy); a real phone or Fold.


## FLACie 1.0 beta 42

_v1.0.0-beta42, 2026-10-08, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta42))

Beta 42 (Android 57, MSI 0.42.0). Still a beta, not a 1.0 final. Mostly web work (which the Windows app contains), plus the finer bit-rate filter on Android. Test log: docs/TEST-LOG-beta40.md (last section) and docs/TODO-beta40-explore-search.md.

**Bit rate filter: bands all the way up (web, Windows, Android)**
- "Above 320 kbps" held almost 5,000 of your songs, so it is now split: 321 to 700, 701 to 900, 901 to 1,000, 1,001 to 1,500, 1,501 to 2,000, 2,001 to 3,000, 3,001 to 4,000, 4,001 to 5,000 and above 5,000 kbps (the highest in your library is about 6,900). The list is grouped "Lossy range" (up to 320) and "Lossless range". Checked against your real library: every band's count matches the spread measured from the files, and together they add up to every song with a known bit rate.

**Explore filter bar (web, Windows)**
- One quiet row: search, Sort and "Filters (N active)". Chosen filters show as removable pills under it, with Clear all. Genre, Decade and Quality are the main filters; Codec, Bit rate, Bit depth and Sample rate are in an "Audio specs" drawer. Collapsing Filters hides the whole panel.
- New dropdowns (the browser's own lists can't do this): at most 240 px tall and scrolling, they open upwards when there is no room below, the chosen row is in the accent colour with a check, and Codec is grouped Lossless / Lossy / Hi-Res.
- The typo notice is soft grey with "Did you mean ...?" (click to fix the search); when nothing matches even loosely it says so.
- Explore song rows keep 16 px clear of the A to Z bar.

**Search results layout (web, Windows)**
- Sections like Spotify: a Top result card (an artist, album or song, with source, format, bit depth / sample rate and kbps badges, and a play button) beside the five best Songs ("Show all N songs"), then Artists (round, with song counts), Albums (cover, artist, year), Playlists and Genres, then the existing "More music" downloads. The same filter pills narrow every section, and the counts on the cards follow them.

**Also (from beta 41, now in the installer)**: typo-tolerant search and the Codec / Bit rate / Sample rate filters; every boolean aria-expanded / pressed in the web UI now says true or false.

**Tested**
- Web against your real library: every bit-rate band count; the filter bar, pills, drawer and dropdowns (height cap, flipping up in a 520 px window, grouped codec list); "Did you mean"; Search for an artist, an album, a song and a genre; the filters narrowing Search (Metallica: 100 songs became 7 with 1980s); wide and narrow layouts. Your server runs this web code.
- Android: unit tests pass and the release build works. On the emulator (signed in to your account) the Bit rate list shows all the new bands under Filters. The emulator cannot reach FLACie Web (it cannot resolve the Tailscale name), and Android takes bit rates from that server, so picking a band returned "Nothing matches" there: the band results on Android are not verified. The web bands are.
- Windows: installer build and app smoke test (starts, /healthz ok, closing stops the server).

**Not tested / not done**
- Android bit-rate band results against real server data (see above); Charts and the download services on the emulator (it cannot resolve the Tailscale name); starting or joining a Jam; notifications (ntfy); a real phone or Fold; installing the MSI itself.
- Search results on phone widths, and a search that matches a playlist name.
- The Android app does not have the sectioned (Spotify-style) search results or the pill-and-drawer filter bar; those are web and Windows only. Android keeps its own Filters sheet with chips.
- Public web: https://your-server.example.ts.net:8443 currently fails with "DNS_PROBE_FINISHED_NXDOMAIN" because Tailscale is not publishing the Funnel's public DNS name (not an app problem: it needs a look in the Tailscale admin console). On a device running Tailscale, http://100.x.y.z:5255 works.


## FLACie 1.0 beta 41

_v1.0.0-beta41, 2026-10-08, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta41))

Beta 41 (Android 56, MSI 0.41.0). Still a beta, not a 1.0 final. The Explore search upgrade you asked for, on web, Android and Windows. Test log: docs/TEST-LOG-beta40.md (last section).

**Explore search upgrade (web, Android and the Windows app)**
- Typo-tolerant search: "beyonse", "metalica", "daft pnuk" still find Beyoncé, Metallica and Daft Punk. Exact matches always win; only when nothing matches exactly does it show close matches, and it says so ("No exact match for ..., showing the closest matches"). Words of 3 letters or fewer stay exact.
- New filters, combinable with the old ones: **Codec** (FLAC, ALAC, WAV/PCM, AIFF, APE, WavPack, DSD, MP3, AAC/M4A, Ogg Vorbis, Opus, WMA), **Bit rate** (128 or lower, 129-192, 193-256, 257-320, above 320 kbps) and **Sample rate** now lists each rate (below 44.1, 44.1, 48, 88.2, 96, 176.4, 192 kHz and up). They use the real format of each file (as read by the server), so a song the server has not read yet may be missing while it is still reading.
- Web: the filters are a tidy grid of labelled fields (Sort next to the search box, Genre, Decade, Quality, Codec, Bit rate, Bit depth, Sample rate), collapsed under a "Filters" heading with an "N active" badge and "Clear all"; a bigger search box with a clear button. Search is left out of the side menu while you are in Explore (it has its own).
- Android: the same search and filters in Explore's Filters sheet (with removable chips), a taller rounded search field with an accent outline, and the close-matches notice.

**Also fixed (Android)**
- The "Local" / "Jellyfin" source pill is only shown when your library really has more than one source; with one source it was only squeezing song and artist names.
- Empty and error messages (for example when the chart can't be loaded) wrap and centre instead of running off both edges, and the chart error now says what failed.

**Tested**
- Web against the real Jellyfin library (7,975 songs): typo searches, every codec, every bit-rate band and every sample rate (the bands add up to the whole library bar one song the server has not read), the new filter grid expanded and collapsed, Clear all, and a filter combination (typo search + decade + codec + bit rate). Deployed to your-server, /healthz ok.
- Android on the emulator, signed in to your Jellyfin account (Quick Connect approved by you): Explore (7,981 songs), typo search "beyonse" (139 Beyoncé songs), Filters sheet with Codec, Codec = MP3 combined with the search (13 songs, one active filter and a chip), Home shelves, Account, Devices, Download log, the Jam screen (opened, no Jam started), the full player with real format badges.
- Android unit tests, Android release build, Windows MSI build.

**Not tested**
- Charts and the download services on the emulator (it cannot resolve your Tailscale name your-server.example.ts.net); Downloads / Soulseek / Lidarr in the apps; starting or joining a Jam; remote control against another device; notifications (ntfy); Admin; a real phone or Fold; installing the MSI itself (the app inside it was run for beta 40).
- Bit rate, bit depth, sample rate, codec and Hi-Res filtering for songs that exist only on the device: they have no server-read format, so those filters do not match them.


## FLACie 1.0 beta 40

_v1.0.0-beta40, 2026-10-08, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta40))

Beta 40 (Android 55, MSI 0.40.0). Still a beta, not a 1.0 final. Full test log: docs/TEST-LOG-beta40.md.

**Fixed (web)**
- Explore: opening the full-screen player no longer leaves the filter box drawn on top of it (the filter bar sat above the player in the stacking order). Also "Playing from" and the name of where the queue came from were run together; they now stack.
- Song "..." menus (Explore, albums, playlists, search) now close on a click elsewhere or Escape; before, only their own button closed them.
- Accessibility: expanded / pressed / selected / checked states (menus, mood chips, A-Z filter, Charts tabs, Explore tabs, Info panel modes, accent swatches) were rendered as an empty value or left out; they now say true or false.
- "1 songs" is now "1 song" on album, artist, genre, playlist, favourites and Home cards.

**Fixed (Android)**
- Renaming a playlist opened a dialog titled "New playlist" with a Create button and an empty box. It now says "Rename playlist", has the current name filled in, and the button says Save.
- The queue said "Playing from Your queue" after starting a song from Explore > Songs; it now says "Songs", as on the web.
- Small things from the earlier 39.5 test pass carry over (split-screen player layout, artist line, seek knob, nav rail).

**Housekeeping**
- Removed an unused timer field in the web server settings. Versions bumped (Android 55 / 1.0-beta40, MSI 0.40.0).
- your-server runs the web code of this release.

**Tested**
- Web, in a browser against the real Jellyfin library: every Explore tab with all sorts, filters (quality, bit depth, sample rate, genre, decade), the A-Z rail and the text filter; Search incl. no-match and odd characters; the player (next, previous, shuffle, repeat, mute, lyrics, info, queue); song menus; Home moods; Charts; Settings (Autoplay saves across a reload); Devices, Jam, Import, Account and Dashboard pages load. Server log has no errors.
- Android, on an emulator (Pixel 6, Android 14) as a guest with 8 generated test files (FLAC, 24-bit/96 kHz FLAC, MP3 128/256/320, M4A): first-run permissions, Home, Explore (Songs, Albums, Artists, Genres, Lossy filter, compact lists), playback, the full player (Info, Up next), playlists (create, add a song, rename, delete confirmation), Search (incl. a typo), Settings sections, Equalizer presets, sleep timer. Cold start 2.2 to 2.4 s and about 49 MB at idle on the emulator (software rendering, small library: not a phone figure).
- Windows: MSI built; the unpacked app starts, its server answers /healthz, and closing the app stops the server.
- Android unit tests run.

**Not tested / not done (please read)**
- Android signed in to Jellyfin (a fresh emulator needs a Quick Connect approval from you): so not walked through on Android are the Downloads page, charts, Jams, remote control, Admin and account sync. Web Jams and Admin actions were not exercised either (they change your real server).
- Notifications (the ntfy server has never answered from here).
- A real phone or Fold; Android Home scroll smoothness; real-library startup time and memory.
- Installing the MSI itself (the app inside it was run).
- Known limitation: on Android, files that exist only on the device have no audio facts, so the Hi-Res filter and format badges never match them (they work for Jellyfin / NAS songs).
- Queued for the next release: typo-tolerant Explore search with codec and kbps filters, a nicer Explore search box, and Search removed from the side rail (docs/TODO-beta40-explore-search.md).


## FLACie 1.0 beta 39.5

_v1.0.0-beta39.5, 2026-10-06, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta39.5))

Beta 39.5 (Android 54, MSI 0.39.5). A half-way build on the way to beta 40: the full test pass was cut short, so this is released as 39.5. Full log: docs/TEST-LOG-beta40.md.

**Fixed (the three reported bugs)**
- Small window / split screen (Android): two columns are only used with real width (>= 600 dp); a short window gets one scrolling column with the song, seek bar and play controls first. Landscape phone keeps two columns with a small fixed cover. The side nav rail fits short screens.
- Artist name cut off beside the buttons: the Equalizer / Add / More buttons now have their own row; the title and artist get the full width (artist may use two lines).
- Seek knob clipped at the start and end of the track (Android): the track is inset by the knob's radius.

**Also**
- Devices page redesigned on web and Android: a card per player with cover, kind (Browser / Phone / Computer), state (This device / Playing / Paused / Idle), progress and controls; "ipodplayer" is gone from it.
- Web: every button and chip is at least 44 px at phone/tablet widths; narrow-screen fixes (320 px: the "Listening on ..." strip, tab labels). Swept 15 pages at 9 window sizes with no horizontal overflow.
- Web: playlists can be renamed and reordered (Move up / down), Explore > Songs rows now have favourite and a menu (play next, queue, add to playlist), which they did not have at all.
- Downloads: a Soulseek file that arrives is now retried when filing it fails for a moment (and the trail says why); the Lidarr wait follows Lidarr's queue (up to 30 min) instead of giving up after 2 minutes. Most of the failures on your account (Without Me, Blue Moon, People You Know, Blush) happened on 5 Oct, before the YouTube source was switched on; YouTube fallback was tested end to end (Halo came back in 6 s; an obscure song correctly found nothing).
- Account avatar letter matches the Account screen.

**Tested**: Android emulator at 6 window sizes (player), Devices, accent colour; web in the browser pane (sweeps, player, Devices, playlists create / rename / reorder / remove / delete, M3U import and stop), real downloads through Soulseek, YouTube via the file mover.
**Not tested / not done**: Jams, notifications (ntfy server still unreachable), Admin from the phone, Android Explore/Search/Settings page by page, startup time and memory on Android, dead-code clean-up, Windows app smoke test, a real Fold. The Soulseek filing retry and Lidarr wait are fixes for failures seen in the logs and were not reproduced.
**Left behind on the NAS (never deleted)**: Cruel Summer (.wav), vampire, Sneaky Snitch and VALHALLA CALLING under music/<artist>/, and Beyonce - Halo.m4a in <file mover root>/_flacie_test_beta40/.


## FLACie 1.0 beta 39

_v1.0.0-beta39, 2026-10-06, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta39))

Beta 39 (Android 53, MSI 0.39.0). Still a beta, not a 1.0 final.

**Fixed (Android, unfolded / wide player)**: beta 38 left the badges squeezed beside the Equalizer / Add to playlist / More buttons, so "16-bit / 44.1 kHz" dropped to a second line. The three buttons now sit next to the heart on the title row, and the badges (Jellyfin, FLAC, 16-bit / 44.1 kHz) have the whole width, on one line. The "Now Playing" label above the cover is gone in this layout (the chevron and the two buttons stay).

**Tested**: Android release build on an emulator at an unfolded size: badges on one line, buttons beside the heart. **Not tested**: a real Fold; a very long set of badges on the wide layout; installing the MSI or launching the 0.39 Windows app.


## FLACie 1.0 beta 38

_v1.0.0-beta38, 2026-10-06, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta38))

Beta 38 (Android 52, MSI 0.38.0). Still a beta, not a 1.0 final.

**Changed (Android and web)**
- Up next: the song playing now has an accent bar on its left edge and an animated equalizer on its cover (Android) / the existing animated equalizer plus the bar (web).
- Up next: the artist / album line and the title may take two lines before they are cut off (web and Android), so album names show much more.
- Format badges stay one row of equal pills on the web (no wrapping inside a badge); on Android the format badge shows "16-bit / 44.1 kHz" as its own pill like the web.
- Web server with these changes is deployed to your-server.

**Tested**: Android release build on an emulator at unfolded size (bar, equalizer, two-line text, badges); web builds. **Not tested**: the web page after this CSS change in a browser; a real Fold; installing the MSI or launching the 0.38 Windows app.


## FLACie 1.0 beta 37

_v1.0.0-beta37, 2026-10-06, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta37))

Beta 37 (Android 51, MSI 0.37.0). Still a beta, not a 1.0 final.

**Changed (Android)**
- Format tags now look like the web's: separate badges (Jellyfin in pink, the format in green when it is lossless, then the sample rate with bit depth, then the bit rate). They wrap onto a second line instead of being cut off or scrolling, so a long set such as AAC · 48 kHz · 256 kbps · Jellyfin always fits.

**Tested**: Android release build on the emulator (phone view: four badges on one line). **Not tested**: a long set that actually wraps (nothing in the library was long enough here); a real Fold; installing the MSI or launching the 0.37 Windows app.


## FLACie 1.0 beta 36

_v1.0.0-beta36, 2026-10-06, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta36))

Beta 36 (Android 50, MSI 0.36.0). Still a beta, not a 1.0 final.

**Changed (Android player polish)**
- Up next: long titles get two lines before they are cut off.
- Format tags are one tidy row: the source badge, then the format, sample rate and bit rate together in a single badge (FLAC · 44.1 kHz).
- Icons: the outline heart is drawn with the same stroke weight as the other action icons (the plus, the list), and the menu dots are a touch larger.
- Wide / unfolded layout: the queue sits on a slightly raised card with smaller covers, and the song playing now is highlighted, so the player on the left stands out.

**Tested**: Android release build on the emulator at an unfolded-sized screen.
**Not tested**: a real Fold; the phone-width tag row with a very long format string; installing the MSI or launching the 0.36 Windows app.


## FLACie 1.0 beta 35

_v1.0.0-beta35, 2026-10-06, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta35))

Beta 35 (Android 49, MSI 0.35.0). Still a beta, not a 1.0 final.

**Changed (Android)**
- The format tags (Jellyfin, FLAC/AAC/MP3, kHz, kbps) no longer disappear on Lyrics, Info and Up next: the one-line title keeps them (artist · Jellyfin · FLAC · 44.1 kHz).
- Swiping between pages is smoother: the player now follows the swipe as soon as it is heading to a page (it used to wait until the page had settled, then jump), and the title block fades and resizes into its new shape instead of swapping.
- Apple Lossless (ALAC, the .m4a in your HoodTrap(LAC) folder) is now converted by the server only on phones that have no ALAC decoder (many have none and played silence); on phones that have one it plays the original. The conversion bitrate is up from 192 to 320 kbps.

**Dark horse (Kryd Hoodtrap / Mylancore)**: the copy in HoodTrap(LAC) is ALAC at 48 kHz (listed Hi-Res because of the rate); the AAC copy is in Unknown artist/Singles. Both decode fine on a computer. Most likely your Fold has no ALAC decoder, which this build covers. If it is still silent, tell me.

**Tested**: Android release build on the emulator (tags in the one-line title on the Info pane, swiping from the cover).
**Not tested**: the ALAC conversion on a phone without a decoder (the emulator has one, so I could not trigger it), the feel on a real Fold, the MSI install, the 0.35 Windows app.


## FLACie 1.0 beta 34

_v1.0.0-beta34, 2026-10-06, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta34))

Beta 34 (Android 48, MSI 0.34.0). Still a beta, not a 1.0 final.

**Fixed**
- Covers fixed in Jellyfin no longer stay stale. Android kept every cover forever (so "Meet Me Halfway" kept showing "The Beginning" after the real cover was set in Jellyfin); it now fetches Jellyfin covers again after three days, and the old cover folder is cleared once on update. The web server and its pages do the same.
- Player: the song title uses the full width instead of being cut off beside the buttons. The format tags (Jellyfin, AAC, 44.1 kHz) sit on one line under the title, with the Equalizer / Add to playlist / More buttons at the right on wide screens.
- A song the phone cannot decode (no decoder for its format) is now played through the server's conversion instead of going silent, and if a song still cannot play, a message names the error.

**Not reproduced**: "Dark horse (Kryd Hoodtrap / Mylancore)" playing with no sound. I checked both copies in the library on the server: the Apple-lossless one in HoodTrap(LAC) and the AAC one in Unknown artist/Singles both decode with normal loudness, and the song plays on the emulator. If it is still silent on your phone, the message from this build should say why; tell me what it says. (Your phone's status bar shows the muted-speaker icon: worth checking the media volume.)

**Tested**: Android release build on the emulator: wide view (title, one-line tags, buttons, Autoplay switch, "Downloaded just now" note), Meet Me Halfway now shows the E.N.D. cover.
**Not tested**: the decode fallback on a song that really cannot be decoded (none available here); a real Fold; installing the MSI or launching the 0.34 Windows app.


## FLACie 1.0 beta 33

_v1.0.0-beta33, 2026-10-06, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta33))

Beta 33 (Android 47, MSI 0.33.0). Still a beta, not a 1.0 final.

**Changed (Android)**
- Up next now has the web's Autoplay switch ("Add similar songs to the end of the queue"), the same setting as Settings > Autoplay.
- The player shows the web's format tags under the title: where it plays from (Jellyfin...), the format (FLAC, MP3 or Hi-Res), the sample rate and the bit rate when the file says it.
- Unfolded / wide / landscape player is back to an even 50 / 50 split, keeping the new tabs (Up next first, Lyrics, Info), the sideways swipe and the buttons beside the heart.
- The remote "Listening on <device>" bar and the remote player now fall back to the album's cover when the song has none of its own.

**Server data (not an app change)**: the Jellyfin album "The Fame Monster" had no cover at all (Bad Romance and the other songs showed a music note). I added the album's cover (from iTunes) to that album in Jellyfin; Jellyfin keeps it in its own metadata, nothing was written to the NAS. Other albums may be missing covers too.

**Tested**: Android release build on the emulator: wide screen shows the 50 / 50 split with the Autoplay switch and the format tags, phone view shows the tags under the title.
**Not tested**: the remote-cover fallback on a song that really has no cover of its own (the album in question was fixed in Jellyfin as well); a real Fold; installing the MSI or launching the 0.33 Windows app.


## FLACie 1.0 beta 32

_v1.0.0-beta32, 2026-10-06, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta32))

Beta 32 (Android 46, MSI 0.32.0). Still a beta, not a 1.0 final.

**Changed (Android, unfolded / wide / landscape player)**
- Cleaner and less cramped: the cover, title, seek bar and transport buttons on the left (about 42% of the width, with wider margins), and a roomy panel on the right.
- The right side is now like the web: three tabs, Up next (the default), Lyrics, Info. Tap a tab or swipe the panel sideways to move between them, in that order.
- The six action pills at the top are gone. Equalizer, Add to playlist and More are small buttons beside the heart under the title.
- Tall screens (phones, the Fold's cover screen) are unchanged: the swipeable pages with the pill row.

**Tested**: Android release build on an emulator screen of about 1968x1800 at 300 dpi (Fold-inner-like): Up next shows first, swiping moved to Lyrics, the tabs and the buttons beside the heart look right. Not seen on a real Fold.
**Not tested**: a real unfolded Fold 7; the Info tab on that layout after this change (the Info panel itself is unchanged); installing the MSI or launching the 0.32 Windows app.
**Seen, not fixed**: the remote "Listening on <device>" bar on Home showed a blank cover for the song your Fold was playing.


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

**Tested**: Android release build on the emulator (swiping from the cover to Info shows both panes sliding together mid-swipe; the title collapses; Up next pane; the dashed line on a FLAC); web server deployed to your-server and answering /healthz.
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

**Tested**: Android release build on the emulator (swipe cover -> Info -> Up next, pills highlight); web at phone width (search kept focus through typing "scream and shout"; swipe order cover -> Info -> Up next and back to Lyrics); web deployed to your-server.
**Not tested**: the web volume slider on a real phone after this change; the "Downloaded" tags with a freshly downloaded or autoplay song; Home scroll smoothness; installing the MSI or launching the 0.28 Windows app.



## FLACie 1.0 beta 27

_v1.0.0-beta27, 2026-10-05, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta27))

Beta 27 (Android 41, MSI 0.27.0). Still a beta, not a 1.0 final.

**Changed**
- Full-screen player: the play/pause and seek controls move up, and a quiet "Playing from <playlist or mix>" line with a small handle sits above the system gesture area. Swipe up from it (it follows your finger) to raise the queue sheet; it no longer sits where a swipe closes the app. The "Up next" pill opens the same sheet. Android and web (phone width).
- Everything from beta 26 (Funnel back on for port 443 on your-server so the phone's Lidarr / Soulseek / File mover addresses connect off Tailscale, Home lag changes, Search shortcuts).

**Tested**: Android release build on the emulator (line shows, swipe from it raises the sheet, controls clear of the gesture bar); web server deployed to your-server and answering /healthz.
**Not tested**: web phone layout after this change; Home scroll smoothness on a real phone; the phone's Connect buttons off Tailscale; downloaded notes with a freshly downloaded song; installing the MSI or launching the 0.27 Windows app.



## FLACie 1.0 beta 26

_v1.0.0-beta26, 2026-10-05, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta26))

Beta 26 (Android 40, MSI 0.26.0). Still a beta, not a 1.0 final.

**New**
- Up next (YouTube Music style): no bar on the full-screen player. Swipe up from the foot of the player (it follows your finger) or press "Up next": the cover fades back and the queue sheet rises until it fills the screen, with the playing song as a small header, "Playing from <playlist or mix>", then the queue with cover, artist, album and a note for songs FLACie downloaded. Android and web (phone width).
- Home: bigger cover cache and cheaper playlist covers (aimed at the scroll lag), search icon, one chip row, Quick picks with options and Play all. Search tab shows shortcuts and genres.
- Server (your-server): Tailscale Funnel turned back on for port 443, so the phone's Lidarr / Soulseek / File mover addresses (your-server.example.ts.net/lidarr, /slskd, /filemove) connect again without Tailscale.

**Tested**: Android release build on the emulator (swipe up raises the sheet with the cover fading, Up next button opens it, "Playing from" shows the list name); web at phone width earlier; your-server answers 403 (key needed, as expected) on the three paths and logs no more rejected connections.
**Not tested**: Home scroll smoothness on a real phone; the phone's Connect buttons for Lidarr/Soulseek/File mover (only the server side was checked); downloaded notes with a freshly downloaded song; installing the MSI or launching the 0.26 Windows app.



## FLACie 1.0 beta 25

_v1.0.0-beta25, 2026-10-05, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta25))

Beta 25 (Android 39, MSI 0.25.0). Still a beta, not a 1.0 final.

**New**
- Up next bar (YouTube Music style) at the foot of the full-screen player, Android and web (phone-width). Drag it up (it follows your finger) or tap it for the queue: cover, title, artist, album, length, and a note when FLACie downloaded the song ("Downloading", "Downloaded just now / today / this week"). Tap a song to jump to it.
- Everything from beta 24: Home search icon and one chip row, Quick picks with options and Play all, Search tab with shortcuts and genres, visible system bars, swipe-away stops playback.
- Web server with the bar is already deployed to your-server.

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

Tested: web in the browser pane (seek hold, focus, menu, toasts with Undo, Explore filters, Info/Playback, probe of 7,100 songs), your-server deploy healthy, Android launch + Explore Hi-Res badges on the emulator, Android unit tests, YouTube search/ranking on your-server.
Not tested: Android Info panel and snackbar on a device, USB DAC playback, a fresh YouTube download after the fix (needs a new request), Windows MSI install.


## FLACie 1.0 beta 19

_v1.0.0-beta19, 2026-10-05, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta19))

## FLACie 1.0 beta 19 (Android 1.0-beta19 / versionCode 33, Windows 0.19.0)

**Hi-Res stands out.** Hi-Res files now carry an amber **✦ Hi-Res** badge *instead of* the plain FLAC tag (web, Windows and Android song lists, and the full-screen player's format badge), and the "[Hi-Res]" is dropped from their displayed title. Explore's Quality filter has a **✦ Hi-Res** option on every device.

**Explore, redesigned.** The filters sit in one calm card with pill-style pickers, the tabs scroll on phones, and the **A–Z index is now a slim rail down the right edge** (web, Windows and Android).

**Songs now join Jellyfin on their own.** A song you download used to be only a file on the NAS (shown with a "NAS" badge) until Jellyfin's next scheduled scan, sometimes hours later. The server now asks Jellyfin to scan about 15 seconds after a download finishes (at most once a minute; needs a Jellyfin administrator account). Songs that already sat on the NAS unscanned were picked up by a manual scan today.

**Duplicates cleaned up (done on the NAS, not part of the app).** 34 files (857 MB) were removed after checking each: 12 identical "(2)/(3)/(4)" copies in AA-playlist, 2 lower-quality copies beside a better file (an mp3 beside the flac, a 128 kbps mp3 beside a 322 kbps m4a), and 20 of FLACie's own `Artist/Artist - Title` downloads of songs that already existed in an album or playlist folder at equal or better quality. The full list with the file kept for each is in `docs/duplicates-removed-2026-10-05.tsv`. About 800 more "same song" pairs were deliberately left: they are the same recording in an album folder and in a playlist folder (your folders double as playlists), which is how the library is organised.

Everything from beta 18 is included.

**Tested:** the Explore page on the web at phone width, the A–Z rail and filter card, the Hi-Res badge on the web build (compiled and rendered in lists), Android's Explore with the rail on the emulator, unit tests, the release APK launching with no crashes or ANRs, the Windows app starting and its server stopping with it, your-server redeployed and healthy.
**Not tested:** the Hi-Res badge on a real Hi-Res row on the phone, the automatic Jellyfin scan after a download finishing (it needs a real download by an administrator account), a real phone, the MSI installer itself.


## FLACie 1.0 beta 18

_v1.0.0-beta18, 2026-10-05, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta18))

## FLACie 1.0 beta 18 (Android 1.0-beta18 / versionCode 32, Windows 0.18.0)

**Hi-Res on songs you already have (administrators).** A quiet **✦ Hi-Res version…** entry at the foot of a song's "…" menu (web and Windows) and the song sheet (Android), with a confirmation ("2 to 5 times bigger, roughly 40 to 200 MB, saved next to this one"). It searches for a Hi-Res copy; if none exists nothing is downloaded or changed (the automatic fall-back to normal quality only applies to the Hi-Res button on Search results, where you do not have the song yet).

**Tested live:** P!nk - "Just Give Me a Reason (feat. Nate Ruess)" through the app's Hi-Res download: found on Soulseek in under 5 s, saved as `music/P!nk/P!nk - Just Give Me a Reason [Hi-Res].flac`, a genuine 24-bit / 44.1 kHz FLAC (43 MB, 4:02).

Also: Hi-Res size wording corrected (2 to 5 times bigger, about 40 to 200 MB), and everything from beta 17 is included.

**Tested:** the web menu entry and its confirmation (it scrolls into view in the menu), the Android song sheet entry, the real Hi-Res download above, unit tests, the release APK launching with no crashes or ANRs, the Windows app starting and its server stopping with it, your-server redeployed and healthy.
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

**Tested:** the title cleaning on a spread of real titles (C# and Kotlin unit tests), a live Soulseek search for the song (0 files for the full-name query, 244 for the first-word query, 84 normal and 25 Hi-Res candidates), the real download of "All The Stars" through the app's download code (20 s, 16-bit/44.1 kHz FLAC, 25 MB), the YouTube match for the same song (4.7 s), Hi-Res detection and ranking (unit tests), the web Hi-Res button and confirmation at phone width, the Android Hi-Res pill and Look and feel, the page reloading itself after a server restart, the release APK launching with no crashes or ANRs, the Windows app starting and its server stopping with it, your-server redeployed and healthy.
**Not tested:** an actual Hi-Res download (the search and ranking were verified, but I did not put a Hi-Res file into your library), the automatic fallback from Hi-Res to normal as a live run, the Hi-Res button being hidden for a non-admin account, the auto-reload on your-server itself, a real phone, the MSI installer itself.


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

**Tested:** the Dashboard, Settings, Look and feel (accent change and saving) and the Devices popup (outside click, Escape, button) in the browser, the Android Downloads screen with sample entries (sections, badges, times, expanded stories), the Android Settings / Dashboard split on the emulator, the new server endpoint (401 without a token on your-server), unit tests, the release APK launching with no crashes or ANRs, the Windows app starting and its server stopping with it, your-server redeployed and healthy.
**Not tested:** the Android Downloads screen showing the server's log (your-server only just got the endpoint and its log is empty), a real running download appearing live in the new screens or under a song in the results, a non-admin account seeing the plain Settings, the cover fix on a real freshly downloaded song, a real phone, the MSI installer itself. Android has no "Look and feel" section (its Settings already had appearance and playback tweaks); that is the one gap against the web.


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

**Server side:** the file mover (`/probe`, `/ytdl`) and the yt-dlp container were updated on your-server; yt-dlp (YouTube) stays OFF until you switch it on in Settings.

**Tested:** the Downloads page with sample log entries at phone width (sections, badges, times, expanded step stories), the Info panel on a real song (Spectrum chip, Came from, size), `/probe` on a FLAC and an AAC file through the file mover (and its refusals), the build, your-server redeployed and healthy, the release APK launching without crashes or ANRs, the Windows app starting and its server stopping with it.
**Not tested:** the live progress line under a song in the search results while a real download runs (the code is in, I did not run a live download for it), the new top-bar colour on a wide desktop window, Android's Downloads screen (Android keeps its own list; only the "Streaming" wording changed there), a real phone, the MSI installer itself.


## FLACie 1.0 beta 14

_v1.0.0-beta14, 2026-10-05, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta14))

## FLACie 1.0 beta 14 (Android 1.0-beta14 / versionCode 28, Windows 0.14.0)

**New: a last-resort source for songs nothing else has (YouTube, via yt-dlp).** Popular songs that Soulseek, the free sources and Lidarr all miss can now be fetched: the server finds the song's official audio (YouTube "Topic" and official-audio uploads first, title, artist and length checked, live/cover/remix/sped-up uploads skipped) and saves it to your library with tags. It is a fourth entry in Settings > Open sources (web, Windows app and Android), in the same list as the Internet Archive, Audius and Jamendo, so you can put it first or last. **It is OFF until you switch it on**: it takes audio from YouTube, which is against YouTube's terms of service, and the file is AAC (about 130 kbps, not lossless).
- It runs in its own small container next to the file mover on your-server (`flacie-ytdl`), reached through the file mover so every device uses it the same way. yt-dlp updates itself whenever that container restarts.

Everything from beta 13 is included (Open sources settings: on/off, order, Jamendo client ID).

**Tested:** the service against real YouTube from your-server (a popular song, "Doja Cat - Say So" and "The Weeknd - Blinding Lights", fetched in about 5 s, valid AAC with the right tags and length), the full path through the app's download code and the public file-mover address (found; a made-up song fails cleanly), the Android call to the same route (live test), the file mover refusing requests without the key and rejecting path escapes, the web Settings row and the Android Settings row, unit tests, the release APK launching with no crashes or ANRs, the Windows app starting and its server stopping with it, your-server redeployed and healthy.
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

**Tested:** the web page at phone width against the real account (check the ID, reorder, save, reload: all kept), the Android section on the emulator (toggle, reorder, Jamendo ID, values stored), unit tests for the order/on-off rules and the live-API finders, the release APK launching with no crashes or ANRs, the Windows app starting and its server stopping with it, your-server redeployed and healthy.
**Not tested:** that the phone picks up a setting changed on the web (and the other way round) after a sync, a real phone, the MSI installer itself, an actual download that uses a reordered source.


## FLACie 1.0 beta 12

_v1.0.0-beta12, 2026-10-04, pre-release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta12))

## FLACie 1.0 beta 12 (Android 1.0-beta12 / versionCode 26, Windows 0.12.0)

*Assets re-uploaded after release with the Jamendo settings field (the version numbers did not change, so install over the earlier beta 12 if you already have it).*

**New: open download sources on every device.** "Download this" now also looks on the Internet Archive (only the Live Music Archive and netlabel collections), Audius, and Jamendo (only tracks whose artist allows downloads) while Soulseek searches. They only *find*; a file is downloaded from them only if Soulseek found nothing, and Lidarr stays the final fallback, so they cannot slow or block the main path. Files are filed through the file mover's new `/fetch` endpoint (live on your-server). Downloads show their source ("archive", "audius", "jamendo"). Songs only; albums still use Soulseek then Lidarr.
- Android, the Windows app (it hosts the same server code) and FLACie Web on your-server all have it.
- **Jamendo:** Android has Settings > Downloads > Open sources > "Jamendo client id" (checked against Jamendo before it is saved, synced with your account). On the web it is the `FLACIE_JAMENDO_CLIENT_ID` environment variable (already set on your-server).

Everything from beta 11 is included.

**Tested:** the finders against the live APIs from the web server code and the Android code (Audius and Internet Archive hits, Jamendo hit and clean misses), the file mover refusing loopback/http/path-escape/no-key requests, real Audius and Jamendo files fetched and filed through your-server's file mover, a real Audius download end to end through the web coordinator, the Jamendo field saving on the emulator, unit tests, the release APK launching on the emulator, the Windows app starting and its server stopping with it.
**Not tested:** an actual download started from the Android app or using Jamendo end to end, a real phone, the MSI installer itself, ntfy-host notifications.


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


