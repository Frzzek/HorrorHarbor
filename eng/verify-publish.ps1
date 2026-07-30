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
    'appsettings.json',
    'data\catalog\recommendation_catalog.json',
    'SQLite3MC.PCLRaw.provider.dll',
    'sqlite3mc.dll'
)
foreach ($relativePath in $requiredRelativePaths) {
    $requiredPath = Join-Path $resolvedPublish $relativePath
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required publish file is missing: $relativePath"
    }
}

$approvedNativeProviderPath = Join-Path $resolvedPublish 'sqlite3mc.dll'
$approvedManagedProviderPath = Join-Path $resolvedPublish 'SQLite3MC.PCLRaw.provider.dll'
$nativeProviderVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo(
    $approvedNativeProviderPath
)
if ($nativeProviderVersion.FileVersion -ne '2.3.6' -or
    $nativeProviderVersion.ProductVersion -ne '2.3.6' -or
    $nativeProviderVersion.OriginalFilename -ne 'sqlite3mc.dll' -or
    $nativeProviderVersion.ProductName -ne 'SQLite3 Multiple Ciphers') {
    throw 'The published sqlite3mc.dll identity does not match reviewed SQLite3MC 2.3.6.'
}

$managedProviderVersion = [Reflection.AssemblyName]::GetAssemblyName(
    $approvedManagedProviderPath
).Version.ToString()
if ($managedProviderVersion -ne '2.3.6.0') {
    throw "Unexpected SQLite3MC managed provider version: $managedProviderVersion"
}

$sqliteNativeCandidates = @(
    $files | Where-Object {
        $_.Name -match '(?i)^(?:(?:e_|lib)?sqlite3(?:mc)?|winsqlite3|(?:lib)?sqlcipher|SQLite\.Interop)\.dll$'
    }
)
if ($sqliteNativeCandidates.Count -ne 1 -or
    -not $sqliteNativeCandidates[0].FullName.Equals(
        $approvedNativeProviderPath,
        [StringComparison]::OrdinalIgnoreCase)) {
    $observed = @($sqliteNativeCandidates | ForEach-Object {
        $_.FullName.Substring($resolvedPublish.Length + 1).Replace('\', '/')
    })
    throw "Publish must contain exactly one approved native SQLite implementation. Observed: $($observed -join ', ')"
}

$sqliteManagedProviderCandidates = @(
    $files | Where-Object {
        $_.Name -match '(?i)^(?:SQLite3MC\.PCLRaw|SQLitePCLRaw)\.provider(?:\..+)?\.dll$'
    }
)
if ($sqliteManagedProviderCandidates.Count -ne 1 -or
    -not $sqliteManagedProviderCandidates[0].FullName.Equals(
        $approvedManagedProviderPath,
        [StringComparison]::OrdinalIgnoreCase)) {
    $observed = @($sqliteManagedProviderCandidates | ForEach-Object {
        $_.FullName.Substring($resolvedPublish.Length + 1).Replace('\', '/')
    })
    throw "Publish must contain exactly one approved managed SQLite provider. Observed: $($observed -join ', ')"
}

$dependencyManifestPath = Join-Path $resolvedPublish 'HorrorHarbor.deps.json'
try {
    $dependencyManifest = Get-Content -LiteralPath $dependencyManifestPath -Raw |
        ConvertFrom-Json
}
catch {
    throw "Published dependency manifest is invalid: $($_.Exception.Message)"
}
$publishedLibraries = @($dependencyManifest.libraries.PSObject.Properties.Name)
$requiredSqliteLibraries = @(
    'Microsoft.Data.Sqlite.Core/10.0.10',
    'SQLite3MC.PCLRaw.bundle/2.3.6',
    'SQLite3MC.PCLRaw.lib/2.3.6',
    'SQLite3MC.PCLRaw.provider/2.3.6',
    'SQLitePCLRaw.core/3.0.2'
)
foreach ($library in $requiredSqliteLibraries) {
    if ($library -notin $publishedLibraries) {
        throw "Required reviewed SQLite dependency is missing from the publish manifest: $library"
    }
}

$forbiddenSqliteLibraryPatterns = @(
    '^Microsoft\.Data\.Sqlite/',
    '^SQLitePCLRaw\.bundle_',
    '^SQLitePCLRaw\.lib\.',
    '^SQLitePCLRaw\.provider\.',
    '^System\.Data\.SQLite'
)
$forbiddenSqliteLibraries = @(
    $publishedLibraries | Where-Object {
        $libraryName = $_
        @($forbiddenSqliteLibraryPatterns | Where-Object {
            $libraryName -match $_
        }).Count -gt 0
    }
)
if ($forbiddenSqliteLibraries.Count -gt 0) {
    throw "Forbidden ordinary or duplicate SQLite dependencies detected: $($forbiddenSqliteLibraries -join ', ')"
}

$forbiddenExtensions = @(
    '.7z', '.bak', '.cache', '.db', '.log', '.pdb', '.sqlite', '.sqlite3',
    '.user', '.zip'
)
$forbiddenPathSegments = @(
    'credential', 'official-posters', 'poster-audit', 'recommendation_feedback',
    'testresults', 'user-data', 'watch-history'
)
$forbiddenFileNames = @(
    'appsettings.Development.json', 'appsettings.Testing.json'
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
    if ($file.Name -in $forbiddenFileNames) {
        throw "Development or testing configuration detected: $portablePath"
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
