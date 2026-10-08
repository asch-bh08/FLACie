Beta 42 (Android 57, MSI 0.42.0). Still a beta, not a 1.0 final. Mostly web work (which the Windows app contains), plus the finer bit-rate filter on Android. Test log: docs/TEST-LOG-beta40.md (last section) and docs/TODO-beta40-explore-search.md.

**Bit rate filter: bands all the way up (web, Windows, Android)**
- "Above 320 kbps" held almost 5,000 of your songs, so it is now split: 321 to 700, 701 to 900, 901 to 1,000, 1,001 to 1,500, 1,501 to 2,000, 2,001 to 3,000, 3,001 to 4,000, 4,001 to 5,000 and above 5,000 kbps (the highest in your library is about 6,900). The list is grouped "Lossy range" (up to 320) and "Lossless range". Checked against your real library: every band's count matches the spread measured from the files, and together they add up to every song with a known bit rate.

**Explore filter bar (web, Windows)**
- One quiet row: search, Sort and "Filters (N active)". Chosen filters show as removable pills under it, with Clear all. Genre, Decade and Quality are the main filters; Codec, Bit rate, Bit depth and Sample rate are in an "Audio specs" drawer. Collapsing Filters hides the whole panel.
- New dropdowns (the browser's own lists can't do this): at most 240 px tall and scrolling, they open upwards when there is no room below, the chosen row is in the accent colour with a check, and Codec is grouped Lossless / Lossy / Hi-Res.
- The typo notice is soft grey with "Did you mean ...?" (click to fix the search); when nothing matches even loosely it says so.
- Explore song rows keep 16 px clear of the A to Z bar.

**Search results layout (web, Windows)**
- Sections like Spotify: a Top result card (an artist, album or song, with source, format, bit depth / sample rate and kbps badges, and a play button) beside the five best Songs ("Show all N songs"), then Artists (round, with song counts), Albums (cover, artist, year), Playlists and Genres, then the existing "More music" downloads. The same filter pills narrow every section, and the counts on the cards follow them.

**Also (from beta 41, now in the installer)**: typo-tolerant search and the Codec / Bit rate / Sample rate filters; every boolean aria-expanded / pressed in the web UI now says true or false.

**Tested**
- Web against your real library: every bit-rate band count; the filter bar, pills, drawer and dropdowns (height cap, flipping up in a 520 px window, grouped codec list); "Did you mean"; Search for an artist, an album, a song and a genre; the filters narrowing Search (Metallica: 100 songs became 7 with 1980s); wide and narrow layouts. Your server runs this web code.
- Android: unit tests pass and the release build works. On the emulator (signed in to your account) the Bit rate list shows all the new bands under Filters. The emulator cannot reach FLACie Web (it cannot resolve the Tailscale name), and Android takes bit rates from that server, so picking a band returned "Nothing matches" there: the band results on Android are not verified. The web bands are.
- Windows: installer build and app smoke test (starts, /healthz ok, closing stops the server).

**Not tested / not done**
- Android bit-rate band results against real server data (see above); Charts and the download services on the emulator (it cannot resolve the Tailscale name); starting or joining a Jam; notifications (ntfy); a real phone or Fold; installing the MSI itself.
- Search results on phone widths, and a search that matches a playlist name.
- The Android app does not have the sectioned (Spotify-style) search results or the pill-and-drawer filter bar; those are web and Windows only. Android keeps its own Filters sheet with chips.
- Public web: https://your-server.example.ts.net:8443 currently fails with "DNS_PROBE_FINISHED_NXDOMAIN" because Tailscale is not publishing the Funnel's public DNS name (not an app problem: it needs a look in the Tailscale admin console). On a device running Tailscale, http://100.x.y.z:5255 works.
