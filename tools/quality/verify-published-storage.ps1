[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,
    [string]$ReportPath = 'artifacts/validation/published-storage-runtime.json'
)

$ErrorActionPreference = 'Stop'

if (-not ('HorrorHarbor.Quality.ExactReadLock' -as [type])) {
    Add-Type -TypeDefinition @'
using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace HorrorHarbor.Quality
{
    public sealed class ExactReadLock : IDisposable
    {
        private const uint GenericRead = 0x80000000;
        private const uint FileShareRead = 0x00000001;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x00000080;
        private const uint FileAttributeDirectory = 0x00000010;
        private const uint FileAttributeReparsePoint = 0x00000400;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FileFlagSequentialScan = 0x08000000;
        private const int FileAttributeTagInfo = 9;

        private SafeFileHandle _handle;

        public ExactReadLock(string path, bool directory)
        {
            _handle = CreateFile(
                path,
                GenericRead,
                FileShareRead,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal
                    | FileFlagOpenReparsePoint
                    | FileFlagSequentialScan
                    | (directory ? FileFlagBackupSemantics : 0),
                IntPtr.Zero);
            if (_handle.IsInvalid)
            {
                throw new IOException(
                    "Exact payload file lock failed.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }

            FileAttributeTagInformation attributes;
            if (!GetFileInformationByHandleEx(
                    _handle,
                    FileAttributeTagInfo,
                    out attributes,
                    (uint)Marshal.SizeOf(typeof(FileAttributeTagInformation))))
            {
                int error = Marshal.GetLastWin32Error();
                _handle.Dispose();
                throw new IOException(
                    "Exact payload file type inspection failed.",
                    new Win32Exception(error));
            }
            if ((attributes.FileAttributes & FileAttributeReparsePoint) != 0)
            {
                _handle.Dispose();
                throw new IOException("Exact payload locking refuses a reparse-point file.");
            }
            bool openedDirectory = (attributes.FileAttributes & FileAttributeDirectory) != 0;
            if (openedDirectory != directory)
            {
                _handle.Dispose();
                throw new IOException("Exact payload object type changed while locking.");
            }
        }

        public void Dispose()
        {
            if (_handle != null)
            {
                _handle.Dispose();
                _handle = null;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileAttributeTagInformation
        {
            public uint FileAttributes;
            public uint ReparseTag;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle fileHandle,
            int fileInformationClass,
            out FileAttributeTagInformation fileInformation,
            uint bufferSize);
    }

    public sealed class DirectoryChangeMonitor : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private int _changes;

        public DirectoryChangeMonitor(string path)
        {
            _watcher = new FileSystemWatcher(path);
            _watcher.IncludeSubdirectories = true;
            _watcher.NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.Size
                | NotifyFilters.LastWrite
                | NotifyFilters.Security;
            _watcher.Changed += OnChanged;
            _watcher.Created += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;
            _watcher.EnableRaisingEvents = true;
        }

        public int ChangeCount { get { return Volatile.Read(ref _changes); } }

        private void OnChanged(object sender, FileSystemEventArgs args)
        {
            Interlocked.Increment(ref _changes);
        }

        private void OnRenamed(object sender, RenamedEventArgs args)
        {
            Interlocked.Increment(ref _changes);
        }

        private void OnError(object sender, ErrorEventArgs args)
        {
            Interlocked.Increment(ref _changes);
        }

        public void Dispose()
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
    }
}
'@
}

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$resolvedRoot = [IO.Path]::GetFullPath($root).TrimEnd('\')
$resolvedPublish = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd('\')
$allowedPublishRoot = [IO.Path]::GetFullPath(
    (Join-Path $root 'artifacts\publish')
).TrimEnd('\')
$resolvedReport = if ([IO.Path]::IsPathRooted($ReportPath)) {
    [IO.Path]::GetFullPath($ReportPath)
}
else {
    [IO.Path]::GetFullPath((Join-Path $root $ReportPath))
}
$allowedReportRoot = [IO.Path]::GetFullPath(
    (Join-Path $root 'artifacts\validation')
).TrimEnd('\')

function Assert-NoReparsePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $pathRoot = [IO.Path]::GetPathRoot($fullPath)
    $current = $pathRoot
    $relative = $fullPath.Substring($pathRoot.Length)
    foreach ($component in $relative.Split(
            [char[]]@('\', '/'),
            [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $component
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Purpose refuses a reparse-point path component: $current"
            }
        }
    }
}

function Get-ConfinedRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $directoryPrefix = [IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith(
            $directoryPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Candidate payload path escaped its inventory root: $fullPath"
    }
    return $fullPath.Substring($directoryPrefix.Length).Replace('\', '/')
}

function Get-TextSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text
    )

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
        return ([BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-CanonicalInventory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,
        [string[]]$ExcludedRelativePaths = @()
    )

    $excluded = [Collections.Generic.HashSet[string]]::new(
        $ExcludedRelativePaths,
        [StringComparer]::Ordinal
    )
    foreach ($childDirectory in Get-ChildItem -LiteralPath $Directory -Recurse -Directory -Force) {
        if (($childDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Candidate payload inventory refuses a reparse-point directory: $($childDirectory.FullName)"
        }
    }

    $filesByPath = @{}
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -File -Force) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Candidate payload inventory refuses a reparse-point file: $($file.FullName)"
        }

        $relativePath = Get-ConfinedRelativePath `
            -Directory $Directory `
            -Path $file.FullName
        if ($excluded.Contains($relativePath)) {
            continue
        }
        if ($filesByPath.ContainsKey($relativePath)) {
            throw "Candidate payload contains a duplicate canonical path: $relativePath"
        }
        $filesByPath.Add($relativePath, $file)
    }

    $relativePaths = [string[]]$filesByPath.Keys
    [Array]::Sort($relativePaths, [StringComparer]::Ordinal)
    $files = @(
        foreach ($relativePath in $relativePaths) {
            $file = $filesByPath[$relativePath]
            [ordered]@{
                path = $relativePath
                bytes = $file.Length
                sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            }
        }
    )
    $json = $files | ConvertTo-Json -Depth 3 -Compress
    $sha256 = Get-TextSha256 -Text $json

    [pscustomobject]@{
        Files = $files
        Json = $json
        Sha256 = $sha256
    }
}

function Open-InventoryLocks {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    $locks = [Collections.Generic.List[IDisposable]]::new()
    try {
        $locks.Add([HorrorHarbor.Quality.ExactReadLock]::new($Directory, $true))
        foreach ($childDirectory in Get-ChildItem -LiteralPath $Directory -Recurse -Directory -Force) {
            $locks.Add([HorrorHarbor.Quality.ExactReadLock]::new(
                    $childDirectory.FullName,
                    $true
                ))
        }
        foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -File -Force) {
            $locks.Add([HorrorHarbor.Quality.ExactReadLock]::new(
                    $file.FullName,
                    $false
                ))
        }
        return $locks
    }
    catch {
        foreach ($lock in $locks) {
            $lock.Dispose()
        }
        throw
    }
}

function Assert-InventoryEqual {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Expected,
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Actual,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    if ($Expected.Files.Count -ne $Actual.Files.Count -or
        $Expected.Sha256 -ne $Actual.Sha256) {
        throw "$Purpose inventory count or digest changed."
    }
    for ($index = 0; $index -lt $Expected.Files.Count; $index++) {
        $expectedFile = $Expected.Files[$index]
        $actualFile = $Actual.Files[$index]
        if ($expectedFile.path -cne $actualFile.path -or
            $expectedFile.bytes -ne $actualFile.bytes -or
            $expectedFile.sha256 -cne $actualFile.sha256) {
            throw "$Purpose inventory changed at index $index."
        }
    }
}

if (-not $resolvedPublish.StartsWith(
        "$allowedPublishRoot\",
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Published storage verification is restricted to '$allowedPublishRoot'."
}
if (-not $resolvedReport.StartsWith(
        "$allowedReportRoot\",
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Published storage reports are restricted to '$allowedReportRoot'."
}
Assert-NoReparsePath -Path $resolvedRoot -Purpose 'Repository validation'
Assert-NoReparsePath -Path $allowedPublishRoot -Purpose 'Published storage verification'
Assert-NoReparsePath -Path $resolvedPublish -Purpose 'Published storage verification'
Assert-NoReparsePath -Path $allowedReportRoot -Purpose 'Published storage reporting'
Assert-NoReparsePath -Path $resolvedReport -Purpose 'Published storage reporting'
if (Test-Path -LiteralPath $resolvedReport) {
    Remove-Item -LiteralPath $resolvedReport -Force
}
if (-not (Test-Path -LiteralPath $resolvedPublish -PathType Container)) {
    throw "Publish directory was not found: $resolvedPublish"
}

$workRoot = Join-Path $root (
    'artifacts\validation\published-storage-probe-' + [Guid]::NewGuid().ToString('N')
)
$candidateDirectory = Join-Path $workRoot 'candidate'
$buildDirectory = Join-Path $workRoot 'build'
$runtimeDirectory = Join-Path $workRoot 'runtime'
$probeProject = Join-Path $PSScriptRoot `
    'PublishedStorageProbe\HorrorHarbor.PublishedStorageProbe.csproj'
$probeAssembly = 'HorrorHarbor.PublishedStorageProbe.dll'
$probeFiles = @(
    $probeAssembly,
    'HorrorHarbor.PublishedStorageProbe.deps.json',
    'HorrorHarbor.PublishedStorageProbe.runtimeconfig.json'
)
$candidateLocks = $null
$runtimeLocks = $null
$sourceLocks = $null
$sourceMonitor = $null
$candidateMonitor = $null
$runtimeMonitor = $null
$verificationSucceeded = $false
try {
    New-Item -ItemType Directory -Force `
        -Path $candidateDirectory, $buildDirectory, $runtimeDirectory | Out-Null
    $sourceMonitor = [HorrorHarbor.Quality.DirectoryChangeMonitor]::new(
        $resolvedPublish
    )
    $sourceBefore = Get-CanonicalInventory -Directory $resolvedPublish
    Copy-Item -Path (Join-Path $resolvedPublish '*') `
        -Destination $candidateDirectory `
        -Recurse `
        -Force
    $candidateSnapshot = Get-CanonicalInventory -Directory $candidateDirectory
    $sourceAfter = Get-CanonicalInventory -Directory $resolvedPublish
    Assert-InventoryEqual `
        -Expected $sourceBefore `
        -Actual $candidateSnapshot `
        -Purpose 'Published source-to-snapshot'
    Assert-InventoryEqual `
        -Expected $candidateSnapshot `
        -Actual $sourceAfter `
        -Purpose 'Published source stability'
    $sourceLocks = Open-InventoryLocks -Directory $resolvedPublish
    Assert-InventoryEqual `
        -Expected $sourceBefore `
        -Actual (Get-CanonicalInventory -Directory $resolvedPublish) `
        -Purpose 'Locked published source'
    $candidateMonitor = [HorrorHarbor.Quality.DirectoryChangeMonitor]::new(
        $candidateDirectory
    )
    $candidateLocks = Open-InventoryLocks -Directory $candidateDirectory
    Assert-InventoryEqual `
        -Expected $candidateSnapshot `
        -Actual (Get-CanonicalInventory -Directory $candidateDirectory) `
        -Purpose 'Locked candidate snapshot'

    $requiredFiles = @(
        'HorrorHarbor.dll',
        'HorrorHarbor.Core.dll',
        'HorrorHarbor.Data.dll',
        'Microsoft.Data.Sqlite.dll',
        'SQLite3MC.PCLRaw.provider.dll',
        'SQLitePCLRaw.batteries_v2.dll',
        'SQLitePCLRaw.core.dll',
        'sqlite3mc.dll'
    )
    foreach ($fileName in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $candidateDirectory $fileName) -PathType Leaf)) {
            throw "Required candidate runtime file is missing: $fileName"
        }
    }

    $appHash = (Get-FileHash -LiteralPath (Join-Path $candidateDirectory 'HorrorHarbor.dll') `
        -Algorithm SHA256).Hash
    $dataHash = (Get-FileHash -LiteralPath (Join-Path $candidateDirectory 'HorrorHarbor.Data.dll') `
        -Algorithm SHA256).Hash
    $nativeHash = (Get-FileHash -LiteralPath (Join-Path $candidateDirectory 'sqlite3mc.dll') `
        -Algorithm SHA256).Hash
    $publishInventory = $candidateSnapshot.Files
    $inventoryHash = $candidateSnapshot.Sha256

    $publishedDirectoryProperty = "-p:PublishedHorrorHarborDirectory=$candidateDirectory"
    dotnet build $probeProject `
        -c Release `
        --nologo `
        $publishedDirectoryProperty `
        -o $buildDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Published storage probe build failed.' }

    Copy-Item -Path (Join-Path $candidateDirectory '*') `
        -Destination $runtimeDirectory `
        -Recurse `
        -Force
    foreach ($fileName in $probeFiles) {
        Copy-Item -LiteralPath (Join-Path $buildDirectory $fileName) `
            -Destination (Join-Path $runtimeDirectory $fileName) `
            -Force
    }
    Assert-InventoryEqual `
        -Expected $candidateSnapshot `
        -Actual (Get-CanonicalInventory -Directory $candidateDirectory) `
        -Purpose 'Candidate snapshot before execution'
    $runtimePayloadBefore = Get-CanonicalInventory `
        -Directory $runtimeDirectory `
        -ExcludedRelativePaths $probeFiles
    Assert-InventoryEqual `
        -Expected $candidateSnapshot `
        -Actual $runtimePayloadBefore `
        -Purpose 'Exact candidate runtime clone'
    $runtimeCompleteBefore = Get-CanonicalInventory -Directory $runtimeDirectory
    if ($runtimeCompleteBefore.Files.Count -ne ($candidateSnapshot.Files.Count + $probeFiles.Count)) {
        throw 'The candidate runtime contains unreported extra files.'
    }
    $runtimeMonitor = [HorrorHarbor.Quality.DirectoryChangeMonitor]::new(
        $runtimeDirectory
    )
    $runtimeLocks = Open-InventoryLocks -Directory $runtimeDirectory
    Assert-InventoryEqual `
        -Expected $runtimeCompleteBefore `
        -Actual (Get-CanonicalInventory -Directory $runtimeDirectory) `
        -Purpose 'Locked runtime snapshot'

    $probeOutput = @(& dotnet (Join-Path $runtimeDirectory $probeAssembly) `
            $appHash `
            $dataHash `
            $nativeHash 2>&1)
    if ($LASTEXITCODE -ne 0) { throw 'Exact-payload encrypted-storage probe failed.' }
    try {
        $probeResult = $probeOutput[-1] | ConvertFrom-Json
    }
    catch {
        throw 'Exact-payload encrypted-storage probe returned malformed evidence.'
    }
    if ($probeResult.policy -ne 'HorrorHarbor exact published storage runtime v1' -or
        $probeResult.status -ne 'PASS' -or
        $probeResult.checks -ne 12) {
        throw 'Exact-payload encrypted-storage probe returned unexpected evidence.'
    }
    Assert-InventoryEqual `
        -Expected $candidateSnapshot `
        -Actual (Get-CanonicalInventory -Directory $candidateDirectory) `
        -Purpose 'Candidate snapshot after execution'
    Assert-InventoryEqual `
        -Expected $runtimeCompleteBefore `
        -Actual (Get-CanonicalInventory -Directory $runtimeDirectory) `
        -Purpose 'Runtime snapshot after execution'
    Start-Sleep -Milliseconds 100
    if ($candidateMonitor.ChangeCount -ne 0 -or
        $runtimeMonitor.ChangeCount -ne 0) {
        throw 'The exact candidate or runtime directory changed during verification.'
    }

    $nativeVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo(
        (Join-Path $candidateDirectory 'sqlite3mc.dll')
    )
    $reportDirectory = Split-Path -Parent $resolvedReport
    New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
    [ordered]@{
        policy = 'HorrorHarbor exact published storage runtime v1'
        status = 'PASS'
        checks = 12
        syntheticFixtures = 2
        defaultUserStorageAccessed = $false
        plaintextMigrationVerified = $true
        keylessReadRejected = $true
        databaseArtifactsOpaque = $true
        candidatePayload = [ordered]@{
            files = $publishInventory.Count
            inventorySha256 = $inventoryHash
            appSha256 = $appHash
            dataSha256 = $dataHash
            nativeProviderSha256 = $nativeHash
        }
        nativeProvider = [ordered]@{
            fileVersion = $nativeVersion.FileVersion
            productVersion = $nativeVersion.ProductVersion
            productName = $nativeVersion.ProductName
            sha256 = $nativeHash
        }
        managedProviderAssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName(
            (Join-Path $candidateDirectory 'SQLite3MC.PCLRaw.provider.dll')
        ).Version.ToString()
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resolvedReport -Encoding utf8
    Assert-InventoryEqual `
        -Expected $sourceBefore `
        -Actual (Get-CanonicalInventory -Directory $resolvedPublish) `
        -Purpose 'Published source through report publication'
    Start-Sleep -Milliseconds 100
    if ($sourceMonitor.ChangeCount -ne 0) {
        throw 'The original published source changed during exact-payload verification.'
    }
    $verificationSucceeded = $true
}
finally {
    if ($runtimeMonitor) {
        $runtimeMonitor.Dispose()
    }
    if ($candidateMonitor) {
        $candidateMonitor.Dispose()
    }
    if ($sourceMonitor) {
        $sourceMonitor.Dispose()
    }
    if ($runtimeLocks) {
        foreach ($lock in $runtimeLocks) {
            $lock.Dispose()
        }
    }
    if ($candidateLocks) {
        foreach ($lock in $candidateLocks) {
            $lock.Dispose()
        }
    }
    if ($sourceLocks) {
        foreach ($lock in $sourceLocks) {
            $lock.Dispose()
        }
    }
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
    if (-not $verificationSucceeded -and
        (Test-Path -LiteralPath $resolvedReport)) {
        Remove-Item -LiteralPath $resolvedReport -Force
    }
}

Write-Host 'Published encrypted-storage runtime verification PASS' -ForegroundColor Green
