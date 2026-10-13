# Chromecast: decision record (not built, on purpose)

Status: written 2026-10-13, overnight run. **Not implemented.** This is a decision for the owner, not a gap in effort.

## Why it is blocked
- A Cast receiver (the Default Media Receiver on a TV or speaker) loads the stream URL itself. It cannot send the `Authorization: MediaBrowser ... Token=` header that FLACie uses for every Jellyfin request (the older `api_key=` query is refused by Jellyfin 12).
- The only way to let a receiver fetch a song is to put a credential in the URL (`ApiKey=<token>`), and that URL is handed to the Cast device (and its cloud-side receiver app). Jellyfin has no short-lived signed stream URLs.
- That puts the user's session token on a device we do not control. The standing rule is that tokens are never put where they can leak, so this was not done without the owner's say-so.

## What would make it safe (pick one)
1. A dedicated, read-only, revocable Jellyfin API key made by the owner for casting only (Settings > Cast key), used only in cast URLs. Revoking it kills every cast URL.
2. A tiny relay in the FLACie server (`flacie-web`) that mints a time-limited URL per song (HMAC, 2 hours) and streams from Jellyfin with the server-side token; the receiver only ever sees the relay URL.

Option 2 is the cleaner one and also gives the web a "cast" path later.

## Plan once the owner picks
- Android: `androidx.mediarouter` + `play-services-cast-framework`, an `OptionsProvider` (Default Media Receiver), no `MediaRouteButton` (it needs an AppCompat theme and crashed the Compose activity in earlier tests); our own sheet lists routes found while the sheet is open (no background scanning).
- Hand-off like Connect: `PlayerController.interceptor` carries toggle/seek/next/prev to the `RemoteMediaClient`, the local player pauses, "Stop casting" resumes locally at the remote position. Not usable while in a Jam (one interceptor slot).
- Needs a real Cast device to test; the emulator has no receivers. Untestable here, so it would ship marked "Not tested on a device".
