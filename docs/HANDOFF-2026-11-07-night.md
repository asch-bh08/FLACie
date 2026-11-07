# Handoff, 7 Nov 2026 night: read this first

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

Done tonight (before the stop):

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
