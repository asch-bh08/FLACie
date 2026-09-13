# Overnight session status — 2026-09-14

Autonomous session working through the HANDOFF.md build order. This file is the
running log (newest entries at the bottom) and ends with the end-of-session
report. Every device write is listed with its pre-write backup folder.

## Session log

### 00:03–00:40 — setup, baseline, read-only survey

- Local repo lives at `C:\IPODAPP\ipodsync\ipodsync` (the outer `C:\IPODAPP`
  folders are the NAS copy without git). `git fetch`: local `main` ==
  `origin/main` (`cc5cd10`), nothing to pull. Uncommitted WIP from the last
  session: `src/IpodSync.Core/Itlp/ItlpPlaylistSync.cs` + `itlp-preview` CLI.
- This machine had **no .NET SDK**; installed .NET SDK 9.0.318 via winget and
  added the nuget.org package source (none was configured). CLI builds clean.
- Device: `D:\` "ASHLEY'S IP", FAT32, 7.4 GB. CDB: 636 tracks, 12 playlists
  (+master). `roundtrip D:/` → byte-identical PASS.
- **Baseline backup before any work:** `C:\IPODAPP\ipodsync\ipod-backups\overnight-baseline-20260914-001739`
  (`iPod_Control/iTunes` + `iPod_Control/Artwork`, SHA1 manifest `device.sha1`, verified).
- `Library.itdb` (and every other `.itdb`) last modified **Sep 7** — the SQLite
  side has never been written by ipodsync. Consequences, found by comparing
  the two databases:
  - SQLite `item` has **635** rows; CDB has **636** → the earlier add-from-file
    track exists only in the CDB.
  - SQLite `container` has 7 rows (master + 6); CDB has the extra
    `iPodSync Test` playlist → not in SQLite.
  - So the "confirmed on hardware" status of add-song / rename in the Desktop
    HANDOFF could only have been CDB-level; the repo's EDIT-PROTOCOL.md still
    says both were awaiting the iPod-screen check. Treating them as unverified
    against the SQLite layer.
- `Locations.itdb.cbk` is 1126 bytes = 46 (hash72 signature) + 20 (final SHA1)
  + 53×20 (SHA1 per 1024-byte block of the 54272-byte `Locations.itdb`). That is
  the libgpod-documented Nano 5G layout: **any change to `Locations.itdb`
  (needed to add a track) requires re-signing the .cbk with hash72.** Playlist
  edits only touch `Library.itdb` / `Dynamic.itdb`, which are not checksummed.
- Music source found at `\raspberrypi\CS-1\Music` (≈700 MP3, 287 FLAC, 44 M4A,
  41 WAV, 255 WMA).
