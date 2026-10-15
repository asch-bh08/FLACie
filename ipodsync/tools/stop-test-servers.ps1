# Stops what is LISTENING on the test ports of this checkout (the web server on 5290, the mock Jellyfin on 8196, the catalog stand-in on 8197) by process id, never by image name:
# several checkouts and sessions can run their own tests on the same machine (give each its own ports with MOCK_PORT / WEB_PORT), and `taskkill /IM dotnet.exe` would stop theirs too.
param([int[]]$Ports = @(5290, 8196, 8197))
foreach ($p in $Ports) {
  Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue | ForEach-Object {
    $id = $_.OwningProcess
    if ($id -gt 4) { try { Stop-Process -Id $id -Force -ErrorAction Stop; "stopped pid $id on port $p" } catch { "could not stop pid $id on port $p" } }
  }
}
