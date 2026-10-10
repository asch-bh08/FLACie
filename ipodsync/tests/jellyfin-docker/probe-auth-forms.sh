B='MediaBrowser Client="ct", Device="ct", DeviceId="ct9", Version="1"'
for P in 8096 8097; do
J=http://localhost:$P
T=$(curl -s -X POST -H 'Content-Type: application/json' -H "Authorization: $B" -d '{"Username":"listener","Pw":"listen123"}' $J/Users/AuthenticateByName | jq -r .AccessToken)
echo "== port $P $(curl -s $J/System/Info/Public | jq -r '.Version // .version')"
echo "  Authorization header : $(curl -s -o /dev/null -w %{http_code} -H "Authorization: $B, Token=\"$T\"" $J/Users/Me)"
echo "  X-Emby-Token         : $(curl -s -o /dev/null -w %{http_code} -H "X-Emby-Token: $T" $J/Users/Me)"
echo "  api_key query        : $(curl -s -o /dev/null -w %{http_code} "$J/Users/Me?api_key=$T")"
echo "  ApiKey query         : $(curl -s -o /dev/null -w %{http_code} "$J/Users/Me?ApiKey=$T")"
echo "  stream via ApiKey    : $(curl -s -o /dev/null -w %{http_code} -r 0-100 "$J/Audio/$(curl -s -H "Authorization: $B, Token=\"$T\"" "$J/Items?IncludeItemTypes=Audio&Recursive=true&Limit=1" | jq -r .Items[0].Id)/stream?static=true&ApiKey=$T")"
WSK="Connection: Upgrade"
echo "  socket hdr auth      : $(curl -s -m4 -o /dev/null -w %{http_code} -H "$WSK" -H 'Upgrade: websocket' -H 'Sec-WebSocket-Version: 13' -H 'Sec-WebSocket-Key: x3JJHMbDL1EzLkh9GBhXDw==' -H "Authorization: $B, Token=\"$T\"" "$J/socket?deviceId=ct9")"
echo "  socket api_key       : $(curl -s -m4 -o /dev/null -w %{http_code} -H "$WSK" -H 'Upgrade: websocket' -H 'Sec-WebSocket-Version: 13' -H 'Sec-WebSocket-Key: x3JJHMbDL1EzLkh9GBhXDw==' "$J/socket?api_key=$T&deviceId=ct9")"
echo "  socket ApiKey        : $(curl -s -m4 -o /dev/null -w %{http_code} -H "$WSK" -H 'Upgrade: websocket' -H 'Sec-WebSocket-Version: 13' -H 'Sec-WebSocket-Key: x3JJHMbDL1EzLkh9GBhXDw==' "$J/socket?ApiKey=$T&deviceId=ct9")"
done
