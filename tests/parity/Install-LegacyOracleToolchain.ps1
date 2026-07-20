[CmdletBinding()]
param(
    [string] $LazarusRoot = 'C:\lazarus',

    [switch] $PassThru
)

$ErrorActionPreference = 'Stop'
$reference = Get-Content -LiteralPath (
    Join-Path $PSScriptRoot 'legacy-reference.json'
) -Raw | ConvertFrom-Json

function Test-PathContainedBy {
    param(
        [Parameter(Mandatory)]
        [string] $Parent,

        [Parameter(Mandatory)]
        [string] $Candidate
    )

    $parentPath = [IO.Path]::GetFullPath($Parent)
    $candidatePath = [IO.Path]::GetFullPath($Candidate)
    $parentPrefix = $parentPath.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    ) + [IO.Path]::DirectorySeparatorChar

    return $candidatePath.StartsWith(
        $parentPrefix,
        [StringComparison]::OrdinalIgnoreCase)
}

function Assert-ExactStringArray {
    param(
        [Parameter(Mandatory)]
        [object[]] $Expected,

        [Parameter(Mandatory)]
        [object[]] $Actual,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if ($Expected.Count -ne $Actual.Count) {
        throw "$Description count mismatch: expected $($Expected.Count), got $($Actual.Count)."
    }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if (-not [StringComparer]::Ordinal.Equals(
                [string] $Expected[$index],
                [string] $Actual[$index])) {
            throw "$Description mismatch at index $index."
        }
    }
}

$rootPath = [IO.Path]::GetFullPath($LazarusRoot)
$volumeRoot = [IO.Path]::GetPathRoot($rootPath)
if ([String]::IsNullOrWhiteSpace($volumeRoot) -or
    [IO.Path]::TrimEndingDirectorySeparator($rootPath) -eq
        [IO.Path]::TrimEndingDirectorySeparator($volumeRoot)) {
    throw "Lazarus root cannot be a filesystem root: $rootPath"
}
if (Test-Path -LiteralPath $rootPath) {
    $rootItem = Get-Item -LiteralPath $rootPath -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Lazarus root cannot be a reparse point: $rootPath"
    }
}

$compiler = Join-Path $rootPath 'fpc\3.2.2\bin\x86_64-win64\fpc.exe'
$backendCompiler = Join-Path (
    Split-Path -Parent $compiler) 'ppcx64.exe'
$lazbuild = Join-Path $rootPath 'lazbuild.exe'
foreach ($path in @($compiler, $backendCompiler, $lazbuild)) {
    if (-not (Test-PathContainedBy -Parent $rootPath -Candidate $path)) {
        throw "Pinned legacy toolchain path escapes the Lazarus root: $path"
    }
}

if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    if (Test-Path -LiteralPath $rootPath) {
        if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) {
            throw "Lazarus root exists but is not a directory: $rootPath"
        }

        $existingItem = Get-ChildItem -LiteralPath $rootPath -Force |
            Select-Object -First 1
        if ($null -ne $existingItem) {
            throw (
                "Existing Lazarus root is incomplete. Refusing to modify " +
                "or reinstall it: $rootPath")
        }
    }

    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $installerPath = Join-Path $temporaryRoot (
        'lazarus-4.6-fpc-3.2.2-win64-' + [guid]::NewGuid() + '.exe')
    if (-not (Test-PathContainedBy `
            -Parent $temporaryRoot `
            -Candidate $installerPath)) {
        throw "Installer path escapes the temporary directory: $installerPath"
    }

    try {
        Invoke-WebRequest -Uri $reference.toolchain.installer `
            -OutFile $installerPath
        $installerHash = (
            Get-FileHash -LiteralPath (
                $installerPath) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($installerHash -cne $reference.toolchain.installerSha256) {
            throw "Lazarus installer hash mismatch: $installerHash"
        }

        $signature = Get-AuthenticodeSignature -LiteralPath $installerPath
        if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid) {
            throw "Lazarus installer signature is not valid: $($signature.Status)"
        }
        if ($signature.SignerCertificate.Subject -notmatch
            'Programming Free Pascal.*Lazarus Foundation') {
            throw "Unexpected Lazarus installer signer: $($signature.SignerCertificate.Subject)"
        }

        $process = Start-Process -FilePath $installerPath `
            -ArgumentList @(
                '/VERYSILENT',
                '/SUPPRESSMSGBOXES',
                '/NORESTART',
                '/SP-',
                "/DIR=$rootPath"
            ) `
            -Wait `
            -PassThru `
            -WindowStyle Hidden
        if ($process.ExitCode -ne 0) {
            throw "Lazarus installer failed with exit code $($process.ExitCode)."
        }
    } finally {
        if (Test-Path -LiteralPath $installerPath -PathType Leaf) {
            Remove-Item -LiteralPath $installerPath -Force
        }
    }
}

foreach ($path in @($compiler, $backendCompiler, $lazbuild)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Pinned legacy toolchain file not found: $path"
    }
}

$fpcVersion = (& $compiler -iV).Trim()
$targetCpu = (& $compiler -iTP).Trim()
$targetOs = (& $compiler -iTO).Trim()
$lazarusVersion = (& $lazbuild --version).Trim()
if ($fpcVersion -cne $reference.toolchain.fpcVersion) {
    throw "Unexpected FPC version: $fpcVersion"
}
if ($targetCpu -cne $reference.toolchain.targetCpu) {
    throw "Unexpected FPC target CPU: $targetCpu"
}
if ($targetOs -cne $reference.toolchain.targetOs) {
    throw "Unexpected FPC target OS: $targetOs"
}
if ($lazarusVersion -cne $reference.toolchain.lazarusVersion) {
    throw "Unexpected Lazarus version: $lazarusVersion"
}

$compilerHash = (
    Get-FileHash -LiteralPath (
        $compiler) -Algorithm SHA256).Hash.ToLowerInvariant()
$backendHash = (
    Get-FileHash -LiteralPath (
        $backendCompiler) -Algorithm SHA256).Hash.ToLowerInvariant()
$lazbuildHash = (
    Get-FileHash -LiteralPath (
        $lazbuild) -Algorithm SHA256).Hash.ToLowerInvariant()
if ($compilerHash -cne $reference.toolchain.compilerSha256) {
    throw "FPC compiler hash mismatch: $compilerHash"
}
if ($backendHash -cne $reference.toolchain.backendCompilerSha256) {
    throw "FPC backend compiler hash mismatch: $backendHash"
}
if ($lazbuildHash -cne $reference.toolchain.lazbuildSha256) {
    throw "Lazarus build-tool hash mismatch: $lazbuildHash"
}

$fingerprintScript = Join-Path (
    $PSScriptRoot) 'Get-LegacyToolchainFingerprint.ps1'
$actualFingerprint = & $fingerprintScript `
    -LazarusRoot $rootPath `
    -RelativeRoots @($reference.toolchain.fingerprint.roots) |
    ConvertFrom-Json
$expectedFingerprint = $reference.toolchain.fingerprint
if ($actualFingerprint.schemaVersion -ne $expectedFingerprint.schemaVersion) {
    throw "Legacy toolchain fingerprint schema mismatch."
}
if ($actualFingerprint.canonicalization -cne
    $expectedFingerprint.canonicalization) {
    throw "Legacy toolchain fingerprint canonicalization mismatch."
}
Assert-ExactStringArray `
    -Expected @($expectedFingerprint.roots) `
    -Actual @($actualFingerprint.roots) `
    -Description 'Legacy toolchain fingerprint roots'
if ($actualFingerprint.aggregateSha256 -cne
    $expectedFingerprint.aggregateSha256) {
    throw (
        "Legacy toolchain aggregate hash mismatch: " +
        $actualFingerprint.aggregateSha256)
}
if ([long] $actualFingerprint.fileCount -ne
    [long] $expectedFingerprint.fileCount) {
    throw (
        "Legacy toolchain file-count mismatch: " +
        $actualFingerprint.fileCount)
}
if ([long] $actualFingerprint.byteCount -ne
    [long] $expectedFingerprint.byteCount) {
    throw (
        "Legacy toolchain byte-count mismatch: " +
        $actualFingerprint.byteCount)
}

$verification = [pscustomobject] [ordered]@{
    root = $rootPath
    lazarusVersion = $lazarusVersion
    fpcVersion = $fpcVersion
    targetCpu = $targetCpu
    targetOs = $targetOs
    compiler = $compiler
    compilerSha256 = $compilerHash
    backendCompiler = $backendCompiler
    backendCompilerSha256 = $backendHash
    lazbuild = $lazbuild
    lazbuildSha256 = $lazbuildHash
    fingerprint = $actualFingerprint
}

Write-Host (
    "Verified Lazarus $lazarusVersion and FPC $fpcVersion at $rootPath")
Write-Host (
    "Legacy toolchain aggregate SHA-256: " +
    $actualFingerprint.aggregateSha256)
if ($PassThru) {
    $verification
}
