Beta 49 (Android 64, MSI 0.49.0). Still a beta, not a 1.0 final.

**Notifications: "someone is listening" and "someone downloaded" (web, Windows, Android)**
- **Music played, live.** With notifications on (Settings > Notifications, an ntfy server and topic), FLACie announces each song a person really listens to, as it starts: the user, the song, the playlist or album it plays from, and the device. A song counts after 20 seconds of listening, so skipping through songs says nothing; a repeat of the same song within a minute counts once. After ten quiet minutes there is one "finished" summary (tracks and minutes). Every song has its own listening clock, so a long track played after a short one is never credited to it.
- **No doubles.** A play that goes through Jellyfin is announced by FLACie Web only when the Jellyfin side of your notification setup ignores the FLACie Web device (the new toggle "Also plays through Jellyfin" is on by default; turn it off if your Jellyfin watcher already announces those plays). Plays from the NAS or from a fresh download that Jellyfin has not scanned yet are always announced by FLACie, because nothing else sees them.
- **Android** reports NAS and fresh-download plays to FLACie Web (start, a heartbeat every ten seconds, stop), so those are announced too. Plays of Jellyfin songs on the phone are left to the Jellyfin side, as before.
- **Downloads.** "<user> downloaded <song>" is sent live for what a person fetched with FLACie; a big burst becomes one summary ("47 more": the first three are live, the rest are one message after a quiet minute). Nothing is sent when nothing downloads.
- Every FLACie message now starts with "FLACie ·" so it is easy to tell from the messages of other tools. A refused token reads "ntfy rejected the token (needs write access to this topic)". New toggles: "Someone plays music", "Someone downloads music".
- The access token is stored encrypted, never written to logs or shown again.

**Web fixes**
- **Skipping a song could freeze the page until a reload.** The Info panel read a value that the skip had just cleared, and the error ended the whole page connection. Fixed, and that code can no longer end the connection.
- **Missing or hanging covers.** A cover lookup that stalled held its slot for ever and held up the other covers; every lookup now has a 15 second limit. A cover that was not found used to be remembered by the browser for a week, so a fixed cover stayed missing; a miss is now remembered for 30 seconds. A cover search that matched neither the album nor the song no longer takes another release's artwork (no cover is better than a wrong one).

**Also in this build (from betas 46 and 47, if you have not updated since):** the headphone test (tone and frequency sweep, 1 Hz to 44 kHz, with a level that is kept safe) on Android and web, and the mini player on web.

**Tested**
- 20 new unit tests (.NET): a Jellyfin-routed play says nothing when switched off; a NAS run announces each song once and one summary after ten idle minutes; skipping through 40 songs a second each says nothing; a replay within 60 seconds counts once; two users have independent runs; a short song then a 20 minute one is credited its own length; a refused token reads as above; an unreachable ntfy server does not delay the caller and the token is not in the log or the stored file; downloads are live then summarised.
- Web: all browser tests pass (37). Android: unit tests pass, the release APK builds and is signed with the same key as beta 48.
- The web fixes and notification settings were deployed to the owner's server before this release; a test message from the settings page arrived on a phone.

**Not tested**
- A real song played from a NAS or an unscanned download, end to end to a phone (the logic is covered by unit tests; no NAS was used).
- The Android reporting on a real phone, and the MSI install.
- The cover fixes were reviewed and built but not measured against a large library; the freeze was reproduced from the server log, not on screen.
- Listening to the headphone test with real headphones.
