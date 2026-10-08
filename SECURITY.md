# Security notes

## Reporting a problem

Please report security problems privately through GitHub: **Security > Report a vulnerability** on this repository (not a public issue).

## What FLACie Web protects, and how

| Area | What it does |
|---|---|
| Sign-in | A Jellyfin password or Quick Connect is checked by **your Jellyfin**; FLACie never stores the password. The sign-in cookie (HTTP-only, SameSite=Lax, secure over HTTPS, 30 days) is encrypted with the key ring in `/data/keys`. |
| Password guessing | 8 failed sign-ins within 15 minutes from one address, or against one user name, are refused until the window passes (in memory; a restart clears it). Quick Connect starts and NAS attempts count too. |
| Forwarded headers | `X-Forwarded-For/Proto/Host` are believed only from loopback and private networks, or from `FLACIE_TRUSTED_PROXIES` if set. A client on the public internet can't forge its address or scheme. |
| Admin pages | Only administrators of the Jellyfin the server is locked to (`FLACIE_JELLYFIN_URL`), or accounts listed in `FLACIE_ADMINS`. A user name alone proves nothing on a server where visitors may type their own Jellyfin address, so there a list entry must be `name@host`. |
| NAS sign-in | Off unless `FLACIE_ALLOW_NAS_LOGIN=true`, because it makes the server connect to an address chosen by the visitor. |
| Headers | `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`, `Referrer-Policy: same-origin`, `Permissions-Policy`, and HSTS over HTTPS. |
| Container | Starts as root only to fix the ownership of `/data`, then runs as an unprivileged user. `/data/keys` and `/data/users` are made private to that user. |

## What is stored in a user's profile (read this before you invite other people)

The profile that follows a person between their phone, PC and the web is a JSON document saved in **their Jellyfin user settings**
(and as `.flacie/profile-<user>.json` on their NAS share, if they use one). It is readable by that person, **by the administrators of that Jellyfin
server, and by anyone who can read that NAS folder.** It contains, in plain text:

- their Jellyfin access token (so the web, phone and PC share one sign-in), and for NAS users the NAS user name, **password** and domain;
- the addresses and **API keys** of the download services they set up (slskd, the file mover, Lidarr) and optional open-source finder settings;
- playlists, favourites and preferences.

What this means for you as the host
- If you are the Jellyfin admin and the only user, nothing changes: you could already read all of it.
- If you host for **other people**, the Jellyfin admin can see their tokens and any NAS password or download-service key they put in FLACie. A Jellyfin admin
  can already act as any user, but a NAS password or a Lidarr/slskd key is more than that. Tell people not to reuse important passwords; give the NAS
  account read-only access to the music; and keep download-service keys to services you run yourself.
- The phone app stores the same document in the same format, so this can't be changed on the web side alone without breaking the apps. Encrypting the secret
  fields in the profile (with a key the user holds) is planned work, not done.

## Things to know

- `/data/keys` is the master secret of the server: anyone who can read it can decrypt the sign-in cookies and the remembered accounts. The key ring is stored
  as files with the container user's permissions, not in a vault. Don't put `/data` on a shared folder.
- Sign-in and NAS/Jellyfin addresses typed by visitors are contacted by the server. Locking the server to your Jellyfin (`FLACIE_JELLYFIN_URL`) and leaving
  NAS sign-in off removes that. If you can't, put the site behind a VPN (Tailscale) or your proxy's authentication.
- Rate limiting is per address and per account name inside the server. Behind a proxy that isn't in the trusted list every visitor looks like the proxy, so
  one attacker could lock everyone out for a few minutes: set `FLACIE_TRUSTED_PROXIES` correctly.
- There is no cross-site request protection on the sign-in and sign-out forms (they work without a prior page). A hostile page can sign someone out; it cannot read anything.
- `FLACIE_DEBUG=1` exposes local-only debug endpoints. Never use it on a server other people reach.
- Running the optional download services is your responsibility: downloading from some sources can be against their terms or your local law.
- The Android app is currently signed with a debug key, and the Windows installer is unsigned (Windows SmartScreen will warn). Both will change before a
  store-style public release.

## Automated checks

`ipodsync/tests/web-smoke` (Playwright) runs on every push: every page opens against a mock Jellyfin, security headers are present, NAS sign-in is refused
while off, wrong passwords are refused, and repeated wrong passwords are throttled.
