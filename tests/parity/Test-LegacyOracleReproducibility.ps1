[CmdletBinding()]
param(
    [string] $LegacyRoot = (
        Join-Path $PSScriptRoot '..\..\artifacts\legacy-reference'),

    [string] $LazarusRoot = 'C:\lazarus',

    [string] $CaseId = 'contest.exchange-shapes'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'ParityPathSafety.ps1')

$repositoryRoot = (Resolve-Path (
    Join-Path $PSScriptRoot '..\..')).Path
$outputRoot = Assert-SafeDescendantPath `
    -Root $repositoryRoot `
    -Candidate (Join-Path (
        $repositoryRoot
    ) 'artifacts\legacy-oracle') `
    -Description 'Legacy oracle reproducibility output'
$savedEnvironment = @{
    LegacyRoot = $env:MORSE_RUNNER_LEGACY_ROOT
    Oracle = $env:MORSE_RUNNER_LEGACY_ORACLE
    Provenance = $env:MORSE_RUNNER_LEGACY_PROVENANCE
    OracleSha256 = $env:MORSE_RUNNER_LEGACY_ORACLE_SHA256
    ProvenanceSha256 = $env:MORSE_RUNNER_LEGACY_PROVENANCE_SHA256
    Registry = $env:MORSE_RUNNER_LEGACY_ORACLE_REGISTRY
    RegistrySha256 =
        $env:MORSE_RUNNER_LEGACY_ORACLE_REGISTRY_SHA256
}

try {
    $first = & (Join-Path (
        $PSScriptRoot) 'Build-LegacyOracle.ps1') `
        -LegacyRoot $LegacyRoot `
        -LazarusRoot $LazarusRoot `
        -OutputRoot $outputRoot `
        -CaseId $CaseId `
        -PassThru
    Start-Sleep -Milliseconds 1100
    $second = & (Join-Path (
        $PSScriptRoot) 'Build-LegacyOracle.ps1') `
        -LegacyRoot $LegacyRoot `
        -LazarusRoot $LazarusRoot `
        -OutputRoot $outputRoot `
        -CaseId $CaseId `
        -PassThru

    $firstEntries = @($first.entries)
    $secondEntries = @($second.entries)
    if ($firstEntries.Count -ne 1 -or
        $secondEntries.Count -ne 1) {
        throw (
            'Legacy oracle reproducibility requires exactly one registry ' +
            'entry from each isolated build.')
    }
    $firstEntry = $firstEntries[0]
    $secondEntry = $secondEntries[0]
    if ($firstEntry.versionId -cne $secondEntry.versionId) {
        throw 'Isolated builds selected different legacy oracle versions.'
    }
    $firstExecutable = Join-Path (
        $repositoryRoot) $firstEntry.executable
    $secondExecutable = Join-Path (
        $repositoryRoot) $secondEntry.executable
    if ($firstExecutable -ceq $secondExecutable) {
        throw 'Isolated builds reused the same executable path.'
    }
    $firstBytes = [IO.File]::ReadAllBytes($firstExecutable)
    $secondBytes = [IO.File]::ReadAllBytes($secondExecutable)
    $bytesMatch = [Linq.Enumerable]::SequenceEqual[byte](
        $firstBytes,
        $secondBytes)
    if (-not $bytesMatch) {
        $firstHash = (
            Get-FileHash -LiteralPath (
                $firstExecutable) -Algorithm SHA256).Hash.ToLowerInvariant()
        $secondHash = (
            Get-FileHash -LiteralPath (
                $secondExecutable) -Algorithm SHA256).Hash.ToLowerInvariant()
        throw (
            "Legacy oracle '$($firstEntry.versionId)' PE bytes are not " +
            "reproducible. First: $firstHash at " +
            "$firstExecutable. Second: $secondHash at " +
            "$secondExecutable.")
    }
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals(
            [string] $firstEntry.executableSha256,
            [string] $secondEntry.executableSha256)) {
        throw (
            'Legacy oracle executable hashes differ despite equal PE ' +
            'bytes.')
    }
    Write-Host (
        "Legacy oracle '$($firstEntry.versionId)' reproduced exactly: " +
        $firstEntry.executableSha256)
} finally {
    $env:MORSE_RUNNER_LEGACY_ROOT = $savedEnvironment.LegacyRoot
    $env:MORSE_RUNNER_LEGACY_ORACLE = $savedEnvironment.Oracle
    $env:MORSE_RUNNER_LEGACY_PROVENANCE =
        $savedEnvironment.Provenance
    $env:MORSE_RUNNER_LEGACY_ORACLE_SHA256 =
        $savedEnvironment.OracleSha256
    $env:MORSE_RUNNER_LEGACY_PROVENANCE_SHA256 =
        $savedEnvironment.ProvenanceSha256
    $env:MORSE_RUNNER_LEGACY_ORACLE_REGISTRY =
        $savedEnvironment.Registry
    $env:MORSE_RUNNER_LEGACY_ORACLE_REGISTRY_SHA256 =
        $savedEnvironment.RegistrySha256
}
