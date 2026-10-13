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
