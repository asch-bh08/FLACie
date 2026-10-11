# Test run 2026-10-11, workstream D-msi

Baseline: beta 49 (MSI 0.49.0). Machine: Windows 11 Pro 10.0.26200, non-elevated user session. Previous build under test for upgrade:
0.48.0 (`gh release download v1.0.0-beta48`). Logs were in `C:\FLACie_run\D` (removed at the end; the key lines are quoted below).

## Result table

| # | Item | Result | Evidence |
|---|------|--------|----------|
| 1 | Snapshot before (programs, services, tasks, startup, firewall, ports, folders, registry) | pass | Nothing FLACie-related registered. Pre-existing from earlier dev runs, not from the MSI: `%LOCALAPPDATA%\FLACie\web` (keys, device-id), `%LOCALAPPDATA%\ipodsync`, `%LOCALAPPDATA%\User Name\dev.ashley.ipodsync`, `Documents\ipodsync`, firewall rules `IpodSync.Web` x2 (dev), old `Temp\flacie-msi` build dir. Left untouched. |
| 2 | MSI metadata 0.49.0 | pass | ProductName FLACie, Manufacturer FLACie, ProductVersion 0.49.0, ProductCode `{4A985BE2-8E88-4437-95A9-E612F557D839}` (0.48.0: `{BB0504F3-...}`), UpgradeCode `{B480A45D-C957-4961-8E9C-6D1802B43BAC}` (same in both), Template `x64;0`, 990 files, 108,984,248 bytes (104 MB), per-user (`Scope=perUser`, no ALLUSERS), `MajorUpgrade` with `DowngradeErrorMessage`, `Authenticode status NotSigned`. Installed size 342 MB, ARP EstimatedSize 351,605 KB. |
| 3 | Silent install 0.48.0 per user, no admin | pass | `msiexec /i ... /qn` exit 0 in 6 s, non-elevated. Files in `%LOCALAPPDATA%\Programs\FLACie` (990), shortcut `Start Menu\Programs\FLACie.lnk`, `HKCU\Software\ipodsync\installed=1`, ARP entry "FLACie 0.48.0" (Windows registered it under HKLM Uninstall by the installer service; visible to the user as normal). Log: `Installation completed successfully`. |
| 4 | Launch app, embedded server | pass | `IpodSync.Maui.exe` -> child `FLACie.Server.exe` listening only on `127.0.0.1:5287`; `/healthz` = `200 "ok"` within 1 s. Window titled FLACie shows FLACie tab (sign-in page, Jellyfin/NAS tabs, Quick Connect button) and "iPod manager" tab; screenshot checked. |
| 5 | Unauthenticated HTTP surface | pass | `/` 302 to `/login?ReturnUrl=%2F`; `/login` 200 (HTML, 6.3 KB); `/healthz` 200; `/manifest.webmanifest` 200; `/search` 302 to login; `/api/settings` 401; unknown paths 404. Headers on `/login`: `X-Content-Type-Options: nosniff`, `Referrer-Policy: same-origin`, `X-Frame-Options: SAMEORIGIN`, `Permissions-Policy: camera=(), microphone=(), geolocation=()`, `Content-Security-Policy: frame-ancestors 'self'`, `Cache-Control: no-cache, no-store`, antiforgery cookie `httponly; samesite=strict`. No HSTS/full CSP (loopback http, acceptable; see note N3). |
| 6 | Upgrade 0.48.0 -> 0.49.0 in place (app running) | pass with caveats | exit 0 in 26 s. One ARP entry (0.49.0, ProductCode `{4A985BE2...}`), no side-by-side folder, same `Programs\FLACie`, one Start menu shortcut, `IpodSync.Maui.dll` FileVersion 0.49.0.0, user data and marker file in `%LOCALAPPDATA%\FLACie\web` kept. Caveats: see BUG 2 (running app is not closed; old processes keep running 0.48 code, 255 reboot-pending deletes). |
| 7 | Launch 0.49.0 after upgrade | pass | After closing the app, server child exits in <1 s. New launch healthz 200 in 0.5 s, server path is the install dir. |
| 8 | Repair `msiexec /fa` | pass | Deleted `flacie-web\FLACie.Server.exe`, deleted `wwwroot`, appended bytes to `IpodSync.Core.dll`; `/fa` exit 0 restored all three (hash equal to original). |
| 9 | Repair of shortcut | pass (by design) | `/fa` does not restore a deleted Start menu shortcut (only files); `msiexec /fomus {ProductCode}` does. |
| 10 | Downgrade 0.48.0 over 0.49.0 | pass | exit 1603, log `LaunchConditions ... A newer version of FLACie is already installed.` Installed version stays 0.49.0. |
| 11 | Reinstall same version | pass | exit 0, configuration completed, version 0.49.0. |
| 12 | Uninstall, app NOT running | pass (minus BUG 1) | exit 0 in 5 s. All product files, shortcut and `HKCU\Software\ipodsync` removed; only `IpodSync.Maui.exe.WebView2` (225 files) left in the install dir (BUG 1). `%LOCALAPPDATA%\FLACie\web` untouched. |
| 13 | Uninstall, app RUNNING | fail | exit 0 reported success but app and server keep running from deleted files; server still answers `/healthz` on 5287 afterwards (BUG 2). After closing the app the server stopped, `flacie-web` empty dir left. |
| 14 | Server child stops with the app | pass | Graceful close: server gone in <1 s. Hard kill of the app (`Stop-Process -Force`): its server exited within 10 s (parent-pid watch works). No orphan `FLACie.Server.exe` observed in any scenario. |
| 15 | Second launch while one runs | fail (minor) | No single-instance guard: a second window and a second server on 5288 start (BUG 3). Both stop cleanly when their app closes. |
| 16 | Port 5287 in use (loopback listener) | pass | App moves to 5288, healthz ok, my listener on 5287 not disturbed. With 5287 and 5288 held on 0.0.0.0 it used 5289. |
| 17 | Path with spaces | pass | `INSTALLFOLDER="C:\FLACie_run\D\Path With Spaces\FLACie App"`: installs, app and server start from it, shortcut target correct, uninstall removes everything except the WebView2 folder. |
| 18 | Sign in (real Jellyfin Quick Connect) | blocked (needs owner approval) | Not attempted. |
| 19 | Sign in + browse + play against MOCK Jellyfin | pass for app mechanics (MOCK only) | `mock-jellyfin.mjs` on 8196 (test account `tester`/`pw`, from the repo's own fixtures), typed into the installed app's login page: `302 Location: /`, cookie `flacie` httponly. `/`, `/explore`, `/search`, `/headphones`, `/settings`, `/playlists`, album page all 200. Playing a track in the page requested `/stream?p=http://127.0.0.1:8196/Audio/s1/stream` -> 206/200 through the installed server. Mock audio is fake bytes so no real sound; this is the mock, not Jellyfin. |
| 20 | Final uninstall and snapshot diff | pass | Before/after snapshots identical for uninstall keys, Get-Package, services, tasks, Run keys, startup folder, firewall, listening ports, processes, FLACie/ipodsync folders and registry keys, Installer database entries (see diff below). |
| 21 | Licences / notices shipped | fail | Only `AboutAssets.txt` (a MAUI template text) is shipped; no `LICENSE`, `THIRD-PARTY-NOTICES.md` or `licenses\*.txt` although SMBLibrary (LGPL-3.0) and TagLibSharp (LGPL-2.1) DLLs are in the tree (BUG 4). |
| 22 | Private strings in shipped tree | pass with notes | Grepped 987 files (ASCII and UTF-16) for `ts.net`, `tailb`, `192.168.`, `100.x.x.x`, `apikey`, `token=`, `password=`, `C:\Users`, `frank`: no hostnames, IP addresses, tokens or keys. `apikey`/`token=`/`password` hits are .NET framework or FLACie code identifiers only (e.g. `, Password = ` in a string builder). Notes: 5 `.pdb` files ship and DLLs embed `C:\ipodemu\ipodsync\...\obj\...pdb` build paths; app id `dev.ashley.ipodsync` (owner's first name; the repo is public anyway). appsettings/web.config contain nothing private. |
| 23 | SmartScreen | note | MSI and the `.exe` are unsigned (`Get-AuthenticodeSignature: NotSigned`; SECURITY.md already says so). Expect "Windows protected your PC / Unknown publisher" (More info -> Run anyway) for the downloaded MSI; no reputation. Silent `/qn` is not affected. |

## BUG list

### BUG 1 (medium): WebView2 profile is written inside the install folder and survives uninstall
- Repro: install, launch the app once, close, `msiexec /x`. `%LOCALAPPDATA%\Programs\FLACie\IpodSync.Maui.exe.WebView2\EBWebView` (225 files, ~14 MB: cookies, local storage, caches, Crashpad) stays; the install folder is never removed. It also holds the player's sign-in cookie and the `flacie` session, so "uninstall" does not sign out.
- Cause: nothing sets `WEBVIEW2_USER_DATA_FOLDER`/`CoreWebView2CreationProperties.UserDataFolder` (grep finds none), so WebView2 defaults to `<exe>.WebView2` next to the exe. The WiX `RemoveFolder` only removes empty directories and the profile is not an installer-tracked file.
- Fix idea: set the user data folder to `%LOCALAPPDATA%\FLACie\webview2` (the same place as `FLACie\web`), or add a `RemoveFile`/custom action for `IpodSync.Maui.exe.WebView2` on uninstall. Also avoids the install dir being written to (breaks if ever installed per-machine).

### BUG 2 (medium): install, upgrade and uninstall do not close the running app/server
- Repro (upgrade): start the app, `msiexec /i FLACie-0.49.0...msi /qn`. Exit 0, but log shows `RESTART MANAGER: Did detect that a critical application holds file[s] in use, so a reboot will be necessary` and 255 `Scheduling reboot operation: Deleting file C:\Config.Msi\*.rbf` entries; app and server keep running the old 0.48 code until restarted. Repro (uninstall): same with `/x`: exit 0, "Removal completed successfully", app window and `FLACie.Server.exe` keep running and serving `127.0.0.1:5287` from an uninstalled tree; 254 more pending deletes. After the two runs `PendingFileRenameOperations` held 511 entries (all rollback files).
- Cause: `ipodsync.wxs` has no `<util:CloseApplication>` / Restart Manager configuration (no `WixToolset.Util.wixext`), no `MSIRESTARTMANAGERCONTROL`/`MSIFASTINSTALL` handling, and `FLACie.Server.exe` (child) is not a registered app for RM.
- Fix idea: add `util:CloseApplication Target="IpodSync.Maui.exe"` and `FLACie.Server.exe` (with `CloseMessage`/`EndSessionMessage`, `RebootPrompt="no"`), schedule before `InstallValidate`; or at least `<Property Id="MSIRESTARTMANAGERCONTROL" Value="Disable"/>` is not recommended, prefer CloseApplication.

### BUG 3 (low/medium): no single-instance guard
- Repro: start `IpodSync.Maui.exe` twice. Two windows, two `FLACie.Server.exe` (5287 and 5288) sharing the same `FLACIE_DATA` (`%LOCALAPPDATA%\FLACie\web`: library cache, users json, keys). The Start menu double-click or a stale shortcut will do this easily.
- Cause: `FlacieHost.StartAsync` only guards within one process (`gate`, `process`); `FreePort(5287)` then picks the next free port. No named mutex / `AppInstance` redirection (grep for Mutex/SingleInstance: none).
- Fix idea: named mutex (or `AppInstance.FindOrRegisterForKey`) and bring the first window to the front.

### BUG 4 (medium, licence compliance): no licence texts or third-party notices in the MSI
- Repro: list the installed tree: only `AboutAssets.txt`. `SMBLibrary.dll` (LGPL-3.0), `TagLibSharp.dll` (LGPL-2.1) and the rest ship without `LICENSE`, `THIRD-PARTY-NOTICES.md`, `licenses\*`. THIRD-PARTY-NOTICES.md itself says LGPL texts are needed.
- Cause: `build-installer.ps1` publishes the app and server only; `ipodsync.wxs` includes `$(PublishDir)\**` only and never copies the repo-root files.
- Fix idea: copy `LICENSE`, `THIRD-PARTY-NOTICES.md` and `licenses\` into `$publish` before `wix build` (and into `flacie-web` is not needed).

### Notes (not bugs)
- N1: Unused `.pdb` files (5, ~0.8 MB) and `workloads*.json`, `createdump.exe` are shipped; harmless, trims size/paths if wanted.
- N2: Settings of the iPod-manager side go to `%LOCALAPPDATA%\User Name\dev.ashley.ipodsync` (literal "User Name" is MAUI's placeholder publisher for unpackaged apps); consider setting a real publisher/folder.
- N3: Server CSP is `frame-ancestors 'self'` only; fine for a loopback-only server but worth a real CSP if it is ever exposed.
- N4: The ARP entry has no `InstallLocation`, help or about URL (cosmetic). `UninstallString` is `MsiExec.exe /I{ProductCode}` (Windows default for MSI), which opens the repair/remove dialog, not a direct uninstall.
- N5: `FLACIE_ALLOW_NAS_LOGIN=true` is set by the host; the NAS tab is visible on the login page of the desktop app (intended, per source).

## Leftover-files diff (before vs after the whole run)

Snapshots (`before` vs `after`) of: uninstall keys (HKCU/HKLM/WOW6432), `Get-Package`, services, scheduled tasks, Run/RunOnce keys, Startup folder, firewall rules, listening TCP ports, processes, files/folders matching flacie|ipodplayer|ipodsync under LocalAppData/AppData/ProgramData/Program Files/Documents/Start Menu/Desktop (depth 3), registry keys under HKCU/HKLM\Software, and Installer database entries: **0 differences in all 13 files**.

Residue that is NOT in the diff because it is not name-matched or I could not remove it:
- Windows `PendingFileRenameOperations` (HKLM, needs admin to edit) now holds about 509 `C:\Config.Msi\*.rbf` deletions from the running-app upgrade and uninstall (BUG 2); cleared by the next reboot.
- `%LOCALAPPDATA%\User Name\dev.ashley.ipodsync\Settings\preferences.dat` timestamp updated by the app (folder pre-existed).
- Test data I created in `%LOCALAPPDATA%\FLACie\web` (marker file, mock-signin `users`, `library`, `art`, `downloads`) was deleted again; the pre-existing `keys` and `device-id` are untouched.
- The WebView2 profile left in the install folder after the last uninstall (BUG 1) was deleted by hand at the end. `C:\FLACie_dl48` (0.48.0 MSI, beta48 APK) kept as allowed.

End state: FLACie not installed, no FLACie processes, 5287-5289 free, mock Jellyfin stopped, `C:\FLACie_run\D` removed.
