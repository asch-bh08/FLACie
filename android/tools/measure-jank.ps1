<#
.SYNOPSIS
  Repeatable scroll-jank measurement for FLACie on a connected Android device (adb + dumpsys gfxinfo).

.DESCRIPTION
  Resets the app's frame statistics, performs N scripted vertical swipes on whatever screen is currently showing
  (a list, Home, ...), then prints frames, janky %, frame-time percentiles and the slow-frame counters.
  The swipes stay in the middle of the screen and keep clear of the bottom strip (mini player and tab bar) and of the
  alphabet rail on the right edge, so they scroll the list instead of tapping a control.

  Procedure for a before/after comparison on the same device:
    1. Install the build, open FLACie, go to the screen to measure (for example Explore > Songs) and let it settle.
    2. .\measure-jank.ps1 -Device <serial> -Label before -Meminfo
    3. Install the other build, open the same screen, repeat with -Label after.
  Baseline (beta 49, RG Rotate, Explore > Songs, 10,036 songs, 12 swipes): 463 frames, 22 janky (4.75 %), p50 17 ms,
  p90 18 ms, p95 21 ms, p99 38 ms, 19 slow UI thread, 5 missed vsync.

  Do it a few times (-Runs 3) and compare medians: one run is noisy because the first scroll after launch also decodes covers.

.PARAMETER Device       adb serial (adb devices). Optional when only one device is connected.
.PARAMETER Swipes       Number of swipes per run (default 12: two thirds forward, the rest back so the list never runs out).
.PARAMETER Runs         Repeat the whole measurement this many times (default 1).
.PARAMETER Package      Application id (default com.ipodemu).
.PARAMETER BottomReserve Fraction of the screen height kept free at the bottom (mini player + tab bar). Default 0.22.
.PARAMETER SwipeMs      Duration of one swipe (default 180 ms: a brisk flick, same as tools/jank.sh).
.PARAMETER PauseMs      Pause between swipes (default 250 ms).
.PARAMETER Meminfo      Also print dumpsys meminfo totals (PSS, Java heap, Native heap, Graphics) after the run.
.PARAMETER Launch       Force-stop and relaunch the app first (cold start) instead of measuring the current screen.
.PARAMETER Label        Free text printed in the header and the result line.
.PARAMETER Adb          Path to adb (default: adb from PATH).
#>
param(
    [string]$Device = "",
    [int]$Swipes = 12,
    [int]$Runs = 1,
    [string]$Package = "com.ipodemu",
    [double]$BottomReserve = 0.22,
    [int]$SwipeMs = 180,
    [int]$PauseMs = 250,
    [switch]$Meminfo,
    [switch]$Launch,
    [string]$Label = "run",
    [string]$Adb = "adb"
)

$ErrorActionPreference = "Stop"

function Invoke-Adb {
    param([string[]]$ArgList)
    $full = @()
    if ($Device -ne "") { $full += @("-s", $Device) }
    $full += $ArgList
    $out = & $Adb @full 2>&1
    return ($out | ForEach-Object { "$_" })
}

# ---- device ------------------------------------------------------------------------------------------------------
$devices = & $Adb devices | Select-Object -Skip 1 | Where-Object { $_ -match "\sdevice$" }
if (-not $devices) { throw "No adb device connected." }
if ($Device -eq "" -and @($devices).Count -gt 1) { throw "More than one device connected: pass -Device <serial>.`n$($devices -join "`n")" }

$size = (Invoke-Adb @("shell", "wm", "size") | Where-Object { $_ -match "(\d+)x(\d+)" } | Select-Object -Last 1)
if ($size -notmatch "(\d+)x(\d+)") { throw "Could not read the screen size ($size)." }
$w = [int]$Matches[1]; $h = [int]$Matches[2]
$density = (Invoke-Adb @("shell", "wm", "density") | Where-Object { $_ -match "(\d+)" } | Select-Object -Last 1)
$model = (Invoke-Adb @("shell", "getprop", "ro.product.model") | Select-Object -First 1)
$build = (Invoke-Adb @("shell", "dumpsys", "package", $Package) | Where-Object { $_ -match "versionName=" } | Select-Object -First 1)

# portrait or square: swipe x in the middle; keep clear of the right-hand letter rail (32 dp) and the bottom strip
$x = [int]($w * 0.45)
$yTop = [int]($h * 0.18)
$yBottom = [int]($h * (1.0 - $BottomReserve) - $h * 0.04)
if ($yBottom -le $yTop + 100) { throw "Screen too small for the swipe area ($yTop..$yBottom)." }

Write-Host "== $Label  device=$model ${w}x$h  $($density.Trim())  $($build.Trim())  swipes=$Swipes x $Runs  area y $yBottom -> $yTop"

function Parse-Gfx([string[]]$lines) {
    $r = [ordered]@{}
    foreach ($l in $lines) {
        if ($l -match "Total frames rendered:\s*(\d+)") { $r.Frames = [int]$Matches[1] }
        elseif ($l -match "Janky frames:\s*(\d+)\s*\(([\d.]+)%\)") { $r.Janky = [int]$Matches[1]; $r.JankyPct = [double]$Matches[2] }
        elseif ($l -match "50th percentile:\s*(\d+)ms") { $r.P50 = [int]$Matches[1] }
        elseif ($l -match "90th percentile:\s*(\d+)ms") { $r.P90 = [int]$Matches[1] }
        elseif ($l -match "95th percentile:\s*(\d+)ms") { $r.P95 = [int]$Matches[1] }
        elseif ($l -match "99th percentile:\s*(\d+)ms") { $r.P99 = [int]$Matches[1] }
        elseif ($l -match "Number Missed Vsync:\s*(\d+)") { $r.MissedVsync = [int]$Matches[1] }
        elseif ($l -match "Number High input latency:\s*(\d+)") { $r.HighInputLatency = [int]$Matches[1] }
        elseif ($l -match "Number Slow UI thread:\s*(\d+)") { $r.SlowUiThread = [int]$Matches[1] }
        elseif ($l -match "Number Slow bitmap uploads:\s*(\d+)") { $r.SlowBitmapUploads = [int]$Matches[1] }
        elseif ($l -match "Number Slow issue draw commands:\s*(\d+)") { $r.SlowDrawCommands = [int]$Matches[1] }
        elseif ($l -match "Number Frame deadline missed:\s*(\d+)") { $r.DeadlineMissed = [int]$Matches[1] }
    }
    return [pscustomobject]$r
}

if ($Launch) {
    Invoke-Adb @("shell", "am", "force-stop", $Package) | Out-Null
    Invoke-Adb @("shell", "monkey", "-p", $Package, "-c", "android.intent.category.LAUNCHER", "1") | Out-Null
    Start-Sleep -Seconds 6
    Write-Host "(launched; navigate to the screen to measure, then press Enter within the next 20 s -- or just have it ready)"
    Start-Sleep -Seconds 14
}

$results = @()
for ($run = 1; $run -le $Runs; $run++) {
    Invoke-Adb @("shell", "dumpsys", "gfxinfo", $Package, "reset") | Out-Null
    $forward = [math]::Ceiling($Swipes * 2 / 3)
    for ($i = 1; $i -le $Swipes; $i++) {
        if ($i -le $forward) { $y1 = $yBottom; $y2 = $yTop } else { $y1 = $yTop; $y2 = $yBottom }   # finger up = list moves forward
        Invoke-Adb @("shell", "input", "swipe", "$x", "$y1", "$x", "$y2", "$SwipeMs") | Out-Null
        Start-Sleep -Milliseconds $PauseMs
    }
    Start-Sleep -Milliseconds 800   # let the last fling finish so its frames are counted
    $gfx = Invoke-Adb @("shell", "dumpsys", "gfxinfo", $Package)
    $r = Parse-Gfx $gfx
    $r | Add-Member -NotePropertyName Run -NotePropertyValue $run
    $results += $r
    Write-Host ("run {0}: frames={1} janky={2} ({3}%) p50={4}ms p90={5}ms p95={6}ms p99={7}ms slowUI={8} missedVsync={9} highInputLatency={10} slowBitmapUploads={11} slowDraw={12} deadlineMissed={13}" -f `
        $run, $r.Frames, $r.Janky, $r.JankyPct, $r.P50, $r.P90, $r.P95, $r.P99, $r.SlowUiThread, $r.MissedVsync, $r.HighInputLatency, $r.SlowBitmapUploads, $r.SlowDrawCommands, $r.DeadlineMissed)
}

if ($Runs -gt 1) {
    function Median($v) { $s = @($v | Sort-Object); if ($s.Count -eq 0) { return 0 }; return $s[[int][math]::Floor(($s.Count - 1) / 2)] }
    Write-Host ("median of {0}: janky% {1}  p90 {2}ms  p95 {3}ms  p99 {4}ms  slowUI {5}  missedVsync {6}" -f $Runs, `
        (Median ($results | ForEach-Object { $_.JankyPct })), (Median ($results | ForEach-Object { $_.P90 })), (Median ($results | ForEach-Object { $_.P95 })), `
        (Median ($results | ForEach-Object { $_.P99 })), (Median ($results | ForEach-Object { $_.SlowUiThread })), (Median ($results | ForEach-Object { $_.MissedVsync })))
}

if ($Meminfo) {
    $mem = Invoke-Adb @("shell", "dumpsys", "meminfo", $Package)
    Write-Host "-- meminfo (KB)"
    $mem | Where-Object { $_ -match "^\s*(Java Heap|Native Heap|Graphics|TOTAL PSS|TOTAL:|TOTAL\s|Views:|Activities:|Total Allocations)" -or $_ -match "TOTAL PSS" } | ForEach-Object { Write-Host $_ }
}
