#!/bin/bash
# Keeps the family's ways in open (runs on frank every 2 minutes, flacie-entrance-watch.timer).
#   - hms-jellyfin's public 443 (what every phone's FLACie signs in to) must be a Funnel to Jellyfin on frank: put back if it is gone or changed
#   - frank's own public entrances (FLACie Web :8443, Alexa /alexa) and Jellyfin itself: an alert if they stop answering
# An alert goes to ntfy when something breaks and again when it is fixed (never every 2 minutes).
JF_HOST=root@192.168.1.182
JF_PUBLIC=https://hms-jellyfin.tailb05910.ts.net
JF_TARGET=http://192.168.1.183:8096   # frank's home address: through Funnel the Tailscale address hangs
STATE=/var/lib/flacie-entrance-watch; mkdir -p "$STATE"

notify() {   # title, message: to the same topic FLACie Web's alerts use, its token read from its own settings
  python3 - "$1" "$2" <<'PY'
import json, sys, urllib.request
n = json.load(open("/home/hms/docker/flacie-web/data/notify.json"))
r = urllib.request.Request(n["Url"].rstrip("/") + "/" + n["Topic"], data=sys.argv[2].encode(), headers={"Title": sys.argv[1], "Tags": "rotating_light"})
if n.get("Token"): r.add_header("Authorization", "Bearer " + n["Token"])
urllib.request.urlopen(r, timeout=20)
PY
}
# remembers each check's last result; tells only on a change
report() {   # name, ok(0/1), message when broken
  local was; was=$(cat "$STATE/$1" 2>/dev/null || echo 0)
  if [ "$2" = 1 ] && [ "$was" = 0 ]; then notify "FLACie: $1 down" "$3"; fi
  if [ "$2" = 0 ] && [ "$was" = 1 ]; then notify "FLACie: $1 back" "$1 is working again."; fi
  echo "$2" > "$STATE/$1"
}
answers() { curl -s -o /dev/null -m 20 -w '%{http_code}' "$1" | grep -q '^[23]'; }

# 1. Jellyfin itself
answers http://127.0.0.1:8096/System/Info/Public; jf_up=$?
report jellyfin "$jf_up" "Jellyfin on frank is not answering (docker container jellyfin)."

# 2. hms-jellyfin's public entrance: the Funnel setting, put back when missing
cfg=$(ssh -o BatchMode=yes -o ConnectTimeout=15 "$JF_HOST" "tailscale funnel status" 2>&1)
if [ $? -eq 0 ] && ! echo "$cfg" | grep -A1 -F "$JF_PUBLIC (Funnel on)" | grep -q -F "$JF_TARGET"; then
  ssh -o BatchMode=yes -o ConnectTimeout=15 "$JF_HOST" "tailscale funnel --bg --https=443 $JF_TARGET" >/dev/null 2>&1 \
    && notify "FLACie: entrance put back" "hms-jellyfin's public address had lost its Jellyfin setting; it has been put back automatically." \
    || notify "FLACie: entrance missing" "hms-jellyfin's public address has lost its Jellyfin setting and could not be put back. Run on hms-jellyfin: tailscale funnel --bg --https=443 $JF_TARGET"
  sleep 10
fi
[ "$jf_up" = 0 ] && { answers "$JF_PUBLIC/System/Info/Public"; report phones-entrance $? "Phones can't reach Jellyfin at $JF_PUBLIC (hms-jellyfin)."; }

# 3. frank's own entrances
answers https://frank.tailb05910.ts.net:8443/; report flacie-web $? "FLACie Web (https://frank.tailb05910.ts.net:8443) is not answering."
code=$(curl -s -o /dev/null -m 20 -w '%{http_code}' -X POST https://frank.tailb05910.ts.net/alexa/skill)
[ "$code" != 000 ] && [ "$code" != 502 ]; report alexa $? "The Alexa skill's address (https://frank.tailb05910.ts.net/alexa) is not answering."
exit 0
