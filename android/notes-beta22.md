FLACie 1.0 beta 22 (Android 36, Windows 0.22.0)

- Playlists you add to on the phone now show up on the web (and the other way round). The web used to read your profile once at sign-in and never look again, and its next save could overwrite the phone's change. It now checks every 20 seconds and merges (newest edit per playlist wins, deletions and favourites carried over).
- Android playlists can be edited: Rename, Delete, Remove dupes (same song, not just same file) on the playlist page; Move up / down / to top and Remove (with Undo) on each song. Adding a song that is already in a playlist (same song, even another copy) says so instead of adding it twice.
- Android Explore redesigned: tabs, one filter box, a Filters button (genre, decade, quality, bit depth, sample rate in one sheet) and Sort. Active filters show as removable chips.
- Android Info graphs no longer get cut off: the graph fits the room, the chips scroll sideways, and the text is shorter.
- Android Settings: Dashboard is the first thing in Settings for administrators, and the Dashboard shows library quality (Hi-Res / lossless / lossy from the real file data) and how the last downloads went per source.
- The remote bar only appears for a device that is actually playing.

Tested: Android emulator (Explore, Filters sheet, playlist page, Info graph, remote bar), web build, the playlist merge compiles and runs on the server. Not tested: the phone-to-web playlist merge against your real phone, Remove dupes / Move / Rename (they change your real playlists), Windows installer.
