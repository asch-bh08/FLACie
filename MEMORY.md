# Memory

Durable context for this project — the things that are not recoverable from the
code or the commit history. [HANDOFF.md](HANDOFF.md) covers project state;
this file covers the people-and-decisions layer.

---

## The user

Ashley. Windows 11 Pro. Owns a large classic-iPod collection and wants to stop
depending on iTunes to manage it.

Communicates in short, fast, informal messages — often with typos, rarely with
punctuation. Take the intent, not the literal spelling. They pushed back
correctly when I was wrong (see below), so treat their corrections as signal.

They want momentum. Long architectural essays landed worse than working code
did; the turn that actually read their library was worth more than the three
turns of design discussion before it. Bias toward building and showing.

**Toolchain on this machine** (checked 2026-09-07): .NET 9 SDK working, Java 8
**JRE only** (no `javac`), Python 3.14, git. No Node, no Rust, no JDK.

---

## Corrections I got wrong, and the user was right

**iPod Touch sync is not impossible.** I said stock-firmware Touches cannot be
synced and implied that settled it. The user replied "are you sure we cant do it
with the ipod touches bro becuase i jailbroke one" — and that changes the answer
completely. A jailbreak gives `afc2` (root filesystem over USB), root to fix
`mobile:mobile` ownership, and the ability to restart `medialibraryd`. Those
three are exactly what makes it work. **Why it matters:** I generalised from
"stock iOS blocks this" to "iOS blocks this". Check whether a constraint is
inherent or just a default before calling something impossible.

**Published format docs were wrong and I trusted them over the hardware.** I
wrote the parser from documented `mhsd` type numbers and a documented encoding
flag. Both were wrong for these devices, and both bugs only surfaced when the
parser hit a real iPod. **How to apply:** on reverse-engineering work, get to
real bytes early. The synthetic fixture I wrote first validated nothing, because
it encoded the same wrong assumptions as the reader.

---

## Decisions with reasons

- **Stock firmware, not Rockbox.** Offered Rockbox as a shortcut (it turns an
  iPod into a plain USB drive and would have made Android sync trivial). User
  rejected it — they want the real Apple interface. So we write Apple's database
  format the hard way, on purpose.
- **iTunes coexistence is a requirement, not a nice-to-have.** User: "I WANT TO
  KEEP STOCK ASWELL SO my apps works and itunes (even if wipe)". Wipes are
  acceptable to them; being locked out of iTunes is not. This is why the sync
  manifest lives off-device.
- **iPod Touch 4G deferred by explicit instruction** — "add it in but dont do
  it". Architecture keeps the seam; no iOS code gets written.
- **C# over Kotlin.** I recommended Kotlin Multiplatform for two turns, then
  reversed after checking what was installed. The user never had a JDK. Check
  the environment before recommending a stack.

---

## Project facts

- Target phone is a **Samsung Galaxy Z Fold 7**. Android support is a hard
  requirement, not a stretch goal.
- User has already turned off iTunes auto-sync. Still needs "Manually manage
  music and videos" per device before any write testing.
- Devices seen connected: `G:\` (Nano 5th gen, 16 GB, 614 tracks) and `H:\`
  (8 GB, unidentified, 635 tracks). Both FAT32.
- Also owned: Nano 7G, iPod 5th gen 80 GB, jailbroken Touch 4G on iOS 6.1.6,
  and most other models except the 6G/7G iPod Classic.
- The user's iPods hold their real music library. There is no test device.
  Treat every device as production; back up before the first write.

---

## References

- libgpod — the existing LGPL C implementation of this format, including the
  hash signing. Porting from it makes this project LGPL; raise that with the
  user before doing so.
- Format findings from the user's actual devices are in [README.md](README.md).
  Those were expensive to derive and contradict published documentation in
  three places.
