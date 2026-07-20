[CmdletBinding()]
param(
    [string] $SourceRepository = (Join-Path $PSScriptRoot '..\..\..\MorseRunner'),

    [string] $Destination = (Join-Path $PSScriptRoot '..\..\artifacts\legacy-reference')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ParityPathSafety.ps1')
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifactsRoot = Assert-SafeDescendantPath `
    -Root $repositoryRoot `
    -Candidate (Join-Path $repositoryRoot 'artifacts') `
    -Description 'Artifacts root'
New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
$destinationPath = Assert-SafeDescendantPath `
    -Root $artifactsRoot `
    -Candidate $Destination `
    -Description 'Legacy reference destination'

$referencePath = Assert-SafeDescendantPath `
    -Root $repositoryRoot `
    -Candidate (Join-Path $PSScriptRoot 'legacy-reference.json') `
    -Description 'Legacy reference definition'
$reference = Get-Content -LiteralPath $referencePath -Raw | ConvertFrom-Json
if ($reference.schemaVersion -ne 1) {
    throw "Unsupported legacy reference schema: $($reference.schemaVersion)"
}

$expectedBundleRelativePath = 'tests/parity/legacy-reference.bundle'
if ([string] $reference.bundle -cne $expectedBundleRelativePath) {
    throw (
        'Legacy reference bundle must use the canonical repository path ' +
        "'$expectedBundleRelativePath'.")
}
$bundlePath = Assert-SafeDescendantPath `
    -Root $repositoryRoot `
    -Candidate (Join-Path $repositoryRoot $expectedBundleRelativePath) `
    -Description 'Legacy reference bundle'
if (-not (Test-Path -LiteralPath $bundlePath -PathType Leaf)) {
    throw "Pinned legacy bundle not found: $bundlePath"
}

$bundleHash = (
    Get-FileHash -LiteralPath (
        $bundlePath) -Algorithm SHA256).Hash.ToLowerInvariant()
if ($bundleHash -cne $reference.bundleSha256) {
    throw "Pinned legacy bundle hash mismatch: $bundleHash"
}

$source = if (Test-Path -LiteralPath $SourceRepository) {
    (Resolve-Path -LiteralPath $SourceRepository).Path
} else {
    $SourceRepository
}

Remove-SafeDirectoryTree `
    -Root $repositoryRoot `
    -Path $destinationPath `
    -Description 'Legacy reference destination'
New-Item -ItemType Directory -Force -Path (
    Split-Path -Parent $destinationPath) | Out-Null

& git clone --no-checkout --no-tags -- $source $destinationPath
if ($LASTEXITCODE -ne 0) {
    throw "Could not clone the legacy source repository: $source"
}

& git -C $destinationPath bundle verify $bundlePath
if ($LASTEXITCODE -ne 0) {
    throw 'Pinned legacy bundle or its public prerequisite is invalid.'
}

& git -C $destinationPath cat-file -e "$($reference.revision)^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) {
    & git -C $destinationPath fetch --no-tags -- $bundlePath `
        'refs/heads/experimental/lazarus-port:refs/remotes/bundle/experimental-lazarus-port'
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not import the pinned legacy commit from the bundle.'
    }
}

& git -C $destinationPath checkout --detach $reference.revision
if ($LASTEXITCODE -ne 0) {
    throw "Could not check out pinned legacy revision $($reference.revision)."
}

$revision = (& git -C $destinationPath rev-parse --verify 'HEAD^{commit}').Trim()
if ($LASTEXITCODE -ne 0 -or $revision -cne $reference.revision) {
    throw "Legacy revision mismatch: $revision"
}

$tree = (& git -C $destinationPath rev-parse --verify 'HEAD^{tree}').Trim()
if ($LASTEXITCODE -ne 0 -or $tree -cne $reference.tree) {
    throw "Legacy tree mismatch: $tree"
}

$status = @(& git -C $destinationPath status --porcelain=v2 --untracked-files=all)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not inspect the prepared legacy worktree.'
}
if ($status.Count -ne 0) {
    throw "Prepared legacy worktree is dirty:`n$($status -join "`n")"
}

Write-Host "Prepared clean legacy reference: $destinationPath"
Write-Output $destinationPath
