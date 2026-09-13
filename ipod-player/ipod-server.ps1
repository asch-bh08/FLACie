<#
  iPod Player - local helper server
  Auto-detects a connected iPod (or a folder you pass) and serves it to ipod-player.html
  so the app opens with your library already loaded. No install required.

  Usage:
    Double-click "Start iPod Player.cmd", or run:
      powershell -ExecutionPolicy Bypass -File ipod-server.ps1
      powershell -ExecutionPolicy Bypass -File ipod-server.ps1 -Root "C:\path\to\iTunes_Control_parent"
#>
param([string]$Root = "")

$ErrorActionPreference = "Stop"
function Show-Msg($text){ try{ (New-Object -ComObject WScript.Shell).Popup($text,0,"iPod Player",0x30) | Out-Null }catch{} }
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$htmlPath  = Join-Path $scriptDir "ipod-player.html"

# Locate the verified ipodsync editor engine (built CLI). Editing routes through
# this, never through JS -- see EDIT-PROTOCOL.md. Absent = the player stays read-only.
$cliExe = $null
foreach ($c in @(
  (Join-Path $scriptDir 'ipodsync\src\IpodSync.Cli\bin\Release\net9.0\IpodSync.Cli.exe'),
  (Join-Path $scriptDir 'ipodsync\src\IpodSync.Cli\bin\Debug\net9.0\IpodSync.Cli.exe'),
  (Join-Path $scriptDir '..\src\IpodSync.Cli\bin\Release\net9.0\IpodSync.Cli.exe'),
  (Join-Path $scriptDir '..\src\IpodSync.Cli\bin\Debug\net9.0\IpodSync.Cli.exe')
)) { if (Test-Path $c) { $cliExe = [System.IO.Path]::GetFullPath($c); break } }
if ($cliExe) { Write-Host "  Editor engine found: $cliExe" -ForegroundColor Green }
else { Write-Host "  Editor engine not built - the player stays read-only (build ipodsync to enable editing)." -ForegroundColor Yellow }

function Find-iPod {
  foreach ($d in (Get-CimInstance Win32_LogicalDisk)) {
    $root = $d.DeviceID + "\"
    if ( (Test-Path (Join-Path $root 'iPod_Control')) -or (Test-Path (Join-Path $root 'iTunes_Control')) ) {
      return [pscustomobject]@{ Path=$root; Label=($d.VolumeName); Fs=$d.FileSystem }
    }
  }
  return $null
}

# Resolve which folder to serve
if ($Root -ne "") {
  if (-not (Test-Path $Root)) { Show-Msg "Folder not found: $Root"; exit 1 }
  $ipodRoot = (Resolve-Path $Root).Path
  $label = Split-Path $ipodRoot -Leaf
} else {
  $found = Find-iPod
  if ($null -eq $found) {
    Write-Host ""
    Write-Host "  No iPod detected." -ForegroundColor Yellow
    Write-Host "  Plug in a click-wheel iPod (it should appear as a drive), then run this again."
    Write-Host "  For an iPod Touch: copy its iTunes_Control folder to the PC first, then run:"
    Write-Host "     powershell -ExecutionPolicy Bypass -File ipod-server.ps1 -Root `"C:\that\folder`""
    Write-Host ""
    Show-Msg "No iPod detected.`r`n`r`nPlug in a click-wheel iPod (it should show up as a drive), then run iPod Player again.`r`n`r`nFor an iPod Touch, copy its iTunes_Control folder to the PC first, then drop that folder onto iPod Player."
    exit 1
  }
  $ipodRoot = $found.Path
  $label = if ($found.Label) { $found.Label } else { "iPod" }
  Write-Host "  Found iPod: $($found.Path) '$label' ($($found.Fs))" -ForegroundColor Green
}
$controlFolder = if (Test-Path (Join-Path $ipodRoot 'iTunes_Control')) { 'iTunes_Control' } else { 'iPod_Control' }
$ipodRootFull = [System.IO.Path]::GetFullPath($ipodRoot)

# ffmpeg enables on-the-fly transcoding of Apple Lossless (ALAC), which browsers can't decode
$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
$xcodeDir = Join-Path $env:TEMP "ipod-xcode"
if (-not (Test-Path $xcodeDir)) { New-Item -ItemType Directory -Path $xcodeDir | Out-Null }
if ($ffmpeg) { Write-Host "  ffmpeg found - Apple Lossless tracks will be transcoded to FLAC (lossless) on demand." -ForegroundColor Green }
else { Write-Host "  ffmpeg NOT found - Apple Lossless (ALAC) tracks will not play. Install with: winget install Gyan.FFmpeg" -ForegroundColor Yellow }

# Pick a free localhost port
$listener = New-Object System.Net.HttpListener
$port = 0
foreach ($p in 8721,8722,8723,8730,8750,8781) {
  try { $listener.Prefixes.Clear(); $listener.Prefixes.Add("http://localhost:$p/"); $listener.Start(); $port=$p; break }
  catch { }
}
if ($port -eq 0) { Show-Msg "Could not open a local port for the player. Try closing other copies of iPod Player and run it again."; exit 1 }
$url = "http://localhost:$port/"

function Get-ContentType([string]$path) {
  switch ([System.IO.Path]::GetExtension($path).ToLower()) {
    ".m4a" {"audio/mp4"} ".m4b" {"audio/mp4"} ".aac" {"audio/aac"} ".flac" {"audio/flac"}
    ".mp3" {"audio/mpeg"} ".wav" {"audio/wav"} ".aif" {"audio/aiff"} ".aiff" {"audio/aiff"}
    ".html" {"text/html; charset=utf-8"} ".json" {"application/json"}
    default {"application/octet-stream"}
  }
}

function Serve-FileWithRange($req,$res,$full){
  $fi = Get-Item $full -Force
  $total = $fi.Length
  $res.ContentType = Get-ContentType $full
  $range = $req.Headers["Range"]
  $start = 0; $end = $total - 1
  if ($range -and $range -match "bytes=(\d*)-(\d*)") {
    if ($matches[1] -ne "") { $start = [int64]$matches[1] }
    if ($matches[2] -ne "") { $end   = [int64]$matches[2] }
    if ($end -ge $total) { $end = $total - 1 }
    $res.StatusCode = 206
    $res.Headers.Add("Content-Range","bytes $start-$end/$total")
  } else { $res.StatusCode = 200 }
  $len = $end - $start + 1
  $res.ContentLength64 = $len
  $fs = [System.IO.File]::Open($full,[System.IO.FileMode]::Open,[System.IO.FileAccess]::Read,[System.IO.FileShare]::ReadWrite)
  try {
    $fs.Seek($start,[System.IO.SeekOrigin]::Begin) | Out-Null
    $buf = New-Object byte[] 262144
    $remaining = $len
    while ($remaining -gt 0) {
      $toRead = [Math]::Min($buf.Length, $remaining)
      $read = $fs.Read($buf,0,$toRead)
      if ($read -le 0) { break }
      $res.OutputStream.Write($buf,0,$read)
      $remaining -= $read
    }
  } finally { $fs.Close() }
}

Write-Host ""
Write-Host "  iPod Player is running." -ForegroundColor Cyan
Write-Host "  Serving: $ipodRootFull"
Write-Host "  Open:    $url"
Write-Host "  (Leave this window open while you listen. Close it to stop.)"
Write-Host ""
# ---- open as a borderless app window if a Chromium browser is available ----
$profileDir = Join-Path $env:TEMP "ipod-app-profile"
$browserExe = $null
foreach ($c in @(
  "$env:ProgramFiles\BraveSoftware\Brave-Browser\Application\brave.exe",
  "${env:ProgramFiles(x86)}\BraveSoftware\Brave-Browser\Application\brave.exe",
  "$env:LOCALAPPDATA\BraveSoftware\Brave-Browser\Application\brave.exe",
  "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
  "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
  "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
  "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
  "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe")) { if (Test-Path $c) { $browserExe=$c; break } }
$browser = $null
if ($browserExe) {
  $browser = Start-Process $browserExe -PassThru -ArgumentList `
    "--app=$url","--user-data-dir=$profileDir","--no-first-run","--no-default-browser-check","--window-size=1200,820"
} else {
  Start-Process $url
}

# ---- request loop (also watches the app window; quits when it closes) ----
$ar = $listener.BeginGetContext($null,$null)
while ($listener.IsListening) {
  if (-not $ar.AsyncWaitHandle.WaitOne(400)) {
    if ($browser -and $browser.HasExited) { break }
    continue
  }
  $ctx = $listener.EndGetContext($ar)
  $ar = $listener.BeginGetContext($null,$null)
  $req = $ctx.Request
  $res = $ctx.Response
  try {
    $res.Headers.Add("Access-Control-Allow-Origin","*")
    $res.Headers.Add("Accept-Ranges","bytes")
    $path = [System.Uri]::UnescapeDataString($req.Url.AbsolutePath)

    if ($req.HttpMethod -eq "OPTIONS") {
      $res.Headers.Add("Access-Control-Allow-Methods","GET,POST,OPTIONS")
      $res.Headers.Add("Access-Control-Allow-Headers","Range,Content-Type")
      $res.StatusCode = 204; $res.Close(); continue
    }

    if ($path -eq "/" -or $path -eq "/index.html") {
      $bytes = [System.IO.File]::ReadAllBytes($htmlPath)
      $res.ContentType = "text/html; charset=utf-8"
      $res.ContentLength64 = $bytes.Length
      $res.OutputStream.Write($bytes,0,$bytes.Length); $res.Close(); continue
    }

    if ($path -eq "/api/info") {
      $ff = if ($ffmpeg) { "true" } else { "false" }
      $ed = if ($cliExe) { "true" } else { "false" }
      $json = "{""label"":""$($label -replace '"','\"')"",""sub"":""$($controlFolder)"",""control"":""$controlFolder"",""ffmpeg"":$ff,""editor"":$ed}"
      $b=[System.Text.Encoding]::UTF8.GetBytes($json)
      $res.ContentType="application/json"; $res.ContentLength64=$b.Length
      $res.OutputStream.Write($b,0,$b.Length); $res.Close(); continue
    }

    # Receive an audio file uploaded from the player (browsers can't hand us a real
    # path). Saved to a temp dir; the returned path is used as an addTrackFromFile source.
    if ($path -eq "/api/upload" -and $req.HttpMethod -eq "POST") {
      $name = [System.IO.Path]::GetFileName([System.Uri]::UnescapeDataString(($req.QueryString["name"] + "")))
      if ([string]::IsNullOrWhiteSpace($name)) { $name = "upload.bin" }
      $updir = Join-Path $env:TEMP "ipod-uploads"
      if (-not (Test-Path $updir)) { New-Item -ItemType Directory -Path $updir | Out-Null }
      $ms = New-Object System.IO.MemoryStream
      $req.InputStream.CopyTo($ms)
      $dest = Join-Path $updir ([Guid]::NewGuid().ToString("N") + "-" + $name)
      [System.IO.File]::WriteAllBytes($dest, $ms.ToArray())
      $payload = @{ ok = $true; path = $dest; name = $name } | ConvertTo-Json -Compress
      $b=[Text.Encoding]::UTF8.GetBytes($payload)
      $res.ContentType="application/json"; $res.ContentLength64=$b.Length; $res.OutputStream.Write($b,0,$b.Length); $res.Close(); continue
    }

    # Apply a JSON change-set from the player through the verified engine.
    # Dry-run by default (writes nothing); ?commit=1 performs the real, backed-up write.
    if ($path -eq "/api/apply-edits" -and $req.HttpMethod -eq "POST") {
      $reader = New-Object System.IO.StreamReader($req.InputStream, [System.Text.Encoding]::UTF8)
      $body = $reader.ReadToEnd(); $reader.Close()
      $commit = ($req.Url.Query -match "commit=1")
      if (-not $cliExe) {
        $b=[Text.Encoding]::UTF8.GetBytes('{"ok":false,"error":"Editor engine not built. Build ipodsync (dotnet build src/IpodSync.Cli) to enable editing."}')
        $res.ContentType="application/json"; $res.ContentLength64=$b.Length; $res.OutputStream.Write($b,0,$b.Length); $res.Close(); continue
      }
      $tmp = Join-Path $env:TEMP ("ipod-edits-" + [Guid]::NewGuid().ToString("N") + ".json")
      [System.IO.File]::WriteAllText($tmp, $body, [System.Text.Encoding]::UTF8)
      $cliArgs = @("apply-edits", $ipodRootFull, "--changes", $tmp)
      if ($commit) { $cliArgs += "--yes" }
      $out = ""
      try { $out = (& $cliExe @cliArgs 2>&1 | Out-String) } catch { $out = "error running editor: $_" }
      $exit = $LASTEXITCODE
      Remove-Item -Force $tmp -ErrorAction SilentlyContinue
      $payload = @{ ok = ($exit -eq 0); commit = [bool]$commit; exit = $exit; output = $out } | ConvertTo-Json -Compress
      $b=[Text.Encoding]::UTF8.GetBytes($payload)
      $res.ContentType="application/json"; $res.ContentLength64=$b.Length; $res.OutputStream.Write($b,0,$b.Length); $res.Close(); continue
    }

    if ($path.StartsWith("/ipod/")) {
      $rel = $path.Substring(6) -replace '/','\'
      $full = [System.IO.Path]::GetFullPath((Join-Path $ipodRootFull $rel))
      if (-not $full.StartsWith($ipodRootFull, [StringComparison]::OrdinalIgnoreCase)) { $res.StatusCode=403; $res.Close(); continue }
      if (-not (Test-Path $full -PathType Leaf)) { $res.StatusCode=404; $res.Close(); continue }
      Serve-FileWithRange $req $res $full
      $res.Close(); continue
    }

    # /x/<relpath> : transcode Apple Lossless (or anything) to AAC, cache, then serve seekable
    if ($path.StartsWith("/x/")) {
      if (-not $ffmpeg) { $res.StatusCode=501; $res.Close(); continue }
      $rel = $path.Substring(3) -replace '/','\'
      $src = [System.IO.Path]::GetFullPath((Join-Path $ipodRootFull $rel))
      if (-not $src.StartsWith($ipodRootFull, [StringComparison]::OrdinalIgnoreCase)) { $res.StatusCode=403; $res.Close(); continue }
      if (-not (Test-Path $src -PathType Leaf)) { $res.StatusCode=404; $res.Close(); continue }
      $md5 = [System.Security.Cryptography.MD5]::Create()
      $hash = [System.BitConverter]::ToString($md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($src.ToLower()))).Replace("-","")
      $cache = Join-Path $xcodeDir ($hash + ".flac")
      if (-not (Test-Path $cache) -or (Get-Item $cache).Length -eq 0) {
        $tmp = $cache + ".part"
        & $ffmpeg -y -loglevel error -i $src -c:a flac -compression_level 5 -f flac $tmp 2>$null
        if ((Test-Path $tmp) -and (Get-Item $tmp).Length -gt 0) { Move-Item -Force $tmp $cache }
        else { if (Test-Path $tmp) { Remove-Item -Force $tmp }; $res.StatusCode=500; $res.Close(); continue }
      }
      Serve-FileWithRange $req $res $cache
      $res.Close(); continue
    }

    $res.StatusCode = 404; $res.Close()
  } catch {
    try { $res.StatusCode = 500; $res.Close() } catch {}
  }
  if ($browser -and $browser.HasExited) { break }
}
try { $listener.Stop(); $listener.Close() } catch {}
