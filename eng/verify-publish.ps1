[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,
    [string]$InventoryPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$resolvedRoot = [IO.Path]::GetFullPath($root).TrimEnd('\')
$resolvedPublish = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd('\')
$allowedPublishRoot = [IO.Path]::GetFullPath(
    (Join-Path $root 'artifacts\publish')
).TrimEnd('\')

if (-not $resolvedPublish.StartsWith(
        "$allowedPublishRoot\",
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish verification is restricted to '$allowedPublishRoot'."
}
if (-not (Test-Path -LiteralPath $resolvedPublish -PathType Container)) {
    throw "Publish directory was not found: $resolvedPublish"
}

$files = @(Get-ChildItem -LiteralPath $resolvedPublish -Recurse -File)
if ($files.Count -eq 0) { throw 'Publish directory is empty.' }

$requiredRelativePaths = @(
    'HorrorHarbor.exe',
    'data\catalog\recommendation_catalog.json'
)
foreach ($relativePath in $requiredRelativePaths) {
    $requiredPath = Join-Path $resolvedPublish $relativePath
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required publish file is missing: $relativePath"
    }
}

$forbiddenExtensions = @(
    '.7z', '.bak', '.cache', '.db', '.log', '.pdb', '.sqlite', '.sqlite3',
    '.user', '.zip'
)
$forbiddenPathSegments = @(
    'credential', 'official-posters', 'poster-audit', 'recommendation_feedback',
    'testresults', 'user-data', 'watch-history'
)
$allowedDataPath = 'data/catalog/recommendation_catalog.json'
$inventory = [Collections.Generic.List[object]]::new()

foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($resolvedPublish.Length + 1)
    $portablePath = $relativePath.Replace('\', '/')
    $extension = $file.Extension.ToLowerInvariant()

    if ($extension -in $forbiddenExtensions) {
        throw "Forbidden publish file extension detected: $portablePath"
    }
    foreach ($segment in $forbiddenPathSegments) {
        if ($portablePath.IndexOf($segment, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Forbidden private-state path detected in publish output: $portablePath"
        }
    }
    if ($portablePath.StartsWith('data/', [StringComparison]::OrdinalIgnoreCase) -and
        -not $portablePath.Equals($allowedDataPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected data payload detected: $portablePath"
    }

    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    $ascii = [Text.Encoding]::ASCII.GetString($bytes)
    $unicode = [Text.Encoding]::Unicode.GetString($bytes)
    if ($ascii -match '(?i)C:\\Users\\[^\\\x00\r\n]+\\' -or
        $unicode -match '(?i)C:\\Users\\[^\\\x00\r\n]+\\') {
        throw "Private Windows profile path detected in publish file: $portablePath"
    }

    $inventory.Add([ordered]@{
        path = $portablePath
        bytes = $file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    })
}

if ($InventoryPath) {
    $resolvedInventory = if ([IO.Path]::IsPathRooted($InventoryPath)) {
        [IO.Path]::GetFullPath($InventoryPath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $resolvedRoot $InventoryPath))
    }
    $inventoryDirectory = Split-Path -Parent $resolvedInventory
    New-Item -ItemType Directory -Force -Path $inventoryDirectory | Out-Null
    [ordered]@{
        publishDirectory = $resolvedPublish.Substring($resolvedRoot.Length).TrimStart('\')
        fileCount = $inventory.Count
        files = $inventory
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resolvedInventory -Encoding utf8
}

Write-Host "Publish verification PASS: $($inventory.Count) files" -ForegroundColor Green
