[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $RegistryPath,

    [Parameter(Mandatory)]
    [string] $RegistrySha256,

    [string] $RepositoryRoot = (
        Resolve-Path (Join-Path $PSScriptRoot '..\..')
    ).Path,

    [switch] $PassThru
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'ParityPathSafety.ps1')

$repositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$oracleArtifactsRoot = Assert-SafeDescendantPath `
    -Root $repositoryRoot `
    -Candidate (Join-Path $repositoryRoot 'artifacts\legacy-oracle') `
    -Description 'Legacy oracle artifacts'
$registriesRoot = Assert-SafeDescendantPath `
    -Root $repositoryRoot `
    -Candidate (Join-Path $oracleArtifactsRoot 'registries') `
    -Description 'Legacy oracle registries'

if ($RegistrySha256 -cnotmatch '^[0-9a-f]{64}$') {
    throw 'Legacy oracle registry SHA-256 must be lowercase hexadecimal.'
}
$resolvedRegistryPath = Assert-SafeDescendantPath `
    -Root $registriesRoot `
    -Candidate $RegistryPath `
    -Description 'Legacy oracle registry'
if (-not (Test-Path -LiteralPath $resolvedRegistryPath -PathType Leaf)) {
    throw "Legacy oracle registry does not exist: $resolvedRegistryPath"
}
if ([IO.Path]::GetFileName($resolvedRegistryPath) -cne
    "$RegistrySha256.json") {
    throw (
        'Legacy oracle registry filename must equal its declared ' +
        'content SHA-256.')
}
$actualRegistryHash = (
    Get-FileHash -LiteralPath (
        $resolvedRegistryPath) -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualRegistryHash -cne $RegistrySha256) {
    throw 'Legacy oracle registry raw SHA-256 does not match its declaration.'
}

$document = Get-Content -LiteralPath $resolvedRegistryPath -Raw |
    ConvertFrom-Json
$rootFields = @($document.PSObject.Properties.Name)
if ($rootFields.Count -ne 2 -or
    -not ($rootFields -ccontains 'schemaVersion') -or
    -not ($rootFields -ccontains 'entries') -or
    [int] $document.schemaVersion -ne 1) {
    throw 'Legacy oracle registry must have the exact schema-v1 root.'
}
$entries = @($document.entries)
if ($entries.Count -eq 0) {
    throw 'Legacy oracle registry must contain at least one entry.'
}
$requiredEntryFields = [string[]] @(
    'adapterId'
    'versionId'
    'source'
    'sourceSha256'
    'buildRecipe'
    'buildRecipeSha256'
    'executable'
    'executableSha256'
    'provenance'
    'provenanceSha256'
)
$versions = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$artifactPaths = [Collections.Generic.List[string]]::new()
foreach ($entry in $entries) {
    $entryFields = @($entry.PSObject.Properties.Name)
    if ($entryFields.Count -ne $requiredEntryFields.Count -or
        @(
            $requiredEntryFields |
                Where-Object { -not ($entryFields -ccontains $_) }
        ).Count -ne 0) {
        throw 'Legacy oracle registry entry has an unexpected schema.'
    }
    $versionId = [string] $entry.versionId
    if ([string]::IsNullOrWhiteSpace($versionId) -or
        -not $versions.Add($versionId)) {
        throw 'Legacy oracle registry version IDs must be nonempty and unique.'
    }
    foreach ($binding in @(
            @(
                'executable',
                'executableSha256',
                'Legacy oracle executable'),
            @(
                'provenance',
                'provenanceSha256',
                'Legacy oracle provenance'))) {
        $pathProperty = [string] $binding[0]
        $hashProperty = [string] $binding[1]
        $description = "$($binding[2]) '$versionId'"
        $relativePath = [string] $entry.$pathProperty
        $declaredHash = [string] $entry.$hashProperty
        $segments = [string[]] $relativePath.Split('/')
        if (-not $relativePath.StartsWith(
                'artifacts/legacy-oracle/',
                [StringComparison]::Ordinal) -or
            $relativePath.Contains('\') -or
            @($segments | Where-Object {
                    $_ -in @('', '.', '..')
                }).Count -ne 0) {
            throw (
                "$description must use an exact repository-relative / path " +
                'below artifacts/legacy-oracle.')
        }
        if ($declaredHash -cnotmatch '^[0-9a-f]{64}$') {
            throw "$description SHA-256 must be lowercase hexadecimal."
        }
        $candidate = Join-Path (
            $repositoryRoot) $relativePath.Replace(
                '/', [IO.Path]::DirectorySeparatorChar)
        $artifactPath = Assert-SafeDescendantPath `
            -Root $oracleArtifactsRoot `
            -Candidate $candidate `
            -Description $description
        if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
            throw "$description is missing: $artifactPath"
        }
        $actualHash = (
            Get-FileHash -LiteralPath (
                $artifactPath) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -cne $declaredHash) {
            throw "$description SHA-256 does not match its registry binding."
        }
        $artifactPaths.Add($artifactPath)
    }
}

Write-Host (
    "Validated $($entries.Count) legacy oracle registry entry artifact " +
    'set.')
if ($PassThru) {
    [pscustomobject]@{
        registry = $resolvedRegistryPath
        registrySha256 = $actualRegistryHash
        artifacts = [string[]] $artifactPaths
    }
}
