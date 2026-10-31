# Third-party software

FLACie's own code is MIT licensed (see [LICENSE](LICENSE)). It uses the libraries below, which keep their own licences.
Full texts are in [licenses/](licenses/). The libraries are not copied into this repository: each is fetched from NuGet, Maven or npm when FLACie is built.
The fonts and pictures further down are in the repository, each with its licence file.

## Libraries under the LGPL

These three are the only open-source libraries in the apps that are not MIT, BSD or Apache. FLACie's source is public, so anyone can rebuild FLACie with a
modified version of any of them (the LGPL's requirement that users can replace the library).

| Library | Used by | Licence | Source |
|---|---|---|---|
| SMBLibrary 1.5.8.1 | FLACie Web and Windows app: browsing a NAS over SMB | LGPL-3.0-or-later ([text](licenses/LGPL-3.0.txt), needs [GPL-3.0](licenses/GPL-3.0.txt)) | https://github.com/TalAloni/SMBLibrary |
| TagLibSharp 2.3.0 | FLACie Web, Windows app and the iPod engine (`libipodsync.so`): reading song tags | LGPL-2.1-only ([text](licenses/LGPL-2.1.txt)) | https://github.com/mono/taglib-sharp |
| jcifs-ng 2.1.10 | Android app: browsing a NAS over SMB | LGPL-2.1 ([text](licenses/LGPL-2.1.txt)) | https://github.com/AgNO3/jcifs-ng |

In the Docker image and the Windows installer SMBLibrary and TagLibSharp are separate `.dll` files that can be swapped. In the Android app
jcifs-ng is compiled into the app (kept whole: the release build does not shrink or rename it), and TagLibSharp is compiled into the native iPod engine;
rebuild from this repository to change them. Retro mode inside the Android app (`assets/retro`, built from `ipodsync/src/FLACie.RetroWasm`) and the separate
FLACie Retro for Android app (`ipodsync/src/FLACie.Hybrid`, released as `FLACie-Retro-*.apk`) also carry SMBLibrary and TagLibSharp, as separate .NET
assemblies (`SMBLibrary.wasm` / `.dll`, `TagLibSharp.wasm` / `.dll`). Each release is built from the matching tag in this repository with the exact versions
above; their source is at the links in the table.

## MIT, BSD and Apache-2.0 libraries

| Library | Licence |
|---|---|
| .NET runtime, ASP.NET Core, Blazor (including `blazor.web.js`, which browsers download from FLACie Web), .NET MAUI, the .NET WebAssembly runtime (Retro mode in the Android apps), Microsoft.Data.Sqlite, Microsoft.Extensions.* | MIT |
| BouncyCastle.Cryptography 2.4.0 (.NET, the iPod engine) | MIT |
| Bouncy Castle for Java, `bcprov-jdk18on` 1.76 (Android app: NAS sign-in, brought in by jcifs-ng) | Bouncy Castle Licence (MIT style) |
| SLF4J API 1.7.36 (Android app, brought in by jcifs-ng) | MIT |
| SQLitePCLRaw (e_sqlite3), SQLite itself | Apache-2.0 (SQLitePCLRaw), public domain (SQLite) |
| OkHttp 4.12, Okio, the Kotlin standard library, AndroidX (Core, Activity, Lifecycle, Palette, ProfileInstaller, Media), Jetpack Compose, Media3 (ExoPlayer, HLS, Session), Guava (brought in by Media3), kotlinx.coroutines | Apache-2.0 ([text](licenses/Apache-2.0.txt)) |
| Guava 33.0.0-android and failureaccess 1.0.2 (FLACie Retro for Android; the build removes the one `ListenableFuture` class from the Guava jar because the app already has it) | Apache-2.0 |
| Microsoft WebView2 SDK loader (Windows app) | BSD-3-Clause style (Microsoft) |
| Open Sans font (Windows app) | Apache-2.0 |

## Microsoft components in the Windows app

The Windows app also ships the Windows App SDK runtime (packaged with the app) and Win2D, which .NET MAUI needs on Windows. These are not open source.
They are Microsoft's redistributable code under the Microsoft Software License Terms for the Windows App SDK and Microsoft's Win2D licence terms (both linked
from their NuGet packages, `Microsoft.WindowsAppSDK` and `Microsoft.Graphics.Win2D`). Those terms, not the MIT licence, cover these files, and installing the
Windows app means accepting them for these components. Among other things they do not allow reverse engineering those Microsoft files.

## Tools used only to build or test (not shipped)

| Tool | Licence |
|---|---|
| Playwright, `@playwright/test` | Apache-2.0 |
| axe-core and `@axe-core/playwright` (accessibility checks in the web smoke test) | MPL-2.0 |
| xUnit, Microsoft.NET.Test.Sdk | Apache-2.0, MIT |
| JUnit 4.13.2 | EPL-1.0 |
| JSON-java (`org.json`, Android unit tests) | Public domain |
| kotlinx-coroutines-test, AndroidX Test | Apache-2.0 |
| Gradle wrapper (`android/gradle/wrapper`) | Apache-2.0 |

## Fonts and pictures (iPod mode, Desk look, the new look)

| Asset | Source | Licence |
|---|---|---|
| Caveat handwriting font (`wwwroot/desk/caveat-*.woff2`) | The Caveat Project Authors, via Fontsource | SIL Open Font License 1.1 ([text](ipodsync/src/FLACie.Server/wwwroot/desk/OFL-caveat.txt)) |
| Oak veneer texture (`wwwroot/desk/oak.jpg`, resized) | Poly Haven, "Oak Veneer 05" | CC0 (public domain) |
| Walnut veneer texture (`wwwroot/retro/walnut.jpg`, resized and tinted warm) | Poly Haven, "American Walnut Veneer" (Jenelle van Heerden) | CC0 (public domain) |
| Linen, plywood, plaster, leather and white oak textures (`wwwroot/retro/linen.jpg`, `plywood.jpg`, `plaster.jpg`, `dash.jpg`, `oak-light.jpg`; resized, some re-toned) | Poly Haven: "Rough Linen", "Plywood", "Beige Wall 001", "Fabric Leather 02", "White Oak Veneer" | CC0 (public domain) |
| Chicago Kare pixel font (`wwwroot/fonts/chicago-kare.woff2`, unmodified, the 1st-4th generation and mini screens: Duane King's re-drawing of the Chicago bitmap face those iPods used) | Duane King, github.com/KingDuane/Chicago-Kare | MIT ([text](ipodsync/src/FLACie.Server/wwwroot/fonts/LICENSE-chicago-kare.txt)) |
| Source Sans 3 (`wwwroot/fonts/source-sans-3.woff2`, Latin subset of the variable font via Fontsource, the 5th generation and first two nanos: Apple's Podium Sans there was drawn from Myriad) | Adobe, Reserved Font Name "Source" | SIL Open Font License 1.1 ([text](ipodsync/src/FLACie.Server/wwwroot/fonts/OFL-source-sans-3.txt)) |
| Arimo (`wwwroot/fonts/arimo.woff2`, Latin subset of the variable font via Fontsource, Helvetica's metric twin: the classic and 3rd-5th nano screens and the touch iPods up to iOS 6) | The Arimo Project Authors | SIL Open Font License 1.1 ([text](ipodsync/src/FLACie.Server/wwwroot/fonts/OFL-arimo.txt)) |
| Inter (`wwwroot/fonts/inter.woff2`, Latin subset of the variable font via Fontsource, standing in for San Francisco on the iOS 7+ screens) | The Inter Project Authors | SIL Open Font License 1.1 ([text](ipodsync/src/FLACie.Server/wwwroot/fonts/OFL-inter.txt)) |
| DotGothic16 dot-matrix font (`wwwroot/fonts/dotgothic16.woff2`, the car and MiniDisc-style displays) | The DotGothic16 Project Authors, via Google Fonts | SIL Open Font License 1.1 ([text](ipodsync/src/FLACie.Server/wwwroot/fonts/OFL-dotgothic16.txt)) |
| DSEG7 Classic Bold segment font (`wwwroot/fonts/dseg7-classic-bold.woff2`, unmodified, the portable CD player's LCD) | keshikan, via the dseg npm package | SIL Open Font License 1.1, Reserved Font Name "DSEG" ([text](ipodsync/src/FLACie.Server/wwwroot/fonts/OFL-dseg.txt)) |
| Archivo (the new look: `wwwroot/fonts/ls/archivo-*.woff2`, subsets via Google Fonts; Android `res/font/archivo_variable.ttf`) | The Archivo Project Authors (Omnibus-Type) | SIL Open Font License 1.1 ([text](android/app/src/main/fonts-licence/OFL-Archivo.txt)) |
| DM Mono (the new look: `wwwroot/fonts/ls/dmmono-*.woff2`; Android `res/font/dm_mono_*.ttf`) | The DM Mono Project Authors | SIL Open Font License 1.1 ([text](android/app/src/main/fonts-licence/OFL-DMMono.txt)) |
| Instrument Serif (the new look: `wwwroot/fonts/ls/instrumentserif-*.woff2`; Android `res/font/instrument_serif_*.ttf`) | The Instrument Serif Project Authors | SIL Open Font License 1.1 ([text](android/app/src/main/fonts-licence/OFL-InstrumentSerif.txt)) |

**Sounds.** FLACie ships no recorded sounds. The click wheel's tick, the record needle's crackle, the DJ's swoosh and the headphone test's tones are all
made by the apps while they run.

## Not bundled

FLACie does **not** ship or link ffmpeg, ffprobe, yt-dlp, slskd, Lidarr or Jellyfin. The Docker image contains Debian, the .NET runtime and FLACie only.

`ipodsync/tools` has the source of three optional helper containers. They are built by whoever runs them; the FLACie project does not publish them as images.
What each one installs keeps its own licence:

| Helper | What it installs | Licences |
|---|---|---|
| `ipodsync/tools/ytdl` (YouTube source, off unless switched on) | yt-dlp, ffmpeg (Debian's build), Python | Unlicense; GPL-2.0-or-later (Debian's ffmpeg); PSF |
| `ipodsync/tools/kokoro` (the AI DJ's own voice) | kokoro-onnx; the Kokoro-82M model and its voices (hexgrad); misaki; espeak-ng (from Debian, and again through espeakng-loader); phonemizer-fork; spaCy and its `en_core_web_sm` model; num2words; lameenc (LAME); ONNX Runtime; NumPy | MIT; Apache-2.0; Apache-2.0; GPL-3.0-or-later; GPL-3.0; MIT; LGPL; LGPL-3.0; MIT; BSD-3-Clause |
| `ipodsync/tools/filemove` (files downloads into the library) | Python only | PSF |

espeak-ng and phonemizer are GPL, so a Kokoro image you build is covered by the GPL-3.0 as a whole if you give it to anyone else (FLACie's own `server.py`
and `sounds.py` in it are MIT, which the GPL allows). The Kokoro model card lists two Creative Commons datasets among its training data: Koniwa `tnc`
(CC BY 3.0) and SIWIS (CC BY 4.0).

## Apple Lossless (ALAC) decoder

`ipodsync/src/FLACie.Core/Alac.cs` is FLACie's own ALAC decoder, written in C# for this project. Apple Lossless is a published format (Apple released its reference
decoder as open source under the Apache-2.0 licence), and the decoding steps (the MP4 layout, the adaptive Golomb-Rice reading, the adaptive predictor and the stereo mixing) are the
format's own, so any decoder, this one included, follows the same algorithm as Apple's and ffmpeg's. No source file of either was copied into FLACie; the output was checked bit for bit
against ffmpeg's on 41 files. FLACie does not run or ship ffmpeg to play ALAC. If you need a stricter provenance guarantee than that for redistribution, have this file reviewed.

## Online services and data

FLACie looks things up on public services while it runs. Nothing from them is stored in this repository. What comes back (names, covers, lyrics) belongs to
its owners; FLACie shows it only to the people signed in to that server and caches it there. Exactly what is sent to each service is in [PRIVACY.md](PRIVACY.md).

| Service | What FLACie uses it for | Terms that apply |
|---|---|---|
| MusicBrainz and the Cover Art Archive | Matching songs to releases, release details, covers and disc pictures | MusicBrainz core data is CC0 (some supplementary data is CC BY-NC-SA 3.0); cover images belong to their rights holders. FLACie keeps to one request a second and sends a FLACie User-Agent. |
| ListenBrainz (labs API) | Similar artists for Autoplay and Quick picks | ListenBrainz data is CC0 |
| Deezer public API | Search for music you don't have, artists' best-known songs, covers | Deezer's API Terms of Use (non-commercial, private family use) |
| Apple's iTunes Search API and iTunes chart feed | Search, covers, the Charts page | Apple's terms for its search and marketing feeds |
| LRCLIB (lrclib.net) | Synced lyrics | LRCLIB's free API; lyrics belong to their songwriters and publishers |
| Internet Archive (Live Music Archive and netlabel collections), Jamendo, Audius | Optional download sources, off until a file mover is set up (Jamendo also needs your own client id) | Each recording's own licence, often Creative Commons; each service's API terms |
| Cloudflare Workers AI (optional, on the server owner's own Cloudflare account) | The AI DJ: Meta's Llama 3.3 70B and Llama 3.1 8B write the lines; Deepgram Aura-1, Aura-2 or MeloTTS speak them; OpenAI's Whisper large-v3-turbo checks how names are said | Cloudflare's terms and each model's own licence: the Llama 3.3 and Llama 3.1 Community Licenses and Meta's Acceptable Use Policy, Deepgram's terms for Aura, MIT for MeloTTS and Whisper |
| Amazon Alexa (optional) | Voice control on Echo speakers | Amazon's Alexa Skills Kit terms |
| ntfy (optional) | Notifications for the server owner | The terms of the ntfy server you choose |

When the AI DJ uses Cloudflare, its lines are written with Meta's Llama models ("Built with Llama"). Llama is a trademark of Meta Platforms, Inc.

## iPod format knowledge

The iPod database and signing code in `ipodsync/src/IpodSync.Core` is an independent implementation written from published format descriptions
and from measurements of real devices, not a port of libgpod (LGPL). The model-number-to-name table in `IpodModels.cs` was compiled using
pypodlib's device matrix and libgpod's device table as references; it contains only model numbers and product names. FLACie does not include
or download Apple's code, keys extracted from Apple's software, or the closed `libhashab` signer; the newest iPods that need it are not supported.

## Trademarks

FLACie is an independent project. It is not affiliated with, endorsed or sponsored by any of the companies below. Their names are used only to say what
FLACie works with, or which classic device a Retro scene is styled after. The players drawn in Retro mode are FLACie's own drawings, made in code; they use no
Apple or Sony logos, photographs, artwork, sounds or fonts.

- "iPod", "iTunes", "Cover Flow", "Apple Music" and "Apple" are trademarks of Apple Inc., registered in the U.S. and other countries.
- "Discman", "MiniDisc" and "Mega Bass" are trademarks of Sony Group Corporation.
- "Android", "Android Auto" and "YouTube" are trademarks of Google LLC.
- "Alexa", "Amazon" and "Echo" are trademarks of Amazon.com, Inc. or its affiliates.
- "Windows" is a trademark of Microsoft Corporation. "Llama" is a trademark of Meta Platforms, Inc.
- "Jellyfin", "Plex", "Soulseek", "Lidarr", "Deezer", "Spotify", "Cloudflare", "Deepgram", "U2" and "(PRODUCT)RED" belong to their respective owners.
- FLAC is the Free Lossless Audio Codec of the Xiph.Org Foundation. FLACie is not made or endorsed by Xiph.Org.
