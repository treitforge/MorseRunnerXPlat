[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $IndexPath,

    [Parameter(Mandatory)]
    [string] $IndexSha256,

    [string] $RepositoryRoot = (
        Resolve-Path (Join-Path $PSScriptRoot '..\..')
    ).Path,

    [switch] $RequireExactClosure
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'ParityPathSafety.ps1')

$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$artifactsRoot = Assert-SafeDescendantPath `
    -Root $root `
    -Candidate (Join-Path $root 'artifacts') `
    -Description 'Full-suite package artifacts'
$indexRoot = Assert-SafeDescendantPath `
    -Root $artifactsRoot `
    -Candidate (
        Join-Path (
            $artifactsRoot
        ) 'parity-full-suite\windows-both-package-index') `
    -Description 'Full-suite package indexes'
if ($IndexSha256 -cnotmatch '^[0-9a-f]{64}$') {
    throw 'Full-suite package index SHA-256 must be lowercase hexadecimal.'
}
$resolvedIndexPath = Assert-SafeDescendantPath `
    -Root $indexRoot `
    -Candidate $IndexPath `
    -Description 'Full-suite package index'
if (-not (Test-Path -LiteralPath $resolvedIndexPath -PathType Leaf)) {
    throw "Full-suite package index does not exist: $resolvedIndexPath"
}
if ([IO.Path]::GetFileName($resolvedIndexPath) -cne
    "$IndexSha256.json") {
    throw (
        'Full-suite package index filename must equal its declared ' +
        'content SHA-256.')
}
$actualIndexHash = (
    Get-FileHash -LiteralPath (
        $resolvedIndexPath) -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualIndexHash -cne $IndexSha256) {
    throw 'Full-suite package index raw SHA-256 does not match.'
}

$document = Get-Content -LiteralPath $resolvedIndexPath -Raw |
    ConvertFrom-Json
$rootFields = @($document.PSObject.Properties.Name)
$requiredRootFields = [string[]] @(
    'schemaVersion'
    'platform'
    'target'
    'registry'
    'files'
)
if ($rootFields.Count -ne $requiredRootFields.Count -or
    @(
        $requiredRootFields |
            Where-Object { -not ($rootFields -ccontains $_) }
    ).Count -ne 0 -or
    [int] $document.schemaVersion -ne 1 -or
    [string] $document.platform -cne 'windows' -or
    [string] $document.target -cne 'Both') {
    throw 'Full-suite package index must have the exact schema-v1 contract.'
}
$registryFields = @($document.registry.PSObject.Properties.Name)
if ($registryFields.Count -ne 2 -or
    -not ($registryFields -ccontains 'path') -or
    -not ($registryFields -ccontains 'sha256')) {
    throw 'Full-suite package registry binding has an unexpected schema.'
}
$registryRelativePath = [string] $document.registry.path
$registryHash = [string] $document.registry.sha256

$files = @($document.files)
if ($files.Count -lt 11) {
    throw 'Full-suite package index has an incomplete file closure.'
}
$indexedPaths = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$orderedPaths = [Collections.Generic.List[string]]::new()
foreach ($file in $files) {
    $fields = @($file.PSObject.Properties.Name)
    if ($fields.Count -ne 2 -or
        -not ($fields -ccontains 'path') -or
        -not ($fields -ccontains 'sha256')) {
        throw 'Full-suite package file entry has an unexpected schema.'
    }
    $relativePath = [string] $file.path
    $declaredHash = [string] $file.sha256
    $segments = [string[]] $relativePath.Split('/')
    if (-not $relativePath.StartsWith(
            'artifacts/',
            [StringComparison]::Ordinal) -or
        $relativePath.Contains('\') -or
        @($segments | Where-Object {
                $_ -in @('', '.', '..')
            }).Count -ne 0 -or
        -not $indexedPaths.Add($relativePath)) {
        throw 'Full-suite package file paths must be unique and safe.'
    }
    if ($declaredHash -cnotmatch '^[0-9a-f]{64}$') {
        throw "Full-suite package file hash is invalid: $relativePath"
    }
    $path = Assert-SafeDescendantPath `
        -Root $artifactsRoot `
        -Candidate (
            Join-Path $root $relativePath.Replace(
                '/', [IO.Path]::DirectorySeparatorChar)
        ) `
        -Description "Full-suite package file '$relativePath'"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Full-suite package file is missing: $relativePath"
    }
    $actualHash = (
        Get-FileHash -LiteralPath (
            $path) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -cne $declaredHash) {
        throw "Full-suite package file hash changed: $relativePath"
    }
    $orderedPaths.Add($relativePath)
}
$sortedPaths = [string[]] @($orderedPaths)
[Array]::Sort($sortedPaths, [StringComparer]::Ordinal)
if (($orderedPaths -join "`0") -cne
    ($sortedPaths -join "`0")) {
    throw 'Full-suite package file entries must be ordinally sorted.'
}
if (-not $indexedPaths.Contains($registryRelativePath)) {
    throw 'Full-suite package registry is absent from the indexed closure.'
}
$registryPath = Join-Path (
    $root) $registryRelativePath.Replace(
        '/', [IO.Path]::DirectorySeparatorChar)
$registryArtifactSet = & (
    Join-Path (
        $PSScriptRoot
    ) 'Assert-LegacyOracleRegistryArtifactSet.ps1') `
    -RegistryPath $registryPath `
    -RegistrySha256 $registryHash `
    -RepositoryRoot $root `
    -PassThru
foreach ($registryArtifact in $registryArtifactSet.artifacts) {
    $relativePath = [IO.Path]::GetRelativePath(
        $root,
        $registryArtifact
    ).Replace('\', '/')
    if (-not $indexedPaths.Contains($relativePath)) {
        throw (
            'A registry-referenced artifact is absent from the package ' +
            "index: $relativePath")
    }
}

$requiredPaths = [string[]] @(
    'artifacts/parity/legacy.json'
    'artifacts/parity/xplat.json'
    'artifacts/parity/test-results/legacy.trx'
    'artifacts/parity/test-results/xplat.trx'
    'artifacts/parity/test-results/legacy-oracle-build-integration.trx'
)
foreach ($requiredPath in $requiredPaths) {
    if (-not $indexedPaths.Contains($requiredPath)) {
        throw "Full-suite package is missing required file: $requiredPath"
    }
}
$integrationReportPath = Join-Path (
    $root
) 'artifacts\parity\test-results\legacy-oracle-build-integration.trx'
& (
    Join-Path (
        $PSScriptRoot
    ) 'Assert-LegacyOracleBuildIntegration.ps1') `
    -TrxPath $integrationReportPath `
    -ProcessExitCode 0
$integrationExecutionPrefix =
    'artifacts/parity/executions/' +
    'legacy-oracle-build-integration/'
$integrationExecutionMatches = @(
    $indexedPaths |
        Where-Object {
            $_.StartsWith(
                $integrationExecutionPrefix,
                [StringComparison]::Ordinal) -and
            $_.EndsWith('.json', [StringComparison]::Ordinal)
        })
if ($integrationExecutionMatches.Count -ne 1) {
    throw (
        'Full-suite package must contain exactly one build-integration ' +
        'execution envelope.')
}
$integrationExecutionPath = Join-Path (
    $root) $integrationExecutionMatches[0].Replace(
        '/', [IO.Path]::DirectorySeparatorChar)
& (
    Join-Path (
        $PSScriptRoot
    ) 'Assert-LegacyOracleBuildIntegrationExecution.ps1') `
    -EnvelopePath $integrationExecutionPath `
    -IntegrationTrxPath $integrationReportPath `
    -RegistryPath $registryPath `
    -RegistrySha256 $registryHash `
    -WindowsLegacyResultPath (
        Join-Path $root 'artifacts\parity\legacy.json') `
    -RepositoryRoot $root
foreach ($targetName in @('legacy', 'xplat')) {
    $prefix = "artifacts/parity/executions/$targetName/"
    $matches = @(
        $indexedPaths |
            Where-Object {
                $_.StartsWith($prefix, [StringComparison]::Ordinal) -and
                $_.EndsWith('.json', [StringComparison]::Ordinal)
            })
    if ($matches.Count -ne 1) {
        throw (
            "Full-suite package must contain exactly one $targetName " +
            'execution envelope.')
    }
}

if ($RequireExactClosure) {
    $actualPackageFiles = @(
        Get-ChildItem -LiteralPath $root -Recurse -File |
            ForEach-Object {
                [IO.Path]::GetRelativePath(
                    $root,
                    $_.FullName
                ).Replace('\', '/')
            } |
            Where-Object {
                $_ -cne (
                    [IO.Path]::GetRelativePath(
                        $root,
                        $resolvedIndexPath
                    ).Replace('\', '/'))
            })
    if ($actualPackageFiles.Count -ne $indexedPaths.Count -or
        @(
            $actualPackageFiles |
                Where-Object { -not $indexedPaths.Contains($_) }
        ).Count -ne 0) {
        throw 'Full-suite staged package contains unindexed files.'
    }
}

Write-Host (
    "Validated full-suite package index $IndexSha256 with " +
    "$($files.Count) files.")
