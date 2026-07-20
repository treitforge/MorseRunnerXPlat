[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageRoot,

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
$packageStagingRoot = Assert-SafeDescendantPath `
    -Root $repositoryRoot `
    -Candidate (
        Join-Path $repositoryRoot 'artifacts\parity-package-staging') `
    -Description 'Parity package staging'
New-Item -ItemType Directory -Force -Path $packageStagingRoot |
    Out-Null
$packageCandidate = if ([IO.Path]::IsPathFullyQualified($PackageRoot)) {
    $PackageRoot
} else {
    Join-Path $repositoryRoot $PackageRoot
}
$resolvedPackageRoot = Assert-SafeDescendantPath `
    -Root $packageStagingRoot `
    -Candidate $packageCandidate `
    -Description 'Parity Both package'
if (Test-Path -LiteralPath $resolvedPackageRoot) {
    Remove-SafeDirectoryTree `
        -Root $packageStagingRoot `
        -Path $resolvedPackageRoot `
        -Description 'Existing parity Both package'
}
New-Item -ItemType Directory -Force -Path $resolvedPackageRoot |
    Out-Null

function Copy-RepositoryArtifact {
    param(
        [Parameter(Mandatory)]
        [string] $Source,

        [string] $DestinationRelativePath
    )

    Copy-SafeRepositoryFileToPackage `
        -RepositoryRoot $repositoryRoot `
        -PackageRoot $resolvedPackageRoot `
        -Source $Source `
        -DestinationRelativePath $DestinationRelativePath `
        -Description 'Parity package source'
}

try {
    $validated = & (
        Join-Path (
            $PSScriptRoot
        ) 'Assert-LegacyOracleRegistryArtifactSet.ps1') `
        -RegistryPath $RegistryPath `
        -RegistrySha256 $RegistrySha256 `
        -RepositoryRoot $repositoryRoot `
        -PassThru
    Copy-RepositoryArtifact -Source $validated.registry
    foreach ($artifactPath in $validated.artifacts) {
        Copy-RepositoryArtifact -Source $artifactPath
    }
    $roleSources = [Collections.Generic.List[object]]::new()
    foreach ($binding in @(
            @('legacy-result', 'artifacts/parity/legacy.json'),
            @('xplat-result', 'artifacts/parity/xplat.json'),
            @(
                'legacy-test-report',
                'artifacts/parity/test-results/legacy.trx'),
            @(
                'xplat-test-report',
                'artifacts/parity/test-results/xplat.trx'),
            @(
                'legacy-oracle-build-integration-test-report',
                'artifacts/parity/test-results/legacy-oracle-build-integration.trx')
        )) {
        $role = [string] $binding[0]
        $relativeSource = [string] $binding[1]
        $source = Assert-SafeDescendantPath `
            -Root $repositoryRoot `
            -Candidate (Join-Path $repositoryRoot $relativeSource) `
            -Description "Windows Both $role"
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw (
                "Required Windows Both artifact is missing: " +
                $relativeSource)
        }
        if ($role -ceq
            'legacy-oracle-build-integration-test-report') {
            & (
                Join-Path (
                    $PSScriptRoot
                ) 'Assert-LegacyOracleBuildIntegration.ps1') `
                -TrxPath $source `
                -ProcessExitCode 0
        }
        $roleSources.Add([pscustomobject]@{
                role = $role
                source = $source
            })
    }
    foreach ($targetName in @('legacy', 'xplat')) {
        $executionRoot = Assert-SafeDescendantPath `
            -Root $repositoryRoot `
            -Candidate (
                Join-Path (
                    $repositoryRoot
                ) "artifacts\parity\executions\$targetName") `
            -Description "Windows Both $targetName executions"
        $envelopes = @(
            Get-ChildItem -LiteralPath $executionRoot `
                -Filter '*.json' -File)
        if ($envelopes.Count -ne 1) {
            throw (
                "Windows Both $targetName must emit exactly one " +
                'execution envelope.')
        }
        $safeEnvelope = Assert-SafeDescendantPath `
            -Root $repositoryRoot `
            -Candidate $envelopes[0].FullName `
            -Description "Windows Both $targetName execution"
        $roleSources.Add([pscustomobject]@{
                role = "$targetName-execution"
                source = $safeEnvelope
            })
    }
    $integrationExecutionRoot = Assert-SafeDescendantPath `
        -Root $repositoryRoot `
        -Candidate (
            Join-Path (
                $repositoryRoot
            ) (
                'artifacts\parity\executions\' +
                'legacy-oracle-build-integration')) `
        -Description 'Legacy oracle build-integration executions'
    $integrationEnvelopes = @(
        Get-ChildItem -LiteralPath $integrationExecutionRoot `
            -Filter '*.json' -File)
    if ($integrationEnvelopes.Count -ne 1) {
        throw (
            'Windows Both must emit exactly one Legacy oracle ' +
            'build-integration execution envelope.')
    }
    $integrationEnvelope = Assert-SafeDescendantPath `
        -Root $repositoryRoot `
        -Candidate $integrationEnvelopes[0].FullName `
        -Description 'Legacy oracle build-integration execution'
    $integrationReport = @(
        $roleSources |
            Where-Object {
                $_.role -ceq
                    'legacy-oracle-build-integration-test-report'
            })
    if ($integrationReport.Count -ne 1) {
        throw 'The package has no exact build-integration TRX binding.'
    }
    & (
        Join-Path (
            $PSScriptRoot
        ) 'Assert-LegacyOracleBuildIntegrationExecution.ps1') `
        -EnvelopePath $integrationEnvelope `
        -IntegrationTrxPath $integrationReport[0].source `
        -RegistryPath $validated.registry `
        -RegistrySha256 $RegistrySha256 `
        -WindowsLegacyResultPath (
            $roleSources |
                Where-Object { $_.role -ceq 'legacy-result' } |
                Select-Object -ExpandProperty source) `
        -RepositoryRoot $repositoryRoot
    $roleSources.Add([pscustomobject]@{
            role = 'legacy-oracle-build-integration-execution'
            source = $integrationEnvelope
        })
    foreach ($roleSource in $roleSources) {
        Copy-RepositoryArtifact -Source $roleSource.source
    }

    $packageFilesByPath =
        [Collections.Generic.Dictionary[string, object]]::new(
            [StringComparer]::Ordinal)
    foreach ($file in @(
            Get-ChildItem -LiteralPath $resolvedPackageRoot -Recurse -File
        )) {
        $relativePath = [IO.Path]::GetRelativePath(
            $resolvedPackageRoot,
            $file.FullName
        ).Replace('\', '/')
        $packageFilesByPath.Add(
            $relativePath,
            [ordered]@{
                path = $relativePath
                sha256 = (
                    Get-FileHash -LiteralPath (
                        $file.FullName) -Algorithm SHA256
                ).Hash.ToLowerInvariant()
            })
    }
    $packagePaths = [string[]] @($packageFilesByPath.Keys)
    [Array]::Sort($packagePaths, [StringComparer]::Ordinal)
    $packageFiles = @(
        foreach ($packagePath in $packagePaths) {
            $packageFilesByPath[$packagePath]
        })
    $registryRelative = [IO.Path]::GetRelativePath(
        $repositoryRoot,
        $validated.registry
    ).Replace('\', '/')
    $index = [ordered]@{
        schemaVersion = 1
        platform = 'windows'
        target = 'Both'
        registry = [ordered]@{
            path = $registryRelative
            sha256 = $RegistrySha256
        }
        files = $packageFiles
    }
    $indexRaw = (
        $index | ConvertTo-Json -Depth 8
    ).Replace("`r`n", "`n").Replace("`r", "`n") + "`n"
    $indexBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        $indexRaw)
    $indexHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $indexBytes)
    ).ToLowerInvariant()
    $indexDirectory = Join-Path (
        $resolvedPackageRoot
    ) 'artifacts\parity-full-suite\windows-both-package-index'
    New-Item -ItemType Directory -Force -Path $indexDirectory |
        Out-Null
    $indexPath = Join-Path $indexDirectory "$indexHash.json"
    [IO.File]::WriteAllBytes($indexPath, $indexBytes)

    & (
        Join-Path (
            $PSScriptRoot
        ) 'Assert-ParityFullSuitePackage.ps1') `
        -RepositoryRoot $resolvedPackageRoot `
        -IndexPath $indexPath `
        -IndexSha256 $indexHash `
        -RequireExactClosure
    if ($PassThru) {
        [pscustomobject]@{
            packageRoot = $resolvedPackageRoot
            index = $indexPath
            indexSha256 = $indexHash
            registry = $validated.registry
            registrySha256 = $RegistrySha256
        }
    }
} catch {
    if (Test-Path -LiteralPath $resolvedPackageRoot) {
        Remove-SafeDirectoryTree `
            -Root $packageStagingRoot `
            -Path $resolvedPackageRoot `
            -Description 'Failed parity Both package'
    }
    throw
}
