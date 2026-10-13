# Android Auto / head unit test (beta 62 build)

**What could and could not be tested.** The real Android Auto cannot run on the emulator here: the image carries only `AndroidAutoStubPrebuilt` (a stub, `1.2.531830-stub`) that needs Google Play to become the real app, and the Desktop Head Unit (installed from the SDK: `extras;google;auto`) needs the real app's head-unit server. The old "Media Browser Simulator" APK targets SDK 21 and Android 14 refuses to install it. So the test is a separate app, `:autosim` (package `com.ipodemu.autosim`, its own uid, `android/autosim`), that connects to FLACie's media library service the way a head unit does and logs what it sees (`adb logcat -s FLACieAutoSim`).

**Result on the emulator (mock Jellyfin, 303 songs):**
- connected from another uid (10193); root `root`; folders Recently played, Favourites, Playlists, Albums, Artists, Downloaded;
- Albums 20 items (browsable + playable), Artists 20; search "song 1": 10 results; an album's songs listed;
- covers read through the content provider by the other app: 13 of 13 decoded as PNG/JPEG (the first look found none on disk, as designed: the browse call fetches them for the next look);
- play from a browse item: playing, title/artist right; next works; custom command (speed) accepted; custom layout `[Favourite, Speed 1.25x]`; pause works.

**Not covered:** the real Android Auto UI (templates, driver-distraction limits, voice), the Desktop Head Unit, a car. To run the DHU: a phone with the real Android Auto app, developer settings > start head unit server, `adb forward tcp:5277 tcp:5277`, `desktop-head-unit.exe` from `%LOCALAPPDATA%\Android\Sdk\extras\google\auto`.
