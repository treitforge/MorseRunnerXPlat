[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'LegacyOracleDescriptor.ps1')

function New-Descriptor {
    param(
        [string] $VersionId = 'legacy-oracle-v1',
        [string] $Source =
            'tests/parity/legacy-oracle/v1/LegacyOracle.lpr',
        [string] $Recipe =
            'tests/parity/legacy-oracle/v1/build-recipe.json'
    )

    return [pscustomobject]@{
        versionId = $VersionId
        source = $Source
        buildRecipe = $Recipe
    }
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
    throw "Expected descriptor rejection: $Description"
}

$vectorPath = Join-Path (
    $PSScriptRoot) 'legacy-oracle-descriptor-vectors.json'
$vectorDocument = Get-Content -LiteralPath $vectorPath -Raw |
    ConvertFrom-Json
if ($vectorDocument.schemaVersion -ne 1 -or
    @($vectorDocument.vectors).Count -eq 0) {
    throw 'Legacy oracle descriptor vectors are invalid.'
}
foreach ($vector in $vectorDocument.vectors) {
    $descriptor = New-Descriptor `
        -VersionId ([string] $vector.versionId) `
        -Source ([string] $vector.source) `
        -Recipe ([string] $vector.buildRecipe)
    if ([bool] $vector.valid) {
        Assert-VersionedLegacyOracleDescriptorPath `
            -Descriptor $descriptor `
            -Description ([string] $vector.id)
    } else {
        Assert-Rejected -Description ([string] $vector.id) {
            Assert-VersionedLegacyOracleDescriptorPath `
                -Descriptor $descriptor `
                -Description ([string] $vector.id)
        }
    }
}

$descriptorsByVersion =
    [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::Ordinal)
$caseIdsByVersion =
    [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::Ordinal)
$sharedDescriptor = [pscustomobject]@{
    adapterId = 'LegacyOracleTarget'
    versionId = 'legacy-oracle-v1'
    source = 'tests/parity/legacy-oracle/v1/LegacyOracle.lpr'
    sourceSha256 = ('1' * 64)
    buildRecipe =
        'tests/parity/legacy-oracle/v1/build-recipe.json'
    buildRecipeSha256 = ('2' * 64)
}
Add-LegacyOracleDescriptorVersionBinding `
    -DescriptorsByVersion $descriptorsByVersion `
    -CaseIdsByVersion $caseIdsByVersion `
    -Descriptor $sharedDescriptor `
    -CaseId case.one
Add-LegacyOracleDescriptorVersionBinding `
    -DescriptorsByVersion $descriptorsByVersion `
    -CaseIdsByVersion $caseIdsByVersion `
    -Descriptor $sharedDescriptor `
    -CaseId case.two
if ($descriptorsByVersion.Count -ne 1 -or
    @($caseIdsByVersion['legacy-oracle-v1']).Count -ne 2) {
    throw 'Identical shared descriptors did not group into one version.'
}
$conflictingDescriptor = $sharedDescriptor.PSObject.Copy()
$conflictingDescriptor.source =
    'tests/parity/legacy-oracle/v1/OtherOracle.lpr'
Assert-Rejected -Description 'conflicting shared version descriptor' {
    Add-LegacyOracleDescriptorVersionBinding `
        -DescriptorsByVersion $descriptorsByVersion `
        -CaseIdsByVersion $caseIdsByVersion `
        -Descriptor $conflictingDescriptor `
        -CaseId case.three
}

Write-Host 'Legacy oracle descriptor version-path checks passed.'
