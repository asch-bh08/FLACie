export DEBIAN_FRONTEND=noninteractive
systemctl start docker 2>/dev/null
mkdir -p /root/ct/music && cd /root/ct
if [ ! -d music/Test\ Artist\ One ]; then
i=0
for a in "Test Artist One" "Test Artist Two"; do for al in "Album A" "Album B"; do for t in 1 2 3; do i=$((i+1));
 d="music/$a/$al"; mkdir -p "$d"
 ffmpeg -loglevel error -y -f lavfi -i "sine=frequency=$((300+i*40)):duration=20" -metadata title="Song $i" -metadata artist="$a" -metadata album_artist="$a" -metadata album="$al" -metadata track=$t -metadata date=$((1995+i)) -metadata genre=Rock "$d/0$t Song $i.flac"
done; done; done
fi
setup() { # name port image
  docker rm -f $1 >/dev/null 2>&1; rm -rf /root/ct/$1; mkdir -p /root/ct/$1/config /root/ct/$1/cache
  docker run -d --name $1 --restart unless-stopped -p $2:8096 -v /root/ct/$1/config:/config -v /root/ct/$1/cache:/cache -v /root/ct/music:/media/music:ro $3 >/dev/null 2>&1
  J=http://localhost:$2; H='Content-Type: application/json'; B='MediaBrowser Client="ct", Device="ct", DeviceId="ct6", Version="1"'
  for i in $(seq 1 80); do [ "$(curl -s -o /dev/null -m3 -w %{http_code} -H "Authorization: $B" $J/Startup/Configuration)" = 200 ] && break; sleep 3; done
  curl -s -X POST -H "$H" -H "Authorization: $B" -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' $J/Startup/Configuration -o /dev/null
  curl -s -H "Authorization: $B" $J/Startup/User -o /dev/null
  curl -s -X POST -H "$H" -H "Authorization: $B" -d '{"Name":"admin","Password":"adminpw1"}' $J/Startup/User -o /dev/null
  curl -s -X POST -H "$H" -H "Authorization: $B" -d '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}' $J/Startup/RemoteAccess -o /dev/null
  curl -s -X POST -H "$H" -H "Authorization: $B" $J/Startup/Complete -o /dev/null
  sleep 10; for i in $(seq 1 40); do curl -sf -m3 $J/Users/Public >/dev/null && break; sleep 3; done
  TOK=$(curl -s -X POST -H "$H" -H "Authorization: $B" -d '{"Username":"admin","Pw":"adminpw1"}' $J/Users/AuthenticateByName | jq -r .AccessToken)
  AT="Authorization: $B, Token=\"$TOK\""
  curl -s -X POST -H "$H" -H "$AT" -d '{"LibraryOptions":{"PathInfos":[{"Path":"/media/music"}]}}' "$J/Library/VirtualFolders?name=Music&collectionType=music&refreshLibrary=true" -o /dev/null
  curl -s -H "$AT" $J/System/Configuration | jq '.QuickConnectAvailable=true' | curl -s -X POST -H "$H" -H "$AT" -d @- $J/System/Configuration -o /dev/null
  curl -s -X POST -H "$H" -H "$AT" -d '{"Name":"listener","Password":"listen123"}' $J/Users/New -o /dev/null
  for i in $(seq 1 30); do n=$(curl -s -H "$AT" "$J/Items?IncludeItemTypes=Audio&Recursive=true&Limit=0" | jq .TotalRecordCount); [ "$n" = 12 ] && break; sleep 4; done
  echo "$1 $3 port $2: token ${#TOK} songs $n version $(curl -s $J/System/Info/Public | jq -r '.Version // .version')"
}
setup jf 8096 jellyfin/jellyfin:latest
setup jf10 8097 jellyfin/jellyfin:10.10.7
