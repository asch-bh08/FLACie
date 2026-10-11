# Test run H: Hi-Res badge (web)

Branch `fix/hires-web`.

## Root cause
The badge was not a function of the file's real facts alone. Several guesses and one cache bug fed it:

1. `InfoService.QuickFormat` returned "HI-RES" whenever the file name contained `[Hi-Res]`. The explicit Hi-Res download picks its Soulseek file by peer-reported depth, or by a bit rate guess (more than 1700 kbps when the peer says nothing, `Slskd.cs` `HiRes`), so a genuine 16/44.1 FLAC could be filed `[Hi-Res]` and badged Hi-Res by name, while the spectrogram (real rate) showed otherwise.
2. `InfoService.GetAsync` cached every `ReadAsync` result forever by path, including a failed read or an extension-only "FLAC, no data" fallback. A transient Jellyfin failure during a burst of skips therefore stuck for the session, and lists (facts from the index) and Now Playing/Info (cached record) could disagree for the same song.
3. The rule was written in several places (`MediaInfo.HiRes`, `AudioFacts.HiRes`, Slskd) and `LosslessCodecs` contained a fake codec "hi-res".
4. `FormatIndex.Set` replaced a good fact with any later Known one, even a worse partial one.

## What changed
- `MediaInfo.IsHiRes(lossless, depth, rate)` is the single rule; `MediaInfo.HiRes` and `AudioFacts.HiRes` call it. `MediaInfo.Known` added.
- File-name guess removed from `QuickFormat`; "hi-res" removed from `LosslessCodecs`. Every surface (TrackList, Songs, ResultSections, Explore filters, Now Playing, Info panel) derives from `QuickFormat`/`MediaInfo`, so they all read the same facts.
- `InfoService.GetAsync` caches and indexes only Known reads, retries up to 3 times (400 ms apart) in-call, and returns the neutral extension record uncached otherwise. Now Playing re-asks (4 times, 2 s apart, guarded by `infoFor`) when the source could not tell.
- `FormatIndex.Set` keeps the more complete fact (rate > depth > bit rate).
- Per-track state: `NowPlaying.LoadInfo` already guards with `infoFor`; `TrackInfoPanel` captures Info/Track once before awaiting and keys the graph by path and rate; the rate is passed per `flacie.viz` call, no shared JS field. No change needed.
- Jellyfin `MediaInfoAsync` unchanged: reports only what Jellyfin says, never guesses. Slskd ranking still uses peer-reported depth/rate and its bit rate heuristic to choose a download (not a badge).

## Tests
- .NET: `HiResTests.cs` (rule table, name vs real facts, unknown facts, partial never overwrites, no caching of failed/partial reads and retry). Full suite: 106 passed, 0 failed.
- Playwright: `1e-hires.spec.ts` against new "Hires Lab" mock songs (24/96; 16/44.1 at 1700 and 2000 kbps; "[Hi-Res]"-named 16/44.1; 24/44.1; 16/48 with a one-time HTTP 500; MP3 320; 24/192): 40 rapid skips then a slow walk; per song the badge is a single value equal to the expected one in Now Playing, Info panel (label and sampling) and the album list rows, stable across reload; `/api/audiofacts` checked. Full suite: 109 passed, 0 failed (2 new).

## Could not verify
- Real Jellyfin/Soulseek/NAS/file-mover paths (no real servers touched).
- The in-call retries mask the single mock 500; the stricter "not cached" behaviour is covered by the xUnit test.
- The spectrogram canvas itself (its axis comes from the same `Info.SampleRateHz`; not pixel-checked).
- A Jellyfin FLAC that lacks BitDepth at 44.1 kHz stays non-Hi-Res (never guessed).
