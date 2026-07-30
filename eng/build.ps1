[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64')]
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
    $publishRoot = [IO.Path]::GetFullPath(
        (Join-Path $root 'artifacts\publish')
    ).TrimEnd('\')
    $publish = [IO.Path]::GetFullPath((Join-Path $publishRoot $Runtime)).TrimEnd('\')
    if (-not $publish.StartsWith(
            "$publishRoot\",
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The publish output must remain below the repository publish directory.'
    }
    foreach ($path in @(
        $root,
        (Join-Path $root 'artifacts'),
        $publishRoot,
        $publish
    )) {
        if (Test-Path -LiteralPath $path) {
            $item = Get-Item -LiteralPath $path -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Publish cleanup refuses reparse-point traversal: $path"
            }
        }
    }
    if (Test-Path -LiteralPath $publish) {
        Remove-Item -LiteralPath $publish -Recurse -Force
    }
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
    & "$root/tools/quality/verify-published-storage.ps1" `
        -PublishDirectory $publish `
        -ReportPath "artifacts/validation/published-storage-runtime-$Runtime.json"

    Write-Host "Build complete: $publish" -ForegroundColor Green
}
finally {
    Pop-Location
}
