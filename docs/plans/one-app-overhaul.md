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

**One button shape (owner, 26 Nov: "why did you make half the buttons rounded rectangles but not the others"):** every button on both
sides is the same rounded rectangle (radius 16-18): main buttons, small buttons, slots, icon buttons, play / previous / next, the dock's
right side. No circles or full pills left for buttons (tags stay pills; covers keep their own corners).

**Big by default (owner, 26 Nov: "I LOVE big buttons ... they're too small, but they can be big and fit, be smart").** Every size below is the
comfortable size (the owner loves car mode's huge buttons: take their feel, chunky, filled, clear icon and word, easy to hit without looking,
at app scale, not car scale; car mode can be that big because it leaves features out, the app must keep every feature, so each screen
shows its 3-5 main actions big and puts the rest one tap away: More, Edit or a sheet, as the playlist page does); a piece only steps down (never below 48 touch / 44 visible) when the screen measures too small for it, by the same fit
rule on both sides (Now Playing's NowPlayingFit idea, applied to every screen). Sizes after this note's update:
| Piece | Decision |
|---|---|
| Colours | One token set (web names): bg, surface 1-3, line, ink, dim, faint, accent (follows the cover), glass fill #0FFFFFFF, glass rim #24FFFFFF. Phone bg moves to the web's slate. |
| Text | Five named styles only: Title (serif 30, italic last word), Section (serif 24), Body (sans 16), Small (sans 14 dim), Label (mono 11 caps, .12em). Phone `Txt(size=…)` calls are replaced by these. |
| Main button | Pill, 56 tall (52 when tight) (phone touch and web), sans 15/600: Primary = cover colour fill, Secondary = tone fill (Play / Shuffle). |
| Small button | Pill, 44 visible / 48 touch (40 when tight), sans 15/600, outlined line; selected = ink fill (filters, tabs, settings choices). |
| Slot | The liked rounded rectangle: 60 tall (52 when tight), radius 18, glass fill, icon 22 over word 12.5/600; on = ink fill. Used for every row of quiet actions (Now Playing, playlist, album, artist, Devices card). Phone and web the same. |
| Icon button | Round 52 (48 when tight), glass; on = accent icon. |
| Status tag | Outlined pill, sans 11.5/600 (the Devices tag): This device, Playing, Offline, Streaming, Recently downloaded. |
| Format tag | The Now Playing tag the owner liked: mono caps 10.5, radius 5, filled: HI-RES (amber), FLAC 16/44.1, MP3 320, source (JELLYFIN). Only where the format matters (Now Playing, song info, search hero, Hi-Res rows). |
| Card | Glass, radius 22, rim; one component each side. |
| List row | 72 tall (64 when tight), cover 52 r10, title Body 600, second line Small, right: time (Label) + ⋯; one row component each side (song, album, playlist, device variants). |
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
- 1g Now Playing + mini bar + queue (onto the kit, keeping its look), rearranged (owner, 26 Nov), every feature kept:
  - no Up next slot: swiping and the Up next line under the controls already go there
  - Shuffle and Repeat out of the transport row (beside the time or in the slot row), so previous / play / next get the room and grow
  - no wasted space: above the title, left and right of the time, under the frame/cover (e.g. the time's two numbers at the frame's
    lower corners); the spare room is used or shared, never a band
  - Lyrics / Info: the plain seek line is liked but too plain: the round thumb becomes a tall rounded-rectangle thumb, the line a little bolder
  - the + (Add to playlist) smaller or tucked, still one tap away; the phone's add-to-playlist page redone on the kit (it is outdated)
- Owner, 26 Nov: "pretty much every page other than Now Playing, Home and Devices is outdated" (like the phone's add-to-playlist page),
  and the More menu/page needs redesigning. So every other page is a redesign, not a re-skin. Method for each page: put the web and the
  phone version side by side (parity page), take the better ideas of each, make them look the same, then fit each to its screen (phone:
  thumb reach, sheets, big main actions; PC: width, popovers, more visible at once). Now Playing, Home and Devices are the reference look.
- 1g-2 Now Playing rethink (owner, 27 Nov on b146: "still bad"):
  - the buttons look weird; there seems to be room for Shuffle and Repeat next to previous/play/next again, yet beside the time is not
    right either: rethink their place (not just back in the row, not where they are now)
  - Lyrics / Info: Shuffle/Repeat there look odd, the spare space is not used, and the seek bar is squashed: redo that layout
  - unfolded (Fold inner / tablet / wide): the cover is too small with lots of wasted space: the cover should take the room
  - the full-screen Info graphs: their buttons need a revamp (onto the kit, even, worded like the dock)
  - method: mock 2-3 layouts side by side (phone tall, phone short, unfolded, PC) on the parity page before building, pick the best
- 1k ONE PAGE FRAME (owner, 28 Nov, top priority: "every page is so different, every time I go to a new page something changes:
  the text, the buttons, the labels, where the back button is, whether the navigation bar is there, whether I can swipe down / left
  out / back, the features, whether I see the same stuff"). Write docs/plans/one-app-frame.md first, then make every page follow it,
  web and phone:
  - one page shell: top bar = back (top left, always the same place and icon) · serif title · up to two actions · account; no page
    invents its own header
  - the tab bar (Home, Search, Library, Devices, Settings) is on every page except Now Playing, full-screen graphs, sign-in and setup;
    the same tab stays lit for pages under it
  - gestures, the same everywhere: swipe right from the left edge = back on every stacked page; swipe down = close on every overlay
    and sheet (Now Playing, menus, full-screen graphs); nothing else uses those swipes
  - a page's header block: kit Title, one facts line (Label), Play / Shuffle (MainButton) when the page plays music, then one
    Slot row of 3-5 actions, the rest in More; lists use the kit SongRow everywhere
  - the same page has the same content on web and phone (a feature on one is on the other, or the gap is listed in the frame doc)
  - a test per side that walks every page and checks: back button position, tab bar present or not per the rule, title style, slot
    row, SongRow; and the parity page shows every page pair
- 1g-3 Now Playing still needs bettering on the phone (owner, 28 Nov); b147 left a gap at each end of previous / play / next: fixed
  locally (they share the row now); the More sheet on the phone is unchanged and needs the new look; web: Volume slot cramped
  (slider squeezed, the word wastes room) and the full-screen graph's worded Shuffle / Repeat clip their words
- Every page on every platform is in scope (web PC, web phone, Android, the Windows app's pages, Android Auto keeps its own car rules).

- 1h Every screen size (owner, 26 Nov: "fits every aspect ratio, resolution, screen size"): one size list used by both checks — phones
  360×640, 375×667, 390×844, 412×915, tall 412×1000 (20:9 and 21:9), a Fold's cover and inner screen, landscape phone, tablet 768×1024 and
  1024×768, laptop 1280×720 / 1366×768 (+125% and 150% zoom), PC 1920×1080, ultrawide 2560×1080, 4K at 200%. Every main screen (not just
  Now Playing) gets the "everything on screen, nothing overlapping, nothing cut off, no sideways scroll, no big empty band" check: the web
  extends 1z-np-fits-all / 1z-fit-everywhere to every page; the phone's emulator driver runs each screen at each size with `wm size`/`density`.
  Spare room is shared out or used (as Now Playing's Up next line), never left as one empty band.
- 1i Admin only for admins: list every server-wide control (server admin dashboard, download sources and policy, imports, the shared
  library's fixes, DJ server settings, users, logs, Alexa, the Windows sync of the server) and make sure a normal user never sees or reaches
  them on any side — hidden in the UI and refused by the server (403), with a test per endpoint and a Playwright/emulator check signed in as
  a normal user.
- 1j Anyone can install it, normal users never see the plumbing (owner, 26 Nov): two paths.
  - Owner / self-hoster: a first-run setup on the web and the phone that walks through connecting their own things (Jellyfin, Plex, a NAS
    folder, Lidarr, their own FLACie Web address), checks each one and says plainly what works; everything technical lives under Settings →
    Server (admin only).
  - Normal user (invited to someone's server): sign in, and that's it — no server addresses, keys, sources or download settings in their way;
    their Settings is playback, downloads to the phone, the DJ and the account. A test signs in as a fresh normal user on both sides and
    checks that the first screen is music, not setup.

**Wave 2: clean-up and proof**
- Delete the old duplicates (second DeviceCard, AlbumCard, headings, empties, text fields, .badge/.tag/.dev-chip, unused button classes).
- Full test runs (C#, Android unit, Playwright, emulator smoke/dj/car/playlists), the parity page reviewed, release as one beta + web deploy.

## Gates (each wave)
- Builds; C# + Android unit + Playwright for touched pages; emulator smoke 23/23.
- Parity page: every pair looks the same family (same fonts, sizes, tags, buttons); a lint test on the phone fails on a raw `Txt(size = …)`
  outside the kit, and on the web on new button classes outside kit.css.

## Size and pacing
About 3 waves; wave 1 is 10 parallel parts (1h-1j added 26 Nov). Weekly usage is at ~40%: wave 0 + 1 fit this week if run as 2-3 agents at a time; wave 2 next.
A beta after wave 1 so the owner can test, then the final one after wave 2.

## Owner decisions this plan assumed (say if any is wrong)
1. Phone background moves to the web's slate (#101416) from near-black.
2. Format tags = the mono square ones (as in Now Playing) everywhere a format shows; status tags = outlined pills.
3. On a phone-width web window, menus become bottom sheets like the phone (popovers stay on a PC).
4. Bottom tabs the same on phone and phone-width web: Home, Search, Library, Devices, Settings.
