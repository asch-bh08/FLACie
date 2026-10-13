# Gap table: FLACie against Plexamp and Symfonium (research, 2026-10-13)

Sources: `symfonium.md`, `plexamp.md` (public docs and press; Plex's own pages returned 403 to the research agent, so Plexamp rows rest on secondary sources and are marked "~"). "FLACie today" is as of beta 59 plus the unreleased branch `feature/phase1a`. Effort S/M/L = days / 1-2 weeks / weeks. Value = my judgement for a Jellyfin-first listener. Scope: A = Android, W = web, X = Windows (the Windows app embeds the web UI, so W gives X).

| Feature | Plexamp | Symfonium | FLACie today | Effort | Value | Scope | Phase |
|---|---|---|---|---|---|---|---|
| Offline downloads (manual + auto) | yes | yes (offline-first) | **Android: built in beta 60** (manual + favourites, resumable, Wi-Fi only). Web/Windows: not applicable (Blazor Server; Windows keeps a disk cache) | L | high | A | 1b |
| Resumable download queue, Wi-Fi only, storage manager | yes | yes | **Android: built in beta 60** | M | high | A | 1b |
| Playback speed 0.5-3x, per-item memory | ~yes | yes | **beta 60** (A, W; Windows via the web UI) | S | medium (spoken word) | A, W, X | 1a |
| Sleep timer: end of track/album, fade | ~yes | yes | **beta 60** (A, W) | S | medium | A, W, X | 1a |
| Media session: current item only, custom actions | n/a | yes | beta 59 (item only); beta 60 adds favourite + speed buttons | S | high (Bluetooth OOM) | A | 1a |
| Android Auto | yes | yes (+ voice) | no | L | high for drivers | A | 1e |
| Chromecast | yes | yes | no | M | medium | A | 1e |
| Crossfade / gapless | yes | yes | gapless yes; crossfade no | M | medium | A, W | 1c |
| Loudness normalisation (ReplayGain / R128) | yes | yes | **beta 61**: Jellyfin NormalizationGain, off by default (A, W); not tried against a real Jellyfin | M | high | A, W | 1c |
| Parametric EQ, per-device profiles | ~ | yes | basic presets (ToneEngine) | M | medium | A | 1c |
| Synced lyrics (embedded, LRC, LRCLIB) | yes | yes | **built before beta 60** (Jellyfin lyrics, sidecar .lrc, LRCLIB; Android and web) | - | - | A, W | done |
| Ratings (stars), play counts, stats | yes | yes | favourites + play counts; no stars | M | medium | A, W | 1d |
| Smart playlists / mix builder | yes (radios, mixes) | yes | no | L | high | A, W | 3 |
| Radio / similarity (artist, album, genre, decade) | yes (sonic, server-side) | ~ (mixes) | Explore + autoplay planner | L | high | A, W | 3 |
| Multiple queues / save queue | ~ | yes | one queue, saved lists | M | low-medium | A, W | later |
| MusicBrainz match + "fix this match" | ~ | no | **beta 61 (web)**: Info panel match you choose, own 1 req/s gate, cache; Android not built | M | medium | W first | 2 |
| Cover picker (embedded, folder, CAA, upload) | ~ | yes (art sources) | catalog lookup (Deezer/iTunes), vetting; no picker | M | medium | A, W | 2 |
| Library browse, search, filters, A-Z index | yes | yes | yes | - | - | - | done |
| Home shelves, quick picks, saved-snapshot cold start | yes | yes | yes (beta 56/57) | - | - | - | done |
| Multi-server / multi-source | Plex only | Jellyfin, Plex, Emby, Subsonic, local | Jellyfin + NAS + Plex + web | - | - | - | done (breadth) |
| Offline-first library DB | ~ | yes | cached list, not a full offline DB | L | medium | A | with 1b |
| Widgets, Wear OS, car mode | yes (some) | yes (widgets, Wear) | no | M | low-medium | A | later |
| Visual polish / motion language | strong | strong | beta 60: new seek bar + play/pause morph (A), seek bar CSS (W), wheel inertia + label contrast; the rest of the specs (player, iPod) not built | L | high | A, W | 4 |
| Pricing model | free app, Plex Pass for some | paid | free, open | - | - | - | - |

## Reading it
- FLACie's real gaps against both are **offline**, **Android Auto/Cast**, **loudness + crossfade**, and **smart mixes**. Its strengths are Jellyfin-first breadth, the web + Windows pair, the cover vetting, and the cold-start work.
- Plexamp's sonic radios depend on Plex's server-side audio analysis; Jellyfin has no equivalent, so FLACie's radio would be tag/similar-items based (Phase 3), not sonic.
- Items marked "~" are from secondary sources and need a first-hand look before they decide anything.
