Beta 61 (Android 76, MSI 0.61.0, FLACie Web 1.0-beta61). Still a beta, not a 1.0 final.

**New: even out volume (Android and web)**
- Jellyfin measures each song's loudness when it scans the library and gives the gain that brings it to a common level. With **Settings > Even out volume** (Android) or **Volume > Even out** in the full-screen player (web), loud songs are turned down (up to 15 dB) and quiet ones up (up to 6 dB, never past full volume). Off by default. If your Jellyfin gave no gain for a song, it plays as it is.
- Not checked against your real Jellyfin: whether it actually sends `NormalizationGain` for your songs is the first thing to look at (turn it on and see if loud and quiet songs even out).

**New: MusicBrainz match (web)**
- Info tab > MusicBrainz > **Find on MusicBrainz** lists the recordings MusicBrainz knows for the song, best fit first with a percentage; **Use this match** keeps your choice beside the library and links to it. Nothing is asked until you press the button (not at launch, not when a song or the Info tab opens). MusicBrainz is asked at most once a second on its own gate, with a User-Agent that names FLACie and its GitHub page (`FLACIE_MB_CONTACT` changes the contact), and answers are kept on disk (30 days; "nothing found" 7 days).
- It never changes your files, your tags or Jellyfin. Not built yet: the cover picker (Cover Art Archive), the same on Android.

**Tested** (this build)
- Android unit tests: 372 run, 2 skipped (as before), 0 failed. .NET tests: 289 pass (19 new: MusicBrainz client/gate/cache/matching, loudness). Browser tests: 138 pass (2 new: volume levelling, MusicBrainz match).
- Release APK versionCode 76, signed with the owner's key (SHA-256 6a4eb335...97a106); installed over a clean emulator, signed in to the mock with Quick Connect, played, skipped, killed and restarted (queue restored), 0 crash lines. Covers: 100 % of visible rows by 10 s in all three runs; by 3 s in two of three (the emulator started right after the MSI build and was slow in one run).
- The MusicBrainz test uses a stand-in for MusicBrainz and checks: no request before the button, the User-Agent text, the query, the order of results, that the choice survives a reload, and that asking again is answered from the cache.
- The MSI (0.61.0) was built, not installed. FLACie Web was deployed (see the notes on the release).

**Not tested**
- Nothing on a phone. The loudness gain was only tested with the mock (a gain of -6 dB lowers the audio element's volume by half) and unit tests; MusicBrainz was tested against a stand-in, not the real service.
- Everything in beta 60's "Not tested" still applies. The MSI is built, not installed.
