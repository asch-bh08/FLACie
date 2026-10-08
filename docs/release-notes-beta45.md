Beta 45 (Android 60, MSI 0.45.0). Still a beta, not a 1.0 final.

**Jellyfin 12 works (web, Windows, Android)**
- Jellyfin 12.x refuses the old `X-Emby-Token` header and `api_key=` link parameter (HTTP 401) and only accepts the `Authorization: MediaBrowser ... Token="..."` header (or `ApiKey=` in a link). Every Jellyfin call in the Android app, the Windows app's Jellyfin tab and FLACie Web now uses those; Connect (remote control) and the live socket use `ApiKey=`. Jellyfin 10.10 accepts all of them, so it keeps working.

**Sign-in lock-out fixed (web)**
- Wrong guesses at a user name no longer lock that user out. Sign-ins are refused only per client address (8 wrong tries in 15 minutes); wrong guesses at one name just make further tries at that name wait 1, 2, 4, up to 8 seconds.

**Android: Jellyfin music source switches on by itself**
- Signing in with Jellyfin now also connects the library (no trip to Settings > Music sources > "Use my account").

**Downloads (web, Windows)**
- When YouTube could not be asked because its service was unreachable, the failure says so ("YouTube was never asked (its service could not be reached); try again in a minute") instead of only "Not found".
- New setting `FLACIE_SERVICE_ROUTES` (`public=>internal` pairs): this server reaches a download service on its own machine directly, so a problem with the public name (it was the DNS name of the Tailscale funnel) cannot stop downloads. The saved address, and so the phone, are unchanged.
- Failed downloads have a **Retry** button each and a **Retry all** at the top of the Failed list (songs only, not whole albums).
- In the queue, a small line under a song says "Downloading" or "Downloaded just now / today / this week" (and "Couldn't be downloaded") for songs Autoplay fetched; it now follows downloads as they change.

**Also**: a sign-in cookie whose Jellyfin token Jellyfin has dropped is signed out to the sign-in page (beta 44 follow-up); on a server locked to one Jellyfin, the admin check no longer depends on which address the account was saved under; MIT licence, security notes, self-hosting guide and a Playwright web smoke test run on every push.

**Tested**
- Against real Jellyfin 12.2.0 and 10.10.7 (Docker): FLACie Web signs in, lists the library, plays, shows the dashboard to the admin, and Jellyfin lists FLACie Web as an active remote-controllable session on both; the Android release APK signs in with Quick Connect, shows the library without a manual step, plays a song, and Jellyfin lists the app as remote-controllable (12.2) / plays (10.10.7); SyncPlay list and create calls succeed on both with the new header. Web smoke test (32 tests) and Android unit tests pass; Windows app starts, serves its pages and stops with the app.
- Frank runs this web build (rollback image kept); the local file-mover address answers.

**Not tested**
- Retry and Retry all and the YouTube service route with a real failing download (no download was run); two real devices in a Jam; the Windows MSI installer itself; a real phone; the queue note while a real autoplay download runs.
