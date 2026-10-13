# Gap table: FLACie against Plexamp and Symfonium (research, 2026-10-13)

Sources: `symfonium.md`, `plexamp.md` (public docs and press; Plex's own pages returned 403 to the research agent, so Plexamp rows rest on secondary sources and are marked "~"). "FLACie today" is as of beta 59 plus the unreleased branch `feature/phase1a`. Effort S/M/L = days / 1-2 weeks / weeks. Value = my judgement for a Jellyfin-first listener. Scope: A = Android, W = web, X = Windows (the Windows app embeds the web UI, so W gives X).

| Feature | Plexamp | Symfonium | FLACie today | Effort | Value | Scope | Phase |
|---|---|---|---|---|---|---|---|
| Offline downloads (manual + auto) | yes | yes (offline-first) | partial: downloads exist only via the slskd/Lidarr acquisition path, no "make available offline" from Jellyfin | L | high | A, W (Service Worker), X (disk) | 1b |
| Resumable download queue, Wi-Fi only, storage manager | yes | yes | no | M | high | A first | 1b |
| Playback speed 0.5-3x, per-item memory | ~yes | yes | **new in feature/phase1a** (A, W) | S | medium (spoken word) | A, W, X | 1a |
| Sleep timer: end of track/album, fade | ~yes | yes | **new in feature/phase1a** (was minutes only) | S | medium | A, W, X | 1a |
| Media session: current item only, custom actions | n/a | yes | beta 59 (item only); favourite + speed buttons in feature/phase1a | S | high (Bluetooth OOM) | A | 1a |
| Android Auto | yes | yes (+ voice) | no | L | high for drivers | A | 1e |
| Chromecast | yes | yes | no | M | medium | A | 1e |
| Crossfade / gapless | yes | yes | gapless yes; crossfade no | M | medium | A, W | 1c |
| Loudness normalisation (ReplayGain / R128) | yes | yes | no | M | high | A, W | 1c |
| Parametric EQ, per-device profiles | ~ | yes | basic presets (ToneEngine) | M | medium | A | 1c |
| Synced lyrics (embedded, LRC, LRCLIB) | yes | yes | lyrics panel exists (embedded); LRCLIB not wired | S-M | medium | A, W | 1d |
| Ratings (stars), play counts, stats | yes | yes | favourites + play counts; no stars | M | medium | A, W | 1d |
| Smart playlists / mix builder | yes (radios, mixes) | yes | no | L | high | A, W | 3 |
| Radio / similarity (artist, album, genre, decade) | yes (sonic, server-side) | ~ (mixes) | Explore + autoplay planner | L | high | A, W | 3 |
| Multiple queues / save queue | ~ | yes | one queue, saved lists | M | low-medium | A, W | later |
| MusicBrainz match + "fix this match" | ~ | no | no | M | medium | W first | 2 |
| Cover picker (embedded, folder, CAA, upload) | ~ | yes (art sources) | catalog lookup (Deezer/iTunes), vetting; no picker | M | medium | A, W | 2 |
| Library browse, search, filters, A-Z index | yes | yes | yes | - | - | - | done |
| Home shelves, quick picks, saved-snapshot cold start | yes | yes | yes (beta 56/57) | - | - | - | done |
| Multi-server / multi-source | Plex only | Jellyfin, Plex, Emby, Subsonic, local | Jellyfin + NAS + Plex + web | - | - | - | done (breadth) |
| Offline-first library DB | ~ | yes | cached list, not a full offline DB | L | medium | A | with 1b |
| Widgets, Wear OS, car mode | yes (some) | yes (widgets, Wear) | no | M | low-medium | A | later |
| Visual polish / motion language | strong | strong | mixed; specs written (player, iPod) | L | high | A, W | 4 |
| Pricing model | free app, Plex Pass for some | paid | free, open | - | - | - | - |

## Reading it
- FLACie's real gaps against both are **offline**, **Android Auto/Cast**, **loudness + crossfade**, and **smart mixes**. Its strengths are Jellyfin-first breadth, the web + Windows pair, the cover vetting, and the cold-start work.
- Plexamp's sonic radios depend on Plex's server-side audio analysis; Jellyfin has no equivalent, so FLACie's radio would be tag/similar-items based (Phase 3), not sonic.
- Items marked "~" are from secondary sources and need a first-hand look before they decide anything.
