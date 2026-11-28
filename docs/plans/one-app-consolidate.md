# ONE APP: consolidate (owner, 28 Nov: "stop treating FLACie as 7 apps")

Feature freeze: no new features, no new design. The look is locked; Now Playing, Home and Devices are the reference. This is the inventory
of every screen, overlay and player on the web and the phone, what each duplicates, and the page type each becomes in the one frame.

## The four page types (the frame owns these; a page only lays out its content and declares its type)
| Type | Has | Closes / goes back by |
|---|---|---|
| **Standard** | top bar: back (top left, one icon) · serif title · at most two actions · account; the tab bar (Home, Search, Library, Devices, Settings); the mini player | back button, swipe right from the left edge (phone), browser back (web) |
| **Full screen** | nothing of the frame: no top bar, no tab bar, no mini player; a close button top left | close, swipe down |
| **Sheet** | slides up over the page, a handle on top; centred on a PC | swipe down, tap outside, Escape |
| **Plain** | nothing but the content (sign-in, first-run setup) | its own buttons |

Outside the frame, untouched: the **iPod** (wheel) theme and **Retro** (FLACie.Retro, /retro, /ipod, the desk): separate themes, no parity, no new features.

## Web (FLACie.App + FLACie.Server pages; the Windows app hosts the same pages)
| Screen | Route | Today | Duplicates | Becomes |
|---|---|---|---|---|
| Home | / | own hero header, no top-bar title | - | Standard (root of Home; no back) |
| Search | /search | own page-title | Explore's browse | Standard (root of Search) |
| Explore overview | /explore | own header + tabs | Search with nothing typed already lists the same | Standard under Search (kept: the overview and Charts tab; see "kept" below) |
| Songs, Albums, Artists, Genres | /songs /albums /artists /genres | "Explore" header + MusicTabs | the phone's Library > Songs/Albums/... | Standard under Library |
| Charts | /charts | own header | - (web only) | Standard under Search |
| Library | /library | own k-title header | phone Library tab | Standard (root of Library) |
| Favourites | /favorites | playlist hero | phone Detail(FAVORITES) | Standard under Library (hero stays as content) |
| Playlists | /playlists | own header + new bar | phone Lib(PLAYLISTS) | Standard under Library |
| Playlist, Edit playlist | /playlist/{id}, /edit | hero / own back | phone Detail(USER), EditPlaylist | Standard under Library |
| Made for you / outside list | /made/{id} | detail hero | phone Made | Standard under Library |
| Album, Artist, Genre | /album /artist /genre | detail hero | phone Detail(ALBUM/ARTIST/GENRE) | Standard (tab of where it was opened from) |
| Devices | /devices | own k-title header | phone Devices overlay | Standard (root of Devices) |
| Jam | /jam | .sp header | phone Jam overlay | Standard under Devices |
| Settings | /settings | .sp header + sub-nav | phone Settings | Standard (root of Settings) |
| Account | /account | .sp hero | phone Account overlay | Standard under Settings |
| Downloads log | /downloads | .sp header | phone Downloads overlay | Standard under Settings (admins) |
| Server admin | /dashboard, /admin | .sp header | phone Screen.Dashboard | Standard under Settings (admins) |
| Import, its downloads | /import, /imports/{id}/downloads | .sp header | - (web only) | Standard under Library |
| Headphone test | /headphones | own header | phone Headphones overlay | Standard under Settings |
| Shared link | /s/{id} | own page | - | Standard |
| Not found, error | /not-found, /error | own page | - | Standard / Plain |
| Sign in | /login | own card | phone LoginScreen | Plain |
| Car mode | /car | inside the frame (rail, top bar, bar) | phone Car overlay | **Full screen** |
| Now Playing | overlay (NowPlaying.razor) | full screen | phone LivingSleeve | Full screen (reference) |
| Another device's Now Playing | overlay (RemoteNowPlaying) | full screen | phone RemoteScreen | Full screen |
| Lyrics, Up next, Info | panes of Now Playing (sheet on a phone width) | - | phone panes | Now Playing panes (they use its controls) |
| Full-screen graph | graph-box.full / :fullscreen | own close ✕, own transport | phone FullGraph dialog | Full screen (close top left, swipe down) |
| Player bar | PlayerBar.razor | on every frame page | phone MiniPlayer | the frame's mini player |
| Mini player (picture in picture) | mini-player.css | separate window | - | stays (it is the system's window, not a page) |
| DJ bar, DJ caption | DjBar, DjCaption | over the page | phone DjCaptionCard | part of the frame / Now Playing |
| Song menu, More, account menu (More), playlist picker, confirms, dropdowns | popovers (bottom sheets at phone width since 1f) | phone sheets | **Sheet** |
| Windows app pages | FLACie.Hybrid /settings /signin /signout | own | the web's Settings and sign-in | Settings = Standard, sign-in/out = Plain |
| Retro, iPod desk | /retro, /ipod (FLACie.Retro) | own world | phone wheel theme | outside the frame, untouched |
| Old sync dashboard | IpodSync.Web / IpodSync.Shared "/" | separate app | - | outside FLACie (left as is) |

## Phone (android/app/.../player)
Screens in the nav stack (`Screen`, PlayerUi.kt ScreenContent):
| Screen | Today | Duplicates | Becomes |
|---|---|---|---|
| Home | own hero header | web Home | Standard (root) |
| Search | TopBar "Search your music" | web Search | Standard (root) |
| Sources (Library tab) | own Title header | web /library | Standard (root of Library) |
| Library (Explore) | ExploreScreen | web /explore | Standard under Search |
| Lib(SONGS / ALBUMS / ARTISTS / GENRES / PLAYLISTS) | TopBar | web /songs ... | Standard under Library |
| Lib(JELLYFIN / PLEX / NAS) | separate song lists per source | Songs with a source filter (SourceFilterChips already on Songs) | **folded into Songs** with its source chip on |
| Lib(MEMOS) | voice memos | - | Standard under Library (phone only: the web has no memos) |
| Detail(ALBUM / ARTIST / GENRE / FOLDER / USER / FAVORITES / RECENT / MIX / DOWNLOADS) | DetailHeader / PlaylistHeader | web detail pages | Standard |
| EditPlaylist | own top bar | web /playlist/{id}/edit | Standard |
| Made, CatalogSong, CatalogAlbum | own headers | web /made, Search's catalog | Standard |
| Queue | own header | Now Playing's Up next pane | Now Playing pane (Queue screen kept for the iPod theme only) |
| Settings, Dashboard, Legal, Offline, Downloads (kept songs) | TopBar / SetPanel | web /settings, /dashboard | Standard under Settings |
| Music (MusicMenu) | the iPod theme's menu | - | iPod theme, untouched |

Overlays (AppRoot.kt, drawn **outside** PlayerRoot: no tab bar, no mini player, each its own header):
| Overlay | Duplicates | Becomes |
|---|---|---|
| Devices (`ui.devicesOpen`; the Devices tab opens it) | web /devices | **Standard page** (Screen.Devices, root of the Devices tab) |
| Jam (`ui.jamOpen`) | web /jam | Standard under Devices |
| Account (`ui.accountOpen`) | web /account | Standard under Settings |
| Download log (`ui.downloadsOpen`) | web /downloads | Standard under Settings |
| Headphone test (`ui.headphonesOpen`) | web /headphones | Standard under Settings |
| Jellyfin / Plex / NAS / Lidarr / iPod sync setup | web Settings > Library (admins) | **Plain** (setup) |
| Login, permission reason | web /login | Plain |
| Car (`ui.carOpen`) | web /car | **Full screen** |
| Remote (another device's Now Playing) | web RemoteNowPlaying | Full screen |
| Picker (iPod model) | - | iPod theme, untouched |
| SyncModeScreen (the phone as an iPod-sync dock) | - | outside the frame (a mode, not a page) |

Players: Living Sleeve (Now Playing, Full screen, reference); NowPlayingUi (the iPod theme's player, untouched); the wheel (IpodView, untouched);
FullGraph (Info's graph full screen, Full screen); UpNextDrawer (iPod theme); Android Auto (its own car rules, outside the frame).

## Consolidation
- **Jellyfin, NAS, Plex and local are one library with source tags.** Already one Library in both apps; what is left: the phone's three
  per-source song lists (Lib JELLYFIN / PLEX / NAS) become Songs with that source's chip on, and the Library tab's source buttons open that.
- **Lyrics and graphs are Now Playing panes using its controls** (done on both by 1g-2; the full-screen graph is the Full screen type).
- **Car mode is a Full screen page** on both (the web's /car leaves the frame).
- **iPod and Retro** stay separate themes outside the frame.

## Kept although they look like duplicates (removing them would remove a feature; listed, not deleted)
- Explore overview (/explore, phone Screen.Library): Search with nothing typed shows most of it, but not the Charts tab and the
  four-way overview; kept under Search.
- Phone Queue screen: the iPod theme's Up next (the modern theme uses the Now Playing pane).
- Phone voice memos (Lib MEMOS): phone only.
- Phone Recent (Detail RECENT): the web has no Recent page (Home's "Listen again" shelf is close but not the same list).
- Web Charts, Import, Shared link: web only (they need the server).
- The old sync dashboard (IpodSync.Web): a separate tool, not FLACie's player.
