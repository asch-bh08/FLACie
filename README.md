# ipodsync

Sync classic iPods without iTunes, from Windows and from Android — the phone
plugs directly into the iPod over USB-OTG and works like a PC would.

Non-destructive by design: the device keeps stock Apple firmware, iTunes keeps
working alongside it, and our own sync state never lives on the iPod.

## The app

`IpodSync.Shared` is one Blazor UI shared by three hosts:

- `IpodSync.Maui`, Windows target: the real desktop app (WinUI 3), with native file/folder pickers.
- `IpodSync.Web`: the same UI in a browser (`dotnet run --project src/IpodSync.Web`, http://localhost:5070). Handy for development.
- `IpodSync.Maui`, Android target: an installed APK that reads an iPod plugged into the phone over USB-OTG. **Read-only for now.**

On Windows (and the web host) the app manages the iPod through `IpodSync.Core.Sync.WritePipeline`, the same verified write path the CLI uses:

| Tab | What it does |
|---|---|
| Songs | Search/sort every song; covers read straight from the iPod's `ithmb` files. Click to play; edit title/artist/album/album artist/genre/composer, star rating, cover art (one song or the whole album), add to a playlist, delete from the iPod. |
| Albums | A cover grid of the whole library; open an album to play it, shuffle it, or add it to a playlist. |
| Playlists | Create, rename, delete; add songs, remove, reorder; play or shuffle. Smart playlists are shown read-only. |
| Add music | Add files. FLAC/Opus/Ogg/WMA are converted (lossless → ALAC, lossy → 256k AAC); tags and embedded covers come along. |
| Sync a folder | Preview a music folder against the iPod (already synced / on the iPod already / to add / duplicates), then add in verified batches. Sync state lives on the PC (`%LOCALAPPDATA%\ipodsync\manifests`). |
| Import playlist | Match an iTunes "Export Playlist" `.txt` or an M3U to songs on the iPod, and queue it as a playlist. |
| Changes | Every edit is queued first. **Write to iPod** stays disabled until a dry run of exactly that queue passes; then there's a confirmation, a verified backup, the write, a read-back re-verify of both databases, signatures and artwork, and an automatic restore if anything doesn't match. |
| Backups / Device health | Pre-write backups with their write logs, and read-only checks: signatures, CDB ↔ SQLite agreement, artwork integrity. |
| Jellyfin | Copy the iPod's playlists to a Jellyfin server (writes only to Jellyfin). |

**The player.** The app plays the iPod's own files — nothing is copied to play a song, and playback never writes to the device (the iPod keeps its own play counts). The bar at the bottom has play/pause, previous/next, a scrubber, volume, shuffle, repeat and the up-next queue. Clicking a song plays it and queues whatever list you are looking at.

| Host | How it plays | Apple Lossless |
|---|---|---|
| Windows | WebView2, with the iPod mapped to a read-only virtual host | converted to FLAC on the fly (needs ffmpeg), cached |
| Web | the same UI, files served by a localhost endpoint with range requests | same |
| Android | Android's own media player, straight from the file | played natively, no conversion |

Backups default to `Documents\ipodsync\ipod-backups`; change the folder in the Backups tab. Settings live in `%LOCALAPPDATA%\ipodsync\app-settings.json`.

The hosts differ only in which `IIpodSyncBackend` is registered (`IpodSync.Shared/Backend/`):

- `LocalIpodSyncBackend` (web, Windows) works on a mounted drive letter.
- `SafIpodSyncBackend` (`IpodSync.Maui/Platforms/Android/`) reads through Android's Storage Access Framework. Reading was verified on a Z Fold 7; see HANDOFF.md.

`AppState` (`IpodSync.Shared/State/`) holds the queue and turns UI actions into EDIT-PROTOCOL ops.

**Testing without an iPod.** Set these environment variables before starting the web host or the app:

- `IPODSYNC_EXTRA_ROOTS=<folder>` lists a folder laid out like an iPod (e.g. the fake root that `tools/fake-root-regression.sh` builds) as a device.
- `IPODSYNC_APP_SETTINGS=<file>` keeps test backups and settings out of your real ones.

## iPod Player (the standalone one)

Playback now lives in the app as well (see above); this is the separate zero-install player, kept
because it needs nothing built and has the full-screen "Now Playing" experience.

[`ipod-player/`](ipod-player/) is a zero-install, single-file browser player —
the lightweight **read-and-play** companion to the sync engine above. Double-click
`iPod Player.vbs`, a borderless app window opens with the iPod already loaded, and
a helper PowerShell server runs hidden. It has its own standalone JavaScript
`iTunesDB`/`iTunesCDB` parser (same format findings as below, validated by an
in-file `?selftest`), decodes real `.ithmb` album art, and plays AAC/MP3 plus
**Apple Lossless transcoded to FLAC on the fly**.

Its player UI is a full-screen, YouTube-Music-style "Now Playing" with artwork-
derived colour, a live spectrum visualizer, an up-next queue, **time-synced
lyrics** (from [lrclib.net](https://lrclib.net), no API key), a **10-band
equalizer** with the classic iPod presets, Sound Check, a sleep timer and a
volume limit. See [`ipod-player/README.md`](ipod-player/README.md).

**It is read-only today.** Editing an iPod's contents in the player is intended
to go through this project's *verified* database writer rather than a second,
unproven writer in JavaScript — the player emits a JSON **change-set** that the
C# engine applies through the round-trip-proven writer. The contract and its
per-operation writer status are in [EDIT-PROTOCOL.md](EDIT-PROTOCOL.md). The same
safety gate applies (see [HANDOFF.md](HANDOFF.md)): nothing writes to a real
device until a modified database round-trips byte-identically, a backup is taken,
and the user is asked.

## Status

**2026-09-14 (overnight session, see [OVERNIGHT-STATUS.md](OVERNIGHT-STATUS.md)):**
every edit now writes the classic CDB **and** the SQLite library bundle the
nano 5G's menus actually use, signed the way iTunes signs them, with artwork,
transcode-on-add and folder/NAS sync — all verified on the real device's files
through one pipeline (backup → dry run → write → read back → re-verify → restore on
failure). On-screen confirmation on the iPod is still pending. The history below
is kept for context.

Reading works, verified against two real devices. The writer's round-trip
proof passes against both, for: reproducing a database unchanged, editing a
real field in place (play count, star rating), and three edits that change a
chunk's byte length (removing a track, adding an *existing* track to a
playlist, renaming a track). **The first real write to hardware is done and
verified** (2026-09-13): `apply-edits --yes` set a track's star rating on a real
635-track device after auto-backing-up, and re-reading the device confirmed the
change applied with nothing else moved. See [HANDOFF.md](HANDOFF.md) for exactly
what is and isn't covered before trusting this for anything not listed there
(constructing a brand-new track, notably, is not — see below); every write still
backs up first and asks before touching a device.

```
dotnet build
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe detect
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe dump G:/ -n 20
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe roundtrip G:/
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe mutate-test G:/
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe resize-test G:/

# two-database / signing / artwork checks (read-only)
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe itlp-diff D:/          # CDB vs SQLite bundle
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe art-check D:/          # ArtworkDB + ithmb integrity
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe hash72-verify D:/      # CDB + Locations.itdb.cbk signatures
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe itlp-orders-check D:/  # SQLite sort-rank rules vs the device

# writes (dry run unless --yes; see EDIT-PROTOCOL.md)
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe apply-edits D:/ --changes edits.json [--yes]
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe itlp-sync D:/ [--yes] [--resign]
./src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe sync-folder D:/ "\\nas\Music\FLAC" [--yes] [--limit N]

tools/fake-root-regression.sh D:/     # every write op against a fake copy of the device

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

### The SQLite library bundle (nano 5G)

`iPod_Control/iTunes/iTunes Library.itlp/` holds `Library.itdb` (items, albums,
artists, genres, composers, playlists = `container`, memberships =
`item_to_container`), `Dynamic.itdb` (`item_stats` ratings/plays,
`container_ui`), `Locations.itdb` (file paths) + `Locations.itdb.cbk`, and
`Extras.itdb` (lyrics). The firmware's menus/search/Now Playing read these, so a
CDB-only edit is invisible. Keys: `item.pid` = CDB track persistent id,
`container.pid` = playlist persistent id, `album.pid` / `artist.pid` = the CDB
album/artist list entries' persistent ids (below). Conventions measured on the
device and encoded in `src/IpodSync.Core/Itlp/` (each re-checkable with
`itlp-orders-check` / `itlp-diff`): every `*_order` column is 100 × rank of the
matching `sort_*` text; sort text strips leading punctuation then one English
article; the collation ignores apostrophes/hyphens, orders space < punctuation <
digits < letters, and puts digit-initial keys last (0 inversions against 589
title / 470 artist ranks); playlist `name_order` ranks all CDB playlists incl.
built-in smart ones with the master first; `shuffle_order` is NULL; 4CC
`'FILE'`/`'M4A '`/`'MP3 '` in `location`; `avformat_info.audio_format` 301 MP3 /
502 AAC / 601 ALAC, duration in samples, ALAC `bit_rate` = rate×bits×channels.

### Signatures (nano 5G)

- **hash72** (`Locations.itdb.cbk`, and the CDB header @0x72): `01 00` + 12 random
  bytes + AES-128-CBC(fixed key, per-device IV, SHA-1 ‖ random). The (IV, random)
  pair is recovered from a signature iTunes wrote; rebuilding the device's cbk
  with it is byte-identical. cbk = signature ‖ SHA-1(block SHA-1s) ‖ SHA-1 of each
  1024-byte block of Locations.itdb. CDB SHA-1 zeroes db id / hash58 / hash72.
- **hash58** (CDB header @0x58, scheme @0x30 = 1): HMAC-SHA1 over the compressed
  file with db id / 0x32 / hash58 zeroed, keyed from the FirewireGuid (= the USB
  serial). Reproduces the original iTunes value exactly. Sign hash72, then hash58.

### Album / artist lists (`mhla` / `mhli`)

Count at +0x08 like `mhlt`. `mhia`: id @0x10, persistent id @0x14 (= SQLite
`album.pid`), artwork track pid @0x20, mhods 200 album / 201 artist / 202 album
artist. `mhii`: id, persistent id (= `artist.pid`), mhod 300 name / 301 sort name.
`mhit` +0x120 → album id, +0x1E0 → artist id, +0x1F4 = track id + 3; list ids share
the track id counter. Also on `mhit`: +0xA8 second copy of the persistent id,
+0xBC sample count, +0x12C second copy of the size, +0x68 date added, +0xA4 has
artwork (1/2), +0x160 artwork image id, u16 +0x7C artwork count, +0x80 source
artwork size.

### Artwork (`iPod_Control/Artwork`)

`ArtworkDB`: mhfd (next image id @0x1C) → mhsd 1 `mhli` images / 2 `mhla` / 3
`mhlf` formats. `mhii` image: id (= SQLite `artwork_cache_id` = mhit +0x160),
track pid @0x14, source size + 1 @0x30, **reference count @0x38**; one `mhni` per
format (format @0x10, ithmb offset @0x14, size @0x18, padding @0x1C/0x1E,
padding + content height/width @0x20/0x22). nano 5G formats: 1056 128², 1078 80²,
1073 240², 1074 50², all **RGB565 little-endian**, stored contiguously in
`F<format>_1.ithmb`. iTunes letterboxes (centred) except 1078, which is
centre-cropped. `mhaf` inside mhod type 6: its length is its header.

### Reading an iPod plugged straight into the phone

**Verified on real hardware** (Samsung Galaxy Z Fold 7, a real iPod): tap Load
library, pick the iPod's folder in the standard Android document picker, real
playlists and track counts display.

The path there took two real-device rounds to find. First attempt: Android
gives an app no mounted filesystem for an arbitrary attached USB mass-storage
device, so `IpodSync.Core/UsbStorage/` implemented the device's own protocol
from scratch — USB Mass Storage Class Bulk-Only Transport (`ScsiBulkOnlyTransport.cs`:
CBW/CSW framing, INQUIRY, TEST UNIT READY, READ CAPACITY(10), READ(10)/WRITE(10)),
a block-device wrapper (`ScsiBlockDevice.cs`), and a read-only FAT32 reader
(`Fat32Volume.cs`: MBR + BPB, 8.3/LFN directory entries, cluster chains) — via
the plain `Android.Hardware.Usb` APIs, confirmed present in the standard
`net9.0-android` bindings by loading `Mono.Android.dll` through
`System.Reflection.MetadataLoadContext` and reading the real member signatures
rather than assuming them. No libaums binding, no JDK needed for that (a JDK
was still needed to package the APK at all — see "Building the app" below).

On real hardware this got past permission and initial SCSI commands, then
failed mid-transfer with Android reporting *"a USB storage device was removed
unsafely"* despite nothing being unplugged. The reason: Android (at least
Samsung's One UI) auto-mounts a recognised USB Mass Storage device as
browsable storage the moment it's attached, and force-claiming the raw
interface (`UsbDeviceConnection.ClaimInterface(force: true)`) yanks it out
from under that mount mid-operation.

Since the OS already mounts the device, reading through Android's **Storage
Access Framework** works with that instead of against it —
`SafBridge.cs` opens the standard folder picker, `SafDocumentReader.cs` walks
to the target file one path segment at a time via `DocumentsContract`'s
child-listing query (document IDs are provider-specific, not something to
construct by hand), and `ContentResolver.OpenInputStream` reads it. That's
what `SafIpodSyncBackend` (the one actually wired up) uses; the resulting
bytes go through the same `ItunesDbReader` everything else does.

The original USB/SCSI/FAT32 stack is still in the tree
(`UsbIpodSyncBackend`) — not deleted, since it's real, working code (the
permission-handling fix in it was correct and proven) and `DocumentsContract`
supports writes too (`OpenOutputStream`), so SAF may end up being the whole
Android story rather than a workaround. It just isn't registered in
`MauiProgram.cs` since SAF is what actually works today.

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
    EntityLinks.cs       album/artist list entries + track links
    PlayCounts.cs        keeps the firmware's Play Counts file aligned with the track list
    EditApplier.cs       applies a JSON change-set (EDIT-PROTOCOL.md)
  Itlp/           the SQLite library bundle: ItlpCompare (verifier), ItlpSync (playlists),
                  ItlpTrackSync (tracks/entities/stats), ItlpSorting (sort text + ranks)
  Signing/        Hash72, Hash58, DeviceSigning (key material proven per device)
  Artwork/        ArtworkDb (lossless tree), ArtworkSession (edits), Thumbnailer (ffmpeg)
  Transcode/      Transcoder (ffmpeg transcode-on-add, cached, probed)
  LocalLibrary/   FolderSync (folder/NAS sync planning + manifest), scanner, preview
  Device/         volume detection, SysInfo, database location, DeviceWriteTransaction
                  (verified backup / write / read-back / restore)
  UsbStorage/     BOT + SCSI + FAT32 for reading an iPod over raw USB -- retained,
                  correct, but not what's wired up; see "Reading an iPod" above
  LocalLibrary/   scans a folder for audio files, reads real tags via TagLibSharp
  Jellyfin/       Jellyfin REST API client + playlist sync orchestrator
src/IpodSync.Cli/    the CLI (WritePipeline.cs is the single write path)
src/IpodSync.Shared/ the app's actual UI (Dashboard.razor) and IIpodSyncBackend,
                      referenced by all three app hosts below
src/IpodSync.Web/    hosts Dashboard.razor as a local web app (Blazor Server)
src/IpodSync.Maui/   hosts Dashboard.razor as a real Windows app and Android APK
                      (Blazor Hybrid); Platforms/Android/ has the Android-specific
                      glue -- SafBridge.cs/SafDocumentReader.cs/SafIpodSyncBackend.cs
                      (what's actually used) plus the retained USB/SCSI classes
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
   end-to-end in a browser against real devices, Jellyfin untested pending an
   API key
6. ~~Android: read an iPod plugged into the phone~~ — done, **verified on real
   hardware** (Z Fold 7 + a real iPod) after three test rounds; ended up as
   Storage Access Framework, not the originally-planned raw USB/SCSI/FAT32
   stack — see "Reading an iPod" above for why
7. Visually check the Windows app's UI (builds and launches; not yet eyeballed)
8. Writing from Android via SAF (`OpenOutputStream`) — plausibly more direct
   now than porting the raw USB stack forward
9. ~~Construct a brand-new track~~ — done, and mirrored into the SQLite bundle (2026-09-14). Was: construct a brand-new track (fresh ids, scrambled path, real audio
   metadata, file copy) — the remaining gate before any real write that adds
   content rather than editing/removing what's already there
10. Writing over raw USB, if the SAF write path turns out to need it after all
11. ~~Artwork (`ithmb`), album (`mhla`) and `mhli` decoding~~ — done: decoded and written (2026-09-14)
12. ~~hash58 / hash72 signing~~ — done for the nano 5G (hashAB for 6G/7G still open)
13. ~~Sync engine~~ — `sync-folder`: off-device manifest keyed by library id + folder (2026-09-14)
14. ~~Transcode FLAC to ALAC/AAC on copy~~ — done, cached by source hash (2026-09-14)
15. iPod Touch (jailbroken): `MediaLibrary.sqlitedb` over afc2 — designed for, not built

## Notes

Both test devices are FAT32 (Windows-formatted), which is required for Android
to reach them at all. HFS+ (Mac-formatted) iPods are Windows/Android-unreadable
and must be restored once on a PC first.
