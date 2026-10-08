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
