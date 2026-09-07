# ipodsync

Sync classic iPods without iTunes, from Windows and from Android — the phone
plugs directly into the iPod over USB-OTG and works like a PC would.

Non-destructive by design: the device keeps stock Apple firmware, iTunes keeps
working alongside it, and our own sync state never lives on the iPod.

## The app

`IpodSync.Shared` is one Blazor UI (`Dashboard.razor`) shared by three hosts:

- `IpodSync.Web` — runs it as a local web app. Used mainly as a way to actually
  see and click through the UI in a browser during development.
- `IpodSync.Maui`, Windows target — a real installed WinUI 3 app.
- `IpodSync.Maui`, Android target — a real installed APK.

The three differ only in which `IIpodSyncBackend` is registered
(`IpodSync.Shared/Backend/`): `LocalIpodSyncBackend` (web, Windows) reads a
mounted drive letter directly; `UsbIpodSyncBackend`
(`IpodSync.Maui/Platforms/Android/`) reads an iPod attached over USB-OTG
through a hand-written SCSI/FAT32 stack (`IpodSync.Core/UsbStorage/`, see
below), then hands the resulting bytes to the same `ItunesDbReader` everything
else uses. **That USB stack has not been tested against real hardware** — see
[HANDOFF.md](HANDOFF.md) for what "tested" means for everything else in this
project and why this piece is the exception so far.

Also wired up: local-folder library scanning with real tag reads
(`IpodSync.Core/LocalLibrary/`, via TagLibSharp), a read-only local-vs-device
sync preview, and Jellyfin playlist sync (`IpodSync.Core/Jellyfin/`, a
server-API-key client that only ever adds to Jellyfin, never deletes).

## Status

Reading works, verified against two real devices. The writer's round-trip
proof passes against both, for: reproducing a database unchanged, editing a
real field in place (play count, star rating), and three edits that change a
chunk's byte length (removing a track, adding an *existing* track to a
playlist, renaming a track). Nothing writes to a device yet — see
[HANDOFF.md](HANDOFF.md) for exactly what is and isn't covered before trusting
this for anything not listed there (constructing a brand-new track, notably,
is not — see below).

```
dotnet build
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe detect
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe dump G:/ -n 20
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe roundtrip G:/
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe mutate-test G:/
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe resize-test G:/

dotnet run --project src/IpodSync.Web                          # app, http://localhost:5070
dotnet build src/IpodSync.Maui -f net9.0-windows10.0.19041.0    # Windows app
dotnet build src/IpodSync.Maui -f net9.0-android -c Release     # Android APK -- Release,
                                                                 # not Debug (see HANDOFF.md:
                                                                 # a Debug APK crashes on launch
                                                                 # when installed standalone)
```

All four read/test commands accept an iPod drive root or a path to a database
file directly, and are read-only — none of them ever write to the device.
`roundtrip` parses the database and serialises it straight back to bytes,
unchanged, and diffs against the original (pass `-o <path>` to also save the
reconstructed bytes locally for inspection). `mutate-test` edits one track's
play count and star rating, then proves — via a raw byte diff *and* an
independent re-read through the already-verified reader — that nothing else
in the file moved. `resize-test` runs the three chunk-length-changing edits
and, since output no longer lines up byte-for-byte with the original, checks
each two different ways: semantically (re-read through the verified reader,
assert only the intended change appears) and for internal self-consistency
(parse the writer's own output a second time, serialize it again, and require
that reproduces the first pass's bytes exactly).

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

A second command, `mutate-test`, proves more than that: it edits one track's
play count and star rating (`TrackFields`, `TrackMutation.cs`) — both
fixed-size fields living inside the mhit chunk's own header, so patching one
never changes that chunk's byte length and nothing upstream needs its
total/count fields recomputed as a result — then serialises and checks two
independent ways that nothing else moved: a raw byte diff confirming every
changed byte falls inside the one mhit chunk that was targeted
(`RawChunkNavigation.ByteRangeOf`), and a full re-read of the result through
`ItunesDbReader` (the already-verified reader, untouched by any of this)
confirming every other track and every playlist is field-for-field identical.

That covers exactly the edit two-way sync needs (writing play counts/ratings
back to the device). Three further edits (`LibraryMutation.cs`) exercise the
part `mutate-test` deliberately avoids — resizing a chunk, which cascades into
every ancestor's total-length and count fields:

- **Remove a track.** Deletes its `mhit` from `mhlt` and every `mhip`
  referencing it from every playlist. Pure deletion, so it carries none of the
  risk a field we don't fully understand the semantics of would.
- **Add an existing track to a playlist.** Clones a real sibling `mhip` in
  that playlist (rather than building one from a guessed layout) and patches
  only the fields confirmed against real bytes: the referenced track id, that
  track's persistent id (duplicated into the entry for integrity), and a
  "date added" timestamp. One field is left deliberately conservative — see
  `LibraryMutation.AddTrackToPlaylist`'s doc comment for what real-byte
  comparison did and didn't confirm about it.
- **Rename a track.** Builds a brand-new `mhod` string chunk from scratch,
  since there's no existing one of the right size to clone. Every fixed field
  (`position=1`, the encoding word `=1` despite UTF-16LE content, `reserved=0`)
  matches what six real Title `mhod`s across two devices actually contain.

Two structural discoveries from decoding real `mhip` bytes before writing any
of this, beyond the mhlt/mhlp quirk above:

- **`mhip` is not a pure leaf.** It embeds a nested `mhod` (type 100,
  playlist column info) inside what looks like flat payload. Harmless for
  reading and for the byte-preserving round-trip (it's copied verbatim either
  way), but it matters the moment you *construct* a new `mhip`: a field at the
  entry's own header +0x14 is duplicated inside that embedded `mhod`'s payload
  at its relative +0x18, and both copies have to be kept in sync or the entry
  is inconsistent.
- **A track's persistent id is duplicated into every playlist entry that
  references it**, 8 bytes at the entry's +0x2C. Confirmed by comparing two
  real entries in the same playlist and matching those bytes against each
  referenced track's own persistent id field.

Each of the three is checked two ways, since resizing means the output no
longer lines up byte-for-byte against the original the way `mutate-test`'s
does: semantically, by re-reading the result through `ItunesDbReader` and
asserting only the intended change appears; and for internal consistency, by
parsing the writer's own output a second time and serializing it again — if
that doesn't reproduce the first pass exactly, the writer disagrees with
itself regardless of whether the first pass happened to look right.

None of this constructs a track from nothing. "Add to playlist" specifically
sidesteps that by referencing a track that already exists. Building a new one
needs a collision-free persistent id and track id, a scrambled `F##/XXXX.ext`
path picked the way the device does, real duration/bitrate/size read off an
actual audio file, and copying that file onto the device — none of which
exists yet.

### USB Mass Storage: reading an iPod plugged straight into the phone

Android gives an app no mounted filesystem for an arbitrary attached USB mass-
storage device — reaching one means speaking its protocol directly. That
protocol, USB Mass Storage Class Bulk-Only Transport (BOT), plus FAT32, is
implemented from scratch in `IpodSync.Core/UsbStorage/`:

- `ScsiBulkOnlyTransport.cs` — BOT framing (31-byte Command Block Wrapper out,
  optional data phase, 13-byte Command Status Wrapper in) and the handful of
  SCSI commands a block-level reader needs: INQUIRY, TEST UNIT READY, READ
  CAPACITY(10), READ(10)/WRITE(10).
- `ScsiBlockDevice.cs` — a sector-addressable block device on top of that,
  chunking large reads since a single USB bulk transfer has practical size
  limits well below READ(10)'s 16-bit block-count field.
- `Fat32Volume.cs` — read-only: MBR + partition table (falling back to
  treating the device itself as the volume for "superfloppy"-formatted media
  with no partition table), FAT32 BPB, 8.3 and long-filename directory
  entries, FAT cluster-chain following. Enough to find and read one file by
  path (`iPod_Control/iTunes/iTunesDB` or `iTunesCDB`).

None of this binds or ports libaums (the usual Java library for this on
Android) — `IpodSync.Maui/Platforms/Android/` wraps the stack above with the
plain `Android.Hardware.Usb` APIs (`UsbManager`, `UsbDeviceConnection.
BulkTransfer`/`ClaimInterface`/`ControlTransfer`), which are already part of
the standard `net9.0-android` bindings. That was confirmed by loading
`Mono.Android.dll` through `System.Reflection.MetadataLoadContext` and reading
the actual member signatures off it, not by assuming the API shape — the same
principle as everything else in this file, just applied to Android's SDK
surface instead of Apple's database format.

**Unverified against real hardware.** Every offset and protocol detail here
comes from the public USB MSC BOT and Microsoft FAT32 specifications, not from
a working reference implementation, and there is no iPod-over-USB-OTG rig
available where this was written. This is the one piece of the project that
hasn't been through the real-device check that caught the wrong assumptions in
the database format work — treat it as "should work" until someone plugs a
real iPod into a real Android phone and reports what happens.

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
    RoundTrip.cs         orchestrates RawChunk parse+serialize+diff for the identity proof
    TrackMutation.cs     RawChunkNavigation (find/locate chunks) + TrackFields (get/set
                          play count and stars in place on a parsed mhit chunk)
    MutationRoundTrip.cs proves one field edit round-trips without touching anything else
    LibraryMutation.cs   resizing edits: remove a track, add an existing track to a
                          playlist, rename a track
    ResizeRoundTrip.cs   proves each resizing edit round-trips (semantic + idempotency
                          checks, since output no longer lines up byte-for-byte)
  Device/         volume detection, SysInfo, database location
  UsbStorage/     BOT + SCSI + FAT32, for reading an iPod over USB-OTG (unverified,
                  see "USB Mass Storage" above)
  LocalLibrary/   scans a folder for audio files, reads real tags via TagLibSharp
  Jellyfin/       Jellyfin REST API client + playlist sync orchestrator
src/IpodSync.Cli/    the CLI: detect/dump/roundtrip/mutate-test/resize-test
src/IpodSync.Shared/ the app's actual UI (Dashboard.razor) and IIpodSyncBackend,
                      referenced by all three app hosts below
src/IpodSync.Web/    hosts Dashboard.razor as a local web app (Blazor Server)
src/IpodSync.Maui/   hosts Dashboard.razor as a real Windows app and Android APK
                      (Blazor Hybrid); Platforms/Android/ has the USB-specific glue
tools/          make_fixture.py - synthetic DB generator for tests
```

`ItunesDbReader` and `RawChunk` are two independent parses of the same bytes,
deliberately not layered on each other yet — see "The writer" above.

`make_fixture.py` only proves the reader is self-consistent with the spec it was
written against; both sides encode the same assumptions. Real verification means
pointing it at a device.

### Building the Android/Windows app

Needs the MAUI Android and Windows workloads (`dotnet workload install
maui-android maui-windows`), a JDK (Android APK packaging needs one regardless
of language — this project uses Microsoft OpenJDK 17), and the Android SDK
command-line tools with `platform-tools`, `platforms;android-35`, and
`build-tools;35.0.0` installed via `sdkmanager`. `JAVA_HOME`/`ANDROID_HOME` set
as environment variables covers it; see HANDOFF.md's "Toolchain additions"
section for the exact versions and paths used so far, and its gotchas section
for a `sdkmanager --licenses` quirk (must run from Bash, not PowerShell, for
piped license acceptance to actually work).

## Roadmap

1. ~~Read `iTunesDB` / `iTunesCDB`~~ — done
2. ~~Writer round-trip: reproduce an unmodified database byte-for-byte~~ — done,
   verified against both real devices
3. ~~Writer mutation: edit a real field (play count, stars) and round-trip
   *that*~~ — done, verified against both real devices; covers fixed-size
   field edits only
4. ~~Writer resizing edits: remove a track, add an existing track to a
   playlist, rename a track~~ — done, verified against both real devices
5. ~~App UI (Blazor, shared across web/Windows/Android hosts), local library
   scanning, sync preview, Jellyfin playlist sync~~ — done; web host verified
   end-to-end in a browser against real devices, Windows/Android hosts build
   and the Windows one launches, Jellyfin untested pending an API key
6. ~~Android: USB mass storage via SCSI + FAT32~~ — written, builds into a
   real APK, but **unverified against real hardware** — the next real gate
7. Construct a brand-new track (fresh ids, scrambled path, real audio
   metadata, file copy) — the remaining gate before any real write that adds
   content rather than editing/removing what's already there
8. Writing over USB (the stack above is read-only so far)
9. Artwork (`ithmb`), album (`mhla`) and `mhli` decoding
10. hash58 / hash72 signing
11. Sync engine: content-hash manifest kept off-device, keyed by serial
12. Transcode FLAC to ALAC/AAC on copy
13. iPod Touch (jailbroken): `MediaLibrary.sqlitedb` over afc2 — designed for, not built

## Notes

Both test devices are FAT32 (Windows-formatted), which is required for Android
to reach them at all. HFS+ (Mac-formatted) iPods are Windows/Android-unreadable
and must be restored once on a PC first.
