# AI DJ: build spec (2026-10-25)

Background and research: `docs/research/ai-dj-free.md` on branch `research/ai-dj` (sections 10 and Appendix A are the design). Owner's decisions: Cloudflare Workers AI makes the voice; easy settings; the DJ can be on only for Android Auto, only for web, or only for one playlist or queue. Ships on Android + web + Windows together. Alexa is not in this build (its "Where" tick is left out until it is).

## How it works
1. When a song starts, the player asks the server about the **next** song (`PrepareAsync` in-process for web/Windows, `POST /api/dj/next` for Android).
2. The server decides whether to speak (settings, switches, frequency, variety; all in FLACie.Core so it is tested once), writes the line from templates, and starts making the MP3 with Cloudflare in the background. The answer is cached per (account, prev, next) so asking again gives the same answer.
3. When the player moves to that song **by itself** (auto-advance, not a skip), it plays the clip on a second small player over the start of the song, with the music ducked to 30% (ramp 400 ms down, 600 ms up). No queue item is added; play counting, the queue, the media session and Android Auto metadata are untouched.
4. If the server returns `ClipUrl = null` and `Backup = true`, the device says `Text` with its own voice (Android `TextToSpeech`, browser `speechSynthesis`), ducking the music the same way.
5. Pause, skip, seek, stop, a new queue: the clip stops at once and the music un-ducks.

## Which switch wins (FLACie.Core, tested)
1. `QueueDj` (Now Playing button, this queue only) if not null.
2. `Playlists[PlaylistId]` if the queue came from a playlist with an entry.
3. `Enabled && Settings.On(Surface)`.
Then the frequency/variety rules (Appendix A.4) and the silent conditions decide this transition.

## Shared types
`ipodsync/src/FLACie.Core/Dj/DjContracts.cs`: `DjSurface`, `DjChatty`, `DjSettings`, `DjSong`, `DjRequest`, `DjCue`, `IDjService`. Change them only by agreement (they are the contract between the three parts).

## HTTP (all under `/api/dj`, caller = cookie or `X-Emby-Token` + `X-Jellyfin-Server`, via `ApiEndpoints.CallerAsync`)
- `GET /api/dj/settings` → `{ enabled, chatty: "rare"|"often", phone, auto, web, windows, backup, playlists: { id: bool }, voiceReady }`
- `POST /api/dj/settings` → partial update with the same field names (type-checked, 400 on wrong types); `{ "playlist": { "id": "...", "on": true|false|null } }` sets or clears one playlist. Returns the GET shape. Any signed-in user, for their own settings only.
- `POST /api/dj/next` body (camelCase of `DjRequest`; `surface` "phone"|"auto"|"web"|"windows"):
  `{ surface, next: { title, artist, album, albumArtist, year, genre, durationMs, path, jellyfinId, trackNo, discNo }, prev: {...}|null, prevPlayedOut, skippedRecently, playlistId, queueDj, localHour, weekday, why }`
  → `{ "speak": false }` or `{ "speak": true, "text": "...", "caption": "...", "clip": "/api/dj/clip/<id>.mp3"|null, "backup": bool }`
- `GET /api/dj/clip/{id}.mp3` → `audio/mpeg`. Waits up to 10 s if the clip is still being made; 404 if it failed or expired. Never counted as a play; not under `/stream`.

## Admin (server-wide, FLACie Web dashboard, admin only)
`{data}/dj.json` like `Notifier`: Cloudflare account id, API token (encrypted with IDataProtector, never returned; "leave empty to keep"), voice `melotts` or `aura-1:<speaker>`, daily cap (% of the 10,000 free neurons, default 50), plus a Test button and today's use. Optional `FLACIE_CLOUDFLARE_ACCOUNT` / `FLACIE_CLOUDFLARE_TOKEN` env bootstrap.
Cloudflare: `POST https://api.cloudflare.com/client/v4/accounts/{account}/ai/run/@cf/myshell-ai/melotts` `{ "prompt": text, "lang": "en" }`, or `.../@cf/deepgram/aura-1` `{ "text": text, "speaker": s, "encoding": "mp3" }`. Accept either a binary `audio/mpeg` body or JSON `{ result: { audio: <base64> } }`. Cost estimate per line: MeloTTS 18.63 neurons per audio minute (estimate audio length from words at 2.6 words/s); Aura-1 1,363.64 per 1,000 characters. Over the cap or failing → no clip (Backup path). Clips cached in `{data}/dj-clips/` by hash of voice+text, oldest pruned past 3,000 files. `api.cloudflare.com` joins the `FLACIE_CATALOG_REDIRECT` test seam.

## Who owns what (parallel work)
- **Server** (FLACie.Core/Dj/*, FLACie.Server/Dj/*, `UserState.cs` Dj block, `Program.cs` registration, `CatalogRedirect.cs`, `Admin.razor` #dj section, FLACie.Tests/Dj*): planner, templates, text clean-up, `DjService`, Cloudflare voice, config store, endpoints, tests.
- **Web/Windows** (FLACie.Retro, FLACie.App, FLACie.Server/Components/Pages/Settings.razor + Shared/DjSettings.razor, wwwroot/flacie.js + css, FLACie.Core/IpodMenus.cs, FLACie.Hybrid NativeOutput, tests/web-smoke spec): PlayerState wiring, overlay + duck, backup speech, Now Playing DJ button, playlist switch, settings section, iPod menu item.
- **Android** (android/ only): FlacieWebClient DJ calls, overlay player + duck, TTS backup, Now Playing DJ button, playlist chip, settings panel, Android Auto DJ button, in-car detection, tests.
