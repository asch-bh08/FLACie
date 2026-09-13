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

### 01:05–01:35 — playlist features re-verified through both databases (live)

Each op tested first on a fake device root (copy of the live bundle +
zero-byte audio placeholders), then run live on `D:` one feature per write,
each gated on a passing dry run. All device checks PASS on every write
(CDB bytes, re-read, byte-identical round-trip, track/playlist counts, all 636
audio files present, SQLite bytes == staged proof, integrity_check, playlists
in sync between CDB and SQLite). A track-metadata edit (`setTrackFields`) is
correctly **refused** by the pipeline until the track mirror exists.

| # | time | change-set | pre-write backup (`C:\IPODAPP\ipodsync\ipod-backups\…`) |
|---|------|------------|------------------|
| 2 | 00:31 | `iPodSync Test`: add 3 tracks, reorder, remove 1 → Smack That, Lights (renamed test track), Rolling in the Deep | `applyedits-20260914-003149` |
| 3 | 00:32 | rename `iPodSync Test` → `iPodSync Playlist Test` | `applyedits-20260914-003204` |
| 4 | 00:32 | create `iPodSync Temp` (2 tracks) — container, container_ui, name_order 900, pop(LAC) re-ranked | `applyedits-20260914-003207` |
| 5 | 00:32 | delete `iPodSync Temp` | `applyedits-20260914-003210` |

Evidence the pair #4/#5 is an exact inverse: the device CDB after #5 is
byte-identical to after #3 (SHA-1 `228D4A27…`), and `Dynamic.itdb`'s SQL
content after #5 equals its content before #4.

**Morning on-screen check (after ejecting):** Playlists menu should show
`iPodSync Playlist Test` (3 songs: Smack That, Lights…, Rolling in the Deep),
and should *not* show `iPodSync Temp`.

Playlist features — create, rename, delete, add/remove track, reorder — are
now written to **both** databases in one operation and verified on the device
files. Remaining playlist caveat: firmware display not yet eyeballed.

### 01:35–02:05 — database signatures: hash72 + hash58 reproduced exactly

Why this matters: the device is an **iPod nano 5G** (USB `VID_05AC&PID_1265`).
Its iTunesCDB header carries hash58 (scheme 1) *and* a hash72 signature, and
`Locations.itdb.cbk` carries hash72. **Every CDB ipodsync has written since
Sep 13 kept iTunes' old signature bytes**, so the device's CDB signatures are
currently stale (`hash72-verify D:/` → "does NOT validate"). Whether the
firmware rejects that is unknown (can't eject tonight), but it is not what
iTunes produces. Adding/removing tracks also needs Locations.itdb → cbk re-signing.

Implemented (independent C#, not LGPL code: hash72's generate/extract is
WTFPL, hash58's reference is BSD-licensed; the S-boxes are the FIPS-197 AES
tables, generated rather than copied, and compared equal to libgpod's in all
256 entries):
- `Signing/Hash72.cs` + `hash72-verify`: on the original iTunes CDBs of **both**
  nano 5Gs seen (D: `3dcaf899…` from Sep 7, and the G: backup) the header
  hash72 validates; every `Locations.itdb.cbk` validates, and **rebuilding the
  cbk from Locations.itdb with the recovered (iv, random) pair is
  byte-identical** to the iTunes-written file.
- `Signing/Hash58.cs` + `hash58-verify`: with FirewireGuid = USB serial
  `000A27001E7D86AB`, HMAC over the compressed CDB with db id / 0x32 / hash58
  zeroed **reproduces the original iTunes hash58 exactly**
  (`322B2633BB46E313…`). hash72 is computed with hash58 zeroed, hash58 with
  hash72 in place → sign hash72 first, hash58 last.
- Wired into the write pipeline (`DeviceSigning`): every CDB write is signed
  (hash72 then hash58; a check proves only the 66 signature bytes differ),
  `Locations.itdb.cbk` is rebuilt whenever `Locations.itdb` changes, and the
  device must pass signature validation after the write or it is restored. Key
  material is discovered, never stored: hash72 pair from the device's valid
  cbk; FirewireGuid candidates from the Windows USB registry, accepted only if
  one reproduces the hash58 of the device CDB or of an iTunes-written backup
  with the same library id. Without proof, writes to a signed database are
  refused (`--allow-unsigned` to override). Tested on the fake root incl. a
  fault-injected restore.

**LIVE WRITE #6 — 00:43 — `itlp-sync D:/ --resign --yes`** (fix stale CDB signatures)
- Pre-write backup: `C:\IPODAPP\ipodsync\ipod-backups\itlpsync-20260914-004335`
- Only the CDB header's hash58 (0x58–0x6B) and hash72 body (0x74–0x9F) changed.
  Device verify all PASS; `hash72-verify D:/` → CDB hash72 valid, cbk valid;
  `hash58-verify` → MATCH. The device's databases now carry the signatures
  iTunes would have written for this content.
