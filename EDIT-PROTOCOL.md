# Edit protocol — the bridge between the iPod Player and the ipodsync writer

The [iPod Player](ipod-player/) is a read-only browser app. To let it *edit* an
iPod's contents **without** growing a second, unproven database writer in
JavaScript, the player never writes the `iTunesDB` itself. Instead it emits a
**change-set** — a plain JSON description of the edits the user made — and hands
it to the verified C# engine (`IpodSync`), which applies it through the
round-trip-proven writer, backing up first and only touching the device after an
explicit confirmation.

```
 iPod Player (JS)                 ipodsync engine (C#)
 ┌────────────────┐   change-set  ┌───────────────────────────┐   gated write
 │ edit UI, stages│──── JSON ────▶│ apply-edits:              │──────────────▶ iPod
 │ ops in memory  │               │  1. back up iTunes/       │  (only after
 │ (no DB write)  │               │  2. apply ops on RawChunk │   user confirms)
 └────────────────┘               │  3. round-trip self-check │
                                   │  4. write DB back         │
                                   └───────────────────────────┘
```

## Transport

For now the player writes the change-set to
`iPod_Control/.ipodsync/pending-edits.json` (or a path chosen by the helper
server), and the engine is invoked as:

```
IpodSync.Cli apply-edits <ipod-root> --changes pending-edits.json [--dry-run] [--yes]
```

- `--dry-run` (default): apply in memory, run every round-trip check, report what
  *would* change. **Never writes to the device.**
- `--yes`: perform the real device write. Refuses unless a fresh backup of
  `iPod_Control/iTunes/` exists. This is the only mode that touches hardware.

Later this same command can be exposed over the helper server's HTTP API so the
player can call it directly; the JSON contract stays identical either way.

## Change-set schema (v1)

```jsonc
{
  "version": 1,
  "dbPath": "D:/iPod_Control/iTunes/iTunesDB",
  "ops": [ /* applied in order */ ]
}
```

Tracks are addressed by their existing **track id** (the `mhit` id the reader
already surfaces); playlists by **name** (or persistent id when available).

| op | fields | writer status |
|----|--------|---------------|
| `setTrackFields` | `trackId`, `fields:{title?,artist?,album?}` | **Implemented** (`EditApplier`). Title = round-trip proven; artist/album reuse the identical string-`mhod` mechanism and pass the reader-reparse + idempotent checks — dry-run verified on a real device. |
| `setTrackRating` | `trackId`, `stars` (0–5) | **Implemented**, proven (fixed-size field). |
| `setPlayCount` | `trackId`, `count` | **Implemented**, proven (fixed-size field). |
| `removeTrack` | `trackId` | **Implemented**, proven (removes `mhit` + every referencing `mhip`). |
| `addTrackToPlaylist` | `playlist`, `trackId`, `position?` | **Implemented**, proven for tracks already on the device. |
| `removeTrackFromPlaylist` | `playlist`, `trackId` | **Implemented**; same `mhip`-deletion mechanism as `removeTrack`, passes the reparse + idempotent checks — dry-run verified on a real device. |
| `reorderPlaylist` | `playlist`, `trackIds[]` | **Implemented**; rearranges existing `mhip`s (no bytes built). Round-trip verified. |
| `createPlaylist` | `name`, `trackIds[]` | **Implemented**; clones a real user-playlist `mhyp` as a template + real `mhip`s, fresh persistent id. Round-trips and re-reads correctly — **on-device acceptance not yet verified** (no hardware). |
| `renamePlaylist` / `deletePlaylist` | `playlist`, (`name`) | **Implemented**; round-trip verified. |

> **Playlists live in more than one dataset.** iTunes writes each playlist into
> two `mhlp` datasets on these devices; the reader de-dups by persistent id. So a
> per-playlist edit (rename/reorder/delete/add/remove-entry) is applied to **every
> copy** with that persistent id, or the de-dup can resurface a stale one. (A first
> cut of delete edited only one copy and the playlist "came back" — the round-trip
> test caught it.) `removeTrack` already spans all playlists.
| `addTrackFromFile` | `sourcePath`, `playlist?` | **Implemented.** Reads real duration/bitrate/tags (TagLibSharp); fresh track id + collision-free persistent id; a scrambled `F##/XXXX.ext` path; clones a same-extension `mhit` as a template then patches numeric fields + clean string mhods; adds to the track list and every master-playlist copy. `apply-edits --yes` copies the file onto the device after the DB write. Round-trips + re-reads correctly (`addtrack-test`); on-device firmware acceptance unverified (no hardware). Transcode-on-add (for FLAC etc.) is future work — for now the source must already be an iPod-playable format (MP3/AAC/ALAC/AIFF/WAV). |

The implemented ops live in `IpodSync.Core/ItunesDb/EditApplier.cs`, invoked by `IpodSync.Cli apply-edits`. Every apply (dry-run or `--yes`) re-reads its result through the verified `ItunesDbReader` and re-serializes it to confirm the writer agrees with itself; `--yes` refuses unless all checks pass and it backs up `iPod_Control/iTunes/` first.

## Safety (inherited from HANDOFF.md — non-negotiable)

1. **No device write until the edit round-trips.** Every op must pass the same
   two-way proof the existing mutations do (semantic re-read + idempotent
   re-serialize) before `--yes` will write it.
2. **Back up `iPod_Control/iTunes/` first.** `--yes` refuses without a fresh backup.
3. **Ask the user before the first real write to a device**, even for a proven edit.
4. The player's staging/preview is always safe; only `apply-edits --yes` is not.
