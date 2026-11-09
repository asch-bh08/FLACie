# Handoff, 7 Nov 2026 night: read this first

## 0a. Queue after b123 (8 Nov): do these next, in this order

**Released 8 Nov 23:20:** https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-b123 (APK + MSI). Web deployed (rollback `flacie-web:rollback-b123a`); flacie-ytdl redeployed with song links (backup `server.py.bak-20261108-songlink`, image `flacie-ytdl:rollback-20261108`). Also in b123: one song from a SoundCloud/YouTube link (web Search + Import, phone search; `PlaylistLinks.SongLink`, `DownloadCoordinator.DownloadLinkAsync`, `POST /api/download {url}`, ytdl `fetch_url`), play/pause in full-screen graphs, volume bar press/drag/wheel (`flacie.volPtr`), Share tile instead of Lyrics, PC bar tiles lined up under the title. Not tested live: a real SoundCloud song end to end.

**b123 holds** (on top of b122): Web A on the PC full-screen player (tiles Add · Lyrics · More | transport | Fav · EQ · Sound; Sound pops up on hover with a tall volume bar, moving bars, even out, fade; rating, speed, sleep in More; `SoundTile.razor`); on phones DJ and mic back in Now Playing's top bar at 52 dp (`LivingSleeve.kt` `SleeveTopBar`, web `.ls-phone-top`); every live graph in the Info tab opens full screen (button or a tap; Android `FullGraph` in `InfoPanel.kt`, web `flacie.graphFull`).

**9 Nov:** DJ names: `DjPronounce` StarterList now has sounds for Katy Perry, Lady Gaga, Gaga, Bobo and every other name the by-ear check (frank `data/dj-pronounce.json`, voice `kokoro:am_liam`, 57 of 169 heard wrong) had none for; `SoundsVersion` "sounds-3" so clips are made again. Web deployed with it (rollback `flacie-web:rollback-b123djnames`). Finding: misaki on frank reads these names fine as plain text (checked), so the remaining suspects for garbled names are the Whisper check's own mishearing and the old clips; listen after the clips are remade.

**Before 20 Nov 2026 (Cloudflare retires models):** `@cf/myshell-ai/melotts` stops working. In FLACie it is the "MeloTTS" DJ voice (`CloudflareVoice.MeloTts`) and also the fallback `NormVoice` returns for any unknown voice. Make the fallback the default voice (Kokoro on frank, else Aura), map a saved "melotts" to the default once (like the existing Version migration, bump `ConfigVersion`), drop MeloTTS from the Dashboard picker and test buttons (`Admin.razor` ~309/343), and update the tests that use it (DjClearTests, DjRoundTwoTests, DjServiceTests, DjSilentTests, DjVoiceTests, DjVoiceVolumeTests, DjWriterTests). `@cf/qwen/qwq-32b` is only in `DjBudget.Weighed` (a cost comparison): remove it. Nothing else FLACie uses is on the list (Llama 3.3 70B, Llama 3.1 8B, Aura-1/2 and Whisper large-v3-turbo stay).

**Phone's FLACie Web address flips (9 Nov):** `TopBar.razor` `ShareAddress()` saves whatever address the web page was opened at into the profile (`services.flacieweb.url`), and the phone uses that address for downloads, seek-bar shapes, share links and the DJ. The owner opens the web at two addresses (frank's own, and a second route through a Caddy proxy on the Proxmox host; both reach the same server today), so the phone's address follows whichever was used last, and a LAN or temporary address would break the phone away from home. Fix: keep one stable address: a "FLACie's address" setting in Server admin (or FLACIE_PUBLIC_URL), defaulting to the first tailnet address seen; ShareAddress writes that, never the page's own address; Settings > About shows it beside "you opened this page at". Test both.

**New bugs from the owner (9 Nov), do first:**
- A. "Play Out of Space by the Prodigy" through the DJ mic played a random demo, not the album track. Then "the other one" failed badly. Look at the version ranking in the DJ ask path (`DjAsk`, the version picker, "the other one" follow-up) on web (`flacie.js` / `DjEndpoints`) and phone (`DjAsk.kt`): a demo/remix/live version must lose to the album version; "the other one" must move to the next-best version and say which.
- B. The DJ mic got stuck: it worked, the music came back, then the mic could not be pressed again because it still said "listening". The listening state must end when the answer is spoken / music resumes, after an error, and after a timeout; check web `DjMic.razor` + `flacie.js` and phone `DjMicUi.kt`. Add a test that presses the mic twice in a row.

Queued (the owner asked for these and they are not done):
1. **The rest of the UI overhaul**, web + phone (+ Windows via web): the sidebar, Home's menus and style, Albums/Artists/Genres/Explore, Favourites, Playlists, Downloads, Dashboard, Account, Settings; the same on the phone's pages; fewer icons with words; circles for artists and round buttons, squares for covers; fluid motion (shared-element moves, springy sheets, smooth list changes) within the performance rules. Start with a page sweep at 1440 and 390 px.
2. **Fix the test-order failures** so the suite runs clean: 1-pages "Quick picks learn…", 1-pages "bottom bar at 375/320px", 1q-dj "Aura-2 voices", 1x-new-look "the next two songs get their shapes…", and check 1z-dj-ask "endpoint understands requests" + 1z-dj-bar "Listening… then its words" (failed 8 Nov; not checked whether they fail without b123's changes).
3. **Search rows with only the label** ("Song"/"Album"/"Playlist", no title): not reproduced; ask the owner for the page address.
4. **Parity leftovers** (`docs/parity-2026-11-07.md`): the Alexa link on the phone, the Charts page on the phone.
5. **Web A on narrow PCs**: the tiles shrink to 62 px at 761–1100 px; check 761–900 px against the transport and the Sound pop-up there.
6. **Tests gotcha:** Playwright reuses a server already on WEB_PORT; after changing Razor/CSS, stop the old server (or pick new ports) or the run tests the old build.

## 0. Take over here (8 Nov, morning)

**Done and released as b122** (https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-b122, APK + Windows MSI, commit d812b66c). Web deployed to frank (rollback tag `rollback-b122a`). Both branches pushed.

1. EQ back on the web: an "EQ" pill in Now Playing's bottom bar (presets + five sliders), `EqMenu.razor`, `SceneEq.Word`.
2. Even controls, web and phone: DJ · shuffle · prev | PLAY | next · repeat · mic (`NowPlaying.razor`, `LivingSleeve.kt` `SleeveControls`).
3. The "Getting N songs" line is admins only (web `DownloadActivity.razor`; phone `DownloadActivity.kt`, offline saving still shown to all).
4. Search shows your playlists holding the song/artist, "Has “X”" / "N songs by Y" (`FLACie.Core/PlaylistHits.cs` + Kotlin twin, "Your playlists" row on the phone).
5. Kind label on every search result (web SONG/ALBUM/ARTIST/PLAYLIST tags; phone "Song · …").
6. UI overhaul, **first pass only**: song menus grouped on web and phone (Add to playlist ›, Not for me ›, The DJ and this song ›; phone rarer items under More…); web bar's Retro + Mini player in a worded More menu; springy web menus and flowing rows/cards; Settings' Volume limit styled; Devices/Jam text in sans; phone Home lost its duplicate Settings cog.

**Not done (pick up here):**
- The rest of the UI overhaul: the sidebar, Home's menus and style, the pages that never got attention (Albums/Artists/Genres/Explore, Favourites, Playlists, Downloads, Dashboard, Account), the same pass on the phone's pages, and more shared-element style motion. Screens to compare against: run a page sweep at 1440 and 390 px (a throwaway spec that screenshots each route).
- The owner's screenshot of search rows showing only "Song"/"Album"/"Playlist" with no title: not reproduced on web or phone. `1z-search-tags.spec.ts` fails if any result loses its title. Ask the owner for the page address if it comes back.
- Pre-existing test-order failures (also fail on 93b7663a, pass alone): 1-pages "Quick picks learn…", 1-pages "bottom bar at 375/320px" (rail shows 7 links after earlier tests), 1q-dj "Aura-2 voices". Worth fixing so the suite runs clean.
- `1x-new-look` "the next two songs get their shapes…" fails on 93b7663a too (wave asked twice).

**Traps found tonight:** in `heavy.ps1 -Cmd`, start with `powershell -NoProfile -ExecutionPolicy Bypass -File …` (a bare .ps1 is opened by cmd, not run); for the playlists driver's `--testapk`, call an inner .ps1 with no arguments. Playwright wipes `test-results/` on every run: read screenshots before the next run.


The last session stopped at about 80% of the weekly limit, at the owner's request ("pass it all to another session"). This file is all you need. The previous session's memory files may not be available to you, so the owner's preferences are written out here.

Branch `ui/living-sleeve`, mirrored to `main`. Push both: `git push origin ui/living-sleeve && git push origin ui/living-sleeve:main`.

## 1. What the owner wants overnight (7 → 8 Nov)

The owner is away and wants **a beta in the morning with fixes and new things, on web, Android and Windows.** Watch the limits and log as you go (§5).

In priority order:

1. **Equalizer back on the web.** Now Playing used to offer it; the owner can't find it now. The code still exists: `eq()`, `tone()`, `bass()` in `ipodsync/src/FLACie.Server/wwwroot/flacie.js`, five bands. Find where its button went (Now Playing's bottom-right icons: star, gauge, moon, wave, volume) and make it obvious.
2. **Even playback controls.** Today the web row is shuffle · prev · PLAY · next · mic · repeat, which is lopsided. The owner likes the **DJ button on the left**, so the row balances, e.g. DJ · shuffle · prev · PLAY · next · repeat · mic. Do it on web **and** phone.
3. **The "downloading N now" line above Now Playing** (the DJ bar's downloads line) is **for admins only**.
4. **Search shows playlists that contain the song.** Searching "dreamer", "with me", "eminem" or "aura" should list the playlists the song or artist is in (the owner's own first), plus recommended playlists. Web and phone.
5. **Every search result gets a type label or symbol** (Song · Album · Playlist · Artist), like Spotify, so the right song is easy to spot. The owner sent a screenshot of result rows showing **only** the label ("Song", "Playlist", "Album") and no title, cover or artist. Find which screen that is (likely the web or phone search "all results" list) and fix it.
6. **A full UI overhaul, on phone and web, and on Windows**, which runs the web UI:
   - Menus are too complicated, and there are icons nobody understands. Use fewer icons with clear words, and simpler song menus. Now Playing's "…" menu has 11 items.
   - Not everything square: circles for artists and round buttons, squares where they fit (album covers).
   - **Fluid, smooth animations.** More than fades: shared-element style moves, springy sheets, smooth list changes. Keep the performance rules in §3.
   - The sidebar, Settings, the Home page's menus and style (the content is fine), and **the pages that never got attention**: go through all of them.
7. **Release beta b122 in the morning, whatever happens: it must work.** The owner wants a beta either way. Commit each item separately, so it can be left out. Before releasing, run every check below on the final build. **If an item makes a check fail and you can't fix it in time, revert that item's commits (`git revert`) and release the rest.** Write in the release notes and the log what was left out and why. Never release a build that fails a check. At the very least, b122 carries the fixes already committed since b121 (AccountSync keeps repeated songs) plus whatever item passes.
   - Server tests (1,945; a single known flake may pass on a rerun).
   - The web tests for every page you changed, plus `1i-download-tags`, `1r-search-artist`, `1z-settings-sound` and `1q-dj`.
   - Android unit tests.
   - All four emulator drivers on the release APK (debug + test APK for playlists): `smoke-android.mjs` 23/23, `playlists-android.mjs` 23/23 plus the stand-in car 9/9, `search-android.mjs` 9/9, `dj-android.mjs` 3/3.
   - Look at a screenshot of each changed screen, on the phone (emulator) and the web.

**Status 8 Nov, 02:50: items 1–5 done, item 6 first pass done, b122 released** (https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-b122, APK + MSI) and the web deployed. Details in `docs/overnight-2026-11-07.md`. **Next:** the rest of the UI overhaul (the sidebar, Home's style, the other pages; phone pages too), and the owner's "rows with only the label" screenshot, which could not be reproduced (test `1z-search-tags` guards it). Known test-order failures, not from tonight: 1-pages "Quick picks" and "bottom bar 375/320", 1q-dj "Aura-2 voices" (fail after other tests, pass alone; same on 93b7663a). Wrapping an emulator driver in heavy.ps1: the -Cmd must start with `powershell -File`, not the .ps1 itself (cmd opens it instead of running it); for the playlists driver's `--testapk` use an inner .ps1 with no arguments.

Done before the stop on 7 Nov:

- **b121 released**: playlist matching, DJ timing, swoosh, DJ playlist wording. https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-b121
- **Web**:
  - Imports mirror their source exactly (order and repeats; 80s–90s rally is 99/99).
  - Artist-name fixes: 888 approved and written to Jellyfin.
  - The artist search shows Albums and Playlists half each.
  - A **full-screen preview** before downloading in More music (`CatalogPreview.razor`).
  - A **Volume limit** setting.
  - ntfy **quiet plays** for user `hms`. In FLACie: Dashboard › Notifications. For Jellyfin app plays: `/opt/ntfy-digest/config.json` `jellyfin.quiet_users`, with a small code change in `digest.py`. Backups `*.bak-20261107`.
- **frank's system disk** was cleaned from 85% to 49%. **Keep only the newest 3 `flacie-web:rollback-*` images** after each deploy, and run `docker builder prune -af` at `nice -n 19 ionice -c3`.
- Parity notes: `docs/parity-2026-11-07.md` (what's on one side only).
- **Not released yet, phone side**: `AccountSync.kt`'s Jellyfin copy of a playlist keeps repeated songs (`.distinct()` removed). It goes out with the next beta.

## 2. How to build, test, deploy, release

- **Server tests:** `cd ipodsync && dotnet test tests/FLACie.Tests -v q --nologo`. 1,945 tests. Usually one flaky test passes on a rerun.
- **Web tests:** `cd ipodsync/tests/web-smoke && MOCK_PORT=8396 WEB_PORT=5490 FM_PORT=8397 npx playwright test <files>`. Use fresh ports, or a reused server keeps old state. The full suite takes about 55 minutes. **Never run the web tests and the emulator at the same time.**
- **Android build:** `powershell -NoProfile -File C:/FLACie-handoff/heavy.ps1 -Dir C:/FLACie/android -Cmd ".\gradlew.bat :app:testDebugUnitTest :app:assembleRelease :app:assembleDebug :app:assembleDebugAndroidTest --no-daemon -q"`
- **Emulator tests:** `ANDROID_SERIAL=emulator-5554`. From `ipodsync/tools/loadtest`, run `run-android.ps1 -Apk <apk> -Label <x> -Profile fast -Port 8297 -Driver <driver>.mjs`. The drivers:
  - `smoke-android.mjs` (23 checks)
  - `playlists-android.mjs`: the debug APK plus `-Extra "--testapk …app-debug-androidTest.apk"`. It also runs the stand-in Android Auto test. Run it from a .ps1 file to avoid quoting problems.
  - `search-android.mjs`
  - `dj-android.mjs`: its fake voice takes 4.5 s like the real one.

  Wrap them in `heavy.ps1`. **Never run the drivers on the owner's phone**: they wipe the app's data.
- **Web deploy to frank** (from `ipodsync/src`):
  ```
  ssh root@frank 'docker tag flacie-web:local flacie-web:rollback-<name> && docker rmi <oldest of the 3 rollback tags>'
  tar --exclude=bin --exclude=obj -czf - FLACie.Core FLACie.Retro FLACie.App FLACie.Server | ssh root@frank 'rm -rf /tmp/fw && mkdir /tmp/fw && tar --no-same-owner -xzf - -C /tmp/fw && cd /tmp/fw && docker build -q -f FLACie.Server/Dockerfile -t flacie-web:local . && cd /home/hms/docker/flacie-web && docker compose up -d --force-recreate && nice -n 19 ionice -c3 docker builder prune -af'
  ```
  Then check `curl http://127.0.0.1:5255/healthz` on frank. If the build fails at apt-get, retry: it's a Debian mirror blip.
- **Release.** Bump the version in six places:
  - `android/app/build.gradle.kts`: versionCode +1, versionName "1.0 bNNN"
  - `AppInfo.cs`
  - `FLACie.Server.csproj`
  - `IpodSync.Maui.csproj`: ApplicationDisplayVersion
  - `tests/FLACie.Tests/G2Tests.cs`
  - `tests/web-smoke/1d-g2.spec.ts`

  Write the notes in `docs/private/release-bodies/v1.0.0-bNNN.md` (gitignored). Then run `gh release create v1.0.0-bNNN --repo asch-bh08/FLACie --prerelease --target <full sha> --title "FLACie 1.0 bNNN" --notes-file … <apk> [msi]`. Copy the APK to `C:/FLACie-handoff/FLACie-1.0-bNNN.apk`. **Pre-releases are allowed without asking** once the tests and the emulator smoke pass. Next is **b122**.
- **Windows** = the MAUI app in `ipodsync/src/IpodSync.Maui`, which wraps the web UI. Web UI changes reach it. Build the MSI as in earlier releases (see `docs/private/HANDOFF.md`, "How to build, test, deploy, release").

## 3. The owner's preferences (follow these)

- **Replies:** short, plain language, no jargon. The owner types fast with typos, so read for intent. Tell them what changed for them, and what's still not done.
- **Speed and visible results:** ship pieces as they're finished. Each piece: test, commit, push, deploy.
- **TDD where practical:** a failing test first, then the fix. For visual work, take a screenshot and look at it before calling it done.
- **Every feature must work on the phone too, not only on the web**, and the other way round. Windows gets the web UI.
- **Code style:** match the surrounding code. Comments in plain British English ("colour"), saying what the person sees and why, often quoting the owner with a date ("owner, 7 Nov: …"). Commit messages say what changed for the person. End each commit with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (or your own model).
- **Git:** `git add` named files only, never a whole folder. Check `git show --stat HEAD` after committing.
- **Performance:** no CSS blur or drop-shadow on moving things, no continuous CSS animations, and only compositor-friendly transforms (transform, opacity).
- **Agents:** allowed, but at most 1–2 at a time. Use Sonnet for well-specified jobs, Opus only for real design work, **never Haiku**. Watch the usage.
- **Limits:** check `get_usage`. **Hand off at 96% of the weekly limit.** If a limit stops work, set one timer until just after the reset, then resume.
- **Safety:**
  - Never print or write the Cloudflare token (it should be rotated).
  - frank's data is read-only except for deploys and changes the owner approved.
  - Never run heavy model tests (like Whisper) on frank.
  - Tests never reach the real Jellyfin or frank. Mocks must pass `requireMock`, and test files must not name the owner's servers: use example.ts.net or 192.0.2.x.
  - The owner's phone may be used to read and test, never to sign out, switch accounts or delete data.
  - Don't reuse Spotify's or SoundCloud's web keys.
- **People may be streaming:** a deploy restarts the server for a few seconds. Tell the owner to reload afterwards.

## 4. Useful facts

- **frank:**
  - `ssh root@frank`.
  - FLACie Web runs in the container `flacie-web`, with its data in `/home/hms/docker/flacie-web/data`.
  - Jellyfin's config is in `/srv/jellyfin/config`. Copy `jellyfin.db*` (with its `-wal` file) before reading it.
  - Users: the owner is `hms`, and their user data file is `E3816B1F….json`.
- **Real-data checks without touching anything:** copy `data/library/<user>.v2.json` (the library) to the scratchpad. Then write a temporary xunit test (`TmpProbeTests.cs`, deleted afterwards) that loads it and runs the real code. This proved the playlist and artist-fix safety tonight.
- **Earlier logs:** `docs/overnight-2026-11-06.md` (b117–b121), `docs/overnight-2026-11-07.md` (tonight's list and progress), `docs/parity-2026-11-07.md`.

## 5. Log as you go

Add a line per step to the Progress log in `docs/overnight-2026-11-07.md`, and commit and push after each item. When near the limit, update this file's §1 with what's done and what's next, so the next session can carry on.

## 6. Traps learned the hard way (read before touching code)

**Editing:**
- **Python heredocs break `\n` and `\`** in Kotlin and C#: they come out as real newlines or single slashes. Use the Edit tool for any text with escapes. Check `git diff` after any scripted edit.
- **Razor:** never `@{ … }` inside an `@if`/`@foreach` block (write `var x = …;` directly). Local names clash across the whole file (`listCount` and `p` already exist in some): pick unusual names. Put complex lambdas in a method or property, not inside an attribute.

**Testing:**
- **Web tests reuse running servers**, which keep their state, so a second run fails for no real reason. Use fresh ports (`MOCK_PORT=8396 WEB_PORT=5490 FM_PORT=8397`).
- **A fake that answers instantly hides timing bugs.** The DJ's real voice takes 1.5–4.5 s. `dj-android.mjs` already waits 4.5 s; do the same for anything timed.
- **Read the real data before guessing.** The phone's own log reaches frank: `/home/hms/docker/flacie-web/data/diag/<user>.jsonl`. It has "line dropped", "started (skip)", queue events and so on, and it found tonight's DJ bugs in minutes. Server logs: `docker logs --since 1h flacie-web`.
- **Prove safety on real data, read-only.** Copy the library (`data/library/<user>.v2.json`), the profile (from the jellyfin.db copy) and the import lists to the scratchpad. Run the real code in a temporary xunit test (`TmpProbeTests.cs`, deleted afterwards). Tonight this proved 787 playlist entries stayed identical before the artist approval.

**Playback (Android):**
- **`onMediaItemTransition` fires inside `setMediaItems`**, before the shuffle order and the queue are set. Anything it triggers must be `handler.post`ed (that was the "playlist plays recommendations" bug).
- **Queue generations** (`queueGen`, Autoplay `refill(seed, gen)`) stop old Autoplay results joining a new queue. Keep them if you touch play()/playFrom().
- **The release APK is minified**, so the stand-in car test (`CarPlaylistTest`, androidTest) needs the **debug** APK plus its test APK.
- **One heavy job at a time**: Gradle, the emulator, the web tests and `dotnet test` each load the PC. Run them one after another (`heavy.ps1` helps).

**Server (frank):**
- **`jellyfin.db` copies need the `-wal` file too**, or you read stale data. Jellyfin also has **old duplicate items** under lower-case `/media/movies/…` and `Music-pre2015`. FLACie's library doesn't include them; don't "fix" them.
- **A Jellyfin song's artist on the phone and server** is the **first** of `Artists` (else AlbumArtist). Playlists, favourites, ratings and plays are keyed by item path or ID, not by name, so renaming artists is safe.
- **Two things send phone notices.** FLACie's Notifier (web and phone plays, downloads, imports) and a separate service `/opt/ntfy-digest/digest.py`, which watches Jellyfin's own apps (systemd `ntfy-digest`, self-test `python3 digest.py --selftest`).
- **`docker build` sometimes fails at apt-get** (a Debian mirror blip): just retry. A failed build leaves the running server untouched.

**Process:**
- **Release notes** go in `docs/private/release-bodies/`, which is gitignored on purpose (don't add it).
- **`NoRealServerInTestsTests` fails the build** if a test file names frank, its Tailscale name or the owner's IPs. Every driver must call `requireMock`.
- **The owner checks results on their Galaxy Z Fold7** and the web (Brave on Windows). Phone layouts must work folded and unfolded.
