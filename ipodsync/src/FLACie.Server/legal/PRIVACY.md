# Privacy

This notice describes FLACie 1.0: FLACie Web, the Android apps and the Windows app. It was checked against the code on 31 October 2026.

## Who is responsible

FLACie is software you run yourself. There is no FLACie company, cloud service or FLACie account.

- **The FLACie project** (the people who write the code) runs no servers for the apps. The apps send nothing to the project: no analytics, no crash
  reports, no adverts, no tracking and no update checks.
- **Whoever runs a FLACie server** (the "host") chooses which features are on and keeps everything listed below on their own machine. Questions about your
  data on a server go to its host.

If you host FLACie only for yourself and your household, data protection law (UK GDPR) probably does not apply to you: it has an exemption for purely
household use. If you host it for other people, you may be the data controller for their data. Use this file as the start of your own notice, and add
your name and how people can reach you.

## What a FLACie server keeps

Everything is in the server's data folder (`/data` in Docker, `%LOCALAPPDATA%\FLACie\web` for the Windows app). Accounts are filed under a scrambled id
(a hash), not under user names.

| What | Where | How long |
|---|---|---|
| The keys that protect sign-ins | `keys/` | Until the host deletes them |
| Per person: AI DJ switches, background download and chart settings, playlist imports, and an encrypted copy of their Jellyfin sign-in (so daily jobs can run while they are away) | `users/<account>.json` | Until the host deletes it |
| Listening history: each song played, skipped, liked, disliked, or added to or taken out of a playlist, with the time, how much was heard, where it was played (phone, car, web, Windows, Alexa) and the listener's local hour | `listen/<account>.jsonl` | Plays older than a year are merged into daily totals. Nothing is deleted automatically. |
| Requests the DJ learned from you (the words of your own requests, the newest 300) | `listen/<account>.phrases.json` | The newest 300 |
| The download log: who asked for which song, and where it came from | `downloads/` | The newest 5,000 |
| Shared links: the song or playlist name, its songs' titles and artists, and the user name of whoever shared it | `shares/` | Until the host deletes them |
| Alexa (only if the host turns it on): the link between an Alexa account and a FLACie account, and the queue | `alexa.json`, `alexa-queues/` | Until you revoke it in Settings > Alexa or disable the skill |
| Caches: each person's library list, covers, lyrics, MusicBrainz answers, the DJ's voice clips | `library/`, `art/`, `lyrics/`, `mb-cache/`, `dj-clips/` | Rebuilt when needed; the DJ's clips are limited in size |
| Recent activity for the admin dashboard: sign-ins (user name and IP address), plays, downloads | Memory only | The last 300 events; gone when the server restarts |

Your browser keeps FLACie's sign-in cookie (30 days), the security cookies the pages need, and your look-and-feel choices in its own storage. There are
no advertising or tracking cookies.

**Your profile** (playlists, favourites, the last 200 songs played, preferences, your Jellyfin access token, and the addresses and keys of any download
services you set up) is not in the data folder. It is saved in your Jellyfin user settings, or as `.flacie/profile-<user>.json` on your NAS share, so it
follows you between devices. Jellyfin's administrators, and anyone who can read that NAS folder, can read it. See [SECURITY.md](SECURITY.md).

## What leaves the server, and when

FLACie never sends your user name or account id to the public look-up services below. They do see the address of the machine asking: the server's for
FLACie Web, the phone's for the Android app.

**Used as soon as FLACie runs:**

| Sent to | What is sent | Why |
|---|---|---|
| Your own Jellyfin, NAS or Plex server | Your sign-in and what you play | To play your music |
| LRCLIB (lrclib.net) | A song's title, artist, album and length | Lyrics |
| Deezer, Apple's iTunes Search API, MusicBrainz, the Cover Art Archive, ListenBrainz | Artist, song and album names, and the words you type in Search | Covers your server lacks, "More music" in Search, Autoplay and Quick picks |
| Apple's iTunes chart feed | Nothing about you | The Charts page |

**Only when the host, or you, switch them on:**

- **Download services** (Soulseek through slskd, Lidarr, the file mover, YouTube through yt-dlp, the Internet Archive, Jamendo, Audius): the song's artist,
  title, album and length. Soulseek searches are seen by other people on the Soulseek network. Downloaded songs are saved into the host's music library.
- **The AI DJ with Cloudflare Workers AI** (the host adds a Cloudflare account): to write a line, FLACie sends Cloudflare the song's details, your first
  name (taken from your user name), your playlist names, your most-played artists, requests you have made before, and the words you said to the DJ. To say
  the line, it sends the line's text. Cloudflare runs Meta's Llama models and Deepgram's voices for this. With FLACie's own voice (Kokoro, on the host's
  machine), speaking stays on the host's machine. FLACie never sends Cloudflare a recording of you.
- **Talking to the DJ** (the mic button): FLACie listens only from your press until you stop talking. Your browser's or phone's own speech recogniser turns
  the sound into words, and only the words reach FLACie. That recogniser belongs to your browser or phone maker, and some send the sound to their servers
  to do this (for example Chrome sends it to Google). On Android 12 and later FLACie asks for the phone's on-device recogniser when it has one.
- **The device's own voice for the DJ** (off by default): your phone's or browser's text-to-speech says the lines. Some browsers use online voices.
- **Alexa** (the host turns it on): Amazon turns your voice into text and sends FLACie the request. FLACie sends Amazon short-lived links to songs and covers,
  and "your download is ready" notifications with the song's title and artist.
- **Notifications** (the host sets up ntfy): sign-ins (user name and IP address), what someone is playing, finished downloads and imports go to the ntfy
  server the host chose. On the public ntfy.sh anyone who knows the topic name can read them, unless the topic is protected.

## The Android and Windows apps

- **Android** keeps on the phone: your library list, covers, lyrics, songs you download for offline listening, settings, recent searches, a copy of your
  profile, and listening events waiting to be sent to FLACie Web. Android's cloud backup is turned off for the FLACie app, so none of this goes into a
  Google backup (the separate FLACie Retro app still allows it). Signing out clears your favourites, playlists, play history, download-service settings
  and the waiting events from the phone. The app talks straight to your own servers and to the public look-ups above. It asks for the microphone only the
  first time you press the DJ mic.
- **Windows** runs FLACie Web on the PC itself (it only listens on `127.0.0.1`), so everything above applies, kept in `%LOCALAPPDATA%\FLACie\web`.
  Before it writes to an iPod, the iPod manager backs up the iPod's database on the PC.

## Your choices

- In the apps you can clear favourites, playlists, recent searches and caches, sign out, and revoke Alexa.
- There is no "delete everything about me" button yet. Ask the host: they can delete your files from the data folder (listed above) and your FLACie
  settings in Jellyfin.
- If you host FLACie for other people, tell them which optional features you have switched on, especially the AI DJ, Alexa, notifications and downloads.

## Changes

If a FLACie release changes what is kept or sent, this file changes with it. The history is in this repository.
