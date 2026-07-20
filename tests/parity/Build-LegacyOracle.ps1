[CmdletBinding()]
param(
    [string] $LegacyRoot = (
        Join-Path $PSScriptRoot '..\..\artifacts\legacy-reference'),

    [string] $LazarusRoot = 'C:\lazarus',

    [string] $OutputRoot = (
        Join-Path $PSScriptRoot '..\..\artifacts\legacy-oracle'),

    [string[]] $CaseId,

    [switch] $PassThru
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'ParityPathSafety.ps1')
. (Join-Path $PSScriptRoot 'ParityCanonicalJson.ps1')
. (Join-Path $PSScriptRoot 'LegacyOracleDescriptor.ps1')

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

function Assert-ExactProperties {
    param(
        [Parameter(Mandatory)]
        [object] $Value,

        [Parameter(Mandatory)]
        [string[]] $Expected,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $actual = @(
        Get-OrdinallySortedStrings @(
            $Value.PSObject.Properties.Name))
    $expectedSorted = @(
        Get-OrdinallySortedStrings @($Expected))
    if ($actual.Count -ne $expectedSorted.Count) {
        throw "$Description has an unexpected property count."
    }
    for ($index = 0; $index -lt $actual.Count; $index++) {
        if ($actual[$index] -cne $expectedSorted[$index]) {
            throw (
                "$Description properties are invalid. Expected " +
                "$($expectedSorted -join ', '); observed " +
                "$($actual -join ', ').")
        }
    }
}

function Assert-NonemptyString {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Value,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "$Description must be a nonempty string."
    }
}

function Assert-Sha256 {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Value,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if ($Value -cnotmatch '^[0-9a-f]{64}$') {
        throw "$Description must be a lowercase SHA-256 digest."
    }
}

function Test-SameSha256 {
    param(
        [Parameter(Mandatory)]
        [string] $Expected,

        [Parameter(Mandatory)]
        [string] $Actual
    )

    return [StringComparer]::OrdinalIgnoreCase.Equals($Expected, $Actual)
}

function Expand-RecipeToken {
    param(
        [Parameter(Mandatory)]
        [string] $Token,

        [Parameter(Mandatory)]
        [Collections.Generic.Dictionary[string, string]] $Values
    )

    $expanded = $Token
    foreach ($entry in $Values.GetEnumerator()) {
        $expanded = $expanded.Replace(
            "{$($entry.Key)}",
            $entry.Value,
            [StringComparison]::Ordinal)
    }
    if ($expanded -match '\{[^{}]+\}') {
        throw "Build recipe contains an unknown placeholder: $Token"
    }
    return $expanded
}

function Assert-ReferenceWorktree {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [object] $Reference,

        [Parameter(Mandatory)]
        [string] $Stage
    )

    $revision = (& git -C $Root rev-parse --verify 'HEAD^{commit}').Trim()
    if ($LASTEXITCODE -ne 0 -or $revision -cne $Reference.revision) {
        throw "Legacy revision mismatch during $Stage`: $revision"
    }
    $tree = (& git -C $Root rev-parse --verify 'HEAD^{tree}').Trim()
    if ($LASTEXITCODE -ne 0 -or $tree -cne $Reference.tree) {
        throw "Legacy tree mismatch during $Stage`: $tree"
    }
    $status = @(
        & git -C $Root status --porcelain=v2 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect the legacy worktree during $Stage."
    }
    if ($status.Count -ne 0) {
        throw (
            "Legacy worktree is dirty during $Stage`:`n" +
            ($status -join "`n"))
    }
}

$repositoryRoot = (Resolve-Path (
    Join-Path $PSScriptRoot '..\..')).Path
$referencePath = Join-Path $PSScriptRoot 'legacy-reference.json'
$manifestPath = Join-Path $PSScriptRoot 'parity-manifest.json'
$reference = Get-Content -LiteralPath $referencePath -Raw |
    ConvertFrom-Json
if ($reference.schemaVersion -ne 1) {
    throw "Unsupported legacy reference schema: $($reference.schemaVersion)"
}
$manifestRaw = Get-Content -LiteralPath $manifestPath -Raw
$manifestShapeDocument =
    [Text.Json.JsonDocument]::Parse($manifestRaw)
try {
    ConvertTo-ParityCanonicalJsonBytes `
        -Element $manifestShapeDocument.RootElement |
        Out-Null
} finally {
    $manifestShapeDocument.Dispose()
}
$manifest = $manifestRaw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 3) {
    throw "Unsupported parity manifest schema: $($manifest.schemaVersion)"
}

$referenceHash = (
    Get-FileHash -LiteralPath (
        $referencePath) -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestHash = (
    Get-FileHash -LiteralPath (
        $manifestPath) -Algorithm SHA256).Hash.ToLowerInvariant()
$bundlePath = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot $reference.bundle))
$bundleHash = (
    Get-FileHash -LiteralPath (
        $bundlePath) -Algorithm SHA256).Hash.ToLowerInvariant()
if (-not (Test-SameSha256 $reference.bundleSha256 $bundleHash)) {
    throw "Pinned legacy bundle hash mismatch: $bundleHash"
}

$legacyRootPath = (Resolve-Path -LiteralPath $LegacyRoot).Path
$outputRootPath = Assert-SafeDescendantPath `
    -Root $repositoryRoot `
    -Candidate $OutputRoot `
    -Description 'Legacy oracle output'
$allowedOutputRoot = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\legacy-oracle'))
$runningOnWindows =
    [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [Runtime.InteropServices.OSPlatform]::Windows)
$pathComparison = if ($runningOnWindows) {
    [StringComparison]::OrdinalIgnoreCase
} else {
    [StringComparison]::Ordinal
}
$allowedPrefix = $allowedOutputRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar
) + [IO.Path]::DirectorySeparatorChar
if (
    -not [string]::Equals(
        $outputRootPath,
        $allowedOutputRoot,
        $pathComparison) -and
    -not $outputRootPath.StartsWith($allowedPrefix, $pathComparison)
) {
    throw "Legacy oracle output must be at or below $allowedOutputRoot."
}

Assert-ReferenceWorktree `
    -Root $legacyRootPath `
    -Reference $reference `
    -Stage 'pre-build verification'

$allCases = @($manifest.cases)
$selectedCases = if ($CaseId.Count -gt 0) {
    $requestedIds = @($CaseId | Select-Object -Unique)
    foreach ($requestedId in $requestedIds) {
        $matches = @(
            $allCases | Where-Object { $_.id -ceq $requestedId })
        if ($matches.Count -ne 1) {
            throw (
                'Legacy oracle build must select exactly one active case ' +
                "for '$requestedId'.")
        }
        $matches[0]
    }
} else {
    $allCases
}
if ($selectedCases.Count -eq 0) {
    throw 'At least one active parity case must be selected.'
}

$descriptorsByVersion =
    [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::Ordinal)
$caseIdsByVersion =
    [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::Ordinal)
$casesById =
    [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::Ordinal)
foreach ($case in $selectedCases) {
    $casesById.Add([string] $case.id, $case)
    if ($null -eq $case.legacyOracle) {
        throw "Parity case '$($case.id)' has no legacyOracle descriptor."
    }
    $descriptor = $case.legacyOracle
    Assert-ExactProperties `
        -Value $descriptor `
        -Expected @(
            'adapterId',
            'versionId',
            'source',
            'sourceSha256',
            'buildRecipe',
            'buildRecipeSha256') `
        -Description "Parity case '$($case.id)' legacyOracle"
    foreach ($propertyName in @(
            'adapterId',
            'versionId',
            'source',
            'buildRecipe')) {
        Assert-NonemptyString `
            -Value ([string] $descriptor.$propertyName) `
            -Description (
                "Parity case '$($case.id)' legacyOracle.$propertyName")
    }
    Assert-Sha256 `
        -Value ([string] $descriptor.sourceSha256) `
        -Description (
            "Parity case '$($case.id)' legacyOracle.sourceSha256")
    Assert-Sha256 `
        -Value ([string] $descriptor.buildRecipeSha256) `
        -Description (
            "Parity case '$($case.id)' legacyOracle.buildRecipeSha256")
    Assert-VersionedLegacyOracleDescriptorPath `
        -Descriptor $descriptor `
        -Description (
            "Parity case '$($case.id)' legacyOracle")
    if ([string] $descriptor.versionId -cnotmatch
        '^[a-z0-9][a-z0-9.-]*$') {
        throw (
            "Parity case '$($case.id)' legacyOracle.versionId cannot " +
            'be used as an isolated build path.')
    }

    $versionId = [string] $descriptor.versionId
    Add-LegacyOracleDescriptorVersionBinding `
        -DescriptorsByVersion $descriptorsByVersion `
        -CaseIdsByVersion $caseIdsByVersion `
        -Descriptor $descriptor `
        -CaseId ([string] $case.id)
}

$toolchain = & (
    Join-Path $PSScriptRoot 'Install-LegacyOracleToolchain.ps1'
) -LazarusRoot $LazarusRoot -PassThru

$buildsRoot = Assert-SafeDescendantPath `
    -Root $repositoryRoot `
    -Candidate (Join-Path $outputRootPath 'builds') `
    -Description 'Legacy oracle builds'
$registriesRoot = Assert-SafeDescendantPath `
    -Root $repositoryRoot `
    -Candidate (Join-Path $outputRootPath 'registries') `
    -Description 'Legacy oracle registries'
New-Item -ItemType Directory -Force -Path (
    $buildsRoot), $registriesRoot | Out-Null

$registryEntries = [Collections.Generic.List[object]]::new()
$createdBuildRoots = [Collections.Generic.List[string]]::new()
try {
    foreach ($versionId in @(
            Get-OrdinallySortedStrings @(
                $descriptorsByVersion.Keys))) {
        $descriptor = $descriptorsByVersion[$versionId]
        $sourcePath = Assert-SafeDescendantPath `
            -Root $repositoryRoot `
            -Candidate (Join-Path $repositoryRoot $descriptor.source) `
            -Description "Legacy oracle '$versionId' source"
        $recipePath = Assert-SafeDescendantPath `
            -Root $repositoryRoot `
            -Candidate (Join-Path $repositoryRoot $descriptor.buildRecipe) `
            -Description "Legacy oracle '$versionId' build recipe"
        foreach ($path in @($sourcePath, $recipePath)) {
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Legacy oracle build input is not a file: $path"
            }
        }

        $sourceHash = (
            Get-FileHash -LiteralPath (
                $sourcePath) -Algorithm SHA256).Hash.ToLowerInvariant()
        $recipeHash = (
            Get-FileHash -LiteralPath (
                $recipePath) -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not (Test-SameSha256 `
                ([string] $descriptor.sourceSha256) $sourceHash)) {
            throw (
                "Legacy oracle '$versionId' source hash mismatch: " +
                $sourceHash)
        }
        if (-not (Test-SameSha256 `
                ([string] $descriptor.buildRecipeSha256) $recipeHash)) {
            throw (
                "Legacy oracle '$versionId' build-recipe hash mismatch: " +
                $recipeHash)
        }

        $recipeRaw = Get-Content -LiteralPath $recipePath -Raw
        $recipeShapeDocument =
            [Text.Json.JsonDocument]::Parse($recipeRaw)
        try {
            ConvertTo-ParityCanonicalJsonBytes `
                -Element $recipeShapeDocument.RootElement |
                Out-Null
        } finally {
            $recipeShapeDocument.Dispose()
        }
        $recipe = $recipeRaw | ConvertFrom-Json
        Assert-ExactProperties `
            -Value $recipe `
            -Expected @(
                'schemaVersion',
                'adapterId',
                'versionId',
                'sourceClosure',
                'invocation') `
            -Description "Legacy oracle '$versionId' build recipe"
        if ($recipe.schemaVersion -ne 1) {
            throw (
                "Legacy oracle '$versionId' build recipe schema is " +
                'unsupported.')
        }
        if ($recipe.adapterId -cne $descriptor.adapterId -or
            $recipe.versionId -cne $descriptor.versionId) {
            throw (
                "Legacy oracle '$versionId' recipe identity does not " +
                'match its descriptor.')
        }
        Assert-ExactProperties `
            -Value $recipe.sourceClosure `
            -Expected @(
                'oracleSource',
                'oracleSourceSha256',
                'legacyRevision',
                'legacyTree',
                'legacyBundleSha256',
                'toolchainFingerprintSha256') `
            -Description "Legacy oracle '$versionId' source closure"
        if ($recipe.sourceClosure.oracleSource -cne $descriptor.source -or
            -not (Test-SameSha256 `
                $recipe.sourceClosure.oracleSourceSha256 $sourceHash) -or
            $recipe.sourceClosure.legacyRevision -cne
                $reference.revision -or
            $recipe.sourceClosure.legacyTree -cne $reference.tree -or
            -not (Test-SameSha256 `
                $recipe.sourceClosure.legacyBundleSha256 $bundleHash) -or
            -not (Test-SameSha256 `
                $recipe.sourceClosure.toolchainFingerprintSha256 `
                $toolchain.fingerprint.aggregateSha256)) {
            throw (
                "Legacy oracle '$versionId' source closure does not " +
                'match its descriptor and pinned reference.')
        }
        Assert-ExactProperties `
            -Value $recipe.invocation `
            -Expected @('compiler', 'arguments') `
            -Description "Legacy oracle '$versionId' invocation"
        Assert-NonemptyString `
            -Value ([string] $recipe.invocation.compiler) `
            -Description "Legacy oracle '$versionId' recipe compiler"
        $recipeArguments = @($recipe.invocation.arguments)
        if ($recipeArguments.Count -eq 0 -or
            @($recipeArguments | Where-Object {
                    $_ -isnot [string] -or
                    [string]::IsNullOrWhiteSpace($_)
                }).Count -ne 0) {
            throw (
                "Legacy oracle '$versionId' recipe arguments must be " +
                'nonempty strings.')
        }
        if (@($recipeArguments | Where-Object {
                    $_ -ceq '-n'
                }).Count -ne 1) {
            throw (
                "Legacy oracle '$versionId' recipe must contain exactly " +
                "one '-n' option so no compiler configuration files are " +
                'consulted.')
        }

        $identityDocument = [ordered]@{
            schemaVersion = 1
            adapterId = [string] $descriptor.adapterId
            versionId = $versionId
            sourceSha256 = $sourceHash
            buildRecipeSha256 = $recipeHash
            legacyRevision = [string] $reference.revision
            legacyTree = [string] $reference.tree
            bundleSha256 = $bundleHash
            compilerSha256 = (
                [string] $toolchain.compilerSha256
            ).ToLowerInvariant()
            backendCompilerSha256 =
                ([string] $toolchain.backendCompilerSha256
                ).ToLowerInvariant()
            toolchainFingerprintSha256 =
                ([string] $toolchain.fingerprint.aggregateSha256
                ).ToLowerInvariant()
        }
        $identityJson = $identityDocument |
            ConvertTo-Json -Compress
        $buildIdentity = Get-Sha256Text $identityJson
        $buildRoot = Assert-SafeDescendantPath `
            -Root $repositoryRoot `
            -Candidate (Join-Path (
                Join-Path (
                    Join-Path $buildsRoot $versionId
                ) $buildIdentity
            ) ([Guid]::NewGuid().ToString('N'))) `
            -Description "Legacy oracle '$versionId' isolated build"
        $executableRoot = Join-Path $buildRoot 'bin'
        $unitRoot = Join-Path $buildRoot 'units'
        New-Item -ItemType Directory -Force -Path (
            $unitRoot), $executableRoot | Out-Null
        $createdBuildRoots.Add($buildRoot)
        $executablePath = Join-Path $executableRoot 'LegacyOracle.exe'

        $placeholders =
            [Collections.Generic.Dictionary[string, string]]::new(
                [StringComparer]::Ordinal)
        $placeholders.Add('legacyRoot', $legacyRootPath)
        $placeholders.Add('toolchainRoot', [string] $toolchain.root)
        $placeholders.Add('unitOutput', $unitRoot)
        $placeholders.Add('executableOutput', $executableRoot)
        $placeholders.Add('executable', $executablePath)
        $placeholders.Add('source', $sourcePath)
        $compilerPath = Expand-RecipeToken `
            -Token ([string] $recipe.invocation.compiler) `
            -Values $placeholders
        $compilerPath = [IO.Path]::GetFullPath($compilerPath)
        if ($compilerPath -cne [IO.Path]::GetFullPath(
                [string] $toolchain.compiler)) {
            throw (
                "Legacy oracle '$versionId' recipe selected an " +
                'unexpected compiler.')
        }
        $arguments = @(
            foreach ($argument in $recipeArguments) {
                Expand-RecipeToken `
                    -Token ([string] $argument) `
                    -Values $placeholders
            }
        )
        if (@($arguments | Where-Object {
                    $_ -ceq "-o$executablePath"
                }).Count -ne 1 -or
            @($arguments | Where-Object {
                    $_ -ceq $sourcePath
                }).Count -ne 1) {
            throw (
                "Legacy oracle '$versionId' recipe must bind exactly one " +
                'source and executable output.')
        }

        & $compilerPath @arguments
        if ($LASTEXITCODE -ne 0) {
            throw (
                "Legacy oracle '$versionId' compilation failed with " +
                "exit code $LASTEXITCODE.")
        }
        if (-not (Test-Path -LiteralPath (
                $executablePath) -PathType Leaf)) {
            throw (
                "Legacy oracle '$versionId' compiler produced no " +
                "executable: $executablePath")
        }

        $executableHash = (
            Get-FileHash -LiteralPath (
                $executablePath) -Algorithm SHA256).Hash.ToLowerInvariant()
        $legacyRootArgument = (
            $legacyRootPath +
            [IO.Path]::DirectorySeparatorChar)
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
        $inputRoot = Join-Path $buildRoot 'inputs'
        New-Item -ItemType Directory -Force -Path $inputRoot |
            Out-Null
        $manifestJsonDocument =
            [Text.Json.JsonDocument]::Parse($manifestRaw)
        $observations = @(
            try {
                foreach ($selectedCaseId in @(
                        Get-OrdinallySortedStrings @(
                            $caseIdsByVersion[$versionId]))) {
                    $caseArray = (
                        $manifestJsonDocument.RootElement.GetProperty(
                            'cases'))
                    $caseElements = @(
                        $caseArray.EnumerateArray() |
                            Where-Object {
                                $_.GetProperty('id').GetString() -ceq
                                    $selectedCaseId
                            })
                    if ($caseElements.Count -ne 1) {
                        throw (
                            "Manifest JSON does not contain one case " +
                            "element for '$selectedCaseId'.")
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
                    $inputPath = Join-Path (
                        $inputRoot) "$inputHash.json"
                    [IO.File]::WriteAllBytes($inputPath, $inputBytes)

                    $raw = (
                        & $executablePath `
                            $legacyRootArgument `
                            $selectedCaseId `
                            $descriptor.adapterId `
                            $descriptor.versionId `
                            $descriptor.source `
                            $sourceHash `
                            $descriptor.buildRecipe `
                            $recipeHash `
                            $caseDefinitionHash `
                            $inputHash `
                            $inputPath |
                            Out-String
                    ).Trim()
                    if ($LASTEXITCODE -ne 0) {
                        throw (
                            "Legacy oracle '$versionId' failed for " +
                            "'$selectedCaseId' with exit code " +
                            "$LASTEXITCODE.")
                    }
                    $observation = $raw | ConvertFrom-Json
                    if ($observation.scenario -cne $selectedCaseId -or
                        $observation.adapterId -cne
                            $descriptor.adapterId -or
                        $observation.versionId -cne
                            $descriptor.versionId -or
                        $observation.source -cne
                            $descriptor.source -or
                        -not (Test-SameSha256 `
                            $observation.sourceSha256 $sourceHash) -or
                        $observation.buildRecipe -cne
                            $descriptor.buildRecipe -or
                        -not (Test-SameSha256 `
                            $observation.buildRecipeSha256 $recipeHash) -or
                        $observation.caseDefinitionSha256 -cne
                            $caseDefinitionHash -or
                        $observation.inputSha256 -cne $inputHash) {
                        throw (
                            "Legacy oracle '$versionId' returned stale " +
                            "bindings for '$selectedCaseId'.")
                    }
                    [ordered]@{
                        scenario = $selectedCaseId
                        valueCount = @($observation.values).Count
                        outputSha256 = Get-Sha256Text $raw
                    }
                }
            } finally {
                $manifestJsonDocument.Dispose()
            }
        )
        $buildScriptHash = (
            Get-FileHash -LiteralPath $PSCommandPath `
                -Algorithm SHA256).Hash.ToLowerInvariant()
        $xplatRevision = (
            & git -C $repositoryRoot rev-parse --verify 'HEAD^{commit}'
        ).Trim()
        $xplatTree = (
            & git -C $repositoryRoot rev-parse --verify 'HEAD^{tree}'
        ).Trim()
        $xplatStatus = @(
            & git -C $repositoryRoot status `
                --porcelain=v2 --untracked-files=all)
        $compilerOptions = @(
            $arguments | Where-Object {
                $_.StartsWith('-', [StringComparison]::Ordinal) -and
                -not $_.StartsWith(
                    '-Fu',
                    [StringComparison]::OrdinalIgnoreCase) -and
                -not $_.StartsWith(
                    '-FD',
                    [StringComparison]::OrdinalIgnoreCase) -and
                -not $_.StartsWith(
                    '-Fl',
                    [StringComparison]::OrdinalIgnoreCase) -and
                -not $_.StartsWith(
                    '-FU',
                    [StringComparison]::Ordinal) -and
                -not $_.StartsWith(
                    '-FE',
                    [StringComparison]::OrdinalIgnoreCase) -and
                -not $_.StartsWith(
                    '-o',
                    [StringComparison]::Ordinal)
            }
        )
        $unitSearchPaths = @(
            $arguments | Where-Object {
                $_.StartsWith(
                    '-Fu',
                    [StringComparison]::Ordinal)
            } | ForEach-Object { $_.Substring(3) }
        )
        $toolSearchPaths = @(
            $arguments | Where-Object {
                $_.StartsWith(
                    '-FD',
                    [StringComparison]::Ordinal)
            } | ForEach-Object { $_.Substring(3) }
        )
        $librarySearchPaths = @(
            $arguments | Where-Object {
                $_.StartsWith(
                    '-Fl',
                    [StringComparison]::Ordinal)
            } | ForEach-Object { $_.Substring(3) }
        )
        $provenance = [ordered]@{
            schemaVersion = 1
            adapterId = [string] $descriptor.adapterId
            versionId = $versionId
            source = [string] $descriptor.source
            sourceSha256 = $sourceHash
            buildRecipe = [string] $descriptor.buildRecipe
            buildRecipeSha256 = $recipeHash
            selectedCaseIds = @(
                Get-OrdinallySortedStrings @(
                    $caseIdsByVersion[$versionId]))
            reference = [ordered]@{
                definition = $referencePath
                definitionSha256 = $referenceHash
                bundle = $bundlePath
                bundleSha256 = $bundleHash
            }
            legacy = [ordered]@{
                repository = [string] $reference.repository
                revision = [string] $reference.revision
                tree = [string] $reference.tree
                root = $legacyRootPath
                clean = $true
            }
            xplat = [ordered]@{
                revision = $xplatRevision
                tree = $xplatTree
                clean = $xplatStatus.Count -eq 0
            }
            oracle = [ordered]@{
                source = [string] $descriptor.source
                sourcePath = $sourcePath
                sourceSha256 = $sourceHash
                buildRecipe = [string] $descriptor.buildRecipe
                buildRecipePath = $recipePath
                buildRecipeSha256 = $recipeHash
                executable = $executablePath
                executableSha256 = $executableHash
                length = (
                    Get-Item -LiteralPath $executablePath).Length
            }
            toolchain = [ordered]@{
                root = [string] $toolchain.root
                lazarusVersion = [string] $toolchain.lazarusVersion
                fpcVersion = [string] $toolchain.fpcVersion
                targetCpu = [string] $toolchain.targetCpu
                targetOs = [string] $toolchain.targetOs
                compiler = [string] $toolchain.compiler
                compilerSha256 = (
                    [string] $toolchain.compilerSha256
                ).ToLowerInvariant()
                backendCompiler = [string] $toolchain.backendCompiler
                backendCompilerSha256 =
                    ([string] $toolchain.backendCompilerSha256
                    ).ToLowerInvariant()
                lazbuild = [string] $toolchain.lazbuild
                lazbuildSha256 = (
                    [string] $toolchain.lazbuildSha256
                ).ToLowerInvariant()
                fingerprint = [ordered]@{
                    schemaVersion =
                        $toolchain.fingerprint.schemaVersion
                    canonicalization =
                        $toolchain.fingerprint.canonicalization
                    roots = @($toolchain.fingerprint.roots)
                    aggregateSha256 = (
                        [string] $toolchain.fingerprint.aggregateSha256
                    ).ToLowerInvariant()
                    fileCount =
                        [long] $toolchain.fingerprint.fileCount
                    byteCount =
                        [long] $toolchain.fingerprint.byteCount
                }
            }
            build = [ordered]@{
                script = $PSCommandPath
                scriptSha256 = $buildScriptHash
                arguments = $arguments
                invocation = [ordered]@{
                    compiler = $compilerPath
                    options = $compilerOptions
                    unitSearchPaths = $unitSearchPaths
                    toolSearchPaths = $toolSearchPaths
                    librarySearchPaths = $librarySearchPaths
                    unitOutputPath = $unitRoot
                    executableOutputPath = $executableRoot
                    outputExecutable = $executablePath
                    source = $sourcePath
                }
                builtAtUtc = (
                    Get-Date
                ).ToUniversalTime().ToString(
                    'yyyy-MM-ddTHH:mm:ss.fffffffZ')
            }
            manifest = [ordered]@{
                path = $manifestPath
                sha256 = $manifestHash
            }
            observations = $observations
        }
        $provenancePath = Join-Path (
            $buildRoot) 'LegacyOracle.provenance.json'
        $provenanceJson = (
            $provenance | ConvertTo-Json -Depth 12
        ) + "`n"
        Write-Utf8LfAtomic `
            -Path $provenancePath `
            -Value $provenanceJson
        $provenanceHash = (
            Get-FileHash -LiteralPath (
                $provenancePath) -Algorithm SHA256).Hash.ToLowerInvariant()

        $registryEntries.Add([ordered]@{
            adapterId = [string] $descriptor.adapterId
            versionId = $versionId
            source = [string] $descriptor.source
            sourceSha256 = $sourceHash
            buildRecipe = [string] $descriptor.buildRecipe
            buildRecipeSha256 = $recipeHash
            executable = (
                [IO.Path]::GetRelativePath(
                    $repositoryRoot,
                    $executablePath
                ).Replace('\', '/'))
            executableSha256 = $executableHash
            provenance = (
                [IO.Path]::GetRelativePath(
                    $repositoryRoot,
                    $provenancePath
                ).Replace('\', '/'))
            provenanceSha256 = $provenanceHash
        })
    }

    Assert-ReferenceWorktree `
        -Root $legacyRootPath `
        -Reference $reference `
        -Stage 'post-build verification'
    $postBuildFingerprint = & (
        Join-Path $PSScriptRoot 'Get-LegacyToolchainFingerprint.ps1'
    ) -LazarusRoot $toolchain.root `
        -RelativeRoots @($reference.toolchain.fingerprint.roots) |
        ConvertFrom-Json
    if ($postBuildFingerprint.aggregateSha256 -cne
        $toolchain.fingerprint.aggregateSha256 -or
        [long] $postBuildFingerprint.fileCount -ne
        [long] $toolchain.fingerprint.fileCount -or
        [long] $postBuildFingerprint.byteCount -ne
        [long] $toolchain.fingerprint.byteCount) {
        throw 'Legacy toolchain changed during oracle builds.'
    }

    $registry = [ordered]@{
        schemaVersion = 1
        entries = @($registryEntries)
    }
    $registryJson = (
        $registry | ConvertTo-Json -Depth 8
    ).Replace("`r`n", "`n").Replace("`r", "`n") + "`n"
    $registryHash = Get-Sha256Text $registryJson
    $registryPath = Join-Path (
        $registriesRoot) "$registryHash.json"
    if (Test-Path -LiteralPath $registryPath -PathType Leaf) {
        $existingRegistryHash = (
            Get-FileHash -LiteralPath (
                $registryPath) -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not (Test-SameSha256 `
                $registryHash $existingRegistryHash)) {
            throw (
                'Existing content-addressed legacy oracle registry has ' +
                "unexpected bytes: $registryPath")
        }
    } else {
        Write-Utf8LfAtomic `
            -Path $registryPath `
            -Value $registryJson
    }

    $env:MORSE_RUNNER_LEGACY_ROOT = $legacyRootPath
    $env:MORSE_RUNNER_LEGACY_ORACLE_REGISTRY = $registryPath
    $env:MORSE_RUNNER_LEGACY_ORACLE_REGISTRY_SHA256 = $registryHash
    if ($registryEntries.Count -eq 1) {
        $singleEntry = $registryEntries[0]
        $env:MORSE_RUNNER_LEGACY_ORACLE = Join-Path (
            $repositoryRoot) $singleEntry.executable
        $env:MORSE_RUNNER_LEGACY_PROVENANCE =
            Join-Path $repositoryRoot $singleEntry.provenance
        $env:MORSE_RUNNER_LEGACY_ORACLE_SHA256 =
            $singleEntry.executableSha256
        $env:MORSE_RUNNER_LEGACY_PROVENANCE_SHA256 =
            $singleEntry.provenanceSha256
    } else {
        $env:MORSE_RUNNER_LEGACY_ORACLE = $null
        $env:MORSE_RUNNER_LEGACY_PROVENANCE = $null
        $env:MORSE_RUNNER_LEGACY_ORACLE_SHA256 = $null
        $env:MORSE_RUNNER_LEGACY_PROVENANCE_SHA256 = $null
    }

    Write-Host "Built $($registryEntries.Count) pinned legacy oracle version(s)."
    Write-Host "Legacy oracle registry: $registryPath"
    Write-Host "Legacy oracle registry SHA-256: $registryHash"
    if ($PassThru) {
        [pscustomobject]@{
            registry = $registryPath
            registrySha256 = $registryHash
            entries = @($registryEntries)
        }
    }
} catch {
    foreach ($createdBuildRoot in $createdBuildRoots) {
        if (Test-Path -LiteralPath $createdBuildRoot) {
            Remove-SafeDirectoryTree `
                -Root $repositoryRoot `
                -Path $createdBuildRoot `
                -Description 'Failed isolated legacy oracle build'
        }
    }
    throw
}
