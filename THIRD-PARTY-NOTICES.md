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

## Not bundled

FLACie does **not** ship or link ffmpeg, ffprobe, yt-dlp, slskd, Lidarr or Jellyfin. If you install them yourself (the optional download
services in `ipodsync/tools/ytdl` use ffmpeg and yt-dlp in their own container) their licences (LGPL/GPL and Unlicense) apply to that container, not to FLACie.
The Docker image contains Debian, the .NET runtime and FLACie only.

## iPod format knowledge

The iPod database and signing code in `ipodsync/src/IpodSync.Core` is an independent implementation written from published format descriptions
and from measurements of real devices, not a port of libgpod (LGPL). The model-number-to-name table in `IpodModels.cs` was compiled using
pypodlib's device matrix and libgpod's device table as references; it contains only model numbers and product names.
"iPod" and "iTunes" are trademarks of Apple Inc.; FLACie is not affiliated with or endorsed by Apple.
