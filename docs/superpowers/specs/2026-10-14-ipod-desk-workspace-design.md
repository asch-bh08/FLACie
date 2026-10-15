# iPod mode: Desk (design, v2)

Date: 2026-10-15 · Status: draft for review · Branch: `ipod/desk` (rebased on `main` abd66af)
Supersedes v1 (a 3D-ish "studio" scene with monitor, speakers and shelf). That approach was prototyped in 3D, as a baked 2D plate,
as a studio, as a windowsill, and rejected. v2 is the design Ashley signed off in the mock-up `alive5.html` (kept in the session scratchpad).

## 1. Goal

A third **Look** for iPod mode (`/ipod`): **Desk**. You look straight down at a wooden desk in golden-hour light through blinds.
The **real iPod emulator** lies on it, with every model, every colour, Cover Flow, the settings, the wheel and the touch models, working exactly as today.
Around it sit:
- **Journal:** a page where the playing song's synced lyrics get written in, line by line.
- **Polaroid:** a Polaroid of the playing album.
- **Up-next CD:** the next album's CD lying open, its disc printed with the real disc art (or one made from the cover) and slowly turning.
- **Queue:** the albums after it in a small stack, plus a handwritten "up next" note.
- **Coffee:** a cup with steam.
- **Earbuds:** plugged into the iPod, and they are the **visualiser**:
  - sound arcs from the buds that follow bass, mids and highs
  - a ring on every beat
  - current running down the cable
  - a warm film light leak that breathes with the beat

It must feel like a warm film photo (Pinterest/film vibe). It is drawn, not a photograph: the colours are muted and the grain is fine.
It must feel *alive*: something is always moving gently.

Success means:
- **Opening Desk:** Panel › Look › **Desk** shows the scene. The choice is remembered.
- **Every iPod works on the desk:** all 23 models and all their colours. The wheel, keys, mouse wheel, MENU, play/pause, skip, select, Cover Flow, settings and the touch home screen and Music all behave as in the Device look.
- **Everything follows playback:** the journal, Polaroid, CDs and note follow the real queue and the real song. The visualiser follows the real audio.
- **Clicking a CD plays it:** clicking the open CD or a CD in the stack plays that album.
- **No WebGL, light to run:** comfortable on a phone. It pauses when the tab is hidden, and it calms down under `prefers-reduced-motion`.
- **Layouts:** a landscape layout for wide windows and a portrait one for phones. There is no horizontal scroll. On a phone, things at the edges of the desk run off the frame, as in a photo; the iPod, the journal's title and the note are never clipped.
- **Tests stay green:** existing tests and `1m-ipod.spec.ts` pass unchanged.

## 2. Architecture

```
Ipod.razor (owns all iPod state; unchanged except where marked)
 └─ .ipod-stage
     ├─ [Look == Desk]  <DeskScene Queue=… Current=… Lyrics=… OnPlay=…>
     │                     ├─ desk, light (blinds), film layers (CSS/SVG)
     │                     ├─ DeskJournal (lyrics written in), DeskPolaroid, DeskCds (open CD + stack + note), coffee
     │                     ├─ the iPod slot: ChildContent = the existing .ipod-area > .ipod-fit > .ipod (unchanged markup, every data-testid kept)
     │                     └─ DeskEarbuds (SVG: cable from the model's jack, splitter, buds, arcs, rings)
     │   [otherwise]       .ipod-area as today
     └─ .ipod-dock panel: Look chips gain "Desk" (data-look="desk")
```

- **The iPod is the existing emulator.** The desk only gives it a slot. The slot is a box in the scene, rotated −7°, and the existing `flacie.ipodFit` sizes the device to it.
  - **Rotation is safe:** the wheel's drag maths uses angles around the wheel's own centre, so a rotated slot doesn't change detents.
  - **Model-agnostic finish:** a desk finish adds a contact and long shadow, a thin back-edge layer for thickness, and a soft window gloss. It's applied around `.ipod` with CSS that doesn't depend on the model.
- **The jack** decides where the earbud cable starts. A small desk-only table maps each model to its jack edge (top or bottom) and its x position across the body. It's kept separate so `IpodModels` stays untouched. The cable route has a top-jack variant (the mock-up) and a bottom-jack variant.
- **All 2D:** CSS, inline SVG and two small canvases (dust, and the disc print, made once per album). One CC0 photo texture is used for the oak desk. There is no WebGL.

### 2.1 Data

| Thing | Source |
|---|---|
| Song, artist, album, cover | `Player.Current`, `<Cover ArtKey>` (existing) |
| Lyrics in the journal | `LyricsService.GetAsync` (synced `LyricLine`s, existing). The line being sung is written in. With no synced lyrics, plain lines are written in at an even pace. With no lyrics at all, only the title and artist show. |
| Up next and stack | `Player.Queue` from `Player.Index + 1`: the next three **distinct albums** in play order (`DeskSources`, reworked) |
| Disc print | A Cover Art Archive image of type **"Medium"** for the album's release, when FLACie already knows the release (through the existing MusicBrainz match), fetched through the existing gate and cache and never at page load. Otherwise a print made from the cover in `desk.js`: a closer crop, a darker edge, `ARTIST · ALBUM` in a ring around the hub, and small print. |
| Visualiser | `flacie.vizFrame()` (new, reuses the existing analyser): bass, mid and high bands plus a beat detector |
| Clicking a CD | `Player.Play(queue, indexOfThatAlbum'sFirstTrack)` with context `desk:<album key>` |

### 2.2 Files

New:
- `Components/Shared/DeskScene.razor`: the layout, slots and film layers
- `Components/Shared/DeskJournal.razor`
- `Components/Shared/DeskCds.razor`
- `Components/Shared/DeskEarbuds.razor`
- `wwwroot/desk.css`
- `wwwroot/desk.js`: visualiser, lyric writing, dust, disc prints, reduced motion
- `wwwroot/desk/oak.jpg`: CC0 texture, ≤ 400 KB
- `wwwroot/desk/journal.woff2`: an OFL handwriting font, so Android and Linux don't fall back to Comic Sans
- `FLACie.Core/DeskJacks.cs`: model → jack
- tests: `DeskSourcesTests`, `DeskJacksTests`, `1n-ipod-desk.spec.ts`

Changed:
- `Ipod.razor`: the Desk chip, the wrap, wiring
- `IpodPrefs.cs`: `LookDesk = 2`, which the iPod's own Settings › look toggle skips
- `flacie.js`: `vizFrame`/`vizRelease`
- `App.razor`: links to `desk.css` and `desk.js`
- `CoverArtArchive`: a helper that picks the "Medium" image

Removed from the branch: the v1 components and CSS (`DeskEnvironment`, `BackgroundVisualizerCanvas`, `MediaShelf`, `IpodShellTheme`). `DeskSources` and the Playwright port overrides stay.

## 3. Look (from the approved mock-up)

- **Desk:** oak veneer, warm and muted, seen from straight above.
- **Light:**
  - golden hour through blinds: soft diagonal slats, shade at about 0.2, sun at about 0.3
  - the slats sway (9 s) and the sun patch drifts (80 s)
- **Film:**
  - lifted blacks, a warm wash at 0.3 and a faint cool soft-light at 0.14
  - fine grain at 0.05 opacity
  - a vignette
  - a light leak in the top-right corner whose opacity follows the beat
- **Objects:** each casts a short contact shadow and a soft longer one, falling down-right.
- **Journal:**
  - a single cream lined page with a red margin
  - the title in handwriting with the artist on a smaller line and an underline
  - lyric lines written in left to right
  - a date in the corner
- **Polaroid:** the playing cover with a short handwritten caption. It lies on its own, never under anything.
- **CDs:**
  - an open jewel case: the booklet (cover) on one side, and on the other the tray with the printed disc turning (10 s per turn; paused when playback is paused)
  - two closed cases in a small stack
  - a taped paper note reading "UP NEXT / Title · Artist"
- **Coffee:** a cup on a saucer with three steam wisps rising.
- **Earbuds:**
  - two matching face-up Apple-style buds: a shell, an off-centre mesh grille with a silver ring, a tapered stem and a collar
  - a long slack cable from the jack: a loop, then over or around the iPod, a lazy curve, a Y splitter in line with the cable, two leads that run together before easing apart, and each stem lined up with its lead
  - the visualiser: three arcs per bud (bass, mid, high), a ring on each beat, warm current along the cable, and the buds twitch slightly on the bass
- **Landscape layout** (aspect ≥ 4:3):
  - journal and Polaroid on the left
  - the iPod centre-left
  - CDs and note top right
  - coffee bottom right
  - earbuds in the space below and right of the iPod
- **Portrait layout** (narrower than 1.15:1, so a laptop window with the side panel open still gets the landscape desk). The frame is a closer look at the same desk: every object keeps its landscape size next to the iPod, and whatever does not fit runs off the edge.
  - the iPod centred and large (about half the height)
  - the journal comes in from the top left: its title and the last two lyric lines show above the iPod, and the rest of the page lies under it
  - the open CD comes in from the top right, with the disc in frame and the booklet off the edge
  - the next CDs come in from the bottom left, with the Up next note on them
  - the coffee comes in from the bottom right
  - the earbuds lie in the band between, with the same shape as on the landscape desk
  - the Polaroid and the steam are hidden; the steam would rise over the iPod
- **Reduced motion:** no sway, drift, steam, spin, twitch or rings. Lyrics appear without the writing animation, and the arcs are static and dim.

## 4. Behaviour and edge cases

- **Nothing playing:** the journal shows "nothing playing" in handwriting. The Polaroid and open CD show blank placeholders (an empty case and a plain silver disc). The visualiser is quiet.
- **One album queued:** no stack, just the open CD. **Queue empty after the current song:** no CDs, and the note reads "end of queue".
- **Touch models:** the jack sits on the bottom edge and the cable is routed downwards. The home button still works.
- **Model switch:** the slot re-fits (the existing fit), the jack table changes the cable route, and everything else stays.
- **Visualiser unavailable** (iOS, no AudioContext): arcs and rings follow a calm, fixed pulse at low strength. No errors.
- **Performance budget:**
  - one rAF loop for the visualiser and cable, capped at 60 fps
  - dust at 25 fps
  - all of it paused when `document.hidden`
  - no layout thrash: transforms and opacity only

## 5. Testing

- **Unit (xunit):**
  - `DeskSources`: the next three distinct albums in play order, skipping the current album, handling a short queue or an empty one
  - `DeskJacks`: every model id has an entry, and touch models are bottom-edge
  - `IpodPrefs`: Desk round-trips and the toggle skips it
- **Playwright `1n-ipod-desk.spec.ts`:**
  1. **Desk renders:** Desk renders with one model from each family (mono, video, nano, touch), and the `1m` test ids resolve.
  2. **iPod controls:** the wheel and keys still drive the iPod. Enter selects, Escape goes back, the next zone skips, the play zone toggles.
  3. **CD click:** clicking the open CD plays the next album.
  4. **Journal:** shows the song title.
  5. **Layout:** at 1280×720, 1920×1080, 2560×1080 and 390×844, every desk object and the iPod are inside the viewport, and there is no horizontal scroll.
  6. **Remembered:** the Desk look survives a reload.
  7. **Clean console:** no console errors.
  8. **Reduced motion:** with reduced motion emulated, there are no running CSS animations under `.desk`.
- **Manual:** checks in the browser pane, with screenshots of each family and of portrait.
- **Must stay green:** `dotnet test` and `1m-ipod.spec.ts`.

## 6. Out of scope

- WebGL and photographs.
- The v1 monitor, speakers, shelf and shell finishes.
- Android (its iPod mode is native).
- Merging, versions, releases and deploys, which are Ashley's.

## 7. Assets needing Ashley's OK before download

- **Desk texture:** `oak_veneer_05` diffuse, 2k, from Poly Haven (CC0). Resized to ≤ 400 KB.
- **Handwriting font:** one OFL font from Google Fonts or Fontsource, for example "Caveat". The size and licence will be stated when asking.
