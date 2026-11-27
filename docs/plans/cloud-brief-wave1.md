# Brief for the cloud session: ONE APP overhaul, wave 1 (27 Nov)

You are the second Claude on this repo (see docs/waves/TEAM.md). The local Claude has done wave 0 (kits, words, parity tool) and part 1g
(Now Playing), released b145 and deployed it. You do the screens. Read in this order, nothing else needed up front:
1. docs/plans/one-app-overhaul.md: the plan. "The one design" table + the notes above it (big by default, one rounded-rectangle shape,
   car-mode feel, 3-5 main actions big and the rest one tap away) are the rules. Wave 1 lists the parts.
2. docs/plans/one-app-words.md: the words. Tests fail on banned spellings (WordsTests.cs, WordsTest.kt).
3. The bottom of docs/HANDOFF-2026-11-25.md: what happened last and what is open.
4. The kits you build with: android/app/src/main/java/com/ipodemu/player/Kit.kt and ipodsync/src/FLACie.Server/wwwroot/kit.css
   (+ FLACie.App/Components/Shared: Slot, Tag, FormatTag, EmptyState, SongMenu).

## Your parts (in this order; one branch each from origin/ui/living-sleeve, named cloud/1x-<name>)
1. 1a Home + Library + Favourites   2. 1b Search   3. 1c Album, Artist, Genre, Made   4. 1d Settings + Account + Downloads/Offline
5. 1e Devices + Jam + Car   6. 1f Menus, sheets, confirms (incl. a redesigned More; phone-width web menus as bottom sheets)
7. 1h every screen size (fix what the parity page flags)   8. 1i admin-only for admins   9. 1j own-setup vs seamless normal users
10. Tab bar: Home, Search, Library, Devices, Settings on the phone and on phone-width web.
Owner rule for every page except Now Playing, Home and Devices: it is a REDESIGN. Put the web and phone versions side by side, take the
better ideas of each, make them look the same, then fit each to its screen (phone: thumb reach, sheets, big main actions; PC: width).

## Added 27 Nov (owner): 1g-2 Now Playing rethink is now yours too, LAST (after part 10): see one-app-overhaul.md part 1g-2.
The do-not-touch list below stops applying to Now Playing for 1g-2 only (the local Claude is not working on it).

## Do not touch (the local Claude is on them now)
PlayerBar.razor, NowPlaying.razor, living-sleeve.css, Playlist.razor, PlaylistEdit.razor, LivingSleeve.kt, PlaylistHeader /
EditPlaylistScreen in PlayerUi.kt (branch polish-1127: dock evened out, no shuffle dot, pencil Edit, player bar revamp).

## Rules (owner's, standing)
- Mocks only: never the real Jellyfin, never sign in with a password, never touch the owner's phone (emulator-5554 only), frank read-only.
- Comments say why, short; plain English labels; no new dependencies; TDD where there is logic.
- Checks per part: `dotnet build` + `dotnet test ipodsync/tests/FLACie.Tests`; `cd android && ./gradlew testDebugUnitTest assembleDebug`;
  the Playwright specs for the pages you touched (ipodsync/tests/web-smoke); the parity tool for the web
  (`node ipodsync/tools/parity/run.mjs --web --screens <yours>`) — no emulator in the cloud, so say the phone side is unit-tested only.
- Commit with "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"; push your cloud/... branch; never push to ui/living-sleeve or
  main, never release or deploy (the local Claude merges, tests on the emulator, releases).
- After each part: one line in docs/waves/team-log.md on your branch (part, branch, commit, tests, what's left), and push.
- Memory is limited: finish and push one part before starting the next; if you run low, push what you have with a clear note.
