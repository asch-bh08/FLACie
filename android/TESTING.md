# Status and on-device test list

## Build status
Compiles; debug APK builds (`app/build/outputs/apk/debug/app-debug.apk`). The 9 JVM wheel-physics tests pass. **The app has never been launched**: no device or emulator was available, so no UI, touch, input or playback behaviour has been observed.

Build: JDK 17 (Temurin), Android SDK 34 at `%LOCALAPPDATA%\Android\Sdk`, then
`JAVA_HOME=<jdk17> ./gradlew :app:testDebugUnitTest :app:assembleDebug`. Install with `adb install -r app-debug.apk`.

## Needs hands-on testing on the RG Rotate (nothing below is verified)
Wheel feel (highest risk) — tune in `WheelPhysics.Config`:
- [ ] Degrees per detent (Slow/Medium/Fast = 24/18/12): one detent ~ one row at a natural finger speed?
- [ ] Velocity gain curve (120-900 deg/s, max 4x): fast spin covers a 10k list without overshooting; slow spin stays 1:1.
- [ ] 6 deg slop: taps on MENU/Prev/Next/Play never register as scrolls; scrolls never register as taps.
- [ ] Wheel size: radius is ~140px on the 720x720 panel; check thumb reach and edge-of-ring touches.
- [ ] Touch sampling rate/latency on this panel (historical samples are consumed, but jitter is unknown).
- [ ] Reversing direction mid-detent; finger crossing the centre button while rotating.
- [ ] Click sound + haptic: does the device have a vibration motor? Is the click too loud/late?

D-pad / buttons:
- [ ] Which keycodes the RG Rotate actually sends (DPAD_* keys vs hat axis, BUTTON_A/B swapped?). Use Settings > Face Buttons to swap A/B and X/Y.
- [ ] Hold-repeat speed for up/down; left/right tap = prev/next, hold = seek; B hold = back to main menu.
- [ ] Hardware buttons and touch wheel interleaved without stuck state.

Display / themes:
- [ ] Layout on the real 720x720 panel: body fits, no system bars, no cutout overlap, rotate/swivel behaviour.
- [ ] Legibility of 160x128 mono theme at 3x integer scale; fonts are system Roboto/monospace, NOT the real Myriad/Chicago (licensing). Fidelity to each era needs side-by-side review against reference screenshots; colours/metrics were reproduced from memory.
- [ ] Scroll/transition smoothness (frame pacing) on the device GPU.

Library / playback:
- [ ] Storage access to the SD card via File API on this Android 12 skin (MediaStore fallback exists).
- [ ] Scan time and memory on a real library; embedded art extraction for mp3/flac/m4a.
- [ ] Voice-memo classification (untagged or recordings-like folders -> Voice Memos).
- [ ] Background playback, headset buttons, notification, gapless, screen-off behaviour.

## Not built
Cover Flow, per-item collage previews on menus, other Nano generations, Photos/Videos/Games/recording (out of scope).

## v0.2 additions (verified by screenshots + adb taps/keys on the RG Rotate; feel not judged)
- Verified: touch tap-to-open, drag-scroll, touch keys, folder playlists, Settings pages, EQ selection, theme switch, playback.
- Still needs hands-on: touch fling feel, backlight timeout/brightness, sleep timer, whether the EQ audibly changes sound (device EQ effect support varies), Time in Title.

## v0.3 additions (Compose Player; verified by adb screenshots/taps/keys on the RG Rotate)
- Verified: Player is default; Home, Albums grid, album detail, Now Playing, Search, Settings, picker (incl. touch model with wheel options disabled), Player-in-iPod frame (Touch 4G), D-pad focus + select, wheel mode still reachable from Settings.
- Still needs hands-on: art-colour fade on varied covers (pink art -> pink UI), light theme, Nano 6/7 shapes, Player-in-iPod wheel tap zones on click models, gamepad flow end to end, other aspect ratios (foldable/phone) for the Compose UI, scroll performance on very large libraries, wheel feel.
- Known gaps: "Remove from playlist" for user playlists is a stub; no A-Z index scroller; shuffle-only iPods not included.

## v0.3.1 gestures (verified on the RG Rotate via adb swipes)
- Left-edge swipe = back (all screens, Now Playing, picker); swipe down on Now Playing to close (follows finger); swipe up on mini player opens it; flick mini player / cover art / title left-right = next/prev; track rows: swipe right = Play next, swipe left = Favorite (queue rows: either way = Remove); animated page slides.
- Click wheel inside "Player in an iPod" now rotates (22 degrees per detent, haptic tick) and moves the highlight; centre selects; Now Playing: rotate = seek.
- Needs hands-on: swipe thresholds/feel with a real finger, edge-swipe vs system gestures on phones, brief system-bar flash seen after an edge swipe on the RG Rotate.

## v0.4 performance / density / appearance (RG Rotate, release build)
- `tools/jank.sh` (Songs scroll + swipes): debug 52% janky (median 28 ms, p99 900 ms) -> release 7-10% -> after recomposition fixes 4-7% (p99 44-73 ms). 5 track changes: p99 450 -> 93 ms after moving artwork to file URIs and saving user data off the main thread.
- Baseline profile is hand-written wildcards + the libraries' own profiles (no Macrobenchmark: needs a rooted / Android 13+ device); `cmd package compile -m speed-profile -f com.ipodemu` after install to apply it immediately.
- Verified: slim mini bar, Home Now Playing card, 56 dp rows, square Now Playing, Appearance screen (skin change restyles the whole app live, back gesture closes it), Player-in-body withheld with the reason shown.
- Needs hands-on: Player in an iPod on a phone/tablet, wheel-view Appearance page, Brightness on device, fixed-colour swatches, light theme under "System".

## v0.5 (RG Rotate, release build)
- Verified on-device: all five skins apply live (LCD, Aqua, Nano, Touch iOS 7+ visibly distinct; Touch iOS 6 looks nearly the same as Aqua - both glossy), click-wheel views after the Appearance consolidation (wheel Settings > Appearance opens the shared screen; Back returns), body view on a small screen (readable text, wheel rotates the highlight, jump button pans between screen and wheel), controller (D-pad + Back) on Home, lists, Now Playing, sheets, Settings and Appearance, For You shelves + mix detail, hardware Home in the body view.
- Not verified: body view on a phone/tablet, Backlight dimming on device, Time in Title, Songs sort beyond the A-Z default, hardware-mode Music submenu, touch-only skins in the body view.

## v0.6 Appearance redesign (RG Rotate)
- Verified on-device: three named modes with previews (Modern Player / iPod Emulator / Click Wheel Fullscreen), live top preview of mode + iPod + colour, device gallery by family with real drawings, colour swatches updating the gallery/preview, touch iPods disabling the wheel mode with the reason, live switching between all three modes (the wheel view rebuilds), emulator with a wheel iPod and with a touch iPod, hardware-accurate emulator Home.
- Not verified: gallery on phone/tablet layouts, focus/controller order through the new cards.

## Full bug pass (v0.6, RG Rotate, release build) — every screen, every mode, every device family
Systematic click-through of Modern Player, iPod Emulator and Click Wheel Fullscreen across Classic, Mini, Nano and
Touch families; mode-then-model and model-then-mode switches in both orders, repeated back-and-forth; Back via
on-screen pill, hardware Back, gamepad B and swipe-back from every screen including nested ones (Settings inside the
Emulator body, Music submenu, Voice Memos, Search, Queue, action sheet, name dialog); the carousel's arrows, direct
neighbour tap and L1/R1; rapid/unthrottled input (10 rapid R1 presses, fling-then-tap mid-animation, rapid alternating
mode taps, backgrounding mid-transition). No crash or ANR anywhere in the pass (logcat clean throughout).

**Found and fixed:**
- Header text overlap with Time in Title on: a real 1st-4th-gen iPod's 160px-wide header (MonoGen1Theme) has no room
  for both a centred title and an "h:mm a" clock next to the battery/play icons; the title, pinned at the true
  centre, collided with the clock. Reproduced on iPod mini (1st gen) in Click Wheel Fullscreen, confirmed to persist
  across app relaunch (not transient), and fixed by recentring the title in whatever space is free of the clock
  instead of the fixed centre. Applied the same defensive fix to ClassicTheme and Nano3Theme's headers (not
  reproduced there — their wider design widths had enough slack — but the same collision was reachable with a long
  enough title). Verified clean afterwards with the header's own longest titles ("Settings", "Now Playing", "Voice
  Memos") on mono, and with "iPod"/"Extras" on Classic/Nano3, all with the clock showing.

**Investigated, not a bug:** a suspected frozen-Back-button repro via an automated `uiautomator dump`-based tap
turned out to be a timing artifact of that tool (the dump raced a screen transition); a direct coordinate tap at the
same step worked correctly. A "Click wheel layout" setting that appeared to have reset was in fact the RG Rotate's
own physical controller-deployed state (Auto mode correctly follows the hinge), not a stored preference reverting.

**Not reproduced / nothing else found:** the original frozen-Back-button bug from the previous session did not
recur under any mode/model switch order, repetition, or rapid/backgrounded input tried here. Not tried: a real phone
or tablet aspect ratio (no simulator available for this physical device), and touch-family models specifically
inside "Player in an iPod" on such a screen.

## v0.8 Modern default, safe area, account + playlist sync (RG Rotate, debug build)
- Verified on-device: Modern theme is the default (nav rail on the square panel); iPod theme via Settings > Theme restyles
  the whole app and Back from its Settings returns to Home; "Modern theme" pill on the Appearance screen switches back.
- Layouts via `am start --es fake WxH --es fakedp N --es fakecutout N`: phone 1080x2400 @411dp, Z Fold cover
  904x2316 @344dp (bottom nav) and inner 1812x2176 @673dp (rail), each with a simulated top cutout -- content, mini
  player and Now Playing stay clear of it.
- NOT verified on a real punch-hole phone/Fold: Android's own cutout emulation overlay crashes system_server on the RG
  Rotate's square display (don't enable `com.android.internal.display.cutout.emulation.*` on it).
- Account: Quick Connect sign-in, profile push, "Sync Test" playlist (local + Jellyfin + NAS track) mirrored as a real
  Jellyfin playlist; `pm clear` + sign-in restored Jellyfin/NAS/Lidarr/slskd/file-mover + ipodsync host, the playlist
  and favourites; deleting the playlist deleted its Jellyfin copy and stayed deleted after the next sync.
- Not verified: password sign-in (not exercised -- no password entered), cross-device title+artist matching of a
  local-only file on a second device, Plex restore (Plex was never configured).

## v0.8.1 Now Playing, lyrics, NAS/Jellyfin de-dup (RG Rotate + simulated phone/Fold/landscape)
- Now Playing verified per shape: square 720x720 (art + title/actions on top, full-width seek/transport), phone
  1080x2400, Fold inner 1812x2176 (portrait layout, big art), landscape 2400x1080 (art | controls | lyrics).
- Lyrics: synced LRCLIB lyrics for "Bad Romance" follow playback (line highlight moved 0:17 -> 0:25) on square and
  phone; a remix with no LRCLIB entry shows "No lyrics found". Jellyfin had no lyrics for the items tried.
- De-dup: "just dance" search went from 5 rows (Local/NAS/Jellyfin copies of one song + NAS/Jellyfin copies of a
  remix) to 2. Songs 8721 -> 5268 = 589 local + ~4.6k Jellyfin songs not on the device + 36 NAS-only files (Jellyfin's
  6460 files are 4904 distinct songs); Library shows "NAS only (36)".
