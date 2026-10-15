# iPod Desk (flat lay) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A third iPod-mode Look, **Desk**: the real iPod emulator lying on a sunlit desk in a film-photo flat lay. Around it:
- a journal the synced lyrics get written into
- a Polaroid
- the next album's open CD with a printed disc and the queue stack
- coffee
- earbuds that are the visualiser

**Architecture:**
- `Ipod.razor` wraps its existing `.ipod-area` in `<DeskScene>` when `Look == Desk`.
- The scene is Razor components, `desk.css`, inline SVG, and `desk.js` (one rAF loop, dust canvas, disc prints, lyric writing).
- The iPod itself is untouched markup in a rotated slot that the existing `flacie.ipodFit` sizes.

**Tech Stack:** .NET 9 Blazor Server, plain CSS/SVG/vanilla JS, xunit, Playwright 1.49.

**Spec:** `docs/superpowers/specs/2026-10-14-ipod-desk-workspace-design.md` (v2). **Reference mock-up:** `alive5.html` (session scratchpad). Its CSS, SVG and JS are the visual source of truth; copy values from it rather than reinventing them.

## Global Constraints

- No WebGL and no new npm/NuGet packages. Assets go in `wwwroot/desk/`:
  - `oak.jpg` (Poly Haven `oak_veneer_05`, CC0, ≤ 400 KB)
  - `caveat.woff2` (OFL) and `OFL-caveat.txt`
- Keep every existing `data-testid` and the `data-look="device"|"wheel"` chips. The new chip is `data-look="desk"`. Desk test ids:
  - `desk`, `desk-journal`, `desk-lyric`, `desk-polaroid`
  - `desk-open-cd`, `desk-disc`, `desk-stack`, `desk-case`, `desk-note`
  - `desk-coffee`, `desk-earbuds`
- `IpodPrefs.LookDesk = 2`. `Cycle("look")` toggles only Device ↔ Wheel Only, and from Desk it goes to Device. `FromJson` accepts 0, 1 and 2.
- **Values from the spec:**
  - iPod slot rotation −7°
  - blinds shade .2, sun .32, slat sway 9 s, sun drift 80 s
  - grain .05
  - warm wash .3, cool soft-light .14
  - disc spin 10 s (paused when the player is paused)
  - landscape layout at aspect ≥ 4:3, portrait below it
- **Visualiser bands:**
  - low = mean of bins 1–8
  - mid = mean of bins 9–40
  - high = mean of bins 41–160 (of a 1024-bin frame)
  - beat when low rises > 0.12 above its 0.5 s moving average, with at most one beat every 280 ms
- **Disc art:** CAA image whose `Types` contains `"Medium"`, looked up only through `MbOverlay.Get(track)` (an existing user-chosen release), on demand after the scene has rendered, through `CoverArtArchiveClient.ListAsync` (gated and cached). Otherwise use the generated print.
- No lyric text is invented in production code. Without lyrics, only the title and artist show.
- `prefers-reduced-motion: reduce`: no CSS animations under `.desk`, no rings, no twitch, static arcs, and lyrics shown without the writing effect.
- Work only in `C:\wt\desk` on `ipod/desk`. No merge, tag, release or deploy. Test ports are `MOCK_PORT=8296` and `WEB_PORT=5390`. Run one heavy command at a time.

## Review Focus

1. **Every model fits its slot:** a mini, a nano 3G (wide and short), touch5 (tall) and ipod1 (buttons). For each, the device sits inside `.desk-ipod`, the cable starts at its jack, and nothing overlaps the journal or CDs. Tested in Task 7 (`every family sits on the desk`).
2. **Wheel drag in a rotated slot:** dragging the ring scrolls the menu the same number of rows as in the Device look, because the angle maths is relative. Tested in Task 7 (`wheel drag scrolls in desk look`).
3. **Queue edge cases:**
   - nothing playing: placeholder CD and journal text
   - one album: no stack
   - a queue with the same album repeated: it counts as one album
   Tested in Task 1 (`DeskSources`) and Task 7 (`empty desk`).
4. **Song changes mid-lyric:** the journal clears and starts the new song. The old song's late lyrics response is ignored (the token check). Tested in Task 5 (`stale lyrics are ignored`).
5. **Portrait phone:** 390×844 keeps the iPod usable (its slot ≥ 55% of the height) with everything inside the viewport. Tested in Task 7 (`portrait phone`).

---

## File map

| File | Responsibility |
|---|---|
| `FLACie.Core/DeskSources.cs` (rework) | Queue → the next distinct albums (`DeskAlbum`) |
| `FLACie.Core/DeskJacks.cs` (new) | Model id → `DeskJack(Edge Top/Bottom, X 0..1)` |
| `FLACie.Core/IpodPrefs.cs` (edit) | `LookDesk` |
| `wwwroot/flacie.js` (edit) | `vizFrame()` / `vizRelease()` |
| `wwwroot/desk.js` (new) | `window.desk`: `start(sceneEl, opts)`, `stop()`, `lyrics(lines, synced)`, `song(key)`, `playing(bool)`, `discPrint(imgUrl, artist, album) → dataUrl`, `loops()` |
| `wwwroot/desk.css` (new) | All desk styling (from the mock-up) |
| `wwwroot/desk/*` (new) | Oak texture, handwriting font and its licence |
| `Components/Shared/DeskScene.razor` (new) | Layout, light, film, the iPod slot (`ChildContent`), the earbuds SVG, the coffee |
| `Components/Shared/DeskJournal.razor` (new) | The journal page (title, artist, lyric lines) |
| `Components/Shared/DeskCds.razor` (new) | Open CD, printed disc, stack, note; `OnPlay` |
| `Components/Pages/Ipod.razor` (edit) | Desk chip, wrap, data wiring |
| `Components/App.razor` (edit) | Links |
| tests | `DeskSourcesTests` (rework), `DeskJacksTests`, `IpodPrefsTests` (edit), `1n-ipod-desk.spec.ts` (rewrite), `fixtures/desk-viz.html` (rewrite) |

Removed: `DeskEnvironment.razor`, `BackgroundVisualizerCanvas.razor`, `MediaShelf.razor`, `IpodShellTheme.razor`, and the v1 `desk.css` and `desk.js`.

---

### Task 0: Workspace, clean-out, assets

- [ ] **Step 1:** Delete the v1 components, `desk.css` and `desk.js`. Keep `DeskSources*`, the Playwright port override and the harness folder.
- [ ] **Step 2:** Download into a new empty scratch directory (untrusted data rule):
  - `https://dl.polyhaven.org/file/ph-assets/Textures/jpg/2k/oak_veneer_05/oak_veneer_05_diff_2k.jpg`
  - `https://cdn.jsdelivr.net/npm/@fontsource/caveat@5.1.0/files/caveat-latin-400-normal.woff2` and `…-700-normal.woff2`
  - the OFL licence: `https://cdn.jsdelivr.net/npm/@fontsource/caveat@5.1.0/LICENSE`

  Resize the oak to 1600 px with ffmpeg at JPEG quality 82, and check it's ≤ 400 KB. Copy into `wwwroot/desk/` as `oak.jpg`, `caveat-400.woff2`, `caveat-700.woff2` and `OFL-caveat.txt`. Add a line to `THIRD-PARTY-NOTICES.md`.
- [ ] **Step 3:** `dotnet build`. Expected: 0 errors (`Ipod.razor` on this branch never referenced the v1 components). Commit `"Desk v2: clear v1 scene, add oak texture and handwriting font (CC0 / OFL)"`.

### Task 1: `DeskSources` → next distinct albums

**Files:** `FLACie.Core/DeskSources.cs`, `tests/FLACie.Tests/DeskSourcesTests.cs`

**Interfaces:** Produces:
- `public sealed record DeskAlbum(string Key, string Album, string Artist, string? ArtKey, int QueueIndex, Track First)`
- `public static class DeskSources { public static List<DeskAlbum> Next(IReadOnlyList<Track> queue, IReadOnlyList<int> order, int index, int count = 3); public static string AlbumKey(Track t); }`

`order` is the play order (`PlayerState`'s shuffle order, exposed in Task 6). `AlbumKey` is `AlbumArtist|Album`, lower-cased; an empty album uses the track path.

- [ ] **Step 1: Write the failing tests** (rewrite the file):
  - `NextSkipsTheCurrentAlbumAndRepeats`: order through tracks of albums A, A, B, B, C, A, D, with the current index on the first A. The result keys are `[B, C, D]`, and the `QueueIndex` values are those albums' first positions after the current one.
  - `FollowsPlayOrderNotQueueOrder`: queue [A, B, C] with order [2, 0, 1] and current at order position 0 (C) gives `[A, B]`.
  - `ShortAndEmptyQueues`: an empty queue gives []. A single album gives []. `count` is respected.
  - `AlbumKeyIgnoresCaseAndFallsBackToPath`.
- [ ] **Step 2:** `dotnet test --filter DeskSourcesTests`. Expected: FAIL (build errors).
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the same command. Expected: PASS.
- [ ] **Step 5:** Commit `"Desk: DeskSources lists the next distinct albums in play order"`.

### Task 2: `DeskJacks`

**Files:** `FLACie.Core/DeskJacks.cs`, `tests/FLACie.Tests/DeskJacksTests.cs`

**Interfaces:**
- `public enum JackEdge { Top, Bottom }`
- `public sealed record DeskJack(JackEdge Edge, double X)`
- `public static class DeskJacks { public static DeskJack For(string modelId); }`

Table:

| Models | Edge | X |
|---|---|---|
| ipod1–4, video5, classic6/7 | Top | 0.2 |
| mini, mini2 | Top | 0.16 |
| nano1–5, nano7 | Bottom | 0.2 |
| nano6 | Bottom | 0.5 |
| touch1–7 | Bottom | 0.2 |
| unknown model | Top | 0.2 |

- [ ] **Step 1:** Tests:
  - `EveryModelHasAJack`: iterate `IpodModels.All`. `For(id)` is defined, and `X` is in (0, 1).
  - `TouchAndNanoJacksAreOnTheBottom`.
  - `ClassicJackIsTopLeft`.
- [ ] **Steps 2–5:** RED, implement, GREEN, then commit `"Desk: where each iPod's headphone jack is"`.

### Task 3: `IpodPrefs.LookDesk`

**Files:** `FLACie.Core/IpodPrefs.cs`, `tests/FLACie.Tests/IpodModelsTests.cs` (`IpodPrefsTests`)

- [ ] **Step 1:** Tests:
  - `DeskLookRoundTrips`
  - `TheIpodsOwnLookToggleSkipsDesk`: Desk then `Cycle` gives Device; Device gives WheelOnly; WheelOnly gives Device.
  - `UnknownLookFallsBackToDevice`: `{"look":7}`.
  - `LookNamesHasDesk`.
- [ ] **Steps 2–5:** RED, implement (`LookDesk = 2`, `LookNames` gets "Desk", `FromJson` accepts 2), GREEN, then commit `"Desk: IpodPrefs.LookDesk"`.

### Task 4: `flacie.vizFrame` and the `desk.js` engine

**Files:** `wwwroot/flacie.js` (edit), `wwwroot/desk.js` (new), `tests/web-smoke/fixtures/desk-viz.html` (rewrite), `tests/web-smoke/1n-ipod-desk.spec.ts` (harness test)

**Interfaces:**
- `flacie.vizFrame()` returns `{ freq: Uint8Array(1024), wave: Uint8Array, playing: bool } | null`. It builds the analyser on demand through the existing `buildGraph(0)`. `flacie.vizRelease()` closes the graph unless the Info tab's viz is running.
- `desk.start(scene, { reduced })` begins one rAF loop driving:
  - bands and beats, using the Global Constraints formula
  - CSS variables on the scene: `--low`, `--mid`, `--high`, `--beat`
  - the SVG elements `[data-arc="0..2"]` (per bud), `[data-pulse]` (cable current) and the ripple group `[data-ripples]`
  - the bud twitch
  - the dust canvas at 25 fps

  Without a frame it falls back to a calm fixed pulse with low = 0.2 and no beats. It pauses while `document.hidden`.
- `desk.stop()` cancels the loop and calls `flacie.vizRelease`.
- `desk.loops()` returns the number of live loops.
- `desk.discPrint(coverUrl, artist, album)` returns `Promise<string>` (a data URL), using the mock-up's crop, darkened rim, ring text and small print.
- `desk.writeLine(el, text, ms)` adds the writing animation (or no animation when reduced).

- [ ] **Step 1:** Rewrite the harness and add the test `desk engine: bands, beats, fallback, disc print, one loop`.
  - **Stub frame:** stub `flacie.vizFrame` with a frame whose low bins pulse at 2 Hz. After 2 s, `--beat` has been > 0.5 at least twice, `--low` is between 0 and 1, and the arc paths have a `d` attribute.
  - **Fallback:** with `?null=1`, `--low` ≈ 0.2 and no ripples are created.
  - **Disc print:** `discPrint` resolves to a `data:image/png` whose centre pixel matches the hub colour.
  - **Restart:** start, start, stop leaves `loops() == 0`.
- [ ] **Step 2:** Run `npx playwright test 1n-ipod-desk -g engine` (with the ports). Expected: FAIL.
- [ ] **Step 3:** Implement `vizFrame`/`vizRelease` in `flacie.js` next to `vizStart`/`vizStop` (only those lines), and write `desk.js`.
- [ ] **Step 4:** Run it again. Expected: PASS. Commit `"Desk: vizFrame and the desk engine (bands, beats, earbud arcs, dust, disc prints)"`.

### Task 5: The scene components and `desk.css`

**Files:** `DeskScene.razor`, `DeskJournal.razor`, `DeskCds.razor`, `wwwroot/desk.css`, `Components/App.razor`

**Interfaces:**
- `<DeskScene Jack="DeskJack" Portrait="bool" Reduced="bool">`, with `ChildContent` (the iPod area) and the slots `Journal`, `Cds` and `Polaroid`.
  - The root is `<div class="desk" data-testid="desk">`.
  - The iPod slot is `<div class="desk-ipod">` with the CSS rotation.
  - The earbuds SVG (`data-testid="desk-earbuds"`) sits under the slot. Its cable path is chosen by `Jack.Edge`, and its start is offset to `Jack.X`.
  - It calls `desk.start` and `desk.stop` from its lifecycle.
- `<DeskJournal Title Artist Lines="IReadOnlyList<string>" Current="int" Reduced="bool" />`
  - Lines up to `Current` are shown; the `Current` line gets the writing effect.
  - Each line has `data-testid="desk-lyric"`.
  - It shows at most 8 lines, scrolling older ones off.
- `<DeskCds Next="IReadOnlyList<DeskAlbum>" DiscArt="string?" Playing="bool" OnPlay="EventCallback<DeskAlbum>" />`
  - The open case shows `Next[0]`.
  - The stack shows `Next[1..2]`.
  - The note reads "Up next / Title · Artist", or "end of queue".
  - When `Next` is empty, it shows an empty case and a plain silver disc.

Steps:
- [ ] **Step 1:** Port the mock-up's markup and CSS into the components and `desk.css`. Units are `cqh` inside a 16:9 cover container for landscape. The portrait layout comes from a container query on aspect ratio (spec §3). Add the `@font-face` rules for Caveat. The desk background is `url(desk/oak.jpg)`.
- [ ] **Step 2:** Lyric timing.
  - The journal's `Current` comes from a JS callback. `desk.js` exposes `desk.follow(times[], dotnetRef)`, which calls `[JSInvokable] LyricLine(int)` when the audio clock crosses a time, reusing the binary search from `flacie.js`'s `lyricTick` on `audio.currentTime`.
  - Unsynced lyrics advance every `songLength / lines` seconds.
  - **Stale lyrics test** (Review Focus 4): add a bUnit-free unit test of the Ipod-side token logic as a pure helper `DeskLyrics.Accept(string requestedFor, string currentPath)` in Core, with a test `StaleLyricsAreIgnored`.
- [ ] **Step 3:** `dotnet build` and `dotnet test --filter DeskLyrics`. Expected: 0 errors and PASS. Commit `"Desk: scene, journal and CDs (look from the approved mock-up)"`.

### Task 6: Wiring into `/ipod`

**Files:** `Ipod.razor`, `PlayerState.cs` (expose `PlayOrder`: `public IReadOnlyList<int> PlayOrder => order;`), `Components/App.razor`

- [ ] **Step 1:** In `Ipod.razor`:
  - `desk => prefs.Look == IpodPrefs.LookDesk`. `flat` stays false in desk.
  - Wrap `.ipod-area` with `@if (desk) { <DeskScene …>@Area()</DeskScene> } else { @Area() }`, where `Area()` is one `RenderFragment` holding the existing markup unchanged.
  - Add the chip `data-look="desk"`.
  - Add `fitKey` (including the look).
  - Data:
    - `DeskSources.Next(Player.Queue, Player.PlayOrder, Player.Index)`
    - lyrics through `LyricsService.GetAsync(Player.Current, Session.Jellyfin)`, with the `DeskLyrics.Accept` token check on each song change
    - disc art: after first render for a new up-next album, run `MbOverlay.Get(first)`; if it has a `ReleaseMbid`, call `Caa.ListAsync(release, group)`, take the first image with `"Medium"` in `Types`, and use its `Thumb` URL (a remote https URL; it is never fetched on the server). Otherwise `null`, which means the generated print.
    - `OnPlay(a)` calls `Player.Play(Player.Queue, a.QueueIndex, "desk:" + a.Key)` and then `FocusStage()`.
  - `Polaroid` = `<Cover ArtKey=Player.Current?.ArtKey>`.
- [ ] **Step 2:** Run `dotnet build` and `dotnet test` (all). Expected: 0 errors, all pass.
- [ ] **Step 3:** Run the dev server on 5390 with the mock and check in the browser pane:
  - classic6, nano3, mini, touch5 and ipod1 in landscape
  - a portrait screenshot
  - play, skip and MENU work
  - lyrics are written in (if the mock serves lyrics; otherwise note the title-only state)
- [ ] **Step 4:** Commit `"Desk: wired into /ipod (real emulator, queue, lyrics, disc art, visualiser)"`.

### Task 7: Browser tests `1n-ipod-desk.spec.ts`

- [ ] **Step 1:** Write the tests listed in spec §5 and Review Focus 1, 2, 3 and 5:
  - `desk renders with the 1m test ids`
  - `every family sits on the desk` (ipod1, mini, video5, nano3, classic6, touch5)
  - `wheel drag scrolls in desk look`: count `ipod-row.sel` moves for a 120° drag in device versus desk; they're equal.
  - `keys and zones still drive the iPod`
  - `clicking the open CD plays the next album`
  - `journal shows the song title`
  - `empty desk` (fresh user, nothing played)
  - `fits at 1280x720, 1920x1080, 2560x1080`
  - `portrait phone 390x844`: the slot height is ≥ 55% of the viewport, and everything is inside it
  - `remembers the desk look`
  - `reduced motion stops the animations`: `getAnimations()` under `.desk` is empty
  - collect console errors in every test and expect none
- [ ] **Step 2:** Delete `.data-smoke`, `.pub-smoke` and `test-results`, then run `npx playwright test 1n-ipod-desk 1m-ipod` with the ports. Expected: all PASS. Commit `"Desk: browser tests"`.

### Task 8: Verification and hand-off

- [ ] **Step 1:** superpowers:verification-before-completion. Run the full `dotnet test`, then the full Playwright suite on 8296/5390, one at a time. Record the counts. Specs that hard-code 8196/5290 (1f, 1g, 1l) are run only if those ports are free; otherwise note them as not run.
- [ ] **Step 2:** Final whole-branch review (superpowers:requesting-code-review; reviewer on the most capable model). Do one fix pass for Critical and Important findings, each RED → GREEN.
- [ ] **Step 3:** `git push -u origin ipod/desk`. No merge. Report the branch, the test counts, screenshots, rulings and what's untested (the Windows WebView2 build, real CAA "Medium" images unless one is available, and the Android build, which isn't affected).
