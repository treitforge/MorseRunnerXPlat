[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'ParityPathSafety.ps1')

function Get-LowercaseSha256 {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    return (
        Get-FileHash -LiteralPath (
            $Path) -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Action,

        [Parameter(Mandatory)]
        [string] $Description
    )

    try {
        & $Action
    } catch {
        return
    }
    throw "Expected registry artifact-set rejection: $Description"
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$oracleArtifactsRoot = Join-Path (
    $repositoryRoot) 'artifacts\legacy-oracle'
$testId = [Guid]::NewGuid().ToString('N')
$buildRoot = Join-Path (
    $oracleArtifactsRoot) "builds\contract-test\$testId"
$binRoot = Join-Path $buildRoot 'bin'
$registriesRoot = Join-Path $oracleArtifactsRoot 'registries'
$executablePath = Join-Path $binRoot 'LegacyOracle.exe'
$provenancePath = Join-Path (
    $buildRoot) 'LegacyOracle.provenance.json'
$registryPath = $null

try {
    New-Item -ItemType Directory -Force `
        -Path $binRoot, $registriesRoot |
        Out-Null
    [IO.File]::WriteAllBytes(
        $executablePath,
        [Text.UTF8Encoding]::new($false).GetBytes('oracle'))
    [IO.File]::WriteAllText(
        $provenancePath,
        "{}`n",
        [Text.UTF8Encoding]::new($false))
    $relativeExecutable = [IO.Path]::GetRelativePath(
        $repositoryRoot,
        $executablePath
    ).Replace('\', '/')
    $relativeProvenance = [IO.Path]::GetRelativePath(
        $repositoryRoot,
        $provenancePath
    ).Replace('\', '/')
    $entry = [ordered]@{
        adapterId = 'LegacyOracleTarget'
        versionId = 'contract-test-v1'
        source = 'tests/parity/legacy-oracle/v1/LegacyOracle.lpr'
        sourceSha256 = ('1' * 64)
        buildRecipe =
            'tests/parity/legacy-oracle/v1/build-recipe.json'
        buildRecipeSha256 = ('2' * 64)
        executable = $relativeExecutable
        executableSha256 = Get-LowercaseSha256 $executablePath
        provenance = $relativeProvenance
        provenanceSha256 = Get-LowercaseSha256 $provenancePath
    }
    $registryRaw = (
        [ordered]@{
            schemaVersion = 1
            entries = @($entry)
        } | ConvertTo-Json -Depth 6
    ).Replace("`r`n", "`n").Replace("`r", "`n") + "`n"
    $registryBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        $registryRaw)
    $registryHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $registryBytes)
    ).ToLowerInvariant()
    $registryPath = Join-Path $registriesRoot "$registryHash.json"
    [IO.File]::WriteAllBytes($registryPath, $registryBytes)

    & (
        Join-Path (
            $PSScriptRoot
        ) 'Assert-LegacyOracleRegistryArtifactSet.ps1') `
        -RegistryPath $registryPath `
        -RegistrySha256 $registryHash

    [IO.File]::AppendAllText(
        $executablePath,
        'tamper',
        [Text.UTF8Encoding]::new($false))
    Assert-Rejected -Description 'tampered executable' {
        & (
            Join-Path (
                $PSScriptRoot
            ) 'Assert-LegacyOracleRegistryArtifactSet.ps1') `
            -RegistryPath $registryPath `
            -RegistrySha256 $registryHash
    }

    Remove-Item -LiteralPath $provenancePath -Force
    Assert-Rejected -Description 'missing provenance' {
        & (
            Join-Path (
                $PSScriptRoot
            ) 'Assert-LegacyOracleRegistryArtifactSet.ps1') `
            -RegistryPath $registryPath `
            -RegistrySha256 $registryHash
    }
} finally {
    if ($null -ne $registryPath -and
        (Test-Path -LiteralPath $registryPath -PathType Leaf)) {
        Remove-Item -LiteralPath $registryPath -Force
    }
    if (Test-Path -LiteralPath $buildRoot) {
        Remove-SafeDirectoryTree `
            -Root $repositoryRoot `
            -Path $buildRoot `
            -Description 'Registry artifact-set contract test root'
    }
}

Write-Host 'Legacy oracle registry artifact-set checks passed.'
