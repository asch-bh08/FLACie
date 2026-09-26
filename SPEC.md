# Technical spec (v0.3)

Stack: Kotlin, Android Views (single custom-drawn `IpodView`), Media3 ExoPlayer, minSdk 30 / target 33. Layout designed for 720x720.

## 1. Click-wheel physics (`input/WheelPhysics.kt`, pure Kotlin, JVM-unit-tested)
- Angle = `atan2` around wheel centre; per-sample delta wrapped to (-180,180]. Historical MotionEvent samples are fed too, so no lost motion at low touch rates.
- Touch zones by radius: centre button (tap = select), ring (rotate / tap top=Menu, right=Next, bottom=Play/Pause, left=Prev; hold left/right = seek).
- 6 deg slop before a ring touch becomes a rotation (separates taps from scrolls); slop motion is credited, not dropped.
- Accumulator emits one detent per `degPerDetent` (Low 24 / Med 18 / High 12). Sub-detent remainder persists between events; direction reversal clears it.
- Velocity accel: EMA-smoothed angular speed (deg/s) -> gain 1x..4x via smoothstep between 120 and 900 deg/s. Long lists (`extraGain`) add up to a further boost so 10k-song libraries stay reachable.
- Each detent = one row + haptic tick + synthesized click. Now Playing: detent = volume step (or scrub step in scrub mode).
- Tunables live in `WheelPhysics.Config`; **final values must be tuned by hand on the device.**

## 2. Theming (`theme/`)
- `IpodTheme` interface owns everything era-specific: `DeviceSpec` (body aspect, screen rect/logical resolution, wheel geometry), body/wheel chrome drawing, screen drawing (list, now playing, volume/scrub overlay), and `buildRoot()` = the menu tree for that generation.
- Navigation, playback, library, input are theme-agnostic (`nav/`, `library/`, `playback/`). Screens draw at logical resolution (320x240 / 160x128) scaled to the physical screen rect.
- Add a theme = one class + one line in `ThemeRegistry`. Themes: Nano 3G (default), Classic/Video, 1st-gen mono.

## 3. Library (`library/`)
- Walk all storage volumes (`StorageManager`), skipping `Android/`, dot-dirs, `.nomedia`; MediaStore query as fallback if the walk finds nothing.
- Tags via `MediaMetadataRetriever`; embedded art (else `cover/folder/front/album.jpg`) -> 320px JPEG thumbnail cached per album key.
- Tagged (artist or album) and not in a recordings-like folder -> Music. Everything else (untagged, or path matches recordings/voice/memo) -> Voice Memos (newest first, filename as title).
- JSON cache in filesDir; incremental rescans keyed by path+mtime+size; runs in background with 4 workers; UI shows cached library immediately. `.m3u/.m3u8` become Playlists.

## 4. Player UI (v0.3, `player/`, Jetpack Compose)
- Default view is a normal touch/D-pad music player styled like iPod (glossy Aqua widgets, mono LCD for 1G-4G/mini). Views (Settings > iPod > Show it as): Player, Player in an iPod body, Click wheel (flat), Click wheel (device). Wheel views host the original `IpodView` via `AndroidView`.
- Dynamic colour: `ArtPalette` (androidx Palette) extracts a vibrant + dark swatch from the playing cover; `buildScheme` derives gradient/accent/text colours (dark or light); `animatedScheme` tweens them for 900 ms so the whole UI fades with the artwork. Mono LCD models are never tinted. Toggle: Settings > iPod > Colours from artwork.
- Model picker (`IpodPicker.kt`): 23 models in sections (Classic 1-7G, Mini, Nano 1-7G, Touch 1-7G; shuffle-only iPods are excluded), each with colourways, live preview and a view-mode row. `UiState` is the observable prefs bridge.
- Features: mini player, full Now Playing (art-led, seek, EQ, sleep timer), queue / play next, favourites, user + folder playlists, recents, search, gamepad mapping (`PlayerKeys`), background playback via `PlaybackService` (MediaSession notification / lock screen).
