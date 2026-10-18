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
