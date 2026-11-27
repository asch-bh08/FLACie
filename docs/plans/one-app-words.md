# ONE APP: the words (part 0c)

The one list of words people read for the common actions. The web (`ipodsync/src/FLACie.App`, `FLACie.Server` razor and cs) and the phone
(`android/app/src/main/java/com/ipodemu/player/*.kt` and the `library/` files that make messages) say these, the same, everywhere: labels, menu
items, `aria-label` / `contentDescription`, toasts, confirms. Code identifiers, routes (`/favorites`), ids, logs and test hooks keep their names.

Two tests keep it so (they fail on the banned spellings in user text, and ignore comments, identifiers, routes and logs):
- web: `ipodsync/tests/FLACie.Tests/WordsTests.cs` (scans every `.razor` in FLACie.App and FLACie.Server, plus the cs files that make toasts and download progress)
- phone: `android/app/src/test/java/com/ipodemu/player/WordsTest.kt` (scans the string literals of `player/*.kt` and the message files in `library/`)

## Spelling rules
| Rule | Say | Never |
|---|---|---|
| Favourites | Favourites, Add to favourites, Remove from favourites | Favorites, Favorite, Unfavorite (in user text; `/favorites`, `IsFavorite`, `Favorites.razor` stay) |
| Ellipsis | the one character `…` (on anything that opens more, and on progress words: Searching Soulseek…) | `...` |
| Quotes | curly `“ ”` around a name | straight `"` in user text |
| Download | Download, Remove download | Download for offline, Save for offline |
| Apostrophe | `'` as in won't, can't | (not banned) |

## The song menu (both sides, this order)
Web: `Components/Shared/SongMenu.razor` (used by every song list: TrackList, Songs, the Search top result). Phone: `openTrackSheet` in `PlayerUi.kt`
(and the Now Playing sheet, `PlayerMoreSheet` in `LivingSleeve.kt`, which lists More…'s four flat because it has room).

| # | Item | Notes |
|---|---|---|
| 1 | Play next | toast: `“t” plays next` |
| 2 | Add to queue | toast: `Added “t” to the queue` |
| 3 | Add to playlist… | opens the playlists (web: inline list "Add to <name>", phone: the picker sheet); toast: `Added “t” to <name>` with Undo; `“t” is already in <name>` when it was there |
| 4 | Add to favourites / Remove from favourites | toast: `Added “t” to favourites` / `Removed “t” from favourites` |
| 5 | Go to artist | |
| 6 | Go to album | only when the song has an album |
| 7 | Share | |
| 8 | Download / Remove download | phone, Jellyfin songs: Download, then Remove download (Cancel download while it runs, Download again after a failure). Web: Download saves the file; a browser keeps nothing offline, so there is no Remove download there |
| 9 | (on a playlist only) Move up · Move down · Remove from playlist | toast: `Removed “t” from <name>` with Undo |
| 10 | Not for me… | Less like this (`Fewer songs like “t”`), Never pick this song (`“t” won't be picked for you again`) |
| 11 | More… | Rate… · The DJ and this song… · ✦ Hi-Res version… (administrators) · Fix the match… (web adds Select songs, last) |

Inside More…: Rate… opens the five stars (Clear the rating); The DJ and this song… has DJ: always introduce this song / DJ: never talk over this
song / DJ: default (toasts `The DJ will always introduce “t”`, `The DJ won't talk over “t”`, `The DJ treats “t” as usual`); ✦ Hi-Res version… asks
`Get a Hi-Res copy?` with Download Hi-Res / Cancel; Fix the match… is the MusicBrainz match (Find on MusicBrainz, Change…, Forget this match).

Where the sides still differ on purpose: the web shows a list of playlists inline under Add to playlist… (the phone has a picker sheet with
New playlist… first); the phone's Now Playing sheet puts Rate… and the rest flat instead of behind More…; Select songs is web-only.

## Confirms
`<Verb> “name”?` with the buttons `<Verb>` and `Keep it` (Cancel only for things that are not losses, like Download Hi-Res).

| Question | Buttons |
|---|---|
| Delete “Road trip”? | Delete / Keep it |
| Clear all favourites? | Clear all / Keep them |
| Remove “Album”? (its downloads) | Remove / Keep it |
| Remove all downloads? | Remove all / Keep them |
| Stop downloading “Album”? | Stop and remove / Keep it |
| Get a Hi-Res copy? | Download Hi-Res / Cancel |

## Toasts
- `Added “t” to <name>` · `Removed “t” from <name>` · `Added “t” to the queue` · `Removed “t” from the queue` · `“t” plays next`
- `Cleared N favourites` (Undo) · `Removed N duplicates`
- Download progress reads the same on both: `Requested “t”`, `Searching Soulseek…`, `Waiting for a peer…`, `Downloading 42%…`, `Filing into the library…`,
  `“t” is ready to play`, `“t” wasn't found`.

## Other common words
Play · Pause · Shuffle · Previous · Next · Search · Settings · Sign in / Sign out · Edit · Rename · Delete playlist · Remove duplicates · Keep it · Cancel · Undo ·
Favourites (the page and the tile) · Downloaded / Downloading (state words; never "available offline").
