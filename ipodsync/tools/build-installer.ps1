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
    -p:WindowsAppSDKSelfContained=true -p:SelfContained=true -p:DebugType=none -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw "Windows publish failed" }
# the FLACie music player (the FLACie Web server) goes beside the app; see FlacieHost.cs
& dotnet publish (Join-Path $repo "src\FLACie.Server\FLACie.Server.csproj") -c Release -r win-x64 --self-contained -p:DebugType=none -p:DebugSymbols=false -o (Join-Path $publish "flacie-web")
if ($LASTEXITCODE -ne 0) { throw "FLACie player publish failed" }

# no debug symbols / build-path leftovers in the shipped tree
Get-ChildItem $publish -Recurse -Filter *.pdb | Remove-Item -Force

# licence texts: SMBLibrary (LGPL-3.0) and TagLibSharp (LGPL-2.1) ship as separate DLLs, so their licences, the notices and
# FLACie's own LICENSE travel with them (installed beside the app, under INSTALLFOLDER)
$root = Split-Path -Parent $repo
foreach ($f in "LICENSE", "THIRD-PARTY-NOTICES.md") {
    $src = Join-Path $root $f
    if (-not (Test-Path $src)) { throw "missing $src (needed in the installer)" }
    Copy-Item $src (Join-Path $publish $f) -Force
}
$lic = Join-Path $root "licenses"
if (-not (Test-Path $lic)) { throw "missing $lic (needed in the installer)" }
Copy-Item $lic (Join-Path $publish "licenses") -Recurse -Force

Write-Host "== installer" -ForegroundColor Cyan
$installer = Join-Path $PSScriptRoot "installer"
Push-Location $installer
try {
  & dotnet tool restore | Out-Null
  # util extension: CloseApplication (closes the running app/server on upgrade and uninstall); cached per user
  & dotnet wix extension add -g WixToolset.Util.wixext/5.0.2 | Out-Null
  $msi = Join-Path $Out "FLACie-$version-windows-x64.msi"
  & dotnet wix build ipodsync.wxs -arch x64 -ext WixToolset.Util.wixext -d "Version=$version" -d "PublishDir=$publish" -o $msi
  if ($LASTEXITCODE -ne 0) { throw "wix build failed" }
} finally { Pop-Location }
Write-Host "   msi: $msi" -ForegroundColor Green
