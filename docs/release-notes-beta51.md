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
