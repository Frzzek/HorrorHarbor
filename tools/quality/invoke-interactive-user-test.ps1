[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoBuild,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $root 'src\HorrorHarbor.App\HorrorHarbor.App.csproj'
$outputDirectory = Join-Path $root (
    'src\HorrorHarbor.App\bin\' + $Configuration + '\net10.0-windows')
$executable = Join-Path $outputDirectory 'HorrorHarbor.exe'
$sessionId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 12)
$userTestsRoot = Join-Path $root 'artifacts\user-tests'
$sessionRoot = Join-Path $userTestsRoot $sessionId
$runtimeRoot = Join-Path $sessionRoot 'runtime'
$feedbackRoot = Join-Path $sessionRoot 'feedback'
$reportName = 'interactive-user-test-report.json'
# Keep the session flags explicit for the isolated test lifecycle.
$createdSession = $false
$createdRuntime = $false
$createdFeedback = $false
$retainFeedback = $false

function Assert-ContainedPath {
    param(
        [Parameter(Mandatory = $true)][string]$Candidate,
        [Parameter(Mandatory = $true)][string]$Boundary,
        [switch]$AllowBoundary
    )

    $candidatePath = [IO.Path]::GetFullPath($Candidate).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $boundaryPath = [IO.Path]::GetFullPath($Boundary).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $pathRoot = [IO.Path]::GetPathRoot($boundaryPath).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    if ($boundaryPath.Equals($pathRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Interactive test storage boundary is unsafe.'
    }

    $isBoundary = $candidatePath.Equals(
        $boundaryPath,
        [StringComparison]::OrdinalIgnoreCase)
    $isDescendant = $candidatePath.StartsWith(
        $boundaryPath + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
    if ((-not ($AllowBoundary -and $isBoundary)) -and (-not $isDescendant)) {
        throw 'Interactive test storage escaped its isolated boundary.'
    }
}

function Assert-NoReparsePoint {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $current = [IO.Path]::GetPathRoot($fullPath)
    foreach ($segment in $fullPath.Substring($current.Length).Split(
            [IO.Path]::DirectorySeparatorChar,
            [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = [IO.Path]::Combine($current, $segment)
        if ((Test-Path -LiteralPath $current) -and
            ((Get-Item -LiteralPath $current -Force).Attributes -band
                [IO.FileAttributes]::ReparsePoint)) {
            throw 'Interactive test storage must not traverse reparse points.'
        }
    }
}

function Assert-ExactSessionPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][ValidateSet('runtime', 'feedback')]
        [string]$LeafName
    )

    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    if (-not [IO.Path]::GetFileName($fullPath).Equals(
            $LeafName,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Path]::GetDirectoryName($fullPath).Equals(
            [IO.Path]::GetFullPath($sessionRoot).TrimEnd(
                [IO.Path]::DirectorySeparatorChar,
                [IO.Path]::AltDirectorySeparatorChar),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Interactive test cleanup target is not the new session subtree.'
    }

    Assert-ContainedPath -Candidate $fullPath -Boundary $userTestsRoot
    Assert-NoReparsePoint -Path $fullPath
}

function Remove-IsolatedTree {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][ValidateSet('runtime', 'feedback')]
        [string]$LeafName
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    Assert-ExactSessionPath -Path $Path -LeafName $LeafName
    Remove-Item -LiteralPath $Path -Recurse -Force
}

function Assert-ExactProperties {
    param(
        [Parameter(Mandatory = $true)][object]$Value,
        [Parameter(Mandatory = $true)][string[]]$Expected
    )

    $actual = @($Value.PSObject.Properties.Name | Sort-Object)
    $expectedSorted = @($Expected | Sort-Object)
    if ($actual.Count -ne $expectedSorted.Count -or
        @(Compare-Object -ReferenceObject $expectedSorted -DifferenceObject $actual).Count -ne 0) {
        throw 'Saved feedback did not match the fixed privacy-safe schema.'
    }
}

function Assert-IntegerRating {
    param([Parameter(Mandatory = $true)][object]$Value)

    if ($Value -isnot [int] -or $Value -lt 1 -or $Value -gt 5) {
        throw 'Saved feedback contained an invalid fixed rating.'
    }
}

function Test-SanitizedReport {
    if (-not (Test-Path -LiteralPath $feedbackRoot -PathType Container)) {
        return $false
    }

    Assert-ExactSessionPath -Path $feedbackRoot -LeafName 'feedback'
    $entries = @(Get-ChildItem -LiteralPath $feedbackRoot -Force)
    if ($entries.Count -eq 0) {
        return $false
    }

    if ($entries.Count -ne 1 -or
        $entries[0].PSIsContainer -or
        -not $entries[0].Name.Equals($reportName, [StringComparison]::Ordinal)) {
        throw 'Only one explicitly saved sanitized feedback report may be retained.'
    }

    Assert-ContainedPath -Candidate $entries[0].FullName -Boundary $feedbackRoot
    Assert-NoReparsePoint -Path $entries[0].FullName
    if ($entries[0].Length -le 0 -or $entries[0].Length -gt 16384) {
        throw 'Saved feedback report size is outside the fixed boundary.'
    }

    try {
        $report = [IO.File]::ReadAllText(
            $entries[0].FullName,
            [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
    }
    catch {
        throw 'Saved feedback report is not valid UTF-8 JSON.'
    }

    Assert-ExactProperties -Value $report -Expected @(
        'schemaVersion',
        'scenarioVersion',
        'steps',
        'ratings',
        'issueTags',
        'completionState',
        'privacyAssertions')
    if ($report.schemaVersion -cne 'interactive-user-test-report-v1' -or
        $report.scenarioVersion -cne 'horrorharbor-guided-usability-v1' -or
        $report.completionState -cne 'completed') {
        throw 'Saved feedback report metadata is invalid.'
    }

    $expectedStepIds = @(
        'ut-step-01',
        'ut-step-02',
        'ut-step-03',
        'ut-step-04',
        'ut-step-05')
    $allowedOutcomes = @(
        'completed-easily',
        'completed-with-effort',
        'could-not-complete')
    $steps = @($report.steps)
    if ($steps.Count -ne $expectedStepIds.Count) {
        throw 'Saved feedback report has an invalid task count.'
    }

    for ($index = 0; $index -lt $expectedStepIds.Count; $index++) {
        Assert-ExactProperties -Value $steps[$index] -Expected @('stepId', 'outcome')
        if ($steps[$index].stepId -cne $expectedStepIds[$index] -or
            $allowedOutcomes -cnotcontains $steps[$index].outcome) {
            throw 'Saved feedback report has an invalid fixed task outcome.'
        }
    }

    Assert-ExactProperties -Value $report.ratings -Expected @(
        'startupClarity',
        'visualAppealReadability',
        'navigationConfidence',
        'likelihoodOfReuse')
    Assert-IntegerRating -Value $report.ratings.startupClarity
    Assert-IntegerRating -Value $report.ratings.visualAppealReadability
    Assert-IntegerRating -Value $report.ratings.navigationConfidence
    Assert-IntegerRating -Value $report.ratings.likelihoodOfReuse

    $allowedTags = @(
        'contrast-or-focus',
        'keyboard-difficult',
        'labels-unclear',
        'layout-crowded',
        'navigation-unexpected',
        'response-felt-slow',
        'text-hard-to-read')
    $tags = @($report.issueTags)
    if ($tags.Count -gt $allowedTags.Count) {
        throw 'Saved feedback report has too many issue tags.'
    }

    $previousTag = $null
    foreach ($tag in $tags) {
        if ($tag -isnot [string] -or
            $allowedTags -cnotcontains $tag -or
            ($null -ne $previousTag -and
                [StringComparer]::Ordinal.Compare($previousTag, $tag) -ge 0)) {
            throw 'Saved feedback report has invalid or duplicate issue tags.'
        }
        $previousTag = $tag
    }

    Assert-ExactProperties -Value $report.privacyAssertions -Expected @(
        'offlineSession',
        'noAccountRequired',
        'noTelemetry',
        'noPersonalDataRequested',
        'noFreeTextCollected',
        'noApplicationStateCollected')
    foreach ($propertyName in @(
            'offlineSession',
            'noAccountRequired',
            'noTelemetry',
            'noPersonalDataRequested',
            'noFreeTextCollected',
            'noApplicationStateCollected')) {
        if ($report.privacyAssertions.$propertyName -isnot [bool] -or
            $report.privacyAssertions.$propertyName -ne $true) {
            throw 'Saved feedback report has invalid privacy assertions.'
        }
    }

    return $true
}

if (-not $NoBuild) {
    & dotnet build $project -c $Configuration --no-restore -nodeReuse:false
    if ($LASTEXITCODE -ne 0) {
        throw 'The interactive user-test application build failed.'
    }
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'The interactive user-test executable is missing; build it before using -NoBuild.'
}

Assert-ContainedPath -Candidate $sessionRoot -Boundary $userTestsRoot
Assert-ContainedPath -Candidate $runtimeRoot -Boundary $sessionRoot
Assert-ContainedPath -Candidate $feedbackRoot -Boundary $sessionRoot
Assert-NoReparsePoint -Path $sessionRoot
if (Test-Path -LiteralPath $sessionRoot) {
    throw 'The unique interactive user-test session already exists.'
}

try {
    New-Item -ItemType Directory -Path $sessionRoot | Out-Null
    $createdSession = $true
    New-Item -ItemType Directory -Path $runtimeRoot | Out-Null
    $createdRuntime = $true
    New-Item -ItemType Directory -Path $feedbackRoot | Out-Null
    $createdFeedback = $true
    Assert-ExactSessionPath -Path $runtimeRoot -LeafName 'runtime'
    Assert-ExactSessionPath -Path $feedbackRoot -LeafName 'feedback'

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $executable
    $startInfo.WorkingDirectory = $outputDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.EnvironmentVariables['DOTNET_ENVIRONMENT'] = 'Development'
    $startInfo.EnvironmentVariables['RuntimeProfile__RootDirectory'] = $runtimeRoot
    $startInfo.EnvironmentVariables['RuntimeProfile__EnableNetworkProviders'] = 'false'
    $startInfo.EnvironmentVariables['InteractiveUserTest__Enabled'] = 'true'
    $startInfo.EnvironmentVariables['InteractiveUserTest__OutputRoot'] = $feedbackRoot
    $startInfo.EnvironmentVariables['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $startInfo.EnvironmentVariables.Remove('HORRORHARBOR_DETACHED_SCREENSHOT_PATH')

    if ($startInfo.EnvironmentVariables['DOTNET_ENVIRONMENT'] -cne 'Development' -or
        $startInfo.EnvironmentVariables['RuntimeProfile__RootDirectory'] -cne $runtimeRoot -or
        $startInfo.EnvironmentVariables['RuntimeProfile__EnableNetworkProviders'] -cne 'false' -or
        $startInfo.EnvironmentVariables['InteractiveUserTest__Enabled'] -cne 'true' -or
        $startInfo.EnvironmentVariables['InteractiveUserTest__OutputRoot'] -cne $feedbackRoot) {
        throw 'The interactive user-test launcher isolation contract is invalid.'
    }

    if ($ValidateOnly) {
        Write-Output 'Interactive user-test launcher validation PASS (application not launched).'
    }
    else {
        Write-Output 'Interactive user test started in an isolated offline Development session.'
        Write-Output 'Follow the on-screen instructions. Closing without saving retains no report.'
        $process = [Diagnostics.Process]::Start($startInfo)
        if (-not $process) {
            throw 'The interactive user-test process did not start.'
        }

        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw 'The interactive user-test process exited unsuccessfully.'
        }

        $retainFeedback = Test-SanitizedReport
        if ($retainFeedback) {
            Write-Output (
                'Sanitized local feedback retained at artifacts\user-tests\' +
                $sessionId + '\feedback\' + $reportName)
        }
        else {
            Write-Output 'Interactive user test closed without a saved feedback report.'
        }
    }
}
finally {
    if ($createdRuntime) {
        Remove-IsolatedTree -Path $runtimeRoot -LeafName 'runtime'
    }
    if ($createdFeedback -and -not $retainFeedback) {
        Remove-IsolatedTree -Path $feedbackRoot -LeafName 'feedback'
    }
    if ($createdSession -and
        (Test-Path -LiteralPath $sessionRoot -PathType Container) -and
        @(Get-ChildItem -LiteralPath $sessionRoot -Force).Count -eq 0) {
        Assert-ContainedPath -Candidate $sessionRoot -Boundary $userTestsRoot
        Assert-NoReparsePoint -Path $sessionRoot
        Remove-Item -LiteralPath $sessionRoot -Force
    }
}
