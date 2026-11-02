
# guard shared by probe-auth-forms.sh and add-alac-songs.sh (source it): only the throw-away "FLACie test" containers made by start-test-jellyfins.sh
require_test_jellyfin() { n=$(curl -s -m5 "$1/System/Info/Public" | jq -r .ServerName); case "$n" in "FLACie test"*) ;; *) echo "REFUSING TO RUN: $1 is not a FLACie test container (server name: $n)" >&2; exit 3;; esac; }
