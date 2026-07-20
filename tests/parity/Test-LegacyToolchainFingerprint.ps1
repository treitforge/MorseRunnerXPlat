[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifactsRoot = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\toolchain-fingerprint-test'))
$testRoot = [IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot ([guid]::NewGuid().ToString('N'))))
$allowedPrefix = $artifactsRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar
) + [IO.Path]::DirectorySeparatorChar
if (-not $testRoot.StartsWith(
        $allowedPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Fingerprint test root escapes the artifact directory: $testRoot"
}

$fingerprintScript = Join-Path (
    $PSScriptRoot) 'Get-LegacyToolchainFingerprint.ps1'

function Get-TestFingerprint {
    param(
        [string[]] $Roots = @('root-a', 'root-b/file-c.txt')
    )

    return & $fingerprintScript `
        -LazarusRoot $testRoot `
        -RelativeRoots $Roots |
        ConvertFrom-Json
}

function Assert-Equal {
    param(
        [Parameter(Mandatory)]
        [object] $Expected,

        [Parameter(Mandatory)]
        [object] $Actual,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if (-not [Object]::Equals($Expected, $Actual)) {
        throw "$Description mismatch. Expected '$Expected', got '$Actual'."
    }
}

function Assert-NotEqual {
    param(
        [Parameter(Mandatory)]
        [object] $Left,

        [Parameter(Mandatory)]
        [object] $Right,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if ([Object]::Equals($Left, $Right)) {
        throw "$Description unexpectedly matched '$Left'."
    }
}

try {
    New-Item -ItemType Directory -Force -Path (
        Join-Path $testRoot 'root-a\nested'
    ), (
        Join-Path $testRoot 'root-b'
    ) | Out-Null
    Set-Content -LiteralPath (
        Join-Path $testRoot 'root-a\file-a.txt'
    ) -Value 'A' -NoNewline -Encoding utf8
    Set-Content -LiteralPath (
        Join-Path $testRoot 'root-a\nested\file-b.txt'
    ) -Value 'BB' -NoNewline -Encoding utf8
    Set-Content -LiteralPath (
        Join-Path $testRoot 'root-b\file-c.txt'
    ) -Value 'CCC' -NoNewline -Encoding utf8

    $baseline = Get-TestFingerprint
    $repeat = Get-TestFingerprint
    Assert-Equal `
        -Expected $baseline.aggregateSha256 `
        -Actual $repeat.aggregateSha256 `
        -Description 'Repeated aggregate'
    Assert-Equal `
        -Expected ([long] 3) `
        -Actual ([long] $baseline.fileCount) `
        -Description 'Baseline file count'
    Assert-Equal `
        -Expected ([long] 6) `
        -Actual ([long] $baseline.byteCount) `
        -Description 'Baseline byte count'

    $addedPath = Join-Path $testRoot 'root-a\added.txt'
    Set-Content -LiteralPath $addedPath `
        -Value 'added' `
        -NoNewline `
        -Encoding utf8
    $withAddition = Get-TestFingerprint
    Assert-Equal `
        -Expected ([long] 4) `
        -Actual ([long] $withAddition.fileCount) `
        -Description 'Added file count'
    Assert-NotEqual `
        -Left $baseline.aggregateSha256 `
        -Right $withAddition.aggregateSha256 `
        -Description 'Added-file aggregate'
    Remove-Item -LiteralPath $addedPath -Force

    $changedPath = Join-Path $testRoot 'root-a\file-a.txt'
    Set-Content -LiteralPath $changedPath `
        -Value 'Z' `
        -NoNewline `
        -Encoding utf8
    $withChange = Get-TestFingerprint
    Assert-Equal `
        -Expected ([long] $baseline.fileCount) `
        -Actual ([long] $withChange.fileCount) `
        -Description 'Changed file count'
    Assert-Equal `
        -Expected ([long] $baseline.byteCount) `
        -Actual ([long] $withChange.byteCount) `
        -Description 'Changed byte count'
    Assert-NotEqual `
        -Left $baseline.aggregateSha256 `
        -Right $withChange.aggregateSha256 `
        -Description 'Changed-file aggregate'
    Set-Content -LiteralPath $changedPath `
        -Value 'A' `
        -NoNewline `
        -Encoding utf8

    $removedPath = Join-Path $testRoot 'root-a\nested\file-b.txt'
    Remove-Item -LiteralPath $removedPath -Force
    $withRemoval = Get-TestFingerprint
    Assert-Equal `
        -Expected ([long] 2) `
        -Actual ([long] $withRemoval.fileCount) `
        -Description 'Removed file count'
    Assert-NotEqual `
        -Left $baseline.aggregateSha256 `
        -Right $withRemoval.aggregateSha256 `
        -Description 'Removed-file aggregate'
    Set-Content -LiteralPath $removedPath `
        -Value 'BB' `
        -NoNewline `
        -Encoding utf8

    $restored = Get-TestFingerprint
    Assert-Equal `
        -Expected $baseline.aggregateSha256 `
        -Actual $restored.aggregateSha256 `
        -Description 'Restored aggregate'

    try {
        Get-TestFingerprint -Roots @('../outside') | Out-Null
        throw 'Escaping root unexpectedly succeeded.'
    } catch {
        if ($_.Exception.Message -eq 'Escaping root unexpectedly succeeded.') {
            throw
        }
    }

    try {
        Get-TestFingerprint -Roots @('root-a', 'root-a/nested') | Out-Null
        throw 'Overlapping roots unexpectedly succeeded.'
    } catch {
        if ($_.Exception.Message -eq 'Overlapping roots unexpectedly succeeded.') {
            throw
        }
    }

    $incompleteRoot = Join-Path $testRoot 'incomplete-installation'
    New-Item -ItemType Directory -Force -Path $incompleteRoot | Out-Null
    Set-Content -LiteralPath (
        Join-Path $incompleteRoot 'user-file.txt'
    ) -Value 'preserve' -NoNewline -Encoding utf8
    try {
        & (Join-Path $PSScriptRoot 'Install-LegacyOracleToolchain.ps1') `
            -LazarusRoot $incompleteRoot
        throw 'Incomplete installation unexpectedly triggered installation.'
    } catch {
        if ($_.Exception.Message -eq
            'Incomplete installation unexpectedly triggered installation.') {
            throw
        }
        if ($_.Exception.Message -notmatch
            'Refusing to modify or reinstall') {
            throw
        }
    }
    Assert-Equal `
        -Expected 'preserve' `
        -Actual (Get-Content -LiteralPath (
            Join-Path $incompleteRoot 'user-file.txt'
        ) -Raw) `
        -Description 'Existing installation content'

    Write-Host 'Legacy toolchain fingerprint tests passed.'
} finally {
    if (Test-Path -LiteralPath $testRoot -PathType Container) {
        $resolvedTestRoot = (Resolve-Path -LiteralPath $testRoot).Path
        if (-not $resolvedTestRoot.StartsWith(
                $allowedPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unsafe fingerprint test path: $resolvedTestRoot"
        }
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
