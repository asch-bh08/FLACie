# Symfonium feature inventory

Android-only third-party player for self-hosted and cloud libraries. Paraphrased from public pages. Main source: https://symfonium.app/ ; release notes at https://support.symfonium.app/ and https://symfonium.app/news/

## Playback
- Gapless playback, volume boost, ReplayGain and crossfeed. https://symfonium.app/
- Fades between tracks: "smart" music-aware fades or a fixed crossfade. https://symfonium.app/
- Hi-res PCM, native DSD/DoP, USB DAC output; bit-perfect USB needs Android 14+. https://symfonium.app/
- Equalizer options: AutoEQ profiles, parametric and graphic EQ, 64-bit DSP path. https://symfonium.app/
- Audiobook tools: speed and pitch control, silence skipping, remembered resume points. https://symfonium.app/
- Internet radio, including importing stations from Subsonic-style servers. https://symfonium.app/

## Queue
- Several independent queues (album, playlist, audiobook), each remembering its own speed, shuffle mode and position. https://symfonium.app/
- Smart Queue (beta in 12.1) replaces Continuous Playback, adding tracks by genre, artist or randomness. https://support.symfonium.app/t/version-12-1-0-beta-1/7115

## Offline / downloads
- Manual downloads, or automatic caching of frequently played items, for offline use. https://symfonium.app/
- A playback cache smooths over flaky connections. https://symfonium.app/
- Likes made offline reportedly sync back to Jellyfin later; playlists can be offline-first with manual sync, online-first with auto sync, or read-only. https://forum.jellyfin.org/t-offline-usecase-for-music?pid=32768 (user report)
- One user reported Jellyfin-side favorites needing a manual sync to appear; may be fixed [unconfirmed]. https://support.symfonium.app/t/jellyfin-favorites-not-being-automatically-synced/3945

## Library / browse
- Merges many sources: local files, NAS, SMB, WebDAV, cloud drives, and servers (Plex, Jellyfin, Emby, Navidrome, Subsonic, Kodi, Audiobookshelf). https://symfonium.app/
- File mode to browse raw folders. https://symfonium.app/
- Supplies artist images and bios where the server has none. https://symfonium.app/
- Jellyfin/Emby setting to skip syncing collections (v11.3). https://support.symfonium.app/t/version-11-3-0-beta-1/5242
- Keeps a local database of the library, so browsing works offline. https://forum.jellyfin.org/t-offline-usecase-for-music?pid=32768 (user report)

## Lyrics
- Shows synced, embedded, or provider lyrics; layout adapts to screen size. https://symfonium.app/
- Lyrics view has optional transport controls; a beta option pushes synced lyrics to Bluetooth displays. https://symfonium.app/news/version-3-0-0/ and https://symfonium.app/news/version-4-2-0/
- Font-weight setting for inactive lines. https://support.symfonium.app/t/version-11-4-0-beta-1/5428

## Ratings / stats
- Star ratings and play history usable as smart-playlist filters; no standalone rating UI described. https://symfonium.app/
- Dedicated listening-statistics screens [unconfirmed].

## Radio / mixes / smart playlists
- Filter engine builds auto-updating playlists from rating, play history, genre, year and more. https://symfonium.app/
- Newer filters: display artist, album artist, composer, original release date. https://support.symfonium.app/t/version-12-1-0-beta-1/7115
- "Mix" modes generate a stream from chosen songs, artists, genres, history and likes. https://symfonium.app/
- Playlists sync with the provider. https://symfonium.app/

## Cover / metadata
- Own tag parser: many formats, multi-value tags, custom separators, composer, CUE sheets. https://symfonium.app/
- Artist art and biography fallback. https://symfonium.app/

## Car / cast / devices
- Android Auto: playback, playlist edits and settings, with voice; home rows configurable (e.g. recently added). https://symfonium.app/ and https://support.symfonium.app/t/version-11-3-0-beta-1/5242
- Casts to Chromecast (transcoding when needed), UPnP/DLNA, Kodi, Sonos, and experimentally Plex/Plexamp clients. https://symfonium.app/ and https://support.symfonium.app/t/version-12-1-0-beta-1/7115
- Wear OS companion with phone-free listening; Android TV UI; resizable widgets. https://symfonium.app/

## Design / UX
- Customisable tabs, layouts, shortcuts, Material You theming. https://symfonium.app/
- Community "Styles" catalogue for sharing looks. https://symfonium.app/

## Pricing
- Free trial with all features, then a one-time purchase; no subscription or ads. https://symfonium.app/
- License tied to Google account; needs a periodic (about monthly) online check. https://symfonium.app/
- Price roughly 5 USD per user posts [unconfirmed]. https://alternativeto.net/software/symfonium/about
