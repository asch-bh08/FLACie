# A free AI DJ for FLACie: research

Written 2026-10-25 on branch `research/ai-dj` (cut from `origin/ui/living-sleeve`, tip `2f2241c`). Research only: no app code touched, nothing deployed, nothing pushed.

Heads-up on sources: the web searches returned few hard numbers. Where a speed or quality figure is my estimate rather than something I read, it says **(estimate)**. Section 7 has a 10-minute benchmark for you to run on frank so we're not guessing.

---

## 1. Summary for the owner

**It's doable for £0.** The cheapest, most reliable way:

1. **Write the line with templates**, not an AI. FLACie already knows title, artist, album, year, genre, and (on your other PC) the engine's "why". A few hundred hand-written line patterns plus those facts sounds like a DJ and costs nothing. A small local LLM can come later as an optional polish, but I wouldn't start there: on a NUC-class CPU it's slow-ish, sometimes weird, and the templates are 90% as good.
2. **Say it with Piper or Kokoro on frank** (free, offline, runs on CPU). Frank turns the line into a short MP3 *clip* (3 to 6 seconds).
3. **Treat the clip as just another item in the queue**, sitting between two songs. That one trick works on every platform:
   - Android phone/car: play it with Media3 like any song (flag it as an "advertisement" so Android Auto doesn't flash a notification).
   - Web/Windows: same, the player plays it, with a short volume dip.
   - Alexa: queue it as its own AudioPlayer stream. (Alexa won't let you *speak* in the gap between queued songs, but it will happily play an MP3 stream there.)
4. Fallback with zero server work: the phone's/browser's built-in text-to-speech. Free, instant, sounds more robotic.

**Costs:** nothing, assuming frank has a few spare GB of RAM and an ordinary CPU. **Biggest risks:** frank's actual CPU (I couldn't check, see 7), Piper being GPL (fine for a private family server, but matters if FLACie is ever shipped as a product), and Alexa needing the clip to be a public HTTPS MP3.

**What I need from you:** pick a voice (3 samples to listen to, see 8), say how chatty the DJ should be, and whether it's OK to add a second Docker container on frank for the voice (I didn't touch frank).

---

## 2. What FLACie already has (relevant bits)

- `Track` has Title, Artist, Album, AlbumArtist, Year, Genre, Duration (`ipodsync/src/FLACie.Core/Track.cs`). Lyrics exist on Android (`Lyrics.kt`). Good enough for most lines.
- `Recommender.cs` / `TasteProfile`: builds taste from plays, favourites, playlists, Jellyfin history. The `/api/recommend/next` with a "why" string is **not on this branch**, it's on the other PC's work; the spec file `2026-10-25-recommendation-engine-design.md` is not here either. I designed around "each pick has a short why text".
- Server (`flacie-web` container) is .NET 9 on `mcr.microsoft.com/dotnet/aspnet:9.0`. **No ffmpeg in the image.** So WAV to MP3 conversion needs a managed encoder (e.g. a .NET LAME wrapper) or a sidecar that outputs MP3/Opus.
- Android player: Media3 `AudioAttributes(USAGE_MEDIA, CONTENT_TYPE_MUSIC)` with audio focus handled by ExoPlayer (`PlayerController.kt:138`).
- Alexa: the skill lives at `ipodsync/src/FLACie.Server/Alexa/AlexaEngine.cs`, uses AudioPlayer, and is reached through Tailscale Funnel `/alexa`.
- Server already keeps "the start of upcoming songs", so prefetching a DJ clip alongside the next song is in the spirit of the existing design.

---

## 3. Text-to-speech options

| Option | Where | Licence | Sound | Speed for one sentence | Size | Notes |
|---|---|---|---|---|---|---|
| **Android `TextToSpeech`** (system engine, e.g. Google TTS) | Phone, on-device | Free, part of Android | OK to good. Google's neural voices are decent; offline voices are a download | Near-instant start | Offline voice packs tens of MB, user-installed | Only as good as the engine installed. **Handhelds like the RG Rotate may have no Google TTS at all**, so check. Can speak out loud, or `synthesizeToFile` to a file ([docs](https://developer.android.com/reference/android/speech/tts/TextToSpeech)) |
| **Web Speech API** (`speechSynthesis`) | Browser, Windows | Free, built in | Varies hugely: Windows "Natural" voices in Edge are good, Chrome's are mixed | Instant | None | Can't capture the audio or mix it through Web Audio; it goes straight to the speakers. Must start from a user click first (autoplay). Voice list loads late (`voiceschanged`). Remote voices need internet and may be missing events. [MDN](https://developer.mozilla.org/en-US/docs/Web/API/Web_Speech_API/Using_the_Web_Speech_API) |
| **Piper** | Server (or on-device via sherpa-onnx) | **GPL-3.0** engine (`OHF-Voice/piper1-gpl`), embeds espeak-ng; each voice has its own licence in its MODEL_CARD, project says "personal use and research" ([repo](https://github.com/OHF-Voice/piper1-gpl), [voices doc](https://github.com/OHF-Voice/piper1-gpl/blob/main/docs/VOICES.md)) | Clearly natural, a bit flat; best "medium/high" voices are good for a one-liner | Fast on CPU, designed for Raspberry Pi: a short sentence should take well under a second **(estimate; I found no measured numbers)** | Voice ~15 to 100 MB | Repo says it is "looking for maintainers" (not archived). Very easy to run as a small HTTP service. Voices download from Hugging Face `rhasspy/piper-voices` |
| **Kokoro-82M** | Server (or on-device) | **Apache-2.0** weights ([repo](https://github.com/hexgrad/kokoro)); espeak-ng (GPL) used for fallback phonemising | Best of the free bunch for "natural" English (US and UK voices) | Faster than real-time on a laptop CPU is commonly claimed, no hard CPU numbers found; a sentence is probably 1 to 3 s on a NUC **(estimate)** | 82M params; ONNX file ~80 to 330 MB depending on quantisation | ONNX wrapper `kokoro-onnx` (MIT) avoids PyTorch. More RAM than Piper |
| **sherpa-onnx** | Android/Win/Linux, on-device or server | Apache-2.0 ([repo](https://github.com/k2-fsa/sherpa-onnx)) | Runs Piper/VITS (and Kokoro in some builds) models | Same as the model | Runtime ~ tens of MB + voice | Lets the *phone* make Piper-quality speech with no server and no Google TTS. Extra APK size and native-library work, so a later step |
| Hosted free tiers (Google/Azure/ElevenLabs/Edge "read aloud" web endpoint) | Cloud | Need accounts/cards or are unofficial | Excellent | Network round trip | n/a | **Rejected**: need keys/cards, or (Edge endpoint) are unofficial and can break/violate terms. Not "zero cost, zero accounts" |

**Takeaway:** use **Kokoro or Piper on frank** as the main voice (same voice on every device, which is what makes it feel like *a DJ*), and the **device's own TTS as an offline fallback**.

Which one: Piper is lighter and simpler (good if frank is a small box); Kokoro sounds better and has a permissive weights licence. Run both on frank and compare (section 7). Voice quality is subjective, and I can't hear audio, so this is your call.

---

## 4. Writing the lines

| Approach | Cost | Speed | Quality | Risk |
|---|---|---|---|---|
| **Templates + song facts + the engine's "why"** | Free | Instant | Predictable, can be warm and varied if we write enough variants | Gets samey; needs care |
| **Local LLM on frank via Ollama** | Free, RAM+CPU | See below | Can sound more natural, but small models ramble, invent facts ("released in 1994" when it wasn't), and need a strict prompt | Hallucination; cringe; frank load while also serving the family |
| Hosted free LLM tiers | Need account/API key; limits change | Fast | Good | **Rejected**: keys and sign-ups (not allowed for me to enter), free tiers shift or disappear |

**Local models that fit a NUC-class box with, say, 8 GB RAM free:** (Ollama library sizes; [qwen2.5](https://ollama.com/library/qwen2.5))

| Model | Download / RAM | Licence | Rough CPU speed |
|---|---|---|---|
| Qwen2.5 0.5B | 398 MB | Apache-2.0 | very fast, weak writing |
| Qwen2.5 1.5B | 986 MB | Apache-2.0 | roughly 8 to 25 tok/s |
| Llama 3.2 1B | ~1.3 GB | Llama community licence | ~10 to 60+ tok/s (sources disagree) |
| Gemma 2/3 1B to 2B | ~1 to 1.7 GB | Gemma terms | ~8 to 19 tok/s |
| Qwen2.5 3B | 1.9 GB | Qwen licence (not Apache) | ~5 to 9 tok/s |

Benchmarks I found disagree wildly and none were on a NUC with Ollama; one CPU-only Linux test measured ~18 tok/s for Gemma 3 1B and ~9 tok/s for a 3B ([itsFOSS](https://itsfoss.com/testing-local-llms-without-gpu)). A DJ line is about 30 tokens, so **expect 2 to 6 seconds per line plus a cold-start load of several seconds**. Fine if generated ahead of time (the next song is known 3+ minutes early). Not fine on demand.

**Recommendation for lines:** templates first. The LLM is an optional "variety" layer later, with the song facts fed in and the facts never allowed to change (only the phrasing). If LLM output fails a check (too long, mentions an unknown name), fall back to a template.

**Keeping it short, varied, not cringe** (concrete rules):
- Max about 18 words. One idea per line. The *why* is the hook: "Same energy as Out of Space, and you play The Prodigy a lot."
- Talk only about things we know are true: title, artist, album, year, and the why. Never invent trivia.
- Don't speak every song: about 1 in 3 to 1 in 4 transitions, never twice in a row, never over a song's quiet intro. A setting: Off / Rare / Often.
- 15+ variants per slot (open, bridge, close), no repeat within the last ~10 lines. Rotate structure ("Up next: ...", "That was ..., now ...", just the why).
- No hype words, no "vibe", no exclamation marks, no jokes. Say names how people say them (e.g. strip "(Remastered 2011)", "feat."; keep a pronunciation override list for odd names).
- Skip the intro for songs under ~60 s and when the previous one was skipped within 10 s (the user is flicking).

---

## 5. Where it runs and how the audio is mixed

### Generate on the server, play as a clip (recommended)

Server makes `dj/{id}.mp3` (mono, 24 kHz, ~48 to 64 kbps) when it picks the next song, caches it, and puts it in the queue as a normal item with a tiny duration. The same file works everywhere, which is the big win: one voice, one place to tune, **only one implementation of "speak"**, and the apps just play files (they already do).

Needs: a TTS service next to `flacie-web` and an MP3 encoder (the image has no ffmpeg). A ~4 s clip at 48 kbps is ~25 KB, so cache cost is nothing.

Alternative, device-side TTS only: no server load and works with no connection, but the voice differs per device, can't work on Alexa, and web TTS can't be mixed.

### Platform details

| Platform | How the voice goes between tracks | Ducking? |
|---|---|---|
| **Android (phone)** | Insert the clip as a `MediaItem` between songs (Media3 playlist). Lowest risk, and works with gapless/crossfade queues already there. For "talk over the end of the song", fade the player volume to ~35% via `player.volume` for the clip's duration, which is simple with a second small `ExoPlayer`/`MediaPlayer` playing the clip | Own the ducking: set volume ourselves. Don't use `AUDIOFOCUS_GAIN_TRANSIENT_MAY_DUCK` for the DJ clip, since our own player ducks itself; the framework uses it for other apps' short sounds ([docs](https://developer.android.com/reference/android/media/AudioFocusRequest)) |
| **Android Auto / car** | Same clip-as-queue-item. Android Auto's rules for media apps (only two mentioned: nothing playing without a user action; nothing alarming) don't mention TTS. Set `METADATA_KEY_IS_ADVERTISEMENT` on the clip item so Android Auto doesn't pop a "now playing changed" notification ([distraction safeguards](https://developer.android.com/training/cars/media/distraction-safeguards)). Don't use `TextToSpeech` directly inside the car session: another app's audio focus + hardware button quirks are the likely failures. | Not needed if the clip sits *between* songs |
| **Web / Windows** | `<audio>` element or Web Audio gain node: fade song down, play clip, fade back up. Because it's a file, Web Audio can mix it (unlike `speechSynthesis`, which can't be captured or mixed). Needs the first user click (already the case since you press Play) | Easy with a gain node |
| **Alexa** | Speech can't be inserted between queued songs: responses to `PlaybackNearlyFinished` may not contain `outputSpeech` ([AudioPlayer reference](https://developer.amazon.com/docs/alexa/custom-skills/audioplayer-interface-reference.html)). Instead, `ENQUEUE` the DJ clip as its own stream: `[song A][DJ clip][song B]`, each with a token. The clip must be public HTTPS on 443, and it's unauthenticated, so it must contain nothing private (no names beyond song/artist). Speech via SSML `<audio>` only works in a launch/intent reply and needs MPEG-2 MP3, 48 kbps, 16/22.05/24 kHz, max 240 s ([SSML reference](https://developer.amazon.com/en-US/docs/alexa/custom-skills/speech-synthesis-markup-language-ssml-reference.html)). Funnel already exposes `/alexa`, so a `/alexa/dj/{id}.mp3` route fits | None, it's sequential. Alexa's built-in TTS voice could say "playing X" on direct requests ("ask flaky for something chill") |

### Edge cases to handle
- Skip pressed during the clip: the clip is a queue item, so "next" naturally skips it (and shouldn't count as a song skip in the taste engine; mark it so the engine ignores it).
- Shuffle, repeat-one, seeking back: clips should be re-attached only on forward auto-advance.
- Cover art/title while the clip plays: show "FLACie DJ" and keep the next song's cover, so lock-screen/car screens don't flicker.
- Offline: if no clip is cached, silently skip it.
- Spoken lines contain only library data, which already lives on the server. No data leaves for third parties.

---

## 6. Later: requests ("play something chill", "more like this")

- **Alexa** already does the hard part (speech to text); extend the skill's intents, and the engine handles the "chill" as an energy/tempo target.
- **Phone/car:** Android Auto/Google Assistant voice actions arrive as `onPlayFromSearch`; map "chill/energetic/more like this" onto the recommender. No speech recognition to build.
- **Web/Windows:** a text box or a few buttons ("Chiller", "Livelier", "More like this") beats speech recognition, which in browsers means Google's cloud service (not private, not free-guaranteed).
- Free-form language understanding with a small local LLM is possible later, but a fixed handful of phrases ("chill", "party", "focus", "something new") covers 95%.

---

## 7. Open facts to check (I could not verify)

1. **Frank's CPU/RAM.** I was told not to touch frank, so I haven't looked. Please run on it and paste back:
   ```bash
   lscpu | grep -E "Model name|^CPU\(s\)"; free -h; docker ps --format '{{.Names}}' | head -30
   ```
   Rule of thumb: 4+ cores and 4 GB free RAM is plenty for Piper or Kokoro; add 2 GB more if you want a small LLM too. Avoid doing both TTS and LLM at once when a family member is streaming.
2. **Real speed numbers.** Quick benchmark once a TTS container is up (in a scratch folder, not in flacie-web), time a single call such as `time curl -s -o /tmp/t.wav -d 'That was Chandelier by Sia. Staying with big vocals, here is Titanium.' http://localhost:5000/api/tts`. Anything under about 2 s per line is fine because clips are made ahead of time.
3. **Does your RG Rotate have a system TTS engine?** Settings > Accessibility > Text-to-speech. If there's no engine, the on-device fallback does nothing there (server clips still work).
4. **Whether Alexa plays a queued clip with no gap-issues.** I believe enqueuing short streams works, but I haven't tested it; a 1-day spike with your skill would answer it.
5. **Piper/Kokoro on frank without ffmpeg:** need an MP3 encoder in the .NET image (or the TTS sidecar returning MP3).

---

## 8. Recommendation

**Stack**
- **Lines:** templates (+ the engine's "why"), with an optional local-LLM variety layer later.
- **Voice:** one server voice, **Kokoro** (best sound) or **Piper** (lightest). Pick after listening; both run in one small container, so the choice isn't locked in.
- **Delivery:** server-generated 24 kHz mono MP3 clip, prefetched and cached next to the next song.
- **Playback:** the clip is a queue item on all platforms; fade/duck in our own players on web and phone.
- **Fallback:** device `TextToSpeech` / `speechSynthesis` when the clip isn't there.

**Effort (rough, one dev; "day" means a focused day)**

| Piece | Effort |
|---|---|
| 1. DJ script engine (template bank of ~150 lines in 6 slots, rotation, rate limiter, name clean-up) + tests | 1.5 days |
| 2. TTS sidecar container on frank + `/api/dj/clip` endpoint, MP3 encode, cache, prefetch | 2 days |
| 3. Android: insert clip item, ad flag for Auto, settings (Off/Rare/Often), engine ignores clips | 1.5 days |
| 4. Web/Windows: same with gain-node ducking, setting | 1 day |
| 5. Alexa: serve the public clip route, enqueue between songs, test on a device | 1 day (+ spike risk) |
| 6. Device-TTS fallback on Android + web | 0.5 day |
| 7. (Later) request intents and buttons | 1 to 2 days |
| 8. (Later) local LLM variety layer | 1 day |
| **Total for v1 (1 to 6)** | **about 7 to 8 days** |

Per the repo rule, it ships on Android + web + Windows together; Alexa is the one most likely to slip because it can't be tested without a real device.

**Risks**
- **Cringe.** Biggest real risk. Mitigation: low frequency, short lines, user setting, a "less DJ" thumb-down that backs the frequency off.
- **Licences.** Piper is GPL-3.0: fine running privately on frank, but FLACie's Docker image is labelled MIT and publicly sourced; keep Piper in a *separate* container rather than bundling it, and check each voice's MODEL_CARD. Kokoro weights (Apache-2.0) avoid this, though its fallback phonemiser (espeak-ng) is GPL.
- **Frank load** while playing for the family; clips are tiny and made ahead, so it should be a brief spike.
- **Pronunciation of names** ("Beyoncé", "Sigur Rós"); needs a small override list.
- **Alexa quirks:** clips are public/unauthenticated; the queue behaviour needs a real test.
- **Maintenance:** Piper is looking for maintainers; Kokoro is active. Hence preferring a swappable TTS service behind one endpoint.

**Decisions for you**
1. Voice: I'll prepare 3 sample sentences per voice (Kokoro UK female/male, Piper UK medium) for you to listen to, as you ask. I can't judge the sound myself.
2. Default chattiness (Off / Rare / Often) and whether each family member can change it.
3. OK to add a TTS container on frank (separate from `flacie-web`), and whether to add an MP3 encoder library to the web image.
4. Should the DJ also say things on Alexa (extra risk) or stay phone/web/Windows-only in v1?
5. Do you want lyrics-based or fun-fact lines? I'd say no: facts we don't own are how you get wrong or cringey lines.

---

## Sources
- [Piper (OHF-Voice/piper1-gpl)](https://github.com/OHF-Voice/piper1-gpl) and [VOICES.md](https://github.com/OHF-Voice/piper1-gpl/blob/main/docs/VOICES.md)
- [Kokoro](https://github.com/hexgrad/kokoro)
- [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx)
- [MDN Web Speech API](https://developer.mozilla.org/en-US/docs/Web/API/Web_Speech_API/Using_the_Web_Speech_API)
- [Alexa AudioPlayer reference](https://developer.amazon.com/docs/alexa/custom-skills/audioplayer-interface-reference.html)
- [Alexa SSML reference](https://developer.amazon.com/en-US/docs/alexa/custom-skills/speech-synthesis-markup-language-ssml-reference.html)
- [Android for Cars: media apps](https://developer.android.com/training/cars/media) and [distraction safeguards](https://developer.android.com/training/cars/media/distraction-safeguards)
- [Android AudioFocusRequest](https://developer.android.com/reference/android/media/AudioFocusRequest)
- [Ollama qwen2.5](https://ollama.com/library/qwen2.5)
- [CPU-only local LLM test (itsFOSS)](https://itsfoss.com/testing-local-llms-without-gpu)
