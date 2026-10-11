# Test run 2026-10-11, workstream F-fixes

Baseline: beta 49. Mark each checklist item pass / fail / blocked with evidence.


## F3 first launch: sign-in before any system prompt

Branch fix/f3-firstlaunch. Problem: MainActivity.onCreate requested the music and notification permissions at cold start, so the system prompts covered the app before the user saw what it was.

What changed

- No permission is requested at launch. The sign-in / source screen is first and explains what FLACie will ask for and when. The rules are pure functions in PermissionPlan.kt (unit tested).
- Music permission (READ_MEDIA_AUDIO on Android 13+, READ_EXTERNAL_STORAGE below): asked only when the user picks Music on this device (the page shows "FLACie needs access to the music on this phone" first; buttons Allow access and start / Start without access). Existing guest installs that never answered get one reason page, then the system prompt. Server / NAS accounts are never prompted; Settings > Permissions offers it.
- If refused: Home (modern theme) shows a card with Open app settings; Settings > Permissions has the same row. Server sources keep working.
- Notification permission (Android 13+): a reason page ("Show playback controls?", Continue / Not now) the first time a track starts while the app is in front, never at launch or during sign-in. Not now is remembered (no nagging); Settings > Permissions lets the user change it.
- Already granted or already answered: no prompt again (prefs mediaAsked / notifAsked, plus a live check on every resume).
- Bluetooth: the app has no BLUETOOTH_CONNECT use or manifest entry, so nothing to defer.
- Gamepad: first choice on the sign-in screen starts focused, focus ring on every choice, A activates, B goes back; reason page focuses Continue.

Verify (do not pm clear on an owner-signed-in device; use pm revoke instead)

1. Clean device or emulator only: adb shell pm clear com.ipodemu, launch. Expect: sign-in screen with three choices and the "What FLACie will ask for" text, no system dialog. Pick Music on this device: page with the reason line, then Allow access and start shows the system prompt. Deny: Home shows the card with Open app settings.
2. Owner devices: adb shell pm revoke com.ipodemu android.permission.POST_NOTIFICATIONS (Android 13+), reopen, play a song: reason page, Continue, system prompt. Relaunch after allowing: no prompt. For music: pm revoke com.ipodemu android.permission.READ_MEDIA_AUDIO (READ_EXTERNAL_STORAGE on the RG, Android 12).
3. RG Rotate 720x720: D-pad up/down moves the ring, A selects, B backs out; nothing is cut off at largest font size (the page scrolls).
4. Fold: cover screen and unfolded: content is a centred column up to 520dp wide.
