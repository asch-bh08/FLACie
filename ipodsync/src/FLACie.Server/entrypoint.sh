#!/bin/sh
# Docker creates a bind-mounted /data owned by root: hand it to the app user, then run as that user, never as root.
# PUID/PGID run it as a specific host user instead (to match files you already own).
set -e
uid="${PUID:-1654}"; gid="${PGID:-1654}"
mkdir -p "${FLACIE_DATA:-/data}"
chown -R "$uid:$gid" "${FLACIE_DATA:-/data}" 2>/dev/null || true
exec setpriv --reuid="$uid" --regid="$gid" --clear-groups dotnet /app/FLACie.Server.dll
