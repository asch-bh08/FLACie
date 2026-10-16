# Third-party software

FLACie's own code is MIT licensed (see [LICENSE](LICENSE)). It uses the libraries below, which keep their own licences.
Full texts are in [licenses/](licenses/). Nothing here is copied into this repository: each is fetched from NuGet or Maven when FLACie is built.

## Libraries under the LGPL

These three are the only ones that are not MIT, BSD or Apache. FLACie's source is public, so anyone can rebuild FLACie with a
modified version of any of them (the LGPL's requirement that users can replace the library).

| Library | Used by | Licence | Source |
|---|---|---|---|
| SMBLibrary 1.5.8.1 | FLACie Web and Windows app: browsing a NAS over SMB | LGPL-3.0-or-later ([text](licenses/LGPL-3.0.txt), needs [GPL-3.0](licenses/GPL-3.0.txt)) | https://github.com/TalAloni/SMBLibrary |
| TagLibSharp 2.3.0 | FLACie Web, Windows app and the iPod engine (`libipodsync.so`): reading song tags | LGPL-2.1-only ([text](licenses/LGPL-2.1.txt)) | https://github.com/mono/taglib-sharp |
| jcifs-ng 2.1.10 | Android app: browsing a NAS over SMB | LGPL-2.1 ([text](licenses/LGPL-2.1.txt)) | https://github.com/AgNO3/jcifs-ng |

In the Docker image and the Windows installer SMBLibrary and TagLibSharp are separate `.dll` files that can be swapped. In the Android app
jcifs-ng is compiled into the app, and TagLibSharp is compiled into the native iPod engine; rebuild from this repository to change them.

## MIT, BSD and Apache-2.0 libraries

| Library | Licence |
|---|---|
| .NET runtime, ASP.NET Core, Blazor, .NET MAUI, Microsoft.Data.Sqlite, Microsoft.Extensions.* | MIT |
| BouncyCastle.Cryptography 2.4.0 | MIT |
| SQLitePCLRaw (e_sqlite3), SQLite itself | Apache-2.0 (SQLitePCLRaw), public domain (SQLite) |
| OkHttp 4.12, AndroidX (Core, Activity, Lifecycle, Palette, ProfileInstaller), Jetpack Compose, Media3 (ExoPlayer, HLS, Session), kotlinx.coroutines | Apache-2.0 ([text](licenses/Apache-2.0.txt)) |
| Open Sans font (Windows app) | Apache-2.0 |
| Playwright (tests only, not shipped) | Apache-2.0 |

## Fonts and pictures (iPod mode, Desk look)

| Asset | Source | Licence |
|---|---|---|
| Caveat handwriting font (`wwwroot/desk/caveat-*.woff2`) | The Caveat Project Authors, via Fontsource | SIL Open Font License 1.1 ([text](ipodsync/src/FLACie.Server/wwwroot/desk/OFL-caveat.txt)) |
| Oak veneer texture (`wwwroot/desk/oak.jpg`, resized) | Poly Haven, "Oak Veneer 05" | CC0 (public domain) |
| Pixelify Sans pixel font (`wwwroot/fonts/pixelify-sans.woff2`, the early iPods' screens) | The Pixelify Sans Project Authors, via Google Fonts | SIL Open Font License 1.1 ([text](ipodsync/src/FLACie.Server/wwwroot/fonts/OFL-pixelify.txt)) |
| DotGothic16 dot-matrix font (`wwwroot/fonts/dotgothic16.woff2`, the car and MiniDisc displays) | The DotGothic16 Project Authors, via Google Fonts | SIL Open Font License 1.1 ([text](ipodsync/src/FLACie.Server/wwwroot/fonts/OFL-dotgothic16.txt)) |
| DSEG7 Classic Bold segment font (`wwwroot/fonts/dseg7-classic-bold.woff2`, unmodified, the Discman's LCD) | keshikan, via the dseg npm package | SIL Open Font License 1.1, Reserved Font Name "DSEG" ([text](ipodsync/src/FLACie.Server/wwwroot/fonts/OFL-dseg.txt)) |

## Not bundled

FLACie does **not** ship or link ffmpeg, ffprobe, yt-dlp, slskd, Lidarr or Jellyfin. If you install them yourself (the optional download
services in `ipodsync/tools/ytdl` use ffmpeg and yt-dlp in their own container) their licences (LGPL/GPL and Unlicense) apply to that container, not to FLACie.
The Docker image contains Debian, the .NET runtime and FLACie only.

## Apple Lossless (ALAC) decoder

`ipodsync/src/FLACie.Core/Alac.cs` is FLACie's own ALAC decoder, written in C# for this project. Apple Lossless is a published format (Apple released its reference
decoder as open source under the Apache-2.0 licence), and the decoding steps (the MP4 layout, the adaptive Golomb-Rice reading, the adaptive predictor and the stereo mixing) are the
format's own, so any decoder, this one included, follows the same algorithm as Apple's and ffmpeg's. No source file of either was copied into FLACie; the output was checked bit for bit
against ffmpeg's on 41 files. FLACie does not run or ship ffmpeg to play ALAC. If you need a stricter provenance guarantee than that for redistribution, have this file reviewed.

## Online catalog data

Quick picks and Autoplay look up the best-known songs and similar artists of the artists you play from Deezer's public API, MusicBrainz and ListenBrainz. Only artist names are sent.

## iPod format knowledge

The iPod database and signing code in `ipodsync/src/IpodSync.Core` is an independent implementation written from published format descriptions
and from measurements of real devices, not a port of libgpod (LGPL). The model-number-to-name table in `IpodModels.cs` was compiled using
pypodlib's device matrix and libgpod's device table as references; it contains only model numbers and product names.
"iPod" and "iTunes" are trademarks of Apple Inc.; FLACie is not affiliated with or endorsed by Apple.
