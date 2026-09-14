#!/usr/bin/env bash
# Publishes the Windows and Android builds as a GitHub release.
#   tools/publish-release.sh <tag> <dir with ipodsync-windows-x64.zip, ipodsync-android.apk, notes.md>
# Needs gh signed in (gh auth login). Build first with tools/build-apps.ps1.
set -euo pipefail
TAG="${1:?tag, e.g. v0.1.0}"; DIR="${2:?release dir}"
GH="$(command -v gh || echo "/c/Program Files/GitHub CLI/gh.exe")"
"$GH" release create "$TAG" --prerelease --title "ipodsync $TAG (Windows + Android)" --notes-file "$DIR/notes.md" \
  "$DIR/ipodsync-windows-x64.zip#Windows (x64, self-contained)" \
  "$DIR/ipodsync-android.apk#Android APK"
