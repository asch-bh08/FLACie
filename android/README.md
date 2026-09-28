# ipodplayer

An iPod-style local music player for the **Anbernic RG Rotate** (720×720, Android 12, touch + D-pad + gamepad buttons). It scans the device's own music (embedded cover art, untagged audio and voice memos included) and plays it as a modern touch player styled after every iPod generation, or as the original click-wheel interface.

> Status: working prototype (`versionName 0.3`). Built and tested on a real RG Rotate over adb; see [TESTING.md](TESTING.md) for what is verified and what still needs hands-on time. Design notes: [SPEC.md](SPEC.md). Original brief: [Rg rotate ipod emu brief.md](Rg%20rotate%20ipod%20emu%20brief.md).

## Features
- **Player (default):** a normal music player with iPod styling — glossy Aqua-era buttons and rows, monochrome LCD look for 1G–4G/mini. Home, Playlists, Artists, Albums (cover grid), Songs, Genres, Voice Memos, Search, Queue ("Up Next"), Favorites, Recently Played, mini player and full Now Playing.
- **Colours fade to the artwork:** the UI gradient, accent and buttons tween (0.9 s) to the palette of the playing cover — pink cover, pink UI. Toggle in Settings; mono LCD models are never tinted.
- **One Appearance screen** (Settings → Look & Mode, also reachable from the click-wheel menus): Look (LCD · Aqua · Nano · Touch iOS 6 · Touch iOS 7+), Mode (Player or Click wheel, plus an optional iPod body), Colour (auto from artwork or fixed) and Theme (system by default). The exact iPod is an *Advanced* option: all 23 models (Classic 1–7, mini 1–2, nano 1–7, touch 1–7; shuffles excluded), each with real colourways. "Player in an iPod" is only offered when the UI would stay readable (`bodyScale`), so not on the RG Rotate's square panel.
- **Modern Player vs faithful device view:** the flat Player (any skin, click-wheel iPods included) is a full-featured app - Made for You / Explore shelves built locally from play counts, favourites and history (Daily Mixes, Suggested, On Repeat, Rediscover, Discover, genre and decade mixes), Songs sorting, A-Z sections, swipe gestures. "Show iPod body" is the emulator view and sticks to real hardware: classic Music / Voice Memos / Settings / Shuffle / Now Playing menu, wheel-only navigation. On small screens the body grows until the iPod screen is readable (>=260 dp) and scrolls: screen on top, wheel below.
- **Click wheel:** a full-screen canvas iPod OS (menus, Now Playing, settings, EQ, sleep timer) with tuned wheel physics (velocity smoothing, acceleration, tap zones, hub resync). The wheel also works inside "Player in an iPod" (rotate = move highlight, centre = select, ring taps = Menu/⏮/⏭/⏯).
- **Gestures (configurable):** swipe right anywhere to go back (or left edge only / off); swipe down closes Now Playing; flick the mini player or cover to skip; swipe a track row for Favorite / Play next / Add to queue (each side configurable). No haptics on swipes.
- **Library:** folder-based playlists (folder name = playlist), user playlists, favourites, play history; embedded art extraction with an LRU cache; untagged/voice-memo audio goes to Voice Memos.
- **Android media integration:** background playback via a Media3 `MediaSessionService` (notification, lock screen, headset/media keys), "Open with" for audio files, sleep timer, EQ presets, volume limit.
- **RG Rotate specifics:** adaptive layouts (square, foldable, phone aspect ratios), hinge-deployed fullscreen, gamepad mapping (A select, B back, X play/pause, Y Now Playing, shoulders prev/next; swappable Xbox/Nintendo layout).

## Build & run
Requirements: JDK 17, Android SDK (compileSdk 34), a device with USB debugging.

```bash
./gradlew :app:testDebugUnitTest     # wheel-physics unit tests
./gradlew :app:installDebug          # build + install on the connected device
```
`local.properties` (SDK path) is per-machine and git-ignored. On first launch grant audio access (and notifications on Android 13+); the library scans automatically.

Stack: Kotlin 1.9.24, AGP 8.5.2, Gradle 8.7, minSdk 30 / targetSdk 33, Jetpack Compose (BOM 2024.06, compiler 1.5.14), Media3 1.4.1 (ExoPlayer + MediaSession), androidx Palette, coroutines.

## How the code works
Package `com.ipodemu`:

| Path | Role |
|---|---|
| `App.kt`, `Prefs.kt`, `UiState.kt` | Process-wide singletons: library, player, art cache, user data; SharedPreferences wrapper; observable UI state (`viewMode`, `model`, `colorway`, `dynamicColor`, `pickerOpen`). |
| `ui/MainActivity.kt` | Single activity. Permissions, "Open with" intents, key dispatch (gamepad → `PlayerKeys`, or forwarded to the wheel view), immersive/hinge handling. Hosts `AppRoot`. |
| `player/AppRoot.kt` | Chooses what to show: `PickerScreen`, the wheel `IpodView` (via `AndroidView`) or the Compose Player (`PlayerRoot`, optionally inside `DeviceFrame`). Builds the colour scheme from the model + artwork. |
| `player/PlayerUi.kt`, `NowPlayingUi.kt`, `SettingsUi.kt`, `IpodPicker.kt` | Compose screens. `PlayerNav` is a tiny back-stack (`Screen` sealed interface); `PlayerHost` renders the current screen, mini player, Now Playing overlay, sheets and dialogs. |
| `player/Style.kt` | Theming: `ArtPalette` (Palette → vibrant/dark swatch, cached), `IpodStyle` (per-model tokens), `buildScheme` / `animatedScheme` (dark/light gradient, accent, text; animated fade). |
| `player/Widgets.kt` | Glossy building blocks: `GlossButton/Pill`, `IpodRow`, `SeekBar`, `ActionSheet`, `ArtImage`, custom vector glyphs drawn in `DrawScope`. |
| `player/Gestures.kt` | `edgeSwipeBack` (Initial-pass watcher), `trackSwipe`, `SwipeRow` (configurable row actions), `WheelFocus` (restores highlight after touch). |
| `player/DeviceFrame.kt` | Draws the physical iPod body and scales the Compose UI onto its screen; handles the click ring in Compose. |
| `theme/` | Model catalogue (`Models.kt`: `Themes.models`, colourways, `groups`) and the Canvas rendering used by the wheel UI (`ClassicTheme`, `Nano3Theme`, `MonoGen1Theme`, `WheelChrome`). |
| `ui/IpodView.kt`, `nav/`, `input/WheelPhysics.kt` | The click-wheel iPod OS: custom `View` with its own menu tree (`MenuBuilder`, `Navigator`) and the pure-Kotlin, unit-tested wheel physics. |
| `library/` | `Scanner` (File API with MediaStore fallback, tag/art extraction), `Library` (derived songs/albums/artists/genres/folder playlists, observers), `ArtCache`, `UserData` (favourites, playlists, recents as JSON), `Track`. |
| `playback/` | `PlayerController` (ExoPlayer wrapper: queue, shuffle/repeat, EQ, sleep timer, volume limit, observers) and `PlaybackService` (MediaSession + notification). |

Data flow: `Scanner → Library → Compose screens`; `PlayerController.observe → rememberSnap → UI`; `UiState`/`Prefs` drive which model, view and colours are active. Images are loaded off the main thread and cached (`ArtCache`); the palette for the fade is computed once per cover and cached.

## Known gaps / roadmap
- Performance: release + R8 + profileinstaller (see `tools/jank.sh`); remaining hitches are track-change recomposition and first-open list composition.
- "Remove from playlist" for user playlists is a stub; no A–Z index scroller; shuffle-only iPods aren't included.
- Needs hands-on: wheel feel, gamepad flow end to end, light theme, other aspect ratios.

iPod and related names/appearance belong to Apple Inc.; this is an unaffiliated hobby project, and system fonts stand in for Apple's.
