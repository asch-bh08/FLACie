# Player UI redesign (Android first; web and Windows follow)

Status: spec, written 2026-10-13. Tracks the U4 task of the overnight prompt. Build on the Phase 4 foundations (one icon set, motion tokens), not as a one-off.

## Problems today (emulator, beta 57; `img/player-today.png`)
1. **Seek track is thin** (4 dp in the modern style, 36 dp tall canvas inside a 44 dp box): hard to hit and hard to see; the knob (7 dp, 11 dp while dragging) hides under the finger.
2. **No scrub bubble.** The elapsed time at the left changes while dragging (beta 57) but sits far from the finger; remaining time shows the total, not the time left.
3. **No buffered portion**, no chapter or position ticks for long items.
4. **Transport is cramped**: shuffle / previous / play / next / repeat are in one row at equal weight; previous and next are bare glyphs with no pressed state; play/pause hard-swaps its glyph (no morph).
5. **Secondary buttons** (Lyrics, Info, Up next, Equalizer, ...) form a horizontally scrolling chip row that is cut off at the right edge ("Equal"): the user cannot tell there is more.
6. **Mixed icon styles** (outline hearts, filled transport, text chips): no single set.
7. **No skip-forward-30 / back-10** for podcasts and audiobooks; no speed control on this screen.
8. **Cover to Now Playing** is an instant swap from the mini player: no shared-element transition. Background colour from the cover is applied without a contrast clamp (white text on light covers risks low contrast).

## New design
- **Seek**: track 8 dp, grows to 10 dp under the finger; touch height 48 dp; knob 16 dp resting, 24 dp dragging. A **scrub bubble** above the knob shows the target time (and for items over 20 min the chapter or "+1:30" delta). Left label elapsed, right label **remaining** ("-2:19"); tap the right label to switch to total. A lighter **buffered** segment is drawn behind the played segment. Tap = seek; drag = scrub with a haptic tick every 10 % (`HapticFeedbackType.SegmentTick` where available, else `TextHandleMove`). The bar state is the pure `SeekHold` (already unit-tested) plus a `ScrubModel` (target fraction, bubble text).
- **Primary play/pause**: 72 dp, filled accent, press scales to 0.94 with a spring (stiffness medium, damping 0.7); the glyph **morphs** pause<->play (animated vector, 160 ms) instead of swapping.
- **Previous / next**: 56 dp targets, 24 dp glyph, visible pressed ripple + scale 0.92. Next/previous press are exempt from long press until the long-press seek is designed.
- **Shuffle / repeat**: clear toggle states: off = outline at 60 % opacity, on = filled accent glyph with a small dot below (not colour alone); repeat-one shows a "1".
- **Long items** (speed != 1x or spoken-word genre): skip-back-10 and skip-forward-30 appear flanking the primary button; shuffle/repeat move into the secondary row. Speed chip shows "1.25x".
- **Secondary actions**: a fixed row of five icon buttons (lyrics, info, up next, equalizer/sound, more) with labels under them at 11 sp; no horizontal scroll; "more" opens the existing sheet.
- **Cover**: shared-element transition from the mini player (bounds, corner radius 12 -> 20 dp, 300 ms, emphasized easing); colours from the cover with the WCAG contrast clamp (text >= 4.5:1, controls >= 3:1; lighten/darken the tint until it passes).
- **Tokens**: `Space` 4/8/12/16/24/32 dp; `Radius` 8/12/20/full; `Motion.fast 120 / base 220 / slow 320 ms`, easing standard + emphasized; accent from the theme.
- **Haptics**: tick on scrub segments, `Confirm` on play/pause and favourite toggle, `Reject` when seek is refused (live stream).
- **Reduced motion** (system animator scale 0 or `Settings.Global.ANIMATOR_DURATION_SCALE`): no spring, no morph (instant), no shared-element transition (fade), the bubble stays but does not animate.
- **Accessibility**: seek bar is a slider with `stateDescription` "1 minute 37 of 5 minutes 2" and `contentDescription` "Seek"; custom actions "Forward 10 seconds / Back 10 seconds" on the slider; every transport button has a label ("Play", "Pause", "Next song", "Previous song", "Shuffle on/off", "Repeat off/all/one"); targets >= 48 dp; contrast as above; TalkBack announces the new song once on a track change.
- **D-pad and keys** (RG Rotate, square layout via `--es fake WxH`): focus order cover -> seek -> transport -> secondary row; Left/Right on the seek bar nudges (existing); the iPod themes' Now Playing screens are untouched by this spec.

## Parity
Web: the same bar (CSS, `input type=range` with `aria-valuetext`), bubble, buffered segment, 72 px play button with a CSS morph, `prefers-reduced-motion`. Windows (embedded web UI): comes with the web part. Haptics and shared-element transitions are Android-only.

## Tests and acceptance
Unit: `ScrubModel` (fraction -> time text, remaining, delta), contrast clamp, reduced-motion switch. Emulator: screenshots at phone, tablet and `--es fake 720x720` square; scrub with `input motionevent`, tap-to-seek, D-pad nudge; TalkBack text read from `uiautomator dump`. By hand (owner): seek feel, haptics, motion, one-hand reach.

## Not started
Nothing in this spec is built yet (see `docs/OVERNIGHT-LOG.md`).
