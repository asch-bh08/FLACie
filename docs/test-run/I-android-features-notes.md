# I - Android features: queue editing and MEDIA_PLAY (beta 52 overnight run)

Branch `beta52/android-features` (from `release/beta52`). Only `android/` and this file changed. No version files touched.
Nothing here was tried on a real device: a phone, the Fold or the RG Rotate (see "Not tested").

## 1. Queue editing in Up next

### What the user gets

| Where | Remove | Move |
|---|---|---|
| Up next pane of the full-screen player (`QueuePanel`, phones and the wide layout) | "More" button on every row (also the playing one) -> sheet: **Play next**, **Move up**, **Move down**, **Remove from queue** | same sheet |
| "Up Next" screen (iPod style list, `QueueScreen`) | swipe either way (as before) or the X (as before), now with an **Undo** message | "More" button (new) or long press -> sheet: Play next, Move up, Move down, Remove from queue |

- The sheet is the app's normal action sheet: it works with touch and with the d-pad / gamepad (focus on the first item, A selects, B closes). There is no drag handle on purpose: the RG Rotate has no drag. Long-press drag-and-drop was not added (not cheap to make safe next to the pane pager and the swipe rows).
- The panel does **not** use swipe-to-remove: it sits inside the horizontal pane pager (Lyrics / cover / Info / Up next) and a swipe there would remove songs while the user only wants to change pane.
- Undo: removing shows "Removed "X" from Up next" with **Undo** (the same snack as removing a song from a playlist). Undo puts the song back at its old index (with shuffle on: right after the song that played before it). It does not change what is playing.
- The playing song keeps playing through every edit:
  - Moving or removing other songs: ExoPlayer keeps the current item (`moveMediaItem` / `removeMediaItem`), position and play state untouched.
  - Removing the **playing** song: the song that plays next (in play order, so the shuffle order with shuffle on, the wrap-around with repeat all) is started first and then the old one removed; if the player was paused the next song is current and stays paused. With nothing after it (last song, repeat off) the song before it becomes current, **paused** at 0:00, and this jump is not counted as a play (no history entry, no plays report). Removing the only song clears the queue (Undo brings it back paused).
  - Shuffle: "Move up / down / Play next" in Up next change the **shuffle order** (`setShuffleOrder`), so what the list shows is what plays. Repeat all: songs that come after the wrap are part of Up next and can be moved too. Repeat one: moves and removals apply to the songs after the repeated one as usual.
- In a Jam the group owns the queue: local edits are refused (no move actions; Remove says so) instead of desynchronising the group. Before this change Remove in a Jam changed only the local queue.
- A pending skip burst (rapid Next presses) is settled first, so an edit works on the song that really plays.
- Persisted: every edit writes `queue.json` at once (`persistNow`), so the order, the current song and position come back after a kill. Shuffle: the play order is now saved too (`"so"` in `QueueStore`, optional key, same file version, windowed with the 300 songs around the current one, validated as a permutation on load, ignored when it does not fit); before, a shuffled queue came back with a new random order.

### Code

- `playback/QueueEdit.kt` (new, pure, no Android classes): `move`, `remove`, `insert` (list + current-index bookkeeping), `upcoming`, `removePlan`, `reorder` (shuffle order), `linearMoves` / `swapMoves` (the exact `moveMediaItem` calls for the plain queue), `playNextTarget`, `rowActions`, `placeAfter`.
- `playback/PlayerController.kt`: `moveUpNext`, `moveInQueue`, `playNextInQueue`, `removeAt` (+ `QueueRemoval`), `undoRemoval`, `canEditQueue`, `queueRev`; `removeFromQueue` kept (delegates). `PlayerSnap.rev` makes the lists refresh after a reorder that leaves count and current song unchanged.
- `playback/QueueStore.kt`: optional shuffle order.
- `player/UpNextDrawer.kt`, `player/PlayerUi.kt`: the sheets, the More buttons, `removeFromQueueWithUndo`, `LocalPlayerNav`.

### Tests (JVM)

- `QueueEditTest` (34): move (current moved / crossed from either side / same place / out of range / empty / single), remove (before, after, current, last, last with wrap, only song, out of range), undo insert round trip for all n<=6, play-order fallback, upcoming with and without wrap, removal plan (other song, shuffle next, last stops on the previous one paused, repeat-all wrap, only song, unknown index) and an exhaustive "the right song stays/becomes current" check, reorder in shuffle order and repeat-all, edges, and an exhaustive check (n<=7, every current, item, step, repeat off/on) that the player's `moveMediaItem` sequence and the shuffle-order edit give the same Up next and keep the playing song.
- `QueueStoreTest` (+4): a reordered queue round trip, shuffle order round trip, shuffle order with the 300-song window and dropped songs, broken shuffle order ignored.

## 2. MEDIA_PLAY after audio-focus loss

Reported: `KEYCODE_MEDIA_PLAY` (126) does not resume after another app took audio focus while FLACie is in the background; PLAY_PAUSE (85) does.

What the code and the earlier device notes (G-rg.md BUG-5, G2-android.md) establish:

1. **Background case: Android routing, not the app.** `dumpsys media_session` on the RG Rotate showed "Media button session is null" and "Last MediaButtonReceiver: youtube": after a permanent focus loss FLACie's session is paused, so it is not the "media button session" (the system picks the session of the app that most recently *played*), and once YouTube's session is gone the system falls back to the last receiver, YouTube's. A bare PLAY key then goes to that dead receiver. Media3 itself is fine: with no other app involved all media keys (also with the screen off) resume FLACie, as G-rg.md measured. The only app-side ways to become the target again are to start playing (the user taps Play) or to hold a silent playing audio track while paused, which is a battery and "now playing" side effect that is not defensible. Not done. This part is not fixable from the app and stays a documented Android limitation (also true of other music apps when another app was the last to play).
2. **A real gap found by reading the code, fixed: the click-wheel (iPod) view.** `MainActivity.dispatchKeyEvent` only used `PlayerKeys` (which handles PLAY and PAUSE since beta 51) when the view is *not* the wheel view; in the wheel view `KeyMapper` only knows PLAY_PAUSE, so a bare PLAY or PAUSE fell through to the system, i.e. exactly the failure mode above even with the app in front. Now both views handle PLAY and PAUSE in the activity (`input/MediaKeys.shouldToggle`: PLAY resumes only when nothing plays, PAUSE pauses only what plays, PLAY_PAUSE/headset hook always toggle).
3. A transient focus loss resumes by itself (ExoPlayer's focus handling, `setAudioAttributes(..., handleAudioFocus = true)`); a permanent loss leaves it paused until the user acts: unchanged and by design.

Tests: `MediaKeysTest` (4).

## Test counts

See the end of this file (filled in after the final run).

## Not tested

- Everything on a real device or the emulator UI: the sheets, the More buttons, Undo, gamepad focus order inside the sheet, the long-press on the Up Next rows. Only JVM unit tests and a compile were run; `ExoPlayer.setShuffleOrder`, `moveMediaItem` and `removeMediaItem` behaviour is as documented by Media3 and was not exercised.
- By ear: that playback continues without a gap when the next song is moved or removed (an edit re-plans the preload of the next songs and re-applies data saver to them, like a track change does; how that behaves on a real stream is not verified).
- The MEDIA_PLAY routing with a real second app (the background case is documented, not changed).
- The web UI and the Windows app: the task was Android only; queue editing there is a separate item.
