# Which iPods this works with, and why the rest don't

Short version: **every iPod that keeps its library in an `iTunesDB`/`iTunesCDB` file works, except the
ones whose signature is hashAB.** That covers iPod 1G–5.5G, mini, photo, nano 1G–5G and every iPod
classic. What's left out is the nano 6G/7G (hashAB), the shuffles (a different library format), and
the iPod touch (a different device entirely).

The app never guesses from the model name. It reads the device's own database header and decides
from that (`IpodProfiler`), so an iPod nobody has tested still works if its database says it can —
and an unfamiliar one is refused before anything is written. `ipodsync profile <root>` prints the
verdict, and the app shows it under **Health**.

## The signature is the whole problem

A classic iPod is just a USB disk: there is no handshake to fake, no "pretend to be iTunes" step.
Plug it in and you can read and write its files. The one thing standing between a third-party tool
and a working library is a checksum in the database header that the firmware verifies. Four schemes
exist (libgpod's names, after the header offsets):

| Scheme | Models | What it is | Here |
|---|---|---|---|
| **none** | iPod 1G–5G, mini, photo, nano 1G–2G, shuffles | no checksum at all | **works** |
| **hash58** | nano 3G/4G, iPod classic (all gens) | HMAC-SHA1 over the database, key derived from the device's FirewireGuid | **works** |
| **hash72** | nano 5G (plus the iPhone/touch 1–3 era) | AES-CBC over the database's SHA-1, with a per-device (IV, random) pair | **works** |
| **hashAB** | nano 6G/7G, shuffle 4G (and iPhone 4/touch 4/iPad 1) | white-box AES; 57-byte signature | **refused, unless you supply a signer** |

hash58 and hash72 are both implemented from scratch here, and neither is trusted until it reproduces
a signature iTunes itself wrote on that same device. hash72 needs per-device key material that only
iTunes mints, which this app recovers from the device's existing signature rather than asking for a
`HashInfo` file.

### Why hashAB is different

hashAB is **white-box AES**: the key isn't a value you can look up, it's baked into a large table
inside the code, so "implement the algorithm" and "extract Apple's key" are the same problem. Nobody
has published a clean-room implementation:

- libgpod doesn't implement it. It `dlopen`s an external `libhashab.so` — "libgpod now has a
  mechanism to dynamically load a module named `$libdir/libgpod/libhashab.so` … to easily enable
  support for these devices **if someone comes up with a way to compute the music database
  checksum**" ([libgpod NEWS](https://sourceforge.net/p/gtkpod/libgpod/ci/master/tree/NEWS)).
- That `libhashab.so` is a closed binary of unclear origin, which a third party then patched by
  disassembly: "I decided that I would try and dissassemble and recreate the libhashab.so library
  myself" ([libhashab](https://github.com/Amoystyle/libhashab)).
- pypodlib supports hashAB by running a **WebAssembly** module under wasmtime, not by implementing
  the maths ([pypodlib](https://pypi.org/project/pypodlib/)).

Shipping someone's extracted Apple code isn't something this project will do. What it does instead:

**A hashAB signer you supply.** Set `IPODSYNC_HASHAB_SIGNER` to a program taking two arguments and
printing 114 hex characters (57 bytes):

```
<program> <sha1-hex> <firewire-guid-hex>   →   stdout: 57 bytes as hex
```

The SHA-1 is over the database with `db_id`, hash58, hash72 and hashAB zeroed and the scheme field
set to 3, matching libgpod's `itdb_hashAB.c`. A few lines of Python around any existing
implementation satisfies it.

The signer is **only trusted after it reproduces the signature already on your device**, exactly as
the hash58/hash72 keys must. If it can't, writing is refused and nothing is touched. This is tested
three ways (no signer, wrong signer, matching signer) against a fixture built by
`ipodsync make-fixture-db --scheme hashab`, which writes a stand-in signature — that fixture proves
the plumbing and the trust gate, not hashAB itself.

## iPod shuffle

Shuffles don't have an `iTunesDB`. They use a flat `iTunesSD` list, in two incompatible forms: 1G/2G
(big-endian, 24-bit integers) and 3G/4G (the little-endian `bdhs` format, with per-track records
carrying the file path and a voiceover id). The 4G additionally wants hashAB. The app detects a
shuffle and says so; it doesn't read or write `iTunesSD` yet. This is a contained piece of work if
you ever want it — the 3G/4G layout is documented, the 1G/2G one is only in surviving tools.

## iPod touch (and iPhone/iPad)

Split the question in two: **the file transport**, and **the library format**. They have different answers.

**The library format.** An iPod touch keeps its library under `iTunes_Control` instead of
`iPod_Control`, and for the early models it is the same `iTunesDB` this app already writes:
libgpod maps touch 1G–3G (and iPhone 1–3) to **hash72** — the scheme the nano 5G uses and the one
implemented here. Touch 4G moves to hashAB, and iOS 5 and later move the library to Apple's own
`MediaLibrary.sqlitedb`.

The engine now accepts either control folder (it picks whichever actually holds the database), so a
touch whose media partition is mounted as a folder is read and written by exactly the same verified
pipeline. Tested against a fixture laid out the iOS way (`iTunes_Control/iTunes/iTunesCDB`): profiled
correctly, written and verified. Not yet tested against a real touch.

| Model | Library | Verdict |
|---|---|---|
| touch 1G / 2G / 3G, iOS ≤ 4 | `iTunes_Control` + iTunesDB, hash72 | **format supported** — needs the files to be reachable (see below) |
| touch 4G | hashAB | blocked, same wall as the nano 6G |
| anything on iOS 5+ | `MediaLibrary.sqlitedb` | not supported by any open-source tool |

**The transport** is the part your "pretend to be iTunes" question is really about, and it is a solved
problem. An iOS device isn't a disk: it speaks a USB multiplexing protocol
(usbmuxd), a control service (lockdownd, with a pairing handshake), and a file service (AFC).
[libimobiledevice](https://libimobiledevice.org/) implements all of that in the open, without a
jailbreak, and pairing genuinely does make the device treat you as a trusted host.

On Windows, Apple's own Mobile Device Service (installed with iTunes) speaks that same multiplexing
protocol on localhost, which is how libimobiledevice works there — so a C# client is plausible
without shipping a driver. On Linux/macOS, `ifuse` already mounts the device's media partition as a
folder, which is all this app needs today.

For anything past that era, the blocker is the *library*, not the protocol. Music lives in Apple's own `MediaLibrary.sqlitedb`,
whose schema changes with iOS and which iTunes updates through a private service. libimobiledevice's
own answer: "music synchronization with newer devices is currently not supported", and music sync
through libgpod "hasn't worked since as early as iOS 6". So an iPod touch would mean: implement
usbmuxd/lockdown/AFC, then reverse-engineer a moving, Apple-controlled database — a different
project from this one. Jailbroken devices (afc2 + a writable filesystem) are a much shorter path,
which is why tools that support the touch usually require one.

## HFS+ (Mac-formatted) iPods

Any iPod formatted on a Mac uses HFS+, which Windows won't mount and Android can't read at all, so
the app never sees it. Restoring the iPod once on a PC makes it FAT32 and everything above applies.
Reading HFS+ ourselves would mean parsing the volume from the raw disk — possible, but a large
piece of work, and writing it safely is larger still.

## What was tested, and how

Only the nano 5G is real hardware here. The other paths were exercised by building fixtures from a
copy of its database (`ipodsync make-fixture-db`) and running the complete write pipeline — backup,
dry run, write, read-back, re-verify — against them:

| Fixture | Result |
|---|---|
| unsigned (pre-2007), no SQLite bundle, no artwork | write verified |
| hash58 only (nano 3G/4G, classic), no SQLite bundle | write verified, hash58 recomputed and re-checked |
| hashAB stand-in, no signer | refused, nothing written |
| hashAB stand-in, wrong signer | refused, nothing written |
| hashAB stand-in, matching signer | write verified through the hook |

A fixture is a copy of a real database with its header rewritten, so it proves the *code paths*, not
that a nano 3G accepts the result. The first write to any newly supported model should still be one
small change, followed by a look at the iPod's own screen.

## Sources

- [libgpod NEWS](https://sourceforge.net/p/gtkpod/libgpod/ci/master/tree/NEWS) and its
  [device table](https://sourceforge.net/p/gtkpod/libgpod/ci/master/tree/src/itdb_device.c?format=raw) —
  model numbers, generations, and the `ItdbChecksumType` mapping (NONE / HASH58 / HASH72 / HASHAB).
- [libgpod `itdb_hashAB.c`](https://sourceforge.net/p/gtkpod/libgpod/ci/master/tree/src/itdb_hashAB.c?format=raw) —
  what is hashed, the 57-byte signature, and the `libhashab.so` blob it loads.
- [libhashab](https://github.com/Amoystyle/libhashab) — the blob's provenance, and that it was
  patched by disassembly rather than reimplemented.
- [pypodlib](https://pypi.org/project/pypodlib/) — a per-model matrix (HASH58 / HASH72 / HASHAB /
  NONE) and a hashAB implementation that runs as WebAssembly.
- [libimobiledevice](https://libimobiledevice.org/) and its
  [FAQ/issues on music sync](https://github.com/libimobiledevice/libimobiledevice) — the iOS
  transport is open, the music library isn't.
- [iTunesSD (shuffle 3G/4G) layout](https://github.com/nims11/IPod-Shuffle-4g/blob/master/docs/iTunesSD3gen.md).
