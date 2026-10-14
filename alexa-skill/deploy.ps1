<#
Deploys the FLACie skill (interaction model + manifest) to YOUR Amazon developer account in the development stage.
You do the Amazon login yourself: run `ask configure` once, in your own terminal, before this. This script never asks for or stores a password or token.

  .\deploy.ps1 -PublicUrl https://flacie.example.com [-SkillId amzn1.ask.skill....]

Without -SkillId it creates the skill; with it, it updates that skill. Nothing is submitted for certification.
#>
param(
  [Parameter(Mandatory = $true)][string]$PublicUrl,
  [string]$SkillId = ""
)
$ErrorActionPreference = "Stop"
if ($PublicUrl -notmatch '^https://[^/:]+(/.*)?$') { throw "PublicUrl must be an https address on port 443, for example https://flacie.example.com" }
if (-not (Get-Command ask -ErrorAction SilentlyContinue)) { throw "ask-cli is not installed. Run: npm install -g ask-cli   then: ask configure" }

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$build = Join-Path $here ".build"
# .build keeps ask-cli's own state (.ask/ask-states.json holds the skill id), so a second run updates the same skill instead of making another one
New-Item -ItemType Directory -Force $build | Out-Null
if (Test-Path (Join-Path $build "skill-package")) { Remove-Item -Recurse -Force (Join-Path $build "skill-package") }
Copy-Item -Recurse (Join-Path $here "skill-package") (Join-Path $build "skill-package")
$endpoint = $PublicUrl.TrimEnd('/') + "/alexa/skill"
$host_ = ([uri]$PublicUrl).Host
$manifestPath = Join-Path $build "skill-package\skill.json"
$text = Get-Content $manifestPath -Raw
$text = $text.Replace("https://YOUR-FLACIE-HOST/alexa/skill", $endpoint).Replace("https://YOUR-FLACIE-HOST", $PublicUrl.TrimEnd('/'))
Set-Content -Path $manifestPath -Value $text -Encoding utf8

$res = @{ askcliResourcesVersion = "2020-03-31"; profiles = @{ default = @{ skillMetadata = @{ src = "./skill-package" } } } }
if ($SkillId -ne "") {
  # tell ask-cli which existing skill this is (its own state file)
  New-Item -ItemType Directory -Force (Join-Path $build ".ask") | Out-Null
  @{ askcliStatesVersion = "2020-03-31"; profiles = @{ default = @{ skillId = $SkillId } } } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $build ".asksk-states.json") -Encoding utf8
}
$res | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $build "ask-resources.json") -Encoding utf8

Push-Location $build
try {
  ask deploy --target skill-metadata
  Write-Host ""
  Write-Host "Done. Next: copy the skill id (amzn1.ask.skill...) from the output into FLACie > Settings > Alexa > Skill ID,"
  Write-Host "then in the Alexa app enable the skill (More > Skills & Games > Your Skills > Dev) and say: Alexa, open flacie"
} finally { Pop-Location }
