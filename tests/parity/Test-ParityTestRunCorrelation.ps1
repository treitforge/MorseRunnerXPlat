[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'ParityPathSafety.ps1')

$repositoryRoot = (Resolve-Path (
    Join-Path $PSScriptRoot '..\..')).Path
$testRoot = Assert-SafeDescendantPath `
    -Root $repositoryRoot `
    -Candidate (Join-Path (
        Join-Path $repositoryRoot 'artifacts\parity\test-correlation'
    ) ([Guid]::NewGuid().ToString('N'))) `
    -Description 'Parity TRX correlation test'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null

function Write-Utf8 {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Value
    )

    [IO.File]::WriteAllText(
        $Path,
        $Value,
        [Text.UTF8Encoding]::new($false))
}

function New-ResultJson {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('passed', 'functional-divergence')]
        [string] $Outcome
    )

    $failureCode = if ($Outcome -eq 'passed') {
        'null'
    } else {
        '"known-functional-gap"'
    }
    return @"
{
  "schemaVersion": 1,
  "target": "xplat",
  "results": [
    {
      "parityId": "case.one",
      "acceptanceTestName": "parity:case.one()",
      "outcome": "$Outcome",
      "failureCode": $failureCode
    }
  ]
}
"@
}

function New-Trx {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Passed', 'Failed', 'NotExecuted')]
        [string] $Outcome,

        [string] $TestName = 'parity:case.one()',

        [switch] $WrongErrorMessage,

        [switch] $WrongExceptionType,

        [switch] $MessageSuffix,

        [switch] $ExtraInfrastructureFailure
    )

    $passed = if ($Outcome -eq 'Passed') { 1 } else { 0 }
    $failed = if ($Outcome -eq 'Failed') { 1 } else { 0 }
    if ($ExtraInfrastructureFailure) {
        $failed++
    }
    $notExecuted = if ($Outcome -eq 'NotExecuted') { 1 } else { 0 }
    $summary = if ($failed -eq 1) { 'Failed' } else { 'Completed' }
    if ($failed -gt 0) {
        $summary = 'Failed'
    }
    $total = 1 + [int] $ExtraInfrastructureFailure.IsPresent
    $errorOutput = if ($Outcome -eq 'Failed') {
        $message = if ($WrongErrorMessage) {
            'Assert.Equal() Failure'
        } else {
            (
                'MorseRunner.LegacyParity.Tests.' +
                'ParityFunctionalDivergenceException : ' +
                'PARITY_FUNCTIONAL_DIVERGENCE|' +
                'case.one|known-functional-gap')
        }
        if ($WrongExceptionType) {
            $message = $message.Replace(
                'MorseRunner.LegacyParity.Tests.' +
                    'ParityFunctionalDivergenceException',
                'System.Exception')
        }
        if ($MessageSuffix) {
            $message += ' unexpected-suffix'
        }
        @"
      <Output>
        <ErrorInfo>
          <Message>$message</Message>
          <StackTrace>at parity.case.one()</StackTrace>
        </ErrorInfo>
      </Output>
"@
    } else {
        ''
    }
    $extraResult = if ($ExtraInfrastructureFailure) {
        @"
    <UnitTestResult testName="unmapped.infrastructure.failure"
      outcome="Failed">
      <Output>
        <ErrorInfo>
          <Message>host setup failed</Message>
          <StackTrace>at infrastructure()</StackTrace>
        </ErrorInfo>
      </Output>
    </UnitTestResult>
"@
    } else {
        ''
    }
    return @"
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testName="$TestName" outcome="$Outcome">
$errorOutput
    </UnitTestResult>
$extraResult
  </Results>
  <ResultSummary outcome="$summary">
    <Counters total="$total" executed="$($passed + $failed)"
      passed="$passed" failed="$failed" error="0" timeout="0"
      aborted="0" inconclusive="0" notExecuted="$notExecuted"
      disconnected="0" warning="0" notRunnable="0" />
  </ResultSummary>
</TestRun>
"@
}

function Invoke-Correlation {
    param(
        [ValidateSet('Legacy', 'XPlat')]
        [string] $Target = 'XPlat',

        [Parameter(Mandatory)]
        [string] $ResultJson,

        [Parameter(Mandatory)]
        [string] $Trx,

        [Parameter(Mandatory)]
        [int] $ExitCode
    )

    $resultPath = Join-Path $testRoot 'result.json'
    $trxPath = Join-Path $testRoot 'result.trx'
    Write-Utf8 -Path $resultPath -Value $ResultJson
    Write-Utf8 -Path $trxPath -Value $Trx
    & (Join-Path $PSScriptRoot 'Assert-ParityTestRun.ps1') `
        -Target $Target `
        -ResultPath $resultPath `
        -TrxPath $trxPath `
        -ProcessExitCode $ExitCode
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
    throw "Expected parity correlation rejection: $Description"
}

try {
    Invoke-Correlation `
        -ResultJson (New-ResultJson -Outcome passed) `
        -Trx (New-Trx -Outcome Passed) `
        -ExitCode 0
    Invoke-Correlation `
        -ResultJson (
            New-ResultJson -Outcome functional-divergence) `
        -Trx (New-Trx -Outcome Failed) `
        -ExitCode 2

    Assert-Rejected -Description 'masked functional divergence exit' {
        Invoke-Correlation `
            -ResultJson (
                New-ResultJson -Outcome functional-divergence) `
            -Trx (New-Trx -Outcome Failed) `
            -ExitCode 0
    }
    Assert-Rejected -Description 'crashed functional divergence process' {
        Invoke-Correlation `
            -ResultJson (
                New-ResultJson -Outcome functional-divergence) `
            -Trx (New-Trx -Outcome Failed) `
            -ExitCode 7
    }
    Assert-Rejected -Description 'Legacy functional divergence' {
        $legacyRed = (
            New-ResultJson -Outcome functional-divergence
        ).Replace('"target": "xplat"', '"target": "legacy"')
        Invoke-Correlation `
            -Target Legacy `
            -ResultJson $legacyRed `
            -Trx (New-Trx -Outcome Failed) `
            -ExitCode 2
    }
    Assert-Rejected -Description 'infrastructure test failure' {
        Invoke-Correlation `
            -ResultJson (New-ResultJson -Outcome passed) `
            -Trx (
                New-Trx `
                    -Outcome Failed `
                    -TestName 'unmapped.infrastructure.failure') `
            -ExitCode 1
    }
    Assert-Rejected -Description 'acceptance name without theory suffix' {
        Invoke-Correlation `
            -ResultJson (
                New-ResultJson -Outcome passed) `
            -Trx (
                New-Trx `
                    -Outcome Passed `
                    -TestName 'parity:case.one') `
            -ExitCode 0
    }
    Assert-Rejected -Description (
        'expected red plus infrastructure failure') {
        Invoke-Correlation `
            -ResultJson (
                New-ResultJson -Outcome functional-divergence) `
            -Trx (
                New-Trx `
                    -Outcome Failed `
                    -ExtraInfrastructureFailure) `
            -ExitCode 2
    }
    Assert-Rejected -Description 'same-name generic exception' {
        Invoke-Correlation `
            -ResultJson (
                New-ResultJson -Outcome functional-divergence) `
            -Trx (
                New-Trx `
                    -Outcome Failed `
                    -WrongErrorMessage) `
            -ExitCode 2
    }
    Assert-Rejected -Description 'wrong divergence exception type' {
        Invoke-Correlation `
            -ResultJson (
                New-ResultJson -Outcome functional-divergence) `
            -Trx (
                New-Trx `
                    -Outcome Failed `
                    -WrongExceptionType) `
            -ExitCode 2
    }
    Assert-Rejected -Description 'divergence message suffix' {
        Invoke-Correlation `
            -ResultJson (
                New-ResultJson -Outcome functional-divergence) `
            -Trx (
                New-Trx `
                    -Outcome Failed `
                    -MessageSuffix) `
            -ExitCode 2
    }
    Assert-Rejected -Description 'skipped test' {
        Invoke-Correlation `
            -ResultJson (New-ResultJson -Outcome passed) `
            -Trx (New-Trx -Outcome NotExecuted) `
            -ExitCode 0
    }
} finally {
    Remove-SafeDirectoryTree `
        -Root $repositoryRoot `
        -Path $testRoot `
        -Description 'Parity TRX correlation test'
}

Write-Host 'Parity TRX correlation checks passed.'
