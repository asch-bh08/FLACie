# Edit protocol — the bridge between the iPod Player and the ipodsync writer

The [iPod Player](ipod-player/) is a browser app. To let it *edit* an iPod's
contents **without** growing a second, unproven database writer in JavaScript, the
player never writes device files itself. Instead it emits a **change-set** — a
plain JSON description of the edits the user made — and hands it to the verified
C# engine (`IpodSync`), which applies it through the round-trip-proven writer.

```
 iPod Player (JS)                 ipodsync engine (C#) — WritePipeline
 ┌────────────────┐   change-set  ┌────────────────────────────────────────┐   verified write
 │ edit UI, stages│──── JSON ────▶│ 1. CDB edit in memory (+ re-parse,      │──────────────▶ iPod
 │ ops in memory  │               │    idempotent re-serialize)             │  (only with --yes,
 │ (no DB write)  │               │ 2. SQLite bundle synced on a STAGED copy│   only if every
 └────────────────┘               │ 3. artwork / transcode / Play Counts    │   check passed)
                                   │ 4. signatures regenerated + validated   │
                                   │ 5. backup (SHA-1 verified) → write →    │
                                   │    read back → re-verify the device →   │
                                   │    restore the backup on any failure    │
                                   └────────────────────────────────────────┘
```

## Two databases (nano 5G and similar)

These iPods keep the classic `iPod_Control/iTunes/iTunesCDB` **and** a SQLite
bundle `iPod_Control/iTunes/iTunes Library.itlp/` (`Library.itdb`, `Dynamic.itdb`,
`Locations.itdb` + signed `Locations.itdb.cbk`, `Extras.itdb`). The firmware's
menus, search, and Now Playing stats come from the SQLite side. **Every op below
writes both in one operation**; a write is refused unless, afterwards,
`itlp-diff` finds the two in sync (tracks, fields, album/artist identity,
artwork links, playlists, memberships). Rating/play-count differences are
reported separately because the iPod updates `item_stats` itself.

The CDB header's hash58 + hash72 and the cbk's hash72 are regenerated on every
write, using key material proven against an existing iTunes signature for that
device (see `OVERNIGHT-STATUS.md`). Writes to a signed database are refused
without that proof (`--allow-unsigned` overrides).

## Transport

```
IpodSync.Cli apply-edits <ipod-root> --changes <file.json> [--yes] [--backup-root dir]
```

- default: **dry run** — everything above except the write. Random choices (new
  persistent ids, file names) are seeded from the database + change-set, and
  transcodes are cached, so a dry run shows exactly what `--yes` will write.
- `--yes`: backs up `iPod_Control/iTunes/` (plus `Artwork/` when artwork changes) to
  `<backup-root>/<label>-yyyyMMdd-HHmmss/`, verifies the backup, writes, reads every
  file back, re-verifies the device, and **restores the backup automatically** if
  any check fails. `write-log.txt` is saved next to the backup.
- The player's helper server (`ipod-server.ps1`) exposes the same command as
  `POST /api/apply-edits` (`?commit=1` for `--yes`); the JSON contract is identical.
- `IpodSync.Web` also serves `POST /api/apply-edits?root=<ipod-root>` for remote clients (ipodplayer's Sync
  mode): same body, same pipeline, two extra gates. Only `setTrackFields`, `setTrackRating` and the playlist ops
  are accepted (nothing that names a file on the host, no `removeTrack`), and `commit=1` must carry the
  `confirmToken` a clean dry run returned for the identical change-set against the unchanged database
  (`&confirm=<token>`), so a write always follows a preview the user saw. Response: `{dryRun, ok, exitCode,
  written, restored, backupDir, ops[], problems[], log[], confirmToken}`. Ops in one change-set can't address a
  playlist an earlier `createPlaylist` in the same set made -- put the final order in `createPlaylist.trackIds`.

Related commands: `itlp-sync <root> [--yes] [--resign]` (bring SQLite in line with
the CDB / re-sign), `itlp-diff`, `art-check`, `hash72-verify`, `hash58-verify`,
`sync-folder` (below). All read-only unless `--yes`.

## Change-set schema (v1)

```jsonc
{ "version": 1, "ops": [ /* applied in order */ ] }
```

Tracks are addressed by **track id** (`dump` prints `#id`); playlists by **name**.

| op | fields | status (2026-09-14) |
|----|--------|--------|
| `setTrackFields` | `trackId`, `fields:{title?,artist?,album?}` | ✅ both DBs; artist/album changes re-link the CDB album/artist lists and the SQLite entities. Live-verified on device files. |
| `setTrackRating` | `trackId`, `stars` 0–5 | ✅ CDB +0x1F **and** `Dynamic.itdb item_stats.user_rating` (20/star). Live-verified on files; on-screen check pending. |
| `setPlayCount` | `trackId`, `count` | ✅ CDB + `item_stats.play_count_user`. Fake-root verified. |
| `removeTrack` | `trackId` | ✅ both DBs (item, stats, location + cbk), releases artwork, realigns `Play Counts`. Audio file left on disk. Live-verified. |
| `addTrackFromFile` | `sourcePath`, `playlist?`, `transcode?` (`auto`\|`alac`\|`aac`\|`never`), `artwork?` (default true) | ✅ both DBs + album/artist entries + embedded cover art; non-iPod formats transcoded (lossless → ALAC 16-bit, lossy → AAC 256k). Live-verified (MP3, FLAC, Opus, M4A). |
| `setTrackArtwork` | `trackId` or `trackIds[]`, `imagePath` (image, or audio file with a cover) | ✅ ArtworkDB + ithmb thumbnails + CDB + SQLite; one image shared by all listed tracks. Live-verified on files (thumbnails decoded back). |
| `removeTrackArtwork` | `trackId` | ✅ Fake-root verified. |
| `relinkTrack` | `trackId` | ✅ repairs a track's album/artist list links from its strings. Live-verified. |
| `addTrackToPlaylist` | `playlist`, `trackId`, `position?` | ✅ both DBs. Live-verified. |
| `removeTrackFromPlaylist` | `playlist`, `trackId` | ✅ both DBs. Live-verified. |
| `reorderPlaylist` | `playlist`, `trackIds[]` | ✅ both DBs. Live-verified. |
| `createPlaylist` | `name`, `trackIds[]` | ✅ both DBs (container, container_ui, name_order rank). Live-verified. |
| `renamePlaylist` | `playlist`, `name` | ✅ both DBs. Live-verified. |
| `deletePlaylist` | `playlist` | ✅ both DBs. Live-verified. |

"Live-verified" = written to the real nano 5G and re-verified from its files
(both databases, signatures, artwork). **None of it has been eyeballed on the
iPod's own screen yet** — see `OVERNIGHT-STATUS.md` for the morning checklist.

> **Playlists live in more than one CDB dataset.** A per-playlist edit is applied
> to every copy with that persistent id, or the reader's de-dup can resurface a
> stale one.

## Folder / NAS sync

```
IpodSync.Cli sync-folder <ipod-root> <music-folder> [--yes] [--batch N] [--limit N]
                         [--playlist name] [--remove-missing]
```

Plans against an off-device manifest (`%LOCALAPPDATA%\ipodsync\manifests\`, per
device library + folder): files already synced, files **already on the iPod**
(adopted without copying), duplicates inside the folder (keeps lossless), and
files to add. Checks free space, then adds in batches of `addTrackFromFile` ops
through the same pipeline. `--remove-missing` only removes tracks the sync added.

## Safety (inherited from HANDOFF.md — non-negotiable)

1. Back up before every write (automatic, verified) — never skipped.
2. Dry-run first; `--yes` refuses unless every check passes.
3. Verify after every write; restore the backup on any failure.
4. Never write bytes that weren't proven off-device first (staged copy / in memory).
5. `tools/fake-root-regression.sh <ipod-or-backup>` runs every op above against a
   fake root and must pass before changing the write path.
