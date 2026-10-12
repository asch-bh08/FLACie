Beta 54 (Android 69, MSI 0.54.0, FLACie Web 1.0-beta54). Still a beta, not a 1.0 final.

**New downloads are labelled the same everywhere, and you can see what is downloading (Android, web, Windows)**
- **One wording.** A song that was just downloaded and is played from your server before Jellyfin has scanned it says **"Streaming from new download"** (web: Now Playing source pill, "Playing from" line and Info; Android: the same). The list badge says **"New download"** (it used to say "Streaming"). The web's green "your server (new download)" pill is gone; the pill is quiet grey on both.
- **A status line under the artist**, in the player card, Now Playing, the queue / Up next, every autoplay song and every list that shows songs (Explore, albums, artists, genres, playlists, favourites, search, Home):
  - **"Downloading · Searching Soulseek..."** (the live stage; a stage that already starts with "Downloading", such as "Downloading 55%...", is shown as it is) while a song is being fetched,
  - **"Recently downloaded · 1s ago"** (ticks live: just now, Ns ago, N min ago) for **60 minutes** after it finished, then it disappears: a song downloaded a week ago says nothing,
  - **"Couldn't download"** for a failed fetch, for the same 60 minutes.
  In narrow places (cards, the phone's Up next pane, the web player bar on a narrow window) it reads "Downloaded · 4m ago"; the age is never the part that is cut. Colours are the same on both: green for recently downloaded, quiet grey for downloading and for "New download", reddish for failed; none follows your accent colour.
- **Autoplay songs are marked, including ones still downloading.** On the web and on Android, "Autoplay · up next after this" lists the songs being fetched ahead as rows with "Downloading · <stage>", and each becomes a normal queue row with "Recently downloaded · Ns ago" when it lands. Those pending rows are display-only (no tap, no remove).
- **A song older than 60 minutes that Jellyfin has not scanned yet** keeps a small "New download" marker (on the phone's list rows, whose width hides the badge).

**Fixed: a download made on the phone was invisible to the web**
- Beta 53 and older: you downloaded "P" by JME on the phone (it played as streaming), then searched for it on the web and it was not there, so you downloaded it again. The phone's download never reached the web server.
- Now the phone **reports every finished download to FLACie Web** (when a FLACie Web address is set; Jellyfin stays the only required thing): `POST /api/downloaded`. The web then shows it as recently downloaded, adds it to your session as a playable new download, offers "In your library" instead of Download in Search (it does not download it a second time), and asks Jellyfin for a scan. The server only accepts a file location that starts with your own file mover address, never fetches anything from what the phone sends, limits reports to 200 an hour per user and clamps the reported time to the last 10 minutes.
- The Downloads page lists such a song as "Downloaded on your phone".
- Not done: a download made on the web does not get the "recently downloaded" tag on the phone (the phone would have to read the web's download list).

**Also**
- Web: Now Playing shows a song's file facts (Hi-Res badge, sampling, kbps) only for the song they were read for, never the previous song's for a moment after a skip.
- Version numbers: Android 1.0-beta54 (69), Windows 0.54.0, FLACie Web 1.0-beta54.

**Tested** (this build)
- .NET unit tests: 250 pass (187 in beta 53). Browser tests: 133 pass (119 in beta 53), including a new spec with a stand-in slskd and file mover that drives a real download through the dashboard and checks the tag in the player card, Up next, a list and the autoplay lists, a 2-hour-old and a week-old record showing no tag, the phone report (401, 400, 413, path rules, 429), Search turning into "In your library", and the narrow form. Android unit tests: 280 run, 2 skipped (as before), 0 failed (236 in beta 53).
- Emulator, release APK, against the mock Jellyfin and a stand-in download stack (the owner's case "P" by JME plus autoplay songs): the status line and its ticking in the mini player, Now Playing, Up next and lists; "Streaming from new download"; autoplay pending rows turning into normal rows without a duplicate; the 60-minute rule with the device clock moved ahead; colours with the pink and the blue accent; the Home cards and the narrow Up next pane; 12 downloads gave 12 reports, none twice; no crash, no ANR; the small 720x720 screen shape without overflow (`docs/test-run/J-android-verify.md`).
- FLACie Web 1.0-beta54 deployed to the owner's server and checked in a browser (labels, live tag text); the new tags themselves were exercised on a local copy against the mock, not by creating a fake download on the real server.

**Not tested**
- A real phone, the Fold, the RG Rotate: only an emulator with a 720x720 shape.
- Lidarr, the open sources and yt-dlp downloads: only Soulseek was served by the stand-in.
- The Windows MSI was built but not installed; the Windows app embeds the same server code.
- The web's autoplay rows with the real online catalog (the specs use a stand-in).

**Known problems**
- On the phone's narrow Up next pane a pending row shows only "Downloading" until the download actually starts (the stage text does not fit).
- After the app is killed and restarted the restored queue keeps its tags but loses the "Autoplay · up next after this" divider.
- On the Android Up next pane the divider's text is slightly clipped at the top edge.
- Everything listed under beta 52 and 53 "Known problems" still applies (the web's early read of the next song uses a different URL from the real stream, the whole queue is exposed to Android's media session, read-ahead of whole songs on the phone, no Undo for web queue removal, `MEDIA_PLAY` in the background after losing audio focus).
