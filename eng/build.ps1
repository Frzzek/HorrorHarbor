[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $version = (& dotnet --version).Trim()
    if (-not $version.StartsWith('10.')) {
        throw "HorrorHarbor requires a .NET 10 SDK. Detected: $version"
    }

    & "$PSScriptRoot/quality-gate.ps1" -Configuration $Configuration
    $publish = Join-Path $root "artifacts/publish/$Runtime"
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    dotnet clean HorrorHarbor.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Pre-publish clean failed.' }
    dotnet restore src/HorrorHarbor.App/HorrorHarbor.App.csproj `
        -r $Runtime `
        -p:PublishReadyToRun=true
    if ($LASTEXITCODE -ne 0) { throw 'Runtime-specific publish restore failed.' }
    dotnet publish src/HorrorHarbor.App/HorrorHarbor.App.csproj `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        --no-restore `
        -p:PublishReadyToRun=true `
        -p:PublishSingleFile=false `
        -p:DebugSymbols=false `
        -p:DebugType=None `
        -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'WPF publish failed.' }

    Get-ChildItem -LiteralPath $publish -Recurse -File -Filter '*.pdb' |
        Remove-Item -Force

    & "$PSScriptRoot/verify-publish.ps1" `
        -PublishDirectory $publish `
        -InventoryPath "artifacts/validation/publish-inventory-$Runtime.json"

    Write-Host "Build complete: $publish" -ForegroundColor Green
}
finally {
    Pop-Location
}
