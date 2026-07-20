[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ParityPathSafety.ps1')

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
$testRoot = Join-Path (
    $artifactsRoot) ('path-safety-test-' + [Guid]::NewGuid().ToString('N'))
$outsideRoot = Join-Path (
    $artifactsRoot) ('path-safety-sentinel-' + [Guid]::NewGuid().ToString('N'))
$redirectPath = Join-Path $testRoot 'redirect'
$caseRoot = Join-Path (
    $artifactsRoot) ('Path-Safety-Case-' + [Guid]::NewGuid().ToString('N'))
$runningOnWindows =
    [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [Runtime.InteropServices.OSPlatform]::Windows)

try {
    New-Item -ItemType Directory -Force `
        -Path $testRoot, $outsideRoot, $caseRoot |
        Out-Null
    $sentinelPath = Join-Path $outsideRoot 'sentinel.txt'
    [IO.File]::WriteAllText($sentinelPath, 'preserve')

    foreach ($escapeCandidate in @(
            (Join-Path $repositoryRoot '..\outside'),
            [IO.Path]::GetPathRoot($repositoryRoot)
        )) {
        $escapeBlocked = $false
        try {
            Assert-SafeDescendantPath `
                -Root $repositoryRoot `
                -Candidate $escapeCandidate `
                -Description 'Lexical path-safety test target' |
                Out-Null
        } catch {
            $escapeBlocked =
                $_.Exception.Message -like '*must be below*'
        }
        if (-not $escapeBlocked) {
            throw (
                'An absolute or parent-traversal path escaped the ' +
                'repository root.')
        }
    }

    if (-not $runningOnWindows) {
        $caseDistinctCandidate = Join-Path (
            $caseRoot.ToLowerInvariant()) 'child'
        $caseEscapeBlocked = $false
        try {
            Assert-SafeDescendantPath `
                -Root $caseRoot `
                -Candidate $caseDistinctCandidate `
                -Description 'Case-sensitive sibling test' |
                Out-Null
        } catch {
            $caseEscapeBlocked =
                $_.Exception.Message -like '*must be below*'
        }
        if (-not $caseEscapeBlocked) {
            throw (
                'A case-distinct path escaped a root on a ' +
                'case-sensitive platform.')
        }
    }

    $redirectCreated = $false
    try {
        $itemType = if ($runningOnWindows) {
            'Junction'
        } else {
            'SymbolicLink'
        }
        New-Item -ItemType $itemType `
            -Path $redirectPath `
            -Target $outsideRoot |
            Out-Null
        $redirectCreated = $true
    } catch {
        if ($runningOnWindows) {
            throw
        }
        Write-Warning (
            'Symbolic-link traversal primitive could not be created; ' +
            'lexical and case-sensitive containment checks still ran.')
    }

    if ($redirectCreated) {
        $copyPackageRoot = Join-Path $testRoot 'package'
        New-Item -ItemType Directory -Force `
            -Path $copyPackageRoot |
            Out-Null
        $copyBlocked = $false
        try {
            Copy-SafeRepositoryFileToPackage `
                -RepositoryRoot $repositoryRoot `
                -PackageRoot $copyPackageRoot `
                -Source (Join-Path $redirectPath 'sentinel.txt') `
                -Description 'Redirected package source'
        } catch {
            $copyBlocked = $_.Exception.Message -like '*reparse point*'
        }
        if (-not $copyBlocked) {
            throw (
                'A package source through a filesystem redirect was not ' +
                'rejected.')
        }

        $blocked = $false
        try {
            Assert-SafeDescendantPath `
                -Root $repositoryRoot `
                -Candidate (Join-Path $redirectPath 'child') `
                -Description 'Path-safety test target'
        } catch {
            $blocked = $_.Exception.Message -like '*reparse point*'
        }

        if (-not $blocked) {
            throw (
                'A recursive operation through a filesystem redirect ' +
                'was not rejected.')
        }
        if (-not (Test-Path -LiteralPath $sentinelPath -PathType Leaf)) {
            throw 'The redirect target sentinel was modified.'
        }
    }
} finally {
    if (Test-Path -LiteralPath $redirectPath) {
        Remove-Item -LiteralPath $redirectPath -Force
    }
    Remove-SafeDirectoryTree `
        -Root $repositoryRoot `
        -Path $testRoot `
        -Description 'Path-safety test root'
    Remove-SafeDirectoryTree `
        -Root $repositoryRoot `
        -Path $outsideRoot `
        -Description 'Path-safety sentinel root'
    Remove-SafeDirectoryTree `
        -Root $repositoryRoot `
        -Path $caseRoot `
        -Description 'Path-safety case root'
}

Write-Host 'Parity path safety checks passed.'
