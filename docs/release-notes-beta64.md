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
