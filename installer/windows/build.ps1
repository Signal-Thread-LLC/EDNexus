<#
  Builds the EDNexus Windows installer with Inno Setup.

  Publishes a self-contained win-x64 build of EDNexus.App, then compiles ednexus.iss into a
  setup .exe that installs to "C:\Program Files\Signal & Thread\EDNexus".

  Prereqs: .NET 10 SDK and Inno Setup 6 (https://jrsoftware.org/isdl.php, or `choco install innosetup`).
  The Sentry DSN, if present in $env:SENTRY_DSN, is injected into the published app (kept out of the repo).
#>
param(
  [string]$Version = "0.0.1",
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$root = Resolve-Path (Join-Path $here "..\..")
$publish = Join-Path $here "publish"
$out = Join-Path $here "out"

if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
New-Item -ItemType Directory -Force $out | Out-Null

Write-Host "==> Publishing EDNexus.App ($Runtime, self-contained)..."
dotnet publish (Join-Path $root "src\EDNexus.App\EDNexus.App.csproj") `
  --configuration $Configuration `
  --runtime $Runtime --self-contained true `
  -p:Version=$Version `
  -p:SentryDsn=$env:SENTRY_DSN `
  --output $publish

# Locate the Inno Setup compiler.
$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
  foreach ($p in @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe")) {
    if (Test-Path $p) { $iscc = $p; break }
  }
}
if (-not $iscc) { throw "ISCC.exe (Inno Setup 6) not found. Install it, e.g. 'choco install innosetup'." }

Write-Host "==> Compiling installer with $iscc ..."
& $iscc "/DAppVersion=$Version" "/DPublishDir=$publish" "/O$out" (Join-Path $here "ednexus.iss")

Write-Host "==> Done: $out\EDNexus-$Version-setup.exe"
