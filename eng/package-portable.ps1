[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

if (-not ('HorrorHarbor.Quality.PortableExactReadLock' -as [type])) {
    Add-Type -TypeDefinition @'
using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace HorrorHarbor.Quality
{
    public sealed class PortableExactReadLock : IDisposable
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

        public PortableExactReadLock(string path, bool directory)
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
                    "Exact portable payload lock failed.",
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
                    "Exact portable payload type inspection failed.",
                    new Win32Exception(error));
            }
            bool openedDirectory =
                (attributes.FileAttributes & FileAttributeDirectory) != 0;
            if ((attributes.FileAttributes & FileAttributeReparsePoint) != 0
                || openedDirectory != directory)
            {
                _handle.Dispose();
                throw new IOException(
                    "Exact portable payload object is a reparse point or changed type.");
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
}
'@
}

$root = Split-Path -Parent $PSScriptRoot

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
        throw "Portable package path escaped its staging root: $fullPath"
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
        [string]$Directory
    )

    foreach ($childDirectory in Get-ChildItem -LiteralPath $Directory -Recurse -Directory -Force) {
        if (($childDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Portable packaging refuses a reparse-point directory: $($childDirectory.FullName)"
        }
    }

    $filesByPath = @{}
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -File -Force) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Portable packaging refuses a reparse-point file: $($file.FullName)"
        }
        $relativePath = Get-ConfinedRelativePath `
            -Directory $Directory `
            -Path $file.FullName
        if ($filesByPath.ContainsKey($relativePath)) {
            throw "Portable packaging found a duplicate canonical path: $relativePath"
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
    [pscustomobject]@{
        Files = $files
        Sha256 = Get-TextSha256 -Text $json
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
        $Expected.Sha256 -cne $Actual.Sha256) {
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

function Open-InventoryLocks {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    $locks = [Collections.Generic.List[IDisposable]]::new()
    try {
        $locks.Add([HorrorHarbor.Quality.PortableExactReadLock]::new(
                $Directory,
                $true
            ))
        foreach ($childDirectory in Get-ChildItem -LiteralPath $Directory -Recurse -Directory -Force) {
            $locks.Add([HorrorHarbor.Quality.PortableExactReadLock]::new(
                    $childDirectory.FullName,
                    $true
                ))
        }
        foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -File -Force) {
            $locks.Add([HorrorHarbor.Quality.PortableExactReadLock]::new(
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

function New-ValidatedArchive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,
        [Parameter(Mandatory = $true)]
        [string]$ExtractionDirectory,
        [Parameter(Mandatory = $true)]
        [pscustomobject]$ExpectedInventory,
        [Parameter(Mandatory = $true)]
        [scriptblock]$WriteEvidence
    )

    Add-Type -AssemblyName System.IO.Compression
    $archiveStream = [IO.FileStream]::new(
        $ArchivePath,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::ReadWrite,
        [IO.FileShare]::Read,
        65536,
        [IO.FileOptions]::WriteThrough
    )
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $archiveStream,
            [IO.Compression.ZipArchiveMode]::Create,
            $true
        )
        try {
            foreach ($file in $ExpectedInventory.Files) {
                $entry = $archive.CreateEntry(
                    $file.path,
                    [IO.Compression.CompressionLevel]::Optimal
                )
                $entry.LastWriteTime = [DateTimeOffset]::new(
                    1980,
                    1,
                    1,
                    0,
                    0,
                    0,
                    [TimeSpan]::Zero
                )
                $entryStream = $entry.Open()
                try {
                    $sourceStream = [IO.FileStream]::new(
                        (Join-Path $SourceDirectory $file.path),
                        [IO.FileMode]::Open,
                        [IO.FileAccess]::Read,
                        [IO.FileShare]::Read,
                        65536,
                        [IO.FileOptions]::SequentialScan
                    )
                    try {
                        $sourceStream.CopyTo($entryStream)
                    }
                    finally {
                        $sourceStream.Dispose()
                    }
                }
                finally {
                    $entryStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
        $archiveStream.Flush($true)

        $archiveStream.Position = 0
        $reader = [IO.Compression.ZipArchive]::new(
            $archiveStream,
            [IO.Compression.ZipArchiveMode]::Read,
            $true
        )
        try {
            $expectedPaths = [Collections.Generic.HashSet[string]]::new(
                [StringComparer]::Ordinal
            )
            foreach ($file in $ExpectedInventory.Files) {
                [void]$expectedPaths.Add($file.path)
            }
            $observedPaths = [Collections.Generic.HashSet[string]]::new(
                [StringComparer]::Ordinal
            )
            $caseFoldedPaths = [Collections.Generic.HashSet[string]]::new(
                [StringComparer]::OrdinalIgnoreCase
            )
            foreach ($entry in $reader.Entries) {
                $entryPath = $entry.FullName.Replace('\', '/')
                $segments = @($entryPath.Split('/'))
                if ([string]::IsNullOrWhiteSpace($entryPath) -or
                    [IO.Path]::IsPathRooted($entryPath) -or
                    $entryPath.EndsWith('/') -or
                    $segments -contains '' -or
                    $segments -contains '.' -or
                    $segments -contains '..' -or
                    -not $expectedPaths.Contains($entryPath) -or
                    -not $observedPaths.Add($entryPath) -or
                    -not $caseFoldedPaths.Add($entryPath)) {
                    throw "Portable archive contains an unsafe or ambiguous entry: $entryPath"
                }

                $expectedFile = $ExpectedInventory.Files |
                    Where-Object path -CEQ $entryPath |
                    Select-Object -First 1
                if ($null -eq $expectedFile -or
                    $entry.Length -ne $expectedFile.bytes) {
                    throw "Portable archive entry length mismatch: $entryPath"
                }

                $destinationPath = Join-Path $ExtractionDirectory $entryPath
                $destinationDirectory = Split-Path -Parent $destinationPath
                New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
                $entryStream = $entry.Open()
                try {
                    $destinationStream = [IO.FileStream]::new(
                        $destinationPath,
                        [IO.FileMode]::CreateNew,
                        [IO.FileAccess]::Write,
                        [IO.FileShare]::None
                    )
                    try {
                        $entryStream.CopyTo($destinationStream)
                    }
                    finally {
                        $destinationStream.Dispose()
                    }
                }
                finally {
                    $entryStream.Dispose()
                }
            }
            if ($observedPaths.Count -ne $expectedPaths.Count) {
                throw 'Portable archive is missing expected payload entries.'
            }
        }
        finally {
            $reader.Dispose()
        }

        Assert-InventoryEqual `
            -Expected $ExpectedInventory `
            -Actual (Get-CanonicalInventory -Directory $ExtractionDirectory) `
            -Purpose 'Extracted portable package'

        $archiveStream.Position = 0
        $sha256 = [Security.Cryptography.SHA256]::Create()
        try {
            $archiveHash = ([BitConverter]::ToString(
                    $sha256.ComputeHash($archiveStream)
                )).Replace('-', '')
            & $WriteEvidence $archiveHash
            return $archiveHash
        }
        finally {
            $sha256.Dispose()
        }
    }
    catch {
        $archiveStream.Dispose()
        Remove-Item -LiteralPath $ArchivePath -Force -ErrorAction SilentlyContinue
        throw
    }
    finally {
        $archiveStream.Dispose()
    }
}

function Invoke-ArchiveSelfTest {
    $selfTestRoot = Join-Path $root (
        'artifacts\validation\portable-archive-self-test-' +
        [Guid]::NewGuid().ToString('N')
    )
    $source = Join-Path $selfTestRoot 'source'
    $extraction = Join-Path $selfTestRoot 'extraction'
    $archive = Join-Path $selfTestRoot 'candidate.zip'
    $evidence = Join-Path $selfTestRoot 'evidence.txt'
    $locks = $null
    try {
        New-Item -ItemType Directory -Force `
            -Path (Join-Path $source 'nested'), $extraction | Out-Null
        [IO.File]::WriteAllBytes(
            (Join-Path $source 'alpha.bin'),
            [byte[]](0, 1, 2, 3, 255)
        )
        [IO.File]::WriteAllText(
            (Join-Path $source 'nested\bravo.txt'),
            'HorrorHarbor synthetic portable archive fixture',
            [Text.Encoding]::UTF8
        )
        $inventory = Get-CanonicalInventory -Directory $source
        $locks = Open-InventoryLocks -Directory $source
        $evidenceWriter = {
            param([string]$validatedArchiveHash)
            Set-Content -LiteralPath $evidence `
                -Value $validatedArchiveHash `
                -Encoding ascii
        }
        $archiveHash = New-ValidatedArchive `
            -SourceDirectory $source `
            -ArchivePath $archive `
            -ExtractionDirectory $extraction `
            -ExpectedInventory $inventory `
            -WriteEvidence $evidenceWriter
        $evidenceHash = (Get-Content -LiteralPath $evidence -Raw).Trim()
        $observedHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
        if ($archiveHash -cne $evidenceHash -or
            $archiveHash -cne $observedHash) {
            throw 'Portable archive self-test evidence did not bind to the validated archive.'
        }
        Assert-InventoryEqual `
            -Expected $inventory `
            -Actual (Get-CanonicalInventory -Directory $extraction) `
            -Purpose 'Portable archive self-test extraction'
        Write-Host 'Portable exact-archive self-test PASS' -ForegroundColor Green
    }
    finally {
        if ($locks) {
            foreach ($lock in $locks) {
                $lock.Dispose()
            }
        }
        if (Test-Path -LiteralPath $selfTestRoot) {
            Remove-Item -LiteralPath $selfTestRoot -Recurse -Force
        }
    }
}

if ($SelfTest) {
    Invoke-ArchiveSelfTest
    return
}

& (Join-Path $PSScriptRoot 'build.ps1') -Runtime $Runtime -Configuration $Configuration

$publish = Join-Path $root "artifacts/publish/$Runtime"
$packageDir = Join-Path $root 'artifacts/packages'
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
$zip = Join-Path $packageDir "HorrorHarbor-Alpha2-$Runtime.zip"
$validationReport = Join-Path $root "artifacts/validation/published-storage-runtime-$Runtime.json"
if (-not (Test-Path -LiteralPath $validationReport -PathType Leaf)) {
    throw 'The exact-payload validation report is missing.'
}
$verified = Get-Content -LiteralPath $validationReport -Raw | ConvertFrom-Json
$staging = Join-Path $root (
    'artifacts/package-staging-' + [Guid]::NewGuid().ToString('N')
)
$extraction = Join-Path $root (
    'artifacts/package-extraction-' + [Guid]::NewGuid().ToString('N')
)
$stagingLocks = $null
if (Test-Path $zip) { Remove-Item $zip -Force }
try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    Copy-Item -Path (Join-Path $publish '*') -Destination $staging -Recurse -Force
    $inventory = Get-CanonicalInventory -Directory $staging
    if ($inventory.Files.Count -ne $verified.candidatePayload.files -or
        $inventory.Sha256 -cne $verified.candidatePayload.inventorySha256) {
        throw 'The portable package snapshot does not match exact-payload validation evidence.'
    }
    $stagingLocks = Open-InventoryLocks -Directory $staging
    Assert-InventoryEqual `
        -Expected $inventory `
        -Actual (Get-CanonicalInventory -Directory $staging) `
        -Purpose 'Locked portable package staging'
    New-Item -ItemType Directory -Path $extraction | Out-Null
    $evidenceWriter = {
        param([string]$validatedArchiveHash)

        $checksum = Join-Path $packageDir 'SHA256SUMS.txt'
        "$validatedArchiveHash  $([System.IO.Path]::GetFileName($zip))" |
            Set-Content -Path $checksum -Encoding ascii
        $manifest = [ordered]@{
            product = 'HorrorHarbor'
            release = 'Alpha 2'
            runtime = $Runtime
            configuration = $Configuration
            package = [System.IO.Path]::GetFileName($zip)
            sha256 = $validatedArchiveHash
            dotnetSdk = (& dotnet --version).Trim()
            sourceDateEpoch = $env:SOURCE_DATE_EPOCH
        } | ConvertTo-Json
        $manifest |
            Set-Content -Path (Join-Path $packageDir 'release-manifest.json') -Encoding utf8
    }
    $archiveHash = New-ValidatedArchive `
        -SourceDirectory $staging `
        -ArchivePath $zip `
        -ExtractionDirectory $extraction `
        -ExpectedInventory $inventory `
        -WriteEvidence $evidenceWriter
    Assert-InventoryEqual `
        -Expected $inventory `
        -Actual (Get-CanonicalInventory -Directory $staging) `
        -Purpose 'Post-compression portable package staging'

}
finally {
    if ($stagingLocks) {
        foreach ($lock in $stagingLocks) {
            $lock.Dispose()
        }
    }
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
    if (Test-Path -LiteralPath $extraction) {
        Remove-Item -LiteralPath $extraction -Recurse -Force
    }
}
Write-Host "SHA256: $archiveHash"
Write-Host "Portable package created: $zip" -ForegroundColor Green
