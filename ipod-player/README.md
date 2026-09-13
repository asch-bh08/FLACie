# iPod Player

A zero-install, single-file browser player for classic click-wheel iPods — the
lightweight, **read-and-play** companion to the main `ipodsync` engine. Where
`ipodsync` is the C#/.NET sync tool (and future writer), this is a self-contained
HTML app you double-click to browse and listen to what's already on an iPod.

## What it does

- **Auto-detects a plugged-in iPod** (via `ipod-server.ps1`) or opens a folder
  you pick — reads the same `iTunesDB`/`iTunesCDB` this project reverse-engineered,
  with its own standalone JavaScript parser (validated by an in-file `?selftest`).
- **Plays** AAC, MP3, and **Apple Lossless (ALAC), transcoded to FLAC on the fly**
  (lossless) via a local ffmpeg the helper server drives.
- **Real album art** decoded straight from the iPod's `.ithmb` files.
- **Full-screen "Now Playing"** (YouTube-Music style): big art, colour extracted
  from the artwork, a live spectrum visualizer, an up-next queue, and
  **time-synced lyrics** (fetched from [lrclib.net](https://lrclib.net) — free,
  no API key).
- **10-band graphic equalizer** with the classic iPod presets, plus Sound Check,
  a sleep timer, a volume limit, search, shuffle/repeat, OS media keys, and
  dark/light themes.

## Run it

Double-click **`iPod Player.vbs`** — a borderless app window opens with your iPod
already loaded; the helper server runs hidden and shuts down when you close the
window. `Start iPod Player.cmd` is the same thing with a visible console for
debugging. For a jailbroken iPod Touch, drag its extracted `iTunes_Control`
folder onto the `.vbs`.

## Relationship to ipodsync

This player is **read-only** today. Editing an iPod's contents (metadata,
playlists, adding/removing tracks) is deliberately routed through `ipodsync`'s
verified database writer rather than reimplemented here — see the repo root
[HANDOFF.md](../HANDOFF.md) safety rules: nothing writes to a real device until a
modified database round-trips byte-identically, a backup is taken, and the user
is asked first.
