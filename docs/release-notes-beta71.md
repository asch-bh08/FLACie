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
