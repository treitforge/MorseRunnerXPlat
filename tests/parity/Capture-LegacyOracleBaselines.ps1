[CmdletBinding()]
param(
    [string] $LegacyRoot = (
        Join-Path $PSScriptRoot '..\..\..\MorseRunner'),

    [string] $LazarusRoot = 'C:\lazarus',

    [string[]] $Scenario
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'ParityPathSafety.ps1')
. (Join-Path $PSScriptRoot 'ParityCanonicalJson.ps1')

function Test-SameSha256 {
    param(
        [Parameter(Mandatory)]
        [string] $Expected,

        [Parameter(Mandatory)]
        [string] $Actual
    )

    return [StringComparer]::OrdinalIgnoreCase.Equals($Expected, $Actual)
}

function Get-Sha256Text {
    param(
        [Parameter(Mandatory)]
        [string] $Value
    )

    return [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            [Text.Encoding]::UTF8.GetBytes($Value))
    ).ToLowerInvariant()
}

$repositoryRoot = (Resolve-Path (
    Join-Path $PSScriptRoot '..\..')).Path
$preparedLegacyRoot = Join-Path (
    $repositoryRoot) 'artifacts\legacy-reference'
$manifestPath = Join-Path $PSScriptRoot 'parity-manifest.json'
$manifestRaw = Get-Content -LiteralPath $manifestPath -Raw
$manifest = $manifestRaw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 3) {
    throw "Unsupported parity manifest schema: $($manifest.schemaVersion)"
}

$scenarioIds = @(
    if (@($Scenario).Count -gt 0) {
        $Scenario | Select-Object -Unique
    } else {
        $manifest.cases.id
    }
)
if ($scenarioIds.Count -eq 0) {
    throw 'At least one active legacy oracle scenario must be captured.'
}
$selectedCases = @(
    foreach ($scenarioId in $scenarioIds) {
        $matches = @(
            $manifest.cases |
                Where-Object { $_.id -ceq $scenarioId })
        if ($matches.Count -ne 1) {
            throw (
                'Fixture capture requires exactly one active case for ' +
                "'$scenarioId'.")
        }
        $matches[0]
    }
)

& (Join-Path $PSScriptRoot 'Prepare-LegacyReference.ps1') `
    -SourceRepository $LegacyRoot `
    -Destination $preparedLegacyRoot
$build = & (Join-Path $PSScriptRoot 'Build-LegacyOracle.ps1') `
    -LegacyRoot $preparedLegacyRoot `
    -LazarusRoot $LazarusRoot `
    -CaseId $scenarioIds `
    -PassThru
if ($null -eq $build -or
    -not (Test-Path -LiteralPath (
        $build.registry) -PathType Leaf)) {
    throw 'Legacy oracle build produced no registry.'
}
$actualRegistryHash = (
    Get-FileHash -LiteralPath (
        $build.registry) -Algorithm SHA256).Hash.ToLowerInvariant()
if (-not (Test-SameSha256 `
        ([string] $build.registrySha256) $actualRegistryHash)) {
    throw "Fresh legacy oracle registry hash mismatch: $actualRegistryHash"
}
$registry = Get-Content -LiteralPath $build.registry -Raw |
    ConvertFrom-Json
if ($registry.schemaVersion -ne 1) {
    throw (
        'Fresh legacy oracle registry has an unsupported schema: ' +
        $registry.schemaVersion)
}

$legacyRootArgument = (
    Resolve-Path -LiteralPath $preparedLegacyRoot
).Path + [IO.Path]::DirectorySeparatorChar
$captureRoot = Assert-SafeDescendantPath `
    -Root $repositoryRoot `
    -Candidate (Join-Path (
        Join-Path (
            $repositoryRoot
        ) 'artifacts\parity\fixture-capture'
    ) ([Guid]::NewGuid().ToString('N'))) `
    -Description 'Legacy fixture capture staging'
New-Item -ItemType Directory -Force -Path $captureRoot | Out-Null
$captures = [Collections.Generic.List[object]]::new()
$manifestJsonDocument =
    [Text.Json.JsonDocument]::Parse($manifestRaw)
$semanticFieldNames = [string[]] @(
    'assertions',
    'behavior',
    'capabilityId',
    'fixture',
    'id',
    'input',
    'legacyOracle',
    'legacySources',
    'legacySurfaceSelectors',
    'obligationIds',
    'platforms',
    'preconditions',
    'targetAdapters')

try {
    foreach ($case in $selectedCases) {
        $scenarioId = [string] $case.id
        $descriptor = $case.legacyOracle
        if ($null -eq $descriptor) {
            throw "Parity case '$scenarioId' has no legacyOracle descriptor."
        }
        $registryEntries = @(
            $registry.entries |
                Where-Object {
                    $_.adapterId -ceq $descriptor.adapterId -and
                    $_.versionId -ceq $descriptor.versionId
                })
        if ($registryEntries.Count -ne 1) {
            throw (
                "Parity case '$scenarioId' must select exactly one " +
                'fresh registry entry.')
        }
        $entry = $registryEntries[0]
        foreach ($binding in @(
                @('source', 'source'),
                @('sourceSha256', 'sourceSha256'),
                @('buildRecipe', 'buildRecipe'),
                @('buildRecipeSha256', 'buildRecipeSha256'))) {
            $entryValue = [string] $entry.($binding[0])
            $descriptorValue = [string] $descriptor.($binding[1])
            if ($binding[0].EndsWith('Sha256')) {
                if (-not (Test-SameSha256 `
                        $descriptorValue $entryValue)) {
                    throw (
                        "Registry entry '$($entry.versionId)' does not " +
                        "match case '$scenarioId' $($binding[1]).")
                }
            } elseif ($entryValue -cne $descriptorValue) {
                throw (
                    "Registry entry '$($entry.versionId)' does not " +
                    "match case '$scenarioId' $($binding[1]).")
            }
        }
        $entryExecutablePath = Assert-SafeDescendantPath `
            -Root $repositoryRoot `
            -Candidate (Join-Path (
                $repositoryRoot) $entry.executable) `
            -Description (
                "Registry entry '$($entry.versionId)' executable")
        $entryProvenancePath = Assert-SafeDescendantPath `
            -Root $repositoryRoot `
            -Candidate (Join-Path (
                $repositoryRoot) $entry.provenance) `
            -Description (
                "Registry entry '$($entry.versionId)' provenance")
        foreach ($pathBinding in @(
                @('executable', $entryExecutablePath),
                @('provenance', $entryProvenancePath))) {
            if (-not (Test-Path -LiteralPath (
                    $pathBinding[1]) -PathType Leaf)) {
                throw (
                    "Registry entry '$($entry.versionId)' " +
                    "$($pathBinding[0]) does not exist.")
            }
        }
        $actualExecutableHash = (
            Get-FileHash -LiteralPath (
                $entryExecutablePath) -Algorithm SHA256).Hash.ToLowerInvariant()
        $actualProvenanceHash = (
            Get-FileHash -LiteralPath (
                $entryProvenancePath) -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not (Test-SameSha256 `
                $entry.executableSha256 $actualExecutableHash) -or
            -not (Test-SameSha256 `
                $entry.provenanceSha256 $actualProvenanceHash)) {
            throw (
                "Registry entry '$($entry.versionId)' content hash " +
                'verification failed.')
        }

        $provenance = Get-Content -LiteralPath (
            $entryProvenancePath) -Raw | ConvertFrom-Json
        foreach ($propertyName in @(
                'adapterId',
                'versionId',
                'source',
                'sourceSha256',
                'buildRecipe',
                'buildRecipeSha256')) {
            $expected = if ($propertyName.EndsWith('Sha256')) {
                [string] $descriptor.$propertyName
            } else {
                [string] $descriptor.$propertyName
            }
            $actual = [string] $provenance.$propertyName
            if ($propertyName.EndsWith('Sha256')) {
                if (-not (Test-SameSha256 $expected $actual)) {
                    throw (
                        "Provenance $propertyName does not match case " +
                        "'$scenarioId'.")
                }
            } elseif ($expected -cne $actual) {
                throw (
                    "Provenance $propertyName does not match case " +
                    "'$scenarioId'.")
            }
        }
        $verifiedObservation = @(
            $provenance.observations |
                Where-Object { $_.scenario -ceq $scenarioId })
        if ($verifiedObservation.Count -ne 1) {
            throw (
                'Scenario must have exactly one fresh build observation: ' +
                $scenarioId)
        }

        $caseArray = (
            $manifestJsonDocument.RootElement.GetProperty('cases'))
        $caseElements = @(
            $caseArray.EnumerateArray() |
                Where-Object {
                    $_.GetProperty('id').GetString() -ceq
                        $scenarioId
                })
        if ($caseElements.Count -ne 1) {
            throw (
                "Manifest JSON does not contain one case element for " +
                "'$scenarioId'.")
        }
        $caseElement = $caseElements[0]
        $inputBytes = ConvertTo-ParityCanonicalJsonBytes `
            -Element $caseElement.GetProperty('input')
        $inputHash = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData(
                $inputBytes)
        ).ToLowerInvariant()
        $caseDefinitionBytes =
            ConvertTo-ParityCanonicalJsonBytes `
                -Element $caseElement `
                -Projection $semanticFieldNames
        $caseDefinitionHash = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData(
                $caseDefinitionBytes)
        ).ToLowerInvariant()
        $inputPath = Join-Path $captureRoot "$inputHash.json"
        [IO.File]::WriteAllBytes($inputPath, $inputBytes)

        $raw = (
            & $entryExecutablePath `
                $legacyRootArgument `
                $scenarioId `
                $descriptor.adapterId `
                $descriptor.versionId `
                $descriptor.source `
                $descriptor.sourceSha256 `
                $descriptor.buildRecipe `
                $descriptor.buildRecipeSha256 `
                $caseDefinitionHash `
                $inputHash `
                $inputPath |
                Out-String
        ).Trim()
        if ($LASTEXITCODE -ne 0) {
            throw (
                "Legacy oracle failed for '$scenarioId' with exit code " +
                "$LASTEXITCODE.")
        }
        $rawHash = Get-Sha256Text $raw
        if (-not (Test-SameSha256 `
                $verifiedObservation[0].outputSha256 $rawHash)) {
            throw (
                "Legacy oracle output changed after build for " +
                "'$scenarioId'. Expected " +
                "$($verifiedObservation[0].outputSha256), observed " +
                "$rawHash.")
        }
        $observation = $raw | ConvertFrom-Json
        if ($observation.scenario -cne $scenarioId -or
            $observation.adapterId -cne
                $descriptor.adapterId -or
            $observation.versionId -cne
                $descriptor.versionId -or
            $observation.source -cne $descriptor.source -or
            -not (Test-SameSha256 `
                $observation.sourceSha256 `
                $descriptor.sourceSha256) -or
            $observation.buildRecipe -cne
                $descriptor.buildRecipe -or
            -not (Test-SameSha256 `
                $observation.buildRecipeSha256 `
                $descriptor.buildRecipeSha256) -or
            $observation.caseDefinitionSha256 -cne
                $caseDefinitionHash -or
            $observation.inputSha256 -cne $inputHash) {
            throw (
                "Legacy oracle returned stale bindings for " +
                "'$scenarioId'.")
        }
        $valueCount = @($observation.values).Count
        if ($valueCount -ne $verifiedObservation[0].valueCount) {
            throw (
                "Legacy oracle value count changed after build for " +
                "'$scenarioId'.")
        }

        $fileStem = $scenarioId.Replace('.', '-')
        $fixtureRelative = (
            "tests/parity/fixtures/legacy/$fileStem.json")
        $fixturePath = Join-Path $repositoryRoot $fixtureRelative
        $stagedPath = Join-Path $captureRoot "$fileStem.json"
        $fixtureDocument = [ordered]@{
            schemaVersion = 2
            revision = $provenance.legacy.revision
            tree = $provenance.legacy.tree
            parityId = $scenarioId
            referenceDefinitionSha256 =
                $provenance.reference.definitionSha256
            oracle = [string] $descriptor.source
            legacyOracleVersionId =
                [string] $descriptor.versionId
            oracleSourceSha256 =
                [string] $descriptor.sourceSha256
            oracleBuildRecipeSha256 =
                [string] $descriptor.buildRecipeSha256
            oracleExecutableSha256 =
                [string] $entry.executableSha256
            toolchain = [ordered]@{
                lazarus = $provenance.toolchain.lazarusVersion
                fpc = $provenance.toolchain.fpcVersion
                target = (
                    "$($provenance.toolchain.targetCpu)-" +
                    "$($provenance.toolchain.targetOs)")
                compilerSha256 =
                    $provenance.toolchain.compilerSha256
                backendCompilerSha256 =
                    $provenance.toolchain.backendCompilerSha256
                lazbuildSha256 =
                    $provenance.toolchain.lazbuildSha256
                fingerprintSha256 =
                    $provenance.toolchain.fingerprint.aggregateSha256
            }
            values = @($observation.values)
        }
        $fixtureJson = (
            $fixtureDocument | ConvertTo-Json -Depth 8
        ).Replace("`r`n", "`n").Replace("`r", "`n") + "`n"
        [IO.File]::WriteAllText(
            $stagedPath,
            $fixtureJson,
            [Text.UTF8Encoding]::new($false))

        $staged = Get-Content -LiteralPath $stagedPath -Raw |
            ConvertFrom-Json
        if ($staged.schemaVersion -ne 2 -or
            $staged.parityId -cne $scenarioId -or
            $staged.legacyOracleVersionId -cne
                $descriptor.versionId -or
            @($staged.values).Count -ne $valueCount) {
            throw "Staged fixture validation failed for '$scenarioId'."
        }
        $captures.Add([pscustomobject]@{
            Scenario = $scenarioId
            ValueCount = $valueCount
            StagedPath = $stagedPath
            DestinationPath = $fixturePath
        })
    }

    foreach ($capture in $captures) {
        $destinationDirectory = Split-Path -Parent (
            $capture.DestinationPath)
        New-Item -ItemType Directory -Force `
            -Path $destinationDirectory | Out-Null
        [IO.File]::Move(
            $capture.StagedPath,
            $capture.DestinationPath,
            $true)
        Write-Host (
            "Captured $($capture.ValueCount) live CE values for " +
            "$($capture.Scenario)")
    }
} finally {
    $manifestJsonDocument.Dispose()
    Remove-SafeDirectoryTree `
        -Root $repositoryRoot `
        -Path $captureRoot `
        -Description 'Legacy fixture capture staging'
}

Write-Host (
    'Legacy fixtures were refreshed from their exact versioned adapters. ' +
    'Red or green XPlat evidence was not synthesized.')
