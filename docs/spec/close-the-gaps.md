# FLACie "close the gaps" programme: specification (draft 1, 2026-10-12)

Status: **specification only, nothing in this document is built yet.** It is written to be argued with before any phase starts. Anything marked **[verify]** is something I believe but have not checked against the product's or API's current public documentation; Phase 0 exists to check it, and the spec is revised after Phase 0 before Phase 1 begins.

Goal: match or beat Plexamp and Symfonium on features, and make FLACie feel hand-made.
Non-negotiables (from the owner, and from the repo's hard rules):
1. **Jellyfin-only keeps working from any network.** Every feature must work with nothing but a Jellyfin URL and a Jellyfin user. flacie-web, MusicBrainz, Soulseek/Lidarr and the rest are optional layers; their absence degrades a feature, never breaks the app.
2. **Every feature ships on Android, web and Windows together**, unless platform-specific (Android Auto, Cast sender on Android; Windows media keys / tray on Windows). When one platform cannot have a feature, the release notes say so.
3. **YouTube (yt-dlp) is allowed only as an opt-in source, flagged "Use at your own risk"** (owner decision, 2026-10-12, replacing the earlier ban). It is **off by default**, switched on per install in Settings behind a plain-language warning (it can breach YouTube's terms and copyright law, the risk is the user's, FLACie does not endorse it), shown as a visibly separate source ("YouTube · at your own risk"), never tried automatically, never first in the race, never bundled in the Play-style distribution. The repo already has the plumbing (`ipodsync/tools/ytdl`, the file mover's ytdl call, `DownloadCoordinator`). No Explo. The other internet sources stay (Soulseek, Lidarr, Jamendo, Audius, Internet Archive, SoundCloud).
4. **Central request gate.** Every Jellyfin request goes through `JellyfinGate` (Android) / the server gate (web) with priorities. MusicBrainz, Cover Art Archive get **their own gates** (own rate limits, own circuit breakers), never sharing Jellyfin's slots.
5. **No secrets in chat or in the repo.** Any token a feature needs is typed by the owner into Settings and stored like the existing service keys; never logged.
6. **Never overwrite existing user tags or covers silently.** Every metadata/cover change is a visible choice with undo.
7. **A song's own picture still wins** by default; `CoverVetting` stays; wrong covers must not regress (tests below).
8. Honest "Tested / Not tested" per release; we cannot hear audio, so by-ear items are listed for the owner.
9. Fold only for device tests (wireless adb). RG Rotate is not touched.

---

## 0. ListenBrainz: CUT (owner decision, 2026-10-12)

ListenBrainz is **out of the programme**: no token field, no scrobbling, no recommendations from it, no gate, no tests. The owner uses YouTube Music and does not need it. Everything that would have used it (weekly mixes, radio, mix builder) runs on signals FLACie already has: Jellyfin play counts, favourites and ratings, genres and years, the local play log, Deezer related-artist top songs (already a dependency family), MusicBrainz relations (Phase 2) and the on-device similarity engine. The weekly mixes are therefore weaker than a real recommender and say so in the UI. If it is ever wanted back, the similarity interface (section 4.1) has a slot for an external artist-similarity signal.

---

## 1. Phase 0: research (documents, no product code)

Output: `docs/research/` — `symfonium.md`, `plexamp.md`, `gap-table.md`, `apis.md`, `design-references.md`. Four independent read-only research agents (no worktrees needed: they only write markdown), each told to cite the public page it read and to mark claims it could not confirm.

| Agent | Reads | Produces |
|---|---|---|
| A: Symfonium | public docs/site, Play Store listing text, public changelog | feature inventory, grouped (library, offline, playback, EQ/ReplayGain, lyrics, Auto/Cast, widgets, backup, tags/metadata, UI) |
| B: Plexamp | public Plex support pages, Plexamp release notes, marketing page | same grouping, plus Plex-server-dependent features flagged as "needs a server we do not have" |
| C: APIs | MusicBrainz API + rate-limit page, Cover Art Archive docs, LRCLIB docs, Deezer public API terms | `apis.md`: endpoints we would use, auth, **rate limits**, required User-Agent, licensing/attribution, caching rules, what is allowed commercially/personally |
| D: Design references | public marketing screenshots only | `design-references.md`: what each app does for type, motion, colour, empty states; no screenshots committed (link only) |

I then merge A+B+current FLACie capabilities into `gap-table.md`: one row per feature: *what, Plexamp?, Symfonium?, FLACie today, effort (S/M/L/XL), value (1-5), platform scope, depends-on, phase*. Ranked by value ÷ effort. The ordering in sections 2-5 below is my prior; the table may reorder items inside a phase but not move an item across phases without telling you.

Known facts about FLACie today that the table starts from (verified in this repo): Jellyfin sign-in + Quick Connect, central gate, bulk/delta sync, WebSocket updates, covers vetted, Home from a saved snapshot (beta 56), Up next editing, sleep timer, EQ (`ToneEngine`), headphone test, lyrics provider (`Lyrics.kt`), Jams (SyncPlay), downloads *of new music* via sources (not offline caching of existing songs), audio-facts / Hi-Res badge, quality rule, Explore search, Home shelves and Quick picks, Deezer taste feed, flacie-web with admin dashboard, Windows app embedding the server.

**Exit criteria:** gap table reviewed by the owner; a one-page "build list" of the top items per phase; API rate limits written down with source links.

---

## 2. Phase 1: playback and offline

### 2.1 Offline downloads (the single most-asked-for Symfonium/Plexamp feature)
- **What:** download songs, albums, playlists, and "all favourites" for offline play; per-item state (queued, downloading n%, downloaded, failed, stale).
- **Jellyfin-only design:** use Jellyfin's own download/stream endpoint (`/Items/{id}/Download` or `/Audio/{id}/stream?static=true`), original file, no transcoding by default; optional "download as AAC/Opus at N kbps" using Jellyfin's universal endpoint for small phones **[verify endpoint behaviour]**.
- **Gate:** downloads are `Priority.BACKGROUND`, concurrency 2, held back while a song is playing on a poor link; resumable (HTTP Range) with a `.part` file + final atomic rename (same lesson as the cover race fixed in beta 56).
- **Storage manager:** screen listing size by album/playlist/"favourites", total used / free, sort by size or last played, bulk delete, a cap ("keep at most 20 GB", least-recently-played eviction, never evicting pinned items), and a "downloaded only" library filter.
- **Rules:** Wi-Fi-only (default on), "only while charging" (optional), auto-download favourites, auto-download a playlist's new songs, auto-remove after N days unplayed (optional).
- **Offline playback:** player prefers the local file; library works with no network (the saved library + snapshot already load without one); play counts/ratings queue and report when back online (`PlaysReporter` already queues — extend it).
- **Web:** the web player is a browser; offline = **Service Worker + Cache Storage / OPFS** for chosen items **[verify quota]**, same UI vocabulary ("Available offline"). **Windows:** the embedded server caches to disk (real files). This parity is the hardest part; if browsers cannot hold enough, web ships "offline" as "keep the last N hours cached" and the release notes say so.
- **Effort:** XL. **Value:** 5. **Risk:** Android storage permissions (use app-private external files dir; no media-permission), battery, partial files.
- **Tests:** unit (resume, eviction order, cap maths, Wi-Fi rule, corrupt-part recovery), emulator with the throttled/flaky mock (kill mid-download, resume), Fold (airplane-mode playback of a downloaded album).

### 2.2 Transitions: crossfade, gapless, "sweet fades"
- **What:** crossfade 0-12 s with curve choice (equal-power default), **gapless preserved** when crossfade is 0 or when tracks are an album sequence the user marks "never crossfade" (live albums, classical, DJ mixes); an "auto" mode that crossfades only when the next track is not a continuation of the same album and fades silence-trimmed ends (the Plexamp "Sweet Fades" idea **[verify what it exactly does]**).
- **How (Android):** Media3 ExoPlayer has no native crossfade. Options, in order of preference: (1) two `ExoPlayer` instances with overlapped volume ramps managed by `PlayerController` (known approach, keeps gapless via pre-buffering the next into the idle player); (2) a custom `AudioProcessor`. Decision after a 2-day spike; both must keep the media session coherent. **Web:** Web Audio API two `<audio>` → `GainNode` ramp. **Windows:** the web engine in WebView2 (same code).
- **Effort:** L. **Value:** 4. **Risk:** gapless regressions, audio focus, Bluetooth latency, seeking during a fade. By-ear verification is the owner's.
- **Tests:** unit for the fade planner (when to start, curve values, short-track and skip handling), emulator log assertions on player state transitions; **cannot be heard here**.

### 2.3 Loudness levelling (ReplayGain / R128)
- **What:** per-track or per-album gain from tags when present (ReplayGain 2.0 `REPLAYGAIN_TRACK_GAIN`, R128 `R128_TRACK_GAIN` for Opus); fallback computed by analysis; pre-amp and clipping-prevention; modes off / track / album / auto (album when playing an album in order).
- **Jellyfin-only:** Jellyfin item JSON may expose `NormalizationGain` / `LUFS` **[verify the field for 10.11]** — first source; else read tags from the file's first bytes (the app already has a `TagReader`); else compute (EBU R128 integrated loudness) on a background pass for *downloaded* files, and lazily for streamed ones (analysis needs audio: only offline/cached files, never an extra full download).
- **Effort:** M (tags) + L (computed fallback). **Value:** 4.
- **Tests:** unit with fixture tags (gain parsing, album vs track choice, clip guard); computed loudness against a calibrated test tone (known LUFS) in a JVM test.

### 2.4 EQ: parametric + headphone presets
- **What:** extend the existing `ToneEngine` to a parametric EQ (N bands: type, frequency, Q, gain), preamp, per-output-device profiles (speaker, wired, each Bluetooth device), a preset library, **headphone correction presets** (AutoEq-style; AutoEq data is MIT-licensed **[verify]** and could be bundled as a compact list for popular models, never fetched per request), import of text-format presets.
- **Web/Windows:** `BiquadFilterNode` chain; same preset format so presets sync through the shared profile (`ProfileSave`).
- **Effort:** M-L. **Value:** 4 (Plexamp's EQ is a headline feature).
- **Tests:** unit for filter coefficient maths against reference values, preset round-trip, device-profile selection.

### 2.5 Sleep timer, playback speed, spoken-word
- Sleep timer exists; add "end of this track/album", fade-out over the last 30 s, shake/tap-to-extend (Android), and parity on web.
- Playback speed 0.5x-3x with pitch correction, **remembered per item for spoken word** (audiobooks/podcasts: detected by Jellyfin media type or genre "Audiobook/Spoken"); resume-position memory for items over 20 min.
- **Effort:** S-M. **Value:** 3 (5 if the owner listens to spoken word; I have no evidence they do).

### 2.6 Lyrics
- Sources, in order: embedded (USLT/SYLT/LYRICS tags, already readable by `TagReader`), sidecar `.lrc` next to the file (via Jellyfin if it exposes the file; NAS path otherwise), then **LRCLIB** (open, no key, community-supplied; usage terms and rate limits **[verify]**; it is a public API intended for exactly this, and we send artist/title/album/duration only).
- Synced display: line highlight + auto-scroll + tap-a-line-to-seek, offset adjust, cache on disk, "lyrics not found" state that is honest and quiet.
- **Effort:** M. **Value:** 4. **Legal note:** lyrics are copyrighted; we only fetch and display on request, never store beyond a private cache, never republish; LRCLIB is the lowest-risk source. We do not scrape lyric sites.

### 2.7 Android Auto, Chromecast, media session, car mode, widgets, notification
- **Media session (the known open item):** expose a **window** of the queue (current ±N, N≈50) to external controllers, correct metadata, artwork URIs via a `ContentProvider` (so Auto/Bluetooth never read 10,000 items: the system Bluetooth OOM found in beta 52). Custom actions (shuffle, repeat, favourite, speed).
- **Android Auto:** a `MediaLibraryService` browse tree (Home, Recent, Playlists, Albums, Artists, Downloaded, Search by voice) with paging, and the Jellyfin-only design (no flacie-web). Needs Google's Auto media app category and review for Play distribution; **sideloaded builds can test with Android Auto "developer mode"** **[verify]**. I cannot test it on the Fold without a car head unit or the Desktop Head Unit (DHU) tool: DHU runs on this PC against the phone, which is the planned test.
- **Chromecast:** `CastContext` sender in the Android app; the receiver must be able to fetch the stream, so for Jellyfin-only from outside the home network the URL must be reachable by the Cast device (Jellyfin's public address works; local-only addresses do not): detect and say so. Use the Default Media Receiver (no custom receiver app = no Google registration) **[verify metadata/queue support is enough]**. **Web:** Cast sender via the Chrome Cast framework (Chrome/Edge only). **Windows:** via WebView2 it is Edge: same path.
- **Car mode:** a large-target now-playing + browse UI (also useful as the in-app experience when Android Auto is not connected), auto-entered on Bluetooth car connection (optional).
- **Widgets:** Glance/RemoteViews widgets (now playing, queue next 3, favourites shuffle) with proper cover colour theming.
- **Notification/lock-screen polish:** correct artwork size, seek bar, custom actions, no stale state after stop.
- **Effort:** XL combined (Auto L, Cast L, session M, widgets M, car mode M). **Value:** Auto 5 if you drive with it (unknown), Cast 3, widgets 3.
- **Tests:** unit for the windowing maths; DHU for Auto; Fold for notification/lock screen/Bluetooth; a Cast **cannot be tested without a Cast device**: listed as "Not tested" unless you own one.

### 2.8 Ratings, play counts, stats
- Ratings: 5-star (half-star optional) stored in Jellyfin as a user rating **[verify `UserData.Rating` write support in 10.11; fallback: custom playlist-less tag in the shared profile]**; surfaced in lists, Now Playing, smart-playlist rules.
- Play counts: Jellyfin already counts; show them, and keep local counts for offline.
- Stats page: top artists/songs/albums by period (week, month, year, all), total listening time, streaks, by hour/weekday, and "genre mix". Source: local play log (new, small, append-only, exportable) merged with Jellyfin's `PlayCount`/`LastPlayedDate` for history before this feature existed. A year-in-review view later.
- **Effort:** M. **Value:** 3-4.

**Phase 1 beta exit criteria:** all unit/Playwright/.NET tests green; emulator regression (first-sync, scrolling, skip-burst, warm-start, covers) within the request budget in §7; Fold checks listed; release notes.

Suggested internal split (each its own beta if it is large): **1a** media session window + notification/lock screen + sleep/speed (S-M, fixes a known defect), **1b** offline downloads, **1c** loudness + EQ + crossfade, **1d** lyrics + ratings + stats, **1e** Auto + Cast + widgets + car mode.

---

## 3. Phase 2: metadata and covers

### 3.1 MusicBrainz client (`MusicBrainzGate`)
- Own gate: **1 request/second** across the whole app instance (MusicBrainz asks for an average of 1 req/s per IP **[verify current limit and the 503 behaviour]**), strict FIFO with priority for "user is looking at it" over background, honours `Retry-After`, circuit breaker, never parallel.
- User-Agent: `FLACie/<version> ( contact-url-or-email )` as MusicBrainz requires **[verify format]**. The contact must be a real address the owner chooses (a setting; default is the project's GitHub URL, not the owner's email).
- Cache: on disk, keyed by query, 30 days positive / 1 day negative; offline-tolerant.
- Matching: by existing MBID tag when present (Jellyfin exposes `ProviderIds.MusicBrainzTrack/Album/Artist` **[verify]**), else by artist + album + track count + duration tolerance; scored, never auto-applied below a threshold.
- Fills gaps: missing year, genre/tags, MBIDs, artist/album disambiguation, track titles, release type (album/EP/single/live/compilation), original release date.
- **Writes:** nowhere by default. Results live in FLACie's own metadata overlay (a small local DB, synced in the shared profile when flacie-web/Jellyfin account is present). A "Write to Jellyfin" action exists only for admins and only per item, with a before/after diff. **Never overwrites a non-empty user tag without the owner choosing** (diff dialog: keep mine / use MusicBrainz / edit).
- **Effort:** L. **Value:** 4.

### 3.2 Cover picker
- For any song/album/artist: sources are **embedded art, folder images** (via Jellyfin's images list), **Cover Art Archive** (via the release/release-group MBID; no key; rate limits **[verify]**, images served from archive.org: respect their guidance), **upload/paste/pick from device** (Android document picker + clipboard image; web file input + paste; Windows file dialog).
- UI: a grid of candidates with size, source label and a live preview in context; "use this" saves.
- **Where the choice is saved:** (1) FLACie's local overlay (always, instant, per item, keyed by Jellyfin item id and by a content key so it survives re-scans); (2) to Jellyfin as the item's Primary image **only if the signed-in user is an administrator and a Settings switch "Allow FLACie to change pictures on the server" is on** (off by default), via the standard image-upload API **[verify]**.
- **Precedence (the regression guard):** override chosen by the owner > the song's own Jellyfin picture > vetted album fallback > catalog lookup > placeholder. `CoverVetting` runs *before* overrides are applied and never touches overrides. An override is keyed by exactly the item it was chosen for (never by AlbumId).
- **Effort:** L. **Value:** 4 (you already hit wrong/missing covers).
- **Tests:** precedence matrix unit test; CoverVetting tests stay; atomic-save race test stays; picker with the mock (upload round-trip), real-library read-only check that no key changes for items without overrides.

### 3.3 Info panel: MusicBrainz match + "fix this match"
- Shows matched recording/release with MBID link, confidence, and what we filled from it; "Fix this match" opens a search (artist/title/album) over MusicBrainz candidates, choose one, saved as a per-item pin in the overlay (so the matcher never re-guesses). Honest "no match" state.
- **Effort:** M. **Value:** 3.

**Phase 2 exit criteria:** as Phase 1 + a read-only run against your real library proving zero unintended tag/cover changes (diff report), and MusicBrainz request log showing ≤ 1 req/s.

---

## 4. Phase 3: discovery and mixes

Ordering inside the phase: **radio engine → mix builder + smart playlists → weekly mixes**. The engine comes first because everything else is a front end to it.

### 4.1 Similarity engine ("cheap first")
One `Similarity` interface: `score(seed, candidate)` and `candidates(seed)`, combining weighted signals, each optional:
1. **Same artist / album artist** and **genre / sub-genre overlap** (from tags, plus MusicBrainz tags when Phase 2 filled them).
2. **Era**: year distance.
3. **Jellyfin "similar items"** (`/Items/{id}/Similar`) **[verify quality for music on 10.11]**.
4. **My history**: play counts, favourites, ratings, skips (new signal from the local play log), time-of-day affinity.
5. **External artist similarity** where available: MusicBrainz artist relations; Deezer "related artists" (already a dependency family); .
6. **Audio features (later):** a slot reserved in the interface for BPM/key/energy/embedding from an analysis pass. Not built now. Data model has a nullable `AudioFeatures` blob per track so adding it later needs no migration of callers.
The engine runs on-device from the saved library (no network needed for 1-4), is deterministic given a seed (testable), and diversifies (max N per artist, no immediate repeats). Same code semantics in Kotlin and C# (ported with shared test vectors, as already done for `TrackTitles`).

### 4.2 Radio
Artist / album / song / genre / decade radio = engine with a seed; endless (tops up the queue via the existing `onQueueLow` autoplay hook). Familiar-vs-new slider shared with the mix builder. Works offline over downloaded songs when no network.

### 4.3 Mix builder
Multi-select albums and artists → generate. Controls: familiar ↔ new slider, mood (existing mood list), era range, genre include/exclude, length, "include songs I don't own" (only suggestions, see 4.4), a preview list with per-row swap/remove, **Save as playlist** (a Jellyfin playlist, so it exists everywhere). 

### 4.4 Weekly Explore mixes (native, Explo-style)
- Weekly job (web server if present; otherwise on-device at app open, at most once a week): build a playlist of ~30 tracks from recommendation signals (§4.1), **match to the library** (the existing `matchKey` logic: title+artist, album tolerant), owned tracks play immediately, **unowned tracks show a "Request" button** that hands `artist/title/album` to the existing `DownloadCoordinator` (Soulseek, Lidarr, Jamendo, Audius, Internet Archive, SoundCloud). YouTube is used only if the owner enabled that opt-in source, and then last; the Request path is exactly the existing Search > Download path, with its existing safeguards and the owner's existing quality rule.
- Weekly cadence, "last week's mix" kept, "keep this track" promotes it to the library playlist, "dismiss" feeds the engine a negative.
- **Honest limitation:** the "new" tracks come from Deezer related-artist top songs + MusicBrainz relations: weaker than a real recommender. Said in the UI, not hidden.

### 4.5 Smart playlists
Rules builder (all/any): genre, year (range), rating, quality (lossless / Hi-Res / lossy, via audio facts), recently added (n days), never played, last played, play count, artist, album, downloaded. Live (re-evaluated), limit + sort, saved as a **FLACie smart playlist** in the shared profile (Jellyfin has no smart-playlist concept; the definition syncs, the membership is computed per device, with an optional "materialise to a Jellyfin playlist now" button). Same rules engine in Kotlin and C# with shared fixtures.

**Phase 3 effort:** engine L, radio M, builder L, weekly M, smart playlists L. **Value:** radio 5, builder 4, smart playlists 4, weekly 4 2 for you specifically.

---

## 5. Phase 4: design polish ("less cheap")

Treat as a real design pass with its own audit, not a colour tweak.

1. **Audit first** (agent per platform, read-only, screenshots into `docs/design/before/`): every screen on web (desktop/tablet/phone widths), Android (Fold cover screen, Fold unfolded, emulator phone), Windows (WebView2 window). Output `docs/design/audit.md`: a defect list with severity, grouped into the buckets below.
2. **Foundations (one shared token set, ported to Compose and CSS):** type scale (one family, 6-7 steps, tabular figures for times), 4/8-pt spacing scale, radius scale, elevation, a **single icon set** (one outline set at one stroke weight, drawn as vectors; zero emoji, zero mixed styles), one **motion language** (durations 120/220/360 ms, one spring for movement, one ease for fades; shared-element cover transitions list → Now Playing; reduced-motion respected), **haptics on Android** (tick on seek, confirm on like/add, long-press pop), **skeleton loaders** instead of spinners, empty/error states with a sentence and an action, **dynamic colour from cover art** (palette extraction with contrast clamps so text stays readable), **dark / light / AMOLED** themes (system-following), **tablet/foldable/wide layouts** (list-detail, a persistent queue pane at ≥ 840 dp, hinge awareness on the Fold), **accessibility** (4.5:1 contrast, TalkBack labels and focus order, 48 dp targets, large-text layouts that don't clip, keyboard/D-pad navigation kept for the RG).
3. **Copy pass:** every user-visible string reviewed; kill debug text, "Unknown" placeholders where a better empty state exists, inconsistent verbs ("Sign in" vs "Log in"), exclamation marks, and robotic phrasing. A short voice guide (`docs/design/voice.md`): plain, warm, specific, no jargon, no cleverness.
4. **Reference comparison:** Plexamp and Symfonium **public marketing screenshots only**, linked not copied; a written "what feels different" list (density, motion, typography, cover treatment, how empty states talk).
5. **Before/after** screenshot per screen in `docs/design/`, produced by a scripted capture (Playwright for web/Windows widths; adb screencap on the Fold and emulator), so they can be regenerated.
6. **Guardrails:** no regression in cold start (< 500 ms to first frame, Home from snapshot), gate budgets, or the RG Rotate square-screen layout (verified by the existing `fake WxH` extras on the Fold/emulator, not on the RG).
- **Effort:** XL (it touches everything). **Value:** 5 for "feels hand-crafted". Done as a sequence of small PRs per component family, not one giant change. The frontend-design guidance is applied to the web/Windows UI; Compose gets the same tokens by hand.

---

## 6. Phase 5: verify and release
- **Jellyfin-only regression:** fresh install, a fresh account, a Jellyfin URL and nothing else (no flacie-web, no MusicBrainz reachability, no NAS, no download sources): every screen loads, every feature either works or shows an honest "needs X" state, no crash, no request to a service the owner did not configure. A scripted emulator run + a manual Fold run.
- **Load:** re-run the 10,000-song mock matrix (fast/slow/flaky/ratelimit) before/after; budgets in §7.
- **Platforms:** Fold (wireless adb), emulator (throttled/flaky), web (Playwright), Windows MSI (build + install/upgrade/uninstall test on this PC if the owner allows an install; otherwise "built, not installed").
- **By-ear list for the owner:** crossfade feel and length, gapless on an album, loudness levelling between a quiet and a loud album, EQ presets on the owner's headphones, sleep-timer fade, speed with pitch correction.
- **Release notes** with Tested / Not tested; flacie-web redeploy only if the server changed (rollback tag first, `/healthz`, NAS login stays off, **confirm the owner can sign in as admin** — Quick Connect initiation checked by me; completing it needs the owner's approval on their device, which I will ask for at that point).

## 7. Budgets (checked every phase, fail the beta if broken)
| Metric | Budget |
|---|---|
| Cold start to Home on screen (Fold, release build, warm caches) | < 500 ms from process start (beta 56 measured ~70-90 ms) |
| Visible rows with a cover after 1 s / 3 s / 10 s (warm / empty cache) | 100 % of rows that have a picture on the server |
| Jellyfin requests, 10,000-song mock first sync, 2 min | ≤ beta 52 numbers (67 requests); no per-song requests; covers fetched once |
| MusicBrainz | ≤ 1 request/s sustained, only on user action or a low-priority background fill, never at launch |
| Peak simultaneous Jellyfin requests | ≤ gate limit (visible floor 4) |
| Tests | Android unit, .NET, Playwright all green at each beta; no test weakened to pass |

## 8. How the work is run
- **Agents:** research agents (Phase 0) in parallel, read-only. For build phases: **one agent per independent feature in its own git worktree and branch** (`phase1/offline`, `phase1/eq`, ...), **only one agent drives the Fold at any time** (a lock file in the scratchpad; emulator work does not need it). Agents commit often and push their branch; **I read each report, diff the branch, rerun its tests myself, and only then merge** to the phase integration branch (`phase1/integration`). Agents that hit the 5-hour usage limit resume from their branch.
- **Parity rule:** a feature is "done" only when Android, web and Windows each have it (or the platform exception is written in the notes). The shared logic (similarity, smart-playlist rules, fade planner, loudness maths, cover precedence, MusicBrainz matching) is specified once, with **shared test vectors** (JSON fixtures) that both the Kotlin and C# test suites load, so the ports cannot drift.
- **Betas:** beta 56 = cold start + covers (in progress, see below). Then beta 57 = Phase 1a ... one beta per sub-phase, each with release notes, all tests green, signed with the same key.
- **Order of risk:** media session window (fixes a real defect) → offline → loudness/EQ/crossfade → lyrics/ratings/stats → Auto/Cast → MusicBrainz → cover picker → engine/radio → mixes → design pass (design foundations are started early as a small token PR so later features are built on them, the sweeping restyle comes after the features exist).

## 9. Risks and things I cannot do alone
- **Cannot hear audio** (crossfade, loudness, EQ, gapless): by-ear list for you.
- **No car, no Cast device** (as far as I know): Android Auto via the Desktop Head Unit only; Cast "not tested" unless you own a Chromecast.
- **Play Store requirements** for Auto (not needed for sideloaded GitHub APKs).
- **Windows MSI install tests** modify this PC: asked before doing it.
- **Browser offline limits** may cap the web's offline feature below the phone's.
- **Scope:** this is roughly a year of normal engineering. Phase 1 alone is bigger than everything shipped since beta 50. I will not pretend otherwise: each phase is cut into betas that are individually useful, and the gap table is allowed to demote items.
- **API facts marked [verify]** are unverified until Phase 0.

## 10. What I need from you before Phase 1 (decisions, with my defaults)
1. ~~ListenBrainz~~ cut (section 0).
2. Do you drive with this phone (Android Auto) and own a Chromecast? **Default = build Auto (DHU-tested), build Cast (untested)**; say which to drop.
3. Do you listen to spoken word (podcasts/audiobooks)? **Default = playback speed ships, per-item memory only.**
4. MusicBrainz contact string for the User-Agent: **default = the GitHub project URL**, not your email.
5. Allow the "write cover to Jellyfin" admin switch to exist (off by default)? **Default = yes, off.**
6. Windows MSI install test on this PC: **default = ask each time.**

---

## Appendix: where the in-progress beta 56 stands (so nothing is lost)
Branch `perf/coldstart-covers` (pushed): cold start (Home from a saved snapshot: shelves on screen at ~65-90 ms instead of 785-1074 ms on the Fold), library load after the first frame, covers (atomic save fixing a real race, retry with backoff, visible-row gate floor, wrong album covers vetted: 1,807 of 10,325 songs on the real library, same rule on the web server). Remaining before release: Playwright run, release build + signing check, Fold final numbers, release notes, web redeploy. The programme above starts after beta 56 is out.

## Phase 0 findings (2026-10-13, from `docs/research/`)
What the research resolved, and what it did not:
- **MusicBrainz**: about 1 request/second per IP on average, excess answered with HTTP 503; a descriptive User-Agent of the form `App/version ( contact )` is required, generic ones are throttled; core data CC0, free for non-commercial use. Confirmed by the MusicBrainz API and rate-limiting pages. **Still open:** an explicit caching rule (none published), the exact 503/`Retry-After` behaviour.
- **Cover Art Archive**: `/release/<MBID>/front[-250|-500|-1200]` and `/release-group/<MBID>/front`; 404 = no art; no published rate limit (stay polite, cache negatives); **image licensing is not stated**: treat as display-only, never redistribute. Confirmed.
- **LRCLIB**: no published rate limit or terms found; keep it optional, cached, one request per song. Not confirmed.
- **Deezer**: terms allow non-commercial use only and say artwork belongs to Deezer or the rights holders. **This constrains the catalog cover lookup the app already uses** (beta 57's no-picture songs): keep it a fallback, cached, never written back into the user's files. Rate limit and image sizes not confirmed (pages are login-gated).
- **Still [verify] and needing a real Jellyfin 10.11** (read-only, authenticated; not available to the research or the mock): `NormalizationGain`/LUFS fields, `UserData.Rating` write support, `ProviderIds.MusicBrainz*`, `/Items/{id}/Similar` quality for music, download/stream endpoint behaviour for original files, web storage quota for the Service Worker design.
