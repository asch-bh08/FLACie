# External APIs for FLACie (research, 2026-10-13)

Items marked [unconfirmed] were not verifiable on the official page fetched.

## MusicBrainz web service
- Base URL: `https://musicbrainz.org/ws/2/` (add `fmt=json`, or Accept: application/json).
- Calls FLACie would make:
  - Search recording: `/recording?query=recording:"TITLE" AND artist:"ARTIST"&fmt=json&limit=5` (other fields: `isrc:`, `release:`, `rgid:`, `reid:`; Lucene syntax; `dismax=true` escapes raw input).
  - Search release: `/release?query=release:"ALBUM" AND artist:"ARTIST"&fmt=json&limit=5`.
  - Lookup: `/recording/<MBID>?inc=releases+release-groups+artist-credits&fmt=json`; `/release/<MBID>?inc=recordings+release-groups+media&fmt=json`.
- Limits: about 1 request/second per IP on average; exceeding it gets all requests from that IP refused with HTTP 503. Anonymous or generic User-Agents (blank, Java, Python-urllib) are throttled heavily; global ceiling about 300 req/s. Lookups return at most 25 linked entities; limit max 100, default 25.
- Required User-Agent: `AppName/<version> ( contact-url-or-email )`, e.g. `FLACie/1.0-beta59 ( contact@example.com )`.
- Caching: no explicit rules found [unconfirmed]; docs say do not poll for changes or wake at fixed times. Cache results locally by MBID/query and never re-query unchanged tracks.
- Terms: core data is CC0. Free for non-commercial use; commercial use needs a MetaBrainz plan. Supplementary dumps (cover-art tables, stats, edits) are CC BY-NC-SA 3.0.
- Sources: https://musicbrainz.org/doc/MusicBrainz_API , https://musicbrainz.org/doc/MusicBrainz_API/Rate_Limiting , https://musicbrainz.org/doc/MusicBrainz_API/Search , https://musicbrainz.org/doc/MusicBrainz_Database/Download

## Cover Art Archive
- Base URL: `https://coverartarchive.org/`
- Calls:
  - `/release/<MBID>/front` (307 redirect to image; append `-250`, `-500` or `-1200` for thumbnails, e.g. `/release/<MBID>/front-500`).
  - `/release/<MBID>/` JSON list (`images[]` with `image`, `thumbnails`, `types`, `front`, `back`, `approved`, `id`).
  - `/release-group/<MBID>/front` (art taken from a release in the group; fallback when the exact release has none).
- Limits: page says no rate limiting rules currently, yet documents 503 for violations; stay polite. A thumbnail redirect can 404 if that size is absent. 404 means no art; 400 means bad UUID.
- Caching: not stated [unconfirmed]; cache image bytes on disk keyed by MBID and size, and cache negative (404) results.
- Terms: image licensing is not stated on the API page [unconfirmed]; art is user-uploaded and often copyrighted. Treat as display-for-identification, not redistribution. Images are hosted on archive.org (redirect target).
- Source: https://musicbrainz.org/doc/Cover_Art_Archive/API

## LRCLIB
- Base URL: `https://lrclib.net`
- Calls:
  - `GET /api/get?track_name=&artist_name=&album_name=&duration=` (track_name and artist_name required; album_name and duration optional; duration 1 to 3600 s, matched with plus/minus 2 s tolerance).
  - `GET /api/get/<id>` by ID.
  - `GET /api/search?q=...` or `track_name=`, `artist_name=`, `album_name=` (up to 20 results, BM25 ranked; `q` overrides structured fields).
  - `/api/get-cached` is listed in the docs index but its behaviour was not documented in fetched content [unconfirmed].
- Response keys: `id, trackName, artistName, albumName, duration, instrumental, plainLyrics, syncedLyrics, lyricsfile`. `syncedLyrics` is LRC text with `[mm:ss.xx]` line timestamps. Errors: 400, 404 (TrackNotFound), 500, 503 (ServerOverloaded with `Retry-After: 1`).
- Limits and User-Agent: no rate limit published in what was fetched [unconfirmed]. Secondary sources say a User-Agent (app name/version + URL) is recommended [unconfirmed].
- Caching: not stated [unconfirmed]; cache hits and 404s locally per track.
- Terms: no terms page found [unconfirmed]; lyrics are community contributed, and copyright in the underlying lyrics may still apply.
- Sources: https://lrclib.net/docs (page body did not render in fetch), https://context7.com/tranxuanthang/lrclib/llms.txt (mirror of the project docs)

## Deezer public API
- Base URL: `https://api.deezer.com/`
- Calls: `/search/album?q=ALBUM%20ARTIST`, `/search/artist?q=NAME`, `/search?q=artist:"X" album:"Y"`; no key needed for read-only search [unconfirmed on fetched pages].
- Image fields: album has `cover, cover_small, cover_medium, cover_big, cover_xl`; artist has `picture, picture_small, picture_medium, picture_big, picture_xl`. Pixel sizes (commonly 56/250/500/1000) [unconfirmed].
- Limits: commonly cited 50 requests per 5 seconds per IP, with a quota error in the JSON body [unconfirmed].
- Terms (sections IV and VII): use is strictly non-commercial and in a non-commercial environment; the developer may not earn revenue tied to the content; artist photos and album artwork belong to Deezer or rights holders and reproducing them without express authorization is prohibited; access can be changed or removed at any time; caching is not addressed. This is the term most likely to block a feature (artist images / artwork from Deezer).
- Sources: https://developers.deezer.com/termsofuse , https://developers.deezer.com/api (docs pages mostly login-gated in fetch)
