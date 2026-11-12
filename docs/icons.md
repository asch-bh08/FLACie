# One icon per action, the same on web and phone

The owner (12 Nov): "the buttons should be consistent across all platforms", and buttons that do different things must not look alike.
Web icons are `Icon Name="…"` (FLACie.Retro/Components/Shared/Icon.razor); phone icons are `Glyph.…` (android/…/player/Widgets.kt) or the small
`…Icon` drawings in LivingSleeve.kt / InfoPanel.kt. Before adding a button, look its action up here; add a row when a new action appears.

| Action | Web | Phone |
|---|---|---|
| Settings | `settings` (cog) | `Glyph.GEAR` |
| Options (rating, speed, sleep, sound in Now Playing) | `sliders` (level sliders) | `SlidersIcon` |
| Swap sides (cover and lyrics/info side, PC Now Playing) | `swap` (two opposite arrows) | none yet (the phone shows one side at a time) |
| Equaliser | `eq` (upright sliders) | `Glyph.EQ` |
| Sound (even out volume, fade between songs) | `wave` | (in the Equaliser and Settings) |
| Sleep timer | `moon` | `Glyph.MOON` / `MoonIcon` |
| Speed | `gauge` | `GaugeIcon` |
| Server admin | `shield` | `Glyph.SHIELD` |
| Sign out | `logout` | `Glyph.LOGOUT` |
| DJ | `dj` | `Glyph.DJ` |
| Talk to the DJ | `mic` | `DjMicButton` (mic) |
| Retro | `retro` | `Glyph.CASSETTE` |
| Car mode | `car` | `Glyph.CAR` |
| Devices | `devices` | `Glyph.DEVICES` |
| Jam | `jam` | `Glyph.JAM` |
| Favourite | `heart` / `heart-fill` | `Glyph.HEART` / `HEART_FILLED` |
| Add to playlist | `plus` | `Glyph.PLUS` |
| Share | `share` | `ShareIcon` |
| More | `more` | `Glyph.MORE` |
| Up next | `queue` | `Glyph.QUEUE` |
| Lyrics | `lyrics` | `Glyph.LYRICS` |
| Info | `info` | `Glyph.INFO` |
| Full screen | `expand` | `ExpandIcon` |
| Close Now Playing | `chevron-down` | `Glyph.DOWN` |
| Close, clear, remove | `x` | `Glyph.CLOSE` |
| Download | `download` | `Glyph.DOWN` |
| Offline | `download` | `Glyph.DOWNLOADED` |
| Shuffle / repeat / repeat one | `shuffle` / `repeat` / `repeat-one` | `Glyph.SHUFFLE` / `REPEAT` / `REPEAT_ONE` |
| Previous / play / pause / next | `prev` / `play` / `pause` / `next` | `Glyph.PREV` / `PLAY` / `PAUSE` / `NEXT` |
