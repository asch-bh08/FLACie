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

### 00:40–01:05 — two-database write pipeline + first live SQLite write

Built (commit `d868d02`):
- `itlp-diff` (`ItlpCompare`): read-only CDB-vs-SQLite verifier keyed by
  persistent id. On the untouched device it matched all 635 shared tracks and
  all 6 original playlists' memberships exactly; the only differences were the
  four CDB-only edits from last session (1 added track, 1 renamed track's
  title/artist/album, the `iPodSync Test` playlist, master 636 vs 635).
- `ItlpSync`: playlist mirror onto a **staged copy** of the bundle.
  Real-row findings encoded: `item_to_container.shuffle_order` is NULL;
  `Dynamic.itdb` has a `container_ui` row (1,0,1,0,0,0) per playlist;
  `container.name_order` = master 100, then every CDB playlist *including the
  built-in smart ones that have no container row* sorted case-insensitively,
  ×100 (reproduces all 7 existing values; re-proven on every run, write refused
  if it ever doesn't).
- `WritePipeline` (`apply-edits`, new `itlp-sync`): integrity_check, per-table
  content hashes (only declared tables may change), refuses any write that adds
  new track divergence, SHA-1-verified backup, read-back verify, full device
  re-verify, automatic verified restore on failure. Restore path proven with a
  fault-injected write on a fake device root.

**LIVE WRITE #1 — 00:30 — `itlp-sync D:/ --yes`** (mirror `iPodSync Test` into SQLite)
- Pre-write backup: `C:\IPODAPP\ipodsync\ipod-backups\itlpsync-20260914-003002`
- Changed: `Library.itdb` (container row for `iPodSync Test`, name_order 800;
  `pop(LAC)` re-ranked 1000→1100; 1 membership row), `Dynamic.itdb`
  (container_ui row). Device verify: all PASS. Independent Python sqlite3 dump
  of a read-back copy vs baseline: exactly those 5 rows differ; SHA-1 of the
  whole `iTunes/` + `Artwork/` tree vs baseline: exactly those 2 files differ.
- Confidence: high at the file level. **Not yet seen on the iPod screen** — the
  device must stay mounted all night (no eject), so the firmware won't reload
  its databases until Ashley ejects it. Morning check: Playlists menu should
  show `iPodSync Test`.
- Note: new playlist persistent ids are "max existing + 1"
  (`0xF906EE395DA00876` = `AA`'s id + 1). Unique, just adjacent.
