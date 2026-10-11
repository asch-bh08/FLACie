# G1 web fixes (branch fix/web-g1, from release/beta50)

## Changes
- /api/import: PlaylistImport.Parse now refuses (InvalidDataException -> 400) bodies with control/NUL bytes, invalid UTF-8 (the Latin-1 fallback is gone), JSON/HTML bodies and lines over 1000 chars. Plain song names, .m3u and .txt lists still import.
- /art/..: Kestrel folded a literal `/art/..` into `/`. A middleware in Program.cs now answers 400 to any request line with a `.`/`..` segment (checked on the raw target, also percent-encoded). Spec uses a raw socket request.
- Variant matching: BaseTitle keeps trailing (Piano Version)/(Orchestral)/(Stripped)/(Lullaby)/(Tribute)/(Reprise)/(String(s)) tags; Slskd ranking rejects piano/acoustic/orchestral/unplugged/stripped/lullaby/tribute/reprise/extended files when not requested. Before, the piano request was reduced to the plain title and matched the original file.
- Autoplay: DownloadManager keeps the last 500 successful fetches per user (RecentKeys); AutoplayPlanner.Prefetch and FetchAsync (kind Autoplay) skip them.
- Mobile bottom nav (app.css): at <=760px the rail is one horizontally scrollable row, all links visible (admins get all 11), each >=44px, no overlap.
- Skip bursts (flacie.js): a load within 500 ms of the previous one drops the loading stream and waits 150 ms for the next skip, so only the song you stop on opens a stream. The server stream endpoints already honour RequestAborted.

## Tests
- .NET: 84 passed (was 70; new tests in tests/FLACie.Tests/G1Tests.cs).
- Playwright: 85 passed (new: /art/.. raw request, import 400/200 matrix, bottom nav at 375 and 320, skip-burst stream count).

## Not done / notes
- The "same length file" bug was reproduced only through the ranking logic (unit tests), not against a real slskd.
- Invalid UTF-8 playlists (old Latin-1 exports) are now refused by design, per the task.
- Skip-burst test drives flacie.load directly, not the Blazor player UI.
