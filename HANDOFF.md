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
- **Android is not optional, and it means the phone plugs directly into the
  iPod over USB-OTG and works like a PC would** — not a remote-control app
  that talks to a PC over the network. A Samsung Galaxy Z Fold 7 is the target
  phone. Any architectural choice that makes direct phone-to-iPod USB
  impossible is wrong.
- **iPod Touch 4G: design for it, do not build it.** The user said "add it in
  but dont do it". Leave the seam in the architecture; write no iOS code.

### What "better than iTunes" means here

Non-destructive (no library-association wipes), two-way (pull play counts and
ratings back off the device), folder-based (no forced library import), FLAC via
transparent transcode, incremental, no account, no cloud.

---

## Current state

**Reading works and is verified against two of the user's real iPods.** The
writer's round-trip proof now covers, against both devices: reproducing a
database unchanged, editing a real field in place (play count, star rating),
and three edits that change a chunk's byte length (removing a track,
adding an *existing* track to a playlist, renaming a track). Nothing writes to
a device. That is deliberate — see Safety below.

**There is now a real app** (`src/IpodSync.Shared/Dashboard.razor`, a Blazor
component shared across three hosts) rather than just a CLI:

- `IpodSync.Web` — runs it as a local web app, used so far purely as a way to
  actually see and click through the UI in a browser (verified working:
  detected both real iPods, loaded G:\'s real 614-track library, scanned a
  real network-share folder with correct FLAC/MP3 tags via TagLibSharp,
  sync-preview compare ran correctly).
- `IpodSync.Maui`, Windows target — a genuine installed Windows app (WinUI 3).
  Builds clean, launches, stays running. Its *rendered content* has not been
  visually checked (no way to screenshot a native window from here the way the
  browser version could be) — check it looks right when you get a chance.
- `IpodSync.Maui`, Android target — a genuine installed APK (built and
  signed: `dev.ashley.ipodsync-Signed.apk`, ~14 MB). This is the one the user
  actually asked for: plug the phone into an iPod via USB-OTG and use it like
  a PC would. **The USB/SCSI/FAT32 code this depends on has never touched real
  hardware** — see the dedicated section below before trusting it.

Jellyfin playlist sync is wired up (`IpodSync.Core/Jellyfin/`) but not yet
exercised against the user's real server — needs an API key.

The three hosts differ only in which `IIpodSyncBackend` is registered
(`src/IpodSync.Shared/Backend/`): `LocalIpodSyncBackend` (web, Windows) reads a
mounted drive letter directly through the already-verified
`IpodSync.Core.ItunesDb` code; `UsbIpodSyncBackend`
(`IpodSync.Maui/Platforms/Android/`) reads an iPod attached over USB-OTG
through a new `IpodSync.Core.UsbStorage` stack, then hands the resulting bytes
to that exact same `ItunesDbReader` — so everything already proven about the
database format applies unchanged once real bytes are actually in hand.

```
dotnet build
dotnet run --project src/IpodSync.Cli -- detect
dotnet run --project src/IpodSync.Cli -- dump G:/ -n 20
dotnet run --project src/IpodSync.Cli -- roundtrip G:/
dotnet run --project src/IpodSync.Cli -- mutate-test G:/
dotnet run --project src/IpodSync.Cli -- resize-test G:/

dotnet run --project src/IpodSync.Web            # app in a browser, http://localhost:5070
dotnet build src/IpodSync.Maui -f net9.0-windows10.0.19041.0   # Windows app
dotnet build src/IpodSync.Maui -f net9.0-android               # Android APK
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

- **Adding a brand-new track is not built.** `resize-test` proves removing a
  track, adding an *existing* track to a playlist, and renaming a track — all
  by editing or cloning real chunks already in the file. None of that
  constructs a track from scratch: that additionally needs a fresh persistent
  id and track id that can't collide with anything on the device, a scrambled
  `F##/XXXX.ext` path picked the same way the device does, reading real
  duration/bitrate/size off an actual audio file, and copying that file onto
  the device — none of which exists yet.
- **The Android USB stack is unverified against real hardware** — see below.
  Until it's tested, treat `UsbIpodSyncBackend` as "should work, per the spec"
  rather than "works."
- No signing. No sync engine. No transcode. No writing over USB (the
  USB/SCSI/FAT32 stack is read-only, matching the project's own rule of never
  attempting a write before reading is solid).
- `mhla` (albums) and `mhli` (unknown) chunks are preserved verbatim by the
  writer but still not semantically decoded by the reader.
- Artwork (`ithmb`) untouched.
- Model identification does not work — see the SysInfo gotcha below.
- Jellyfin sync only ever adds tracks/creates playlists; it never removes or
  deletes anything there.

---

## The Android USB/SCSI/FAT32 stack — needs a real hardware test

This is the piece HANDOFF has called "the largest unknown in the project"
since before any of this code existed, and it is the one piece in the whole
project that could not be checked against real bytes from here — there is no
iPod-over-USB-OTG rig attached to this dev machine. Everything else in this
project only became trustworthy after a real-device pass; this hasn't had one
yet. Treat it accordingly.

What it is: `src/IpodSync.Core/UsbStorage/` implements USB Mass Storage Class
Bulk-Only Transport (`ScsiBulkOnlyTransport.cs`: CBW/CSW framing, INQUIRY, TEST
UNIT READY, READ CAPACITY(10), READ(10)/WRITE(10)), a block-device wrapper
(`ScsiBlockDevice.cs`), and a minimal read-only FAT32 reader
(`Fat32Volume.cs`: MBR + BPB parsing, 8.3 and LFN directory entries, cluster
chain following) — enough to find and read one file by path. Built from the
public USB MSC BOT and Microsoft FAT32 specs, not from libaums or any other
implementation. `src/IpodSync.Maui/Platforms/Android/` wraps this with the
actual Android USB host APIs (`UsbManager`/`UsbDeviceConnection`, confirmed
present in the standard `net9.0-android` bindings via direct reflection on
`Mono.Android.dll` — no libaums binding needed, no JDK needed for that part).

**Why C# instead of binding libaums**: bindings need a JDK to run the binding
tool, and this machine only had a JRE (see "C# / .NET 9" below) — that
constraint doesn't touch the code, just the tooling. Separately, actually
*packaging an Android APK* (dexing, apksigner) also needs a JDK regardless of
language — that one's now installed (Microsoft OpenJDK 17), see the
Environment section.

**To actually test this**: install the APK
(`src/IpodSync.Maui/bin/{Debug,Release}/net9.0-android/dev.ashley.ipodsync-Signed.apk`)
on the Z Fold 7, plug an iPod in via a USB-OTG cable, open the app, hit
Refresh, grant the USB permission prompt, and try loading the library. If it
fails, the exception message and where it throws (device discovery, permission,
opening the connection, INQUIRY, READ CAPACITY, partition parsing, FAT32
parsing, or the file walk) narrows down which layer has the bug — this is
exactly the same "get a real error from real hardware" loop that fixed the
database format's wrong assumptions earlier in the project, just one level
lower in the stack.

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

**USB Mass Storage: hand-written C# (BOT + SCSI + FAT32), not a libaums
binding.** The obvious Android library for this (libaums) is Java, and binding
a Java library needs a JDK to run the binding tool, which this machine didn't
have. Turned out not to matter either way: the raw APIs a binding of libaums
would have wrapped (`Android.Hardware.Usb.UsbManager`/`UsbDeviceConnection`,
with `BulkTransfer`/`ClaimInterface`/`ControlTransfer`) are already present in
the standard `net9.0-android` bindings Microsoft ships — confirmed by loading
`Mono.Android.dll` via `System.Reflection.MetadataLoadContext` and inspecting
the actual member signatures rather than assuming them, the same "check the
real thing, don't guess" approach the file-format work used. So the whole BOT
+ SCSI + FAT32 stack is plain C# in `IpodSync.Core.UsbStorage`, no binding, no
JDK needed for *that* part. (Packaging the resulting APK is a separate JDK
dependency regardless of language — see Environment below.)

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

**The app is one Blazor UI (`IpodSync.Shared`) shared across three hosts
(`IpodSync.Web`, `IpodSync.Maui` Windows, `IpodSync.Maui` Android) via an
`IIpodSyncBackend` abstraction**, rather than separate UIs or a phone-remote-
controls-PC design. The user's actual requirement, once clarified, is that the
*phone* plugs directly into the iPod over USB-OTG and works like a PC would —
not a client/server model where the phone remotely drives a PC that has the
iPod attached. (An earlier pass down that remote-control road was abandoned
mid-build once this was clarified; nothing from it survived except the
now-differently-purposed `IIpodSyncBackend` interface shape.) That's why
Android needs its own real USB implementation rather than reusing
`LocalIpodSyncBackend` over a network call.

### Toolchain additions this session

- **Microsoft OpenJDK 17**, via `winget install Microsoft.OpenJDK.17`, at
  `C:\Program Files\Microsoft\jdk-17.0.20.101-hotspot`. Needed to package an
  Android APK (dexing/`apksigner` are JVM tools) regardless of language — this
  is unrelated to the "no JDK needed for the USB code" point above, which was
  specifically about *binding a Java library*, not about *building an APK*.
- **Android SDK command-line tools**, manually installed (no Android Studio)
  to `C:\Android\sdk`: `platform-tools`, `platforms;android-35`,
  `build-tools;35.0.0`, via `sdkmanager`.
- `JAVA_HOME`, `ANDROID_HOME`, `ANDROID_SDK_ROOT` set as persistent user
  environment variables pointing at the above. A **new terminal** is needed to
  pick these up; a session already open when they were set won't see them
  (pass `-p:JavaSdkDirectory=... -p:AndroidSdkDirectory=...` to `dotnet build`
  in that case, or just open a fresh terminal).

---

## Safety — read before writing any code that touches a device

The user's iPods hold their actual music library. Treat them as production.

1. **Never write to a connected iPod until a *modified* database round-trips.**
   Four things are proven now, all in-memory only, against both `G:\` and
   `H:\`: the identity case (`roundtrip`), a fixed-size field edit — play
   count and star rating (`mutate-test`) — and three resizing edits: remove a
   track, add an *existing* track to a playlist, rename a track
   (`resize-test`, `LibraryMutation.cs`/`ResizeRoundTrip.cs`). Every offset
   `LibraryMutation.cs` writes to was read directly off real bytes via a
   throwaway `inspect` command (since removed), not assumed from docs — see
   its class comment and README's "The writer" section for two specific things
   that would have been easy to get wrong (mhip embeds a nested mhod rather
   than being flat, and one of its fields is duplicated in two places that
   both need patching together).
   This does **not** cover constructing a brand-new track — see "What is NOT
   done" above. Do not treat "the round-trip gate passed" as "arbitrary writer
   capability now exists" — check which specific edit is in the commands above
   before trusting it for something not listed there.
2. **Back up before the first real write.** Copy the whole `iPod_Control/iTunes/`
   directory off the device first.
3. **Prefer `H:\` for experiments** over `G:\` if a sacrificial device is needed —
   but ask the user first, do not assume either is expendable.
4. Do not delete files from a device to "clean up". Orphaned audio files are
   harmless; a deleted library is not.
5. **Ask before the first actual write to a device**, even once a specific edit
   is proven in-memory. Proving the bytes are right is not the same decision as
   touching the hardware.
6. **The Android USB stack is read-only by construction** — there is no
   `Write10`-calling code path anywhere in the app yet, so testing it against a
   real iPod (see the dedicated section above) cannot corrupt anything on the
   device even though the code itself is unverified. That said, it's still real
   hardware the user is plugging in themselves to test; if you're ever the one
   about to trigger a write path over USB, the same ask-first rule applies.

---

## Next steps, in order

1. **Test the Android USB stack on real hardware.** This is the single
   highest-value next step — nothing about it is provable from a dev machine.
   Install the APK on the Z Fold 7, plug in an iPod via USB-OTG, and report
   back what happens (or what exception, and where). See the dedicated section
   above.
2. **Visually check the Windows app's rendered UI.** It builds and launches
   without crashing but hasn't been eyeballed for correctness.
3. **Get a Jellyfin API key and exercise the sync path for real** — the client
   code is written but has never made a real request against
   `192.168.1.183:8096`.
4. **A real first write, scoped to what's proven.** Play count/star rating,
   track removal, adding an existing track to a playlist, and renaming a track
   all have a passing round-trip now (see Safety above). A sensible first real
   write is one of those — back up `iPod_Control/iTunes/` first, write it,
   and confirm the device (not just our own reader) still shows the library
   correctly. Ask the user first regardless of what's proven in memory.
5. **Constructing a brand-new track.** Needs: a persistent id and track id
   guaranteed not to collide with anything already on the device, a scrambled
   `F##/XXXX.ext` path picked the way the device expects, real
   duration/bitrate/size read off an actual audio file, and copying that file
   onto the device. None of the existing mutation code creates content from
   nothing — `resize-test`'s "add to playlist" specifically avoids this by
   referencing a track that already exists.
6. **Writing over USB** (not just reading) — needed before the phone can
   actually sync anything to a device, as opposed to just viewing it.
7. **Signing.** Only needed once writing is proven. See the signature-region
   notes in the README, and check them against libgpod's implementation rather
   than trusting the dumps — libgpod is LGPL, so a port makes this project LGPL.
   Flag that to the user before porting any of it.
8. **Sync engine.** Content-hash manifest, off-device, keyed by serial.
9. **Transcode.** FLAC to ALAC or AAC on copy, recording the source so re-syncs
   do not re-transcode.

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
- **`sdkmanager --licenses` piped `y` answers only work from Git Bash (`yes |
  sdkmanager ...`), not PowerShell.** PowerShell's non-interactive stdin
  (attached to the null device in this environment) swallows piped input to
  a launched batch file silently — the command exits having accepted nothing,
  with no error. If an Android SDK/tooling command needs to answer interactive
  prompts, run it from Bash.
- **A Blazor Web App's server-side routing needs its own
  `.AddAdditionalAssemblies(...)` call on `MapRazorComponents<App>()` in
  `Program.cs`** to find `@page`-routed components that live in a referenced
  Razor Class Library (`IpodSync.Shared`) — the `<Router AppAssembly=...
  AdditionalAssemblies=...>` component's own `AdditionalAssemblies` only
  covers client-side navigation *after* the first render. Miss the
  `Program.cs` one and every route in the RCL 404s on first load with no
  further explanation. MAUI Blazor Hybrid doesn't have this split (everything
  renders client-side in the WebView), so `IpodSync.Maui`'s `Routes.razor`
  only needed the one `AdditionalAssemblies`.

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
