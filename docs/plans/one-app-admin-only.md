# ONE APP 1i: server-wide controls are for administrators only

An administrator is whoever `AdminAccess.Is` says (FLACIE_ADMINS, else the administrators of the locked Jellyfin). Everyone else signs in and
gets music, their own playlists, their own settings and their own downloads, and never sees or reaches the server's plumbing.

## The server-wide controls and how each is kept to administrators
| Control | Where (web) | Hidden in the UI | Refused by the server |
|---|---|---|---|
| Server admin dashboard (devices, users, server, storage, notifications, allow downloads) | /dashboard (Admin.razor) | rail AdminLink, account menu: admins only | the page shows "for the server's administrators" and none of its blocks |
| Load and activity numbers | GET /api/admin/load | (dashboard only) | 403 |
| Server settings (download policy, prefetch, free space) | POST /api/settings | in Server admin (dashboard) | 403 |
| Daily chart downloads | POST /api/charts/run | (Charts page; the run is the server's) | 403 |
| DJ voice test (server's voice service) | POST /api/dj/test | (no page calls it) | 403 |
| Everyone's downloads log | /downloads (Downloads.razor) | account menu: admins only; "Everyone's downloads" admins only | the page says it is for administrators |
| Bulk imports (playlist files, links) | /import, POST /api/import, /api/import/link | Playlists hides Import unless `MayBulk` | ImportNotAllowedException -> 403 |
| /debug/* | FLACIE_DEBUG=1 only, loopback only | never linked | 404 from anywhere else |

Per-person, so open to everyone (each sees only their own): /api/settings GET (their view), /api/downloads and /api/downloads/retry (their own
jobs), /api/dj/settings (their own DJ), /api/library/fixes (read-only, their library), /api/made, /api/where, /api/played, /api/listen.

On the phone: the account menu lists Server admin and Downloads only for administrators (`accountPicks`, AccountMenuTest), and Settings
shows its Server admin button only when `canAdmin`.

## Checks
- Web: `ipodsync/tests/web-smoke/1w-admin-only.spec.ts` signs in as `listener` (a normal user of the mock Jellyfin) and checks the menus, the
  admin pages and a 403 from each server-wide endpoint above.
- Phone: AccountMenuTest (no Server admin or Downloads for a normal user). No emulator check from the cloud session.
- Still to do (1j): the Library section of Settings (Add NAS / Add Jellyfin) is shown to everyone; for a normal user it should not be.
