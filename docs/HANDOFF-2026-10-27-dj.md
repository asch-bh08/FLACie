# Handoff: recommendation engine + AI DJ (2026-10-27)

Branch `ui/living-sleeve`, pushed; `main` was fast-forwarded to it for beta 92. Read `docs/waves/log.md` (the end has everything since 25 Oct) and `docs/spec/ai-dj.md`.

## State
- **Released:** beta 93 (also has captions, clearer voice, pronunciation check, learned requests); before that beta 92 (v1.0.0-beta92, APK versionCode 107, MSI 0.92.0). It contains:
  - the per-account recommendation engine;
  - the AI DJ (Workers AI llama-3.3-70b lines, Aura Helios voice, intro timing, Style, Every song, Talk after skips, volume and duck, always/never per song, one-voice rule);
  - Talk to the DJ (mic, smart assistant, shared context, auto-download);
  - the history and favourites fixes.
- **Web only, newer than beta 92** (deployed to frank 2026-10-27, rollback tag `flacie-web:rollback-2026-10-27-pre-djclear`): commit 6eedee0. It adds:
  - pronunciation verified on frank by voice + Whisper (data/dj-pronounce.json, Dashboard "Check pronunciations");
  - DJ captions;
  - duck default 20 %, a voice chain, and an optional cue sound.
  The phone and Windows apps get these in the next beta.
- **Deploying frank:** allowed by rules in `.claude/settings.local.json` (rollback tag first, then `git archive HEAD:ipodsync/src ... | ssh ... docker compose up -d --force-recreate`, then `/healthz`).

## Open, in the owner's order
1. **(DONE f3f6edc, web) Learned requests.** "Play car songs" / "B-road" / "back roads" should play what the person actually plays in that situation (e.g. Cappella "U Got 2 Know" in the car).
   - Tag listen events with the surface (car = Android Auto, phone, web) and the time of day.
   - Let the ask AI map a phrase to a vibe and context.
   - Learn per person from plays and skips after a request: phrase → songs.
2. **(DONE 582edf5, needs the owner to run alexa-skill\deploy.ps1) Alexa** ("flaky" skill, `ipodsync/src/FLACie.Server/Alexa/`) should use the same assistant brain and context.
3. **Conversation mode** (Gemini-Live-like): keeps listening after each answer until "that's all"; free via device speech-to-text + Cloudflare; ~2-3 s replies.
4. **Real-data gate for the engine.** Re-run `ipodsync/tools/receval` on a read-only copy of frank's data once vibe.json and listen logs have grown. Two product questions for the owner: the same-artist-twice rail (25 % of the owner's real next songs are the same artist) and NewShare 0.5.
5. **Android:** the mic sits beside the DJ button on Now Playing (no room opposite at 360 dp); no mic in the mini bar; a song downloaded for a voice request plays only after the next library sync.
6. **Owner to-dos:** check the DJ and captions by ear, and press Dashboard > AI DJ > "Check pronunciations" if names are still wrong.

## Rules (from memory)
Never Haiku; Sonnet for easy jobs, Opus for hard ones. TDD. `git add` named files only, never a directory. Rollback tag before every deploy. When a usage limit stops agents, start one timer until just after the reset and resume them; start it pre-emptively when the 5-hour limit is high. Hand off at 96 % weekly.
