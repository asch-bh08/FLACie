# Design references for FLACie (research, 2026-10-13)

[unconfirmed] means not verified on an official page during this pass.

## Material 3 motion tokens
Source: https://github.com/material-components/material-components-android/blob/master/docs/theming/Motion.md (spec: https://m3.material.io/styles/motion/easing-and-duration/tokens-specs)
- Easing: standard cubic-bezier(0.2, 0, 0, 1); standard decelerate (0, 0, 0, 1); standard accelerate (0.3, 0, 1, 1); emphasized decelerate (0.05, 0.7, 0.1, 1); emphasized accelerate (0.3, 0, 0.8, 0.15); linear (0, 0, 1, 1). Emphasized itself is a multi-segment path, not one cubic bezier.
- Durations (ms): short1 50, short2 100, short3 150, short4 200; medium1 250, medium2 300, medium3 350, medium4 400; long1 450, long2 500, long3 550, long4 600; extraLong1 700, extraLong2 800, extraLong3 900, extraLong4 1000.
- Practical use: small state changes short2 to short4; screen or sheet transitions medium2 to medium4; entering elements decelerate, exiting elements accelerate.

## Android haptics (HapticFeedbackConstants)
Source: https://developer.android.com/reference/android/view/HapticFeedbackConstants (page too large to read fully; levels below come from general knowledge) [unconfirmed]
- API 30: SEGMENT_TICK (discrete steps of a continuous control such as a slider), SEGMENT_FREQUENT_TICK (denser variant), CONFIRM, REJECT, TOGGLE_ON.
- API 34: GESTURE_START, GESTURE_END, DRAG_START, TOGGLE_OFF.
- Older: LONG_PRESS (1), VIRTUAL_KEY (1), CLOCK_TICK (early API), CONTEXT_CLICK (23).
- Call `view.performHapticFeedback(constant)`; guard on `Build.VERSION.SDK_INT` and fall back to CLOCK_TICK. Devices may ignore unsupported constants, so haptics must never be the only signal.

## WCAG 2.1 AA numbers
Sources: https://www.w3.org/WAI/WCAG21/Understanding/contrast-minimum.html , https://www.w3.org/WAI/WCAG21/Understanding/target-size.html
- 1.4.3 text contrast: 4.5:1 normal; 3:1 for large text (18 pt, about 24 px, or 14 pt bold, about 18.5 px). Ratios are not rounded (4.499 fails).
- 1.4.11 non-text contrast (icons, control boundaries, focus indicators): 3:1 [unconfirmed on fetched page; standard value].
- 2.5.5 target size: 44 by 44 CSS px, but Level AAA in 2.1, not AA. WCAG 2.2 adds 2.5.8 at AA with a 24 by 24 CSS px minimum [unconfirmed]. Android guidance is 48 dp targets [unconfirmed here]; recommend 48 dp.

## Media3 MediaSession: notification and lock screen
Source: https://developer.android.com/media/implement/surfaces/mobile
- Use a MediaSessionService; Media3 builds and updates the MediaStyle notification from Player state.
- Lock screen: notification visibility must be public so controls show.
- Tap opens the PendingIntent passed to `MediaSession.Builder#setSessionActivity` (must launch an activity).
- Play/pause/spinner comes from Player: playWhenReady false or ENDED = Play; true and BUFFERING = spinner; true and READY = Pause.
- Android 13+ shows up to 5 action buttons; compact view only the first 3. Slots: 1 play/pause, 2 previous or SLOT_BACK, 3 next or SLOT_FORWARD, 4 to 5 SLOT_OVERFLOW custom buttons in the order added.
- Custom buttons: `setMediaButtonPreferences` plus custom SessionCommands authorised in `onConnect` and handled in `onCustomCommand`.
- Duration and a seek action are needed for the seek bar (legacy path). Artwork size is not specified on this page.

## Android Auto media apps
Sources: https://developer.android.com/training/cars/media/create-media-browser/content-hierarchy , https://developer.android.com/training/cars/media/create-media-browser/media-artwork , https://developer.android.com/docs/quality-guidelines/car-app-quality
- Root: usually 4 browsable tabs maximum (read `BROWSER_ROOT_HINTS_KEY_ROOT_CHILDREN_LIMIT`, default 4); root children must be browsable. No pagination in onGetChildren.
- Artwork: pass as `content://` or `android.resource://` URI (bitmap or vector), not `setIconBitmap` (1 MB binder limit, unsupported on AAOS). Remote art needs a ContentProvider that downloads and caches.
- Quality rules: buttons respond within 2 s; app launches within 10 s; content loads within 10 s; no autoplay on launch; no auto-scrolling text; no animation; only one static background image plus nav icons; provide white icon sets; support voice commands; restore previous state on relaunch.
- Items-per-list limits live on Google's design pages, not fetched [unconfirmed].
