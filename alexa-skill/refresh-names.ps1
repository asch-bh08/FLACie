<#
Brings the skill's list of names up to date with the music library, so a newly added song is understood when its name is said on its own
("Alexa, ask flaky for <new song>"). Reads every account's library from the FLACie server over ssh, then runs deploy.ps1.

  .\refresh-names.ps1 [-Server root@frank] [-PublicUrl https://frank.tailb05910.ts.net] -SkillId amzn1.ask.skill....

A name that is not in the list is still heard in "play <name>" and, as far as Amazon's own test shows, as a bare name too; the list makes a bare
name reliable. Needs the ssh login to the server and ask-cli (`ask configure`) as for deploy.ps1.
#>
param(
  [Parameter(Mandatory = $true)][string]$SkillId,
  [string]$Server = "root@frank",
  [string]$PublicUrl = "https://frank.tailb05910.ts.net",
  [string]$DataDir = "/home/hms/docker/flacie-web/data/library"
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("flacie-names-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory $tmp | Out-Null
try {
  scp -q "${Server}:${DataDir}/*.v2.json" $tmp
  $files = @(Get-ChildItem $tmp -Filter *.v2.json | ForEach-Object { $_.FullName })
  if ($files.Count -eq 0) { throw "no library found on $Server in $DataDir" }
  $names = & python (Join-Path $here "library-names.py") @files
  [IO.File]::WriteAllText((Join-Path $here "names.txt"), (($names -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
  Write-Host "$($names.Count) names from $($files.Count) libraries"
} finally { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
& (Join-Path $here "deploy.ps1") -PublicUrl $PublicUrl -SkillId $SkillId
