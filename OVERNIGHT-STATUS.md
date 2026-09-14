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

### 02:05–02:55 — track mirror (Library/Dynamic/Locations.itdb) + live sync to full parity

Research against the device's own rows (all encoded in code, all re-checkable):
- `ItlpSorting` sort-name rule reproduces `sort_title/artist/album/album_artist/composer`
  for all 635 items; the collation reproduces title/artist/album-artist/composer
  ranks with 0 inversions (1 album pair of ~2,800 differs). `itlp-orders-check`.
- Entities: `track_artist` (small int pid) ↔ `item.artist`; `artist` (64-bit pid)
  named by album artist, else track artist, else the is_unknown row; `album`
  keyed by name + artist pid; unknown album/composer/genre rows carry
  name_order 4294967295; item.genre_order = 100 × case-insensitive genre rank;
  `item.physical_order` == master-playlist position; `location` uses 4CC
  `'FILE'` / `'M4A '` / `'MP3 '`, `kind_id` → `location_kind_map`;
  `avformat_info` 301 MP3 / 502 AAC / 601 ALAC with duration in samples.

`ItlpTrackSync` mirrors removed / changed / new tracks (item, avformat_info,
location, item_stats, lyrics/chapters on removal, entity rows created with
neighbour-placed ranks). The pipeline now requires **tracks fully in sync**
(not just "no new divergence") and rebuilds + verifies the cbk. Master
playlist appends insert only the new rows.

**LIVE WRITE #7 — 00:51 — `itlp-sync D:/ --yes`** (mirror last session's CDB-only track edits)
- Pre-write backup: `C:\IPODAPP\ipodsync\ipod-backups\itlpsync-20260914-005144`
- Library.itdb: renamed test track (`iPodSync – ipodsync RENAMED TEST`, album
  `Edit Test`, new track_artist/artist/album rows); added `EsDeeKid – Century`
  (item, avformat_info, master membership). Dynamic.itdb: item_stats.
  **Locations.itdb: location row → Locations.itdb.cbk re-signed** (first cbk
  write on the device; generation proven byte-identical against iTunes' file).
- Device verify all PASS incl. `tracks in sync`, `Locations cbk valid`.
  `itlp-diff D:/` → **IN SYNC**. `hash72-verify D:/` → CDB + cbk valid.
- The device's CDB and SQLite library now agree completely, both signed.

### 02:55–03:25 — CDB album/artist links, add-from-file fixes, live track feature pass

Found while checking what add-from-file writes: last session's
`AddTrackFromFile` cloned a template track's whole mhit header, so the added
EsDeeKid track carried the template's **second persistent-id copy (+0xA8)**,
sample count (+0xBC), size copy (+0x12C), date added (+0x68), and its
**album/artist list links** (+0x120 / +0x1E0 → the template's `????????`
album/artist). Each offset was confirmed across all 636 tracks (e.g. +0xA8 ==
persistent id for every iTunes-written track; +0x120 → mhia whose persistent id
== SQLite `item.album_pid` for all 596 tracks with an album).

Fixed (commit `8ecdae2` + determinism follow-up):
- `RawChunk` parses mhla/mhli/mhia/mhii structurally; **all 21 databases on
  hand still round-trip byte-identically**. Reader exposes the lists + links.
- `EntityLinks` finds/creates album & artist entries (ids from the shared
  track/list counter, mhod layout identical to every real entry).
- `AddTrackFromFile` sets every per-track field itself, prefers a no-artwork
  template, reads album artist / composer / track+disc counts / codec (ALAC vs
  AAC) from tags, and picks an **existing** `F##` folder (device has F00–F13).
- `setTrackFields` re-links on artist/album changes; `relinkTrack` op.
- SQLite sync reuses the CDB entity pids; verifier checks album_pid/artist_pid.
- Random choices (persistent ids, file names) are seeded from the CDB bytes +
  change-set, so **a dry run now previews exactly what `--yes` writes** (before
  this fix, live #9's dry run showed a different path/pid than the real run —
  harmless, since every write re-proves its own bytes, but not a true preview).

| # | time | change-set | pre-write backup (`ipod-backups\…`) |
|---|------|------------|------------------|
| 8 | 01:00 | `relinkTrack` ×2: repair the two test tracks' album/artist links (CDB) → SQLite re-pointed to the same pids, 2 stray rows pruned | `applyedits-20260914-010021` |
| 9 | 01:02 | `addTrackFromFile` test tone MP3 (ffmpeg-generated, tagged) → +playlist `iPodSync Playlist Test` | `applyedits-20260914-010216` |
| 10 | 01:02 | `setTrackFields` retag title + album of that track (relinks album) | `applyedits-20260914-010218` |
| 11 | 01:02 | `removeTrack` that track | `applyedits-20260914-010220` |

All four: every device check PASS (incl. CDB signatures, cbk valid, tracks +
playlists in sync); after #11 `itlp-diff D:/` → IN SYNC, `hash72-verify` valid.
Side effect of #9 (pre-fix code): the tone's audio was copied to a new folder
`D:\iPod_Control\Music\F33\RB76.mp3` (481,649 bytes). After #11 it is an
unreferenced file — left in place per the no-deleting rule; harmless.

**HANDOFF step 2 status:** create-playlist, playlist rename/delete,
add/remove/reorder playlist tracks, rename/retag track, add track from file and
delete track now all write CDB + SQLite in one verified operation, confirmed on
the device's files. Not yet confirmed on the iPod's screen (no eject tonight).
