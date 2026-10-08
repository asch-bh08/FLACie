# Clean-machine install test, 2026-10-08

Method: only `docs/SELF-HOSTING.md` was followed, on a fresh Docker host (Ubuntu 26.04 in WSL2, Docker 29.1.3) with empty data folders. Image: `ghcr.io/asch-bh08/flacie-web:latest` (build of commit 5f08979, pulled from GHCR). Test Jellyfin: 12 FLAC files made with ffmpeg; Jellyfin 12.2.0 (`:latest`) and 10.10.7. Browser steps were driven with Playwright, the phone step on the Android emulator with the release APK (1.0-beta44).

| # | Step | Result | Docs accurate? |
|---|---|---|---|
| 1 | Jellyfin only: compose from the docs, password sign-in, Quick Connect, play, playlist, Info panel, Downloads | **Pass** on the web (Jellyfin 12.2 and 10.10). Password and Quick Connect sign-in, 12 songs listed, playback runs, playlist created and a song added, Info shows FLAC / 16-bit / 44.1 kHz / Came from, Downloads page opens | Mostly: said nothing about which address the container can reach, nor Jellyfin versions (fixed) |
| 2 | NAS only (`FLACIE_ALLOW_NAS_LOGIN=true`), Samba share | **Pass.** NAS tab appears only when enabled; 12 songs, 4 albums, playback runs | Missing: NAS-only admin needs `FLACIE_ADMINS: "user@host"`, otherwise the dashboard says "ask the person who runs FLACie" (fixed; the `user@host` form verified) |
| 3 | Non-admin account | **Pass.** `/dashboard` and `/admin` show the admins-only message, no Dashboard link, Downloads has no "Everyone's downloads", `POST /api/settings` and `/api/charts/run` return 403 (admin: 200) | Accurate |
| 4 | HTTPS behind Caddy (docs' Caddyfile with `tls internal`) | **Pass.** Sign-in stays on https, cookie Secure + HttpOnly + SameSite=Lax, HSTS sent. Default trust: a forged `X-Forwarded-For` from another container on the Docker network **is believed** (private network, as documented). With `FLACIE_TRUSTED_PROXIES` set to Caddy's address, a forged header from any other container is ignored, from Caddy honoured, and a forged header sent through Caddy is ignored. Rate limit: after 10 wrong tries from one client the correct password was refused for that client, while another client address (a different container) signed in fine; the throttle keys on the real client address, not the proxy | Accurate |
| 5 | Upgrade, backup and restore | **Pass.** Started on `sha-0e40f89` (beta 44 build), signed in; `docker compose pull && up -d` on `latest`; the same cookie still worked and the playlist was still there. Stop, `tar`, wipe `flacie-data`, restore: still signed in, same data, `keys/` and `users/` mode 700 | Tag example `1.0.0` did not exist (fixed to `1.0.0-beta44` and `sha-` tags) |
| 6 | Android release APK on the emulator | **Pass with Jellyfin 10.10.7; fails on Jellyfin 12.2.** Install, permission prompts, Quick Connect sign-in and playback of a Jellyfin song work on 10.10.7. On 12.2 "Use my account" fails with HTTP 401 | Missing: the music source must be switched on separately (Settings > Music sources > Jellyfin > Use my account) (fixed); the emulator reaches the host at `10.0.2.2`. Connecting the app to the FLACie Web server itself was not tested |

## Code bugs found (1–3 fixed in beta 45, see CHANGELOG; 4–5 still open)

1. **Jellyfin 12.x compatibility (high).** Jellyfin 12.2 rejects the `X-Emby-Token` header and the `api_key` query parameter for user tokens (HTTP 401); it accepts the `Authorization: MediaBrowser ... Token="..."` header (and an `ApiKey` query). Effects:
   - Android: every direct Jellyfin call that sets `X-Emby-Token` (`JellyfinDirectClient.kt`, `Library.kt:102`, `Lyrics.kt:61`) fails; "Use my account" returns 401, so the Jellyfin library never loads. Also `JellyfinConnect.kt:182` (`/socket?api_key=`).
   - Web: `JellyfinClient.SocketUri` uses `/socket?api_key=...`; Jellyfin logs "Token is required" for `GET /socket` in a loop every ~1 second, so Connect, remote control and Jams do not work. Everything else on the web works.
2. **Account lock-out by anyone (low/medium).** The sign-in throttle also counts failures per user name, so anyone who can reach the login can lock out a real user (for example `admin`) for 15 minutes with 8 wrong guesses, from any address.
3. **Android sign-in does not switch the music source on (UX).** After signing in on first start the Home screen shows only on-device music until the user finds Settings > Music sources > Jellyfin > Use my account.
4. **First-run order of the permission prompts** (notifications, then audio) is shown before sign-in, with no explanation; minor.
5. Playlist creation on the web: the "Create" control on the Playlists page is not a button (no accessible name "New playlist"); it opens an inline field. Minor accessibility note.

## Not tested

- A second Android device, the Android app talking to FLACie Web, Jams and Connect between two devices (blocked by bug 1), Jellyfin behind a real public hostname with a real certificate, arm64 image, the Windows installer on a clean PC, Downloads actually fetching (no Soulseek/Lidarr in the test).
