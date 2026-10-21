# By-hand checks for the owner (things only a phone, a car or ears can judge)

Each beta adds its items; tick them off on the Fold. Nothing here was verified on a device unless it says so.

## Beta 66
- [ ] **Type in the search box** (the top one, Explore, and the Downloads filter): do letters still get lost or swapped?
- [ ] A touch iPod (touch 5, nano 7): does it feel like an iPod OS (home screen, Music tabs, Now Playing, Settings, Clock, Stopwatch)? Drag a list with the mouse, use the Home button.
- [ ] On the wheel iPods: Extras (Clock, Stopwatch), and the stars on Now Playing (press the middle button twice).
- [ ] The iPod page on your own screen at the sizes you use (also a phone-sized window): anything cut off or overlapping?

## Beta 65
- [ ] iPod mode on your own screen: does it fill the window in both looks, and is the panel (cover, model, colour, look) clear? Do the covers show in the lists, the preview pane, Now Playing and Cover Flow?
- [ ] Android: Cover Flow on a nano / classic 6-7 skin with a real library.

## Beta 64
- [ ] iPod mode on the web / Windows: try a few of the 23 models and colours (account menu > iPod mode, or the sliders button): the mono LCD of the early iPods, the colour video menus, the nano/classic screens, a touch model. Do they look like the real thing to you?
- [ ] The wheel with a finger: speed settings (Settings > Controls), the flick, hold next/previous to seek, hold MENU, the clicker sound and haptics (Controls > Clicker), Cover Flow.
- [ ] Windows: the iPod page inside the installed app.

## Beta 63
- [ ] Stars: rate a song (button beside the heart), then look at it in Jellyfin (the song's rating should be stars x 2). Rate on the phone, open the web: same stars.
- [ ] Lock screen / Bluetooth / car: the Up next list shows the queue around the song; next, previous and picking a song work; with a 10,000-song queue nothing hangs.
- [ ] Cover transition: open Now Playing from the mini player, and close it again; the cover should fly, not jump.
- [ ] iPod mode on the web / Windows (account menu > iPod mode): wheel feel by finger and by mouse, keys, volume on Now Playing. Black model: add ?c=black.
- [ ] Android iPod Classic: Cover Flow (Cover Flow in the main menu), scrollbar, scrub diamond (press the middle on Now Playing).
- [ ] Chromecast: decide how a receiver may fetch songs (docs/spec/chromecast.md).

## Beta 57 / 58
- [ ] Seek bar feel: drag, tap, release. The knob should stay where you let go (no jump back), the time under it follows your finger.
- [ ] Skip feel: press Next several times quickly. The next cover should show at once, not the placeholder note.
- [ ] "Out of Space" (The Prodigy) now shows a cover (play it fresh: a copy already in a restored queue keeps old data until played again).
- [ ] Info tab graph (spectrograph): smooth while a new song starts; no stutter when skipping. Close the tab: no extra battery use.
- [ ] Bluetooth head unit / earbuds / Android Auto / lock screen with the 10,000-song queue (beta 59 hides the queue from the session: it should no longer run out of memory; the head unit now sees one song, no queue list).
- [ ] Anything audible: gapless, Hi-Res, EQ.

## Beta 60
- [ ] Speed: pick 1.5x on a song, skip and come back: the 1.5x is remembered for that song only. On an audiobook/podcast the next one starts at the speed you used last. Listen for artefacts at 0.5x and 3x.
- [ ] Spoken word / long items: "Back 10 s" / "Forward 30 s"; stop a 30-minute mix, kill the app, play it again: it should carry on near where you stopped.
- [ ] Sleep timer: "End of this song" fades and pauses at the end; "End of this album"; minutes with the 30 s fade; "Add 15 minutes"; shake the phone in the last two minutes (there is a toast).
- [ ] Notification / lock screen / Bluetooth: favourite and speed buttons work and the heart/label follow.
- [ ] Offline: download a song and an album's worth, put the phone in flight mode, play them; switch Wi-Fi only off/on; "Keep favourites offline" with a small limit; remove one; remove all. Watch the Jellyfin server load while downloading (should be one song at a time).
- [ ] Seek bar (new look): drag along the whole bar: the ticks, the pill under your finger, the bar growing; tap-to-seek; TalkBack reading "Seek ..." and the Back 10 / Forward 30 actions; the play button morph.
- [ ] iPod wheel: flick fast and let go: it should coast and stop smoothly; touch to stop it.

## Beta 61
- [ ] Even out volume (Android Settings, web Volume): does your Jellyfin give a gain at all? Turn it on, play a loud and a quiet song one after the other: they should sound closer in level. Check there is no pumping or clipping at the +6 dB end.
- [ ] MusicBrainz match (web Info tab): press Find on a few songs (a well-known one, a live one, an obscure one); check the best match comes first; choose one, reload: it is still there; Forget this match.

## Beta 62
- [ ] Android Auto / car: in the car (or the Desktop Head Unit) open FLACie: the six folders, a page of songs with covers, play from Favourites (the list queues), search by voice ("play ..."), the favourite and speed buttons. Check covers load (they come from the app's content provider).
- [ ] Fade between songs (Android Settings / web Fade): set 4 s; skip between two albums: the end fades and the next starts soft; play an album straight through: no fade at the joins.
- [ ] MusicBrainz match + cover (Android song menu, web Info tab): pick a match, use the Cover Art Archive cover, check it shows (Android: after leaving the screen), then use the original again.

## Betas 74 to 79 (things the emulator and the browser tests can't show)
- [ ] Retro on your phone: it opens on the song playing (Now Playing), your playlists are under Playlists, lyrics show for a well-known song, Shuffle/Repeat set on the iPod change the app's (and the other way round).
- [ ] Seek frame on your phone with a song not played before: the bars fill in round the cover as the song downloads (not all at once at the end).
- [ ] Sign-in on a fresh install at home: your Jellyfin server is offered under "Found on this network"; tapping it fills the address.
- [ ] Web after a server update while a song plays: the page shows Reconnecting, reloads by itself, and the song carries on (a browser that blocks autoplay may leave it paused: say if so).
- [ ] Windows app: the keyboard's play/pause, next and previous keys work, and Windows' media panel (volume flyout) shows the song with working buttons.
- [ ] TalkBack on Android: every button on Home, Search, Library, Devices, a song list and the player reads a name (the a11y-android.mjs check finds none unnamed on the emulator).
- [ ] Choosing several songs: on the phone (Select in a song's menu) and on a PC (Shift-click), add them to a playlist; check the playlist on the other device.
