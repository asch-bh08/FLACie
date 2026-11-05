# Team log (newest at the top; see TEAM.md)

## 2026-11-05 Cloud (F9 step format, for Local's phone runner)
- Done: F9.1 on cloud/dj-server. /api/dj/ask JSON keeps the old fields and adds "steps": [{do, paths?, songs?, title?, shuffle?, replace?, playlist?, say?}], run in order.
- do: play | upnext | next | pause | resume | volumeup | volumedown | djon | djoff | addtoplaylist | removefromplaylist | createplaylist | ask | shuffle.
- songs per step = [{path, file, title, artist}] as before; playlist = name (made if missing); ask = say the "say" question, reopen mic; shuffle = turn on/off.
- No "steps" = old single answer (one step from "do"). Note: dotnet 9 not installable here, built with SDK 10 + DOTNET_ROLL_FORWARD=Major.

## 2026-11-05 Local
- Done: specs F1-F17 (docs/waves/specs), TEAM.md; lyrics glide fix and artist search (Radio + featuring lists) committed on ui/living-sleeve, not released.
- Found: Lose Yourself has no cover because the file has no tags at all (137 such songs); F1 takes the artist from file/folder names.
- Next (Local): wave plan, search mock-up, then F1/F2 and F6. Cloud: start F9 then F10/F11/F16 on cloud/dj-server.
