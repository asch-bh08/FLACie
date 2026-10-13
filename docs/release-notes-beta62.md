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

**Changed (web): the speed, sleep timer, volume and fade controls are redesigned.** They were a row of labelled form boxes; they are now three small pills under the player (Speed, Sleep timer, Sound), each opening a short menu above it (speeds as a grid; Sound holds "Even out volume" and the fade choices). Skip back 10 / forward 30 are small pills in the same row when they apply.

**Small:** shuffle and repeat show a dot under the icon when on (not colour alone).

**Tested** (this build)
- See the end of this file.

**Not tested**
- Nothing on a phone or in a car. The fade was tested by its rules (unit tests) and on the web in the browser; not by ear on Android. The Cover Art Archive and MusicBrainz were tested against stand-ins (and the one real call is yours to try).
- **Not built (decisions, not oversights):** Chromecast (needs a Cast device to check and a change to how the player is swapped; the theme the Cast button needs is not the app's), ratings (stars; Jellyfin's rating write is unchecked), the shared-element cover transition, the iPod LCD renderer / Cover Flow, and a queue window for the system media session (browsing covers the head-unit case).
