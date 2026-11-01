# Hosting FLACie Web yourself

FLACie Web is one container. It plays music from **your** Jellyfin server or **your** NAS, and keeps each person's playlists, favourites and
settings in their own Jellyfin user settings (or in a `.flacie` folder on their NAS share). There is no FLACie account service and nothing is sent to
anyone else: you run the server, you own the data.

Contents: [Jellyfin only](#a-jellyfin-only) · [NAS only](#b-nas-only-no-jellyfin) · [HTTPS and reverse proxy](#https-and-reverse-proxy) ·
[Alexa](ALEXA.md) · [Upgrading](#upgrading) · [Backups](#backups) · [Environment variables](#environment-variables) · [Troubleshooting](#troubleshooting) ·
[Security notes](../SECURITY.md)

You need Docker (with Compose) on any machine that can reach your Jellyfin or NAS. The image runs on amd64 and arm64 (Raspberry Pi 4/5 included).

**Jellyfin versions.** Tested against Jellyfin **10.10.7** and **12.2.0** (the current `jellyfin/jellyfin:latest`): signing in, browsing, playing, playlists, the Info panel, Connect (remote control between devices) and the SyncPlay calls Jams use all work on both, on the web and in the Android app (from beta 45 on; older versions of the app cannot use Jellyfin 12, which refuses the `X-Emby-Token` header and `api_key=` those sent).

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

   Use an address the **container** can reach: the Docker host's LAN address or name (for example `http://192.168.1.20:8096`), not `localhost` or `127.0.0.1`, which inside the container is the container itself.

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

- **Admin with a NAS sign-in:** the dashboard is closed to everyone until you name an admin. Add `FLACIE_ADMINS: "yourNasUser@nas.local"`, written exactly as the user name and NAS address are typed on the sign-in page.

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

## Rate limits and bot protection

A server that is reachable from the internet gets knocked on by scripts. FLACie limits how often each door can be used, so a password guesser, a
runaway app or a stolen session is stopped within a minute, while nobody using it normally ever meets a limit. There are no CAPTCHAs.

**Rate limits.** Each limit is a number of requests a minute. Doors anyone can reach count the client's address (the real one only when it comes
through a trusted proxy, see above: otherwise every visitor looks like the proxy and shares one count). Everything else counts the signed-in account,
or the phone app by its device, so a household behind one address does not share a limit. A refused request gets `429 Too Many Requests` with a
`Retry-After` header and a message saying how long to wait. Set any of them with `FLACIE_RATE_<NAME>` (the name in capitals, `-` as `_`; `0` or `off`
switches that one off):

| Name | Default a minute | Counted by | What it covers |
|---|---|---|---|
| `signin` | 10 | address | Password and NAS sign-in, starting Quick Connect |
| `qc-poll` | 60 | address | The sign-in page asking whether a Quick Connect code was approved (it asks 24 times a minute) |
| `public` | 600 | address | `/healthz`, `/robots.txt`, `/licenses`, `/api/ping`, the Alexa store page's policy and icons (each open tab asks `/healthz` every 2-3.5 s while the server is away) |
| `token-check` | 20 | address | Phone-app tokens this server has not checked in the last 45 seconds (each costs one call to Jellyfin) |
| `alexa` / `alexa-account` | 120 / 30 | address / Alexa account | The Alexa skill endpoint, before and after the request is verified |
| `share-page` / `share` | 30 / 30 | account | Opening `/s/...` share links / making one |
| `share-miss` | 10 | account | Share links that lead nowhere: past this, every link shows nothing for the rest of the minute, so ids cannot be guessed |
| `art` | 30000 | account | Covers. Flinging through a 10,000-song list at full speed asks for about 11,600 a minute |
| `stream` | 1200 | account | Starting or seeking a song (`/stream`, `/api/alac`, Alexa's links). 40 skips in a row open about 16 |
| `wave` | 1200 | account | The seek bar's shapes. 40 skips in a row ask for about 80 |
| `dj` / `dj-ask` | 60 / 20 | account | New DJ lines between songs / "talk to the DJ" questions for the text model. Past them the DJ stays quiet or answers from its quick rules: one client cannot spend the day's Cloudflare allowance |
| `search` | 360 | account | Online catalog searches from the Search page (one per keystroke) |
| `api` | 600 | account | Every other `/api` call |

`FLACIE_RATE_SCALE` multiplies every default (a test server uses `50`); `FLACIE_RATE_LIMITS=off` switches them all off (only for a server nobody else
can reach). Pages, static files, covers already in the browser and the live connection are never limited. The counters are in memory and bounded
(at most 10,000 addresses or accounts per limit), so a flood from many addresses cannot grow the server's memory.

**Sign-in.** On top of the `signin` rate limit, wrong passwords are counted:
- 8 wrong passwords for one name from one address within 10 minutes stop that name from that address until the 10 minutes have passed (short, and
  only there);
- 30 failures of any names from one address within 15 minutes stop that address (someone working through a list of names);
- guesses at one name from many addresses are never refused, but after 4 wrong ones each further try at that name waits 1, 2, 4, 8, then 16 seconds;
- a browser that has signed in as that name before carries a signed "known device" cookie (only sent to `/auth`), and is not held up by anyone else's
  guessing at all: however much a stranger guesses, the real person signs in at once from their own phone or laptop.
Jellyfin has its own lockout as well (each user's setting for failed sign-ins before the account is locked; by default 3, and 5 for administrators),
and FLACie passes each try on to Jellyfin, so set that the way you want it.

**Other protections.** Every answer carries `X-Robots-Tag: noindex, nofollow` and `/robots.txt` disallows everything, so search engines do not list
your server; `Content-Security-Policy` (no plugins, no framing by other sites, no foreign `<base>`, forms only to this server), `X-Frame-Options`,
`X-Content-Type-Options: nosniff` and `Referrer-Policy: same-origin` are set; no request body over 2 MB is read (the playlist import allows 20 MB).
Share links are 10 random characters (about 58 bits), only work for someone signed in to this server, and a wrong one says nothing about which
links exist.

## Upgrading

```bash
docker compose pull
docker compose up -d
```

Your data lives in `./flacie-data` and is kept. Releases are tagged (for example `ghcr.io/asch-bh08/flacie-web:1.0.0-beta44`, the beta number of the release on GitHub), every build also has a `sha-<commit>` tag, and `latest` follows the main branch. To stay on
a known version, put that tag in the compose file instead of `latest`. (Older tags such as beta44 do not have the sign-in rate limit or the trusted-proxy rules.) Release notes: [CHANGELOG.md](../CHANGELOG.md). To go back, set the earlier tag
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
| `shares/` | Songs and playlists shared as links (`/s/...`): their names and artists only | Those links stop working |
| `library/`, `art/`, `lyrics/`, `audiofacts.json`, `downloads/` | Caches and the download log | Rebuilt automatically |

Playlists, favourites and per-user settings are **not** in `/data`: they live in each person's Jellyfin user settings (back up Jellyfin) or on their NAS share.

```bash
docker compose stop && tar czf flacie-data-$(date +%F).tgz flacie-data && docker compose start
```

Keep backups private: `keys/` is what protects the saved sign-ins.

## The phone app

The Android app is a separate download (APK on the GitHub release page; enable installing from unknown sources). On first start choose **Sign in with Jellyfin**, enter your Jellyfin address (from the phone, so a name or address the phone can reach; in the Android emulator your PC is `10.0.2.2`) and use Quick Connect or a password. Then open **Settings > Library > Jellyfin > Use my account** once: signing in syncs your profile, but the music source is switched on separately. Your Jellyfin must be 10.10.x for now (see above). The web server is only needed for extras such as the audio-format filters, sharing songs as links and the seek bar's shapes; the app plays straight from Jellyfin. The phone learns the web server's address from your profile: open FLACie on the web once (at the address people use, not `localhost`) and the phone picks it up.

## Environment variables

| Variable | Default | What it does |
|---|---|---|
| `FLACIE_JELLYFIN_URL` | (not set) | Lock sign-in to this Jellyfin (people then only type a username). Strongly recommended for any server others can reach. Also the address the server uses to check phone-app tokens. |
| `FLACIE_JELLYFIN_INTERNAL_URL` | (not set) | Where this server itself reaches Jellyfin when that differs from `FLACIE_JELLYFIN_URL` (a Docker network name or LAN address). |
| `FLACIE_ALLOW_NAS_LOGIN` | `false` | `true` shows the NAS sign-in tab (SMB). Off by default: it lets visitors make the server connect to addresses they type. |
| `FLACIE_ADMINS` | (not set) | Comma separated admins. With `FLACIE_JELLYFIN_URL` set: Jellyfin user names (`alice,bob`). Otherwise write `name@jellyfin-host:port` (or `name@nas-host`). Not set: the administrators of the locked Jellyfin. |
| `FLACIE_TRUSTED_PROXIES` | loopback and private networks | Addresses or CIDR ranges (comma separated) whose `X-Forwarded-*` headers are believed. `*` trusts every sender (only if nothing but your proxy can reach the port). |
| `FLACIE_SERVICE_ROUTES` | (not set) | Comma separated `public=>internal` pairs, for example `https://files.example.com/filemove=>http://127.0.0.1:8090`. This server sends its own requests for a download service's public address to the internal one, so a DNS or tunnel problem on the public name cannot stop downloads when the service runs on the same machine. The address saved in people's profiles (and used by the phone) is not changed. |
| `FLACIE_RATE_<NAME>`, `FLACIE_RATE_SCALE`, `FLACIE_RATE_LIMITS` | see [Rate limits](#rate-limits-and-bot-protection) | Requests a minute for one limit (`0` or `off` switches it off); a multiplier for every default; `off` switches every limit off. |
| `FLACIE_DATA` | `/data` in Docker | The data folder (see Backups). |
| `PUID`, `PGID` | `1654` | Docker only: run as this user/group so the files in the mounted folder belong to you (`id` shows yours). |
| `FLACIE_PUBLIC_URL`, `FLACIE_ALEXA_SKILL_IDS`, `FLACIE_ALEXA_CLIENT_ID`, `FLACIE_ALEXA_CLIENT_SECRET` | (not set) | Only for the optional [Alexa](ALEXA.md) skill (off until switched on in Settings): the public https address Alexa reaches this server at, the allowed skill id(s), and the skill's Login with Amazon client id and secret for "your download is ready" notifications. |
| `FLACIE_NTFY_URL`, `FLACIE_NTFY_TOPIC`, `FLACIE_NTFY_TOKEN` | (not set) | First-start defaults for push notifications through an [ntfy](https://ntfy.sh) server. Later changes are made in the dashboard. |
| `FLACIE_DOWNLOADS` | (not set) | `on` or `off` overrides the dashboard's **Allow downloads** switch. Not set: the switch decides, and it is off on a new install (an install that had already downloaded something before this switch existed starts with it on). Off means no download buttons and nothing fetched by charts, imports, Autoplay, Alexa or the DJ. |
| `FLACIE_JAMENDO_CLIENT_ID` | (not set) | Your own free [Jamendo](https://devportal.jamendo.com) client id, to offer Jamendo's artist-approved downloads. |
| `FLACIE_DEBUG` | (not set) | `1` turns on local debug endpoints (loopback only). **Never set this on a server other people can reach.** |
| `ASPNETCORE_URLS` | `http://+:8080` in Docker | Address and port the server listens on. |
| `FLACIE_PARENT_PID` | (internal) | Set by the Windows app so its server stops with it. Not for Docker. |

Downloading is **off on a new install**. An admin switches it on in the dashboard (Downloads on this server > Allow downloads) or with `FLACIE_DOWNLOADS=on`.
The optional download services (Soulseek through slskd, Lidarr, the open-source finders) are not installed by FLACie. Their addresses and keys are entered
by an admin in the dashboard (Download services) or in the phone app and stored in that person's profile. "Download all" and playlist imports are for admins
unless the dashboard's "Let everyone use Download all and imports" is on. The automatic downloads (daily charts, Autoplay's Fetch ahead, Alexa) start off.
Only download music you own or have permission to download: downloading copyrighted music without permission can be against the law where you live and
against those services' terms. Using them is your decision and your responsibility.

## Troubleshooting

- **"Reconnecting…" forever, or the page loads but never becomes live:** the proxy is not passing WebSockets (see above).
- **Redirects go to `http://` after signing in:** the proxy isn't sending `X-Forwarded-Proto`, or its address is not in `FLACIE_TRUSTED_PROXIES`.
- **"Couldn't reach …" when signing in:** the container can't reach that Jellyfin address; try `FLACIE_JELLYFIN_INTERNAL_URL` with a LAN address.
- **Signed in but no songs:** Jellyfin's music library must be visible to that user; check the dashboard's "loading the library" line and `docker compose logs flacie`.
- **"Too many failed attempts":** 8 wrong passwords for that name from this address in 10 minutes, or 30 failures from this address in 15 minutes; the
  message says how long is left. Wait, or restart the container. Guessing at one user name never locks that user out (see
  [Sign-in](#rate-limits-and-bot-protection)): elsewhere each further try is only held back, and a browser that signed in before is not held back at all.
- **"Too many requests just now" (429):** a [rate limit](#rate-limits-and-bot-protection). If everyone hits it at once, your proxy's address is
  probably not trusted (`FLACIE_TRUSTED_PROXIES`), so all visitors share one address; otherwise raise that limit with `FLACIE_RATE_<NAME>`.
- **The NAS tab is missing:** set `FLACIE_ALLOW_NAS_LOGIN: "true"`.
