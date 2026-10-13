# Plexamp feature inventory

Plex's music player (mobile, desktop, headless). Plex's own pages returned 403 to automated fetch, so most points come from search snippets and secondary coverage; treat as lightly verified. Release notes: https://forums.plex.tv/t/plexamp-release-notes/221280/72

## Playback
- Gapless playback and loudness leveling are in the free tier. https://plex.tv/blog/free-bird-plexamp-spreads-its-wings-for-every-music-lover
- "Sweet Fades" smooth transitions are free; closest thing to crossfade. https://plex.tv/blog/free-bird-plexamp-spreads-its-wings-for-every-music-lover
- Visualizers and advanced pre-caching of upcoming tracks are free. https://plex.tv/blog/free-bird-plexamp-spreads-its-wings-for-every-music-lover
- Equalizer / DSP details [unconfirmed].

## Queue
- Guest DJ hands the play queue to a themed DJ that keeps adding tracks (Stretch, Contempo, Groupie, Doppelganger); some need sonic analysis. https://forums.plex.tv/t/plexamp-release-notes/221280/72
- Autoplay (paid tier) keeps music going after the queue ends. https://plex.tv/blog/free-bird-plexamp-spreads-its-wings-for-every-music-lover

## Offline / downloads
- Downloads of playlists and stations for off-grid listening; paid tier. https://plex.tv/blog/free-bird-plexamp-spreads-its-wings-for-every-music-lover

## Library / browse
- Works only against a Plex Media Server library. https://plex.tv/music
- Library-restriction quirks reported with sonic stations. https://forums.plex.tv/t/bug-plexamp-sonic-stations-do-not-work-with-library-restrictions/828138.md

## Lyrics
- Lyrics are a paid-tier feature; sources and sync behaviour [unconfirmed]. https://plex.tv/blog/free-bird-plexamp-spreads-its-wings-for-every-music-lover

## Ratings / stats
- Star ratings on tracks; Plex staff say radios and shuffles weight toward higher-rated items. https://forums.plex.tv/t/plexamp-automatic-star-ratings/718693.md
- Rated-track filters can be saved as smart playlists. https://forums.plex.tv/t/play-by-rating/577461.md
- Song vs album rating distinction is discussed by users. https://forums.plex.tv/t/plexamp-song-vs-album-ratings/919625
- "Aural Fixations" summarises most-played tracks, albums and artists. https://forums.plex.tv/t/plexamp-release-notes/221280/72

## Radio / mixes / smart playlists
- Sonic analysis (neural net, about 50 audio parameters) runs on the server, can take hours or days; powers similar tracks, albums and artists. https://techcrunch.com/2021/08/12/plexs-new-feature-matches-your-sonically-similar-music-to-make-playlists
- Track Radio and Album Radio built from sonic neighbours. https://techcrunch.com/2021/08/12/plexs-new-feature-matches-your-sonically-similar-music-to-make-playlists
- Sonic Adventure: pick two tracks, get a playlist travelling between them (paid). https://plex.tv/blog/free-bird-plexamp-spreads-its-wings-for-every-music-lover
- Artist and Album Mix Builders (paid); smart playlists and stations (free). https://plex.tv/blog/free-bird-plexamp-spreads-its-wings-for-every-music-lover
- Sonic Sage: describe music in words, an LLM builds a playlist; needs an OpenAI API key, can be limited to your own library (launch also required TIDAL). https://www.soundandvision.com/content/plexamp-s-sonic-sage-uses-chatgpt-improved-discovery

## Cover / metadata
- Relies on Plex server metadata matching; tag-handling complaints exist. https://forums.plex.tv/t/plexamp-and-mp3-tags/847760
- Album-art-driven colour theming [unconfirmed].

## Car / cast / devices
- CarPlay, Android Auto, Siri, AirPlay and Chromecast supported. https://www.plex.tv/hi/blog/free-bird-plexamp-spreads-its-wings-for-every-music-lover/
- Headless / Raspberry Pi mode [unconfirmed].

## Design / UX
- Winamp-inspired look with strong visual polish. https://entertainment.slashdot.org/story/23/07/18/1728209/plexs-winamp-inspired-music-player-plexamp-is-now-free

## Pricing
- App free since July 2023; Plex Pass unlocks downloads, lyrics, sonic features, Guest DJ, Sonic Sage. https://entertainment.slashdot.org/story/23/07/18/1728209/plexs-winamp-inspired-music-player-plexamp-is-now-free
- Plex Pass reported at about 5 USD/month or 120 USD lifetime [unconfirmed]. https://selfhosting.sh/compare/jellyfin-vs-plex-music/
