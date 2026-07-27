[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$SigningCertificateThumbprint
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$packageScript = Join-Path $PSScriptRoot 'package-portable.ps1'

& $packageScript -Runtime $Runtime -Configuration $Configuration

$publish = Join-Path $root "artifacts/publish/$Runtime"
$executable = Join-Path $publish 'HorrorHarbor.exe'
if ($SigningCertificateThumbprint) {
    $signTool = Get-Command signtool.exe -ErrorAction Stop
    & $signTool.Source sign /sha1 $SigningCertificateThumbprint /fd SHA256 /td SHA256 `
        /tr http://timestamp.digicert.com $executable
    if ($LASTEXITCODE -ne 0) { throw 'Authenticode signing failed.' }
    & $signTool.Source verify /pa /all $executable
    if ($LASTEXITCODE -ne 0) { throw 'Authenticode verification failed.' }
}
else {
    Write-Warning 'No signing thumbprint supplied. Artifacts are suitable for internal verification only.'
}

$sbomDirectory = Join-Path $root 'artifacts/sbom'
New-Item -ItemType Directory -Force -Path $sbomDirectory | Out-Null
dotnet list (Join-Path $root 'HorrorHarbor.sln') package --include-transitive --format json `
    --output-version 1 | Set-Content -Path (Join-Path $sbomDirectory 'dotnet-packages.json') -Encoding utf8
if ($LASTEXITCODE -ne 0) { throw 'SBOM package inventory generation failed.' }

Write-Host "Release artifacts complete: $(Join-Path $root 'artifacts')" -ForegroundColor Green
