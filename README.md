<p align="center"><img src="logo/flacie.svg" width="128" alt="FLACie logo"></p>

# FLACie

A music player for Android phones, foldables and handhelds (built on an Anbernic RG Rotate) that streams from
Jellyfin, Plex and a NAS, downloads through Lidarr and Soulseek — and syncs and edits a classic iPod, either plugged
straight into the phone over USB or into a PC.

| Folder | What | Built with |
|---|---|---|
| [`android/`](android/) | **FLACie for Android**: Modern (default) and iPod-style themes, lyrics, account sync, Sync mode for editing an iPod. The iPod engine is built in (`app/src/main/jniLibs`). | Kotlin, Jetpack Compose, Gradle |
| [`ipodsync/`](ipodsync/) | **The iPod engine** (reading, the verified writer, database signatures), the **FLACie desktop app** (Windows) with its installer, a web host, a CLI, and `IpodSync.Engine` — the engine as a native library for the Android app. | C#/.NET 9, MAUI, NativeAOT |
| [`logo/`](logo/) | The FLACie logo (SVG master). | |

## Building

- **Android app:** `cd android && ./gradlew assembleRelease` (JDK 17, Android SDK). See [android/README.md](android/README.md).
- **iPod engine for the Android app** (only when `ipodsync/src/IpodSync.Core` changes): on Linux or WSL,
  `ipodsync/tools/build-engine-android.sh` — rebuilds `libipodsync.so` straight into `android/app/src/main/jniLibs`.
- **Windows desktop app + installer:** `powershell -File ipodsync\tools\build-installer.ps1` → `FLACie-<version>-windows-x64.msi`.
- Safety rules for anything that writes to an iPod: [ipodsync/EDIT-PROTOCOL.md](ipodsync/EDIT-PROTOCOL.md) — back up first, dry-run first,
  verify after, restore on failure, and `tools/fake-root-regression.sh` before changing the write path.

## History

This repository was `ipodplayer` (the Android app); `ipodsync` was merged in with its full history under `ipodsync/`.
Older releases of each live on their original release pages.
