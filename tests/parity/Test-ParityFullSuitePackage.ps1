[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'ParityPathSafety.ps1')

function Write-TestFile {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $RelativePath,

        [string] $Content
    )

    $path = Join-Path (
        $Root) $RelativePath.Replace(
            '/', [IO.Path]::DirectorySeparatorChar)
    New-Item -ItemType Directory -Force `
        -Path (Split-Path -Parent $path) |
        Out-Null
    [IO.File]::WriteAllText(
        $path,
        $(if ($PSBoundParameters.ContainsKey('Content')) {
                $Content
            } else {
                "$RelativePath`n"
            }),
        [Text.UTF8Encoding]::new($false))
    return $path
}

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
    throw "Expected full-suite package rejection: $Description"
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$packageRoot = Join-Path (
    $repositoryRoot
) "artifacts\full-suite-package-test-$([Guid]::NewGuid().ToString('N'))"
try {
    New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
    $executableRelative =
        'artifacts/legacy-oracle/builds/test-v1/id/run/bin/LegacyOracle.exe'
    $provenanceRelative =
        'artifacts/legacy-oracle/builds/test-v1/id/run/' +
        'LegacyOracle.provenance.json'
    $executablePath = Write-TestFile `
        -Root $packageRoot `
        -RelativePath $executableRelative
    $provenancePath = Write-TestFile `
        -Root $packageRoot `
        -RelativePath $provenanceRelative
    [IO.File]::WriteAllText(
        $provenancePath,
        (
            "{`"selectedCaseIds`":[" +
            "`"case.B`",`"case.a`"]}`n"),
        [Text.UTF8Encoding]::new($false))
    $entry = [ordered]@{
        adapterId = 'LegacyOracleTarget'
        versionId = 'package-test-v1'
        source = 'tests/parity/legacy-oracle/v1/LegacyOracle.lpr'
        sourceSha256 = ('1' * 64)
        buildRecipe =
            'tests/parity/legacy-oracle/v1/build-recipe.json'
        buildRecipeSha256 = ('2' * 64)
        executable = $executableRelative
        executableSha256 = Get-LowercaseSha256 $executablePath
        provenance = $provenanceRelative
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
    $registryRelative =
        "artifacts/legacy-oracle/registries/$registryHash.json"
    $registryPath = Join-Path (
        $packageRoot) $registryRelative.Replace(
            '/', [IO.Path]::DirectorySeparatorChar)
    New-Item -ItemType Directory -Force `
        -Path (Split-Path -Parent $registryPath) |
        Out-Null
    [IO.File]::WriteAllBytes($registryPath, $registryBytes)

    $requiredPaths = [string[]] @(
        'artifacts/parity/legacy.json'
        'artifacts/parity/xplat.json'
        'artifacts/parity/test-results/legacy.trx'
        'artifacts/parity/test-results/xplat.trx'
        'artifacts/parity/test-results/legacy-oracle-build-integration.trx'
        'artifacts/parity/executions/legacy/legacy.json'
        'artifacts/parity/executions/xplat/xplat.json'
    )
    foreach ($requiredPath in $requiredPaths) {
        Write-TestFile `
            -Root $packageRoot `
            -RelativePath $requiredPath |
            Out-Null
    }
    $legacyResultPath = Join-Path (
        $packageRoot) 'artifacts\parity\legacy.json'
    $legacyResult = [ordered]@{
        runContext = [ordered]@{
            platform = 'windows'
            processArchitecture = 'x64'
            runtimeIdentifier = 'win-x64'
            framework = '.NET test'
            xplat = [ordered]@{
                revision = ('a' * 40)
                tree = ('b' * 40)
                clean = $true
            }
            legacy = [ordered]@{
                revision = ('c' * 40)
                tree = ('d' * 40)
                clean = $true
            }
        }
    }
    $legacyResultRaw = (
        $legacyResult | ConvertTo-Json -Depth 6
    ).Replace("`r`n", "`n").Replace("`r", "`n") + "`n"
    [IO.File]::WriteAllText(
        $legacyResultPath,
        $legacyResultRaw,
        [Text.UTF8Encoding]::new($false))
    $integrationReportPath = Join-Path (
        $packageRoot
    ) 'artifacts\parity\test-results\legacy-oracle-build-integration.trx'
    $integrationTestName =
        'MorseRunner.LegacyParity.Tests.LegacyOracleTargetTests.' +
        'ConfiguredBuildRegistryAttestsAndExecutesEverySelectedCase'
    $integrationTrx = @"
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testName="$integrationTestName" outcome="Passed" />
  </Results>
  <ResultSummary outcome="Completed">
    <Counters total="1" executed="1" passed="1" error="0" failed="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
  </ResultSummary>
</TestRun>
"@
    [IO.File]::WriteAllText(
        $integrationReportPath,
        $integrationTrx.Replace("`r`n", "`n").Replace("`r", "`n"),
        [Text.UTF8Encoding]::new($false))
    $integrationEnvelope = [ordered]@{
        schemaVersion = 1
        target = 'LegacyOracleBuildIntegration'
        platform = 'windows'
        architecture = 'x64'
        runtimeIdentifier = 'win-x64'
        revision = ('a' * 40)
        tree = ('b' * 40)
        registrySha256 = $registryHash
        selectedCaseIds = @('case.B', 'case.a')
        testName = $integrationTestName
        testReportSha256 =
            Get-LowercaseSha256 $integrationReportPath
        testProcessExitCode = 0
        wrapper = [ordered]@{
            completed = $true
            correlationValidated = $true
            exitCode = 0
        }
    }
    $integrationEnvelopeRaw = (
        $integrationEnvelope | ConvertTo-Json -Depth 6
    ).Replace("`r`n", "`n").Replace("`r", "`n") + "`n"
    $integrationEnvelopeBytes =
        [Text.UTF8Encoding]::new($false).GetBytes(
            $integrationEnvelopeRaw)
    $integrationEnvelopeHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $integrationEnvelopeBytes)
    ).ToLowerInvariant()
    $integrationEnvelopePath = Join-Path (
        $packageRoot
    ) (
        'artifacts\parity\executions\' +
        'legacy-oracle-build-integration\' +
        "$integrationEnvelopeHash.json")
    New-Item -ItemType Directory -Force `
        -Path (Split-Path -Parent $integrationEnvelopePath) |
        Out-Null
    [IO.File]::WriteAllBytes(
        $integrationEnvelopePath,
        $integrationEnvelopeBytes)

    $filesByPath =
        [Collections.Generic.Dictionary[string, object]]::new(
            [StringComparer]::Ordinal)
    foreach ($file in @(
            Get-ChildItem -LiteralPath $packageRoot -Recurse -File
        )) {
        $relativePath = [IO.Path]::GetRelativePath(
            $packageRoot,
            $file.FullName
        ).Replace('\', '/')
        $filesByPath.Add(
            $relativePath,
            [ordered]@{
                path = $relativePath
                sha256 = Get-LowercaseSha256 $file.FullName
            })
    }
    $filePaths = [string[]] @($filesByPath.Keys)
    [Array]::Sort($filePaths, [StringComparer]::Ordinal)
    $files = @(
        foreach ($filePath in $filePaths) {
            $filesByPath[$filePath]
        })
    $indexRaw = (
        [ordered]@{
            schemaVersion = 1
            platform = 'windows'
            target = 'Both'
            registry = [ordered]@{
                path = $registryRelative
                sha256 = $registryHash
            }
            files = $files
        } | ConvertTo-Json -Depth 8
    ).Replace("`r`n", "`n").Replace("`r", "`n") + "`n"
    $indexBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        $indexRaw)
    $indexHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $indexBytes)
    ).ToLowerInvariant()
    $indexRelative =
        'artifacts/parity-full-suite/windows-both-package-index/' +
        "$indexHash.json"
    $indexPath = Join-Path (
        $packageRoot) $indexRelative.Replace(
            '/', [IO.Path]::DirectorySeparatorChar)
    New-Item -ItemType Directory -Force `
        -Path (Split-Path -Parent $indexPath) |
        Out-Null
    [IO.File]::WriteAllBytes($indexPath, $indexBytes)

    & (
        Join-Path (
            $PSScriptRoot
        ) 'Assert-ParityFullSuitePackage.ps1') `
        -RepositoryRoot $packageRoot `
        -IndexPath $indexPath `
        -IndexSha256 $indexHash `
        -RequireExactClosure

    $runtimeMismatch = [ordered]@{}
    foreach ($property in $integrationEnvelope.GetEnumerator()) {
        $runtimeMismatch[$property.Key] = $property.Value
    }
    $runtimeMismatch.architecture = 'raced-architecture'
    $runtimeMismatchRaw = (
        $runtimeMismatch | ConvertTo-Json -Depth 6
    ).Replace("`r`n", "`n").Replace("`r", "`n") + "`n"
    $runtimeMismatchBytes =
        [Text.UTF8Encoding]::new($false).GetBytes($runtimeMismatchRaw)
    $runtimeMismatchHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $runtimeMismatchBytes)
    ).ToLowerInvariant()
    $runtimeMismatchPath = Join-Path (
        $packageRoot
    ) (
        'artifacts\parity\executions\' +
        'legacy-oracle-build-integration\' +
        "$runtimeMismatchHash.json")
    [IO.File]::WriteAllBytes(
        $runtimeMismatchPath,
        $runtimeMismatchBytes)
    Assert-Rejected -Description 'mismatched integration runtime identity' {
        & (
            Join-Path (
                $PSScriptRoot
            ) 'Assert-LegacyOracleBuildIntegrationExecution.ps1') `
            -EnvelopePath $runtimeMismatchPath `
            -IntegrationTrxPath $integrationReportPath `
            -RegistryPath $registryPath `
            -RegistrySha256 $registryHash `
            -WindowsLegacyResultPath $legacyResultPath `
            -RepositoryRoot $packageRoot
    }

    [IO.File]::AppendAllText(
        (Join-Path $packageRoot 'artifacts\parity\xplat.json'),
        'tamper',
        [Text.UTF8Encoding]::new($false))
    Assert-Rejected -Description 'tampered indexed result' {
        & (
            Join-Path (
                $PSScriptRoot
            ) 'Assert-ParityFullSuitePackage.ps1') `
            -RepositoryRoot $packageRoot `
            -IndexPath $indexPath `
            -IndexSha256 $indexHash `
            -RequireExactClosure
    }
} finally {
    if (Test-Path -LiteralPath $packageRoot) {
        Remove-SafeDirectoryTree `
            -Root $repositoryRoot `
            -Path $packageRoot `
            -Description 'Full-suite package contract test root'
    }
}

Write-Host 'Full-suite package closure checks passed.'
