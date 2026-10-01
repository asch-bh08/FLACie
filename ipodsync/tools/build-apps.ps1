<#
  Builds the ipodsync apps.

    powershell -ExecutionPolicy Bypass -File tools\build-apps.ps1 [-Windows] [-Android] [-Out <dir>]

  -Windows  self-contained Windows app (no .NET or Windows App SDK install needed) in <Out>\app-windows
  -Android  signed Release APK, copied to <Out>\ipodsync.apk
  (neither flag = both)

  Needs the MAUI workloads (dotnet workload install maui-windows maui-android). For Android
  also a JDK 17 and the Android SDK: set JAVA_HOME / ANDROID_HOME, or pass -Toolchain pointing
  at a folder with dotnet\, jdk\ and android-sdk\ inside (how this machine was set up, without
  admin rights, in %LOCALAPPDATA%\ipodsync-toolchain).

  Publishing with -r win-x64 fails for this multi-targeted project (restore tries to resolve an
  Android/Mono runtime pack for win-x64), and a global -p:TargetFrameworks=... breaks the referenced
  projects' restore. RuntimeIdentifierOverride is the documented MAUI way round both.
#>
param(
  [switch]$Windows,
  [switch]$Android,
  [string]$Out = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) ""),
  [string]$Toolchain = (Join-Path $env:LOCALAPPDATA "ipodsync-toolchain")
)
$ErrorActionPreference = "Stop"
if (-not $Windows -and -not $Android) { $Windows = $true; $Android = $true }
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "src\IpodSync.Maui\IpodSync.Maui.csproj"

$dotnet = "dotnet"
if (Test-Path (Join-Path $Toolchain "dotnet\dotnet.exe")) {
  $dotnet = Join-Path $Toolchain "dotnet\dotnet.exe"
  $env:DOTNET_ROOT = Join-Path $Toolchain "dotnet"
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

if ($Windows) {
  $dest = Join-Path $Out "app-windows"
  Write-Host "== Windows app -> $dest" -ForegroundColor Cyan
  & $dotnet publish $project -f net9.0-windows10.0.19041.0 -c Release `
      -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None `
      -p:WindowsAppSDKSelfContained=true -p:SelfContained=true -o $dest
  if ($LASTEXITCODE -ne 0) { throw "Windows publish failed" }
  # the FLACie music player: the same server the Docker image runs, packaged beside the app and shown in a WebView (see FlacieHost.cs)
  $server = Join-Path $repo "src\FLACie.Server\FLACie.Server.csproj"
  & $dotnet publish $server -c Release -r win-x64 --self-contained -o (Join-Path $dest "flacie-web")
  if ($LASTEXITCODE -ne 0) { throw "FLACie player publish failed" }
  Write-Host "   run: $dest\IpodSync.Maui.exe" -ForegroundColor Green
}

if ($Android) {
  Write-Host "== Android APK" -ForegroundColor Cyan
  $props = @()
  if (Test-Path (Join-Path $Toolchain "jdk")) { $props += "-p:JavaSdkDirectory=$(Join-Path $Toolchain 'jdk')"; $env:JAVA_HOME = Join-Path $Toolchain "jdk" }
  if (Test-Path (Join-Path $Toolchain "android-sdk")) { $props += "-p:AndroidSdkDirectory=$(Join-Path $Toolchain 'android-sdk')" }
  # One permanent signing key, so each new APK installs as an update. Kept only on this PC
  # (never committed): <Toolchain>\signing\ipodsync-release.keystore + keystore-password.txt.
  # A different key (e.g. the old per-machine debug key) makes Android say "App not installed".
  $keystore = Join-Path $Toolchain "signing\ipodsync-release.keystore"
  $pwFile = Join-Path $Toolchain "signing\keystore-password.txt"
  if (Test-Path $keystore) {
    $props += "-p:AndroidKeyStore=true", "-p:AndroidSigningKeyStore=$keystore", "-p:AndroidSigningKeyAlias=ipodsync",
              "-p:AndroidSigningStorePass=file:$pwFile", "-p:AndroidSigningKeyPass=file:$pwFile"
  } else { Write-Warning "no release keystore at $keystore; the APK gets this PC's debug key" }
  & $dotnet build $project -f net9.0-android -c Release -p:AcceptAndroidSDKLicenses=True @props
  if ($LASTEXITCODE -ne 0) { throw "Android build failed" }
  $apk = Get-ChildItem (Join-Path $repo "src\IpodSync.Maui\bin\Release\net9.0-android") -Filter "*-Signed.apk" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  Copy-Item $apk.FullName (Join-Path $Out "ipodsync.apk") -Force
  Write-Host "   apk: $(Join-Path $Out 'ipodsync.apk')" -ForegroundColor Green
}
