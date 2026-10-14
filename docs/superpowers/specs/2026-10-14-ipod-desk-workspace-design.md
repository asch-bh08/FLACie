# iPod mode: Desk workspace (design)

Date: 2026-10-14 · Status: draft for review · Branch (when built): `ipod/desk`, from the overnight run's next commit on `main`

## 1. Goal

Add a third **Look** to iPod mode (`/ipod`): **Desk**. The existing iPod is unchanged. It sits on a desk scene:
a monitor behind it showing a live visualiser, studio speakers on either side, and a shelf of cassettes or CDs
in front of it. Each tape or CD is a playable source. A shell-finish selector re-skins the iPod.
The reference pictures are a white iPod classic on a wooden desk with a mug, earbuds and a plant, and an iPod
between two wooden studio monitors on a desk mat.

Success means:
- **Opens Desk:** Panel › Look › **Desk** shows the scene, and the choice is remembered like the other looks.
- **Same iPod:** the iPod is the same component and still works fully (wheel, keys, touch models, 23 models,
  colours).
- **Live monitor:** it pulses with the playing music, and shows a calm idle state when nothing plays.
- **Shelf plays:** clicking a tape or CD starts that source. Items lift and glow on hover.
- **Shell finish:** switching to classic, clear acrylic or anodised re-skins the iPod with a smooth transition.
- **Scales cleanly:** 1280×720 to 2560×1440 and 16:10 / 21:9 with no clipping or misaligned canvas. Narrow
  windows degrade gracefully (props hide; no horizontal page scroll).
- **Tests stay green:** existing tests and `1m-ipod.spec.ts` pass unchanged.

## 2. What the user asked for vs. what the codebase is

| Brief | FLACie reality | Decision |
|---|---|---|
| CSS/Tailwind | Blazor Server, Razor components, plain CSS files (`ipod.css`, `app.css`) | Plain CSS in a new `desk.css`; no Tailwind |
| `selectSource()` / `playTrack()` | `PlayerState.Play(list, index, context)`, `ShuffleAll(list)` | Shelf calls `Player.Play` |
| "existing audio analyser stream" | `flacie.js` builds a silent twin → `AnalyserNode` (`buildGraph`), used by the Info tab via `flacie.viz(...)` | Add `flacie.vizFrame()` that reuses that graph; no second AudioContext |
| "sources / playlists / Up Next" | `Player.Queue`/`Index`, `Session.Playlists` + `Session.Resolve`, `Session.Favorites` | Shelf items = Up Next, Favourites, each playlist |
| iPod skins | `IpodModels` (23 models) + per-model `IpodColor` | Finish is a desk-only overlay on the chosen model/colour; `IpodModels` untouched |

## 3. Architecture

```
Ipod.razor (existing page, owns all iPod state)
 └─ .ipod-stage
     ├─ [Look == Desk]  <DeskEnvironment Finish=… Viz=…>
     │                     ├─ BackgroundVisualizerCanvas   (monitor, canvas + desk.js loop)
     │                     ├─ speakers L/R                 (pure CSS/SVG, in DeskEnvironment)
     │                     ├─ ChildContent  ← the unchanged .ipod-area (keys, fit, device)
     │                     └─ MediaShelf                   (tapes/CDs → Player.Play)
     │   [otherwise]       .ipod-area as today
     └─ .ipod-dock panel: Look chips (+ "Desk"), and when Desk: IpodShellTheme + shelf/visualiser options
```

The desk components receive data and callbacks. They never own player state.

### 3.1 New files

| File | Purpose |
|---|---|
| `Components/Shared/DeskEnvironment.razor` | The scene. Lays out the wall, monitor slot, speaker slots, the iPod slot (`ChildContent`) and the shelf slot. Parameters: `ChildContent`, `Finish`, `Surface` (wood/matte), `VizStyle`, `ShelfStyle`. CSS grid with named areas. |
| `Components/Shared/BackgroundVisualizerCanvas.razor` | `<canvas>` inside a monitor bezel. On first render, calls `desk.vizStart(canvas, style)`; on dispose, calls `desk.vizStop()`. Parameter `Style` = `bars` / `wave` / `particles`; `Playing` (bool) for the idle state. |
| `Components/Shared/MediaShelf.razor` | Row of `MediaItem`s (record `DeskSource(string Id, string Label, string Sub, string? ArtKey, IReadOnlyList<Track> Tracks)`), rendered as a cassette (shell + J-card + window with reels) or a CD jewel case. Art via existing `<Cover>`. `OnPick` callback. Keyboard: each item is a `<button>`. |
| `Components/Shared/IpodShellTheme.razor` | Panel control: three finish chips (Model colour, Clear acrylic, Anodised). Emits `FinishChanged`. |
| `wwwroot/desk.css` | All desk styling. Linked in `App.razor` next to `ipod.css`. |
| `wwwroot/desk.js` | `window.desk = { vizStart, vizStop }`: rAF loop, DPR-aware sizing via `ResizeObserver`, pauses on `document.hidden`, reads `flacie.vizFrame()`. |
| `FLACie.Core/DeskSources.cs` | Pure function `Build(PlayerState-like queue, favourites, playlists, resolve)` → `List<DeskSource>`; unit-testable. |
| `tests/FLACie.Tests/DeskSourcesTests.cs` | Unit tests for source building. |
| `tests/web-smoke/1n-ipod-desk.spec.ts` | Playwright spec (section 6). |

### 3.2 Edits to existing files (kept small; the overnight run owns these files)

| File | Change |
|---|---|
| `Ipod.razor` | (1) If `prefs.Look == LookDesk`, wrap the existing `.ipod-area` in `<DeskEnvironment>`. Every `data-testid` and `@ref` stays on the same element. (2) Add a third chip `data-look="desk"` to the Look section; existing chips unchanged. (3) When Desk is on, the panel shows `IpodShellTheme` plus two small segmented controls (Visualiser: bars/wave/particles; Shelf: cassettes/CDs). (4) `flat` stays false in Desk (the device look is used). |
| `FLACie.Core/IpodPrefs.cs` | `LookDesk = 2`; `LookNames` gets "Desk"; JSON keeps `look` (parse accepts 2); new prefs `Finish` (0 model, 1 acrylic, 2 anodised), `DeskViz` (0 bars, 1 wave, 2 particles), `DeskShelf` (0 cassette, 1 CD), `DeskSurface` (0 wood, 1 matte). The iPod's own Settings › "look" toggle cycles only Device ↔ Wheel Only, as today. |
| `wwwroot/flacie.js` | `vizFrame()` next to `vizStart`/`vizStop`: makes sure the graph exists (`buildGraph(0)`), syncs the twin, returns `{ freq: Uint8Array, wave: Uint8Array, playing: bool }` or `null` when unavailable (iOS / no AudioContext). Does not start the Info tab's draw loop. |
| `Components/App.razor` | `<link rel="stylesheet" href="@Assets["desk.css"]" />` |

### 3.3 Fit and alignment

`flacie.ipodInit` fits `.ipod-fit` to the `stage` element passed in, which is `.ipod-area`. In Desk mode, `.ipod-area` is the
centre grid cell, so the existing fit logic sizes the iPod to that cell and needs no JS change.
The cell's size is set in CSS: about 46% of the scene height, centred, and never wider than the gap between the speakers.

The monitor canvas is sized by its own `ResizeObserver` (CSS size × `devicePixelRatio`), so it stays sharp and aligned at any zoom.

## 4. Visual design

All visuals are CSS gradients and inline SVG. No photos or bitmaps are committed.

- **Wall / background:** warm off-white to soft grey gradient, a faint window-light diagonal.
- **Desk surface:** lower ~40% of the scene, slight perspective (`transform: perspective() rotateX(…)` on a pseudo-element). Wood is layered
  `repeating-linear-gradient`s, warm oak (#b07a45 to #8a5a2e) with fine grain lines and a soft front-edge highlight. Matte is a dark felt
  desk mat (#2b2d31) with a stitched edge, like reference 2.
- **Monitor:** thin black bezel, 16:9, on a short stand, behind and above the iPod, slightly blurred and dimmed (depth of field: `filter: blur(.4px)`; opacity .92). The canvas fills the panel.
- **Speakers:** walnut cabinets (vertical gradient + grain), black baffle, woofer (radial gradients for cone, surround and dust cap), tweeter above,
  a small front port. They are placed left and right and taller than the iPod. They rest on the desk line with a contact shadow.
  The woofers pulse subtly with bass energy: `desk.js` sets a CSS variable `--bass` on the scene, giving `scale(1 + bass*0.03)`, and this is
  turned off under reduced motion.
- **iPod:** the existing device with its own drop shadow, plus a desk contact shadow (an elliptical blurred shadow under the slot).
- **Shelf:** a low wooden or felt ledge in the front band, items standing upright at a slight angle (`rotateX(8deg)`).
  - *Cassette:* 100×64 shell (smoky translucent plastic), a J-card label strip (cover art thumbnail on the left, title and sub on the right),
    and a window with two reels that **spin** while that source is playing.
  - *CD:* 96×96 jewel case with a hinge edge, cover art under a glass sheen, and the disc edge visible.
  - *Hover/focus:* `translateY(-8px)`, stronger shadow, a soft glow in the item's accent colour (a fixed hue per shelf position, no colour sampling from art). Transition 180 ms ease-out.
  - *Active source:* a raised position and a small LED dot.
  - Overflow: the shelf scrolls horizontally (scroll-snap) when there are more items than fit. Fade edges.
- **Shell finishes** (CSS classes on `.ipod`, Desk only, via `--top/--bottom/--edge` overrides plus a `::before` overlay):
  - *Model colour*: as today.
  - *Clear acrylic*: translucent body (`rgba(220,235,245,.35)` gradients, `backdrop-filter: blur(2px)`), a faint internal "board" pattern
    (repeating-linear-gradient traces plus a battery rectangle) under the faceplate, and bright edge highlights.
  - *Anodised*: brushed metal (`repeating-linear-gradient` micro-lines over a colour gradient taken from the model colour's `Top`), a specular sweep.
  - Transitions: `transition: background .4s, box-shadow .4s, filter .4s` on `.ipod`. Track metadata changes on screen already fade
    (`lcd-fade`); no change there.
- **Light / dark:** the scene follows the existing colour's `Dark` flag for lamp-on (darker wall, warmer light) vs daylight.

## 5. Behaviour

- **Visualiser:**
  - *bars* draws 48 log-spaced bars from `freq`, with a rounded top, a gradient and a peak cap that falls.
  - *wave* draws a smoothed line from `wave` with a glow.
  - *particles* draws about 160 particles whose speed and size follow band energy, with an emitter at the centre.
  - *Idle* (no frame, or paused) shows a slow breathing gradient with the current track's title, or "FLACie" when nothing is playing.
  - The loop is skipped when `document.hidden`, and draws at most 60 fps.
- **Shelf sources (`DeskSources.Build`):**
  1. *Up Next* is shown only if something is playing: label "Up Next", sub "N songs", the current track's art. Tracks are the queue
     from the current position on.
  2. *Favourites* is shown only if non-empty, with the first resolvable art.
  3. *Each playlist* is in `Session.Playlists` order, with the first resolvable art, and is skipped if it resolves to 0 tracks.
  Picking an item calls `Player.Play(tracks, 0, "desk:<id>")`. Picking Up Next does nothing (it's already the queue) except flash.
  The "active" source is the one where `Player.Context == "desk:<id>"`. Up Next is active while anything plays and no other desk source matches.
- **Persistence:** the four new prefs are saved with the existing `flacie.ipod` stash, like the other prefs.
- **Errors:**
  - If the visualiser isn't available (iOS / no AudioContext), the monitor shows the idle state, with no error.
  - If a playlist can't be resolved, it is skipped.
  - If a cover is missing, the `<Cover>` placeholder is shown.

## 6. Responsive rules

| Width | Layout |
|---|---|
| ≥ 1100 px | Full scene: speakers, monitor, iPod, shelf |
| 860 to 1099 px | Speakers scale to 70% and move outward; the monitor gets narrower |
| < 860 px | Speakers and monitor stand hidden. The monitor becomes a thin glow strip behind the iPod. The shelf is a single scrollable row. The panel becomes the existing bottom sheet. |
| Height < 640 px | The shelf shrinks (cassettes at 80%) |

Everything is sized in `vmin`/`clamp()` from the scene box, so 16:9, 16:10 and 21:9 keep the same composition. The page never scrolls (the stage is `position: fixed`).

## 7. Testing

- **Unit (xunit), `DeskSourcesTests`:**
  - Up Next only while playing, tracks taken from the current index.
  - Favourites skipped when empty.
  - Playlists that resolve to nothing are skipped.
  - Art is the first resolvable one.
  - Order is stable.
- **Unit, `IpodPrefs`:** `LookDesk` round-trips through JSON. An unknown look falls back to Device. The new prefs are clamped.
- **Playwright, `1n-ipod-desk.spec.ts`** (against the mock, like `1m`):
  1. Pick Desk, and check `[data-testid=desk]` is visible and every `1m` testid still resolves.
  2. The shelf has ≥ 1 playlist item. Clicking one makes playback start (the existing now-playing selector).
  3. Changing the finish changes a class on `ipod-device`.
  4. At 1280×720, 1920×1080 and 2560×1080: the canvas bounding box is inside the monitor box, the iPod is inside the viewport, and there's no element
     overflow on the x-axis.
  5. At 800×700: the speakers are hidden.
  6. After reloading, the look is still Desk.
- **Manual:**
  - Check in the browser pane with screenshots at those sizes, playing a track and watching the monitor and speakers move.
  - Check under `prefers-reduced-motion`.
  - Check the Windows app's WebView2 if a build is available; otherwise list it as not tested.
- **Must stay green:** `dotnet test`, `1m-ipod.spec.ts`. Android is not affected (the web/Windows iPod mode only); the release notes will say "Desk look: web and Windows; Android not applicable yet" if this ships.

## 8. Out of scope (YAGNI)

- Extra desk props (mug, plant, glasses, notebook): maybe later as pure decoration; not in this pass.
- Dragging tapes into the iPod, or any 3D/WebGL scene.
- New iPod models or colours in `IpodModels`.
- Android parity (Android's iPod mode is a separate native skin).
- Merging, version bumps, releases or deploys: they are left for Ashley.

## 9. Coordination

The overnight autonomous session is changing `Ipod.razor`, `ipod.css`, `flacie.js` (ipod*/ios* methods), `IpodPrefs.cs`, and other files.
It asked me to branch from its next commit, keep the `data-testid`s (`ipod`, `ipod-keys`, `ipod-device`, `ipod-panel`, `ipod-gear`) and the
existing `data-look` chips. It confirmed that `LookDesk` and `vizFrame()` are safe to add. Work happens in a separate worktree, on branch `ipod/desk`.
