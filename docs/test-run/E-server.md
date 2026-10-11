# Test run 2026-10-11, workstream E-server

Baseline: beta 49. Branch `fix/server-e`. Black-box probing was done against a Release publish with the mock Jellyfin (ports 5390/8296, stopped afterwards).
xUnit: 63 tests, all pass (was 20). Playwright spec `1c-api.spec.ts` was written but NOT run (usage limit); it is untested.

## Results

| Item | Result | Evidence |
|---|---|---|
| Anonymous /api/* (account, import, settings, charts, played, alac, audiofacts, downloads, cancel) | pass | all 401 (probe) |
| /api/ping, /healthz anonymous | pass | 200, ping reveals only name+import |
| /stream, /art, /api/whoami anonymous | pass | 302 to /login |
| Wrong HTTP method | pass | 405 with Allow |
| /debug/* with FLACIE_DEBUG off | pass | 404 |
| /art key traversal (`..%2f`, `\`, 401+ chars) | pass | 400; file name is a SHA1 of the key, no traversal possible |
| /art external URL SSRF | pass | only https dzcdn/mzstatic/deezer/lidarr.audio hosts, others 404 |
| /stream path scheme / foreign URL | pass | 404 (only library paths) |
| /stream Range (206, suffix, past end 416, inverted ignored) | pass | probe + ByteRangesTests |
| /stream NAS path, empty file, other unit | fixed | BUG 5 |
| Cookie flags | pass (note) | HttpOnly, SameSite=Lax; no Secure unless request is https (proxy proto honoured) |
| ReturnUrl open redirect | pass | login page never reads ReturnUrl, sign-in always goes to `/` |
| Security headers | pass (note) | nosniff, frame SAMEORIGIN, referrer, HSTS on https. No Content-Security-Policy (not set). |
| Antiforgery | note | /auth/* POSTs and /api POSTs call DisableAntiforgery (login CSRF possible, SameSite=Lax limits cookie-bearing cross-site posts) |
| Sign-in throttle per client address, never per name | pass | 8 failures per IP block; per name only delays; existing smoke spec covers |
| X-Forwarded-For from an untrusted sender | pass | ignored (10 failures with spoofed XFF were blocked) |
| FLACIE_TRUSTED_PROXIES parsing | not run | read only |
| Unlocked server (no FLACIE_JELLYFIN_URL) | note, open | anonymous sign-in form is a connect/refuse oracle for any host (error text shows the .NET socket message); bounded by the 8-failure throttle; documented design, set FLACIE_JELLYFIN_URL |
| /api/import size | fixed | BUG 7 |
| /api/played auth, via validation, length caps, Jellyfin via with toggle off | pass | 401 anon; bad via/event/key>600 400; via=Jellyfin and toggle off 204 no-op; long title/device cut. Event validated before recording (BUG 2) |
| Error pages / exception leakage | pass | /error is a fixed page, no stack text |
| Notifier wording 401/403/429/500/404, timeout 10 s, toggles, corrupt file, bad token blob, concurrency | pass | NotifierEdgeTests |
| Notifier token in logs/UI/errors | pass | tests |
| Play tracker memory / clock back / threads | fixed + pass | BUG 3, tests |
| Download digest burst / threads | fixed + pass | BUG 3 |
| DownloadLog concurrent append, corrupt line, id traversal | fixed + pass | BUG 6 |
| Retry / Retry all (latest failed only, albums skipped, max) | pass | DownloadTests |
| Digest Kind filter (only Search) | pass (read) | DownloadManager.Finish; success path needs network, not unit tested |
| File mover route in /stream | not run | needs a file mover service |
| YouTube source | not run | excluded by instruction |
| Jellyfin 10.10.x / 12.x in Docker | blocked | 2.1 GB free RAM (< 5 GB) |
| Mock jf-compat.mjs | not run | |
| Playwright web-smoke (37 tests + new 1c-api) | not run | usage limit; 1c-api.spec.ts untested |
| `ssh frank docker logs flacie-web` | not run | usage limit |

## Bugs

| # | Bug | Repro | Cause | Fix |
|---|---|---|---|---|
| 1 | Profile corrupted, library fails to load ("An item with the same key has already been added. Key: account") | import a playlist, which saves the profile from several tasks | SaveAsync mutated/serialised the lazily initialised JsonObject from several threads | 84ac43b (ProfileSaveTests fails before, passes after) |
| 2 | /api/settings with bad JSON, wrong type, `[null]` gave a 500 page; /api/played recorded source before validating event | POST `{"prefetch":"x"}` | unchecked GetValue | 66160c5 |
| 3 | Play tracker `via` entries never freed (every stream / progress report), unlimited keys, tracked while off; digest O(n^2) per burst | many /api/played keys | Served without Begin never swept | b928384 |
| 4 | Header injection into ntfy request via a token with CR LF | token `abc\r\nX-Evil: 1` | token sent verbatim | ff37814 |
| 5 | NAS stream: empty file 416, `items=` treated as bytes; `/art/nf<bad base64>` 500 | Range header / key | duplicated range code, uncaught FormatException | 223790e |
| 6 | Download log line lost when two downloads finish together; import cancel by id before ownership check | parallel Add | unlocked append, swallowed IOException | b17c43a |
| 7 | Chunked /api/import over 30 MB gave 500; X-Emby-Token with quotes/commas was pasted into the Jellyfin Authorization header | probe | no body limit on chunked; no token validation | 43846b3 |

### Open / noted, not fixed
- Unlocked server connect-oracle (see table), by design.
- First /stream after a cold start answers 404 until the library has loaded (session loads lazily on first use).
- No CSP header; login/logout/QC endpoints have antiforgery disabled.
- Who() asks Jellyfin /Users/Me on every API call (no cache, amplification of anonymous calls on a fixed server).
- /art misses cache is global across users; art disk cache unbounded for lookup keys (authenticated only).
- Android app's use of via=Jellyfin not checked (android/ out of scope).

## Endpoint inventory

| Endpoint | Auth | Notes |
|---|---|---|
| GET /api/ping | none | |
| GET/POST /api/import, POST /api/import/{id}/cancel | token | 20 MB cap |
| GET/POST /api/settings | token (POST admin) | |
| POST /api/charts/run | token, admin | |
| GET /api/charts/{id:int} | token | |
| GET /api/account, /api/downloads, /api/audiofacts, /api/alac | token | |
| POST /api/played | token | via NAS/direct/Jellyfin |
| GET /api/whoami | cookie | |
| GET /stream?p, GET /art/{key} | cookie | |
| POST /auth/jellyfin, /auth/nas, /auth/qc/start, /auth/qc/poll, /auth/logout | none | throttled on failure |
| GET /healthz | none | |
| /debug/login, token, library, jf | FLACIE_DEBUG=1 + loopback | |
| Razor pages: / /login /error /songs /albums /artists /genres /charts /favorites /playlists /search /devices /settings /account /dashboard(/admin) /downloads /import /jam /headphones /explore /artist/{n} /genre/{n} /album/{k} /playlist/{id} | cookie (login, error anonymous) | |
