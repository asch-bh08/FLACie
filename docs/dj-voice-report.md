# DJ voice options on Cloudflare Workers AI (2026-10-27)

Status: research done, **sample clips NOT made**. The ssh step that would have read the Cloudflare credentials was blocked by the permission checker, and the token in `dj.json` is stored encrypted (ASP.NET Data Protection), so it cannot be used from curl anyway. See "Samples" for how to unblock.

## 1. Every text-to-speech model on Workers AI

Sources: Cloudflare model pages (developers.cloudflare.com/workers-ai/models/aura-1, aura-2-en), the Workers AI pricing page, Deepgram's voice list (developers.deepgram.com/docs/tts-models) and prompting guide (developers.deepgram.com/docs/text-to-speech-prompting).

| Model | Voices | Speed control | Neurons per 1,000 chars | Cost vs aura-1 | Lines/day at 50% share (5,000 neurons), ~100-char line |
|---|---|---|---|---|---|
| `@cf/deepgram/aura-1` | 12 (angus, asteria, arcas, orion, orpheus, athena, luna, zeus, perseus, helios, hera, stella) | None documented | 1,363.64 | 1x | about 36 |
| `@cf/deepgram/aura-2-en` | 40 (list below) | None documented | 2,727.27 | 2x | about 18 |
| `@cf/deepgram/aura-2-es` | Spanish voices | None | 2,727.27 | 2x | not relevant (Spanish) |
| `@cf/myshell-ai/melotts` | one per language, no speaker choice | None (the model has a speed option in MyShell's own library, not exposed on Workers AI) | 18.63 per audio minute (almost free) | roughly 1/100 | hundreds |

Common parameters for both Aura models: `text`, `speaker`, `encoding` (linear16, flac, mulaw, alaw, mp3, opus, aac), `container` (none, wav, ogg), `sample_rate`, `bit_rate`. There is **no speed, emotion, style or SSML parameter** on either Aura model. Aura-2 speaker names have no `-en` suffix in the request (for example `"speaker": "aries"`).

Aura-2 English speakers: amalthea, andromeda, apollo, arcas, aries, asteria, athena, atlas, aurora, callista, cora, cordelia, delia, draco, electra, harmonia, helena, hera, hermes, hyperion, iris, janus, juno, jupiter, luna, mars, minerva, neptune, odysseus, ophelia, orion, orpheus, pandora, phoebe, pluto, saturn, thalia, theia, vesta, zeus. Deepgram's table also lists `selene`, which Cloudflare's page does not; test it before relying on it.

Cloudflare's neurons figure for aura-2 (2,727.27) is exactly double aura-1, matching the dollar prices ($0.030 vs $0.015 per 1k chars). Neurons used is not returned in the audio response, so the cost above is from the price table.

## 2. Voices that suit an energetic, warm radio DJ

Traits are Deepgram's own descriptions.

Male:
- **aries** (aura-2): warm, energetic, caring. Best fit for a friendly DJ.
- **atlas** (aura-2): enthusiastic, confident, friendly. Advert-style punch.
- **apollo** (aura-2): confident, relaxed, casual. Laid-back late-night style.
- **draco** (aura-2): British, warm, approachable, deep. Closest to today's British helios but warmer.
- **hermes** (aura-2): expressive, engaging.
- Aura-1: angus (Irish, warm, friendly, natural) is the warmest aura-1 male; helios (current) is described as "professional, clear, confident", which fits the owner's "dull, heartless" complaint.

Female:
- **phoebe** (aura-2): energetic, warm, casual.
- **aurora** (aura-2): cheerful, expressive, energetic.
- **ophelia** (aura-2): expressive, enthusiastic, cheerful.
- **thalia** (aura-2): clear, confident, energetic, enthusiastic.
- **pandora** (aura-2): British, smooth, calm, melodic. Good for the calm variant.
- Aura-1: asteria (clear, confident, energetic) is the liveliest.

Slowing down: there is no speed parameter. Pacing is "context aware" and driven by the text, so slow it with punctuation (below), shorter sentences, and a calmer voice. The only real speed change would be post-processing the mp3 (for example ffmpeg `atempo=0.92`), which has to be done on the server and changes pitch handling slightly (atempo keeps pitch).

## 3. Text tricks, from Deepgram's prompting guide

- Commas and periods give very short pauses; do not overuse them.
- Ellipses `...` give longer, natural pauses (a breath, a stretched word, or silence; non-deterministic). Use groups of three dots; five dots does not register.
- Spaced dots `. . .` give pauses more likely to stay silent. Add more for longer.
- Fillers such as "um" and "uh" make it more conversational, best combined with ellipses.
- Spell out odd words phonetically; spell acronyms as spoken; "and" in numbers.
- The guide says nothing on exclamation marks, sentence length or speed. Aura-2's own description says it adjusts expressiveness to the text, so in practice (to be confirmed by listening): exclamation marks and short punchy sentences, "Alright, alright!" style openers, and an ellipsis before the song title.

Suggested prompt change for the line writer: ask for one or two very short sentences, one exclamation for upbeat songs, "..." before the title on slow songs, no semicolons.

## 4. Samples

Planned set (not made): baseline `aura1-helios`, then `aura2-aries`, `aura2-atlas`, `aura2-draco`, `aura2-phoebe`, `aura2-aurora`, `aura1-angus`, `aura2-pandora` (calm only), each with the lively line ("Alright, alright! Here's Lady Gaga with Bad Romance, turn it up!") and the calm line ("Easing it back now... this is Adele, Someone Like You."). About 16 calls, roughly 16 x 60 chars x 2.7 = under 3,000 neurons in total.

Why none exist: the auto-mode permission check refused the ssh read of the server environment (classified as credential exploration), and `dj.json` holds only an encrypted token blob. To unblock, either (a) give a Workers AI token for a one-off run, or (b) allow `ssh root@frank` to run curl against the container's `FLACIE_CLOUDFLARE_TOKEN` environment variable, then rerun this task. Output would go to `C:\FLACie\docs\dj-voice-samples\` as `aura2-<voice>-<variant>.mp3`.

## 5. Recommendation (untested by ear)

1. **aura-2-en `aries`** (male, warm, energetic). Same price tier as the others in aura-2; halves the lines per day (about 18 instead of about 36 at 100 chars, more for short lines).
2. **aura-2-en `phoebe`** (female, energetic, warm, casual), or `aurora` for more sparkle.
3. **aura-1 `angus`** (Irish, warm). Same cost as today, so no cap change; a cheap first step if the owner wants to stay on the 36-lines budget.

Cost note: aura-2 doubles the neurons per character. The server's pooled-line logic (`DjBudget`) will adapt through `EstimateNeurons`, but fewer unique lines per day will be possible. Shorter lines (about 70 characters) bring aura-2 back to roughly 26 lines/day.

## 6. Code change in `CloudflareVoice.cs`

Voices are stored as `"aura-1:<speaker>"`. Add an `aura-2:<speaker>` form:

1. `Speakers` stays for aura-1; add `public static readonly string[] Speakers2 = ["aries", "atlas", "apollo", "draco", "phoebe", "aurora", "ophelia", "thalia", "pandora", "hermes"];`
2. `NormVoice`: also accept `s.StartsWith("aura-2:") && Speakers2.Contains(s[7..])`.
3. `EstimateNeurons`: for `aura-2:` use `text.Length / 1000.0 * 2727.27` (aura-1 stays 1363.64).
4. `CallAsync`, the model/body switch becomes:
   - aura-1: model `@cf/deepgram/aura-1`, body `{ "text": text, "speaker": v[7..], "encoding": "mp3" }` (as now)
   - aura-2: model `@cf/deepgram/aura-2-en`, body `{ "text": text, "speaker": v[7..], "encoding": "mp3" }`
5. To switch, set `Voice` to `"aura-2:aries"` in the dashboard or dj.json. Bump `ConfigVersion` is not needed. Also update `DefaultVoice` only after the owner has listened.
6. Anything else that assumes `v[7..]` or the `aura-1:` prefix (dashboard voice list, pronunciation check, tests in the DJ test project) needs the same widening; grep for `aura-1:`.

Not verified against the live API: that `speaker` for aura-2-en accepts the bare name (the Cloudflare schema lists bare names, so it should), and the response is binary mp3 like aura-1.
