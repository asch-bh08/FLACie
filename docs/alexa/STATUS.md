# Alexa: status log (2026-10-14)

Branch `alexa/main-work`, merged to main at abd66af, web deployed to frank the same day (rollback tag `flacie-web:rollback-2026-10-14-pre-alexa`). Alexa is **off** on the live server until switched on in Settings > Alexa.

## Built
Verifier and rate limiter, pairing store, queue, voice matcher, engine, custom-skill adapter, signed stream and cover links, catalog exporter (for the later music-skill path), Settings section, Echo in the device picker, skill package with deploy scripts, docs. Details: [../ALEXA.md](../ALEXA.md), [PLAN.md](PLAN.md).

## Verified (by machine)
| Check | Result |
|---|---|
| .NET unit tests, whole suite | 636 pass, 0 fail (about 300 are Alexa: verifier 55, voice 114, engine 33, skill 49, catalog 7, state/security 30, model 3) |
| Existing browser suite (Playwright) on this branch | 158 pass |
| Alexa end to end (`node ipodsync/tests/alexa-e2e/run.mjs`): real server + mock Jellyfin, requests signed by a test authority | 39 of 39 |
| Same with `BIG_SONGS=10000` (10,000 song library) | 43 of 43: 200 voice requests median 16 ms, p95 21 ms; catalog export 0.9 s for 4 MB; Jellyfin asked 0 times for items during requests and export |
| Live server after deploy | `/healthz` ok; login page 200; a wrong sign-in reaches Jellyfin and says "Wrong username or password" (so the sign-in path works; I did not and cannot enter your password); `/alexa/skill` and `/alexa/stream` answer 404 while off; NAS login is `false` in the compose file |
| Independent review by a second agent | 13 findings; the 10 real ones fixed with tests (leading-zero codes, failed enqueued song, permission payload, cert URL query, locking, empty library, matcher pruning, stream error mapping, public address, unreadable state file) |

## Not verified
Real Amazon signatures, a real Echo, the `ask deploy` run, any sound or voice-recognition quality, Amazon's exact event JSON for permissions, transcoded-MP3 seeking on an Echo, whether the one-word invocation name is accepted, Tailscale path mounting. See [BY-EAR.md](BY-EAR.md).

## Known limits and later
- Needs "ask FLACie" (custom skill). Default music service needs the Music Skill API probe (PLAN.md, "Later").
- The live public address in the compose notes uses port 10000. **Alexa needs https on port 443**: use a name that serves 443 (the Caddy on frank, or Tailscale on 443) and expose only `/alexa/*`.
- ListenBrainz feedback for thumbs is not built (ListenBrainz is not in the server yet). Ratings are written as stars and to Jellyfin.
- Feature table with reasons: PLAN.md.
