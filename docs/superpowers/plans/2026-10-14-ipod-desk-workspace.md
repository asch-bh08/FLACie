# iPod Desk Workspace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a third iPod-mode Look, **Desk**. It puts the unchanged iPod on a desk scene: a monitor showing a live visualiser, studio speakers, a shelf of cassettes or CDs that play sources, and shell finishes.

**Architecture:** Four new Razor components and a pure `DeskSources` builder in Core. `desk.css` and `desk.js` hold the visuals. `Ipod.razor` wraps its existing `.ipod-area` in `<DeskEnvironment>` when `prefs.Look == IpodPrefs.LookDesk`, and every `data-testid` / `@ref` stays where it is. The monitor reads the existing analyser through a new `flacie.vizFrame()`.

**Tech Stack:** .NET 9 Blazor Server (Razor components), plain CSS, vanilla JS (Canvas 2D), xunit, Playwright 1.49 (`tests/web-smoke`).

**Spec:** `docs/superpowers/specs/2026-10-14-ipod-desk-workspace-design.md`

## Global Constraints

- No Tailwind, no new npm/NuGet dependencies; plain CSS in `wwwroot/desk.css`, JS in `wwwroot/desk.js`.
- No bitmaps committed for the scene: CSS gradients and inline SVG only.
- Keep every existing `data-testid` (`ipod`, `ipod-keys`, `ipod-device`, `ipod-panel`, `ipod-gear`, …) on the same element; keep the existing `data-look="device"` / `data-look="wheel"` chips unchanged. The new chip is `data-look="desk"`.
- `IpodPrefs.LookDesk = 2`. The iPod's own Settings › look toggle still cycles Device ↔ Wheel Only only.
- New prefs and their ranges: `Finish` 0 model / 1 acrylic / 2 anodised; `DeskViz` 0 bars / 1 wave / 2 particles; `DeskShelf` 0 cassette / 1 CD; `DeskSurface` 0 wood / 1 matte. JSON keys `finish`, `deskViz`, `deskShelf`, `deskSurface`.
- Shelf play context string: `"desk:<id>"`. Ids: `upnext`, `favorites`, `pl:<playlist id>`.
- Breakpoints: full scene ≥ 1100 px; speakers at 70% between 860 and 1099 px; speakers and monitor stand hidden below 860 px; shelf at 80% when height < 640 px.
- Finish transition `.4s`; shelf hover `translateY(-8px)`, `180ms ease-out`; visualiser at most 60 fps, paused while `document.hidden`.
- `prefers-reduced-motion: reduce` turns off the woofer pulse, the reel spin and the hover lift (glow stays).
- Never commit to `main`, merge, bump versions, tag, release or deploy. Work only in worktree `C:\wt\desk` on branch `ipod/desk`.
- One heavy command at a time (16 GB PC): never run `dotnet test` and Playwright together.

## Review Focus

1. **Many or long sources**: 40 playlists with 80-character names. The shelf scrolls sideways inside its band, labels ellipsize, the page never scrolls horizontally and the iPod is not pushed off-screen. Tested in Task 7 (`many playlists stay inside the shelf`).
2. **Leaving Desk**: going Desk → Device → Desk repeatedly stops the visualiser loop and releases the analyser (no growing rAF count, no second AudioContext), and the iPod is re-fitted each time. Tested in Task 7 (`switching looks keeps one loop and refits`).
3. **Keyboard after a shelf click**: after clicking a tape, the arrow keys and Enter still drive the iPod, so focus returns to `ipod-keys`. Tested in Task 7 (`keys still drive the iPod after a shelf pick`).
4. **Touch models on the desk**: an iPod touch (no wheel, home button) fits the iPod slot and its screen still works. Tested in Task 7 (`touch models sit on the desk`).
5. **No analyser available**: `vizFrame()` returns null (no AudioContext, or nothing playing). The monitor shows the idle state and logs nothing to the console. Tested in Task 2 (`desk-viz.html` harness) and Task 7 (console-error check).

---

## File map

| File | Responsibility |
|---|---|
| `ipodsync/src/FLACie.Core/DeskSources.cs` (new) | Pure: queue/favourites/playlists → `List<DeskSource>` |
| `ipodsync/tests/FLACie.Tests/DeskSourcesTests.cs` (new) | Unit tests for the above |
| `ipodsync/src/FLACie.Server/wwwroot/desk.js` (new) | `window.desk`: visualiser loop (bars/wave/particles/idle), `--bass` variable |
| `ipodsync/src/FLACie.Server/Components/Shared/BackgroundVisualizerCanvas.razor` (new) | Monitor bezel + canvas, starts/stops `desk.js` |
| `ipodsync/src/FLACie.Server/Components/Shared/DeskEnvironment.razor` (new) | Scene layout: wall, desk surface, monitor, speakers, iPod slot, shelf slot |
| `ipodsync/src/FLACie.Server/Components/Shared/MediaShelf.razor` (new) | Cassettes / CD cases for `DeskSource`s |
| `ipodsync/src/FLACie.Server/Components/Shared/IpodShellTheme.razor` (new) | Finish chips for the panel |
| `ipodsync/src/FLACie.Server/wwwroot/desk.css` (new) | All desk styling |
| `ipodsync/src/FLACie.Core/IpodPrefs.cs` (edit) | `LookDesk`, four new prefs |
| `ipodsync/tests/FLACie.Tests/IpodModelsTests.cs` (edit, `IpodPrefsTests`) | Prefs tests |
| `ipodsync/src/FLACie.Server/wwwroot/flacie.js` (edit) | `vizFrame()`, `vizRelease()` |
| `ipodsync/src/FLACie.Server/Components/App.razor` (edit) | Link `desk.css`, `desk.js` |
| `ipodsync/src/FLACie.Server/Components/Pages/Ipod.razor` (edit) | Wrap `.ipod-area`, Desk chip, desk panel section |
| `ipodsync/tests/web-smoke/1n-ipod-desk.spec.ts` (new) | Browser tests |

Tasks 1 to 5 only create new files and can be built on `d08b192`. Task 6 edits files the overnight session owns. It starts only after that session has sent its commit hash, and then begins by rebasing `ipod/desk` onto that commit.

---

### Task 0: Worktree and docs

- [ ] **Step 1:** Use superpowers:using-git-worktrees. `git worktree add C:/wt/desk -b ipod/desk d08b192` (from `C:\FLACie`).
- [ ] **Step 2:** Copy the spec and this plan into `C:\wt\desk\docs\superpowers\{specs,plans}\` (same file names). Remove the uncommitted copies from `C:\FLACie\docs\superpowers\` only after checking they are byte-identical.
- [ ] **Step 3:** Commit: `git add docs/superpowers && git commit -m "Docs: iPod desk workspace spec and plan"`.

### Task 1: `DeskSources` (Core)

**Files:** Create `ipodsync/src/FLACie.Core/DeskSources.cs`, `ipodsync/tests/FLACie.Tests/DeskSourcesTests.cs`

**Interfaces:**
- Consumes: `Track` and `Group` (`FLACie.Core/Track.cs`), `Playlist` (`Id`, `Name`, `Entries`), `PlaylistEntry`.
- Produces:
  - `public sealed record DeskSource(string Id, string Label, string Sub, string? ArtKey, IReadOnlyList<Track> Tracks)`
  - `public static class DeskSources { public const string ContextPrefix = "desk:"; public static List<DeskSource> Build(IReadOnlyList<Track> queue, int index, IReadOnlyList<PlaylistEntry> favorites, IReadOnlyList<Playlist> playlists, Func<IEnumerable<PlaylistEntry>, IReadOnlyList<Track>> resolve); public static string? ActiveId(string? context, bool playing); }`

- [ ] **Step 1: Write failing tests** in `DeskSourcesTests`, using the `T(...)` track helper style of `IpodContentTests`:
  - `UpNextOnlyWhilePlayingAndStartsAtTheCurrentSong`: queue [a,b,c], index 1 → first item `Id=="upnext"`, `Label=="Up Next"`, `Sub=="2 songs"`, `Tracks==[b,c]`, `ArtKey==b.ArtKey`. With an empty queue or `index == -1` there is no `upnext`.
  - `OneSongSaysSong`: a single remaining track gives `Sub == "1 song"`.
  - `FavouritesSkippedWhenEmpty`: favourites [] means no `favorites` item. With two resolvable entries: `Id=="favorites"`, `Label=="Favourites"`, `Sub=="2 songs"`.
  - `PlaylistsInOrderSkippingEmptyOnes`: playlists P1 (2 tracks), P2 (resolves to 0), P3 (1) → ids `["pl:P1","pl:P3"]`, labels the playlist names.
  - `ArtIsTheFirstTrackThatHasArt`: tracks [noArt, "k2", "k3"] → `ArtKey == "k2"`; all without art → `null`.
  - `OrderIsUpNextFavouritesThenPlaylists`.
  - `ActiveIdReadsTheDeskContext`: `ActiveId("desk:pl:P1", true) == "pl:P1"`, `ActiveId("album:x", true) == "upnext"`, `ActiveId(null, false) == null`.
- [ ] **Step 2:** `cd ipodsync\tests\FLACie.Tests; dotnet test --filter DeskSourcesTests`. Expected: build fails (`DeskSources` not defined).
- [ ] **Step 3:** Implement `DeskSources.cs` in namespace `FLACie.Core`, with a one-line `<summary>` in the house style.
- [ ] **Step 4:** Run the same command. Expected: all `DeskSourcesTests` PASS.
- [ ] **Step 5:** Commit `"Desk: DeskSources builds the shelf's sources"`.

### Task 2: Visualiser engine (`desk.js`) and `BackgroundVisualizerCanvas`

**Files:** Create `wwwroot/desk.js`, `Components/Shared/BackgroundVisualizerCanvas.razor`, and the test harness `ipodsync/tests/web-smoke/fixtures/desk-viz.html`

**Interfaces:**
- Consumes: `window.flacie.vizFrame()` (added in Task 6). Until then `desk.js` must treat a missing function as "no frame".
- Produces:
  - JS: `window.desk.vizStart(canvas: HTMLCanvasElement, style: "bars"|"wave"|"particles", scene: HTMLElement|null, title: string)`, `window.desk.vizStyle(style)`, `window.desk.vizTitle(title)`, `window.desk.vizStop()`, `window.desk.loops(): number` (count of live rAF loops, for tests).
  - Razor: `<BackgroundVisualizerCanvas Style="bars" Title="@string" Scene="@ElementReference?" />`. The root is `<div class="desk-monitor" data-testid="desk-monitor">` containing `<canvas data-testid="desk-viz">`.
- Frame contract: `vizFrame()` returns `{ freq: Uint8Array, wave: Uint8Array, playing: boolean } | null`.

- [ ] **Step 1:** Write `fixtures/desk-viz.html`. It loads `desk.js` and stubs `window.flacie.vizFrame` with a sine-driven frame, switchable to `null` with `?null=1`. Add a test to the Task 7 spec file for now: `desk-viz harness draws and idles without errors`. It opens the harness via `file://`, checks the canvas has non-blank pixels after 300 ms in each style, and that with `?null=1` it still draws (idle) with no console errors.
- [ ] **Step 2:** Run `npx playwright test 1n-ipod-desk -g harness`. Expected: FAIL (`desk.js` missing).
- [ ] **Step 3:** Implement `desk.js`, following the spec's §5 visualiser rules:
  - 48 log-spaced bars with falling peak caps;
  - a smoothed glowing waveform;
  - 160 particles driven by band energy;
  - an idle breathing gradient with the title (`"FLACie"` when empty).
  The canvas backing size is CSS size × `devicePixelRatio`, set from a `ResizeObserver`. Frames are skipped while `document.hidden`. `scene.style.setProperty("--bass", value)`, where value is the mean of bins 1 to 8 / 255 (two decimals), and 0 when idle. Only one loop is allowed: `vizStart` stops any previous loop. `vizStop` cancels the loop, disconnects the observer and calls `flacie.vizRelease?.()`.
- [ ] **Step 4:** Implement the Razor component:
  - `OnAfterRenderAsync(first)` calls `desk.vizStart`;
  - `OnParametersSet` sends `desk.vizStyle` and `desk.vizTitle` when the values change, after the first render;
  - `IAsyncDisposable` calls `desk.vizStop`, swallowing `JSDisconnectedException`.
- [ ] **Step 5:** Run the harness test. Expected: PASS. Run `dotnet build ipodsync\src\FLACie.Server`. Expected: 0 errors.
- [ ] **Step 6:** Commit `"Desk: background visualiser (bars, wave, particles, idle)"`.

### Task 3: `DeskEnvironment` and the scene CSS

**Files:** Create `Components/Shared/DeskEnvironment.razor` and `wwwroot/desk.css` (the scene part)

**Interfaces:**
- Consumes: `BackgroundVisualizerCanvas` (Task 2).
- Produces: `<DeskEnvironment Surface="0|1" VizStyle="bars|wave|particles" Title="@string" Dark="@bool" Shelf="@RenderFragment?">@ChildContent</DeskEnvironment>`.
  - The root is `<div class="desk desk-wood|desk-matte @(Dark ? "desk-dark" : "")" data-testid="desk" @ref=scene>`.
  - Grid areas: `wall`, `monitor`, `spk-l`, `ipod`, `spk-r`, `shelf`.
  - Speakers are `<div class="desk-spk" data-testid="desk-speaker">`, each with `.woofer`, `.tweeter` and `.port`, all in CSS.
  - The `ChildContent` goes in `<div class="desk-ipod">`, which also holds a `.desk-contact` shadow.
  - The root `@ref` is passed to the canvas as `Scene` so `--bass` lands on `.desk`.

- [ ] **Step 1:** Implement the component and the scene CSS following spec §4:
  - wood: oak `#b07a45` to `#8a5a2e` grain, with a front-edge highlight;
  - matte: `#2b2d31` felt with a stitched edge;
  - a walnut speaker cabinet, with the woofer scaled by `1 + var(--bass,0)*.03`;
  - the monitor behind the iPod and above it, with `filter: blur(.4px)` and opacity `.92`.

  `.desk` is `position:absolute; inset:0` inside `.ipod-stage`. `.desk-ipod` is the grid cell that the existing `.ipod-area` fills (`.desk .ipod-area { width:100%; height:100% }`), so the existing fit code sizes the iPod to the cell. Add the breakpoint rules from Global Constraints and the reduced-motion rules.
- [ ] **Step 2:** `dotnet build ipodsync\src\FLACie.Server`. Expected: 0 errors. The visuals are checked in Task 6 once the component is mounted.
- [ ] **Step 3:** Commit `"Desk: DeskEnvironment scene (wall, desk, monitor, speakers)"`.

### Task 4: `MediaShelf`

**Files:** Create `Components/Shared/MediaShelf.razor`; add the shelf part of `desk.css`

**Interfaces:**
- Consumes: `DeskSource` (Task 1), `<Cover ArtKey Class>` (existing).
- Produces: `<MediaShelf Sources="IReadOnlyList<DeskSource>" ActiveId="string?" Style="0|1" Playing="bool" OnPick="EventCallback<DeskSource>" />`.
  - The root is `<div class="desk-shelf" data-testid="desk-shelf">`.
  - Each item is `<button type="button" class="desk-media tape|cd @(active ? "on" : "")" data-testid="desk-media" data-source="@s.Id" aria-label="Play @s.Label" title="@s.Label · @s.Sub" style="--hue:@(index*47%360)">`.
  - An empty source list shows `<p class="desk-shelf-empty">` with the text: "Your playlists will show up here."

- [ ] **Step 1:** Implement the markup.
  - A **tape** has a `.shell`, a `.jcard` (art + `<b>` label + `<small>` sub) and a `.window` with two `.reel` elements. The reels get `.spin` when the item is active and `Playing` is true.
  - A **CD** has a `.case` (with a hinge edge), an art layer, a `.glass` sheen and a `.disc` edge.
  - Hover and focus-visible use the lift and glow from Global Constraints. Labels use `text-overflow: ellipsis`.
  - The shelf row uses `overflow-x: auto; scroll-snap-type: x mandatory` with fade masks at both ends.
- [ ] **Step 2:** `dotnet build`. Expected: 0 errors.
- [ ] **Step 3:** Commit `"Desk: media shelf (cassettes and CD cases)"`.

### Task 5: `IpodShellTheme` and finishes

**Files:** Create `Components/Shared/IpodShellTheme.razor`; add the finish part of `desk.css`

**Interfaces:**
- Produces:
  - `<IpodShellTheme Finish="int" FinishChanged="EventCallback<int>" />` renders in the `.dock-seg` style with buttons `class="ipod-chip @(on?"on":"")" data-finish="model|acrylic|anodised"`.
  - CSS classes on `.ipod`: `finish-acrylic` and `finish-anodised`. `finish-model` changes nothing.

- [ ] **Step 1:** Implement the component.
- [ ] **Step 2:** Implement the finish CSS per spec §4.
  - Acrylic: translucent `rgba(220,235,245,.35)` gradients, a `backdrop-filter: blur(2px)`, and a board/battery pattern in a `::before` behind the bezel and wheel (`z-index: 0`).
  - Anodised: brushed micro-lines over `var(--top)` → `var(--bottom)`, plus a specular sweep.
  - All finishes: `transition: background .4s, box-shadow .4s, filter .4s`.
  - Scope every rule under `.desk` so other looks never change.
- [ ] **Step 3:** `dotnet build`. Expected: 0 errors. Commit `"Desk: shell finishes (acrylic, anodised)"`.

### Task 6: Integration (starts only after the overnight session's commit hash)

**Files:** Modify `FLACie.Core/IpodPrefs.cs`, `tests/FLACie.Tests/IpodModelsTests.cs` (`IpodPrefsTests`), `wwwroot/flacie.js`, `Components/App.razor`, `Components/Pages/Ipod.razor`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - `IpodPrefs.LookDesk`, with `LookNames` = `["Wheel Only", "Device", "Desk"]`.
  - Properties `Finish`, `DeskViz`, `DeskShelf`, `DeskSurface`, each clamped.
  - `flacie.vizFrame()` and `flacie.vizRelease()`.

- [ ] **Step 1:** `git -C C:/wt/desk rebase <hash>`. The task files touch no shared file yet, so a conflict here means stop and inspect.
- [ ] **Step 2: Write failing prefs tests** in `IpodPrefsTests`:
  - `DeskLookRoundTrips`: Look = `LookDesk` and Finish 2 → `ToJson` → `FromJson` gives the same values.
  - `DeskPrefsAreClamped`: `Finish = 9` → 2; `DeskViz = -1` → 0; JSON `{"look":7}` → `LookDevice`.
  - `TheIpodsOwnLookToggleSkipsDesk`: Look = `LookDesk`, then `Cycle("look")` → `LookDevice`. From Device it goes to WheelOnly, and from WheelOnly to Device.
  - `ResetClearsDeskPrefs`.
- [ ] **Step 3:** `dotnet test --filter IpodPrefsTests`. Expected: FAIL.
- [ ] **Step 4:** Implement the prefs. `FromJson` accepts look 0, 1 and 2, and anything else becomes Device. `Cycle("look")` → `Look == LookWheelOnly ? LookDevice : LookWheelOnly`.
- [ ] **Step 5:** Run the same command. Expected: PASS.
- [ ] **Step 6:** Add `vizFrame()` and `vizRelease()` to `flacie.js` next to `vizStart`/`vizStop`, touching only those lines.
  - `vizFrame` calls `buildGraph(0)` once (`false` → `null`), then `syncTwin()`, and returns `{freq, wave, playing: !audio.paused}`, filling `wave` with `an.getByteTimeDomainData`.
  - `vizRelease` calls `closeGraph()` unless the Info tab's `vizOn` is true.
- [ ] **Step 7:** Link `desk.css` after `ipod.css` and `desk.js` after `flacie.js` in `App.razor`.
- [ ] **Step 8:** Edit `Ipod.razor`:
  - `flat` stays false when `Look == LookDesk`.
  - Wrap the existing `.ipod-area` element in `@if (desk) { <DeskEnvironment …>[area]</DeskEnvironment> } else { [area] }`, using one `RenderFragment Area()` so the markup isn't duplicated.
  - Add a `.finish-*` class on `.ipod` when in desk mode.
  - Add the chip `<button … data-look="desk" @onclick="() => PickLook(IpodPrefs.LookDesk)">Desk</button>` after the existing two.
  - When `desk`, add panel sections "Finish" (`IpodShellTheme`), "Visualiser" (Bars/Wave/Particles) and "Shelf" (Cassettes/CDs, plus Wood/Mat). The buttons carry `data-desk-viz` / `data-desk-shelf` / `data-desk-surface` and save prefs the way `PickLook` does.
  - Shelf `OnPick`: `await Player.Play(src.Tracks, 0, DeskSources.ContextPrefix + src.Id)` (except for `upnext`), then `await FocusStage()`.
  - `fitKey` includes the look so the iPod refits on a look change.
- [ ] **Step 9:** Run `dotnet build` and `dotnet test`. Expected: 0 errors, all tests pass (the overnight suite included).
- [ ] **Step 10:** Browser check with the built-in browser against the local dev server (`dotnet run` with the web-smoke mock, as `docs/NEXT-SESSION-START-HERE.md` describes):
  - pick Desk and play a song;
  - take screenshots at 1920×1080, 1280×720 and 2560×1080 with Wood and Mat, all three finishes, all three visualiser styles, and both shelf styles;
  - fix whatever looks wrong.
- [ ] **Step 11:** Commit `"Desk: third iPod look wired into /ipod"`.

### Task 7: Browser tests `1n-ipod-desk.spec.ts`

**Files:** Create or extend `ipodsync/tests/web-smoke/1n-ipod-desk.spec.ts`. Reuse `signIn` / `openIpod`, copied from `1m-ipod.spec.ts` (keep the prefs-seeding approach with `look: 2`).

- [ ] **Step 1: Write the tests.**
  - Seed playlists with `POST /api/import?name=<n>.m3u`, using header `X-Emby-Token: mock-token` and `text/plain` body lines `Artist - Title` for two songs the mock serves (read them from the iPod Songs list or from `1c-api.spec.ts`'s known titles).
  - `desk look shows the scene and keeps the iPod's test ids`: `desk`, `desk-monitor`, `desk-viz`, 2× `desk-speaker`, `desk-shelf` visible; `ipod-keys`, `ipod-device`, `ipod-panel`, `ipod-gear` still visible; the `[data-look=desk]` chip has `.on`.
  - `a shelf pick plays that source`: click `[data-source^="pl:"]`; `ipod-np-title` becomes visible within 20 s; that item gets `.on`.
  - `keys still drive the iPod after a shelf pick` (Review Focus 3): after the pick, `Escape` leaves Now Playing (`ipod-np` count 0) and `ArrowDown` moves `ipod-row.sel`.
  - `finish changes the shell`: click `[data-finish=acrylic]` → `ipod-device` has class `finish-acrylic`; then `anodised`.
  - `fits at desktop sizes`: for 1280×720, 1920×1080 and 2560×1080, `desk-viz`'s box is inside `desk-monitor`'s box, `ipod-device`'s box is inside the viewport, and `document.documentElement.scrollWidth <= innerWidth`.
  - `narrow window hides the speakers`: at 800×700 the `desk-speaker` elements are hidden and there is no horizontal scroll.
  - `many playlists stay inside the shelf` (Review Focus 1): import 40 playlists with 80-character names; the same overflow assertions hold, and `desk-shelf`'s `scrollWidth > clientWidth`.
  - `switching looks keeps one loop and refits` (Review Focus 2): desk → device → desk three times, then `await page.evaluate(() => desk.loops())` is `1`; `ipod-device` is inside the viewport.
  - `touch models sit on the desk` (Review Focus 4): seed `model: 'touch5', look: 2`; `ipod-touch-screen` is visible and inside `.desk-ipod`.
  - `remembers the desk look`: reload and the `desk` testid is still visible.
  - Every test collects `page.on('console')` errors and expects none (Review Focus 5).
- [ ] **Step 2:** Delete `.data-smoke`, `.pub-smoke` and `test-results`, then run `npx playwright test 1n-ipod-desk 1m-ipod`. Expected: all PASS. Fix the code, not the tests, unless a test is wrong about the spec.
- [ ] **Step 3:** Commit `"Desk: browser tests"`.

### Task 8: Verification and hand-off

- [ ] **Step 1:** Use superpowers:verification-before-completion. Run `dotnet test` (full), then the full Playwright suite (the guide's command), one at a time, and record the pass counts.
- [ ] **Step 2:** Use superpowers:requesting-code-review for a whole-branch review of `ipod/desk` against the spec; fix the confirmed findings.
- [ ] **Step 3:** `git push -u origin ipod/desk`. Do not merge, tag or deploy. Use superpowers:finishing-a-development-branch and report the branch, test results, screenshots and what is untested (Windows WebView2 build, Android not applicable) to Ashley and the overnight session.
