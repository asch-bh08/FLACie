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
