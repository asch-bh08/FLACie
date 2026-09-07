# ipodsync

Sync classic iPods without iTunes, from Windows and (later) Android.

Non-destructive by design: the device keeps stock Apple firmware, iTunes keeps
working alongside it, and our own sync state never lives on the iPod.

## Status

Reading works, verified against two real devices. Nothing writes to a device yet.

```
dotnet build
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe detect
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe dump G:/ -n 20
```

`dump` accepts an iPod drive root or a path to a database file directly.

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
  ItunesDb/     format models, mhod types, reader
  Device/       volume detection, SysInfo, database location
src/IpodSync.Cli/
tools/          make_fixture.py - synthetic DB generator for tests
```

`make_fixture.py` only proves the reader is self-consistent with the spec it was
written against; both sides encode the same assumptions. Real verification means
pointing it at a device.

## Roadmap

1. ~~Read `iTunesDB` / `iTunesCDB`~~ — done
2. Artwork (`ithmb`), album (`mhla`) and `mhli` decoding
3. Writer: rebuild the database, then hash58 / hash72 signing
4. Sync engine: content-hash manifest kept off-device, keyed by serial
5. Transcode FLAC to ALAC/AAC on copy
6. Android: USB mass storage via SCSI + FAT32, or the Storage Access Framework
7. iPod Touch (jailbroken): `MediaLibrary.sqlitedb` over afc2 — designed for, not built

## Notes

Both test devices are FAT32 (Windows-formatted), which is required for Android
to reach them at all. HFS+ (Mac-formatted) iPods are Windows/Android-unreadable
and must be restored once on a PC first.
