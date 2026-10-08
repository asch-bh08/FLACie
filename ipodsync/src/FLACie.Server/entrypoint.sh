#!/bin/sh
# Docker creates a bind-mounted /data owned by root: hand it to the app user, then run as that user, never as root.
# PUID/PGID run it as a specific host user instead (to match files you already own).
set -e
uid="${PUID:-1654}"; gid="${PGID:-1654}"
mkdir -p "${FLACIE_DATA:-/data}"
chown -R "$uid:$gid" "${FLACIE_DATA:-/data}" 2>/dev/null || true
# the key ring and remembered accounts are private to the app user
mkdir -p "${FLACIE_DATA:-/data}/keys" "${FLACIE_DATA:-/data}/users"
chmod 700 "${FLACIE_DATA:-/data}/keys" "${FLACIE_DATA:-/data}/users" 2>/dev/null || true
chown "$uid:$gid" "${FLACIE_DATA:-/data}/keys" "${FLACIE_DATA:-/data}/users" 2>/dev/null || true
exec setpriv --reuid="$uid" --regid="$gid" --clear-groups dotnet /app/FLACie.Server.dll
