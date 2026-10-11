# What is left for beta 50 (written 2026-10-11, mid-session)

Public-safe (no private names or addresses). Device addresses are in `docs/private/HANDOFF-2026-10-11.md`.
Branch: `release/beta50` (pushed; the newest edits below are NOT yet committed). Nothing is released or deployed yet.

## Done and checked
| Item | Result | Where |
|---|---|---|
| Merged F1, F2, F3, F4, F5, server-E into `release/beta50` | merged; F1 and F4 had compile errors, fixed | PC |
| .NET tests (FLACie.Tests) | 66 / 66 pass | PC |
| Android unit tests | 78 run, 0 failed, 2 skipped | PC |
| Browser tests (Playwright) | 79 pass, 2 fail (both in the never-run `1c-api.spec.ts`; see "Open") | PC |
| Windows MSI 0.50.0: install, launch, one instance only, licences present, no .pdb, uninstall closes the running app | pass | PC |
| Release APK builds, signed with the same debug key as beta 48/49 (SHA-256 6a4eb335...) | pass | PC |
| Playback keeps going with the screen off; media keys work while asleep; media notification and audio focus held | pass | RG Rotate |
| 20 quick Next presses: music starts about 2 s after the last press (was 38 s) | pass | RG Rotate |
| Seek bar updates when seeking while paused | fixed in code, NOT re-tested on a device | - |
| Out-of-memory crash while saving the library (RG Rotate, big library) | fixed (written as a stream); not seen again | RG Rotate |
| Explore list jank, RG Rotate | beta 49 5.7 % janky; beta 50 1.9 % to 7 % once the NAS scan is finished; 70 to 85 % WHILE the first NAS tag scan runs (see Open 2) | RG Rotate |

## Open problems (must be fixed or stated in the release notes)
1. **Fold crash (HIGH), still reproducing.** Home tab, music playing from the phone, scrolling Home up and down while pressing Next:
   `ArrayIndexOutOfBoundsException` inside Compose (slot table). Upgrading Compose (BOM 2024.09.03) did NOT fix it; it died again after
   about 5 minutes (release) and after about 320 loop iterations (debug). A code change that removes an early `return` inside the Home
   lazy-list item (`PlayerUi.kt`, HomeScreen) is written but not yet built or tested. Test: run the loop below on the Fold for 700 iterations;
   if it still dies, replace the nested `LazyRow` inside the quick-picks item with a plain scrollable `Row` and the `BoxWithConstraints` in
   `ForYouUi.kt` with `onSizeChanged`.
2. **First NAS tag scan makes lists jerky (HIGH for first run).** F1's background reading of NAS tags keeps the screen at 70 to 85 % janky
   while it runs. Mitigations added: 2 workers at background priority, library rebuild at most every 10 minutes, scan pauses while you touch
   the screen. The pause could not be proven because the scan on the test device had already finished. Needs a fresh install or cleared app data
   on a device with a big NAS (needs the owner to sign in again).
3. `1c-api.spec.ts` (written by the server workstream, never run before): 2 failures. (a) `/api/import` with a junk body answers 200, the spec
   expects 400: the server treats junk as a one-line text playlist. (b) `/art/..` is turned into `/` by the HTTP client, so it answers 200.
   Probably wrong expectations, not server bugs; I was not allowed to change the spec, so decide.
4. From the test run, not fixed: Charts download of a "(Piano Version)" saved the original length; Autoplay downloads the same songs again;
   mobile bottom nav overlaps at 375 px for admins; late start after skip bursts on web; no queue reorder on Android/web; tap targets under 44 dp
   on RG Rotate; Home Quick picks have no D-pad focus ring (RG); Explore header takes half the RG screen. See the workstream files.
5. Encoding: a script of mine briefly mangled non-ASCII characters in three Android files; repaired and re-checked, but run
   `git diff origin/main` on `ExploreUi.kt`, `Library.kt`, `UpNextDrawer.kt` once to be sure. Several files also gained a UTF-8 BOM (harmless, builds fine).

## Still to TEST, by device
### Z Fold 7 (cover screen and inner screen; wireless adb)
- Crash loop for 700 iterations after the Home fix (script idea: tap a Quick pick, then swipe Home up and down, press Next every third round).
- 30-minute memory loop on the RELEASE build. First attempt: Java heap climbed from 120 MB to 190 MB (limit about 192 MB) in 13 rounds, then the crash. Re-run after the crash fix and record the curve.
- Inner screen, orientation change, dark/light, font and display scaling, split screen, 3-button navigation.
- Jams / Connect between Fold, RG Rotate and the web (control and playback hand-over).
- Background playback, lock screen, audio focus (another app starts playing), kill and restore, offline and network switching, expired session.
- First launch with the new explanation screens (F3), including the notification permission being asked only when needed.
- NAS songs show real titles, durations, embedded covers (F1); Jellyfin songs with file-name titles are cleaned (F2).

### RG Rotate (USB)
- Not run: rotation, dark/light, font scale, folders, lyrics, sleep timer, shuffle/repeat/gapless, playlist operations, downloads, settings, headphone test, remaining media keys, kill/restore, offline, startup time.
- Re-measure list jank: `android/tools/measure-jank.ps1 -Device <serial> -Runs 5` on Explore > Songs (open Explore first).
- Home Quick picks D-pad focus ring; D-pad navigation through the new first-launch screens.

### Web (FLACie Web, built-in browser or Chrome)
- Run through the pages against the real server after the deploy: sign-in as admin, playlists, charts, downloads, settings, notifications toggles.
- Titles of untagged songs (F2) and the skip-burst behaviour.
- Mobile layout at 375 and 320 px (bottom nav overlap).

### Windows (PC)
- Real sign-in in the installed app (needs the owner's Quick Connect approval), playback, an upgrade from 0.49 over a running app.
- Smoke test passed for install/launch/uninstall only; the app UI itself was not looked at.

### Release and deploy (nothing done yet)
- Final APK + MSI from the final commit, release notes `docs/release-notes-beta50.md` (with Tested / Not tested), top of `CHANGELOG.md`,
  `gh release create v1.0.0-beta50 ... --latest`, then redeploy only `flacie-web` on frank with a rollback tag first, check `/healthz`, check the owner can sign in as admin.

## By-ear checks only the owner can do
Gapless transitions, Hi-Res playback and the Hi-Res badge on a 48 kHz file, EQ on/off, headphone volume behaviour, a skip burst on a real song.

## Hard rules (unchanged)
Never type or ask for passwords or tokens (Quick Connect is approved by the owner); never write to a real iPod; never delete NAS files;
on frank touch only `flacie-web`; no YouTube download source; features ship on Android, web and Windows together; say plainly what was tested and what was not.

## STATUS UPDATE (end of session, usage limit reached)
Merged into release/beta50 and pushed: fix/web-g1, fix/web-g2 (87 .NET, 107 Playwright pass on that branch), fix/android-g2 (99 Android unit tests pass).
NOT yet done: merged-tree full test run (Gradle + dotnet + Playwright on release/beta50 HEAD), emulator crash-loop result (agent was still running: see docs/test-run/G-emu.md if it finished),
Fold crash gate (Fold removed from test: crash fix UNVERIFIED on a real Fold; Compose BOM 2024.09.03 + Home early-return removal + Quick picks BoxWithConstraints removal are candidates only),
first-NAS-scan jank proof (needs fresh install + owner sign-in), version bump already done (65 / 1.0-beta50, MSI 0.50.0 / 51),
docs/release-notes-beta50.md + CHANGELOG, final APK/MSI build, gh release, frank redeploy. NOTHING is released or deployed.
Open from reports: G-win.md (desktop), G-rg.md (RG), G2-android.md, G2-web.md. Not fixed: BUG-5 background MEDIA_PLAY routing, one slow Jellyfin song after skip bursts, O5 device label, queue reorder (feature, not included).
By-ear checks for the owner: gapless, Hi-Res badge on a 48 kHz file, EQ on/off, headphone volume, skip burst on a real song.