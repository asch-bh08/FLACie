#!/usr/bin/env python3
"""
Build a small synthetic iTunesDB for testing the reader.

This only proves the reader is self-consistent with the format spec we coded
against -- it is NOT proof of correctness against a real device, since both
sides encode the same assumptions. Real verification means pointing the reader
at an actual iPod. Use this to catch crashes, off-by-ones and structural bugs.
"""
import struct
import sys
import os

MAC_EPOCH_OFFSET = 2082844800  # 1904-01-01 -> 1970-01-01, in seconds


def u32(v):
    return struct.pack('<I', v & 0xFFFFFFFF)


def u64(v):
    return struct.pack('<Q', v & 0xFFFFFFFFFFFFFFFF)


def pad(block, size):
    """Pad a chunk header out to its declared header length."""
    assert len(block) <= size, f'header {len(block)} exceeds declared {size}'
    return block + b'\x00' * (size - len(block))


def mhod_string(mhod_type, text):
    """A string data object: 0x18 header, then position/len/encoding/reserved."""
    data = text.encode('utf-16-le')
    hdr = pad(b'mhod' + u32(0x18) + u32(0x18 + 16 + len(data)) + u32(mhod_type), 0x18)
    body = u32(1) + u32(len(data)) + u32(0) + u32(0) + data
    return hdr + body


def mhit(track_id, persistent_id, title, artist, album, location,
         *, ms=210000, size=5_000_000, bitrate=192, samplerate=44100,
         track_no=1, total_tracks=12, year=2004, plays=3, stars=0, disc=1, discs=1):
    mhods = b''.join([
        mhod_string(1, title),
        mhod_string(2, location),
        mhod_string(3, album),
        mhod_string(4, artist),
    ])
    n = 4
    hlen = 0x184

    h = bytearray(hlen)
    h[0x00:0x04] = b'mhit'
    h[0x04:0x08] = u32(hlen)
    h[0x08:0x0C] = u32(hlen + len(mhods))
    h[0x0C:0x10] = u32(n)
    h[0x10:0x14] = u32(track_id)
    h[0x14:0x18] = u32(1)          # visible
    h[0x18:0x1C] = b'MP3 '         # filetype
    h[0x1C] = 0                    # type1
    h[0x1D] = 0                    # type2
    h[0x1E] = 0                    # compilation
    h[0x1F] = stars * 20           # rating, 0-100 in steps of 20
    h[0x20:0x24] = u32(1_100_000_000 + MAC_EPOCH_OFFSET)   # last modified
    h[0x24:0x28] = u32(size)
    h[0x28:0x2C] = u32(ms)
    h[0x2C:0x30] = u32(track_no)
    h[0x30:0x34] = u32(total_tracks)
    h[0x34:0x38] = u32(year)
    h[0x38:0x3C] = u32(bitrate)
    h[0x3C:0x40] = u32(samplerate << 16)
    h[0x50:0x54] = u32(plays)
    h[0x58:0x5C] = u32(1_600_000_000 + MAC_EPOCH_OFFSET)   # last played
    h[0x5C:0x60] = u32(disc)
    h[0x60:0x64] = u32(discs)
    h[0x70:0x78] = u64(persistent_id)
    return bytes(h) + mhods


def mhip(track_id, position):
    hlen = 0x4C
    h = bytearray(hlen)
    h[0x00:0x04] = b'mhip'
    h[0x04:0x08] = u32(hlen)
    h[0x08:0x0C] = u32(hlen)
    h[0x0C:0x10] = u32(0)          # no child mhods
    h[0x14:0x18] = u32(position)   # group id
    h[0x18:0x1C] = u32(track_id)
    return bytes(h)


def mhyp(name, track_ids, is_master=False, persistent_id=0x1122334455667788):
    children = mhod_string(1, name) + b''.join(
        mhip(tid, i) for i, tid in enumerate(track_ids))
    hlen = 0x30
    h = bytearray(hlen)
    h[0x00:0x04] = b'mhyp'
    h[0x04:0x08] = u32(hlen)
    h[0x08:0x0C] = u32(hlen + len(children))
    h[0x0C:0x10] = u32(1)                  # one string mhod
    h[0x10:0x14] = u32(len(track_ids))
    h[0x14] = 1 if is_master else 0
    h[0x18:0x1C] = u32(1_200_000_000 + MAC_EPOCH_OFFSET)
    h[0x1C:0x24] = u64(persistent_id)
    return bytes(h) + children


def mhlt(tracks):
    return pad(b'mhlt' + u32(0x5C) + u32(len(tracks)), 0x5C) + b''.join(tracks)


def mhlp(playlists):
    return pad(b'mhlp' + u32(0x5C) + u32(len(playlists)), 0x5C) + b''.join(playlists)


def mhsd(kind, payload):
    hdr = pad(b'mhsd' + u32(0x60) + u32(0x60 + len(payload)) + u32(kind), 0x60)
    return hdr + payload


def mhbd(datasets):
    payload = b''.join(datasets)
    h = bytearray(0x68)
    h[0x00:0x04] = b'mhbd'
    h[0x04:0x08] = u32(0x68)
    h[0x08:0x0C] = u32(0x68 + len(payload))
    h[0x0C:0x10] = u32(1)
    h[0x10:0x14] = u32(0x19)       # version
    h[0x14:0x18] = u32(len(datasets))
    h[0x18:0x20] = u64(0xDEADBEEFCAFEBABE)
    return bytes(h) + payload


def build():
    tracks = [
        mhit(101, 0xAAAA0000000000001, 'Paranoid Android', 'Radiohead', 'OK Computer',
             ':iPod_Control:Music:F00:ABCD.mp3', ms=383000, track_no=2, stars=5, plays=41),
        mhit(102, 0xAAAA0000000000002, 'Karma Police', 'Radiohead', 'OK Computer',
             ':iPod_Control:Music:F01:EFGH.mp3', ms=261000, track_no=6, stars=4, plays=17),
        mhit(103, 0xAAAA0000000000003, 'Teardrop', 'Massive Attack', 'Mezzanine',
             ':iPod_Control:Music:F02:IJKL.mp3', ms=330000, track_no=4, stars=3, plays=8),
    ]
    all_ids = [101, 102, 103]

    playlists = [
        mhyp('iPod', all_ids, is_master=True),
        mhyp('Late Night', [103, 101]),
        mhyp('Radiohead Only', [101, 102]),
    ]

    return mhbd([
        mhsd(1, mhlt(tracks)),
        mhsd(4, mhlp(playlists)),
    ])


if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else 'fixtures/iTunesDB'
    os.makedirs(os.path.dirname(out) or '.', exist_ok=True)
    data = build()
    with open(out, 'wb') as f:
        f.write(data)
    print(f'wrote {out} ({len(data)} bytes)')
