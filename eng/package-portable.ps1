[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Runtime $Runtime -Configuration $Configuration

$publish = Join-Path $root "artifacts/publish/$Runtime"
$packageDir = Join-Path $root 'artifacts/packages'
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
$zip = Join-Path $packageDir "HorrorHarbor-Alpha2-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = Get-FileHash $zip -Algorithm SHA256
$checksum = Join-Path $packageDir 'SHA256SUMS.txt'
"$($hash.Hash)  $([System.IO.Path]::GetFileName($zip))" | Set-Content -Path $checksum -Encoding ascii
$manifest = [ordered]@{
    product = 'HorrorHarbor'
    release = 'Alpha 2'
    runtime = $Runtime
    configuration = $Configuration
    package = [System.IO.Path]::GetFileName($zip)
    sha256 = $hash.Hash
    dotnetSdk = (& dotnet --version).Trim()
    sourceDateEpoch = $env:SOURCE_DATE_EPOCH
} | ConvertTo-Json
$manifest | Set-Content -Path (Join-Path $packageDir 'release-manifest.json') -Encoding utf8
$hash | Format-List
Write-Host "Portable package created: $zip" -ForegroundColor Green
