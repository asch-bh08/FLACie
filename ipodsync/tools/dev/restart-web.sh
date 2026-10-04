#!/bin/bash
# Dev helper: republish FLACie Web and (re)start it on 127.0.0.1:5255 against the real Jellyfin, with a throw-away data folder.
#   bash ipodsync/tools/dev/restart-web.sh                       (as an admin account)
#   FLACIE_ADMINS=someoneelse bash ipodsync/tools/dev/restart-web.sh   (to test as a NON-admin)
# Must be a PUBLISHED build (dotnet run serves an empty app.css). Sign the browser in with http://127.0.0.1:5255/debug/login
# (works once the account has been approved through Jellyfin Quick Connect on this data folder). /debug/token returns the signed-in account's
# token (loopback only) so /api/* can be called from the browser console.
D=${FLACIE_DEV_DIR:-$HOME/flacie-dev}; mkdir -p "$D"
for pid in $(netstat -ano | grep ":5255 " | awk '{print $5}' | sort -u); do taskkill //PID $pid //F >/dev/null 2>&1; done
sleep 1
cd "$(dirname "$0")/../.." && dotnet publish src/FLACie.Server -c Release -o "$D/pub" 2>&1 | grep -E "error" | head -5
cd "$D/pub" && (FLACIE_JELLYFIN_URL=http://100.114.148.48:8096 FLACIE_DATA="$D/webdata" FLACIE_DEBUG=1 nohup dotnet FLACie.Server.dll --urls http://127.0.0.1:5255 > "$D/web.log" 2>&1 &)
sleep 6; curl -s -o /dev/null -w "up %{http_code}\n" http://127.0.0.1:5255/healthz
