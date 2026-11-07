# Overnight 6–7 Nov: everything works, every platform

The owner asked for all of FLACie to be tested as if nothing were fixed: phone, car (Android Auto), web, with fixes and a beta by morning.

## Log
- 23:10 Weekly usage 62% at the start. New end-to-end tests, because the emulator smoke never touched playlists or the car:
  - `tools/loadtest/playlists-android.mjs`: the phone's Play / Next / Shuffle on playlists (and one whose songs moved), then runs
  - `android/app/src/androidTest/.../CarPlaylistTest.kt`: a stand-in Android Auto browses Playlists and presses Play all, Shuffle, a song in the list, another playlist, the same playlist again; checks every song that plays is the playlist's.
- 00:07 **Found and fixed: Shuffle on a playlist left it for recommendations (phone and car).** The new test reproduced it: Shuffle on a 10-song playlist bounced between two songs, then Next played songs from outside it. Cause: `play()` set the shuffle order before loading the new songs; ExoPlayer then made its own random order for them with the first song anywhere in it, so the songs before it never played, the queue "ended" after a few, and Autoplay's songs came next. Fix: `QueueShuffle.start` (chosen song first, then every other one shuffled), applied after the songs are loaded in `play()` and `playFrom()`; a restored queue without a saved order gets `QueueShuffle.on`. Unit test + the end-to-end test now checks every song plays once. Result: 23/23 (phone Play/Next/Shuffle, a playlist whose files moved, and the stand-in car's 5 tests).
