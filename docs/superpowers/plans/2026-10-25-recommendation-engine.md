# Recommendation Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One per-account engine on the FLACie server that picks what plays next and fills Home, learning from skips, finishes, hearts,
thumbs-down, "less like this" and the playlists each person plays, matching by vibe, half familiar and half new.

**Architecture:** Listening events go from every client to the server and are kept per account (`data/listen/<account>.jsonl`). The engine
in `FLACie.Core` builds on the existing `TasteProfile` (Recommender.cs), adds a per-song vibe index (energy, loudness, tempo) and an on-disk
cache of online "related" songs, and scores candidates. `FLACie.Server` exposes it as `/api/listen`, `/api/recommend/next` and
`/api/recommend/home`; Android, web/Windows, the car and Alexa use those, Android keeping its local Recommend.kt as the offline fallback.

**Tech Stack:** C# / .NET (FLACie.Core, FLACie.Server, xUnit in tests/FLACie.Tests), Kotlin (Android, JUnit in app/src/test).

**Spec:** docs/superpowers/specs/2026-10-25-recommendation-engine-design.md

## Global Constraints

- Everything is per **account** (the `UserSession.Id` on the server, the signed-in account on clients), never per device.
- Skip = under 30 s heard; finished = 90 % or more heard (or the song ended); replay = started again within the same session.
- Blend: half familiar, half new ("new" = not in the library or never played by this account).
- Vibe (energy, tempo, danceability, mood) decides fit, not genre. A song this account likes may cross genres when the vibe matches; a
  never-heard song also needs a genre or artist link to the session.
- Rails: never the same song within 4 hours, never the same lead artist as the previous song, never a thumbed-down song, never explicit songs
  when the explicit filter is on, never inserted inside a playlist or before a song the person picked. Autoplay only adds when at most one song
  follows the current one (PlayerController.checkQueueLow, unchanged).
- Shared playlists: ownership and visibility on Jellyfin are never changed; a playlist counts for a person by what they play from it.
- A pick answers in under 200 ms on the server; online lookups are cached and never awaited inside a pick.
- Event files keep one year in full; older events are thinned to daily totals per song.
- No release (beta) until the owner says so.

## Review Focus

1. A brand-new account with no events and no Jellyfin history: Home and Autoplay must still fill (library-wide picks, no exception).
2. A session whose seed song has no vibe data yet (not analysed): fit falls back to genre/artist links instead of scoring zero for everything.
3. The phone offline for days with hundreds of queued events: they all reach the server once, in order, with no duplicates.
4. Every candidate filtered out (tiny library, everything heard in the last 4 hours): the rails relax in order (4 h, then artist) rather than returning nothing.
5. Online related lookup slow or down: picks still answer within 200 ms from the library alone.

Each line has its test in the owning task (Tasks 2, 5, 9, 5 and 6).

---

### Task 1: Listening events and the per-account log

**Files:**
- Create: `ipodsync/src/FLACie.Core/ListenEvents.cs`
- Create: `ipodsync/src/FLACie.Server/ListenLogStore.cs`
- Test: `ipodsync/tests/FLACie.Tests/ListenLogTests.cs`

**Interfaces:**
- Produces: `enum ListenKind { Play, Heart, Unheart, ThumbDown, LessLikeThis, PlaylistAdd, PlaylistRemove }`;
  `sealed record ListenEvent(string Id, ListenKind Kind, long At, string Path, string Title, string Artist, double Heard = 0, double Length = 0, string? Context = null)`
  (Id is client-made, for de-duplication; Heard/Length in seconds for Play; Context = playlist id or "autoplay" / "search" / "home");
  `static class ListenClass { bool IsSkip(ListenEvent e); bool IsFinish(ListenEvent e); }` (30 s / 90 % rules);
  `sealed class ListenLogStore(DataPaths paths) { void Append(UserSession s, IEnumerable<ListenEvent> events); IReadOnlyList<ListenEvent> Read(UserSession s); void Thin(UserSession s, DateTime utcNow); }`.

- [ ] **Step 1: Write the failing tests** in `ListenLogTests`: `Skip_is_under_30_seconds` (Heard 29 of 200 → IsSkip true; 31 → false);
  `Finish_is_90_percent` (180 of 200 → true; 170 → false); `Append_ignores_an_id_already_kept` (append the same Id twice → Read returns one);
  `Read_returns_events_in_time_order`; `Thin_folds_events_older_than_a_year_into_daily_totals` (two plays of one song on one day 400 days ago
  → one Play event with Heard summed, recent events untouched).
- [ ] **Step 2: Run** `dotnet test ipodsync/tests/FLACie.Tests --filter ListenLogTests` — Expected: FAIL (types not defined).
- [ ] **Step 3: Implement** the types and store: one JSON object per line in `Path.Combine(paths.Root, "listen", s.Id + ".jsonl")`, a
  per-account lock, ids kept in a HashSet loaded on first use.
- [ ] **Step 4: Run** the same filter — Expected: PASS.
- [ ] **Step 5: Commit** `git add` the three files; message "Recommend: listening events and the per-account log".

### Task 2: `/api/listen` and seeding from existing history

**Files:**
- Modify: `ipodsync/src/FLACie.Server/ApiEndpoints.cs` (new endpoint beside `/api/import`)
- Create: `ipodsync/src/FLACie.Server/ListenSeed.cs`
- Modify: `ipodsync/src/FLACie.Server/Program.cs` (register `ListenLogStore` as singleton)
- Test: `ipodsync/tests/FLACie.Tests/ListenApiTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces: `POST /api/listen` body `{ "events": [ListenEvent…] }` → 200 `{ "kept": n }`, 401 without a session;
  `static class ListenSeed { IReadOnlyList<ListenEvent> From(IReadOnlyList<PlayedEntry> history, IReadOnlyList<PlaylistEntry> favourites, IReadOnlyList<JfPlay> jellyfin, Library lib); }`
  run once per account when its log file does not exist (history = the profile's play history, favourites → Heart events, Jellyfin play counts → Play events marked finished).

- [ ] **Step 1: Failing tests:** `Listen_without_a_session_is_401`; `Listen_keeps_events_for_that_account_only` (two sessions, each reads back
  only its own); `Seed_turns_favourites_into_hearts_and_jellyfin_counts_into_finished_plays`; `Seed_runs_once` (second request does not add
  seeds again); Review Focus 1: `New_account_with_no_history_gets_an_empty_log_not_an_error`.
- [ ] **Step 2: Run** `--filter ListenApiTests` — FAIL.
- [ ] **Step 3: Implement**, using the existing `Who(ctx, store, jf)` for the session, as `/api/import` does.
- [ ] **Step 4: Run** — PASS. Also run the whole suite: `dotnet test ipodsync/tests/FLACie.Tests` — all pass.
- [ ] **Step 5: Commit** "Recommend: /api/listen, seeded from history, favourites and Jellyfin counts".

### Task 3: Vibe index (energy, loudness, tempo)

**Files:**
- Create: `ipodsync/src/FLACie.Core/Vibe.cs`
- Create: `ipodsync/src/FLACie.Server/VibeIndex.cs` (background fill like `FormatProbeService`, stored in `data/vibe.json`)
- Test: `ipodsync/tests/FLACie.Tests/VibeTests.cs`

**Interfaces:**
- Consumes: the decoded-level data `/api/wave` already makes (`WaveShape`).
- Produces: `sealed record Vibe(double Energy, double Loudness, double Bpm, double Dance)` (Energy/Loudness/Dance 0..1, Bpm 0 when unknown);
  `static class VibeMath { Vibe FromLevels(ReadOnlySpan<float> levels, double secondsPerLevel); double Distance(Vibe a, Vibe b); }`
  (Distance 0..1; tempo compared modulo doubling, so 64 and 128 BPM are close);
  `sealed class VibeIndex { Vibe? Of(Track t); }` — null when not analysed yet.

- [ ] **Step 1: Failing tests:** `A_click_every_half_second_is_120_bpm` (synthetic levels, ±2 BPM); `Half_and_double_tempo_are_close`
  (Distance(64 BPM, 128 BPM, same energy) < 0.15); `Quiet_sparse_audio_has_low_energy`; `Loud_dense_audio_has_high_energy`;
  `Ballad_and_banger_are_far` (Distance > 0.5).
- [ ] **Step 2: Run** `--filter VibeTests` — FAIL.
- [ ] **Step 3: Implement** `FromLevels`: energy = mean level, loudness = 95th percentile, tempo = the strongest autocorrelation peak of the
  onset envelope between 60 and 180 BPM, dance = tempo-peak strength × steadiness. `VibeIndex` fills songs in the background, newest
  played first, and saves every 200 songs.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** "Recommend: vibe index (energy, loudness, tempo)".

### Task 4: Taste from listening events

**Files:**
- Modify: `ipodsync/src/FLACie.Core/Recommender.cs` (`TasteProfile`)
- Test: `ipodsync/tests/FLACie.Tests/TasteEventsTests.cs`

**Interfaces:**
- Consumes: Task 1 types.
- Produces: new `TasteProfile` constructor parameter `IReadOnlyList<ListenEvent>? events = null`, and
  `double Penalty(Track t)` (0..1: thumbs-down = 1 for the song; skips decay over 30 days; LessLikeThis applies to the lead artist),
  `bool Disliked(Track t)` (thumbed down), `bool Likes(Track t)` (hearted, or finished 3+ times in 90 days).
  Playlists count by the account's own plays from them: an entry's weight comes from Play events whose Context is that playlist's id; the old
  flat 0.7 per playlist entry is removed.

- [ ] **Step 1: Failing tests:** `A_finish_counts_more_than_a_skip`; `Three_skips_push_a_song_down`; `Thumbs_down_is_a_full_penalty`;
  `Less_like_this_marks_the_artist_down_not_only_the_song`; `A_shared_playlist_counts_only_for_who_plays_it` (Dad's events play from AA only
  → only AA's songs gain for Dad; the owner's events play all seven → all gain); `Likes_is_hearted_or_finished_three_times`.
- [ ] **Step 2: Run** `--filter TasteEventsTests` — FAIL.
- [ ] **Step 3: Implement** in `TasteProfile`, keeping the existing decay halves (30 days FLACie plays, 120 days Jellyfin).
- [ ] **Step 4: Run** the filter, then the whole suite (existing Home/Recommender tests must still pass).
- [ ] **Step 5: Commit** "Recommend: taste learns from skips, finishes, thumbs-down and per-person playlist plays".

### Task 5: The engine

**Files:**
- Create: `ipodsync/src/FLACie.Core/Engine.cs`
- Test: `ipodsync/tests/FLACie.Tests/EngineTests.cs`

**Interfaces:**
- Consumes: `TasteProfile` (Task 4), `Vibe`/`VibeMath` (Task 3), `Library`.
- Produces: `sealed record Pick(Track? Track, CatalogSong? New, double Score, string Why)` (exactly one of Track/New set);
  `sealed record EngineOptions(int Take = 10, bool Explicit = true, double NewShare = 0.5, long Seed = 0)`;
  `static class Engine { List<Pick> Next(Library lib, TasteProfile taste, Func<Track, Vibe?> vibe, IReadOnlyList<Track> session, IReadOnlyList<CatalogSong> related, DateTime utcNow, EngineOptions o); }`.
  Score = 0.35·fit + 0.3·taste + 0.25·together − 0.6·penalty, where fit = 1 − VibeMath.Distance to the mean of the last 3 session songs,
  together = present in `related` for the seed or shares a played-from playlist with it. Gate: a liked song passes on fit ≥ 0.6; a
  never-heard song needs fit ≥ 0.6 and a genre or artist link to the session. Then rails, then blend familiar/new by `NewShare`.
  `Why` names the strongest part, e.g. "Same energy as Out of Space · you play The Prodigy a lot".

- [ ] **Step 1: Failing tests:**
  - `Liked_songs_cross_genre_when_the_vibe_matches` — session [The Prodigy "Out of Space", rave, high energy]; candidates: David Guetta
    "Sexy Bitch" (club, high energy, liked) and a liked slow ballad; Guetta ranks above the ballad.
  - `A_never_heard_song_needs_a_genre_or_artist_link` — an unheard high-energy song in an unrelated genre is not picked.
  - `No_ballad_after_a_banger` — a slow low-energy song scores below the gate after three high-energy songs.
  - `Never_the_same_artist_twice_running`; `Not_heard_within_4_hours`; `Thumbed_down_never_comes_back`;
    `Explicit_off_drops_explicit_songs`.
  - `Half_familiar_half_new` — with enough candidates of both kinds, Take = 10 gives 5 and 5.
  - Review Focus 2: `A_seed_without_vibe_data_falls_back_to_genre_and_artist`.
  - Review Focus 4: `When_everything_is_filtered_the_rails_relax_in_order` (tiny library all heard in the last hour → still returns picks,
    4-hour rule relaxed first, artist rule second; thumbs-down never relaxed).
  - `Why_names_the_strongest_reason`.
- [ ] **Step 2: Run** `--filter EngineTests` — FAIL.
- [ ] **Step 3: Implement** `Engine.Next` with the weights and gates above.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** "Recommend: the engine (vibe fit, taste, together, penalties, rails, half new)".

### Task 6: Related-songs cache and `/api/recommend`

**Files:**
- Create: `ipodsync/src/FLACie.Server/RelatedCache.cs` (wraps `Catalog.RelatedAsync`, kept 7 days in `data/related/`)
- Create: `ipodsync/src/FLACie.Server/RecommendEndpoints.cs`; Modify: `Program.cs` (map it, register `RelatedCache`, `VibeIndex`)
- Modify: `ipodsync/src/FLACie.Core/Home.cs` (shelves take picks from `Engine` instead of the old ranking)
- Test: `ipodsync/tests/FLACie.Tests/RecommendApiTests.cs`, existing `HomeSpeedTests`

**Interfaces:**
- Consumes: Tasks 1–5.
- Produces: `GET /api/recommend/next?session=<path>,<path>,…&n=10` → `{ picks: [{ path?, title, artist, album, durationMs, isNew, why }] }`;
  `GET /api/recommend/home` → the same shelves the web Home shows (Quick picks, Listen again, Daily Mixes, Discover, Albums for you) as JSON;
  `RelatedCache.Peek(artist, title)` returns cached or empty at once and refreshes in the background.

- [ ] **Step 1: Failing tests:** `Next_answers_within_200_ms_for_a_10000_song_library`; Review Focus 5:
  `Next_does_not_wait_for_a_slow_related_lookup` (catalog stub sleeps 5 s → answer under 200 ms, library-only picks);
  `Home_shelves_change_hourly_not_while_shown` (same hour → same shelves); `Next_is_401_without_a_session`.
- [ ] **Step 2: Run** `--filter "RecommendApiTests|HomeSpeedTests"` — FAIL.
- [ ] **Step 3: Implement** the endpoints and switch `Home.cs` shelves to the engine.
- [ ] **Step 4: Run** the whole suite and the web smoke tests (`ipodsync/tests/web-smoke`) — all pass.
- [ ] **Step 5: Commit** "Recommend: /api/recommend (next, home) and the related cache; web Home uses the engine".

### Task 7: Offline evaluation gate

**Files:**
- Create: `ipodsync/tests/FLACie.Tests/RecommendEvalTests.cs`
- Create: `ipodsync/tools/receval/Program.cs` (+ csproj): runs the same measure on a copy of real data

**Interfaces:**
- Consumes: `Engine.Next`, the old `Recommender.Recommend` (for comparison).
- Produces: `static class RecEval { double HitRateAt10(Func<IReadOnlyList<Track>, List<Track>> recommender, IReadOnlyList<(IReadOnlyList<Track> session, Track actualNext)> cases); }`.

- [ ] **Step 1: Failing test:** `New_engine_beats_the_old_on_a_synthetic_history` (a generated history with clear vibe clusters: new hit rate
  ≥ 1.5 × old).
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement** `RecEval` and the tool (reads a copy of `data/` given as an argument, prints old vs new
  per account; never writes). **Step 4: Run** — PASS.
- [ ] **Step 5: Run the tool on a copy of frank's data** (copy, never the live folder) and record the numbers in `docs/waves/log.md`.
  Expected: new ≥ 1.5 × old for the owner's account. If not, tune the weights in Task 5 (tests must stay green) before going on.
- [ ] **Step 6: Commit** "Recommend: offline evaluation (hit rate at 10, old vs new)".

### Task 8: Web and Windows: menu actions and Why

**Files:**
- Modify: `ipodsync/src/FLACie.App/Components/Shared/TrackList.razor`, `Songs.razor` (song menus), `NowPlaying.razor` (Info line)
- Modify: the client player code that ends a song (where FLACie Web records history) to also post Play events with Heard/Length
- Test: `ipodsync/tests/web-smoke` (new spec `1z-thumbs-down.spec`), `ipodsync/tests/FLACie.Tests/ListenApiTests.cs`

**Interfaces:**
- Consumes: `/api/listen`, `/api/recommend/next` (`why`).
- Produces: "Less like this" and "Thumbs down" in every song menu; thumbs-down removes the row from the current list at once; Autoplay on the
  web asks `/api/recommend/next`; "Why this song?" in the Info panel.

- [ ] **Step 1: Failing smoke test:** thumbs-down on a row removes it and a `ThumbDown` event reaches `/api/listen`.
- [ ] **Step 2: Run** the smoke spec — FAIL. **Step 3: Implement.** **Step 4: Run** all web-smoke specs and the .NET suite — PASS.
- [ ] **Step 5: Commit** "Recommend: web/Windows thumbs-down, less like this, Why; web Autoplay uses the engine".

### Task 9: Android: events, Autoplay, Home, menu actions

**Files:**
- Create: `android/app/src/main/java/com/ipodemu/library/ListenQueue.kt` (events kept in `files/listen-queue.json`, sent with SyncRetry until accepted)
- Modify: `playback/PlayerController.kt` (on track end / skip / replay → ListenQueue), `library/Autoplay.kt` (ask `/api/recommend/next`
  first, today's logic as the fallback), `player/PlayerUi.kt` (Home from `/api/recommend/home` with HomeShelves as the fallback;
  "Less like this" and "Thumbs down" in `openTrackSheet`), `player/InfoPanel.kt` (Why line)
- Test: `android/app/src/test/java/com/ipodemu/library/ListenQueueTest.kt`, `AutoplaySourceTest.kt`

**Interfaces:**
- Consumes: `/api/listen`, `/api/recommend/next`, `/api/recommend/home` (Task 6), `SyncRetry.attempt(pauses, thenEvery)`.
- Produces: `class ListenQueue(dir: File) { fun add(e: ListenEventJson); fun pending(): List<ListenEventJson>; fun accepted(ids: Set<String>) }`.
  Picks with `isNew` go through Autoplay's existing request path (`waiting`, fetched ahead and joining the queue as they land), unchanged.

- [ ] **Step 1: Failing tests:** Review Focus 3: `Days_offline_send_every_event_once_in_order` (500 events queued, two send attempts with the
  first failing → server stub receives 500, in order, no duplicates); `Skip_and_finish_are_measured_from_heard_time`;
  `Autoplay_uses_the_server_and_falls_back_offline` (server stub down → today's picks).
- [ ] **Step 2: Run** `android\gradlew.bat :app:testDebugUnitTest` — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS; install on the
  `flacie_phone` emulator and check Home, a thumbs-down and Autoplay after a playlist's last song by hand.
- [ ] **Step 5: Commit** "Recommend: Android sends listening, Autoplay and Home from the engine, thumbs-down and less like this".

### Task 10: Car and Alexa

**Files:**
- Modify: `android/.../playback/BrowseTree.kt` + `AppBrowseSource.kt` (Home and Autoplay from the engine via the app), 
  `ipodsync/src/FLACie.Server/Alexa/AlexaEngine.cs` (when Alexa's queue runs low, fill from `Engine.Next` for that account)
- Test: `android/app/src/test/java/com/ipodemu/playback/BrowseTreeTest.kt`, `ipodsync/tests/FLACie.Tests/AlexaSkillTests.cs`

- [ ] **Step 1: Failing tests:** `Car_home_shows_the_engines_quick_picks`; `Alexa_continues_with_engine_picks_after_the_asked_song`.
- [ ] **Step 2: Run** both suites — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS; `CarBrowseTest` on `flacie_car`.
- [ ] **Step 5: Commit** "Recommend: car and Alexa use the engine".

### Stage 3 (later, own plan)

The trained "goes together" model replaces the online related data for library songs once a few weeks of events exist. It plugs into
`Engine.Next` through the `related` parameter and needs no other change; it gets its own spec and plan when there is enough data.
