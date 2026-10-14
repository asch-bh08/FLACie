# Alexa for FLACie: plan

Written 2026-10-14 after reading the current Alexa developer docs. Every claim and its source URL is in [RESEARCH.md](RESEARCH.md). `[V]` there means the docs say it, `[I]` means inferred, `[?]` means untested.

## Decision

**Build B now: a custom skill with the AudioPlayer interface, invocation name "flacie".** Structure the server so A (the Music Skill API) can be added later without touching the core.

| | A. Music Skill API ("default music service") | B. Custom skill + AudioPlayer |
|---|---|---|
| Say | "Alexa, play Blinding Lights on FLACie" | "Alexa, ask FLACie to play Blinding Lights" |
| Endpoint | **AWS Lambda in us-east-1 only** (no HTTPS endpoint option documented). Needs an AWS account and a relay Lambda that calls the home server. | **Your own HTTPS endpoint** (the flacie-web container). Self-signed is allowed for the endpoint in dev, never for audio URLs. |
| Language | **US English only** | Any |
| Voice recognition of your library | Needs the catalog ingested; "SLU modelling can take several weeks". Obscure names are not recognised until then. | Slot values come from our own interaction model; we match the words ourselves, so it works on day one. |
| Default service ("play X" with no service name) | **Unknown for a dev-stage skill** (docs never say). Must be tested with a probe skill. | **No.** The invocation name is always needed. |
| Thumbs, shuffle, repeat, next, previous | Native directives | Built-in intents (`AMAZON.NextIntent` and so on) work without saying "flacie" while the skill owns the audio |
| Proactive "your download is ready" | **No** (Proactive Events are custom-skill only) | **Yes** (permission card, then `/v1/proactiveEvents`) |
| Alarms and multi-room | Documented for music skills | Probably no (inferred) |
| Needs Amazon approval | Not for a plain music skill | No |

Why B first: it works end to end without an AWS account, has no catalog delay, can notify when a download finishes, and every piece of it (verification, matching, queue, streams, downloads) is also needed by A. A is worth a one-day probe afterwards (see "Later").

## Feature list: Amazon Music features for third-party skills

Key: **Do** = built in this work. **Can't** = not possible, with the reason. **Later** = possible but not now.

| Feature | B (built now) | Notes |
|---|---|---|
| Play song / artist / album / playlist | **Do** | Our own slots and fuzzy and phonetic matching (`VoiceMatcher`). |
| Play genre, decade, "recently added", "my library" | **Do** | Built as a made-up playlist from the library. |
| "Something like <artist>" | **Do** (simple) | The artist's own songs first, then songs of other artists in the same genres and years, from your library. It is not an audio-similarity radio. |
| Pause, resume, stop, next, previous, start over | **Do** | `AMAZON.*` intents and `PlaybackController`. |
| Shuffle, repeat one/all | **Do** | `AMAZON.ShuffleOn/Off`, `LoopOn/Off`, `RepeatIntent`. |
| What's playing / who sings this | **Do** | Custom intent (needs "ask flacie"); title and artist are also shown on screens. |
| Thumbs up / down | **Do** | Custom intents "I like this" / "I don't like this". Stored as 5 / 1 star ratings (existing ratings) and written to Jellyfin. ListenBrainz feedback only if it is enabled, later. |
| Add current song to a playlist | **Do** | Custom intent, uses the existing playlist code. |
| Song not in library: say so, search and download | **Do** | `DownloadManager` (Soulseek, Lidarr, Jamendo, Audius, Internet Archive, SoundCloud), tagged "Alexa" in the Downloads log. |
| "Is it ready?" / "What's downloading?" / "play the last download" | **Do** | |
| Notify when a download finishes | **Do** | Proactive notification (`AMAZON.MessageAlert` or `MediaContent.Available`) once you grant the permission; otherwise the next time you talk to FLACie it tells you. |
| Resume where you left off | **Do** | Position is saved per link. |
| Now playing card on Echo Show and Fire TV | **Do** | Title, subtitle, cover art and background (all four are sent together). |
| Progressive "searching…" message | **Do** | `VoicePlayer.Speak`, max 5, only from intents. |
| Pair an Alexa account | **Do** | One-time six digit code from Settings (no OAuth server needed). Allowlist only, revoke button. |
| Show Alexa in Connect as a playing device | **Do** | Read-only "Echo (Alexa)" row in the devices menu with hand-over to the app ("Play here"). Controlling an Echo from the app is **not possible**: a skill can only answer Alexa's requests, never start or stop playback by itself. |
| Sleep timer | **Can't** (not as a skill feature) | Not documented for third-party skills. "Alexa, stop playing in 30 minutes" is a device feature; test whether it works with the skill (listed in the by-ear script). |
| Alarms with music, routines with music | **Can't** for B | Built-in alarms cannot target a custom skill. Routines may call "ask flacie…" as a custom action (to test). A might support alarms. |
| Multi-room groups | **Can't** for B (inferred) | Custom AudioPlayer skills do not join native groups. A: documented, **Later**. |
| Lyrics display | **Can't** | No skill API for it. |
| Follow artist, podcasts, radio stations, explicit filter, HD/UHD | **Can't / Later** | Podcasts and radio are invite-only previews; premium audio needs an Amazon representative. FLACie already has radio-like mixes, which the "something like" intent covers. |
| Be the default music service | **Later** (needs A) | Needs the probe in the "Later" section. |
| Catalog ingestion (voice model of the library) | **Later** (needs A) | An exporter is built now (`AlexaCatalog`) and tested, because it is also the source of the custom-skill slot values. |

## Audio

- Alexa plays AAC/MP4, MP3, PLS, M3U/M3U8 and HLS at 16 to 384 kbps. FLAC is not listed, so it is treated as unsupported.
- FLACie serves `/alexa/stream/...` as **MP3 at 256 kbps** (or the file itself if it is already MP3 or AAC under 384 kbps). Jellyfin does the transcoding through its own `stream.mp3` endpoint, so no ffmpeg is added to the container and the call goes through the existing request gate.
- URLs are HTTPS on port 443, no redirects, signed (HMAC), valid for a few hours, and contain no Jellyfin token. The Echo cannot send headers.

## Safety

- Off by default. Settings toggles: Enable Alexa, Allow Alexa to download, Revoke access.
- Every request is verified: certificate URL rules, chain to a trusted root, `echo-api.amazon.com` in the SAN, RSA signature of the raw body (`Signature-256`), timestamp within 150 seconds, skill ID in the allow-list. If any check fails, the answer is HTTP 400 with no detail.
- Rate limit per link and per address. Logs hold event kinds and short reasons, never tokens, codes or bodies.
- Only linked Alexa accounts are served. The Jellyfin sign-in behind a link is encrypted with the server's data-protection keys and never leaves the server.
- The audio endpoint needs a valid signature; nothing else on the server becomes reachable without a cookie.

## Order of work

1. Verifier, rate limiter, signed URLs (done, tested).
2. Link store with pairing, queue state (done, tested).
3. Voice matcher and aliases (done, tested).
4. **Engine**: intent to reply, play-or-fetch, library queries, ratings, playlists, downloads, notices.
5. **Endpoint and streams**: `/alexa/skill`, `/alexa/stream/...`, `/api/alexa/*` for Settings, wired in `Program.cs`, Settings section.
6. **Skill package**: interaction model, manifest, deploy script (`ask deploy -t skill-metadata`).
7. Tests with recorded fixtures, 10,000-song load test, docs, by-ear script.

## Later (not part of this work)

1. **Probe for A**: a Music-type skill with a stub Lambda returning one MP3 and one FLAC URL. Checks (a) does it show under Default Services for a dev-stage skill, (b) does FLAC play, (c) does "play <artist> on <name>" reach GetPlayableContent. Needs an AWS account and the Amazon login, so it is a by-hand step.
2. If the probe passes: relay Lambda, an A adapter on the same engine, catalog upload with `ask smapi upload-catalog`.
3. ListenBrainz feedback for thumbs when ListenBrainz is enabled.
4. Alexa as a controllable Connect target.

## Things only you can check

Listed in `docs/alexa/BY-EAR.md` once written: everything involving sound, Echo behaviour, and the Amazon login steps.
