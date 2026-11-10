# How fast the Windows app's own FLACie Web starts: launch the server from an installer build folder (hidden, as the app does), time until
# /healthz answers, then the first page and its scripts (time and size as the WebView would get them, compressed). A throwaway data folder,
# so nothing of the owner's is touched. Runs it -Runs times and prints the middle value of each.
#   powershell -File win-start.ps1 -Dir C:/FLACie-handoff/msi-b125/app-windows/flacie-web [-Port 5297] [-Runs 3]
param([Parameter(Mandatory = $true)][string]$Dir, [int]$Port = 5297, [int]$Runs = 3)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$exe = Join-Path $Dir 'FLACie.Server.exe'
$base = "http://127.0.0.1:$Port"
$rows = @()
for ($r = 1; $r -le $Runs; $r++) {
  $data = Join-Path $env:TEMP ("flacie-winstart-" + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory $data | Out-Null
  $psi = New-Object System.Diagnostics.ProcessStartInfo $exe
  $psi.WorkingDirectory = $Dir; $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
  $psi.EnvironmentVariables['ASPNETCORE_URLS'] = $base
  $psi.EnvironmentVariables['FLACIE_DATA'] = $data
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $p = [Diagnostics.Process]::Start($psi)
  $handler = New-Object System.Net.Http.HttpClientHandler
  $handler.AutomaticDecompression = [System.Net.DecompressionMethods]::None
  $http = New-Object System.Net.Http.HttpClient($handler)
  $http.DefaultRequestHeaders.AcceptEncoding.ParseAdd('gzip, br')
  $ready = $null
  while ($sw.Elapsed.TotalSeconds -lt 60) {
    try { if ($http.GetAsync("$base/healthz").Result.IsSuccessStatusCode) { $ready = $sw.ElapsedMilliseconds; break } } catch { }
    Start-Sleep -Milliseconds 50
  }
  $t = [Diagnostics.Stopwatch]::StartNew()
  $page = $http.GetAsync("$base/login").Result
  $html = $page.Content.ReadAsStringAsync().Result
  $pageMs = $t.ElapsedMilliseconds
  # every script and stylesheet the page names, fetched one after another as bytes on the wire
  $assets = [regex]::Matches($html, '(?:src|href)="(/?[^"]+\.(?:js|css)(?:\?[^"]*)?)"') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
  $bytes = 0; $t.Restart()
  foreach ($a in $assets) { $u = if ($a.StartsWith('/')) { "$base$a" } else { "$base/$a" }; try { $bytes += $http.GetByteArrayAsync($u).Result.Length } catch { } }
  $assetMs = $t.ElapsedMilliseconds
  $p.Refresh(); $mem = [math]::Round($p.WorkingSet64 / 1MB)
  $rows += [pscustomobject]@{ readyMs = $ready; loginMs = $pageMs; assetsMs = $assetMs; assetsKB = [math]::Round($bytes / 1KB); files = @($assets).Count; memMB = $mem }
  Stop-Process $p.Id -Force; Start-Sleep -Milliseconds 500
  Remove-Item -Recurse -Force $data -ErrorAction SilentlyContinue
}
$rows | Format-Table -AutoSize | Out-String | Write-Host
$mid = { param($xs) ($xs | Sort-Object)[[math]::Floor(@($xs).Count / 2)] }
"middle: ready {0} ms, login page {1} ms, scripts+styles {2} ms / {3} KB in {4} files, memory {5} MB" -f (& $mid $rows.readyMs), (& $mid $rows.loginMs), (& $mid $rows.assetsMs), (& $mid $rows.assetsKB), $rows[0].files, (& $mid $rows.memMB)
