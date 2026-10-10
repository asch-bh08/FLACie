# Real Jellyfin test servers (Docker, run in WSL or any Linux)

Helpers used to test FLACie against **real** Jellyfin servers (10.10.7 and 12.2), not only the mock in `../web-smoke/mock-jellyfin.mjs`. Test accounts only: `admin` / `adminpw1`
(and `listener` / `listen123`) on throw-away containers. They are not secrets and mean nothing outside these test containers.

- `start-test-jellyfins.sh`: needs `docker`, `ffmpeg`, `jq`, `curl`. Makes 12 tagged FLAC files under `/root/ct/music`, starts Jellyfin 12.2 on port 8096 and 10.10.7 on port 8097, completes the
  setup wizard through the API, adds the music library, turns Quick Connect on, makes `admin` and `listener`.
- `add-alac-songs.sh`: adds Apple Lossless songs to that library (it expects some real ALAC `.m4a` files next to it; edit the paths at the top) and rescans.
- `probe-auth-forms.sh`: shows which auth forms each Jellyfin version accepts (`Authorization: MediaBrowser ... Token=`, `X-Emby-Token`, `api_key`, `ApiKey`, the socket).

The paths inside were written for the machine they were first run on (`/mnt/c/Users/.../Temp`); change them to wherever you put the files. Run the web server against them with
`FLACIE_JELLYFIN_URL=http://localhost:8096 FLACIE_DATA=/tmp/d dotnet FLACie.Server.dll --urls http://127.0.0.1:5301` and sign in as `admin`.
