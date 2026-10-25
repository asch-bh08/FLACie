# A free AI DJ for FLACie: research

Written 2026-10-25 on branch `research/ai-dj` (cut from `origin/ui/living-sleeve` at `2f2241c`). Research only: no app code was changed, nothing was deployed, and frank was not touched.

How sure each number is: nobody has published Piper or Kokoro timings on an Intel N100 or NUC, so speed figures for frank are **(estimate)**, scaled from the nearest measured hardware. The free-tier allowance numbers come straight from the providers' pricing pages. The code references were checked against this branch.

---

## 1. Summary for the owner

**Yes, a free AI DJ is doable, and it needs nothing new on frank except one small library.**

- **The lines:** written from templates filled with facts FLACie already has: title, artist, album, year, genre, and the new engine's "why". Appendix A has 60 ready-made templates. An AI rewording step is optional, for later.
- **The voice:** made on frank inside the existing `flacie-web` container, with no second container. It uses **Piper** voices through a free .NET package (sherpa-onnx), at roughly **1 second per line** on a NUC-class CPU (estimate).
- **Free hosted option:** your Cloudflare account's free Workers AI allowance would cover about **6,400 lines a day** with its MeloTTS voice. A busy family day is about 150. Cloudflare can also write the lines with a decent 8B model, for about 2% more of the allowance. This is optional, and you'd need to create the key yourself.
- **Playback on phone, car, web and Windows:** a small second player says the line over the start of the next song while the music dips. Nothing is added to the queue, so the queue, Android Auto and play counts stay exactly as they are.
- **Playback on Alexa:** the server glues the spoken clip onto the front of the next song's audio. Alexa can't speak between queued songs, and a separate short clip risks the music stopping.
- **Fallback:** the phone's or browser's own voice, if frank can't make a clip.

**Cost:** £0. **Effort:** about 8 to 9 days for everything in version 1. **Biggest risks:** the lines being cringe (kept rare and calm), Alexa's handling of glued-on clips (needs testing on a real Echo), and licences, which only matter if FLACie builds are ever distributed.

**You decide:** the voice (listen to samples), local or Cloudflare, how chatty, and whether Alexa is in v1. The full list is in section 9.

---

## 2. What FLACie already has

- **Song facts:** `Track` has Title, Artist, Album, AlbumArtist, Year, Genre and DurationMs (`ipodsync/src/FLACie.Core/Track.cs:11`). Title clean-up already exists: `Catalog.BaseTitle`, `Phonetic.StripTags` / `SpeechSafe` (Alexa's spoken titles), `TrackTitles.CleanAll` / `IsFileName`, and `Matching.FeatParen` / `FeatTail`.
- **Taste:** `Recommender.cs` / `TasteProfile` builds a taste profile. `/api/recommend/next` and its "why" text, and the spec `2026-10-25-recommendation-engine-design.md`, are **not on any pushed branch**. They're still on the other PC. This doc assumes each pick comes with a short "why" text.
- **Server image:** .NET 9 on `aspnet:9.0` (Debian 12 "bookworm-slim"), with **no ffmpeg**.
- **Android:**
  - Media3 with ExoPlayer audio focus (`PlayerController.kt:138`).
  - Output volume is already one place: `applyOutputVolume()` = volume × fade × crossfade × loudness (`PlayerController.kt:280`), which is the natural hook for ducking.
- **Alexa:**
  - Songs are served as 256 kbps MP3 through signed URLs (`AlexaEndpoints.cs:136-138`, `SignedUrls.cs`).
  - Queue tokens are `serial:index:pass` (`AlexaEngine.cs:77`).
  - The next song is queued with ENQUEUE on `PlaybackNearlyFinished` (`AlexaEngine.cs:385-400`, `AlexaSkill.cs:163-164`).

---

## 3. Voices (text-to-speech)

### 3a. Free, no account

| Option | Runs on | Licence | Sound (my judgement, I can't hear) | Speed per line | Size | Notes |
|---|---|---|---|---|---|---|
| **Piper** (medium voices) | frank, inside flacie-web via [sherpa-onnx NuGet](https://www.nuget.org/packages/org.k2fsa.sherpa.onnx) | Piper GPL-3.0; voices "personal use and research", check each MODEL_CARD ([voices](https://github.com/OHF-Voice/piper1-gpl/blob/main/docs/VOICES.md)) | Natural, a little flat | RTF ~0.06 on "an Intel Core i5" ([HA blog](https://www.home-assistant.io/blog/2023/04/27/year-of-the-voice-chapter-2/)); 0.36 on a Pi 4 ([sherpa RTF](https://k2-fsa.github.io/sherpa/onnx/tts/pretrained_models/rtf.html)). **~1 s per 5 s line on an N100 (estimate)** | 63 MB medium, 114 MB high; container ~200-300 MB RAM ([review](https://mustafa.net/2026/08/31/piper-review-after-6-months-running-local-tts-in-production/)) | **Recommended first voice** |
| **Kokoro-82M** | frank, same NuGet | Apache-2.0 weights ([repo](https://github.com/hexgrad/kokoro)) | The most natural of the free local voices | RTF 3.2 on a Pi 4; ~0.5 on 4 EPYC cores ([sherpa RTF](https://k2-fsa.github.io/sherpa/onnx/tts/pretrained_models/rtf.html), [benchmark](https://reddit.sentinel-team.org/posts/1tgo96d/snapshots/2026-05-22T21%3A16%3A06.79106Z)). **~5-8 s per line on an N100 (estimate)** | fp32 326 MB, int8 114 MB (int8 was slower and noisier on ARM, [sherpa #3754](https://github.com/k2-fsa/sherpa-onnx/issues/3754)); ~1 GB RAM (estimate) | Works because clips are made minutes ahead, but heavy on a small CPU. Upgrade option |
| **Android `TextToSpeech`** | the phone | Free, built in | Good with Google TTS | Instant | Voice packs are user downloads | Retro handhelds may have **no engine at all**: Pico was disabled in AOSP in 2018 ([log](https://android.googlesource.com/platform/external/svox/+log/refs/heads/main)). The app needs a `<queries>` entry for `TTS_SERVICE` (Android 11+, targetSdk 33) ([package visibility](https://developer.android.com/training/package-visibility/use-cases)) |
| **sherpa-onnx TTS engine APK** | the phone | Apache-2.0 code, licence varies per voice | Piper/Kokoro quality on-device | Depends on device | 44-178 MB ([APK list](https://k2-fsa.github.io/sherpa/onnx/tts/apk-engine.html)) | Installs as a system TTS engine on phones with none |
| **Web Speech API** | browser and Windows | Free, built in | Edge "Natural" voices are good; Chrome's are mixed | Instant | none | Can't be captured or mixed; needs a user click first ([MDN](https://developer.mozilla.org/en-US/docs/Web/API/Web_Speech_API/Using_the_Web_Speech_API)). Fallback only |

Ready-made TTS servers exist, but each needs a **second container** on frank:
- [Kokoro-FastAPI](https://github.com/remsky/Kokoro-FastAPI): Apache-2.0, CPU image 1.56 GB, returns MP3.
- [wyoming-piper](https://github.com/rhasspy/wyoming-piper): no HTTP, no MP3.
- [Speaches](https://speaches.ai/installation/): MIT, Kokoro, MP3.
- [openedai-speech](https://github.com/matatonic/openedai-speech): archived.

Running the voice **inside flacie-web through sherpa-onnx** avoids a second container. The NuGet package (Apache-2.0) includes linux-x64 native libraries (10 MB) and has C# examples for [Kokoro](https://raw.githubusercontent.com/k2-fsa/sherpa-onnx/master/dotnet-examples/kokoro-tts-lexicon/Program.cs) and [Piper](https://github.com/k2-fsa/sherpa-onnx/tree/master/dotnet-examples/offline-tts-phonemize). The voice files would be downloaded into `/data` on first use, not built into the image.

### 3b. Free hosted (needs an account and a key the owner creates)

How much DJ each one gives, assuming an 80-character, 5-second line and about 150 lines on a busy family day:

| Service | Free amount | Lines per day | Catch |
|---|---|---|---|
| **Cloudflare Workers AI, MeloTTS** | 10,000 neurons a day (Free and Paid plans, resets 00:00 UTC). MeloTTS = 18.63 neurons per audio minute ([pricing](https://developers.cloudflare.com/workers-ai/platform/pricing/)) | **~6,400** (a busy day uses ~2%) | Voice quality unknown to me. Titles and artists go to Cloudflare. Allowance shared with any other Workers AI use. Believed to fail rather than bill on the Free plan (not confirmed) |
| Cloudflare, Deepgram Aura-1 / Aura-2 | 1,364 / 2,727 neurons per 1k characters (same page) | ~90 / ~45 | Runs out by mid-afternoon |
| Azure Speech free tier (F0) | 500k neural characters a month, 1 request at a time ([pricing](https://azure.microsoft.com/pricing/details/cognitive-services/speech-services)) | ~200 | Excellent UK voices; Azure sign-up usually asks for a card |
| Google Cloud TTS | ~1-4 M characters a month by voice type ([pricing](https://cloud.google.com/text-to-speech/pricing)) | plenty | Billing must be on (card); charges past the free amount |
| Gemini TTS free tier | ~10-15 requests a day ([pricing](https://ai.google.dev/gemini-api/docs/pricing)) | ~10 | Far too few; free-tier data is used by Google |
| ElevenLabs Free | 10k credits a month ([pricing](https://elevenlabs.io/pricing)) | ~4 | Non-commercial, must credit them |
| Edge "Read aloud" (edge-tts) | unlimited, no account | plenty | Unofficial; could be blocked any day. Not recommended |

**Cloudflare MeloTTS is the only hosted option that is truly free, with room to spare.** Keep Piper as the fallback for when it's down or the allowance runs out.

---

## 4. Writing the lines

| Approach | Cost | Speed | Quality | Risk |
|---|---|---|---|---|
| **Templates + facts + "why"** (Appendix A) | free | instant | Calm and predictable; varied with 60 templates and rotation rules | Can feel samey after weeks |
| Local LLM on frank (Ollama) | free | Llama 3.2 1B ~18 tok/s and 3B ~11 tok/s on an N100 ([DigiKey](https://www.digikey.com/en/maker/projects/review-of-llama3.2-vs-llama3.1-performance-on-lattepanda-mu/fe2224b0153e4bd98c03ae5ebae698e4)); Qwen2.5 1.5B ~1-5 tok/s on an N100 in another test ([post](https://blog.patshead.com/2024/12/should-you-run-a-large-language-model-on-an-intel-n100-mini-pc.html)). **~2-5 s per line (estimate)** | Small models drift and invent facts | 1-2 GB RAM, CPU spikes while the family streams |
| **Cloudflare Llama 3.1 8B** (`llama-3.1-8b-instruct-fp8-fast`) | ~2 neurons per line (4,119 in / 34,868 out per M tokens, [pricing](https://developers.cloudflare.com/workers-ai/platform/pricing/)) | fast | Much better writer than anything that fits frank | Needs a token; facts go to Cloudflare |
| OpenRouter / Groq free tiers | free | fast | good | 50-100 requests a day, accounts ([OpenRouter](https://openrouter.ai/docs/api-reference/limits), [Groq](https://console.groq.com/docs/rate-limits)). Not worth it |

Local model licences:
- Qwen2.5 0.5B and 1.5B, and Qwen3 1.7B: Apache-2.0.
- Qwen2.5 3B: non-Apache research licence.
- Llama 3.2: community licence.
- Gemma 3: Gemma terms.
- Phi-4-mini: MIT.

**Decision: templates first.** An LLM is only ever allowed to *reword* a template line, never add facts, and a strict validator falls back to the template (Appendix A.7). If you want rewording, Cloudflare 8B is the better choice than a model on frank.

Rules against cringe:
- At most 18 words.
- Only true facts; never trivia.
- Speak about 1 song in 3 ("Often") or 1 in 7 ("Rare").
- Never twice in a row.
- No "!", no "vibe", no hype, no jokes, no "I" or "we".
- Stay quiet when the user is skipping, on songs under 60 s, and inside albums that play straight through.

---

## 5. Making and sending the clip

- **When:** when the engine picks the next song (minutes ahead). The server writes the line, makes the WAV, encodes it and caches it as `dj/{hash}.mp3`. If it isn't ready in time, the DJ just stays quiet.
- **Encoding without ffmpeg:**
  - Add Debian's `libmp3lame0` (356 kB, [package](https://packages.debian.org/bookworm/libmp3lame0)) to the image and call it from .NET (P/Invoke, unverified).
  - `NAudio.Lame` is Windows-only. Managed options are unvetted ([EggEncoder](https://www.nuget.org/packages/EggEncoder/4.40.0)).
  - Mono 48 kbps CBR MP3 plays on Alexa, Chrome/Edge and ExoPlayer ([Alexa](https://developer.amazon.com/en-US/docs/alexa/custom-skills/use-long-form-audio.html), [ExoPlayer](https://developer.android.com/media/media3/exoplayer/supported-formats)).
  - The Alexa copy is encoded to match the song stream (256 kbps) so the two can be joined.
- **Size:** a 4 s clip is about 25 KB, so cache cost is nothing.
- **Privacy:** clips contain only song facts and the "why". Alexa URLs are signed like the songs' are. For Alexa, drop "you play X a lot" parts.

---

## 6. Playing it on each platform

| Platform | How | Why this way |
|---|---|---|
| **Android phone** | A **second small player** with `handleAudioFocus=false` plays the clip over the start of the next song. A new duck factor in `applyOutputVolume()` (`PlayerController.kt:280`) ramps the music to about 25% and back, modelled on the 150 ms crossfade ticker (`:283-294`). Stop the clip on pause, skip, seek, `clearQueue` (`:606`) and `onTaskRemoved` (`PlaybackService.kt:224`) | Putting the clip **in the queue** would break about 10 places that assume one queue item per song: `queue`/`queueIndex`, `persistNow`, `onAutoAdvance` firing twice (the Jam would advance twice), the end-of-track sleep timer, `checkQueueLow` and the shuffle order. A second player that took focus would make ExoPlayer **pause the music** (focus loss), so focus must stay off ([AudioFocusManager](https://github.com/androidx/media/blob/release/libraries/common/src/main/java/androidx/media3/common/audio/AudioFocusManager.java)) |
| **Android Auto** | Same overlay. The car screen keeps showing the real song, so there's no metadata change and no notification | Car quality rules don't mention speech. MA-1 says no audio without a user action, which is met because the DJ only speaks inside playback the user started ([quality](https://developer.android.com/docs/quality-guidelines/car-app-quality)). If clips ever become queue items, Media3's `MediaConstants.EXTRAS_KEY_IS_ADVERTISEMENT` hides the change notification ([safeguards](https://developer.android.com/training/cars/media/distraction-safeguards)) |
| **Web / Windows** | A second `<audio>` element plus a Web Audio gain ramp on the music | The same model as Android; `speechSynthesis` can't be mixed |
| **Alexa** | **Glue the clip onto the front of the next song** in the stream route (`AlexaEndpoints.cs:132-139`): voice then song in one stream. Fix Content-Length and Range (`:104,113`); skip the clip when the offset is over 0 (resume) and on Repeat One | Speech isn't allowed in replies to `PlaybackNearlyFinished` ([AudioPlayer](https://developer.amazon.com/en-US/docs/alexa/custom-skills/audioplayer-interface-reference.html)). A separate 3-6 s clip stream is risky: NearlyFinished can arrive 0.1-0.5 s after a short stream starts, and events come out of order ([sample #37](https://github.com/alexa/skill-sample-nodejs-audio-player/issues/37), [MA #93](https://github.com/alams154/music-assistant-alexa-skill-prototype/issues/93)). Over Funnel, the ENQUEUE for the song may miss and the music stops. Plan B, if glueing causes MP3 glitches: a separate clip stream with token `{serial}:{ni}:{np}:dj:{guid}`. `TryToken` already ignores 5-part tokens (`AlexaEngine.cs:78-83`); song B is enqueued from the clip's own NearlyFinished. Policy: don't use Alexa's or Polly's voice for clips, and keep the word "Alexa" out of them, since it would wake the device ([policy](https://developer.amazon.com/en-US/docs/alexa/custom-skills/policy-requirements-for-an-alexa-skill.html), [tests](https://developer.amazon.com/en-US/docs/alexa/custom-skills/functional-testing-for-a-custom-skill.html)) |

Note: Alexa hears voice *then* song, while the other platforms hear voice *over* the song's start. An "in the gap" mode for phone and web (hold the next song until the clip ends) would match Alexa exactly, and could be a setting.

---

## 7. Later: requests ("play something chill", "more like this")

- **Alexa:** add intents. "Chill", "party", "focus", "new" map to energy and tempo targets in the engine.
- **Android and Auto:** Media3 turns `playFromSearch` into `onSetMediaItems` with `searchQuery`. Only genre, artist, album, playlist and title extras are documented; mood isn't ([voice actions](https://developer.android.com/training/cars/media/voice-actions)), so match mood words in the query text.
  - **Existing gap:** `PlaybackService.kt:145` ignores blank queries, so "play some music" plays nothing. An empty query should mean "start something sensible".
- **Web / Windows:** "Chiller", "Livelier" and "More like this" buttons, not speech recognition (in browsers that's Google's cloud).

---

## 8. Open checks (things I couldn't do)

1. **Frank's CPU and RAM.** I wasn't allowed on frank, so please run this there and send me the output:
   ```bash
   lscpu | grep -E "Model name|^CPU\(s\)"; free -h
   ```
   N100-class: use Piper. A recent i5 or better: Kokoro is fine too.
2. **Real timings and samples.** A local benchmark on this PC was stopped on request. The first implementation step should log ms per line on frank.
3. **RG Rotate TTS engine:** run `adb shell cmd package query-services -a android.intent.action.TTS_SERVICE` (untested). No engine means no device fallback there; server clips still work.
4. **Alexa on a real Echo:** whether the glued clip plays cleanly, and resume behaviour. A one-day spike.
5. **Cloudflare free plan when the allowance runs out:** confirm requests fail rather than bill.
6. **Side finding:** `AlexaSkill.cs:194-195` sends `shouldEndSession` in replies to AudioPlayer events, which the docs say may not contain standard properties. It works today; worth checking.

---

## 9. Recommendation

**Stack**
- **Lines:** templates (Appendix A), with optional rewording by Cloudflare Llama 3.1 8B later.
- **Voice:** Piper (UK medium voice) inside flacie-web via sherpa-onnx. Optional Cloudflare MeloTTS if you prefer its sound; Kokoro later if frank's CPU can take it. All of them sit behind one `IDjVoice` interface, so swapping is a setting.
- **Clip:** mono 48 kbps MP3 via libmp3lame, made when the next song is picked and cached in `/data`.
- **Playback:** overlay plus duck on Android, Auto, web and Windows; glued onto the next song's stream on Alexa.
- **Fallback:** Android `TextToSpeech` (to a file, played by the overlay) and the web's `speechSynthesis`.

**Effort (one dev, focused days)**

| Piece | Days |
|---|---|
| 1. Line engine in Core: slots, templates, clean-up (reusing existing helpers), variety, pronunciation file, tests | 2 |
| 2. Server voice: sherpa-onnx + Piper, voice download to `/data`, libmp3lame, cache, prefetch, `/api/dj/clip` with signed URLs | 2 |
| 3. Android: overlay player, duck factor, cancel on skip/pause/seek, setting, `<queries>` | 1.5 |
| 4. Web and Windows: overlay audio, gain ramp, setting | 1 |
| 5. Alexa: glue clip onto the next song (Content-Length/Range), test on a real Echo | 1.5 |
| 6. Device TTS fallback (Android and web) | 0.5 |
| **Version 1 total** | **about 8.5** |
| 7. (Optional) Cloudflare provider: MeloTTS + 8B rewording, neuron budget, fallback | 1 |
| 8. (Later) requests: intents, mood words, buttons, plus the blank-query fix | 1.5 |

Per the repo rule, it ships on Android + web + Windows together. Alexa is the piece most likely to slip, since it needs a real device.

**Risks**
- **Cringe.** Mitigated by low frequency, calm templates, a setting per person, and staying quiet while skipping.
- **Licences.**
  - sherpa-onnx includes espeak-ng (GPL-3.0); Piper voices are "personal use and research".
  - Fine on a private family server.
  - If FLACie Docker images or installers are ever *published*, that is distributing GPL code. Kokoro with a lexicon avoids espeak-ng, but unknown words are dropped ([#3931](https://github.com/k2-fsa/sherpa-onnx/issues/3931)).
- **CPU on frank.** Piper is light; Kokoro and a local LLM are not. Clips are made ahead, so there's no live load.
- **Alexa glueing** may click at the join or upset seeking. Plan B is a separate clip stream (racy).
- **Pronunciation of odd names:** an override file (Appendix A.5).
- **Piper's project is looking for maintainers**, which is a reason to keep the voice swappable.

**Decisions for you**
1. **Voice:** listen to a few. The Piper UK candidates are `en_GB-alba-medium`, `en_GB-cori-high`, `en_GB-northern_english_male-medium` and `en_GB-alan-medium` (samples on the [Piper voices page](https://rhasspy.github.io/piper-samples/)). MeloTTS (British accent) on Cloudflare and Kokoro `bf_emma` / `bm_george` are the alternatives.
2. **Local (Piper) or Cloudflare as the main voice.** If Cloudflare, you create a Workers AI token in your dashboard and add it to frank's settings yourself.
3. **Chattiness default:** Often (about 1 in 3) or Rare (about 1 in 7), and whether each person can change it.
4. **Over the song's start, or in the gap?**
5. **OK to add `libmp3lame0` to the flacie-web image?**
6. **Alexa in v1, or v1.1?**
7. **Will FLACie builds ever be published?** That decides how much the GPL parts matter.

---

## Appendix A: line writing (draft)

One server-side implementation for every platform. Placeholders, in spoken form:
- `{T}`/`{A}`: next song's title and artist.
- `{pT}`/`{pA}`: previous song's title and artist.
- `{eT}`: a song heard earlier.
- `{Al}`: album. `{Y}`: year. `{D}`: decade. `{G}`: genre.
- `{M}`: minutes. `{W}`: the engine's "why".

### A.1 Slots (used only when their facts are known; a missing fact is never spoken)

- **OPEN:** first song of a session (30 minutes idle ends a session). **GREET** goes in front when the device time is known: 06-11 morning, 12-17 afternoon, 18-02 evening.
- **BRIDGE:** previous song played to the end and `{pA}` is known; otherwise **INTRO**.
- **WHY:** the engine gave a "why".
- **YEAR:** 1900 to now; not on compilations or remasters.
- **ALBUM:** not blank, "Unknown Album", the title itself, or Various Artists.
- **GENRE:** on a speakable list (rock, pop, jazz, soul, disco...). Not for "Other", "Unknown" or blank. "Pop;Rock" uses the first.
- **SAME ARTIST:** the next artist is the same as the previous one.
- **RETURN:** the artist was heard 5+ songs and 15+ minutes earlier.
- **RESET:** first full play after 3+ skips in the last 5.
- **LONG:** 7+ minutes.
- **ARTIST-ONLY:** the title is over 8 words or 50 characters.
- **TITLE-ONLY:** the artist is blank, "Unknown Artist" or "Various" (`TrackTitles.cs`). Unknown artist and a file-name title: stay silent.

### A.2 Templates (60)

- **OPEN:** "Starting with {T} by {A}." "First up, {T} by {A}." "Here's {A} to start, with {T}." "Let's begin with {T}, by {A}." "Opening with {A}: {T}."
- **GREET:** "Good morning." "Good afternoon." "Good evening." "{Saturday|Sunday} morning."
- **BRIDGE:** "That was {pT} by {pA}. Here's {T} by {A}." "{pT}, from {pA}. Now it's {A}, with {T}." "After {pA}, here's {A} with {T}." "That was {pT}. Next up, {T} by {A}." "You heard {pA} there. This is {T} by {A}." "From {pA} to {A}: this is {T}." "That was {pT}. Here's {A} next, with {T}."
- **INTRO:** "Here's {T} by {A}." "Next, {T} by {A}." "This is {A}, with {T}." "Up next, {T} from {A}." "Now, {A}: {T}."
- **WHY:** "{W}. Here's {T} by {A}." "{W}, so here's {T} by {A}." "Next, {T} by {A}. {W}." "This is {T} by {A}: {W1}." "Picked because {w}: {T} by {A}." "That was {pT}. {W}, here's {T}." "Chosen for you: {W1}. This is {A} with {T}."
- **YEAR:** "From {Y}, this is {T} by {A}." "Back to {Y} now: {T} by {A}." (10+ years old) "{T} by {A}, from {Y}." "Here's {A} in {Y}, with {T}." "A {D} one now: {T} by {A}." "Something from the {D}: {A}, {T}." ({D} only for 1950-1999)
- **ALBUM:** "From the album {Al}, here's {T} by {A}." "{T}, by {A}, from {Al}." "Here's {A} with {T}, from {Al}." "Another from {Al}: {T}." (same album) "From the {Al} soundtrack, this is {T} by {A}." (soundtrack)
- **GENRE:** "Some {G} now: {T} by {A}." "Staying with {G}, here's {T} by {A}." (same genre) "A bit of {G} next: {A}, with {T}."
- **SAME ARTIST:** "More from {A}: this is {T}." "Staying with {A}, here's {T}." "{A} again, with {T}." "Another from {A} now: {T}."
- **RETURN:** "Back to {A} now, with {T}." "Returning to {A}: {T}." "We heard {A} earlier with {eT}. Now it's {T}."
- **RESET:** "Let's try something different: {T} by {A}." "A change of direction now. Here's {T} by {A}." "Something else, then. This is {A}, with {T}."
- **LONG:** "A long one now, about {M} minutes: {T} by {A}." "Settle in, this one's about {M} minutes: {T} by {A}."
- **ARTIST-ONLY:** "Here's {A}." "Next, something from {A}." "This one's from {A}."
- **TITLE-ONLY:** "Here's {T}." "Next, {T}." "This one's called {T}."

**The "why", spoken:**
- Split on "·" and keep at most 2 parts. Drop a part with digits, a banned word, or 9+ words.
- `{W}` joins the parts with "and": "Same energy as Out of Space, and you play The Prodigy a lot".
- `{w}` is the same in lower case. `{W1}` is part one only.
- Keep the engine's "you". The DJ never says "I" or "we". "New to you" becomes "Something new for you".
- Better still: the engine sends each part as `{kind, text, refTrackId}`.
- On Alexa, drop the listening-habit parts.
- Over 18 words: drop part two, then the "That was" half, else stay silent.

### A.3 Clean-up before speaking (overrides from A.5 are matched first)

1. `Catalog.BaseTitle` (`FLACie.Core/Catalog.cs`) drops "(feat. X)", "(Remastered 2011)", "[Deluxe]" and "- 2009 Remaster", and keeps "(Live)". `Phonetic.StripTags` (`FLACie.Server/Alexa/Phonetic.cs:26`, already used for Alexa's spoken titles) also drops "[Live]" and "(Radio Edit)". Move it to Core and share it.
2. **feat.:** titles via `FeatParen`/`FeatTail` (`Track.cs:25-26`). Artists become "{lead} with {guest}" if it fits. Don't use `ArtistSplit` (`Track.cs:28`): it splits on "&", which breaks Simon & Garfunkel.
3. **"&" and "+" become "and".** `Phonetic.SpeechSafe` already does "&" and strips SSML characters.
4. **Keep "The" as tagged.** `PrimaryArtist` / `AlbumNorm` drop it, so they're lookup keys only, never spoken. `Edition` (`Track.cs:29`) drops "(Deluxe Edition)". A soundtrack suffix sets the soundtrack flag.
5. **File-name titles:** `TrackTitles.CleanAll` strips "06 - " on load. If `IsFileName` is still true, don't speak the title. Keep digits in real titles ("1979").
6. **Capitals and symbols:** words of 4+ capitals become Title Case (ABBA becomes Abba); shorter ones are spelt out. Drop "!", "?", quotes and emoji.
7. **Long artist names:** over 6 words, say the lead artist only.
8. **Years written out:** "nineteen ninety-seven", "nineteen oh five", "two thousand and five", "twenty twelve".

### A.4 Variety and frequency

- **Repeats:**
  - Never two lines in a row.
  - No template again within the last 12 lines.
  - No slot twice running.
  - Opening words unlike the last 3.
  - WHY at most 3 of every 6 lines.
- **Slot weights:** SAME ARTIST, RETURN and RESET 4; WHY 3; BRIDGE and INTRO 2; others 1.
- **Memory and randomness:** per-account memory of the last 20 lines on the server, shared by phone, web and Alexa. Seeded randomness keeps prefetch and tests repeatable.
- **Off:** never speaks.
- **Often** (default, about 1 in 3.2): one forced silent transition, then p = 0.45 each; OPEN always.
- **Rare** (about 1 in 7.3): four forced silent transitions, then p = 0.30; OPEN p = 0.5.
- **Silent without a draw** when:
  - the next song is under 60 s;
  - a song was skipped within 10 s in the last 2 transitions;
  - the change is not an auto-advance;
  - it's within 2 transitions of a voice request;
  - the next song is the next album track in order (gapless);
  - it's spoken word;
  - the clip isn't ready.

### A.5 Pronunciation overrides

`dj-pronounce.tsv` on the server has these columns: written, kind (artist/title/word), say (respelling), and an optional ipa. Screens keep the real spellings.

```
Sigur Rós    artist  Siggur Rose
Beyoncé      artist  Bee-yon-say  biˈjɒnseɪ
Mötley Crüe  artist  Motley Crew
CHVRCHES     artist  Churches
MØ           artist  Mur
deadmau5     artist  dead mouse
Sia          artist  See-a        ˈsiːə
Dvořák       word    Dvor-zhak
SOS          title   S O S
```

- **Tune by ear:** these respellings are starting points.
- **Piper and device TTS:** get the respelling.
- **Kokoro:** takes `[Beyoncé](/biˈjɒnseɪ/)` when ipa is set (verify).
- **Alexa:** clips are our MP3. Its own SSML replies use `<sub alias="…">` or `<phoneme alphabet="ipa">`, as `AlexaSkill.cs` already does for "flacky".

### A.6 Example lines

1. Saturday morning. Starting with Mr. Blue Sky by Electric Light Orchestra.
2. Staying with disco, here's Le Freak by Chic.
3. This is Breathe by The Prodigy: same energy as Out of Space.
4. Something new for you, and close to Florence and the Machine. Here's Bloom by The Paper Kites.
5. From nineteen ninety-seven, this is Bitter Sweet Symphony by The Verve.
6. A seventies one now: SOS by Abba.
7. From the album Parachutes, here's Yellow by Coldplay.
8. From the Frozen soundtrack, this is Let It Go by Idina Menzel.
9. More from Taylor Swift: this is Shake It Off.
10. Back to Arctic Monkeys now, with Do I Wanna Know.
11. Let's try something different: Valerie by Mark Ronson with Amy Winehouse.
12. A long one now, about twelve minutes: I'd Do Anything for Love by Meat Loaf.
13. Next, something from Antonín Dvořák.
14. Next, The Wheels on the Bus.
15. Halo, from Beyoncé. Now it's CHVRCHES, with The Mother We Share.

### A.7 Optional LLM rewording

Run it with Cloudflare `llama-3.1-8b-instruct-fp8-fast`, or locally with `qwen2.5:1.5b` / `llama3.2:1b`. Settings: temperature 0.6, made ahead, 8 s timeout.

```
SYSTEM: Reword one line said by a calm British radio presenter on a family music app.
Same facts, nothing added. Keep TITLE and ARTIST exactly. No other names or numbers.
At most 18 words. No exclamation marks, questions, jokes, slang or hype words.
Reply with the line only.
USER: TITLE: Bitter Sweet Symphony | ARTIST: The Verve
LINE: From nineteen ninety-seven, this is Bitter Sweet Symphony by The Verve.
```

**Validator.** On any failure, use the template line; after 3 failures in a row, turn rewording off for an hour.
1. One line, 4-18 words, ending in ".".
2. Contains the exact `{T}` and `{A}` (case-sensitive) when the template has them.
3. Every capitalised word mid-sentence appears in the template line.
4. No digits except inside `{T}`/`{A}` or the year.
5. No "!" or "?". Outside the names, none of: vibe, banger, tune, epic, amazing, awesome, iconic, love, favourite, I, we, my.
6. Not one of the last 12 lines.

---

## Sources

**Voices**
- Piper: [piper1-gpl](https://github.com/OHF-Voice/piper1-gpl), [voices](https://github.com/OHF-Voice/piper1-gpl/blob/main/docs/VOICES.md), [samples](https://rhasspy.github.io/piper-samples/).
- Kokoro: [kokoro](https://github.com/hexgrad/kokoro), [kokoro-onnx PR #198](https://github.com/thewh1teagle/kokoro-onnx/pull/198).
- sherpa-onnx: [repo](https://github.com/k2-fsa/sherpa-onnx), [NuGet](https://www.nuget.org/packages/org.k2fsa.sherpa.onnx), [RTF table](https://k2-fsa.github.io/sherpa/onnx/tts/pretrained_models/rtf.html), [engine APKs](https://k2-fsa.github.io/sherpa/onnx/tts/apk-engine.html), [#3754](https://github.com/k2-fsa/sherpa-onnx/issues/3754), [#3931](https://github.com/k2-fsa/sherpa-onnx/issues/3931).
- Benchmarks: [Home Assistant on Piper](https://www.home-assistant.io/blog/2023/04/27/year-of-the-voice-chapter-2/), [Piper review](https://mustafa.net/2026/08/31/piper-review-after-6-months-running-local-tts-in-production/).
- TTS servers: [Kokoro-FastAPI](https://github.com/remsky/Kokoro-FastAPI), [wyoming-piper](https://github.com/rhasspy/wyoming-piper), [Speaches](https://speaches.ai/installation/).
- Browser: [MDN Web Speech](https://developer.mozilla.org/en-US/docs/Web/API/Web_Speech_API/Using_the_Web_Speech_API).

**Free tiers:** [Cloudflare Workers AI pricing](https://developers.cloudflare.com/workers-ai/platform/pricing/), [Azure Speech](https://azure.microsoft.com/pricing/details/cognitive-services/speech-services), [Google TTS](https://cloud.google.com/text-to-speech/pricing), [Gemini](https://ai.google.dev/gemini-api/docs/pricing), [ElevenLabs](https://elevenlabs.io/pricing), [OpenRouter](https://openrouter.ai/docs/api-reference/limits), [Groq](https://console.groq.com/docs/rate-limits).

**Local LLMs:** [DigiKey N100](https://www.digikey.com/en/maker/projects/review-of-llama3.2-vs-llama3.1-performance-on-lattepanda-mu/fe2224b0153e4bd98c03ae5ebae698e4), [N100 post](https://blog.patshead.com/2024/12/should-you-run-a-large-language-model-on-an-intel-n100-mini-pc.html), [It's FOSS](https://itsfoss.com/testing-local-llms-without-gpu), [Ollama qwen2.5](https://ollama.com/library/qwen2.5).

**Encoding:** [libmp3lame0](https://packages.debian.org/bookworm/libmp3lame0), [NAudio.Lame](https://github.com/Corey-M/NAudio.Lame), [EggEncoder](https://www.nuget.org/packages/EggEncoder/4.40.0).

**Android:**
- Media3: [playlists](https://developer.android.com/media/media3/exoplayer/playlists), [supported formats](https://developer.android.com/media/media3/exoplayer/supported-formats), [AudioFocusManager](https://github.com/androidx/media/blob/release/libraries/common/src/main/java/androidx/media3/common/audio/AudioFocusManager.java), [MediaConstants](https://github.com/androidx/media/blob/release/libraries/session/src/main/java/androidx/media3/session/MediaConstants.java).
- Android Auto: [media apps](https://developer.android.com/training/cars/media), [distraction safeguards](https://developer.android.com/training/cars/media/distraction-safeguards), [car quality](https://developer.android.com/docs/quality-guidelines/car-app-quality), [voice actions](https://developer.android.com/training/cars/media/voice-actions).
- Platform: [AudioFocusRequest](https://developer.android.com/reference/android/media/AudioFocusRequest), [package visibility](https://developer.android.com/training/package-visibility/use-cases), [svox log](https://android.googlesource.com/platform/external/svox/+log/refs/heads/main).

**Alexa:**
- Amazon docs: [AudioPlayer](https://developer.amazon.com/en-US/docs/alexa/custom-skills/audioplayer-interface-reference.html), [long-form audio](https://developer.amazon.com/en-US/docs/alexa/custom-skills/use-long-form-audio.html), [SSML](https://developer.amazon.com/en-US/docs/alexa/custom-skills/speech-synthesis-markup-language-ssml-reference.html), [policy](https://developer.amazon.com/en-US/docs/alexa/custom-skills/policy-requirements-for-an-alexa-skill.html), [functional tests](https://developer.amazon.com/en-US/docs/alexa/custom-skills/functional-testing-for-a-custom-skill.html).
- Field reports: [sample-skill #37](https://github.com/alexa/skill-sample-nodejs-audio-player/issues/37), [Music Assistant #79](https://github.com/alams154/music-assistant-alexa-skill-prototype/issues/79), [#93](https://github.com/alams154/music-assistant-alexa-skill-prototype/issues/93).
