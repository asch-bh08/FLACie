# ipodsync

Sync classic iPods without iTunes, from Windows and (later) Android.

Non-destructive by design: the device keeps stock Apple firmware, iTunes keeps
working alongside it, and our own sync state never lives on the iPod.

## Status

Reading works, verified against two real devices. The writer's round-trip test
also passes byte-identically against both. Nothing writes to a device yet — see
[HANDOFF.md](HANDOFF.md) for why round-tripping an *unmodified* file isn't the
same thing as being ready to write.

```
dotnet build
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe detect
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe dump G:/ -n 20
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe roundtrip G:/
```

`dump` and `roundtrip` both accept an iPod drive root or a path to a database
file directly. `roundtrip` is read-only: it parses the database, serialises it
straight back to bytes, and diffs against the original — it never writes
anything, to the device or otherwise (pass `-o <path>` to also save the
reconstructed bytes locally for inspection).

## Format notes

Findings from actual devices, since published documentation disagrees with them
in places. Verified against two iPods reporting database version 115.

### iTunesCDB

Later iPods write `iPod_Control/iTunes/iTunesCDB` instead of `iTunesDB`. It is
**not** a different format: it is the ordinary 244-byte `mhbd` header, stored
plain, followed by a single zlib stream containing the datasets.

- Header length is at `+0x04`; the compressed body begins there.
- `+0x08` (total length) describes the *compressed* file, so it must be
  rewritten after inflating or offsets computed from it will be wrong.
- Detect it by checking for a zlib header at the body offset rather than by
  filename — the reader accepts either name.
- Observed compression ratio ~4.6x (245 KB on disk, 1.13 MB inflated).

### mhsd type numbers are not reliable

Widely-published tables say type 1 = tracks, 2/4 = playlists, 3 = podcasts. At
version 115 the actual layout is:

| type | contains | meaning |
|------|----------|---------|
| 1 | `mhlt` | tracks |
| 3 | `mhlp` | playlists (the real ones) |
| 4 | `mhla` | albums |
| 5 | `mhlp` | built-in smart playlists (Music, Movies, TV Shows, ...) |
| 8 | `mhli` | not yet decoded |

Dispatch on the magic of the list chunk *inside* the dataset, not on the type
number. Several datasets can hold playlists, so de-duplicate by persistent id.

### String encoding is not in the flag field

A string `mhod` stores, after its 24-byte header: position, byte length, a word
usually documented as an encoding flag, then a reserved word, then the data.

That "flag" reads `1` on both test devices while the payload is plainly UTF-16LE
(`4c 00 69 00` = `Li`). Trusting it renders every string with a gap between
characters. Detect from the data instead: odd byte length means UTF-8; a NUL at
any odd index means UTF-16LE; otherwise let a strict UTF-8 decode decide.

### Signature regions

Not yet needed — reading requires no signature — but mapped for when writing
lands. In the 244-byte `mhbd` header:

- `0x58`, 20 bytes: high-entropy. Matches the size and position that `hash58`
  is named for.
- `0x72`–`0xA0`, 46 bytes: high-entropy. Size and offset consistent with
  `hash72`.
- `0xAB` onward: zero on both test devices.

Which scheme a given device demands must be confirmed against libgpod's
implementation rather than inferred from these dumps.

### The writer: a lossless chunk tree, parsed independently of the reader

`ItunesDbReader` builds a semantic model (`Track`, `Playlist`) that only keeps
the fields it decodes — everything else (unhandled `mhod` types, `mhla`,
`mhli`, header bytes past what's parsed) is dropped. Regenerating a file from
that model would mean reconstructing those dropped bytes, which is exactly
backwards for a format where "bytes we do not understand ... must be preserved
verbatim, not regenerated."

So the writer parses the same file a second, independent way, into
`RawChunk` (`src/IpodSync.Core/ItunesDb/RawChunk.cs`): a tree that keeps every
byte. Each node holds its own header bytes plus either children (for the six
chunk types this format nests: `mhbd`, `mhsd`, `mhlt`, `mhlp`, `mhit`, `mhyp`)
or a flat payload (leaves: `mhod`, `mhip`, and anything unrecognised).
`Serialize()` recomputes only the structural fields this format actually ties
to the tree shape — a chunk's own total length, and its children's count(s) —
and copies everything else through unchanged. A chunk type it doesn't
recognise (`mhla`, `mhli`, or anything future) is captured whole and never
touched at all: its header is cloned but nothing is ever patched into it.

Two structural quirks worth knowing if you touch this code:

- **`mhlt`/`mhlp` have no total-length field of their own.** Every other
  container in this format (`mhbd`, `mhsd`, `mhit`, `mhyp`) carries
  `magic(4) + headerLen(4) + total(4) + ...`. `mhlt`/`mhlp` instead carry
  `magic(4) + headerLen(4) + count(4) + ...` — the same byte offset means
  something different. Their extent is implied by the parent `mhsd`'s total,
  not self-declared. Blindly "recomputing a total at +0x08" for these two
  would silently corrupt the count field instead.
- **An unrecognised chunk gets a name, not just raw bytes.** When `mhsd`'s
  inner chunk isn't `mhlt`/`mhlp` (i.e. it's `mhla` or `mhli`), it's still
  parsed as one opaque leaf via the generic `magic+headerLen+total` shape,
  rather than folded into the parent's payload as anonymous trailing bytes.
  Same verbatim-preservation guarantee either way, but naming it means
  `roundtrip`'s diagnostics can say which chunk types were passed through
  untouched.

Verified: `roundtrip` against both `G:\` and `H:\` reproduces the inflated
database byte-for-byte, including their `mhla`/`mhli` chunks. It does **not**
attempt to reproduce Apple's exact zlib-compressed bytes on recompression —
different zlib encoders legitimately produce different compressed output for
identical input, so that's checked separately (recompress, reinflate, confirm
the content still matches) rather than compared byte-for-byte.

This round-trip only proves the tree faithfully captures a file *unchanged*.
There is no mutation API yet — nothing edits a field or adds/removes a chunk
and re-serialises. That's the next task, not this one.

### Miscellaneous

- `iPod_Control/Device/SysInfo` is **0 bytes** on both test devices, so model,
  serial and FirewireGuid cannot be read from it. `SysInfoExtended` is absent
  too. iTunes gets these over USB via a vendor SCSI command; we will need to do
  the same before hashAB signing is possible.
- Bitrates above 1000 kbps are real, not misreads — the filetype `mhod` on those
  tracks says "Apple Lossless".
- U+FFFD characters in some tag fields are genuinely stored on the device.

## Layout

```
src/IpodSync.Core/
  ItunesDb/
    Models.cs          Track/Playlist/ItunesDatabase - the semantic model
    MhodType.cs         mhod subtype enum
    ItunesDbReader.cs    the verified reader: bytes -> semantic model
    BinaryIo.cs          shared magic/int/inflate/deflate primitives
    RawChunk.cs          the lossless chunk tree + parser: bytes -> tree -> bytes
    RoundTrip.cs         orchestrates RawChunk parse+serialize+diff for the writer's proof
  Device/       volume detection, SysInfo, database location
src/IpodSync.Cli/
tools/          make_fixture.py - synthetic DB generator for tests
```

`ItunesDbReader` and `RawChunk` are two independent parses of the same bytes,
deliberately not layered on each other yet — see "The writer" above.

`make_fixture.py` only proves the reader is self-consistent with the spec it was
written against; both sides encode the same assumptions. Real verification means
pointing it at a device.

## Roadmap

1. ~~Read `iTunesDB` / `iTunesCDB`~~ — done
2. ~~Writer round-trip: reproduce an unmodified database byte-for-byte~~ — done,
   verified against both real devices
3. Writer mutation API: edit a field, add/remove a track or playlist entry, and
   round-trip *that* — the actual gate before any real write
4. Artwork (`ithmb`), album (`mhla`) and `mhli` decoding
5. hash58 / hash72 signing
6. Sync engine: content-hash manifest kept off-device, keyed by serial
7. Transcode FLAC to ALAC/AAC on copy
8. Android: USB mass storage via SCSI + FAT32, or the Storage Access Framework
9. iPod Touch (jailbroken): `MediaLibrary.sqlitedb` over afc2 — designed for, not built

## Notes

Both test devices are FAT32 (Windows-formatted), which is required for Android
to reach them at all. HFS+ (Mac-formatted) iPods are Windows/Android-unreadable
and must be restored once on a PC first.
