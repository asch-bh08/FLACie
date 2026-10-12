# Unattended Android load measurement of FLACie against the 10,000-song mock Jellyfin, on the headless emulator.
#
#   powershell -NoProfile -File run-android.ps1 -Apk <path.apk> -Label before|after -Profile fast|slow|flaky|ratelimit
#           [-Window 120] [-Port 8296] [-Items 10000] [-Out <result.json>] [-Scenarios first-sync,scrolling,skip-burst,warm-start,kill-restore]
#
# One run = boot the AVD headless -> install the APK -> fresh mock (own process) + `pm clear` -> scenarios (run-android.mjs) -> emulator killed, mock killed.
# It boots the emulator, so run it under the machine-wide lock:
#   powershell -NoProfile -File C:\FLACie-handoff\heavy.ps1 -Dir <this dir> -Cmd "powershell -NoProfile -File run-android.ps1 -Apk ... -Label before -Profile fast"
# (run-android-matrix.ps1 does that for all four profiles.)
#
# -HoldOnly   boot + install, then wait until the file given by -StopFile exists (or -HoldMinutes pass), then tear down: a driver-development session.
# -Reuse      do not boot/kill: use the emulator that is already online (still needs the lock if anybody else may start one).
param(
  [string]$Apk, [string]$Label = 'run', [string]$Profile = 'fast', [int]$Window = 120, [int]$Port = 8296, [int]$Items = 10000,
  [string]$Out = '', [string]$Scenarios = 'first-sync,scrolling,skip-burst,warm-start,kill-restore',
  [string]$Avd = 'flacie_phone', [int]$Memory = 2048, [switch]$HoldOnly, [int]$HoldMinutes = 14, [string]$StopFile = "$env:TEMP\flacie-android-stop",
  [switch]$Reuse, [string]$Shots = '', [int]$BootTimeoutSec = 420, [string]$Driver = 'run-android.mjs'
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { "$env:LOCALAPPDATA\Android\Sdk" }
$adb = "$sdk\platform-tools\adb.exe"
$emuExe = "$sdk\emulator\emulator.exe"
if (-not $Out) { $Out = Join-Path $here "result-android-$Label-$Profile.json" }
if (-not $Shots) { $Shots = "C:\wt\android-shots\$Label-$Profile" }
New-Item -ItemType Directory -Force $Shots | Out-Null
function Log($m) { Write-Host ("[run-android {0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $m) }
function Adb { & $adb @args }

$emu = $null
function Stop-Emulator {
  try { & $adb emu kill 2>$null | Out-Null } catch { }
  Start-Sleep -Seconds 3
  Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^(emulator|qemu-system-x86_64.*|emulator64-crash-service)$' } | ForEach-Object { try { $_.Kill() } catch { } }
}

try {
  if (-not $Reuse) {
    $online = (& $adb devices) -match 'emulator-\d+\s+device'
    if ($online) { throw 'an emulator is already online: another run owns it (use -Reuse to drive it anyway)' }
    Log "booting AVD $Avd headless (memory $Memory MB)"
    $emu = Start-Process -FilePath $emuExe -WindowStyle Hidden -PassThru -ArgumentList @('-avd', $Avd, '-no-window', '-no-audio', '-no-snapshot', '-no-boot-anim', '-gpu', 'swiftshader_indirect', '-memory', "$Memory", '-netfast')
  }
  & $adb wait-for-device
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($true) {
    $b = (& $adb shell getprop sys.boot_completed 2>$null) -join ''
    if ($b.Trim() -eq '1') { break }
    if ($sw.Elapsed.TotalSeconds -gt $BootTimeoutSec) { throw "emulator did not boot in $BootTimeoutSec s" }
    Start-Sleep -Seconds 3
  }
  Log ("booted in {0:n0} s" -f $sw.Elapsed.TotalSeconds)
  Start-Sleep -Seconds 5
  # quiet, awake, no animations (software rendering is slow; animations only add noise to the timings)
  foreach ($s in 'window_animation_scale 0', 'transition_animation_scale 0', 'animator_duration_scale 0', 'stay_on_while_plugged_in 7', 'screen_off_timeout 2147483647') {
    $kv = $s -split ' '; Adb shell settings put global $kv[0] $kv[1] | Out-Null
  }
  Adb shell settings put system screen_off_timeout 2147483647 | Out-Null
  Adb shell input keyevent KEYCODE_WAKEUP | Out-Null
  Adb shell wm dismiss-keyguard | Out-Null
  Adb shell svc power stayon true | Out-Null
  # the build under test, installed fresh (run-android.mjs grants the notification permission and keeps music-file access off,
  # so the emulator's own sdcard files never mix into the mock's 10,000 songs)
  if ($Apk) {
    Log "installing $Apk"
    Adb uninstall com.ipodemu 2>$null | Out-Null
    $r = Adb install -r $Apk
    Log ("install: " + ($r -join ' '))
    if (($r -join ' ') -notmatch 'Success') { throw "install failed" }
  }
  if ($HoldOnly) {
    Remove-Item $StopFile -ErrorAction SilentlyContinue
    Log "holding the emulator (up to $HoldMinutes min); create $StopFile to end"
    $end = (Get-Date).AddMinutes($HoldMinutes)
    while ((Get-Date) -lt $end -and -not (Test-Path $StopFile)) { Start-Sleep -Seconds 2 }
  } else {
    $node = (Get-Command node).Source
    $args2 = @((Join-Path $here $Driver), '--label', $Label, '--profile', $Profile, '--window', "$Window", '--port', "$Port", '--items', "$Items",
               '--out', $Out, '--scenarios', $Scenarios, '--adb', $adb, '--shots', $Shots)
    if ($Apk) { $args2 += @('--apk', (Resolve-Path $Apk).Path) }
    Log "driver: node $($args2 -join ' ')"
    # own process with its output in files (PowerShell 5.1 turns a native command's stderr into errors; the driver logs a lot to stderr)
    $qargs = ($args2 | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $o = Join-Path $Shots 'driver.out.txt'; $e = Join-Path $Shots 'driver.err.txt'
    $p = Start-Process -FilePath $node -ArgumentList $qargs -NoNewWindow -Wait -PassThru -RedirectStandardOutput $o -RedirectStandardError $e
    Get-Content $o, $e -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
    Log "driver exit code $($p.ExitCode)"
    $script:driverExit = $p.ExitCode
  }
}
finally {
  if (-not $Reuse) { Log 'shutting the emulator down'; Stop-Emulator }
  # the driver kills its mock itself; make sure nothing is left on the port
  try {
    Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue | ForEach-Object {
      $p = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue
      if ($p -and $p.Name -eq 'node') { Log "killing leftover node on port $Port"; $p.Kill() }
    }
  } catch { }
}
if ($script:driverExit) { exit $script:driverExit }
