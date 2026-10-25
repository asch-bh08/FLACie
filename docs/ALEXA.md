# Alexa for FLACie Web

Optional and **off by default**. Jellyfin-only keeps working from any network without it; nothing in FLACie needs Alexa.

With it on, you can say to an Echo:

- "Alexa, ask FLACie to play Blinding Lights" / "play XTC" / "play my Road Trip playlist" / "play something like Radiohead" / "play the album OK Computer"
- "next", "previous", "pause", "resume", "shuffle on", "loop on" (repeat the list), "start over" (restart the song), "I like this", "I don't like this", "what's playing", "add this to my Road Trip playlist"
- If a song is not in your library, Alexa says so and searches for it. It asks Soulseek, Lidarr, Jamendo, Audius, the Internet Archive and SoundCloud through the same downloader the Search page uses, tags it **Requested from Alexa** in the Downloads log, and tells you when it is ready ("is it ready?", "play the last download", and a notification on the Echo if you set that up below).

Why "ask FLACie" and not just "play ...": Amazon only lets its Music Skill API, not a custom skill, be a default music service, and that API needs an AWS Lambda in us-east-1 and US English. The reasons, the full feature table (what is built, what cannot be done, what is for later) and the sources are in [alexa/PLAN.md](alexa/PLAN.md) and [alexa/RESEARCH.md](alexa/RESEARCH.md).

## What you need

1. FLACie Web running (see [SELF-HOSTING.md](SELF-HOSTING.md)), signed in with Jellyfin. NAS-only sign-ins are not supported by Alexa.
2. **A public https address on port 443 with a certificate Amazon trusts** (Let's Encrypt is fine). Alexa's servers call `/alexa/skill`, and the Echo itself downloads each song from `/alexa/stream/...`. A name that only resolves inside your home or a Tailscale network will not work.
3. An Amazon developer account (free) signed in on the same Amazon account as your Echo, and Node.js for `ask-cli`.
4. For the "ready" notification on the Echo (optional): the skill's Login with Amazon security profile (client id and secret).

### Expose only what Alexa needs

Alexa needs **only** `/alexa/*` (and `/healthz` if you like). Do not put the whole site on the internet for this. With Caddy:

```
flacie.example.com {
    @alexa path /alexa/*
    reverse_proxy @alexa flacie:8080
    respond 404
}
```

With Tailscale Funnel, mount only the path if your version supports it (`tailscale funnel --set-path /alexa ...`); otherwise use a proxy as above. FLACie logs every refused Alexa request (a reason only; also too-large and rate-limited ones) so you can see what reaches it.

## Setup

1. **Turn it on in FLACie.** Settings > Alexa (administrators): switch on *Enable Alexa*. Leave *Alexa can download songs* on or off as you like. Set `FLACIE_PUBLIC_URL=https://flacie.example.com` in the container: it is the address put into the song links. (Without it, the address of the first verified request is used.)
2. **Install ask-cli and sign in yourself.** `npm install -g ask-cli`, then `ask configure` (it opens the Amazon login in your browser). Nobody else, and none of FLACie's scripts, ever sees that login.
3. **Deploy the skill.** From the repository:
   ```
   alexa-skill\deploy.ps1 -PublicUrl https://flacie.example.com        (Windows)
   alexa-skill/deploy.sh https://flacie.example.com                    (macOS, Linux)
   ```
   It creates a development-stage skill named FLACie (invocation name "flacie") on your account from `alexa-skill/skill-package`, with the endpoint set to your address. Nothing is submitted for certification. If Amazon refuses a one-word invocation name, change `invocationName` in `alexa-skill/skill-package/interactionModels/custom/*.json` to "flacie player" and deploy again.
4. **Tell FLACie which skill is allowed.** Copy the skill id (`amzn1.ask.skill....`) printed by the deploy into Settings > Alexa > *Skill ID*. Until you do, every request is refused. (`FLACIE_ALEXA_SKILL_IDS` gives the first-start value from the environment; once Settings holds a value, Settings wins. Several ids can be separated by commas.)
5. **Enable the skill in the Alexa app** (More > Skills & Games > Your Skills > Dev). Then say "Alexa, open FLACie".
6. **Link your Alexa to your FLACie.** In Settings > Alexa press *Create a code*. Within ten minutes say: "Alexa, open FLACie" and then "my code is 4 8 2 9 1 7". The code works once.
7. **Notifications (optional).** In the developer console open the skill's *Permissions* and *Login with Amazon* security profile, put its client id and secret in the server's environment as `FLACIE_ALEXA_CLIENT_ID` and `FLACIE_ALEXA_CLIENT_SECRET`, and restart. The first time Alexa starts a download it shows a permission card in the Alexa app; allow notifications there. Without this, the same message is simply spoken the next time you talk to FLACie.

## Keeping the names up to date

"Alexa, ask flaky for <song>" reaches the skill as just the song's name, and Alexa recognises a bare name best when the skill knows it. The skill carries a
list of every title, artist and album in the library (`alexa-skill/library-names.py` makes it from the server's library files). After adding music, update it:

```
alexa-skillefresh-names.ps1 -SkillId amzn1.ask.skill....
```

It reads every account's library from the server over ssh and redeploys the skill (about three minutes, most of it Amazon building the model). The list
(`alexa-skill/names.txt`) stays on your computer and is never committed. A song missing from the list is still heard in "play <song>" and, in Amazon's own
test, as a bare name too; the list makes it reliable.

Phrases that work: "ask flaky to play X", "ask flaky for X", "open flaky" then "play X". "play X on flaky" does not: Alexa keeps that form for its own music services.

## Security

- Every request must carry Amazon's signature. FLACie checks the certificate address (`https://s3.amazonaws.com/echo.api/...`, port 443), downloads the certificate, checks it is in date, names `echo-api.amazon.com` and chains to a trusted authority, checks the signature of the exact bytes received, requires a timestamp within 150 seconds and your skill id. Anything else gets HTTP 400 and a one-line log entry (a reason, never the request).
- Allowlist only. An Alexa account is served only after it said a pairing code made by a signed-in person. *Revoke access* in Settings (each person for their own links, administrators for all) removes the stored sign-in at once.
- The Jellyfin sign-in behind a link is stored encrypted with the server's data-protection keys (in `/data`). It is never written to a log, never put in a URL and never sent to Amazon.
- Songs reach the Echo through short-lived signed links (`/alexa/stream/t/<song>.mp3?u=...&e=...&s=...`, six hours). The link proves itself with a signature; it holds no Jellyfin token. Anyone who gets a link can stream that one song until it expires, which is how Echo devices work (they cannot send a password).
- Requests are rate limited per address and per Alexa account, and Jellyfin is only ever asked through the same request gate as the rest of FLACie (library listing and catalog export make no call per song; each song that plays is one proxied stream or transcode, like the browser player).
- Turn the download switch off in Settings and Alexa can never start a download.

## Limitations

- Needs the words "ask FLACie" (or the skill already open). It cannot be Alexa's default music service (see PLAN.md for the one thing that might change that: the "later" probe).
- FLAC and ALAC are not played by Echo devices, so FLACie asks Jellyfin to turn them into 256 kbps MP3 on the fly (MP3 files are sent as they are). Seeking inside a transcoded song, and resuming at an exact second, are not verified on a real Echo.
- Not available to a custom skill: Amazon alarms and routines that play "from FLACie", multi-room groups, the sleep timer as a skill feature ("Alexa, stop playing in 30 minutes" is a device feature: try it), lyrics, podcasts and radio.
- The Echo shows in FLACie's device list (the devices button in the player bar) with what it was last told to play, and "Play here" hands that song over to the browser. FLACie cannot start or stop an Echo by itself: a skill can only answer Alexa's requests.
- Songs downloaded for Alexa have to be scanned by Jellyfin before they can be played; "is it ready?" says when that is still going on.
- US English and UK English are in the interaction model (en-US, en-GB). Other languages would need their own model.
- A library with 10,000 songs answers a request in about 20 ms and exports its catalog in about one second; an Echo gives a skill about 8 seconds.

## Troubleshooting

| What you see | What to check |
|---|---|
| "There was a problem with the requested skill's response" | Settings > Alexa: is it enabled and is the skill id right? FLACie's log has `Alexa request refused: <reason>`. `wrong-skill` means the id is missing or different; `stale-timestamp` means the server clock is off; `cert-fetch-failed` means the server cannot reach `s3.amazonaws.com`. |
| "To use FLACie, open Settings..." | The Alexa account is not linked yet (or you revoked it): make a new code. |
| It answers but nothing plays | Open the stream address in a browser (it is in the developer console's test output): it must download as audio. The address must be https on 443 with a trusted certificate. |
| "I'm still loading your library" | The library of that account is being read from Jellyfin; try again in a moment. |
| Nothing found for a name you know | Spell it as it sounds. Check `/api/alexa/catalog?type=AMAZON.MusicRecording` (signed in) to see the names and spoken forms FLACie knows. |

## For developers

- Code: `ipodsync/src/FLACie.Server/Alexa/` (verification `AlexaRequestVerifier`, pairing `AlexaStore`, voice matching `VoiceMatcher`/`Phonetic`, logic `AlexaEngine`, Alexa JSON `AlexaSkill`, session glue `AlexaHost`, doors `AlexaEndpoints`, catalogs `AlexaCatalog`). The engine has no HTTP or Alexa JSON in it, so a music-skill adapter can be added next to `AlexaSkill`.
- Tests: `dotnet test ipodsync/tests/FLACie.Tests --filter FullyQualifiedName~Alexa` (unit), and `node ipodsync/tests/alexa-e2e/run.mjs` (end to end against the mock Jellyfin with requests signed by a test authority; add `BIG_SONGS=10000` for the load test). Neither can use a real Amazon signature, a real Echo, or hear anything: see [alexa/BY-EAR.md](alexa/BY-EAR.md) for what only a person can check.
