[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $pythonPath = $null
    $pythonArguments = @()
    $installedPython = Join-Path $env:LOCALAPPDATA 'Programs\Python\Python313\python.exe'
    if (Test-Path $installedPython) {
        $pythonPath = $installedPython
    }
    else {
        $python = Get-Command python -CommandType Application -ErrorAction SilentlyContinue
        if ($python -and $python.Source -notlike '*WindowsApps*') {
            $pythonPath = $python.Source
        }
        else {
            $pythonLauncher = Get-Command py -CommandType Application -ErrorAction SilentlyContinue
            if ($pythonLauncher) {
                $pythonPath = $pythonLauncher.Source
                $pythonArguments = @('-3')
            }
        }
    }

    if (-not $pythonPath) { throw 'Python 3 is required for static and privacy validation.' }
    & $pythonPath @pythonArguments tools/quality/static_validate.py --root . --report artifacts/validation/static-validation.json
    if ($LASTEXITCODE -ne 0) { throw 'Static validation failed.' }

    & "$PSScriptRoot/security-scan.ps1" -Configuration $Configuration
    dotnet format HorrorHarbor.sln --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Formatting verification failed.' }
    dotnet build HorrorHarbor.sln -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
    & "$PSScriptRoot/test.ps1" -Configuration $Configuration
    $posterAuditOutput = Join-Path `
        'artifacts/poster-audit' `
        ("quality-gate-{0}" -f [Guid]::NewGuid().ToString('N'))
    dotnet run --project src/HorrorHarbor.Tools.PosterAudit/HorrorHarbor.Tools.PosterAudit.csproj `
        -c $Configuration --no-build -- `
        --catalog data/catalog/recommendation_catalog.json `
        --output $posterAuditOutput `
        --width 480 `
        --height 720 `
        --maximum-file-bytes 5000000
    if ($LASTEXITCODE -ne 0) { throw 'Poster Build P2 evidence generation failed.' }
    dotnet run --project tests/HorrorHarbor.PerformanceTests/HorrorHarbor.PerformanceTests.csproj `
        -c $Configuration --no-build -- `
        data/catalog/recommendation_catalog.json artifacts/performance/performance-report.json
    if ($LASTEXITCODE -ne 0) { throw 'Performance gate failed.' }
    Write-Host 'All Alpha 2 quality gates PASS' -ForegroundColor Green
}
finally {
    Pop-Location
}
