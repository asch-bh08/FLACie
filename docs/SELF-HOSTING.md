# Hosting FLACie Web yourself

FLACie Web is one container. It plays music from **your** Jellyfin server or **your** NAS, and keeps each person's playlists, favourites and
settings in their own Jellyfin user settings (or in a `.flacie` folder on their NAS share). There is no FLACie account service and nothing is sent to
anyone else: you run the server, you own the data.

Contents: [Jellyfin only](#a-jellyfin-only) · [NAS only](#b-nas-only-no-jellyfin) · [HTTPS and reverse proxy](#https-and-reverse-proxy) ·
[Upgrading](#upgrading) · [Backups](#backups) · [Environment variables](#environment-variables) · [Troubleshooting](#troubleshooting) ·
[Security notes](../SECURITY.md)

You need Docker (with Compose) on any machine that can reach your Jellyfin or NAS. The image runs on amd64 and arm64 (Raspberry Pi 4/5 included).

## A. Jellyfin only

1. Make a folder and save this as `docker-compose.yml` in it (also in the repository as `ipodsync/src/FLACie.Server/docker-compose.example.yml`):

   ```yaml
   services:
     flacie:
       image: ghcr.io/asch-bh08/flacie-web:latest
       container_name: flacie
       restart: unless-stopped
       ports:
         - "8080:8080"
       volumes:
         - ./flacie-data:/data
       environment:
         FLACIE_JELLYFIN_URL: "https://jellyfin.example.com"   # your Jellyfin, as people reach it
   ```

2. `docker compose up -d`, then open `http://<this-machine>:8080`.
3. Sign in with a Jellyfin username and password, or press **Sign in with Quick Connect** and approve the code in any Jellyfin app
   (Settings > Quick Connect). Your Jellyfin music library appears; playlists and favourites are saved in your Jellyfin user settings.

Notes
- **`FLACIE_JELLYFIN_URL` is strongly recommended.** It locks the login to your Jellyfin. Without it the login page asks for a server address, so a
  visitor can make your FLACie server contact any address they type. Leave it out only on a private network.
- If FLACie can't reach Jellyfin at that address from inside Docker (for example the address is a public name your router won't loop back to),
  also set `FLACIE_JELLYFIN_INTERNAL_URL: "http://<jellyfin-lan-address>:8096"`. People still use the public address; only the server uses the internal one.
- **Who is an admin?** The admin pages (Dashboard, server settings, downloads) are for the administrators of the locked Jellyfin. To pick people
  yourself, set `FLACIE_ADMINS: "alice,bob"` (Jellyfin user names).
- The first library load takes a while on a large collection; the page shows "loading the library".

## B. NAS only (no Jellyfin)

FLACie can read music straight from an SMB/CIFS share (a Synology, QNAP, Samba or Windows share).

```yaml
services:
  flacie:
    image: ghcr.io/asch-bh08/flacie-web:latest
    restart: unless-stopped
    ports: ["8080:8080"]
    volumes: ["./flacie-data:/data"]
    environment:
      FLACIE_ALLOW_NAS_LOGIN: "true"      # NAS sign-in is OFF unless you turn it on
```

Open the site, choose the **NAS** tab and enter the NAS address (for example `nas.local`), the share name (`Music`), an optional folder inside it, and a
user name and password that can read the share. The profile is saved on the share as `.flacie/profile-<user>.json`.

- Use a NAS account with **read access to the music only**. The NAS password is kept in the encrypted sign-in cookie, in the server's memory, and in that
  profile file on the share (see [security notes](../SECURITY.md)).
- Turning NAS sign-in on lets a signed-in-or-not visitor make the server connect to an address they type. Only enable it on a private network, or behind
  something that already restricts who can open the page (a VPN such as Tailscale, or your proxy's authentication).
- Without Jellyfin there are no Jellyfin-only features: Connect (remote control between devices), Jams, and Jellyfin-reported audio facts.

You can set both `FLACIE_JELLYFIN_URL` and `FLACIE_ALLOW_NAS_LOGIN=true`; the login page then shows both tabs. An account that uses both keeps one profile.

## HTTPS and reverse proxy

Put FLACie behind a proxy that does HTTPS if anyone reaches it from outside your home network. The browser then talks HTTPS to the proxy and plain HTTP
goes to port 8080. **WebSockets must be passed through** (the page is live over a WebSocket; without it you see "Reconnecting…").

**Caddy** (automatic certificates):

```
flacie.example.com {
    reverse_proxy flacie:8080
}
```

**Nginx:**

```nginx
location / {
    proxy_pass http://flacie:8080;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $remote_addr;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_read_timeout 3600s;
}
```

**Traefik / Nginx Proxy Manager:** turn on WebSocket support and forward the usual `X-Forwarded-*` headers.
**Tailscale Funnel:** `tailscale funnel --bg --https=8443 http://127.0.0.1:8080`.

Rules of thumb
- Serve FLACie on its **own host name**, not a sub-path such as `/flacie`.
- **Trusted proxies.** FLACie believes `X-Forwarded-For/Proto/Host` only from loopback and private networks (10/8, 172.16/12, 192.168/16, 100.64/10,
  fc00::/7), never from the public internet, so a stranger who reaches port 8080 directly can't pretend to be another client. If your proxy has
  a public address, or you want to be strict, set `FLACIE_TRUSTED_PROXIES` to its address or range, for example `FLACIE_TRUSTED_PROXIES: "172.18.0.0/16"`.
  Without trusted forwarded headers the site still works but sees the proxy's address as the client, and the proxy's scheme as plain HTTP.
- **Don't publish port 8080 to the internet directly.** Bind it to the proxy only (`ports: ["127.0.0.1:8080:8080"]`, or no `ports` and put the proxy in the same Docker network).
- Sign-in only works over HTTPS (or plain HTTP on a trusted LAN). The cookie is HTTP-only, SameSite=Lax and "secure" whenever the page is served over HTTPS.

## Upgrading

```bash
docker compose pull
docker compose up -d
```

Your data lives in `./flacie-data` and is kept. Releases are tagged (`ghcr.io/asch-bh08/flacie-web:1.0.0`); `latest` follows the main branch. To stay on
a known version, put that tag in the compose file instead of `latest`. Release notes: [CHANGELOG.md](../CHANGELOG.md). To go back, set the earlier tag
and `docker compose up -d`.

## Backups

Back up the folder you mounted at `/data` (`./flacie-data`) while the container is stopped, or copy it as it is: everything in it can be rebuilt except
the sign-in keys.

| In `/data` | What | If lost |
|---|---|---|
| `keys/` | The key ring that encrypts the sign-in cookie and remembered accounts | Everyone has to sign in again |
| `users/` | Remembered accounts (encrypted with the keys) | Signed in again; nothing else |
| `device-id` | One id so Jellyfin lists FLACie Web as one device | A new device appears in Jellyfin |
| `notify.json` | The notification (ntfy) settings | Set them again in the dashboard |
| `library/`, `art/`, `lyrics/`, `audiofacts.json`, `downloads/` | Caches and the download log | Rebuilt automatically |

Playlists, favourites and per-user settings are **not** in `/data`: they live in each person's Jellyfin user settings (back up Jellyfin) or on their NAS share.

```bash
docker compose stop && tar czf flacie-data-$(date +%F).tgz flacie-data && docker compose start
```

Keep backups private: `keys/` is what protects the saved sign-ins.

## Environment variables

| Variable | Default | What it does |
|---|---|---|
| `FLACIE_JELLYFIN_URL` | (not set) | Lock sign-in to this Jellyfin (people then only type a username). Strongly recommended for any server others can reach. Also the address the server uses to check phone-app tokens. |
| `FLACIE_JELLYFIN_INTERNAL_URL` | (not set) | Where this server itself reaches Jellyfin when that differs from `FLACIE_JELLYFIN_URL` (a Docker network name or LAN address). |
| `FLACIE_ALLOW_NAS_LOGIN` | `false` | `true` shows the NAS sign-in tab (SMB). Off by default: it lets visitors make the server connect to addresses they type. |
| `FLACIE_ADMINS` | (not set) | Comma separated admins. With `FLACIE_JELLYFIN_URL` set: Jellyfin user names (`alice,bob`). Otherwise write `name@jellyfin-host:port` (or `name@nas-host`). Not set: the administrators of the locked Jellyfin. |
| `FLACIE_TRUSTED_PROXIES` | loopback and private networks | Addresses or CIDR ranges (comma separated) whose `X-Forwarded-*` headers are believed. `*` trusts every sender (only if nothing but your proxy can reach the port). |
| `FLACIE_DATA` | `/data` in Docker | The data folder (see Backups). |
| `PUID`, `PGID` | `1654` | Docker only: run as this user/group so the files in the mounted folder belong to you (`id` shows yours). |
| `FLACIE_NTFY_URL`, `FLACIE_NTFY_TOPIC`, `FLACIE_NTFY_TOKEN` | (not set) | First-start defaults for push notifications through an [ntfy](https://ntfy.sh) server. Later changes are made in the dashboard. |
| `FLACIE_JAMENDO_CLIENT_ID` | (not set) | Your own free [Jamendo](https://devportal.jamendo.com) client id, to offer Jamendo's artist-approved downloads. |
| `FLACIE_DEBUG` | (not set) | `1` turns on local debug endpoints (loopback only). **Never set this on a server other people can reach.** |
| `ASPNETCORE_URLS` | `http://+:8080` in Docker | Address and port the server listens on. |
| `FLACIE_PARENT_PID` | (internal) | Set by the Windows app so its server stops with it. Not for Docker. |

The optional download services (Soulseek through slskd, Lidarr, the open-source finders, YouTube through yt-dlp) are not installed by FLACie. Their addresses
and keys are entered by an admin in the dashboard (Downloads) or in the phone app and stored in that person's profile. They are **off until set up**.
Taking audio from YouTube or Soulseek can be against those services' terms or your local copyright law: using them is your decision and your responsibility.

## Troubleshooting

- **"Reconnecting…" forever, or the page loads but never becomes live:** the proxy is not passing WebSockets (see above).
- **Redirects go to `http://` after signing in:** the proxy isn't sending `X-Forwarded-Proto`, or its address is not in `FLACIE_TRUSTED_PROXIES`.
- **"Couldn't reach …" when signing in:** the container can't reach that Jellyfin address; try `FLACIE_JELLYFIN_INTERNAL_URL` with a LAN address.
- **Signed in but no songs:** Jellyfin's music library must be visible to that user; check the dashboard's "loading the library" line and `docker compose logs flacie`.
- **"Too many failed attempts":** more than 8 wrong sign-ins from one address or for one user name in 15 minutes. Wait, or restart the container.
- **The NAS tab is missing:** set `FLACIE_ALLOW_NAS_LOGIN: "true"`.
