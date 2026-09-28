<#
  Builds the Windows installer: ipodsync-<version>-windows-x64.msi (per-user, no admin needed).

    powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 [-Out <dir>]

  1. Self-contained Windows publish (same command as build-apps.ps1 -Windows).
  2. Packs it with WiX 5 (a repo-local dotnet tool: tools\installer\.config\dotnet-tools.json -- restored
     automatically, nothing installed machine-wide) using tools\installer\ipodsync.wxs.
  The MSI is unsigned, so Windows SmartScreen shows "unknown publisher" the first time.
  Needs the maui-windows workload (dotnet workload install maui-windows).
#>
param([string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) "dist"))
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "src\IpodSync.Maui\IpodSync.Maui.csproj"
$version = ([xml](Get-Content $project)).Project.PropertyGroup.ApplicationDisplayVersion | Where-Object { $_ } | Select-Object -First 1
$publish = Join-Path $Out "app-windows"
New-Item -ItemType Directory -Force $Out | Out-Null

Write-Host "== publish $version -> $publish" -ForegroundColor Cyan
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
& dotnet publish $project -f net9.0-windows10.0.19041.0 -c Release `
    -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true -p:SelfContained=true -o $publish
if ($LASTEXITCODE -ne 0) { throw "Windows publish failed" }

Write-Host "== installer" -ForegroundColor Cyan
$installer = Join-Path $PSScriptRoot "installer"
Push-Location $installer
try {
  & dotnet tool restore | Out-Null
  $msi = Join-Path $Out "ipodsync-$version-windows-x64.msi"
  & dotnet wix build ipodsync.wxs -arch x64 -d "Version=$version" -d "PublishDir=$publish" -o $msi
  if ($LASTEXITCODE -ne 0) { throw "wix build failed" }
} finally { Pop-Location }
Write-Host "   msi: $msi" -ForegroundColor Green
