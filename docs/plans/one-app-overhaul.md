# ONE APP overhaul: plan (26 Nov)

Owner: "the web buttons, labels, text, menus and the phone's: the exact same styling. ONE APP." and "don't just copy the web, make it best for
mobile but look the same as web". Now Playing and the Devices tab are the best screens; the rounded-rectangle slots (Lyrics / Up next / More /
Info / Add) are liked; the web's small square mono tags on Now Playing are liked.

## What the inventory found
- Fonts and colour values are already shared (Instrument Serif, Archivo, DM Mono; the same ink / surface numbers). Web bg is slate #101416, phone #0B0B0E.
- Buttons: web has ~9 families (heights 34/38/40/44/52), phone ~9 composables (36/44/48/56). No common scale.
- Tags: web 4 styles (.badge, .tag, .dev-chip, .dv-chip), phone 4 (PlTag, Devices Chips, FormatChip, SleeveChip).
- Slots: web .ls-slot 56/r18, phone ToolSlot 58/r18 and Now Playing Slot 50/r16; "on" is ink on two, accent tint on one.
- Text: the phone's Txt takes any size (13 different ones in use); no named styles on either side.
- Menus: web popovers, phone sheets (two renderers: ActionSheet and DesignSheet). Item order and words differ.
- Toasts: web stays until dismissed with an x; phone goes after 2.5 / 5 s, no x. Warn: border on web, amber text on phone.
- Duplicates on the phone: 2 DeviceCards, 2 AlbumCards, 3 section headings, 2 empty states, 5 top bars, 5 text fields.
- Wording: "Favorites"/"Favorite"/"Unfavorite" left on the phone; "..." vs "…"; straight vs curly quotes; "Download for offline" vs "Download";
  confirms "Clear all N / Keep them" (web) vs "Yes, clear them" (phone).
- Screens only on the web: admin, charts, import. Only on the phone: offline settings, legal, catalog song/album pages, setup screens, remote.

## The one design (the spec both sides build to)
| Piece | Decision |
|---|---|
| Colours | One token set (web names): bg, surface 1-3, line, ink, dim, faint, accent (follows the cover), glass fill #0FFFFFFF, glass rim #24FFFFFF. Phone bg moves to the web's slate. |
| Text | Five named styles only: Title (serif 30, italic last word), Section (serif 24), Body (sans 15), Small (sans 13 dim), Label (mono 11 caps, .12em). Phone `Txt(size=…)` calls are replaced by these. |
| Main button | Pill, 48 tall (phone touch and web), sans 15/600: Primary = cover colour fill, Secondary = tone fill (Play / Shuffle). |
| Small button | Pill, 36 visible / 48 touch, sans 14/600, outlined line; selected = ink fill (filters, tabs, settings choices). |
| Slot | The liked rounded rectangle: 52 tall, radius 16, glass fill, icon 18 over word 11.5/600; on = ink fill. Used for every row of quiet actions (Now Playing, playlist, album, artist, Devices card). Phone and web the same. |
| Icon button | Round 44 (48 touch), glass; on = accent icon. |
| Status tag | Outlined pill, sans 11.5/600 (the Devices tag): This device, Playing, Offline, Streaming, Recently downloaded. |
| Format tag | The Now Playing tag the owner liked: mono caps 10.5, radius 5, filled: HI-RES (amber), FLAC 16/44.1, MP3 320, source (JELLYFIN). Only where the format matters (Now Playing, song info, search hero, Hi-Res rows). |
| Card | Glass, radius 22, rim; one component each side. |
| List row | 64 tall, cover 44 r8, title Body 600, second line Small, right: time (Label) + ⋯; one row component each side (song, album, playlist, device variants). |
| Menu | Same items, order and words everywhere (table below). Phone: one sheet (DesignSheet; ActionSheet only for the iPod theme). Web: popover on a PC, bottom sheet on a phone-width window (best for touch). |
| Confirm | Phone sheet / web inline swap, same words: "Delete “name”?" → "Delete" / "Keep it". |
| Toast | Both: bottom, surface-3, 2.5 s (5 s with Undo), tap or x to dismiss, warn = amber text. |
| Empty state | Both: centred glyph, one line, optional main button. |
| Switch | 44×26, knob 22, white knob, accent track. |
| Search field | Pill r24, 48 tall, surface fill, mic inside. |
| Top bar | Back · serif title · actions · account; Now Playing keeps its own. |
| Navigation | Phone and phone-width web: Home, Search, Library, Devices, Settings. PC rail: the same five first, then the extras (Jam, Car, Headphone test, Admin). |

Song menu (both): Play next · Add to queue · Add to playlist… · Add to favourites / Remove from favourites · Go to artist · Go to album · Share ·
Download / Remove download · Not for me… · More… (Rate…, The DJ and this song…, ✦ Hi-Res version…, Fix the match…). On a playlist add
Remove from playlist. Words: Favourites (never Favorites), "…" (one character), curly quotes, "Download" (not "for offline").

## Waves (dependency order; each wave's parts run in parallel, one agent each, in worktrees cut from ui/living-sleeve)
**Wave 0: the kit (must land first; everything else uses it)**
- 0a Phone kit: `ui/Kit.kt` with Title/Section/Body/Small/Label, MainButton, SmallButton, Slot, IconButton, StatusTag, FormatTag, GlassCard,
  SongRow, EmptyState, Toast timings, one Sheet; tokens in one place; old composables become thin wrappers so screens keep building.
- 0b Web kit: `wwwroot/kit.css` with the same pieces as classes (.k-title, .k-btn, .k-btn-sm, .k-slot, .k-icon, .k-tag, .k-fmt, .k-card,
  .k-row, .k-empty, .k-toast) and Blazor components where markup repeats (Slot, Tag, FormatTag, EmptyState, SheetMenu for phone width).
- 0c Words: one strings list (`docs/plans/one-app-words.md`) + fix every phone and web string to it; a test on each side that fails on
  "Favorites", "...", straight quotes in user text.
- 0d Parity check tool: one script that shoots every screen on the web at 390 wide and on emulator-5554 and puts each pair side by side in
  one HTML page (docs/parity/), so the owner (and the gate) can see them together.

**Wave 1: screens onto the kit (parallel, after wave 0)**
- 1a Home + Library + Favourites
- 1b Search (hero, sections, catalog song/album pages)
- 1c Album, Artist, Genre, Made pages (playlist page already done; bring it onto the kit)
- 1d Settings + Account + Offline/Downloads
- 1e Devices + Jam + Car
- 1f Menus and sheets (song menu everywhere, confirms, playlist picker), toasts, dialogs
- 1g Now Playing + mini bar + queue (onto the kit, keeping its look)

**Wave 2: clean-up and proof**
- Delete the old duplicates (second DeviceCard, AlbumCard, headings, empties, text fields, .badge/.tag/.dev-chip, unused button classes).
- Full test runs (C#, Android unit, Playwright, emulator smoke/dj/car/playlists), the parity page reviewed, release as one beta + web deploy.

## Gates (each wave)
- Builds; C# + Android unit + Playwright for touched pages; emulator smoke 23/23.
- Parity page: every pair looks the same family (same fonts, sizes, tags, buttons); a lint test on the phone fails on a raw `Txt(size = …)`
  outside the kit, and on the web on new button classes outside kit.css.

## Size and pacing
About 3 waves; wave 1 is 7 parallel parts. Weekly usage is at ~40%: wave 0 + 1 fit this week if run as 2-3 agents at a time; wave 2 next.
A beta after wave 1 so the owner can test, then the final one after wave 2.

## Owner decisions this plan assumed (say if any is wrong)
1. Phone background moves to the web's slate (#101416) from near-black.
2. Format tags = the mono square ones (as in Now Playing) everywhere a format shows; status tags = outlined pills.
3. On a phone-width web window, menus become bottom sheets like the phone (popovers stay on a PC).
4. Bottom tabs the same on phone and phone-width web: Home, Search, Library, Devices, Settings.
