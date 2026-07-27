[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$SkipApplicationBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw 'Inno Setup 6 compiler was not found. Install JRSoftware.InnoSetup with winget.'
}

if (-not $SkipApplicationBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration -Runtime $Runtime
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
}

$publish = Join-Path $root "artifacts/publish/$Runtime/HorrorHarbor.exe"
if (-not (Test-Path $publish)) {
    throw "Published executable was not found: $publish"
}

& (Join-Path $PSScriptRoot 'verify-publish.ps1') `
    -PublishDirectory (Split-Path -Parent $publish) `
    -InventoryPath "artifacts/validation/installer-publish-inventory-$Runtime.json"

$output = Join-Path $root 'artifacts/installer'
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory -Force -Path $output | Out-Null

& $iscc (Join-Path $root 'installer/NocturneHarbor.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }

$installers = @(Get-ChildItem -Path $output -Filter '*.exe')
if ($installers.Count -ne 1) {
    throw "Expected exactly one installer executable in '$output', but found $($installers.Count)."
}
$installer = $installers[0]
$hash = Get-FileHash $installer.FullName -Algorithm SHA256
"$($hash.Hash)  $($installer.Name)" | Set-Content -Path (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii

Write-Host "Installer created: $($installer.FullName)" -ForegroundColor Green
Write-Host "SHA-256: $($hash.Hash)" -ForegroundColor Green
