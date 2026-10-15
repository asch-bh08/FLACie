# Retro scenes: design (overnight brief, 2026-10-16)

Ashley's overnight brief asked for four more listening scenes beyond the iPod desk, each with its own view toggle, layout, media and controls wired to the player, built and tested on branch `ipod/desk` (no merge, release or deploy). This note records how they fit into FLACie. The brief itself is in the session transcript.

## Where they live

- **Scene switcher.** The `/ipod` page gets a *Scene* setting next to Look: iPod (today's page: Device, Wheel only or Desk), Car, Hi-Fi, Boombox, MiniDisc. It is kept with the iPod settings (`IpodPrefs.Scene`, JSON `scene`). An unknown value means iPod.
- **Other scenes replace the iPod.** In any scene other than iPod, the stage shows that scene and not the emulator. The panel keeps the player card and the Scene choice, and hides the model, colour, look and desk options, which only apply to the iPod.
- **One component per scene,** in `Components/Shared`:
  - `RetroCar.razor`
  - `RetroHifi.razor`
  - `RetroBoombox.razor`
  - `RetroMinidisc.razor`
  - shared styles in `wwwroot/retro.css`
- **The engine.** `desk.js` drives the moving parts for every scene from one read of the analyser per frame:
  - CSS variables `--low`, `--mid`, `--high` and `--beat`
  - meter needles (`data-needle`, one or more)
  - the coffee, where there is one
  - the mobile-data rule: no second copy of the song, a calm pulse instead
- **The song's clock.** `flacie.clock()` provides the position for tape counters and the tonearm.

## What every scene shows and does

The data comes from `PlayerState`, as for the desk:
- the song (title, artist, album, year), its position and length, and whether it plays
- the next albums and songs
- your playlists

Every control calls the player: play/pause, next, previous, seek, and playing an album or playlist. Media are buttons (Tab, Enter, Space). With reduced motion everything holds still.

| Scene | Look | Controls bound to the player |
|---|---|---|
| Car | A night dashboard seen through a rain-streaked windshield, with blurred city lights and a wiper sweep; a single-DIN head unit with an amber dot-matrix display, a CD slot and a volume knob; six presets; climate dials | Play/pause, previous/next (the display "tunes" between songs, with a frequency sweep). Eject pauses and the disc slides out; the slot or the disc plays again. Presets 1–6 are your playlists. The knob sets the volume. |
| Hi-Fi | A 1970s wood media console in a lamp-lit room with a plant; a silver receiver with a tuning dial and two green signal meters; a direct-drive turntable | The platter spins while playing and the record's label is the cover. The tonearm tracks across the groove with the song's position, and a click on the record seeks. The pitch slider sets the speed. A short needle-drop crackle plays on a song change (Web Audio, quiet; off with reduced motion). A crate of sleeves (next albums, then playlists) flips through, and a sleeve plays its album or playlist. |
| Boombox | A workbench with stickers and spare batteries; a dual-cassette boombox with chrome grilles whose woofers move with the bass, a handle and EQ sliders that follow the bands | Deck A holds the playing album with turning reels and a counter of the song's position. The keys are rewind (previous), play, fast-forward (next) and stop/eject (pause, door open). Deck B's tape is the next album or playlist, and a click plays it. |
| MiniDisc | A clean 2000s dorm desk; a brushed-aluminium micro system with a top-loading MD slot, a blue backlit LCD (title, artist, track and time), a jog-shuttle dial and small speakers; a stack of MiniDiscs | The jog dial turns to skip (a turn to the next or previous song) and a press plays or pauses. The disc in the slot is the playing album. The MDs on the desk are your playlists, and a click plays one. |

## Layout

Each scene, like the desk, is a size container with a landscape (16:9) and a portrait (9:19) layout. A phone gets a closer frame of the same scene in which the hardware stays big, and there is no horizontal scroll. Everything is drawn in CSS and SVG; nothing is downloaded.

## Tests

- **Unit:** `IpodPrefs.Scene` round-trips, and unknown values fall back.
- **Playwright, per scene:**
  - it renders with no console errors
  - it shows the playing song
  - play/pause, next and previous drive the player
  - media clicks play
  - phone and desktop sizes fit, with no sideways scroll
  - reduced motion holds still
- **The desk and the iPod suites stay green.**
