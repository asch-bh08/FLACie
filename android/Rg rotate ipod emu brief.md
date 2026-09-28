# Brief: Custom iPod-Style Music Player for RG Rotate

## Goal

Build a custom Android app for the Anbernic RG Rotate that faithfully recreates the iPod experience — starting with an iPod Nano 3rd-gen look as the default, with the ability to switch to other iPod-era themes (Classic/Video, original 2001 mono, Nano variants, etc.) from within the app.

This is a from-scratch build, not a reskin of an existing app — the two off-the-shelf options tried (ClassiPod, NostalgicPod) weren't good enough, so this needs to be built properly to the spec below.

## Target device

Anbernic RG Rotate specifically — square screen (720×720), Android 12-based custom skin, has both a touchscreen and physical D-pad/gamepad controls. Design for this device's exact screen dimensions and input model, not a generic Android phone.

## Core interaction — the click wheel

Two input methods must both work, fully interchangeably:

1. **Real touch-drag click wheel physics** — a circular touch area that responds to circular drag gestures the way a genuine iPod click wheel does (rotational scroll speed proportional to drag speed/distance, not just a simple swipe-up/down list scroll). This is the primary, "wow factor" interaction and needs to feel authentic, not like a cheap simulation.
2. **Physical D-pad fallback** — the RG Rotate's hardware D-pad/buttons must navigate the exact same menus/lists as the touch wheel, so the device is fully usable without touching the screen at all (e.g., up/down = scroll, a face button = select/center click, another = back/menu).

Both input paths should feel equally native — this isn't "touch is primary, D-pad is an afterthought."

## Visual theming

- **Default theme: iPod Nano 3rd generation** — recreate its actual UI accurately (the "Cover Flow"-adjacent list menu style, typography, icon style, colors/chrome of that specific era).
- **Theme switcher** — a settings option to switch the entire UI to other iPod eras — at minimum Classic/Video (the white list-menu style with Music/Videos/Photos/etc.), and ideally other Nano generations or the original 2001 iPod's simpler mono-style look as stretch options. Each theme should be a faithful recreation of that specific iPod generation's actual interface, not a reskin with the same underlying layout — menu structures, fonts, and visual chrome genuinely varied across iPod generations and that should be reflected.

## Scope — what the app actually does

- **Music playback from local files** on the device (this is a local file player, not a streaming app — no account, no internet dependency).
- **Cover art display** — read and show embedded album art from audio file metadata (ID3/etc.), matching how the real iPod displayed cover art during playback and in list views.
- **General audio file playback beyond music** — the app should also handle arbitrary audio files (e.g. voice memo-style recordings), not just tagged music library files, similar to how the iPod Classic/Video handled its separate "Voice Memos" section. Doesn't need to be a literal separate recording feature — just needs to play back non-music audio files cleanly alongside the music library.

## Explicitly out of scope for this brief

Photos, Videos, Games, and actual voice recording — none of these were requested. Keep the build focused on music + general audio playback + cover art + theming + the two input methods. Don't build these unless asked in a follow-up.

## Process

**Spec the technical approach before building** — specifically:

1. How the click-wheel touch physics will be implemented (this is the riskiest/most important piece to get right)
2. How theme-switching will be architected (so adding more iPod-era themes later is straightforward, not a rewrite each time)
3. How local audio file scanning/library management will work (folder-based, metadata scanning, etc.)

**Verify on-device once built** — this is exactly the kind of UI-feel project that can't be judged from code alone, so flag clearly what needs real hands-on testing on the actual RG Rotate hardware before considering any piece "done."
