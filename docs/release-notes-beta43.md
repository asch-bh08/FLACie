Beta 43 (Android 58, MSI 0.43.0). Still a beta, not a 1.0 final. Mostly web work, which the Windows app contains; the Android app has no feature change (version bumped to keep the three in step). Tested / not-tested lists below. Notes: docs/TODO-beta40-explore-search.md and docs/NEW-SESSION-START-HERE.md.

**Explore opens on a new Overview tab (web, Windows)**
- Explore is now Overview, Songs, Albums, Artists, Genres, Charts, and the side menu's Explore goes to Overview. It is stacked and full width: a Top result banner (artist, album or song, with a 188 px cover and the Jellyfin / FLAC / bit depth and sample rate / kbps badges), Songs as a list with 48 px thumbnails, Albums as a row of big square covers (title, artist, year), Artists as round avatars ("Artist - N songs"), then Playlists and Genres chips. With nothing typed it browses: newest songs and albums and your biggest artists.
- One compact bar: search, Sort (Best match, Title, Artist, Recently added, Newest year, Longest) and "Filters (N active)", with removable pills under it; the pills narrow every section. "See all N songs" opens the flat Songs tab with the same search and filters. The flat table stays on the Songs tab only.
- Search uses the same stacked layout, with the "More music" downloads below.
- The Top result banner: clicking the banner plays the song and opens Now Playing; the round play button only plays (and becomes pause while it plays); neither restarts a song that is already playing (the button pauses / resumes, the banner just opens the player). The cover no longer overlaps the title.

**Fixed: empty playlists, recommendations and admin after signing in (web)**
- The web server kept one session per Jellyfin user holding the token of whichever sign-in reached it first. After a redeploy an old browser token (already dropped by Jellyfin) came first, the profile load failed, and a later sign-in reused that session, so playlists, recommendations and the admin check stayed empty and Jellyfin was asked with a dead token every few seconds. A fresh sign-in now replaces the cached session, and a session Jellyfin refuses is no longer kept.

**Public address**
- `https://frank.tailb05910.ts.net:8443` stopped working off the tailnet because Tailscale is not publishing a public DNS name for frank (the funnel itself is configured correctly; it needs a look in the Tailscale admin console). The web app is now also published through the Caddy on hms-jellyfin, which does have a public name: **https://hms-jellyfin.tailb05910.ts.net:10000**, and **https://hms-jellyfin.tailb05910.ts.net/flacie** redirects to it. The app treats any `*.ts.net` address as https. The frank name still works on devices running Tailscale. A phone app that uses the frank address keeps working while the phone runs Tailscale.

**Also**: the Bit rate filter bands, typo-tolerant search and the one-row filter bar with the Audio specs drawer and new dropdowns from beta 41 and 42 are in the Windows installer too.

**Tested**
- Web, in the browser against your real library: the Overview in browse mode and for an artist, an album-style and a song search, the banner click and play button (play, pause, resume without restart, banner click while playing), "See all" to the Songs tab with the filters kept, a filter narrowing the page, Search using the same layout, sizes (188 px cover, 48 px thumbnails), and the cover-overlap fix. The public addresses answer over the real public path (health check, login page, /flacie redirect, the live-update channel), and Jellyfin on 443 and Jellyseerr on 8443 still answer after the Caddy changes. Frank runs this web code; after the session fix its profile requests succeed with no 401s.
- Windows: installer build and app smoke test (starts, /healthz ok, closing it stops the server).
- Android: unit tests and the release build; no code change since beta 42.

**Not tested / not done**
- The new session fix was checked by its effect on your live session; a deliberate dead-token sign-in was not reproduced.
- The Overview on phone widths; a search that matches a playlist name; the Windows app signed in; installing the MSI itself.
- The Android app has no Overview tab and keeps its own Explore; the Android bit-rate band results against real server data (the emulator cannot reach FLACie Web); Charts and downloads on the emulator; Jams; notifications (ntfy); a real phone or Fold.
