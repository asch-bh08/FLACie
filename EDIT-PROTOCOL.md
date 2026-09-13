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
| `setTrackFields` | `trackId`, `fields:{title?,artist?,album?}` | title = **proven** (rename). artist/album share the same `mhod` mechanism — needs its own round-trip test before trusting. |
| `setTrackRating` | `trackId`, `stars` (0–5) | **proven** (fixed-size field). |
| `setPlayCount` | `trackId`, `count` | **proven** (fixed-size field). |
| `removeTrack` | `trackId` | **proven** (removes `mhit` + every referencing `mhip`). |
| `addTrackToPlaylist` | `playlist`, `trackId`, `position?` | **proven** for tracks already on the device. |
| `removeTrackFromPlaylist` | `playlist`, `trackId` | Same `mhip`-deletion mechanism as `removeTrack`; **needs its own round-trip test**. |
| `reorderPlaylist` | `playlist`, `trackIds[]` | To build (reorder `mhip`s; round-trip first). |
| `createPlaylist` | `name`, `trackIds[]` | To build (new `mhyp` + `mhip`s; round-trip first). |
| `renamePlaylist` / `deletePlaylist` | `playlist`, (`name`) | To build. |
| `addTrackFromFile` | `sourcePath`, `playlist?`, `transcodeTo?` | **Hardest, not built in either app.** Needs a collision-free persistent id + track id, a scrambled `F##/XXXX.ext` path picked the way the device does, real duration/bitrate/size read off the audio file, and copying the file onto the device. See HANDOFF.md "Constructing a brand-new track". |

## Safety (inherited from HANDOFF.md — non-negotiable)

1. **No device write until the edit round-trips.** Every op must pass the same
   two-way proof the existing mutations do (semantic re-read + idempotent
   re-serialize) before `--yes` will write it.
2. **Back up `iPod_Control/iTunes/` first.** `--yes` refuses without a fresh backup.
3. **Ask the user before the first real write to a device**, even for a proven edit.
4. The player's staging/preview is always safe; only `apply-edits --yes` is not.
