Beta 55 (Android 70, MSI 0.55.0, FLACie Web 1.0-beta55). A small follow-up to beta 54. Still a beta, not a 1.0 final.

**Changed: a new download's source reads just "Streaming", in blue (Android, web, Windows)**
- A song that was just downloaded and is played straight from your server before Jellyfin has scanned it used to say "Streaming from new download" (the source pill, the Info panel, the "Playing from" line) and "New download" (the list badge). All of those now say **"Streaming"**, in a fixed **blue** (not the accent colour): the list badge, the Now Playing pill, the Info source line, and the small marker a phone row keeps for such a song after the 60-minute window.
- Unchanged from beta 54: the status line under the artist ("Downloading · ..." in grey, "Recently downloaded · 1s ago" in green for 60 minutes, "Couldn't download" in red), the autoplay rows, and the phone's report of finished downloads to FLACie Web.
- Beta 54 had a rule that the word "Streaming" alone was never a badge; a test asserted it. That test now asserts the reverse (only a new download is called "Streaming"); no other test was changed or removed.

**Tested** (this build)
- .NET unit tests: 250 pass. Browser tests: 133 pass (the download-tags spec now also checks the badge text and its blue colour). Android unit tests pass; the release APK is signed with the owner's key (versionCode 70).
- FLACie Web 1.0-beta55 deployed to the owner's server; the live stylesheet gives the badge and the Now Playing pill `rgb(109, 179, 255)` on `rgb(18, 35, 59)`.

**Not tested**
- The phone on a device or the emulator for this change: the new colour and word are constants covered by unit tests and a compile, not looked at on screen (the beta 54 emulator run covered the layout they sit in).
- The Windows MSI was built, not installed.
- Everything in beta 54's "Not tested" and "Known problems" still applies.
