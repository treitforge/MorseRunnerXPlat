[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'ParityPathSafety.ps1')

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
    throw "Expected build-integration TRX rejection: $Description"
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$testRoot = Join-Path (
    $repositoryRoot
) "artifacts\build-integration-test-$([Guid]::NewGuid().ToString('N'))"
$trxPath = Join-Path $testRoot 'integration.trx'
$testName =
    'MorseRunner.LegacyParity.Tests.LegacyOracleTargetTests.' +
    'ConfiguredBuildRegistryAttestsAndExecutesEverySelectedCase'
$validTrx = @"
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testName="$testName" outcome="Passed" />
  </Results>
  <ResultSummary outcome="Completed">
    <Counters total="1" executed="1" passed="1" error="0" failed="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
  </ResultSummary>
</TestRun>
"@

try {
    New-Item -ItemType Directory -Force -Path $testRoot |
        Out-Null
    [IO.File]::WriteAllText(
        $trxPath,
        $validTrx,
        [Text.UTF8Encoding]::new($false))
    & (
        Join-Path (
            $PSScriptRoot
        ) 'Assert-LegacyOracleBuildIntegration.ps1') `
        -TrxPath $trxPath `
        -ProcessExitCode 0

    Assert-Rejected -Description 'failed process' {
        & (
            Join-Path (
                $PSScriptRoot
            ) 'Assert-LegacyOracleBuildIntegration.ps1') `
            -TrxPath $trxPath `
            -ProcessExitCode 7
    }

    [IO.File]::WriteAllText(
        $trxPath,
        $validTrx.Replace(
            'ConfiguredBuildRegistryAttestsAndExecutesEverySelectedCase',
            'WrongTest'),
        [Text.UTF8Encoding]::new($false))
    Assert-Rejected -Description 'wrong test identity' {
        & (
            Join-Path (
                $PSScriptRoot
            ) 'Assert-LegacyOracleBuildIntegration.ps1') `
            -TrxPath $trxPath `
            -ProcessExitCode 0
    }

    [IO.File]::WriteAllText(
        $trxPath,
        $validTrx.Replace('outcome="Passed"', 'outcome="Failed"'),
        [Text.UTF8Encoding]::new($false))
    Assert-Rejected -Description 'failed test outcome' {
        & (
            Join-Path (
                $PSScriptRoot
            ) 'Assert-LegacyOracleBuildIntegration.ps1') `
            -TrxPath $trxPath `
            -ProcessExitCode 0
    }

    [IO.File]::WriteAllText(
        $trxPath,
        "$validTrx<tamper>",
        [Text.UTF8Encoding]::new($false))
    Assert-Rejected -Description 'malformed tampered TRX' {
        & (
            Join-Path (
                $PSScriptRoot
            ) 'Assert-LegacyOracleBuildIntegration.ps1') `
            -TrxPath $trxPath `
            -ProcessExitCode 0
    }
} finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-SafeDirectoryTree `
            -Root $repositoryRoot `
            -Path $testRoot `
            -Description 'Build-integration TRX contract test root'
    }
}

Write-Host 'Legacy oracle build-integration TRX checks passed.'
