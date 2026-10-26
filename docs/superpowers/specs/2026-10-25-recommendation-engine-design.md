# Recommendation engine (per account) — design

Date: 2026-10-25. Status: draft for the owner's review. Follow-on project: the AI DJ (its own spec, after this one).

## Why

The owner's verdict on today's picks: Autoplay plays songs that don't fit (energy, genre/era, and too far from what they know), and Home and
the mixes feel random or stale rather than like their own taste. The goal is Spotify / YouTube Music quality: about half familiar, half new.

Today: Android `Recommend.kt` scores artists and genres from play counts, favourites and recents with a seeded shuffle; `Autoplay.kt` takes
online "related" songs (WebCatalog) and pads from the library. The web (`FLACie.Core/Home.cs`) has its own copy. Nothing uses skips, nothing
measures what flows into what, and each app decides on its own.

## Decisions (from the owner)

- One engine on the FLACie server (frank), used by every app: Android, web, Windows, car, Alexa, and later the AI DJ (approach 1), built so
  a trained model can be added later (approach 3).
- Everything belongs to the **account**, not the device: a person's taste follows them everywhere; family members never mix.
- It learns from: skips and finishes, hearts, a thumbs-down, "less like this", and the playlists the person plays from (see Shared playlists).
- About half familiar, half new ("new" = not in the library or never played; fetched ahead as Autoplay does today).
- What fits next is decided by **vibe** (energy, tempo, danceability, mood), not genre: two songs the person likes may sit side by side across
  genres when the vibe matches (the owner's example: The Prodigy "Out of Space" then David Guetta "Sexy Bitch"). Songs the person has never
  heard stay closer to the genre and artists of what is playing.
- Playing a playlist plays all of it; Autoplay only adds after its last song (as today: when at most one song follows the current one).

## 1. What is recorded

Per account, on the server, one event per line: play (with how much was heard: skipped under 30 s, part, finished, replayed), heart / un-heart,
thumbs-down, less-like-this, playlist add / remove. Every client sends events to the server with the signed-in account; the phone keeps them
while offline and sends them later (retry until it works, like the account sync). One file per person next to `data/users/`; events older than a
year are thinned to daily totals.

Seeded on first run from what exists: Jellyfin play counts and favourites, FLACie's play history (weeks of it for the owner), and playlists.

## 2. How a song is scored

For a candidate song, given the last few songs played (the "session") and the person's profile:

1. **Vibe fit** with the session: energy, tempo, danceability, mood. Energy and loudness come from the waveform analysis FLACie already runs per
   song; tempo (BPM) is added to the same pass. Mood and genre from tags and online data.
2. **Taste**: how much the person plays, finishes, hearts and playlists this song, its artist and close artists; recent listening counts more.
3. **Goes together**: online "fans of X also like Y" for artists and songs (cached on the server), plus songs that share the person's playlists.
4. **Penalties**: skips and thumbs-down push a song and its close relatives down; less-like-this pushes down that artist or style in that
   context; heard in the last few hours, or the same artist as the last song, goes down.

Rules on top: the person's liked songs can cross genres when the vibe matches; never-heard songs need a genre or artist link to the session as
well as the vibe. The top of the list is then blended half familiar, half new.

## 3. Where it is used

- **Autoplay**: asks the engine when the queue runs low. A skip steers the next picks within the session.
- **Home**, same rows as now, filled per person: Quick picks (now: time of day and recent listening), Listen again, Daily Mixes (one per main
  taste, each held together by vibe), Discover (half not in the library), Albums for you. Rows change through the day, never while on screen.
- **Thumbs-down and "Less like this"** in every song menu (Android, web, Windows); thumbs-down takes the song out of the current list at once.
- **Car, Alexa, the AI DJ** ask the same engine.
- **Stage 3**: once a few weeks of family listening exist, a small model trained overnight on frank learns which songs go together (including
  each person's own cross-genre pairings) and replaces the online "also like" data for songs in the library. Nothing else changes.

## 4. Making sure it is better

- **Offline test on real history**: hide what the person actually played next and measure how often the engine ranks it near the top, against
  today's Autoplay. The new engine ships only when it clearly wins.
- **Rails**: never the same song within a few hours, never the same artist twice running, never a thumbed-down song or (with the setting on)
  explicit songs, never cuts into a playlist or a song the person picked.
- **Speed**: a pick in under 0.2 s on the server; online data cached so there is no wait between songs. With no signal the phone uses a simpler
  local version.
- **"Why this song?"** in a song's Info ("Same energy as Out of Space · you play The Prodigy a lot").

## Shared playlists

The family's playlists are shared ("public"): on Jellyfin all seven audio playlists (2026, AA, aura, Golden Era Mix, HoodTrap, House Music, pop)
sit on the Dad account, but Dad listens to AA only and the owner to all of them. Who owns or can see a playlist is **not changed**. A playlist
counts towards a person's taste by what that person plays from it (plays, finishes, skips), not by who owns it: AA for Dad, all seven for the
owner.

## Not in this project

The AI DJ (spoken links between songs, requests like "play something chill"): its own spec next, using this engine.
