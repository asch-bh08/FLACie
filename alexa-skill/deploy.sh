#!/usr/bin/env bash
# Deploys the FLACie skill to YOUR Amazon developer account (development stage). Run `ask configure` yourself first; this script never handles credentials.
#   ./deploy.sh https://flacie.example.com [amzn1.ask.skill....]
set -euo pipefail
url="${1:?usage: deploy.sh https://your-flacie-host [skill-id]}"; skill="${2:-}"
[[ "$url" =~ ^https://[^/:]+(/.*)?$ ]] || { echo "PublicUrl must be https on port 443"; exit 1; }
command -v ask >/dev/null || { echo "ask-cli is not installed: npm install -g ask-cli ; ask configure"; exit 1; }
here="$(cd "$(dirname "$0")" && pwd)"; build="$here/.build"
# .build keeps ask-cli's own state (.ask/ask-states.json holds the skill id), so a second run updates the same skill
mkdir -p "$build"; rm -rf "$build/skill-package"; cp -r "$here/skill-package" "$build/skill-package"
base="${url%/}"
sed -i "s#https://YOUR-FLACIE-HOST#${base}#g" "$build/skill-package/skill.json"
printf '{"askcliResourcesVersion":"2020-03-31","profiles":{"default":{"skillMetadata":{"src":"./skill-package"}}}}' > "$build/ask-resources.json"
if [ -n "$skill" ]; then mkdir -p "$build/.ask"; printf '{"askcliStatesVersion":"2020-03-31","profiles":{"default":{"skillId":"%s"}}}' "$skill" > "$build/.ask/ask-states.json"; fi
cd "$build" && ask deploy --target skill-metadata
echo "Done. Put the skill id into FLACie > Settings > Alexa > Skill ID, then enable the skill in the Alexa app."
