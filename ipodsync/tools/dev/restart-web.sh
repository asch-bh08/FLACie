#!/bin/bash
# Dev helper: republish FLACie Web and (re)start it on 127.0.0.1:5255 against a MOCK Jellyfin, with a throw-away data folder.
# There is NO default Jellyfin: FLACIE_JELLYFIN_URL must be given, and it must be a mock (it has to answer GET /__mock with {"mock":true}).
#   node ipodsync/tests/web-smoke/mock-jellyfin.mjs                       (PORT=8196 by default; or: node ipodsync/tools/loadtest/mock.mjs --port 8296)
#   FLACIE_JELLYFIN_URL=http://127.0.0.1:8196 bash ipodsync/tools/dev/restart-web.sh                        (as an admin account; mock login tester / pw)
#   FLACIE_JELLYFIN_URL=http://127.0.0.1:8196 FLACIE_ADMINS=someoneelse bash ipodsync/tools/dev/restart-web.sh   (to test as a NON-admin)
# Must be a PUBLISHED build (dotnet run serves an empty app.css). Sign the browser in with http://127.0.0.1:5255/debug/login
# (/debug/token returns the signed-in account's token (loopback only) so /api/* can be called from the browser console).
# Never point this at a real Jellyfin: tests have left playlists on one before.
if [ -z "$FLACIE_JELLYFIN_URL" ]; then
  echo "REFUSING TO RUN: set FLACIE_JELLYFIN_URL to a mock Jellyfin (see the header of this script). There is no default." >&2; exit 2
fi
if [ "$(curl -s -m 5 "$FLACIE_JELLYFIN_URL/__mock" | tr -d ' ')" != '{"mock":true}' ]; then
  echo "REFUSING TO RUN: $FLACIE_JELLYFIN_URL does not answer GET /__mock with {\"mock\":true}, so it is not a mock Jellyfin." >&2; exit 3
fi
D=${FLACIE_DEV_DIR:-$HOME/flacie-dev}; mkdir -p "$D"   # data goes to $D/webdata-mock: an older $D/webdata may hold a real signed-in account and is never reused
for pid in $(netstat -ano | grep ":5255 " | awk '{print $5}' | sort -u); do taskkill //PID $pid //F >/dev/null 2>&1; done
sleep 1
cd "$(dirname "$0")/../.." && dotnet publish src/FLACie.Server -c Release -o "$D/pub" 2>&1 | grep -E "error" | head -5
cd "$D/pub" && (FLACIE_JELLYFIN_URL="$FLACIE_JELLYFIN_URL" FLACIE_DATA="$D/webdata-mock" FLACIE_DEBUG=1 nohup dotnet FLACie.Server.dll --urls http://127.0.0.1:5255 > "$D/web.log" 2>&1 &)
sleep 6; curl -s -o /dev/null -w "up %{http_code}\n" http://127.0.0.1:5255/healthz
