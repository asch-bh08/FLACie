# Handoff brief

For an agent or developer picking this up cold. Written 2026-09-07.

Read this, then [README.md](README.md) for the binary-format findings. The
README is the technical reference; this file is the context you cannot recover
by reading code.

---

## What this is

An iPod sync tool that replaces iTunes, targeting Windows first and Android
second. The user has a large collection of classic iPods and wants to manage
them from a PC *and* from their phone.

### Hard constraints from the user

These are decisions already made. Do not relitigate them.

- **Stock firmware stays.** Rockbox was raised and explicitly rejected — the
  user wants the real Apple UI. That is why we write Apple's database format
  instead of the much easier drag-and-drop route.
- **iTunes must keep working alongside us.** The user accepts that iTunes may
  occasionally wipe the device. Our design must make a wipe cost nothing but
  re-copy time.
- **Android is not optional.** A Samsung Galaxy Z Fold 7 is the target phone.
  Any architectural choice that makes Android impossible is wrong.
- **iPod Touch 4G: design for it, do not build it.** The user said "add it in
  but dont do it". Leave the seam in the architecture; write no iOS code.

### What "better than iTunes" means here

Non-destructive (no library-association wipes), two-way (pull play counts and
ratings back off the device), folder-based (no forced library import), FLAC via
transparent transcode, incremental, no account, no cloud.

---

## Current state

**Reading works and is verified against two of the user's real iPods.** The
database writer's round-trip test also passes byte-identically against both of
those same devices. Nothing writes to a device. That is deliberate — see
Safety below; round-trip passing is necessary but not sufficient for that.

```
dotnet build
dotnet run --project src/IpodSync.Cli -- detect
dotnet run --project src/IpodSync.Cli -- dump G:/ -n 20
dotnet run --project src/IpodSync.Cli -- roundtrip G:/
```

Verified output: 614 tracks / 11 playlists on one device, 635 / 11 on the other.
Titles, artists, albums, playlist names and membership, smart-playlist flags,
star ratings, play counts, durations, bitrates, and the scrambled `F##/XXXX.m4a`
paths all read correctly.

`roundtrip` reads a real database, parses it into a lossless chunk tree
(`RawChunk`, `src/IpodSync.Core/ItunesDb/RawChunk.cs`), serialises that tree
straight back to bytes, and diffs the result against the original — on both
`G:\` and `H:\` the inflated bytes match exactly, including the undecoded
`mhla`/`mhli` chunks and every header field the semantic reader doesn't model.
See "Decisions and why" below for how that tree is built and why it's a
separate parse from the verified reader rather than a rewrite of it.

### What is NOT done

- **No mutation API yet.** The round-trip proves we can reproduce an
  *unmodified* database byte-for-byte. There is no code path yet that changes
  a field, adds/removes a track, or edits a playlist and re-serialises —
  building one and round-tripping *that* is the next real gate before any
  write, not what has been proven so far.
- No signing. No sync engine. No transcode. No UI. No Android.
- `mhla` (albums) and `mhli` (unknown) chunks are preserved verbatim by the
  writer but still not semantically decoded by the reader.
- Artwork (`ithmb`) untouched.
- Model identification does not work — see the SysInfo gotcha below.

---

## The user's hardware

| Device | Notes |
|---|---|
| `G:\` 16 GB, FAT32 | Nano 5th gen. 614 tracks. Identified only from its master-playlist name. |
| `H:\` 8 GB, FAT32 | Unidentified. 635 tracks. Model unknown — SysInfo is empty. |
| iPod Nano 7G | Mentioned by the user; not yet seen plugged in. Needs hashAB. |
| iPod 5th gen 80 GB | Mentioned; hard-drive device, no signing required. Easiest write target. |
| iPod Touch 4G | Jailbroken, iOS 6.1.6. Deferred by user instruction. |
| Others | User: "all ipods basically beside the classics and touchs" — so no 6G/7G Classic. |

Both connected devices are **FAT32**, which is required for Android to reach
them at all. HFS+ (Mac-formatted) iPods are unreadable from Android and from
Windows without extra drivers.

The user has already disabled iTunes auto-sync. For coexistence they also need
each iPod set to **"Manually manage music and videos"** in iTunes — otherwise
iTunes deletes anything we added that is not in its library. This has been
explained to them; confirm it is actually set before any write test.

---

## Decisions and why

**C# / .NET 9.** Chosen after checking the machine: .NET 9 SDK present and
working, Java 8 **JRE only** (no `javac`), no Node, no Rust. An earlier
recommendation of Kotlin Multiplatform was reversed for this reason. Do not
switch stacks without a strong reason; the parser is the valuable part and it
ports more easily than platform shells do.

Known tradeoff: the obvious Android USB-mass-storage library (libaums) is Java.
On .NET for Android you either bind its AAR or write the SCSI Bulk-Only
Transport + FAT32 layer in C#. Unresolved, and fine to leave unresolved until
the Android phase.

**Parse by chunk magic, never by type number.** Published `mhsd` type tables are
wrong for the DB version these devices write. See README.

**The writer parses independently of the reader, into a lossless tree, rather
than re-deriving bytes from `ItunesDatabase`.** `ItunesDatabase`/`Track`/
`Playlist` (the semantic model `ItunesDbReader` builds) throw away everything
they don't model — unhandled `mhod` types, `mhla`/`mhli`, header bytes past
what's parsed, exact chunk ordering. Regenerating a file from that model would
mean *reconstructing* those bytes, which is exactly the risk the task called
out ("must be preserved verbatim, not regenerated"). Instead `RawChunk` is a
second, independent parse of the same file into a tree that keeps every byte:
each node holds its own header bytes and payload/children, `Serialize()`
recomputes only the handful of structural fields this format ties to the tree
shape (a chunk's own total length, its children's count), and anything it
doesn't have a named field for is copied through unchanged — including entire
chunk types it doesn't understand, like `mhla`/`mhli`, which it captures as a
named leaf and never touches. Keeping this as a second parse rather than
rebuilding `ItunesDbReader` on top of it means a bug in the tree can't corrupt
the already-verified reader; unifying them (so field edits made through the
semantic model flow into the tree) is future work once the round-trip needed
to prove that is in place.

**Sync manifest lives off-device**, on the PC/phone, keyed by the iPod's serial.
Never store our state on the iPod. This is what makes an iTunes wipe harmless.

**Project moved out of the session scratch workspace** to `C:\Users\Ashley\ipodsync`
because the scratch directory is deleted when the session ends, and its very long
path broke `dotnet run` with MAX_PATH errors.

---

## Safety — read before writing any code that touches a device

The user's iPods hold their actual music library. Treat them as production.

1. **Never write to a connected iPod until a *modified* database round-trips.**
   The identity case (parse a real file, re-serialise it unchanged, compare) now
   passes on both `G:\` and `H:\` — that only proves the tree faithfully
   captures what's already there. The real gate is round-tripping a database
   with an actual change made through code (a field edited, a track added or
   removed) and confirming the rest of the file is still untouched. Until a
   mutation path exists and passes that, a write is still a guess.
2. **Back up before the first real write.** Copy the whole `iPod_Control/iTunes/`
   directory off the device first.
3. **Prefer `H:\` for experiments** over `G:\` if a sacrificial device is needed —
   but ask the user first, do not assume either is expendable.
4. Do not delete files from a device to "clean up". Orphaned audio files are
   harmless; a deleted library is not.

---

## Next steps, in order

1. **Mutation API + round-trip on modified content.** The writer can currently
   only reproduce a database unchanged (see Current state). Add a way to edit a
   field (start with something low-risk and real, like play count or rating,
   since two-way sync will need exactly that) and prove the round-trip still
   holds for everything *except* the field that was intentionally changed.
2. **Signing.** Only needed once writing is proven. See the signature-region
   notes in the README, and check them against libgpod's implementation rather
   than trusting the dumps — libgpod is LGPL, so a port makes this project LGPL.
   Flag that to the user before porting any of it.
3. **Sync engine.** Content-hash manifest, off-device, keyed by serial.
4. **Transcode.** FLAC to ALAC or AAC on copy, recording the source so re-syncs
   do not re-transcode.
5. **Android.** Decide libaums-binding vs native C# SCSI/FAT32 at this point,
   not before.

---

## Gotchas that will cost you time

- **`iPod_Control/Device/SysInfo` is 0 bytes on both devices**, and
  `SysInfoExtended` is absent. So no model, serial, or FirewireGuid. hashAB
  signing needs the FirewireGuid, so this blocks writing to Nano 6G/7G. iTunes
  obtains it over USB via a vendor SCSI command; we will need to do the same.
  This is currently the largest unknown in the project.
- **Bitrates over 1000 kbps are real** (Apple Lossless), not misreads.
- **U+FFFD characters in tags are genuinely on the device**, not decode errors.
- **`tools/make_fixture.py` proves nothing about correctness.** It encodes the
  same assumptions as the reader, so it only catches crashes and structural
  bugs. Real verification means a real device.
- Passing a Windows drive path through Git Bash needs quoting: `"G:/"`, not `G:\`.

---

## Unverified assumptions

Be suspicious of these; they are reasoning, not measurement.

- That the 20 bytes at `0x58` are hash58 and the 46 at `0x72` are hash72. The
  sizes and offsets fit the names, but this was inferred from entropy in two
  hex dumps, not confirmed against a signing implementation.
- That Nano 6G/7G use hashAB. Widely reported, not verified here.
- That iOS 6.1.6 uses `MediaLibrary.sqlitedb` as the authoritative store. Very
  likely, but the Touch has not been inspected — dump the device before writing
  any iOS code.
- Every `mhit` field offset past `0x20`. They parse plausibly on two devices of
  the same DB version, which is weak evidence. Re-verify against a 5th gen (a
  much older DB version) before trusting them broadly.
