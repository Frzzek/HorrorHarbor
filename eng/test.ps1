[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet test tests/HorrorHarbor.UnitTests/HorrorHarbor.UnitTests.csproj `
        -c $Configuration --no-build `
        /p:CollectCoverage=true `
        /p:CoverletOutput=../../artifacts/coverage/unit/ `
        /p:CoverletOutputFormat=cobertura `
        /p:Threshold=80 `
        /p:ThresholdType=line `
        /p:ThresholdStat=total
    if ($LASTEXITCODE -ne 0) { throw 'Unit tests or coverage gate failed.' }
    $integrationResults = 'artifacts/test-results/quality-gate-integration'
    dotnet test tests/HorrorHarbor.IntegrationTests/HorrorHarbor.IntegrationTests.csproj `
        -c $Configuration `
        --no-build `
        --logger 'trx;LogFileName=integration.trx' `
        --results-directory $integrationResults
    if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed.' }
    dotnet test tests/HorrorHarbor.SecurityTests/HorrorHarbor.SecurityTests.csproj -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Security tests failed.' }
    dotnet run --project tests/HorrorHarbor.SelfTest/HorrorHarbor.SelfTest.csproj -c $Configuration --no-build -- `
        data/catalog/recommendation_catalog.json
    if ($LASTEXITCODE -ne 0) { throw 'Self-test failed.' }
}
finally {
    Pop-Location
}
