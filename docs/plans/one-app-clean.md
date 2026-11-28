# ONE APP step 5: clean and optimise, the starting point (28 Nov, cloud)

Nothing is deleted or optimised yet. This is the measured "before" and what was found, so the clean-up can start from facts.

## Web, before (origin/ui/living-sleeve at 987d746c, measured from wwwroot)
| What every page loads | Files | Raw | gzip |
|---|---|---|---|
| Stylesheets (App.razor) | 23 | 663 KB | 167 KB |
| Scripts (flacie-pip, flacie, motion, headphones, desk, retro) | 6 | 260 KB | 88 KB |
| of which only the Retro / iPod theme uses (ipod.css, ipod-touch.css, desk.css, retro.css, design-j-retro.css, desk.js, retro.js) | 7 | 284 KB | 77 KB |

The biggest first-load win measured so far: the Retro / iPod theme's 7 files are about 30% of what every page downloads (77 of 255 KB gzipped) and
are only needed on /retro and /ipod. Loading them only there needs a change to how the Retro pages load their assets; Retro is outside the frame
and "untouched", so this is listed for the owner rather than done. Not measured yet: first page load time, the web Now Playing frame time, the
graph's frame time; the phone's cold start, first song, 10,000-song scroll, memory and APK size (the phone needs the emulator).

## The known failing tests (investigated, not fixed)
- `1i-download-tags:469` (Autoplay lists: songs being fetched show as "Downloading"): the three suggestions never reach `Player.AutoplayFetching`
  with the mock, so Up next shows no pending rows (0 of 3). The next step is to trace prefetch -> catalog -> the download jobs for Autoplay.
- `1z-bar-tap` (one click anywhere on the bar opens Now Playing): it fails at the very first point, 3% along the bar at 1400 wide. That point is the
  cover placeholder's svg inside the bar's song button; the test counts it as "not a button" (it excludes .bar-song) and expects the click to open
  Now Playing, which it does not there. Next: see whether the cover's own handling stops the click, or whether the test's point is wrong.
- `1o-ipod-desk-sizes` classic6: Retro; not looked at.
- Every Retro / iPod spec (1o, 1r, 1t, 1v) also fails in the cloud only: the newer cloud Chromium logs "Permissions policy violation: unload is
  not allowed" and those specs fail on any console error. Environment, not code (1-pages already ignores that notice).

## Deleting the duplicates (not started)
The plan's list stands: the second DeviceCard, AlbumCard, old headings, empty states and text fields on the phone, ActionSheet outside the iPod
theme; .badge / .tag / .dev-chip, .dv-btn / .pl-tool / .chipbtn, old .btn variants and unused design-i / design-j rules on the web (kit.css maps
them today, so each needs its markup moved to the kit class first, then the old rule removed); the Txt-size allowlist down to zero on modern
screens (the 1d/1e passes took ~70 calls off it).
