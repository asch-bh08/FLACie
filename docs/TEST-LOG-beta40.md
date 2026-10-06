# Beta 40 test and fix log

Started 2026-10-06. Each entry: what was seen, steps, fix, re-test result.

## Reported
1. Small window / split screen: layout breaks (side-by-side layout chosen by aspect ratio only).
2. Artist name truncated beside the buttons although there is room.
3. Seek bar thumb clipped at the start/end of a track.

## Fixed so far (Android)
- Bug 1 (small window / split screen): the two-column player layout was chosen by aspect ratio alone, so a 400dp-wide split window got two squashed columns. Now two columns need >= 600dp of width; a short window (< 560dp tall) uses a single scrolling column with song, seek and transport first (cover and pages below); landscape phone keeps two columns with a fixed small cover. Verified at 1080x1155@420 (split), 2400x1080@420 (landscape phone), 720x1280@320, 2560x1600@280.
- Bug 2 (artist cut off with room to spare): the Equalizer / Add / More buttons sat beside the title and heart and squeezed the title block (title wrapped, artist ellipsised). They now have their own row under the badges; the artist may use two lines.
- Bug 3 (seek knob clipped at the start/end): the knob was drawn centred on the track end inside a canvas that clips, so half of it vanished at 0:00 and at the end. The track is now inset by the knob radius at both ends (touch mapping the same).
- Nav rail clipped the Settings item on a landscape phone (about 400dp tall): items now share the available height (50-68dp) and still scroll if needed.
