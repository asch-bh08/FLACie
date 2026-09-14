#!/usr/bin/env bash
# Full write-path regression on a FAKE iPod root — never touches a device.
#
#   tools/fake-root-regression.sh <source> [workdir]
#
# <source> is a mounted iPod root (read only: its iTunes/ and Artwork/ folders are
# copied) or a backup folder containing iTunes/ and Artwork/. Audio files are
# represented by zero-byte placeholders. Every op in EDIT-PROTOCOL.md runs through
# the real write pipeline with --yes against the fake root, and after each write:
#   itlp-diff (CDB <-> SQLite in sync), art-check (ArtworkDB integrity),
#   hash72-verify (signatures). A fault-injected write must restore every file
#   SHA-1-identical. Exits non-zero on the first failure.
#
# Needs: a built CLI (dotnet build src/IpodSync.Cli), ffmpeg/ffprobe on PATH.
set -uo pipefail

SRC="${1:?usage: fake-root-regression.sh <ipod-root|backup-dir> [workdir]}"
WORK="${2:-$(mktemp -d)}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
CLI="$REPO/src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli.exe"
[ -x "$CLI" ] || CLI="$REPO/src/IpodSync.Cli/bin/Debug/net9.0/IpodSync.Cli"
[ -e "$CLI" ] || { echo "build the CLI first: dotnet build src/IpodSync.Cli"; exit 2; }

FAKE="$WORK/fakeipod"; MEDIA="$WORK/media"; CS="$WORK/changesets"; BK="$WORK/backups"
export IPODSYNC_TRANSCODE_CACHE="$WORK/transcode-cache" IPODSYNC_MANIFEST_DIR="$WORK/manifests"
win() { if command -v cygpath >/dev/null; then cygpath -m "$1"; else echo "$1"; fi; }
fail() { echo "REGRESSION FAILED: $*"; exit 1; }
step() { echo; echo "=== $*"; }

if [ -d "$SRC/iPod_Control" ]; then CTRL="$SRC/iPod_Control"; else CTRL="$SRC"; fi
[ -f "$CTRL/iTunes/iTunesCDB" ] || [ -f "$CTRL/iTunes/iTunesDB" ] || fail "no iTunes database under $CTRL/iTunes"

step "building fake root in $WORK"
rm -rf "$FAKE" "$BK" "$CS" "$WORK/manifests"; mkdir -p "$FAKE/iPod_Control" "$MEDIA" "$CS"
cp -a "$CTRL/iTunes" "$FAKE/iPod_Control/iTunes" || fail "copy iTunes"
[ -d "$CTRL/Artwork" ] && cp -a "$CTRL/Artwork" "$FAKE/iPod_Control/Artwork"
"$CLI" dump "$FAKE" -n 0 | grep -o "iPod_Control/Music/[^ ]*" | while read -r f; do
  mkdir -p "$FAKE/$(dirname "$f")"; : > "$FAKE/$f"; done

step "generating test media"
ff() { ffmpeg -hide_banner -loglevel error -y "$@" || fail "ffmpeg $*"; }
ff -f lavfi -i "sine=frequency=440:duration=8" -ac 2 -c:a libmp3lame -b:a 192k -metadata title="Regression MP3" -metadata artist="Regression" -metadata album="Regression Album" "$MEDIA/tone.mp3"
ff -f lavfi -i "testsrc2=size=500x500:rate=1" -frames:v 1 "$MEDIA/cover.png"
ff -f lavfi -i "smptebars=size=640x360:rate=1" -frames:v 1 "$MEDIA/wide.jpg"
ff -f lavfi -i "sine=frequency=523:duration=6:sample_rate=96000" -i "$MEDIA/cover.png" -map 0:a -map 1:v -ac 2 -c:a flac -sample_fmt s32 -c:v png -disposition:v attached_pic -metadata title="Regression FLAC" -metadata artist="Regression" -metadata album="Regression Album" "$MEDIA/tone.flac"
ff -f lavfi -i "sine=frequency=392:duration=5" -ac 2 -c:a libopus -b:a 96k -metadata title="Regression Opus" -metadata artist="Regression" "$MEDIA/tone.opus"
mkdir -p "$MEDIA/folder"; cp "$MEDIA/tone.mp3" "$MEDIA/folder/a.mp3"; cp "$MEDIA/tone.opus" "$MEDIA/folder/b.opus"

checks() {
  "$CLI" itlp-diff "$FAKE" | tail -1 | grep -q "IN SYNC" || { "$CLI" itlp-diff "$FAKE"; fail "itlp-diff after $1"; }
  if [ -d "$FAKE/iPod_Control/Artwork" ]; then "$CLI" art-check "$FAKE" >/dev/null || { "$CLI" art-check "$FAKE"; fail "art-check after $1"; }; fi
  "$CLI" hash72-verify "$FAKE" | grep -q "does NOT validate" && fail "signature invalid after $1"
  echo "    checks PASS (in sync, artwork, signatures)"
}
apply() { # name json
  echo "$2" > "$CS/$1.json"
  "$CLI" apply-edits "$FAKE" --changes "$(win "$CS/$1.json")" --yes --backup-root "$(win "$BK")" > "$CS/$1.log" 2>&1 \
    || { cat "$CS/$1.log"; fail "apply $1"; }
  grep -E "\[ok\]" "$CS/$1.log" | sed 's/^/   /'
  grep -q "WRITE VERIFIED\|nothing to write" "$CS/$1.log" || { cat "$CS/$1.log"; fail "$1 not verified"; }
  checks "$1"
}
tid() { "$CLI" dump "$FAKE" -n 0 | grep -A2 -F -- "$1" | grep -o "#[0-9]*" | head -1 | tr -d '#'; }

step "0. fault-injected write must restore everything"
SNAP=$(cd "$FAKE/iPod_Control" && find iTunes Artwork -type f -exec sha1sum {} \; | sort)
echo "{\"version\":1,\"ops\":[{\"op\":\"createPlaylist\",\"name\":\"Regression Fault\",\"trackIds\":[]},{\"op\":\"addTrackFromFile\",\"sourcePath\":\"$(win "$MEDIA/tone.flac")\"}]}" > "$CS/fault.json"
IPODSYNC_FAULT_INJECT=postverify "$CLI" apply-edits "$FAKE" --changes "$(win "$CS/fault.json")" --yes --backup-root "$(win "$BK")" > "$CS/fault.log" 2>&1
grep -q "Device restored to its pre-write state" "$CS/fault.log" || { cat "$CS/fault.log"; fail "fault injection did not restore"; }
[ "$SNAP" = "$(cd "$FAKE/iPod_Control" && find iTunes Artwork -type f -exec sha1sum {} \; | sort)" ] || fail "restore not byte-identical"
echo "    restore byte-identical PASS"
checks "fault injection"

step "1. playlists";   apply pl-create '{"version":1,"ops":[{"op":"createPlaylist","name":"Regression List","trackIds":[]}]}'
step "2. add MP3";     apply add-mp3 "{\"version\":1,\"ops\":[{\"op\":\"addTrackFromFile\",\"sourcePath\":\"$(win "$MEDIA/tone.mp3")\",\"playlist\":\"Regression List\"}]}"
step "3. add FLAC (transcode + embedded cover)"; apply add-flac "{\"version\":1,\"ops\":[{\"op\":\"addTrackFromFile\",\"sourcePath\":\"$(win "$MEDIA/tone.flac")\",\"playlist\":\"Regression List\"}]}"
step "4. add Opus (transcode to AAC)";           apply add-opus "{\"version\":1,\"ops\":[{\"op\":\"addTrackFromFile\",\"sourcePath\":\"$(win "$MEDIA/tone.opus")\",\"playlist\":\"Regression List\"}]}"
MP3=$(tid "Regression MP3"); FLAC=$(tid "Regression FLAC"); OPUS=$(tid "Regression Opus")
[ -n "$MP3" ] && [ -n "$FLAC" ] && [ -n "$OPUS" ] || fail "could not find added track ids ($MP3/$FLAC/$OPUS)"
step "5. reorder / rename"; apply pl-edit "{\"version\":1,\"ops\":[{\"op\":\"reorderPlaylist\",\"playlist\":\"Regression List\",\"trackIds\":[$OPUS,$FLAC,$MP3]},{\"op\":\"renamePlaylist\",\"playlist\":\"Regression List\",\"name\":\"Regression List 2\"}]}"
step "6. retag (relinks album/artist)"; apply retag "{\"version\":1,\"ops\":[{\"op\":\"setTrackFields\",\"trackId\":$MP3,\"fields\":{\"title\":\"Regression MP3 Retagged\",\"artist\":\"Regression Artist 2\",\"album\":\"Regression Album 2\"}}]}"
step "7. artwork set / replace / remove"
apply art-set "{\"version\":1,\"ops\":[{\"op\":\"setTrackArtwork\",\"trackIds\":[$MP3,$OPUS],\"imagePath\":\"$(win "$MEDIA/cover.png")\"}]}"
apply art-replace "{\"version\":1,\"ops\":[{\"op\":\"setTrackArtwork\",\"trackId\":$OPUS,\"imagePath\":\"$(win "$MEDIA/wide.jpg")\"}]}"
apply art-remove "{\"version\":1,\"ops\":[{\"op\":\"removeTrackArtwork\",\"trackId\":$MP3}]}"
step "8. rating / play count"; apply stats "{\"version\":1,\"ops\":[{\"op\":\"setTrackRating\",\"trackId\":$FLAC,\"stars\":4},{\"op\":\"setPlayCount\",\"trackId\":$FLAC,\"count\":7}]}"
"$CLI" itlp-diff "$FAKE" | grep -q "rating/play-count differences (info only)    0" || echo "    (note: other pre-existing rating/play-count differences present)"
step "9. remove from playlist / remove tracks / delete playlist"
apply pl-remove "{\"version\":1,\"ops\":[{\"op\":\"removeTrackFromPlaylist\",\"playlist\":\"Regression List 2\",\"trackId\":$OPUS}]}"
apply remove "{\"version\":1,\"ops\":[{\"op\":\"removeTrack\",\"trackId\":$MP3},{\"op\":\"removeTrack\",\"trackId\":$OPUS}]}"
apply pl-delete '{"version":1,"ops":[{"op":"deletePlaylist","playlist":"Regression List 2"}]}'

step "10. folder sync (twice: second run must add nothing)"
"$CLI" sync-folder "$FAKE" "$(win "$MEDIA/folder")" --yes --backup-root "$(win "$BK")" > "$CS/sync1.log" 2>&1 || { cat "$CS/sync1.log"; fail "sync-folder"; }
grep "SYNC COMPLETE" "$CS/sync1.log" | sed 's/^/   /'; checks "sync-folder"
"$CLI" sync-folder "$FAKE" "$(win "$MEDIA/folder")" > "$CS/sync2.log" 2>&1 || fail "sync-folder re-plan"
grep -q "^to add      0" "$CS/sync2.log" || { cat "$CS/sync2.log"; fail "second sync would add again"; }
echo "    re-plan adds nothing PASS"

echo; echo "REGRESSION PASSED ($WORK)"
