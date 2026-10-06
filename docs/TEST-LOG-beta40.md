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
- Devices page redesigned (web + Android): cards with cover, device kind (Browser / Phone / Computer, guessed from the device name), state chips (This device, Playing, Paused, Idle), progress line, controls; "ipodplayer" no longer shown.
- Web touch targets: every button/link chip >= 44px at <= 1024px wide (buttons, subnav, mood chips, avatar, burger, search input, side rail links, swatches, reorder arrows, import file picker). My first CSS version broke two things (the remote strip's "Play here" button reappearing and squeezing the song name to 64px at 320px wide; the logo centred and cut off): both fixed and re-swept.
- Web remote "Listening on ..." strip at <= 400px: previous (and next at <= 340px) are hidden so the song name has room.
- Web tab bar labels no longer run into each other at <= 400px.
- Android avatar letter matched to the name the Account screen shows (a NAS-primary account showed H from the Jellyfin name but G on the Account page).
- Web sweep (iframes, 15 pages x 9 sizes: 320x568, 360x640, 390x844, 768x1024, 1024x768, 1280x720, 1920x1080, 2560x1080, 800x360): no horizontal overflow, nothing clipped, no small targets left except the A-Z letter rail (a scrubber: 27 letters cannot all be 44px tall).
