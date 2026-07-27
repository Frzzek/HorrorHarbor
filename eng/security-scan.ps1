[CmdletBinding()]
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet restore HorrorHarbor.sln `
        -p:NuGetAudit=true `
        -p:NuGetAuditMode=all `
        -p:NuGetAuditLevel=moderate `
        -p:TreatWarningsAsErrors=true
    if ($LASTEXITCODE -ne 0) { throw 'NuGet restore and vulnerability audit failed.' }

    $patterns = @(
        '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----',
        '(?i)(api[_-]?key|client[_-]?secret|access[_-]?token)\s*[:=]\s*["''][^"'']{12,}["'']',
        '(?i)password\s*[:=]\s*["''][^"'']+["'']',
        '(?i)authorization\s*[:=]\s*["''](?:bearer|basic)\s+[A-Za-z0-9._~+/=-]{12,}',
        '(?i)\bgh[pousr]_[A-Za-z0-9]{20,}\b',
        '(?i)\bAKIA[0-9A-Z]{16}\b',
        '(?i)\bC:\\Users\\(?!Public\\|<user>\\|USERNAME\\)[^\\\r\n]+\\'
    )

    $binaryExtensions = @(
        '.7z', '.avif', '.bmp', '.db', '.dll', '.exe', '.gif', '.ico', '.jpeg',
        '.jpg', '.pdb', '.png', '.sqlite', '.sqlite3', '.webp', '.zip'
    )
    $candidatePaths = @(
        git ls-files --cached --others --exclude-standard
    ) | Sort-Object -Unique
    if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate the candidate source tree.' }

    $files = $candidatePaths |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Where-Object { [IO.Path]::GetExtension($_).ToLowerInvariant() -notin $binaryExtensions }

    foreach ($path in $files) {
        $content = Get-Content -LiteralPath $path -Raw
        foreach ($pattern in $patterns) {
            if ($content -match $pattern) {
                throw "Potential secret or private local path detected in $path."
            }
        }
    }

    Write-Host 'Security scan PASS' -ForegroundColor Green
}
finally {
    Pop-Location
}
