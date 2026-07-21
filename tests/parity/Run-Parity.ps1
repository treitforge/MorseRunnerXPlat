[CmdletBinding()]
param(
    [ValidateSet('Legacy', 'XPlat', 'Both')]
    [string] $Target = 'Both',

    [ValidateSet('Baseline', 'PullRequest', 'Development', 'Release')]
    [string] $Mode = 'Development',

    [string] $LegacyRoot = (Join-Path $PSScriptRoot '..\..\..\MorseRunner'),

    [string] $LazarusRoot = 'C:\lazarus',

    [string[]] $PromoteCaseId,

    [ValidateSet('Red', 'Green')]
    [string] $PromotionKind,

    [string[]] $GreenResult,

    [string[]] $GreenTestReport,

    [string[]] $GreenExecution,

    [string[]] $CaptureGreenCaseId,

    [string[]] $GreenRegressionCaseId,

    [string[]] $FullSuiteResult,

    [string[]] $FullSuiteTestReport,

    [string[]] $FullSuiteExecution,

    [string] $FullSuiteLegacyOracleRegistry,

    [string] $FullSuiteLegacyOracleRegistrySha256,

    [string] $FullSuitePackageIndex,

    [string] $FullSuitePackageIndexSha256
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ParityPathSafety.ps1')
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$testProject = Join-Path (
    $repositoryRoot
) 'tests\MorseRunner.LegacyParity.Tests\MorseRunner.LegacyParity.Tests.csproj'
$resultsRoot = Assert-SafeDescendantPath `
    -Root $repositoryRoot `
    -Candidate (Join-Path $repositoryRoot 'artifacts\parity') `
    -Description 'Parity results'
$preparedLegacyRoot = Join-Path $repositoryRoot 'artifacts\legacy-reference'
$manifestPath = Join-Path $repositoryRoot (
    'tests\parity\parity-manifest.json')
$manifest = Get-Content -LiteralPath $manifestPath -Raw |
    ConvertFrom-Json
if ($manifest.schemaVersion -ne 3) {
    throw "Unsupported parity manifest schema: $($manifest.schemaVersion)"
}
$certificationPlatform = if (
    [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [Runtime.InteropServices.OSPlatform]::Windows)
) {
    'windows'
} elseif (
    [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [Runtime.InteropServices.OSPlatform]::OSX)
) {
    'macos'
} else {
    'linux'
}
$applicableCases = @(
    $manifest.cases |
        Where-Object {
            @($_.platforms) -ccontains $certificationPlatform
        })
if ($applicableCases.Count -eq 0) {
    throw (
        'The parity manifest has no active cases for ' +
        "$certificationPlatform.")
}

if ($Mode -eq 'Release' -and $Target -ne 'Both') {
    throw 'Release parity requires -Target Both.'
}
if ($Mode -eq 'Release') {
    $releaseStatus = @(
        & git -C $repositoryRoot status --porcelain=v2 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not inspect the XPlat worktree for Release parity.'
    }
    if ($releaseStatus.Count -ne 0) {
        throw (
            "Release parity requires a clean XPlat worktree:`n" +
            ($releaseStatus -join "`n"))
    }
}

$greenResultArguments = [Collections.Generic.List[string]]::new()
$greenResultPlatforms =
    [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
$greenTestReportArguments =
    [Collections.Generic.List[string]]::new()
$greenTestReportPlatforms =
    [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
$greenExecutionArguments =
    [Collections.Generic.List[string]]::new()
$greenExecutionPlatforms =
    [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)

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

function Add-GreenPromotionArtifact {
    param(
        [Parameter(Mandatory)]
        [string] $Value,

        [Parameter(Mandatory)]
        [ValidateSet('result', 'test report', 'execution')]
        [string] $ArtifactKind,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [Collections.Generic.HashSet[string]] $Platforms,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [Collections.Generic.List[string]] $Arguments
    )

    if ($Value -cnotmatch
        '^(windows|linux|macos)=(.+)$') {
        throw (
            "Green $ArtifactKind '$Value' must use platform=path.")
    }
    $greenPlatform = $Matches[1]
    if (-not $Platforms.Add($greenPlatform)) {
        throw (
            "Green $ArtifactKind platform '$greenPlatform' is duplicated.")
    }
    $greenCandidate = if (
        [IO.Path]::IsPathFullyQualified($Matches[2])
    ) {
        $Matches[2]
    } else {
        Join-Path $repositoryRoot $Matches[2]
    }
    $greenPath = Assert-SafeDescendantPath `
        -Root $repositoryRoot `
        -Candidate $greenCandidate `
        -Description "Green $ArtifactKind '$greenPlatform'"
    $artifactsRoot = [IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot 'artifacts'))
    $platformIsWindows =
        [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [Runtime.InteropServices.OSPlatform]::Windows)
    $greenPathComparison = if ($platformIsWindows) {
        [StringComparison]::OrdinalIgnoreCase
    } else {
        [StringComparison]::Ordinal
    }
    $artifactsPrefix = $artifactsRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    ) + [IO.Path]::DirectorySeparatorChar
    if (-not $greenPath.StartsWith(
            $artifactsPrefix,
            $greenPathComparison)) {
        throw "Green $ArtifactKind must be below $artifactsRoot."
    }
    $resultsPrefix = $resultsRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    ) + [IO.Path]::DirectorySeparatorChar
    if ($greenPath.StartsWith(
            $resultsPrefix,
            $greenPathComparison)) {
        throw (
            "External green $ArtifactKind files cannot be stored below " +
            'the parity ' +
            'results root that is cleared at startup.')
    }
    if (-not (Test-Path -LiteralPath $greenPath -PathType Leaf)) {
        throw "Green $ArtifactKind does not exist: $greenPath"
    }
    $Arguments.Add("$greenPlatform=$greenPath")
}

if ($null -ne $GreenResult) {
    foreach ($greenResultValue in $GreenResult) {
        Add-GreenPromotionArtifact `
            -Value $greenResultValue `
            -ArtifactKind result `
            -Platforms $greenResultPlatforms `
            -Arguments $greenResultArguments
    }
}
if ($null -ne $GreenTestReport) {
    foreach ($greenTestReportValue in $GreenTestReport) {
        Add-GreenPromotionArtifact `
            -Value $greenTestReportValue `
            -ArtifactKind 'test report' `
            -Platforms $greenTestReportPlatforms `
            -Arguments $greenTestReportArguments
    }
}
if ($null -ne $GreenExecution) {
    foreach ($greenExecutionValue in $GreenExecution) {
        Add-GreenPromotionArtifact `
            -Value $greenExecutionValue `
            -ArtifactKind execution `
            -Platforms $greenExecutionPlatforms `
            -Arguments $greenExecutionArguments
    }
}

$fullSuiteResultArguments =
    [Collections.Generic.List[string]]::new()
$fullSuiteTestReportArguments =
    [Collections.Generic.List[string]]::new()
$fullSuiteExecutionArguments =
    [Collections.Generic.List[string]]::new()
$fullSuiteResultTargets =
    [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
$fullSuiteTestReportTargets =
    [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
$fullSuiteExecutionTargets =
    [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
$fullSuiteResultPaths = @{}
$fullSuiteTestReportPaths = @{}
$fullSuiteExecutionPaths = @{}
$fullSuiteLegacyOracleRegistryPath = $null
$fullSuitePackageIndexPath = $null
$fullSuitePackageRoot = $null
$fullSuitePackageIndexDocument = $null

function Add-FullSuiteArtifact {
    param(
        [Parameter(Mandatory)]
        [string] $Value,

        [Parameter(Mandatory)]
        [ValidateSet('result', 'test report', 'execution')]
        [string] $ArtifactKind,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [Collections.Generic.HashSet[string]] $Targets,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [Collections.Generic.List[string]] $Arguments,

        [Parameter(Mandatory)]
        [hashtable] $Paths
    )

    if ($Value -cnotmatch
        '^(windows|linux|macos)/(Legacy|XPlat)=(.+)$') {
        throw (
            "Full-suite $ArtifactKind '$Value' must use " +
            'platform/Target=path.')
    }
    $artifactKey = "$($Matches[1])/$($Matches[2])"
    if (-not $Targets.Add($artifactKey)) {
        throw (
            "Full-suite $ArtifactKind key '$artifactKey' is " +
            'duplicated.')
    }
    $candidate = if ([IO.Path]::IsPathFullyQualified($Matches[3])) {
        $Matches[3]
    } else {
        Join-Path $repositoryRoot $Matches[3]
    }
    $path = Assert-SafeDescendantPath `
        -Root $repositoryRoot `
        -Candidate $candidate `
        -Description "Full-suite $ArtifactKind '$artifactKey'"
    $artifactsRoot = [IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot 'artifacts'))
    $comparison = if (
        [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [Runtime.InteropServices.OSPlatform]::Windows)
    ) {
        [StringComparison]::OrdinalIgnoreCase
    } else {
        [StringComparison]::Ordinal
    }
    $artifactsPrefix = $artifactsRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    ) + [IO.Path]::DirectorySeparatorChar
    if (-not $path.StartsWith($artifactsPrefix, $comparison)) {
        throw "Full-suite $ArtifactKind must be below $artifactsRoot."
    }
    $resultsPrefix = $resultsRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    ) + [IO.Path]::DirectorySeparatorChar
    if ($path.StartsWith($resultsPrefix, $comparison)) {
        throw (
            "Full-suite $ArtifactKind cannot be below the parity " +
            'results root that is cleared at startup.')
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Full-suite $ArtifactKind does not exist: $path"
    }
    $Paths[$artifactKey] = $path
    $Arguments.Add("$artifactKey=$path")
}

if ($null -ne $FullSuiteResult) {
    foreach ($value in $FullSuiteResult) {
        Add-FullSuiteArtifact `
            -Value $value `
            -ArtifactKind result `
            -Targets $fullSuiteResultTargets `
            -Arguments $fullSuiteResultArguments `
            -Paths $fullSuiteResultPaths
    }
}
if ($null -ne $FullSuiteTestReport) {
    foreach ($value in $FullSuiteTestReport) {
        Add-FullSuiteArtifact `
            -Value $value `
            -ArtifactKind 'test report' `
            -Targets $fullSuiteTestReportTargets `
            -Arguments $fullSuiteTestReportArguments `
            -Paths $fullSuiteTestReportPaths
    }
}
if ($null -ne $FullSuiteExecution) {
    foreach ($value in $FullSuiteExecution) {
        Add-FullSuiteArtifact `
            -Value $value `
            -ArtifactKind execution `
            -Targets $fullSuiteExecutionTargets `
            -Arguments $fullSuiteExecutionArguments `
            -Paths $fullSuiteExecutionPaths
    }
}
if (-not [string]::IsNullOrWhiteSpace(
        $FullSuiteLegacyOracleRegistry)) {
    if ([string]::IsNullOrWhiteSpace(
            $FullSuiteLegacyOracleRegistrySha256)) {
        throw (
            '-FullSuiteLegacyOracleRegistry requires ' +
            '-FullSuiteLegacyOracleRegistrySha256.')
    }
    $candidate = if ([IO.Path]::IsPathFullyQualified(
            $FullSuiteLegacyOracleRegistry)) {
        $FullSuiteLegacyOracleRegistry
    } else {
        Join-Path $repositoryRoot $FullSuiteLegacyOracleRegistry
    }
    $fullSuiteLegacyOracleRegistryPath =
        Assert-SafeDescendantPath `
            -Root $repositoryRoot `
            -Candidate $candidate `
            -Description 'Full-suite Legacy oracle registry'
} elseif (-not [string]::IsNullOrWhiteSpace(
        $FullSuiteLegacyOracleRegistrySha256)) {
    throw (
        '-FullSuiteLegacyOracleRegistrySha256 requires ' +
        '-FullSuiteLegacyOracleRegistry.')
}
if (-not [string]::IsNullOrWhiteSpace($FullSuitePackageIndex)) {
    if ([string]::IsNullOrWhiteSpace($FullSuitePackageIndexSha256)) {
        throw (
            '-FullSuitePackageIndex requires ' +
            '-FullSuitePackageIndexSha256.')
    }
    $candidate = if ([IO.Path]::IsPathFullyQualified(
            $FullSuitePackageIndex)) {
        $FullSuitePackageIndex
    } else {
        Join-Path $repositoryRoot $FullSuitePackageIndex
    }
    $fullSuitePackageIndexPath = Assert-SafeDescendantPath `
        -Root $repositoryRoot `
        -Candidate $candidate `
        -Description 'Full-suite package index'
    if ($FullSuitePackageIndexSha256 -cnotmatch
        '^[0-9a-f]{64}$') {
        throw (
            'Full-suite package index SHA-256 must be lowercase ' +
            'hexadecimal.')
    }
    $fullSuitePackageRoot = Assert-SafeDescendantPath `
        -Root $repositoryRoot `
        -Candidate (
            Join-Path (
                $repositoryRoot
            ) "artifacts\parity-imports\$FullSuitePackageIndexSha256") `
        -Description 'Imported full-suite package root'
    if (-not (Test-Path `
            -LiteralPath $fullSuitePackageRoot `
            -PathType Container)) {
        throw (
            'Imported full-suite package root does not exist: ' +
            $fullSuitePackageRoot)
    }
    $expectedIndexPath = Join-Path (
        $fullSuitePackageRoot
    ) (
        'artifacts\parity-full-suite\' +
        'windows-both-package-index\' +
        "$FullSuitePackageIndexSha256.json")
    if ([IO.Path]::GetFullPath($fullSuitePackageIndexPath) -cne
        [IO.Path]::GetFullPath($expectedIndexPath)) {
        throw (
            'Full-suite package index must be under the immutable import ' +
            'root named by its SHA-256.')
    }
    & (
        Join-Path (
            $PSScriptRoot
        ) 'Assert-ParityFullSuitePackage.ps1') `
        -RepositoryRoot $fullSuitePackageRoot `
        -IndexPath $fullSuitePackageIndexPath `
        -IndexSha256 $FullSuitePackageIndexSha256
    if ($LASTEXITCODE -ne 0) {
        throw (
            'Downloaded full-suite package closure validation failed with ' +
            "exit code $LASTEXITCODE.")
    }
    $fullSuitePackageIndexDocument = Get-Content `
        -LiteralPath $fullSuitePackageIndexPath `
        -Raw |
        ConvertFrom-Json
    $boundRegistryPath = Join-Path (
        $fullSuitePackageRoot
    ) ([string] $fullSuitePackageIndexDocument.registry.path).Replace(
        '/', [IO.Path]::DirectorySeparatorChar)
    if ($null -eq $fullSuiteLegacyOracleRegistryPath -or
        [IO.Path]::GetFullPath($boundRegistryPath) -cne
            [IO.Path]::GetFullPath(
                $fullSuiteLegacyOracleRegistryPath) -or
        [string] $fullSuitePackageIndexDocument.registry.sha256 -cne
            $FullSuiteLegacyOracleRegistrySha256) {
        throw (
            'The full-suite package index registry binding must exactly ' +
            'match the supplied Legacy oracle registry and SHA-256.')
    }
} elseif (-not [string]::IsNullOrWhiteSpace(
        $FullSuitePackageIndexSha256)) {
    throw (
        '-FullSuitePackageIndexSha256 requires ' +
        '-FullSuitePackageIndex.')
}

$savedEnvironment = @{
    Target = $env:MORSE_RUNNER_PARITY_TARGET
    Results = $env:MORSE_RUNNER_PARITY_RESULTS
    LegacyRoot = $env:MORSE_RUNNER_LEGACY_ROOT
    Oracle = $env:MORSE_RUNNER_LEGACY_ORACLE
    Provenance = $env:MORSE_RUNNER_LEGACY_PROVENANCE
    OracleSha256 = $env:MORSE_RUNNER_LEGACY_ORACLE_SHA256
    ProvenanceSha256 = $env:MORSE_RUNNER_LEGACY_PROVENANCE_SHA256
    Registry = $env:MORSE_RUNNER_LEGACY_ORACLE_REGISTRY
    RegistrySha256 =
        $env:MORSE_RUNNER_LEGACY_ORACLE_REGISTRY_SHA256
    SelectedCaseIds = $env:MORSE_RUNNER_PARITY_CASE_IDS
}
$targetExitCodes = @{}
$resultPaths = @{}
$trxPaths = @{}
$executionPaths = @{}
$integrationExecutionPath = $null
$legacyRegistryPath = $null
$legacyRegistryHash = $null
$selectedPromotionKind = $null
$captureGreenCases = @()
$greenRegressionCases = @()
$localPromotionCaseIds = @()

if (-not $PromoteCaseId -and
    -not $CaptureGreenCaseId -and
    -not [string]::IsNullOrWhiteSpace(
        [string] $savedEnvironment.SelectedCaseIds)) {
    throw (
        'Ordinary parity runs forbid inherited ' +
        'MORSE_RUNNER_PARITY_CASE_IDS selection.')
}

if ($PromoteCaseId) {
    if ($Mode -ne 'Baseline') {
        throw 'Evidence promotion requires -Mode Baseline.'
    }

    if (@($PromoteCaseId | Select-Object -Unique).Count -ne
        @($PromoteCaseId).Count) {
        throw 'Evidence promotion case IDs must be unique.'
    }
    $promotionCases = @(
        foreach ($promotionCaseId in $PromoteCaseId) {
            $matches = @(
                $manifest.cases |
                    Where-Object { $_.id -ceq $promotionCaseId })
            if ($matches.Count -ne 1) {
                throw (
                    'Evidence promotion must identify one active case: ' +
                    $promotionCaseId)
            }
            $matches[0]
        }
    )
    if ($promotionCases.Count -eq 0) {
        throw 'Evidence promotion requires at least one active case.'
    }
    $localPromotionCaseIds = @(
        $promotionCases |
            Where-Object {
                @($_.platforms) -ccontains $certificationPlatform
            } |
            ForEach-Object { $_.id })
    if ($PromotionKind) {
        $selectedPromotionKind = $PromotionKind.ToLowerInvariant()
    } else {
        $existingEvidenceCount = @(
            $promotionCases |
                Where-Object {
                    Test-Path -LiteralPath (
                        Join-Path $repositoryRoot $_.evidence
                    ) -PathType Leaf
                }).Count
        if ($existingEvidenceCount -eq 0) {
            $selectedPromotionKind = 'red'
        } elseif ($existingEvidenceCount -eq
            $promotionCases.Count) {
            $selectedPromotionKind = 'green'
        } else {
            throw (
                'Cannot infer one atomic promotion kind for a mixed batch. ' +
                'Use separate all-red and all-green batches.')
        }
    }
    if ($selectedPromotionKind -eq 'red' -and $Target -ne 'Both') {
        throw 'Red evidence promotion requires -Target Both.'
    }
    if ($selectedPromotionKind -eq 'green' -and
        ($Target -ne 'Both' -or
            $certificationPlatform -ne 'windows')) {
        throw (
            'Green evidence promotion requires -Target Both on Windows ' +
            'for the live Legacy certification gate.')
    }
} elseif ($PromotionKind) {
    throw '-PromotionKind requires -PromoteCaseId.'
}
if ($CaptureGreenCaseId) {
    if ($PromoteCaseId -or $PromotionKind -or
        $GreenRegressionCaseId -or
        $greenResultArguments.Count -gt 0 -or
        $greenTestReportArguments.Count -gt 0 -or
        $greenExecutionArguments.Count -gt 0) {
        throw (
            '-CaptureGreenCaseId cannot be combined with evidence ' +
            'promotion arguments.')
    }
    if ($Target -ne 'XPlat' -or $Mode -ne 'Baseline') {
        throw (
            '-CaptureGreenCaseId requires -Target XPlat and ' +
            '-Mode Baseline.')
    }
    if (@($CaptureGreenCaseId | Select-Object -Unique).Count -ne
        @($CaptureGreenCaseId).Count) {
        throw 'Green capture case IDs must be unique.'
    }
    $captureGreenCases = @(
        foreach ($captureCaseId in $CaptureGreenCaseId) {
            $matches = @(
                $applicableCases |
                    Where-Object { $_.id -ceq $captureCaseId })
            if ($matches.Count -ne 1) {
                throw (
                    'Green capture must identify one active applicable ' +
                    "case: $captureCaseId")
            }
            if ($matches[0].status -cne
                'legacy-green-xplat-red') {
                throw (
                    "Green capture case '$captureCaseId' is not " +
                    'manifest-red.')
            }
            $matches[0]
        })
    if ($captureGreenCases.Count -eq 0) {
        throw 'Green capture requires at least one case.'
    }
}
if ($GreenRegressionCaseId) {
    if ($PromoteCaseId -or $PromotionKind -or
        $CaptureGreenCaseId -or
        $greenResultArguments.Count -gt 0 -or
        $greenTestReportArguments.Count -gt 0 -or
        $greenExecutionArguments.Count -gt 0 -or
        $fullSuiteResultArguments.Count -gt 0 -or
        $fullSuiteTestReportArguments.Count -gt 0 -or
        $fullSuiteExecutionArguments.Count -gt 0) {
        throw (
            '-GreenRegressionCaseId cannot be combined with capture or ' +
            'promotion artifacts.')
    }
    if ($Target -notin @('XPlat', 'Both') -or
        $Mode -ne 'Development') {
        throw (
            '-GreenRegressionCaseId requires an XPlat target and ' +
            '-Mode Development.')
    }
    if (@(
            $GreenRegressionCaseId |
                Select-Object -Unique
        ).Count -ne @($GreenRegressionCaseId).Count) {
        throw 'Green regression case IDs must be unique.'
    }
    $greenRegressionCases = @(
        foreach ($regressionCaseId in $GreenRegressionCaseId) {
            $matches = @(
                $applicableCases |
                    Where-Object { $_.id -ceq $regressionCaseId })
            if ($matches.Count -ne 1 -or
                $matches[0].status -cne
                    'legacy-green-xplat-red') {
                throw (
                    'Green regression must identify one applicable ' +
                    "manifest-red case: $regressionCaseId")
            }
            $matches[0]
        })
    if ($greenRegressionCases.Count -eq 0) {
        throw 'Green regression validation requires at least one case.'
    }
}
if ($greenResultArguments.Count -gt 0 -and
    (-not $PromoteCaseId -or
        $selectedPromotionKind -ne 'green')) {
    throw (
        '-GreenResult is valid only for an atomic green evidence promotion.')
}
if ($greenTestReportArguments.Count -gt 0 -and
    (-not $PromoteCaseId -or
        $selectedPromotionKind -ne 'green')) {
    throw (
        '-GreenTestReport is valid only for an atomic green evidence ' +
        'promotion.')
}
if ($greenExecutionArguments.Count -gt 0 -and
    (-not $PromoteCaseId -or
        $selectedPromotionKind -ne 'green')) {
    throw (
        '-GreenExecution is valid only for an atomic green evidence ' +
        'promotion.')
}
if ($greenResultPlatforms.Count -ne $greenTestReportPlatforms.Count -or
    $greenResultPlatforms.Count -ne $greenExecutionPlatforms.Count -or
    @(
        $greenResultPlatforms |
            Where-Object { -not $greenTestReportPlatforms.Contains($_) }
    ).Count -ne 0 -or
    @(
        $greenResultPlatforms |
            Where-Object { -not $greenExecutionPlatforms.Contains($_) }
    ).Count -ne 0) {
    throw (
        '-GreenResult, -GreenTestReport, and -GreenExecution must name ' +
        'the same platforms.')
}
$hasFullSuiteArtifacts =
    $fullSuiteResultArguments.Count -gt 0 -or
    $fullSuiteTestReportArguments.Count -gt 0 -or
    $fullSuiteExecutionArguments.Count -gt 0
if ($hasFullSuiteArtifacts -and
    (-not $PromoteCaseId -or
        $selectedPromotionKind -ne 'green')) {
    throw (
        'Full-suite artifacts are valid only for atomic green ' +
        'promotion.')
}
$hasFullSuiteLegacyOracleRegistry =
    $null -ne $fullSuiteLegacyOracleRegistryPath
$hasFullSuitePackageIndex = $null -ne $fullSuitePackageIndexPath
if ($hasFullSuiteLegacyOracleRegistry -and
    (-not $PromoteCaseId -or
        $selectedPromotionKind -ne 'green')) {
    throw (
        'The full-suite Legacy oracle registry is valid only for atomic ' +
        'green promotion.')
}
if ($hasFullSuitePackageIndex -and
    (-not $PromoteCaseId -or
        $selectedPromotionKind -ne 'green')) {
    throw (
        'The full-suite package index is valid only for atomic green ' +
        'promotion.')
}
if ($PromoteCaseId -and $selectedPromotionKind -eq 'green') {
    $requiredFullSuiteTargets = [string[]] @(
        'windows/Legacy'
        'windows/XPlat'
        'linux/XPlat'
        'macos/XPlat'
    )
    if ($fullSuiteResultTargets.Count -ne
            $requiredFullSuiteTargets.Count -or
        $fullSuiteTestReportTargets.Count -ne
            $requiredFullSuiteTargets.Count -or
        $fullSuiteExecutionTargets.Count -ne
            $requiredFullSuiteTargets.Count -or
        @(
            $requiredFullSuiteTargets |
                Where-Object {
                    -not $fullSuiteResultTargets.Contains($_)
                }
        ).Count -ne 0 -or
        @(
            $requiredFullSuiteTargets |
                Where-Object {
                    -not $fullSuiteTestReportTargets.Contains($_)
                }
        ).Count -ne 0 -or
        @(
            $requiredFullSuiteTargets |
                Where-Object {
                    -not $fullSuiteExecutionTargets.Contains($_)
                }
        ).Count -ne 0) {
        throw (
            'Green promotion requires full-suite XPlat result, TRX, and ' +
            'execution artifacts for every selected case platform.')
    }
    if (-not $hasFullSuiteLegacyOracleRegistry) {
        throw (
            'Green promotion requires the exact external Windows ' +
            'full-suite Legacy oracle registry, SHA-256, executable, and ' +
            'provenance artifact set.')
    }
    if (-not $hasFullSuitePackageIndex) {
        throw (
            'Green promotion requires the content-addressed Windows ' +
            'full-suite package index and SHA-256.')
    }
    $packageParityRoot = Join-Path (
        $fullSuitePackageRoot) 'artifacts\parity'
    $expectedWindowsArtifacts =
        [Collections.Generic.List[object]]::new()
    $expectedWindowsArtifacts.Add([pscustomobject]@{
            paths = $fullSuiteResultPaths
            key = 'windows/Legacy'
            path = Join-Path $packageParityRoot 'legacy.json'
        })
    $expectedWindowsArtifacts.Add([pscustomobject]@{
            paths = $fullSuiteResultPaths
            key = 'windows/XPlat'
            path = Join-Path $packageParityRoot 'xplat.json'
        })
    $expectedWindowsArtifacts.Add([pscustomobject]@{
            paths = $fullSuiteTestReportPaths
            key = 'windows/Legacy'
            path = Join-Path (
                $packageParityRoot) 'test-results\legacy.trx'
        })
    $expectedWindowsArtifacts.Add([pscustomobject]@{
            paths = $fullSuiteTestReportPaths
            key = 'windows/XPlat'
            path = Join-Path (
                $packageParityRoot) 'test-results\xplat.trx'
        })
    foreach ($targetName in @('Legacy', 'XPlat')) {
        $targetLower = $targetName.ToLowerInvariant()
        $prefix =
            "artifacts/parity/executions/$targetLower/"
        $matches = @(
            $fullSuitePackageIndexDocument.files |
                Where-Object {
                    ([string] $_.path).StartsWith(
                        $prefix,
                        [StringComparison]::Ordinal) -and
                    ([string] $_.path).EndsWith(
                        '.json',
                        [StringComparison]::Ordinal)
                })
        if ($matches.Count -ne 1) {
            throw (
                'The imported package index must bind exactly one ' +
                "$targetName execution envelope.")
        }
        $expectedWindowsArtifacts.Add([pscustomobject]@{
                paths = $fullSuiteExecutionPaths
                key = "windows/$targetName"
                path = Join-Path (
                    $fullSuitePackageRoot
                ) ([string] $matches[0].path).Replace(
                    '/', [IO.Path]::DirectorySeparatorChar)
            })
    }
    foreach ($binding in $expectedWindowsArtifacts) {
        $paths = [hashtable] $binding.paths
        $key = [string] $binding.key
        $expectedPath = [IO.Path]::GetFullPath(
            [string] $binding.path)
        if (-not $paths.ContainsKey($key) -or
            [IO.Path]::GetFullPath(
                [string] $paths[$key]) -cne $expectedPath) {
            throw (
                "Windows full-suite '$key' must use the exact file " +
                'bound by the immutable imported package index.')
        }
    }
}
if ($PromoteCaseId -and
    $selectedPromotionKind -eq 'red' -and
    $localPromotionCaseIds.Count -ne $promotionCases.Count) {
    throw (
        'Red promotion cases must all apply to the current platform.')
}
$externalGreenCoversCurrentPlatform =
    $greenResultPlatforms.Contains($certificationPlatform)
$fullyExternalGreenPromotion =
    $PromoteCaseId -and
    $selectedPromotionKind -eq 'green' -and
    $greenResultPlatforms.Count -gt 0 -and
    $localPromotionCaseIds.Count -eq 0
$runLegacyTarget =
    $Target -in @('Legacy', 'Both')
$runXPlatTarget =
    $Target -in @('XPlat', 'Both') -and
    -not $fullyExternalGreenPromotion -and
    -not (
        $PromoteCaseId -and
        $selectedPromotionKind -eq 'green' -and
        $externalGreenCoversCurrentPlatform)
$focusedRedPromotion =
    $PromoteCaseId -and $selectedPromotionKind -eq 'red'

Remove-SafeDirectoryTree `
    -Root $repositoryRoot `
    -Path $resultsRoot `
    -Description 'Parity results'
New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null
$trxRoot = Join-Path $resultsRoot 'test-results'
New-Item -ItemType Directory -Force -Path $trxRoot | Out-Null

function Get-LowercaseFileSha256 {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    return (
        Get-FileHash -LiteralPath (
            $Path) -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-Utf8LfAtomic {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Value
    )

    $temporaryPath = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText(
            $temporaryPath,
            $Value.Replace("`r`n", "`n").Replace("`r", "`n"),
            [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryPath, $Path)
    } finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function New-ParityExecutionEnvelope {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Legacy', 'XPlat')]
        [string] $SelectedTarget
    )

    $resultPath = $resultPaths[$SelectedTarget]
    $testReportPath = $trxPaths[$SelectedTarget]
    $document = Get-Content -LiteralPath $resultPath -Raw |
        ConvertFrom-Json
    $functionalDivergencesByCase =
        [Collections.Generic.SortedDictionary[string, object]]::new(
            [StringComparer]::Ordinal)
    foreach ($result in @(
            $document.results |
                Where-Object {
                    $_.outcome -ceq 'functional-divergence'
                }
        )) {
        if ([string]::IsNullOrWhiteSpace(
                [string] $result.failureCode)) {
            throw (
                "Functional divergence '$($result.parityId)' has no " +
                'failure code for its execution envelope.')
        }
        $functionalDivergencesByCase.Add(
            [string] $result.parityId,
            [ordered]@{
                caseId = [string] $result.parityId
                failureCode = [string] $result.failureCode
            })
    }
    $functionalDivergences = @(
        $functionalDivergencesByCase.Values)
    $revision = (
        & git -C $repositoryRoot rev-parse --verify 'HEAD^{commit}'
    ).Trim()
    if ($LASTEXITCODE -ne 0 -or
        $revision -cnotmatch '^[0-9a-f]{40}$') {
        throw "Could not bind $SelectedTarget execution to a revision."
    }
    $tree = (
        & git -C $repositoryRoot rev-parse --verify 'HEAD^{tree}'
    ).Trim()
    if ($LASTEXITCODE -ne 0 -or
        $tree -cnotmatch '^[0-9a-f]{40}$') {
        throw "Could not bind $SelectedTarget execution to a tree."
    }
    $envelope = [ordered]@{
        schemaVersion = 1
        target = $SelectedTarget
        platform = $certificationPlatform
        operatingSystem = (
            [Runtime.InteropServices.RuntimeInformation]::
                OSDescription.Trim())
        architecture = (
            [Runtime.InteropServices.RuntimeInformation]::
                ProcessArchitecture.ToString().ToLowerInvariant())
        runtimeIdentifier = (
            [Runtime.InteropServices.RuntimeInformation]::
                RuntimeIdentifier)
        revision = $revision
        tree = $tree
        resultSha256 = Get-LowercaseFileSha256 $resultPath
        testReportSha256 =
            Get-LowercaseFileSha256 $testReportPath
        testProcessExitCode =
            [int] $targetExitCodes[$SelectedTarget]
        wrapper = [ordered]@{
            completed = $true
            correlationValidated = $true
            exitCode = 0
        }
        functionalDivergences = $functionalDivergences
    }
    $raw = (
        $envelope | ConvertTo-Json -Depth 6
    ).Replace("`r`n", "`n").Replace("`r", "`n") + "`n"
    $rawBytes = [Text.UTF8Encoding]::new($false).GetBytes($raw)
    $rawHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $rawBytes)
    ).ToLowerInvariant()
    $executionRoot = Assert-SafeDescendantPath `
        -Root $repositoryRoot `
        -Candidate (Join-Path (
            $resultsRoot
        ) "executions\$($SelectedTarget.ToLowerInvariant())") `
        -Description "$SelectedTarget execution envelopes"
    New-Item -ItemType Directory -Force -Path (
        $executionRoot) | Out-Null
    $path = Join-Path $executionRoot "$rawHash.json"
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        if ((Get-LowercaseFileSha256 $path) -cne $rawHash) {
            throw (
                'Existing content-addressed execution envelope has ' +
                "unexpected bytes: $path")
        }
    } else {
        Write-Utf8LfAtomic -Path $path -Value $raw
    }
    return $path
}

function Invoke-LegacyOracleBuildIntegration {
    param(
        [Parameter(Mandatory)]
        [string[]] $SelectedCaseIds
    )

    $env:MORSE_RUNNER_PARITY_CASE_IDS = ConvertTo-Json `
        -InputObject ([string[]] $SelectedCaseIds) `
        -Compress
    $fileName = 'legacy-oracle-build-integration.trx'
    $path = Join-Path $trxRoot $fileName
    & dotnet test `
        --project $testProject `
        --configuration Release `
        --results-directory $trxRoot `
        -- `
        --filter-trait 'Category=LegacyOracleBuildIntegration' `
        --minimum-expected-tests 1 `
        --report-xunit-trx `
        --report-xunit-trx-filename $fileName
    $exitCode = $LASTEXITCODE
    & (
        Join-Path (
            $PSScriptRoot
        ) 'Assert-LegacyOracleBuildIntegration.ps1') `
        -TrxPath $path `
        -ProcessExitCode $exitCode
    $registryDocument = Get-Content `
        -LiteralPath $legacyRegistryPath `
        -Raw |
        ConvertFrom-Json
    $registrySelectedCaseIds =
        [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($entry in $registryDocument.entries) {
        $provenancePath = Join-Path (
            $repositoryRoot) ([string] $entry.provenance).Replace(
                '/', [IO.Path]::DirectorySeparatorChar)
        $provenance = Get-Content `
            -LiteralPath $provenancePath `
            -Raw |
            ConvertFrom-Json
        foreach ($caseId in $provenance.selectedCaseIds) {
            if (-not $registrySelectedCaseIds.Add([string] $caseId)) {
                throw (
                    'Legacy oracle build-integration selected case IDs ' +
                    "must be globally unique: $caseId")
            }
        }
    }
    $orderedSelectedCaseIds = [string[]] @(
        Get-OrdinallySortedStrings @($registrySelectedCaseIds))
    if ($orderedSelectedCaseIds.Count -eq 0) {
        throw (
            'Legacy oracle build-integration execution must bind at least ' +
            'one selected case ID.')
    }
    $revision = (
        & git -C $repositoryRoot rev-parse --verify 'HEAD^{commit}'
    ).Trim()
    if ($LASTEXITCODE -ne 0 -or
        $revision -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Could not bind build integration to an XPlat revision.'
    }
    $tree = (
        & git -C $repositoryRoot rev-parse --verify 'HEAD^{tree}'
    ).Trim()
    if ($LASTEXITCODE -ne 0 -or
        $tree -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Could not bind build integration to an XPlat tree.'
    }
    $envelope = [ordered]@{
        schemaVersion = 1
        target = 'LegacyOracleBuildIntegration'
        platform = $certificationPlatform
        architecture = (
            [Runtime.InteropServices.RuntimeInformation]::
                ProcessArchitecture.ToString().ToLowerInvariant())
        runtimeIdentifier = (
            [Runtime.InteropServices.RuntimeInformation]::
                RuntimeIdentifier)
        revision = $revision
        tree = $tree
        registrySha256 = $legacyRegistryHash
        selectedCaseIds = $orderedSelectedCaseIds
        testName = (
            'MorseRunner.LegacyParity.Tests.LegacyOracleTargetTests.' +
            'ConfiguredBuildRegistryAttestsAndExecutesEverySelectedCase')
        testReportSha256 = Get-LowercaseFileSha256 $path
        testProcessExitCode = [int] $exitCode
        wrapper = [ordered]@{
            completed = $true
            correlationValidated = $true
            exitCode = 0
        }
    }
    $raw = (
        $envelope | ConvertTo-Json -Depth 6
    ).Replace("`r`n", "`n").Replace("`r", "`n") + "`n"
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($raw)
    $hash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            $bytes)
    ).ToLowerInvariant()
    $executionRoot = Assert-SafeDescendantPath `
        -Root $repositoryRoot `
        -Candidate (
            Join-Path (
                $resultsRoot
            ) 'executions\legacy-oracle-build-integration') `
        -Description 'Legacy oracle build-integration executions'
    New-Item -ItemType Directory -Force -Path $executionRoot |
        Out-Null
    $script:integrationExecutionPath = Join-Path (
        $executionRoot) "$hash.json"
    if (Test-Path `
            -LiteralPath $script:integrationExecutionPath `
            -PathType Leaf) {
        if ((Get-LowercaseFileSha256 `
                $script:integrationExecutionPath) -cne $hash) {
            throw (
                'Existing content-addressed build-integration execution ' +
                'envelope has unexpected bytes.')
        }
    } else {
        Write-Utf8LfAtomic `
            -Path $script:integrationExecutionPath `
            -Value $raw
    }
}

function Invoke-ParityTests {
    param(
        [ValidateSet('Legacy', 'XPlat')]
        [string] $SelectedTarget
    )

    $targetCaseIds = @(
        if ($PromoteCaseId) {
            if (-not (
                    $SelectedTarget -eq 'Legacy' -and
                    $selectedPromotionKind -eq 'green')) {
                $localPromotionCaseIds
            }
        } elseif ($CaptureGreenCaseId) {
            $CaptureGreenCaseId
        })
    if ($targetCaseIds.Count -gt 0) {
        $env:MORSE_RUNNER_PARITY_CASE_IDS =
            ConvertTo-Json `
                -InputObject ([string[]] $targetCaseIds) `
                -Compress
    } else {
        $env:MORSE_RUNNER_PARITY_CASE_IDS = $null
    }
    $env:MORSE_RUNNER_PARITY_TARGET = $SelectedTarget
    $resultPaths[$SelectedTarget] = Join-Path (
        $resultsRoot
    ) "$($SelectedTarget.ToLowerInvariant()).json"
    $env:MORSE_RUNNER_PARITY_RESULTS =
        $resultPaths[$SelectedTarget]
    $trxFileName =
        "$($SelectedTarget.ToLowerInvariant()).trx"
    $trxPaths[$SelectedTarget] = Join-Path $trxRoot $trxFileName

    if ($SelectedTarget -eq 'XPlat') {
        $env:MORSE_RUNNER_LEGACY_ROOT = $null
        $env:MORSE_RUNNER_LEGACY_ORACLE_REGISTRY = $null
        $env:MORSE_RUNNER_LEGACY_ORACLE_REGISTRY_SHA256 = $null
        $env:MORSE_RUNNER_LEGACY_ORACLE = $null
        $env:MORSE_RUNNER_LEGACY_PROVENANCE = $null
        $env:MORSE_RUNNER_LEGACY_ORACLE_SHA256 = $null
        $env:MORSE_RUNNER_LEGACY_PROVENANCE_SHA256 = $null
    }

    & dotnet test `
        --project $testProject `
        --configuration Release `
        --results-directory $trxRoot `
        -- `
        --filter-trait 'Category=ParityAcceptance' `
        --minimum-expected-tests 1 `
        --report-xunit-trx `
        --report-xunit-trx-filename $trxFileName
    $targetExitCodes[$SelectedTarget] = $LASTEXITCODE

    if (-not (Test-Path -LiteralPath $env:MORSE_RUNNER_PARITY_RESULTS)) {
        throw (
            "$SelectedTarget parity target produced no execution results " +
            "(test exit code $($targetExitCodes[$SelectedTarget])).")
    }
    & (Join-Path $PSScriptRoot 'Assert-ParityTestRun.ps1') `
        -Target $SelectedTarget `
        -ResultPath $resultPaths[$SelectedTarget] `
        -TrxPath $trxPaths[$SelectedTarget] `
        -ProcessExitCode $targetExitCodes[$SelectedTarget]
    $executionPaths[$SelectedTarget] =
        New-ParityExecutionEnvelope `
            -SelectedTarget $SelectedTarget
}

function Invoke-PromotionPreflight {
    & (Join-Path $PSScriptRoot 'Test-ParityPathSafety.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw (
            'Parity path safety checks failed with exit code ' +
            "$LASTEXITCODE.")
    }
    & (Join-Path $PSScriptRoot 'Test-ParityCanonicalJson.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw (
            'PowerShell canonical JSON checks failed with exit code ' +
            "$LASTEXITCODE.")
    }
    & (Join-Path $PSScriptRoot 'Test-LegacyOracleDescriptor.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw (
            'Legacy oracle descriptor checks failed with exit code ' +
            "$LASTEXITCODE.")
    }
    & (
        Join-Path (
            $PSScriptRoot
        ) 'Test-LegacyOracleRegistryArtifactSet.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw (
            'Legacy oracle registry artifact-set checks failed with exit ' +
            "code $LASTEXITCODE.")
    }
    & (Join-Path $PSScriptRoot 'Test-LegacyOracleBuildIntegration.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw (
            'Legacy oracle build-integration TRX checks failed with exit ' +
            "code $LASTEXITCODE.")
    }
    & (Join-Path $PSScriptRoot 'Test-ParityFullSuitePackage.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw (
            'Full-suite package closure checks failed with exit code ' +
            "$LASTEXITCODE.")
    }
    & (Join-Path $PSScriptRoot 'Test-ParityTestRunCorrelation.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw (
            'Parity TRX correlation checks failed with exit code ' +
            "$LASTEXITCODE.")
    }
    & uv run --locked python -m unittest discover `
        -s tools\parity -p 'test_*.py'
    if ($LASTEXITCODE -ne 0) {
        throw (
            'Parity tooling tests failed with exit code ' +
            "$LASTEXITCODE.")
    }
}

try {
    $env:MORSE_RUNNER_PARITY_CASE_IDS = $null
    if ($PromoteCaseId) {
        Invoke-PromotionPreflight
    }
    if ($runLegacyTarget) {
        & (Join-Path $PSScriptRoot 'Prepare-LegacyReference.ps1') `
            -SourceRepository $LegacyRoot `
            -Destination $preparedLegacyRoot

        $selectedLegacyCaseIds = @(
            if ($focusedRedPromotion) {
                $localPromotionCaseIds
            } else {
                $applicableCases.id
            })
        $legacyBuild = & (
            Join-Path $PSScriptRoot 'Build-LegacyOracle.ps1'
        ) `
            -LegacyRoot $preparedLegacyRoot `
            -LazarusRoot $LazarusRoot `
            -CaseId $selectedLegacyCaseIds `
            -PassThru

        $legacyRegistryPath = $legacyBuild.registry
        $legacyRegistryHash = $legacyBuild.registrySha256
        if ([string]::IsNullOrWhiteSpace($legacyRegistryPath)) {
            throw 'Legacy build did not publish an oracle registry path.'
        }
        if ([string] $legacyRegistryHash -cnotmatch
            '^[0-9a-f]{64}$') {
            throw (
                'Legacy build did not publish a lowercase oracle registry ' +
                'SHA-256.')
        }
        Invoke-LegacyOracleBuildIntegration `
            -SelectedCaseIds $selectedLegacyCaseIds
        if ($PromoteCaseId) {
            $reproducibilityCases = @(
                $promotionCases |
                    Group-Object {
                        $_.legacyOracle.versionId
                    } |
                    ForEach-Object { $_.Group[0].id })
            foreach ($reproducibilityCase in
                $reproducibilityCases) {
                & (
                    Join-Path (
                        $PSScriptRoot
                    ) 'Test-LegacyOracleReproducibility.ps1') `
                    -LegacyRoot $preparedLegacyRoot `
                    -LazarusRoot $LazarusRoot `
                    -CaseId $reproducibilityCase
                if ($LASTEXITCODE -ne 0) {
                    throw (
                        'Legacy oracle reproducibility failed for ' +
                        "'$reproducibilityCase' with exit code " +
                        "$LASTEXITCODE.")
                }
            }

        } else {
            & (Join-Path $PSScriptRoot 'Test-ParityCompleteness.ps1') `
                -LegacyRoot $preparedLegacyRoot
        }

        Invoke-ParityTests -SelectedTarget Legacy
    }

    if ($runXPlatTarget) {
        Invoke-ParityTests -SelectedTarget XPlat
    }

    $validationArguments = @(
        'run'
        '--locked'
        'python'
        'tools\parity\validate_parity.py'
        '--mode'
        $Mode
    )
    if ($runLegacyTarget) {
        $validationArguments += @(
            '--legacy-root'
            $preparedLegacyRoot
            '--legacy-results'
            $resultPaths.Legacy
            '--legacy-test-report'
            $trxPaths.Legacy
            '--legacy-execution'
            $executionPaths.Legacy
            '--legacy-oracle-registry'
            $legacyRegistryPath
            '--legacy-oracle-registry-sha256'
            $legacyRegistryHash
        )
    }
    if ($runXPlatTarget) {
        $validationArguments += @(
            '--xplat-results'
            $resultPaths.XPlat
            '--xplat-test-report'
            $trxPaths.XPlat
            '--xplat-execution'
            $executionPaths.XPlat
        )
    }

    if ($PromoteCaseId) {
        if ($targetExitCodes.ContainsKey('Legacy')) {
            $legacyDocument = Get-Content `
                -LiteralPath $resultPaths.Legacy `
                -Raw |
                ConvertFrom-Json
            foreach ($promotionCaseId in $localPromotionCaseIds) {
                $legacyMatches = @(
                    $legacyDocument.results |
                        Where-Object {
                            $_.parityId -ceq $promotionCaseId -and
                            $_.outcome -ceq 'passed'
                        })
                if ($legacyMatches.Count -ne 1) {
                    throw (
                        "Promotion case '$promotionCaseId' did not pass " +
                        'the exact Legacy adapter.')
                }
            }
        }
        if ($targetExitCodes.ContainsKey('XPlat')) {
            $xplatDocument = Get-Content `
                -LiteralPath $resultPaths.XPlat `
                -Raw |
                ConvertFrom-Json
            $requiredXPlatOutcome = if (
                $selectedPromotionKind -eq 'red'
            ) {
                'functional-divergence'
            } else {
                'passed'
            }
            foreach ($promotionCaseId in $localPromotionCaseIds) {
                $xplatMatches = @(
                    $xplatDocument.results |
                        Where-Object {
                            $_.parityId -ceq $promotionCaseId -and
                            $_.outcome -ceq $requiredXPlatOutcome
                        })
                if ($xplatMatches.Count -ne 1) {
                    throw (
                        "Promotion case '$promotionCaseId' did not " +
                        "produce required XPlat outcome " +
                        "'$requiredXPlatOutcome'.")
                }
            }
        }

        $promotionArguments = if (
            $selectedPromotionKind -eq 'green'
        ) {
            @(
                'run'
                '--locked'
                'python'
                'tools\parity\validate_parity.py'
                '--mode'
                $Mode
                '--legacy-oracle-registry'
                $fullSuiteLegacyOracleRegistryPath
                '--legacy-oracle-registry-sha256'
                $FullSuiteLegacyOracleRegistrySha256
                '--full-suite-package-index'
                $fullSuitePackageIndexPath
                '--full-suite-package-index-sha256'
                $FullSuitePackageIndexSha256
            )
        } else {
            @($validationArguments)
        }
        $promotionArguments += @(
            '--promote-evidence'
            $selectedPromotionKind
        )
        foreach ($promotionCaseId in $PromoteCaseId) {
            $promotionArguments += @(
                '--case-id'
                $promotionCaseId)
        }
        foreach ($greenResultArgument in $greenResultArguments) {
            $promotionArguments += @(
                '--green-result'
                $greenResultArgument)
        }
        foreach ($greenTestReportArgument in
            $greenTestReportArguments) {
            $promotionArguments += @(
                '--green-test-report'
                $greenTestReportArgument)
        }
        foreach ($greenExecutionArgument in
            $greenExecutionArguments) {
            $promotionArguments += @(
                '--green-execution'
                $greenExecutionArgument)
        }
        foreach ($argument in $fullSuiteResultArguments) {
            $promotionArguments += @(
                '--full-suite-result'
                $argument)
        }
        foreach ($argument in $fullSuiteTestReportArguments) {
            $promotionArguments += @(
                '--full-suite-test-report'
                $argument)
        }
        foreach ($argument in $fullSuiteExecutionArguments) {
            $promotionArguments += @(
                '--full-suite-execution'
                $argument)
        }
        & uv @promotionArguments
        if ($LASTEXITCODE -ne 0) {
            throw (
                "$selectedPromotionKind evidence promotion failed with " +
                'exit code ' +
                "$LASTEXITCODE.")
        }
    } elseif ($GreenRegressionCaseId) {
        $regressionDocument = Get-Content `
            -LiteralPath $resultPaths.XPlat `
            -Raw |
            ConvertFrom-Json
        foreach ($regressionCaseId in $GreenRegressionCaseId) {
            $matches = @(
                $regressionDocument.results |
                    Where-Object {
                        $_.parityId -ceq $regressionCaseId -and
                        $_.outcome -ceq 'passed'
                    })
            if ($matches.Count -ne 1) {
                throw (
                    "Green regression case '$regressionCaseId' did not " +
                    'pass in the full suite.')
            }
            $validationArguments += @(
                '--green-regression-case-id'
                $regressionCaseId)
        }
        $validationArguments += '--verify-live-results'
        & uv @validationArguments
        if ($LASTEXITCODE -ne 0) {
            throw (
                'Full green regression validation failed with exit code ' +
                "$LASTEXITCODE.")
        }
    } elseif ($CaptureGreenCaseId) {
        $captureDocument = Get-Content `
            -LiteralPath $resultPaths.XPlat `
            -Raw |
            ConvertFrom-Json
        foreach ($captureCaseId in $CaptureGreenCaseId) {
            $matches = @(
                $captureDocument.results |
                    Where-Object {
                        $_.parityId -ceq $captureCaseId -and
                        $_.outcome -ceq 'passed'
                    })
            if ($matches.Count -ne 1) {
                throw (
                    "Green capture case '$captureCaseId' did not pass.")
            }
            $validationArguments += @(
                '--capture-green-case-id'
                $captureCaseId)
        }
        $validationArguments += '--verify-live-results'
        & uv @validationArguments
        if ($LASTEXITCODE -ne 0) {
            throw (
                'Green capture validation failed with exit code ' +
                "$LASTEXITCODE.")
        }
    } else {
        $validationArguments += '--verify-live-results'
        & uv @validationArguments
        if ($LASTEXITCODE -ne 0) {
            throw (
                "$Mode parity validation failed with exit code " +
                "$LASTEXITCODE.")
        }
    }

    if (
        -not $PromoteCaseId -and
        $targetExitCodes.ContainsKey('Legacy') -and
        $targetExitCodes.Legacy -ne 0
    ) {
        throw (
            'Legacy parity product tests failed after their live result ' +
            "validated (exit code $($targetExitCodes.Legacy)).")
    }

    if (
        -not $PromoteCaseId -and
        $targetExitCodes.ContainsKey('XPlat')
    ) {
        $xplatDocument = Get-Content `
            -LiteralPath $resultPaths.XPlat `
            -Raw |
            ConvertFrom-Json
        $functionalDivergences = @(
            $xplatDocument.results |
                Where-Object {
                    $_.outcome -ceq 'functional-divergence'
                })
        if (
            $functionalDivergences.Count -eq 0 -and
            $targetExitCodes.XPlat -ne 0
        ) {
            throw (
                'All XPlat live results passed, but the dedicated product ' +
                "test process failed with exit code $($targetExitCodes.XPlat).")
        }
        if (
            $functionalDivergences.Count -gt 0 -and
            $targetExitCodes.XPlat -eq 0
        ) {
            throw (
                'XPlat live results contain functional divergences, but the ' +
                'dedicated product test process did not fail.')
        }
    }
} finally {
    $env:MORSE_RUNNER_PARITY_TARGET = $savedEnvironment.Target
    $env:MORSE_RUNNER_PARITY_RESULTS = $savedEnvironment.Results
    $env:MORSE_RUNNER_LEGACY_ROOT = $savedEnvironment.LegacyRoot
    $env:MORSE_RUNNER_LEGACY_ORACLE = $savedEnvironment.Oracle
    $env:MORSE_RUNNER_LEGACY_PROVENANCE = $savedEnvironment.Provenance
    $env:MORSE_RUNNER_LEGACY_ORACLE_SHA256 =
        $savedEnvironment.OracleSha256
    $env:MORSE_RUNNER_LEGACY_PROVENANCE_SHA256 =
        $savedEnvironment.ProvenanceSha256
    $env:MORSE_RUNNER_LEGACY_ORACLE_REGISTRY =
        $savedEnvironment.Registry
    $env:MORSE_RUNNER_LEGACY_ORACLE_REGISTRY_SHA256 =
        $savedEnvironment.RegistrySha256
    $env:MORSE_RUNNER_PARITY_CASE_IDS =
        $savedEnvironment.SelectedCaseIds
}
