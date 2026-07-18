[CmdletBinding()]
param(
    [string] $LegacyRoot = (Join-Path $PSScriptRoot '..\..\..\MorseRunner')
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$legacyRootPath = (Resolve-Path $LegacyRoot).Path + [IO.Path]::DirectorySeparatorChar
$oraclePath = Join-Path $repositoryRoot 'artifacts\legacy-oracle\LegacyOracle.exe'
$revision = (& git -C $legacyRootPath rev-parse --verify HEAD).Trim()

if (-not (Test-Path $oraclePath -PathType Leaf)) {
    throw "Legacy oracle not found: $oraclePath"
}
if ($revision -ne '55bbd019c29d8cf693184ea420a17a253f16fe1e') {
    throw "Legacy revision mismatch: $revision"
}

$scenarios = @(
    'simulation.legacy-effects'
    'audio-dsp.legacy-processing'
    'data.legacy-parsers'
    'simulation.state-models'
    'simulation.runtime-routines'
    'simulation.live-operator-session'
    'logging.scoring-rate-and-results'
    'contest.cq-wpx-scoring'
    'audio.legacy-adapters'
    'contest.legacy-implementations'
)

foreach ($scenario in $scenarios) {
    $raw = (& $oraclePath $legacyRootPath $scenario | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Legacy oracle failed for $scenario with exit code $LASTEXITCODE."
    }
    $observation = $raw | ConvertFrom-Json
    if ($observation.scenario -ne $scenario) {
        throw "Legacy oracle returned the wrong scenario for $scenario."
    }

    $fileStem = $scenario.Replace('.', '-')
    $fixtureRelative = "tests/parity/fixtures/legacy/$fileStem.json"
    $evidenceRelative = "tests/parity/evidence/$fileStem.baseline.json"
    $fixturePath = Join-Path $repositoryRoot $fixtureRelative
    $evidencePath = Join-Path $repositoryRoot $evidenceRelative
    $capturedAt = if (Test-Path $evidencePath -PathType Leaf) {
        (Get-Content $evidencePath -Raw | ConvertFrom-Json).capturedAtUtc
    } else {
        (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    }

    [ordered]@{
        revision = $revision
        parityId = $scenario
        oracle = 'tests/parity/legacy-oracle/LegacyOracle.lpr'
        toolchain = [ordered]@{
            lazarus = '4.6'
            fpc = '3.2.2'
            target = 'x86_64-win64'
        }
        values = @($observation.values)
    } | ConvertTo-Json -Depth 6 | Set-Content -Path $fixturePath -Encoding utf8

    [ordered]@{
        parityId = $scenario
        referenceRevision = $revision
        capturedAtUtc = $capturedAt
        legacy = [ordered]@{
            outcome = 'pass'
            source = 'tests/parity/legacy-oracle/LegacyOracle.lpr'
            fixture = $fixtureRelative
            observedValueCount = @($observation.values).Count
            toolchain = 'Lazarus 4.6, FPC 3.2.2, x86_64-win64'
        }
        xplat = [ordered]@{
            outcome = 'fail'
            failureCode = 'unsupported-capability'
            firstDivergence = "The XPlat $scenario capability does not exist."
        }
        classification = 'legacy-green-xplat-red'
    } | ConvertTo-Json -Depth 6 | Set-Content -Path $evidencePath -Encoding utf8

    Write-Host "Captured $(@($observation.values).Count) values for $scenario"
}
