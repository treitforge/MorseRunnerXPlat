[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Legacy', 'XPlat')]
    [string] $Target,

    [Parameter(Mandatory)]
    [string] $ResultPath,

    [Parameter(Mandatory)]
    [string] $TrxPath,

    [Parameter(Mandatory)]
    [int] $ProcessExitCode
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

function Get-OrdinallySortedStrings {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]] $Value
    )

    $sorted = [string[]] @(
        $Value | ForEach-Object { [string] $_ })
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    return $sorted
}

if (-not (Test-Path -LiteralPath $ResultPath -PathType Leaf)) {
    throw "$Target structured parity result was not created: $ResultPath"
}
if (-not (Test-Path -LiteralPath $TrxPath -PathType Leaf)) {
    throw "$Target TRX result was not created: $TrxPath"
}

$document = Get-Content -LiteralPath $ResultPath -Raw |
    ConvertFrom-Json
if ($document.schemaVersion -ne 1 -or
    $document.target -cne $Target.ToLowerInvariant()) {
    throw "$Target structured parity result has an invalid identity."
}
$results = @($document.results)
if ($results.Count -eq 0) {
    throw "$Target structured parity result contains no cases."
}
if (@($results.parityId | Select-Object -Unique).Count -ne
    $results.Count) {
    throw "$Target structured parity result contains duplicate cases."
}
if (@($results.acceptanceTestName | Select-Object -Unique).Count -ne
    $results.Count) {
    throw "$Target structured parity result contains duplicate test names."
}
foreach ($result in $results) {
    $expectedName = "parity:$($result.parityId)()"
    if ([string]::IsNullOrWhiteSpace(
            [string] $result.acceptanceTestName) -or
        $result.acceptanceTestName -cne $expectedName) {
        throw (
            "$Target case '$($result.parityId)' does not bind to its " +
            "exact acceptance test name '$expectedName'.")
    }
    if ($result.outcome -notin @(
            'passed',
            'functional-divergence')) {
        throw (
            "$Target case '$($result.parityId)' has noncertifying " +
            "outcome '$($result.outcome)'.")
    }
    if ($Target -eq 'Legacy' -and
        $result.outcome -cne 'passed') {
        throw (
            "Legacy case '$($result.parityId)' must pass. Functional " +
            'divergence is certifiable only for XPlat.')
    }
}

try {
    [xml] $trx = Get-Content -LiteralPath $TrxPath -Raw
} catch {
    throw "$Target TRX result is not valid XML: $TrxPath"
}
$summaryNodes = @(
    $trx.SelectNodes(
        "/*[local-name()='TestRun']/*[local-name()='ResultSummary']"))
if ($summaryNodes.Count -ne 1) {
    throw "$Target TRX must contain exactly one result summary."
}
$summary = $summaryNodes[0]
$unitResults = @(
    $trx.SelectNodes(
        "//*[local-name()='UnitTestResult']"))
if ($unitResults.Count -ne $results.Count) {
    throw (
        "$Target TRX executed $($unitResults.Count) tests, but the " +
        "structured result contains $($results.Count) cases.")
}

$trxByName = @{}
$trxResultByName = @{}
foreach ($unitResult in $unitResults) {
    $testName = [string] $unitResult.testName
    if ([string]::IsNullOrWhiteSpace($testName)) {
        throw "$Target TRX contains a result without a testName."
    }
    if ($trxByName.ContainsKey($testName)) {
        throw "$Target TRX contains duplicate testName '$testName'."
    }
    $outcome = [string] $unitResult.outcome
    if ($outcome -notin @('Passed', 'Failed')) {
        throw (
            "$Target TRX test '$testName' has noncertifying outcome " +
            "'$outcome'.")
    }
    $trxByName.Add($testName, $outcome)
    $trxResultByName.Add($testName, $unitResult)
}

$expectedNames = @(
    Get-OrdinallySortedStrings @(
        $results.acceptanceTestName))
$actualNames = @(
    Get-OrdinallySortedStrings @($trxByName.Keys))
if ($expectedNames.Count -ne $actualNames.Count) {
    throw "$Target TRX and structured result test sets differ."
}
for ($index = 0; $index -lt $expectedNames.Count; $index++) {
    if ($expectedNames[$index] -cne $actualNames[$index]) {
        throw (
            "$Target TRX contains missing or extra acceptance tests. " +
            "Expected '$($expectedNames[$index])', observed " +
            "'$($actualNames[$index])'.")
    }
}

$expectedFailed = @(
    Get-OrdinallySortedStrings @(
        $results |
            Where-Object {
                $_.outcome -ceq 'functional-divergence'
            } |
            ForEach-Object { $_.acceptanceTestName }))
$expectedPassed = @(
    Get-OrdinallySortedStrings @(
        $results |
            Where-Object { $_.outcome -ceq 'passed' } |
            ForEach-Object { $_.acceptanceTestName }))
$actualFailed = @(
    Get-OrdinallySortedStrings @(
        $trxByName.GetEnumerator() |
            Where-Object { $_.Value -ceq 'Failed' } |
            ForEach-Object { $_.Key }))
$actualPassed = @(
    Get-OrdinallySortedStrings @(
        $trxByName.GetEnumerator() |
            Where-Object { $_.Value -ceq 'Passed' } |
            ForEach-Object { $_.Key }))
foreach ($result in $results) {
    $testName = [string] $result.acceptanceTestName
    $unitResult = $trxResultByName[$testName]
    $errorInfo = @(
        $unitResult.SelectNodes(
            ".//*[local-name()='ErrorInfo']"))
    if ($result.outcome -ceq 'functional-divergence') {
        if ($errorInfo.Count -ne 1) {
            throw (
                "$Target expected-red test '$testName' must contain " +
                'exactly one TRX ErrorInfo marker.')
        }
        $messageNodes = @(
            $errorInfo[0].SelectNodes(
                "*[local-name()='Message']"))
        $stackNodes = @(
            $errorInfo[0].SelectNodes(
                "*[local-name()='StackTrace']"))
        $expectedMessage = (
            'MorseRunner.LegacyParity.Tests.' +
            'ParityFunctionalDivergenceException : ' +
            'PARITY_FUNCTIONAL_DIVERGENCE|' +
            "$($result.parityId)|$($result.failureCode)")
        if ($messageNodes.Count -ne 1 -or
            [string] $messageNodes[0].InnerText -cne
                $expectedMessage -or
            $stackNodes.Count -ne 1 -or
            [string]::IsNullOrWhiteSpace(
                [string] $stackNodes[0].InnerText)) {
            throw (
                "$Target expected-red test '$testName' does not contain " +
                'the exact functional-divergence exception marker.')
        }
    } elseif ($errorInfo.Count -ne 0) {
        throw "$Target passed test '$testName' contains TRX ErrorInfo."
    }
}
foreach ($sets in @(
        @($expectedFailed, $actualFailed, 'failed'),
        @($expectedPassed, $actualPassed, 'passed'))) {
    $expected = @($sets[0])
    $actual = @($sets[1])
    if ($expected.Count -ne $actual.Count) {
        throw (
            "$Target TRX $($sets[2]) count does not match structured " +
            'parity outcomes.')
    }
    for ($index = 0; $index -lt $expected.Count; $index++) {
        if ($expected[$index] -cne $actual[$index]) {
            throw (
                "$Target TRX $($sets[2]) test '$($actual[$index])' " +
                "does not match expected '$($expected[$index])'.")
        }
    }
}

$counterNodes = @(
    $summary.SelectNodes("*[local-name()='Counters']"))
if ($counterNodes.Count -ne 1) {
    throw "$Target TRX must contain exactly one counters element."
}
$counters = $counterNodes[0]
$requiredCounters = [ordered]@{
    total = $results.Count
    executed = $results.Count
    passed = $expectedPassed.Count
    failed = $expectedFailed.Count
    error = 0
    timeout = 0
    aborted = 0
    inconclusive = 0
    notExecuted = 0
    disconnected = 0
    warning = 0
    notRunnable = 0
}
foreach ($counter in $requiredCounters.GetEnumerator()) {
    $attribute = $counters.Attributes[$counter.Key]
    if ($null -eq $attribute -or
        [int] $attribute.Value -ne $counter.Value) {
        throw (
            "$Target TRX counter '$($counter.Key)' must be " +
            "$($counter.Value).")
    }
}

$summaryOutcome = [string] $summary.outcome
if ($expectedFailed.Count -gt 0) {
    if ($summaryOutcome -cne 'Failed' -or
        $ProcessExitCode -ne 2) {
        throw (
            "$Target functional divergences require a failed TRX summary " +
            'and Microsoft.Testing.Platform exit code 2.')
    }
} else {
    if ($summaryOutcome -notin @('Completed', 'Passed') -or
        $ProcessExitCode -ne 0) {
        throw (
            "$Target all-green execution requires a completed TRX summary " +
            'and zero test-process exit code.')
    }
}
