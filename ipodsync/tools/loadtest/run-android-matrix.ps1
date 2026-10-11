# Runs the Android load driver for one APK over the four server profiles; every profile (emulator boot -> scenarios -> teardown) takes the
# machine-wide heavy lock separately, so nothing else heavy runs meanwhile.
# Usage: powershell -NoProfile -File run-android-matrix.ps1 -Apk C:\wt\apk\FLACie-1.0-beta51.apk -Label before [-Window 120] [-Profiles fast,slow,flaky,ratelimit]
param([Parameter(Mandatory=$true)][string]$Apk, [Parameter(Mandatory=$true)][string]$Label, [int]$Window = 120,
      [string[]]$Profiles = @('fast','slow','flaky','ratelimit'), [string]$Out = 'C:\FLACie\docs\test-run\load-data', [int]$Port = 8296)
$ErrorActionPreference = 'Continue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
New-Item -ItemType Directory -Force $Out | Out-Null
foreach ($p in $Profiles) {
  $json = Join-Path $Out "android-$Label-$p.json"
  powershell -NoProfile -File C:\FLACie-handoff\heavy.ps1 -Dir $here -Cmd "powershell -NoProfile -File run-android.ps1 -Apk $Apk -Label $Label -Profile $p -Window $Window -Port $Port -Out $json 2>&1"
  Add-Content -Path 'C:\FLACie-handoff\android-matrix.log' -Value "$(Get-Date -Format s) android $Label $p exit $LASTEXITCODE"
}
Add-Content -Path 'C:\FLACie-handoff\android-matrix.log' -Value "$(Get-Date -Format s) android $Label MATRIX DONE"
