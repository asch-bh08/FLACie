# Changelog

## 0.6
- Three explicit modes (Modern Player / iPod Emulator / Click Wheel Fullscreen) on one Appearance screen, with previews
## FLACie 1.0 beta 35

_v1.0.0-beta35, 2026-10-06, release_  ([release page](https://github.com/asch-bh08/FLACie/releases/tag/v1.0.0-beta35))

Beta 35 (Android 49, MSI 0.35.0). Still a beta, not a 1.0 final.

**Changed (Android)**
- The format tags (Jellyfin, FLAC/AAC/MP3, kHz, kbps) no longer disappear on Lyrics, Info and Up next: the one-line title keeps them (artist · Jellyfin · FLAC · 44.1 kHz).
- Swiping between pages is smoother: the player now follows the swipe as soon as it is heading to a page (it used to wait until the page had settled, then jump), and the title block fades and resizes into its new shape instead of swapping.
- Apple Lossless (ALAC, the .m4a in your HoodTrap(LAC) folder) is now converted by the server only on phones that have no ALAC decoder (many have none and played silence); on phones that have one it plays the original. The conversion bitrate is up from 192 to 320 kbps.

**Dark horse (Kryd Hoodtrap / Mylancore)**: the copy in HoodTrap(LAC) is ALAC at 48 kHz (listed Hi-Res because of the rate); the AAC copy is in Unknown artist/Singles. Both decode fine on a computer. Most likely your Fold has no ALAC decoder, which this build covers. If it is still silent, tell me.

**Tested**: Android release build on the emulator (tags in the one-line title on the Info pane, swiping from the cover).
**Not tested**: the ALAC conversion on a phone without a decoder (the emulator has one, so I could not trigger it), the feel on a real Fold, the MSI install, the 0.35 Windows app.


- Socket-style tilted-iPod carousel gallery for all 23 models (faked 3D), L1/R1 to cycle
- Modern Player: For You shelves (daily mixes, suggested, on repeat, discover, genre/decade), Songs sorting, A-Z sections
- Fullscreen click wheel: artwork uses the freed space; body view grows and scrolls to stay readable
- Back/controller fixes (Search, Appearance Back pill, stale screen after mode switch), body never hidden by the hinge
- Release build: R8, baseline profile; jank ~52% -> ~5% on the RG Rotate

## 0.3
- Compose Player as default, art-colour fade, 23-model picker, swipe gestures, background playback
