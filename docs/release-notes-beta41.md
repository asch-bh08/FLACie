Beta 41 (Android 56, MSI 0.41.0). Still a beta, not a 1.0 final. The Explore search upgrade you asked for, on web, Android and Windows. Test log: docs/TEST-LOG-beta40.md (last section).

**Explore search upgrade (web, Android and the Windows app)**
- Typo-tolerant search: "beyonse", "metalica", "daft pnuk" still find Beyoncé, Metallica and Daft Punk. Exact matches always win; only when nothing matches exactly does it show close matches, and it says so ("No exact match for ..., showing the closest matches"). Words of 3 letters or fewer stay exact.
- New filters, combinable with the old ones: **Codec** (FLAC, ALAC, WAV/PCM, AIFF, APE, WavPack, DSD, MP3, AAC/M4A, Ogg Vorbis, Opus, WMA), **Bit rate** (128 or lower, 129-192, 193-256, 257-320, above 320 kbps) and **Sample rate** now lists each rate (below 44.1, 44.1, 48, 88.2, 96, 176.4, 192 kHz and up). They use the real format of each file (as read by the server), so a song the server has not read yet may be missing while it is still reading.
- Web: the filters are a tidy grid of labelled fields (Sort next to the search box, Genre, Decade, Quality, Codec, Bit rate, Bit depth, Sample rate), collapsed under a "Filters" heading with an "N active" badge and "Clear all"; a bigger search box with a clear button. Search is left out of the side menu while you are in Explore (it has its own).
- Android: the same search and filters in Explore's Filters sheet (with removable chips), a taller rounded search field with an accent outline, and the close-matches notice.

**Also fixed (Android)**
- The "Local" / "Jellyfin" source pill is only shown when your library really has more than one source; with one source it was only squeezing song and artist names.
- Empty and error messages (for example when the chart can't be loaded) wrap and centre instead of running off both edges, and the chart error now says what failed.

**Tested**
- Web against the real Jellyfin library (7,975 songs): typo searches, every codec, every bit-rate band and every sample rate (the bands add up to the whole library bar one song the server has not read), the new filter grid expanded and collapsed, Clear all, and a filter combination (typo search + decade + codec + bit rate). Deployed to your-server, /healthz ok.
- Android on the emulator, signed in to your Jellyfin account (Quick Connect approved by you): Explore (7,981 songs), typo search "beyonse" (139 Beyoncé songs), Filters sheet with Codec, Codec = MP3 combined with the search (13 songs, one active filter and a chip), Home shelves, Account, Devices, Download log, the Jam screen (opened, no Jam started), the full player with real format badges.
- Android unit tests, Android release build, Windows MSI build.

**Not tested**
- Charts and the download services on the emulator (it cannot resolve your Tailscale name your-server.example.ts.net); Downloads / Soulseek / Lidarr in the apps; starting or joining a Jam; remote control against another device; notifications (ntfy); Admin; a real phone or Fold; installing the MSI itself (the app inside it was run for beta 40).
- Bit rate, bit depth, sample rate, codec and Hi-Res filtering for songs that exist only on the device: they have no server-read format, so those filters do not match them.
