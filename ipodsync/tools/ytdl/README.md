# flacie-ytdl (last-resort YouTube source)

A small container on frank that finds a song on YouTube with yt-dlp and files the audio into the music folder. Off in the apps until the user switches
"YouTube (yt-dlp)" on in Settings > Open sources. Taking audio from YouTube is against YouTube's terms of service.

- Files: `Dockerfile`, `server.py` (copied to `/home/hms/docker/flacie-ytdl/` on frank).
- Run: `docker build -t flacie-ytdl:local . && docker run -d --name flacie-ytdl --restart unless-stopped -p 172.17.0.1:8091:8091 -v /media/Movies:/data -v /home/hms/docker/filemove/apikey.txt:/key:ro flacie-ytdl:local`
  (published on the docker bridge address only, so the file mover container can reach it and the LAN cannot; it uses the file mover's API key).
- Clients never talk to it directly: the file mover has `POST /ytdl` which forwards here (`ipodsync/tools/filemove/server.py`, `YTDL_URL`).
- API: `POST /ytdl {"artist","title","durationSec","to":"Folder/Artist/Artist - Title"}` writes `<to>.m4a|opus` and answers `{"ok":true,"ext":"m4a","durationSec":..}`; 404 when no upload matches (title, artist and length within 8 s checked; "Topic"/official-audio uploads first; live/cover/remix/karaoke/sped-up uploads skipped).
- yt-dlp is upgraded every time the container starts (`docker restart flacie-ytdl`); YouTube breaks extractors often, restart first when it starts failing.
