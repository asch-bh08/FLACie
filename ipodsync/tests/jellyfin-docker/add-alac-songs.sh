M="/root/ct/music/Test Artist Alac/Alac Album"; mkdir -p "$M"
cd /mnt/c/Users/ashle/AppData/Local/Temp
cp "alac/Dark"*horse*.m4a "$M/01 Dark horse real.m4a"
cp "alac/1 thing hoodtrap.m4a" "$M/02 One thing real.m4a"
ffmpeg -v error -nostdin -y -f lavfi -i "sine=f=330:d=12:r=44100" -f lavfi -i "sine=f=440:d=12:r=44100" -filter_complex "[0][1]amerge=inputs=2" -metadata title="Alac Tone Stereo" -metadata artist="Test Artist Alac" -metadata album="Alac Album" -metadata track=3 -c:a alac "$M/03 tone.m4a"
ffmpeg -v error -nostdin -y -t 20 -i "alac/Dark"*horse*.m4a -c:a alac -sample_fmt s32p -bits_per_raw_sample 24 -metadata title="Alac 24 bit" -metadata artist="Test Artist Alac" -metadata album="Alac Album" -metadata track=4 "$M/04 hires.m4a"
ls "$M"
B='MediaBrowser Client="ct", Device="ct", DeviceId="ct21", Version="1"'
for P in 8096 8097; do J=http://localhost:$P
  T=$(curl -s -X POST -H 'Content-Type: application/json' -H "Authorization: $B" -d '{"Username":"admin","Pw":"adminpw1"}' $J/Users/AuthenticateByName | jq -r .AccessToken)
  AT="Authorization: $B, Token=\"$T\""
  curl -s -X POST -H "$AT" $J/Library/Refresh -o /dev/null
  for i in $(seq 1 40); do n=$(curl -s -H "$AT" "$J/Items?IncludeItemTypes=Audio&Recursive=true&Limit=0" | jq .TotalRecordCount); [ "$n" = 16 ] && break; sleep 4; done
  echo "port $P songs: $n"
done
