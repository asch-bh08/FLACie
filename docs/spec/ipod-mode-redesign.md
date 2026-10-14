# iPod mode redesign

Status (beta 63): built: Classic Cover Flow, list scrollbar, scrub diamond, bezel inner shadow (Android); the whole iPod mode on the web and Windows (/ipod, IpodNav + WheelDetents tested). Not built: integer-scaled logical-resolution LCD with a bitmap font, title cross-fade, Cover Flow on the other themes. Original spec, written 2026-10-13 (U4 of the overnight prompt). Built so far (beta 60): wheel inertia (WheelInertia, tested) and wheel label contrast (ContrastClamp, tested); the LCD renderer, device art, menu transitions, per-generation Now Playing and Cover Flow are not built. The current themes stay untouched until a new one passes the same tests (D-pad/keys, wheel physics `WheelPhysicsTest`, square layout via `--es fake WxH`).

## What exists today (emulator, beta 57)
Three modes in Settings > Mode > iPod appearance: **iPod Player** (flat app in iPod colours), **iPod Emulator** (device body + screen + wheel), **Click Wheel Fullscreen** (menus + wheel without body). Models and colourways come from `theme/Themes`. Screens: `img/ipod-emulator-today.png`, `img/ipod-player-home-today.png`.

## What feels cheap
1. **The LCD is a small window in a large empty body.** The screen is ~70 % of the body width and 25 % of its height; the menu fills 6 rows and leaves a blank third of the screen. The real 4th-gen has a 160x128 LCD occupying the full upper band.
2. **Typography is a generic monospace** (Droid-style) at one weight. The real devices use Chicago (classic) / Myriad (later) with 1 px sharp pixels; a pixel-faithful bitmap font drawn at integer scale would read as "the hardware idea, crisp" rather than "a terminal".
3. **Flat vector body**: a single vertical gradient and a hairline; no bevel on the screen bezel, no wheel inner shadow, the centre button has no pressed state, the wheel labels (MENU, back/forward, play/pause) are low-contrast grey on white.
4. **Wheel**: touch tracking exists (`WheelPhysics`), but there is no haptic click per detent, no inertia after a fast flick, and no audible-style feedback option.
5. **List transitions**: already there (a 240 ms push/pop slide in IpodView); what is missing is a title cross-fade and the scrollbar polish. (An earlier version of this spec said they were instant: that was read off screenshots and was wrong.)
6. **Now Playing** already has its own drawing per generation (MonoGen1Theme, ClassicTheme, Nano3Theme); what is missing is Cover Flow (only named in Models.kt) and the Classic scrub diamond. (Also corrected from the first version of this spec.)
7. **iPod Player skin Settings bug (found while capturing)**: Settings drew dark text on dark cards (`img/ipod-player-settings-bug.png`). Fixed in beta 58 by giving Settings the flat dark scheme (`SettingsScreen`).

## Redesign (faithful to the hardware idea, crisp)
- **Device art**: vector, per model, drawn from real proportions (4th gen 61.8 x 103.5 mm = 0.597; Classic 61.8 x 103.5; Nano 2nd 40 x 90) with an LCD bezel (inner shadow, 1.5 dp), a body highlight along the top edge, and a wheel with a concave radial gradient, a pressed centre button, and engraved-looking labels at >= 4.5:1 contrast. Scale so the LCD is the largest feature the model allows; keep >= 16 dp margins.
- **Screen**: render at the device's logical resolution (160x128, 320x240 Classic, 240x320 Nano) into an offscreen canvas and scale by an integer (nearest neighbour) so edges stay sharp at any phone/fold/square size; the font is a bundled bitmap/outline font (licence check first: Chicago itself is Apple's; use a freely licensed lookalike or the existing theme font, record the licence in THIRD-PARTY-NOTICES).
- **Wheel**: 15-degree detents; a haptic tick per detent (`HapticFeedbackType.SegmentTick`, fallback `TextHandleMove`), a lighter tick on the centre press, inertia (velocity-based, ~400 ms decay, stops on touch); optional click sound off by default; the existing `WheelPhysics` stays the model and is extended, not replaced.
- **Menus**: the push/pop slide exists (240 ms); add a title cross-fade; the selection bar moves with the wheel without a jump; scrolling list with a scrollbar at the right like the hardware.
- **Now Playing per generation**: Classic: art left, text right, progress bar with a scrub diamond; Nano: full-width art with overlaid text; time elapsed/remaining under the bar; the volume overlay on a wheel turn; rating dots from Jellyfin favourites.
- **Cover Flow**: a perspective row of album covers (rotate 60 degrees, reflection) with the wheel as the scroller, from the Explore > Albums data; covers come from `ArtCache` (no extra requests: only albums already on screen).
- **Sizes**: phone, foldable inner display, square (RG Rotate, `--es fake 720x720`): the device art letterboxes in the available box; on square the Fullscreen wheel layout is the default.
- **Accessibility**: the wheel is also reachable by D-pad/keys; every screen row has a TalkBack label; reduced motion disables the slide and inertia.

## Plan
1. Offscreen LCD renderer + integer scaling behind a flag. 2. Device art. 3. Wheel haptics/inertia (pure physics tests extended). 4. Menu transitions. 5. Now Playing per generation. 6. Cover Flow. Each step is released only when the existing iPod tests pass and the emulator screenshots (phone, tablet, square) are checked.

## By hand (owner)
Wheel feel, haptic strength, inertia, legibility of the LCD in sunlight, Cover Flow smoothness on the Fold and the RG Rotate.
