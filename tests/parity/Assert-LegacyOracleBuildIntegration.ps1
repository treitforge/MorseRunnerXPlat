[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $TrxPath,

    [Parameter(Mandatory)]
    [int] $ProcessExitCode
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

if ($ProcessExitCode -ne 0) {
    throw (
        'Legacy oracle build integration process must exit 0; observed ' +
        "$ProcessExitCode.")
}
if (-not (Test-Path -LiteralPath $TrxPath -PathType Leaf)) {
    throw "Legacy oracle build integration produced no TRX: $TrxPath"
}

[xml] $trx = Get-Content -LiteralPath $TrxPath -Raw
$namespace = [Xml.XmlNamespaceManager]::new($trx.NameTable)
$namespace.AddNamespace(
    't',
    'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
$results = @($trx.SelectNodes(
        '/t:TestRun/t:Results/t:UnitTestResult',
        $namespace))
$expectedTestName = (
    'MorseRunner.LegacyParity.Tests.LegacyOracleTargetTests.' +
    'ConfiguredBuildRegistryAttestsAndExecutesEverySelectedCase')
if ($results.Count -ne 1) {
    throw (
        'Legacy oracle build integration must execute exactly one test; ' +
        "observed $($results.Count).")
}
$result = $results[0]
if ($result.testName -cne $expectedTestName -or
    $result.outcome -cne 'Passed') {
    throw (
        'Legacy oracle build integration did not report the one exact ' +
        "passing test. Name='$($result.testName)', " +
        "outcome='$($result.outcome)'.")
}
if ($null -ne $result.SelectSingleNode(
        't:Output',
        $namespace) -or
    $null -ne $result.SelectSingleNode(
        't:Output/t:ErrorInfo',
        $namespace)) {
    throw (
        'Passing legacy oracle build integration TRX cannot contain ' +
        'captured errors.')
}

$counters = $trx.SelectSingleNode(
    '/t:TestRun/t:ResultSummary/t:Counters',
    $namespace)
if ($null -eq $counters) {
    throw 'Legacy oracle build integration TRX has no counters.'
}
foreach ($counterName in @('total', 'executed', 'passed')) {
    if ([int] $counters.GetAttribute($counterName) -ne 1) {
        throw (
            "Legacy oracle build integration counter '$counterName' " +
            'must equal 1.')
    }
}
foreach ($counterName in @(
        'error',
        'failed',
        'timeout',
        'aborted',
        'inconclusive',
        'passedButRunAborted',
        'notRunnable',
        'notExecuted',
        'disconnected',
        'warning',
        'completed',
        'inProgress',
        'pending')) {
    if ([int] $counters.GetAttribute($counterName) -ne 0) {
        throw (
            "Legacy oracle build integration counter '$counterName' " +
            'must equal 0.')
    }
}
$summary = $trx.SelectSingleNode(
    '/t:TestRun/t:ResultSummary',
    $namespace)
if ($null -eq $summary -or
    $summary.outcome -notin @('Completed', 'Passed')) {
    throw (
        'Legacy oracle build integration TRX summary is not a ' +
        'successful completion.')
}

Write-Host (
    'Legacy oracle build integration executed the actual registry and ' +
    'passed exactly once.')
