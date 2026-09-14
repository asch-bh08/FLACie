# Overnight session status — 2026-09-14

Autonomous session working through the HANDOFF.md build order. This file is the
running log (newest entries at the bottom) and ends with the end-of-session
report. Every device write is listed with its pre-write backup folder.

## Session log

> Correction (01:45): the time ranges in these headings were first written as
> rough estimates and were wrong; they now reflect the real clock (git commit
> times). Times in the write tables were always real — they come from the backup
> folder timestamps.

### 00:03–00:21 — setup, baseline, read-only survey

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

### 00:21–00:30 — two-database write pipeline + first live SQLite write

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

### 00:30–00:32 — playlist features re-verified through both databases (live)

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

### 00:32–00:44 — database signatures: hash72 + hash58 reproduced exactly

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

### 00:44–00:52 — track mirror (Library/Dynamic/Locations.itdb) + live sync to full parity

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

### 00:52–01:03 — CDB album/artist links, add-from-file fixes, live track feature pass

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

### 01:03–01:14 — HANDOFF step 3: transcode-on-add (done, live)

`addTrackFromFile` now takes `"transcode": "auto" | "alac" | "aac" | "never"`
(default auto = convert only what the iPod can't play). `Transcode/Transcoder.cs`:
- lossless sources (FLAC/APE/WavPack/PCM…) → **ALAC 16-bit stereo**, lossy
  (Opus/Ogg/WMA…) → **AAC 256k**; 44.1 kHz family → 44100, 48 kHz family → 48000
  (the device already holds iTunes-synced ALAC at both rates).
- SHA-256-keyed cache (`%LOCALAPPDATA%\ipodsync\transcode`), ffmpeg bit-exact:
  two transcodes of the same source are byte-identical, so the dry run and the
  write use the same file. Output probed (codec, channels, rate, duration ±0.25 s)
  and rejected otherwise. Tags from format or stream metadata (Ogg/Opus).

Bugs found and fixed on the way (all caught on the fake root, never live):
- **Reader:** sample rate of every 48 kHz track read as negative (signed shift of
  `rate << 16`) → SQLite mirror would have written 44100 Hz / wrong sample count.
- ALAC bitrate: TagLib reports 0; now iTunes' nominal rate×bits×channels
  (matches all 146 existing ALAC rows: 1536/2304/1411/2116).
- Deterministic file names could collide with an orphan already on disk (a
  fake-root write failed safely on this and restored); names now avoid existing
  files. Untagged titles no longer fall back to the cache file name.

| # | time | change-set | pre-write backup (`ipod-backups\…`) |
|---|------|------------|------------------|
| 12 | 01:14 | add 24-bit/96 kHz **FLAC** test tone → ALAC 16/48 → `F01/08X8.m4a`, album `iPodSync Transcode Test`, playlist `iPodSync Playlist Test` | `applyedits-20260914-011427` |
| 13 | 01:14 | add **Opus** test tone → AAC 256k → `F08/SV6N.m4a`, same album/playlist | `applyedits-20260914-011430` |

Both: all device checks PASS; dry run and write identical; `itlp-diff` IN SYNC;
CDB + cbk signatures valid. Device now 638 tracks.

**Morning on-screen check (after ejecting):** Playlists → `iPodSync Playlist Test`
should list Smack That, Lights → now titled `ipodsync RENAMED TEST`, Rolling in the
Deep, `iPodSync Transcode Tone FLAC` (15 s two-tone chord), `iPodSync Transcode
Tone Opus` (12 s tone). Both tones should play. Artists → iPodSync; Albums →
`iPodSync Transcode Test`.

### 01:14–01:21 — HANDOFF step 4: album artwork — research pass (read-only)

Decoded from the real device (`ArtworkDB` + 4 `.ithmb` files, 529 images):
- Structure: `mhfd` (next image id @0x1C = 817) → `mhsd` 1 images (`mhli`),
  2 photo albums (`mhla`, empty), 3 files (`mhlf`: formats 1056/1078/1073/1074).
- `mhii`: id @0x10 == SQLite `item.artwork_cache_id` == CDB mhit @0x160;
  representative track pid @0x14; **reference count @0x38 = number of tracks
  using the image (529/529 match)**; source image size + 1 @0x30 where the track
  records one (mhit @0x80). CDB mhit @0xA4 = 1 has art / 2 none; u16 @0x7C = artwork count.
- Four thumbnails per image, one `mhni` each: 1056 = 128×128, 1078 = 80×80,
  1073 = 240×240, 1074 = 50×50; **RGB565 little-endian** (verified visually:
  image 288 decodes to *The Marshall Mathers LP* for "Stan"; big-endian is noise).
  Non-square art is scaled to fit and **centred**: @0x1C/@0x1E = top/left padding,
  @0x20/@0x22 = padding + content height/width (verified on a letterboxed image).
- Thumbnails are stored contiguously, one slot per image, no gaps (file size =
  529 × slot size for all four files) → adding art = append to each ithmb.
- `mhaf` inside mhod type 6: length is its header (96); its +0x08 is not a total.
- `ArtChunk` / `ArtworkDb` lossless tree + read-only `art-check`: **ArtworkDB
  round-trips byte-identical**; all 2,116 thumbnails in range; 0 dangling track
  links; 0 reference-count mismatches.

### 01:21–01:30 — HANDOFF step 4: album artwork — writer (done, live)

New ops: `setTrackArtwork` (`trackId` or `trackIds` + `imagePath`: an image, or an
audio file with an embedded cover; one image shared by all listed tracks),
`removeTrackArtwork`; `addTrackFromFile` copies the source file's embedded cover
automatically (also when transcoding; `"artwork": false` to skip); `removeTrack`
releases its image. Implementation (`Artwork/`):
- New image = clone of a real `mhii`/`mhni` layout; id from mhfd next-id; thumbnails
  appended at the end of each ithmb; reference count kept (replaced/removed art
  decrements, an image at 0 references is removed; its slots stay unused).
- Thumbnails via ffmpeg (bit-exact): fit + centred letterbox for 1056/1073/1074;
  **centre-crop for 1078** — found by noticing iTunes never pads that format
  (decoded the iTunes 80×80 of a wide image: it is cropped, not letterboxed).
- CDB mhit +0xA4/+0x160/+0x7C/+0x80 and SQLite artwork_status/artwork_cache_id
  written together; `itlp-diff` now cross-checks artwork links.
- Pipeline: backs up `Artwork/` as well (85 MB, SHA-1 verified), re-validates the
  new ArtworkDB before writing, appends only after confirming each ithmb length,
  verifies ArtworkDB bytes + integrity on the device, and restores by truncating
  each ithmb to its backed-up length. Fault-injected test on the fake root
  restored all 7 files SHA-1-identical. Every new thumbnail was decoded back to
  PNG and inspected (letterbox, crop, square, embedded cover).

| # | time | change-set | pre-write backup (`ipod-backups\…`, incl. Artwork) |
|---|------|------------|------------------|
| 14 | 01:29 | `setTrackArtwork` test pattern (600×600 PNG) → both transcode tones (image #817, 2 refs) | `applyedits-20260914-012917` |
| 15 | 01:29 | replace the Opus tone's art with a wide 800×450 JPEG (image #818; #817 → 1 ref) | `applyedits-20260914-012929` |
| 16 | 01:29 | add `test-cover.flac` (embedded PNG cover) → ALAC `F10/DEIN.m4a` + image #819 | `applyedits-20260914-012941` |

All PASS incl. `device artwork integrity`; afterwards `art-check D:/`: ArtworkDB
round-trips, 532 images, all thumbnails in range, 592 tracks with art, 0 dangling
links, 0 reference-count mismatches; `itlp-diff` IN SYNC; signatures valid. A
thumbnail read straight back off the iPod decodes to the right cover.

**Morning on-screen check:** Now Playing art for `iPodSync Transcode Tone FLAC`
(colour test pattern), `…Tone Opus` (SMPTE bars, letterboxed; 80×80 list thumb
cropped), `iPodSync Cover Art Tone` (test pattern). Existing albums' art should be
unchanged.

### 01:30–01:41 — HANDOFF step 5: folder/NAS sync (done, live)

`sync-folder <ipod-root> <music-folder> [--yes] [--batch N] [--limit N]
[--playlist name] [--remove-missing]`:
- Scans a folder (MP3/AAC/ALAC/WAV/AIFF/FLAC/Ogg/Opus/WMA/APE/WavPack), reads tags.
- **Off-device manifest** per device library + source folder
  (`%LOCALAPPDATA%\ipodsync\manifests\<library id>-<folder hash>.json`):
  source file → track persistent id, origin `added` / `adopted`.
- Per file: already synced · **already on the iPod** (normalised title + first
  artist + duration ±2.5 s, or a loose fallback for artist-in-title tags:
  duration ±1.5 s and both device title and artist appear in the source's
  title/artist/file name) → adopted, not copied · **duplicate inside the
  folder** (e.g. FLAC + Tidal M4A of the same song; keeps lossless, then larger)
  · to add.
- Free-space check (200 MB kept spare); dry run proves the first batch end to end;
  adds go through the verified pipeline in batches (transcode, embedded artwork,
  both databases, signing), manifest saved after each verified batch;
  `--remove-missing` only removes tracks this sync *added*. Re-running is a no-op.

Source music: `\raspberrypi\CS-1\Music` drops its SMB session every few minutes
tonight ("network name is no longer available"), so test folders were copied to
local disk first (`AAC-M4A` 55 MB, `Car Playlist` 740 MB) and synced from there.

Matching was checked by hand against the device before any live write, which
caught two planner gaps that are now fixed: artist-in-title tags ("NOTION -
CHRYSTAL - THE DAYS" was about to be re-added although the iPod has it), and a
lossy duplicate of an adopted song. After the live writes, a re-check caught two
more (fixed, nothing on the device affected): a second folder's sync replaced
the first folder's manifest, and lossy copies of already-synced songs were not
treated as duplicates.

| # | time | sync | pre-write backup (`ipod-backups\…`, incl. Artwork) |
|---|------|------|------------------|
| 17 | 01:39 | `AAC-M4A`: 5 already on the iPod adopted; **added** `AbbyLara – Timber (feat. Ke$ha)` (AAC, 25 MB, with its cover) | `syncfolder-20260914-013854` |
| 18a | 01:39 | `Car Playlist` `--limit 3 --batch 2`, batch 1: 8 adopted; **added** FLAC→ALAC `Kill The Lights (Audien Remix)`, `Love Potions` (with covers) | `syncfolder-20260914-013926` |
| 18b | 01:39 | batch 2: **added** FLAC→ALAC `Deftones – Mascara` (with cover) | `syncfolder-20260914-013945` |

All batches WRITE VERIFIED; afterwards `itlp-diff` IN SYNC, `art-check` clean
(596 tracks with art), CDB + cbk signatures valid. Re-plans: `AAC-M4A` → 6 in
sync, 0 to add; `Car Playlist` → 11 in sync, 15 duplicates skipped, **8 still to
add** (218 MB) — deliberately left for Ashley (device has ~1.3 GB free). All
real songs added tonight were also put in `iPodSync Playlist Test` so they are
easy to find; remove them from that playlist in the player if unwanted.

### 01:41–01:44 — HANDOFF step 6 (optional): star ratings (done, live)

Finding: the nano 5G keeps per-track stats in `Dynamic.itdb` → `item_stats`.
iTunes keeps it equal to the CDB (`play_count_user` == CDB play count for all
643 tracks). The only rated track in the CDB — the test track rated 5★ by last
session's CDB-only write — had `user_rating` 0 there, which matches "the
engine's rating isn't what Now Playing shows". The CDB offset itself was right
(+0x1F, as libgpod reads it; compilation +0x1E, app rating +0x79).

Change: `setTrackRating` / `setPlayCount` also write `item_stats.user_rating`
(20 per star, libgpod's convention) / `play_count_user` — **only for tracks the
change-set sets**, because the iPod updates item_stats itself (rating from Now
Playing, plays) and a CDB value isn't necessarily newer. New tracks get their
CDB rating/plays. `itlp-diff` lists remaining differences as information.

| # | time | change-set | pre-write backup |
|---|------|------------|------------------|
| 19 | 01:43 | `setTrackRating` 3★ on `ipodsync RENAMED TEST` (#37499): CDB 100 → 60, item_stats 0 → 60 | `applyedits-20260914-014339` |

Verified; exactly that one Dynamic.itdb value changed (fake-root dump diff);
`itlp-diff`: 0 rating/play-count differences, IN SYNC.
**Morning on-screen check:** Now Playing for `ipodsync RENAMED TEST` should show
3 stars. If it still shows none, the rating source is elsewhere — restore is not
needed (harmless), but note it.

### 01:44–02:00 — hardening after the build order

- **Play Counts alignment** (`e6d23a6`): the firmware's `Play Counts` file is
  matched to tracks by position. Checked on the device: 635 entries, 0 pending
  plays, and all 635 positions still hold the same tracks as the original iTunes
  CDB (tonight only appended tracks and only removed appended ones). Any future
  write that changes positions now realigns the file by persistent id (tested on
  the fake root by removing an original mid-list track: 634 entries, each its own
  track's).
- **Regression suite** `tools/fake-root-regression.sh <ipod|backup>` (`b410ef7`):
  every op + folder sync + fault-injected restore against a fake root, with
  in-sync / artwork / signature checks after each write. **First run found a real
  bug**: after an image's last reference is removed, its ithmb slots are unused and
  the append guard refused all later artwork writes. Fixed; suite passes. (Never hit
  on the device.)
- **Player UI** (`a39778f`): star rating + cover art (track or whole album) in the
  edit dialog; add-songs accepts FLAC/Ogg/Opus/WMA/APE/WavPack. Tested in a browser
  via the new headless `tools/player-dev-server.py` against the fake root (staged
  rating → review → dry run through the engine: all checks passed). The launcher copy
  `C:\IPODAPP\ipodsync\ipod-player.html` was updated (previous copy kept as
  `ipod-player.html.bak-20260914`).
- **Docs**: EDIT-PROTOCOL.md and README.md rewritten for the current engine.
- **`import-playlist`** (`iTunes export .txt` / `.m3u`): matches entries to tracks on
  the iPod and creates the playlist or appends missing tracks (`--replace` to
  mirror exactly). Validated read-only against the NAS exports vs the device's own
  playlists: `2026.txt` 15/15 and `aura.txt` 17/17 already in their `(LAC)`
  playlists; `HoodTrap(LAC).txt` 38/39 (the miss is the track renamed in testing).

| # | time | change | pre-write backup |
|---|------|--------|------------------|
| 20 | 01:58 | `import-playlist Playlist.txt --name "iPodSync Import Test"`: new playlist, 27/27 entries matched | `importplaylist-20260914-015838` |

(The dry run for #20 was the immediately preceding command on the same device
state; it passed.) Verified; IN SYNC; signatures valid. Note: the new playlist got
persistent id `0xF906EE395DA00877`, the id the deleted `iPodSync Temp` used
earlier (ids are "max + 1"); SQLite has no trace of the old one.
