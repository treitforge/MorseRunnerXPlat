[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $EnvelopePath,

    [Parameter(Mandatory)]
    [string] $IntegrationTrxPath,

    [Parameter(Mandatory)]
    [string] $RegistryPath,

    [Parameter(Mandatory)]
    [string] $RegistrySha256,

    [Parameter(Mandatory)]
    [string] $WindowsLegacyResultPath,

    [string] $RepositoryRoot = (
        Resolve-Path (Join-Path $PSScriptRoot '..\..')
    ).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'ParityPathSafety.ps1')

function Get-LowercaseSha256 {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    return (
        Get-FileHash -LiteralPath (
            $Path) -Algorithm SHA256).Hash.ToLowerInvariant()
}

$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$envelope = Assert-SafeDescendantPath `
    -Root $root `
    -Candidate $EnvelopePath `
    -Description 'Build-integration execution envelope'
$trx = Assert-SafeDescendantPath `
    -Root $root `
    -Candidate $IntegrationTrxPath `
    -Description 'Build-integration TRX'
$registry = Assert-SafeDescendantPath `
    -Root $root `
    -Candidate $RegistryPath `
    -Description 'Build-integration registry'
$windowsLegacyResult = Assert-SafeDescendantPath `
    -Root $root `
    -Candidate $WindowsLegacyResultPath `
    -Description 'Windows Legacy full-suite result'
foreach ($path in @(
        $envelope,
        $trx,
        $registry,
        $windowsLegacyResult)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Build-integration attestation file is missing: $path"
    }
}
$envelopeHash = Get-LowercaseSha256 $envelope
if ([IO.Path]::GetFileName($envelope) -cne "$envelopeHash.json") {
    throw (
        'Build-integration execution envelope filename must equal its raw ' +
        'SHA-256.')
}

$document = Get-Content -LiteralPath $envelope -Raw |
    ConvertFrom-Json
$requiredFields = [string[]] @(
    'schemaVersion'
    'target'
    'platform'
    'architecture'
    'runtimeIdentifier'
    'revision'
    'tree'
    'registrySha256'
    'selectedCaseIds'
    'testName'
    'testReportSha256'
    'testProcessExitCode'
    'wrapper'
)
$fields = @($document.PSObject.Properties.Name)
if ($fields.Count -ne $requiredFields.Count -or
    @(
        $requiredFields |
            Where-Object { -not ($fields -ccontains $_) }
    ).Count -ne 0 -or
    [int] $document.schemaVersion -ne 1 -or
    [string] $document.target -cne
        'LegacyOracleBuildIntegration' -or
    [string] $document.platform -cne 'windows' -or
    [string]::IsNullOrWhiteSpace(
        [string] $document.architecture) -or
    [string]::IsNullOrWhiteSpace(
        [string] $document.runtimeIdentifier) -or
    [string] $document.revision -cnotmatch '^[0-9a-f]{40}$' -or
    [string] $document.tree -cnotmatch '^[0-9a-f]{40}$') {
    throw (
        'Build-integration execution envelope has an unexpected schema or ' +
        'runtime identity.')
}
if ([string] $document.registrySha256 -cne $RegistrySha256 -or
    (Get-LowercaseSha256 $registry) -cne $RegistrySha256) {
    throw (
        'Build-integration execution envelope registry binding does not ' +
        'match.')
}
$windowsResult = Get-Content -LiteralPath $windowsLegacyResult -Raw |
    ConvertFrom-Json
$runContext = $windowsResult.runContext
$expectedArchitecture = [string] $runContext.processArchitecture
$expectedRuntimeIdentifier = [string] $runContext.runtimeIdentifier
if ([string] $runContext.platform -cne 'windows' -or
    $expectedArchitecture -cnotmatch
        '^(x86|x64|arm|arm64|wasm|s390x|loongarch64|ppc64le)$' -or
    -not $expectedRuntimeIdentifier.StartsWith(
        'win-',
        [StringComparison]::Ordinal) -or
    -not $expectedRuntimeIdentifier.EndsWith(
        "-$expectedArchitecture",
        [StringComparison]::Ordinal) -or
    [string] $document.architecture -cne $expectedArchitecture -or
    [string] $document.runtimeIdentifier -cne
        $expectedRuntimeIdentifier -or
    [string] $document.revision -cne
        [string] $runContext.xplat.revision -or
    [string] $document.tree -cne
        [string] $runContext.xplat.tree) {
    throw (
        'Build-integration execution envelope runtime identity does not ' +
        'match the Windows Legacy full-suite result.')
}
$expectedTestName =
    'MorseRunner.LegacyParity.Tests.LegacyOracleTargetTests.' +
    'ConfiguredBuildRegistryAttestsAndExecutesEverySelectedCase'
if ([string] $document.testName -cne $expectedTestName -or
    [string] $document.testReportSha256 -cne
        (Get-LowercaseSha256 $trx) -or
    [int] $document.testProcessExitCode -ne 0) {
    throw (
        'Build-integration execution envelope does not bind the exact ' +
        'passing test process and TRX.')
}
$wrapperFields = @($document.wrapper.PSObject.Properties.Name)
if ($wrapperFields.Count -ne 3 -or
    -not ($wrapperFields -ccontains 'completed') -or
    -not ($wrapperFields -ccontains 'correlationValidated') -or
    -not ($wrapperFields -ccontains 'exitCode') -or
    [bool] $document.wrapper.completed -ne $true -or
    [bool] $document.wrapper.correlationValidated -ne $true -or
    [int] $document.wrapper.exitCode -ne 0) {
    throw 'Build-integration execution wrapper is not an exact success.'
}

$registryDocument = Get-Content -LiteralPath $registry -Raw |
    ConvertFrom-Json
$expectedCaseIds = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($entry in $registryDocument.entries) {
    $provenancePath = Join-Path (
        $root) ([string] $entry.provenance).Replace(
            '/', [IO.Path]::DirectorySeparatorChar)
    $provenance = Get-Content `
        -LiteralPath $provenancePath `
        -Raw |
        ConvertFrom-Json
    foreach ($caseId in $provenance.selectedCaseIds) {
        if (-not $expectedCaseIds.Add([string] $caseId)) {
            throw (
                'Build-integration registry provenance repeats selected ' +
                "case ID: $caseId")
        }
    }
}
$expectedOrdered = [string[]] @($expectedCaseIds)
[Array]::Sort($expectedOrdered, [StringComparer]::Ordinal)
$actualOrdered = [string[]] @($document.selectedCaseIds)
if ($expectedOrdered.Count -eq 0 -or
    $actualOrdered.Count -ne $expectedOrdered.Count -or
    ($actualOrdered -join "`0") -cne
        ($expectedOrdered -join "`0")) {
    throw (
        'Build-integration execution envelope selected case IDs do not ' +
        'match registry provenance.')
}

Write-Host (
    'Validated the retained Legacy oracle build-integration execution ' +
    "envelope $envelopeHash.")
