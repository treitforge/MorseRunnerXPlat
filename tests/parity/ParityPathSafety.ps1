Set-StrictMode -Version 3.0

function Assert-SafeDescendantPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $Candidate,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $rootPath = (Resolve-Path -LiteralPath $Root).Path
    $candidatePath = [IO.Path]::GetFullPath($Candidate)
    $rootPrefix = $rootPath.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    ) + [IO.Path]::DirectorySeparatorChar
    $runningOnWindows =
        [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [Runtime.InteropServices.OSPlatform]::Windows)
    $pathComparison = if ($runningOnWindows) {
        [StringComparison]::OrdinalIgnoreCase
    } else {
        [StringComparison]::Ordinal
    }
    if (-not $candidatePath.StartsWith(
            $rootPrefix,
            $pathComparison)) {
        throw "$Description must be below $rootPath."
    }

    $relativePath = [IO.Path]::GetRelativePath($rootPath, $candidatePath)
    $segments = $relativePath.Split(
        [char[]] @(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar),
        [StringSplitOptions]::RemoveEmptyEntries)
    $currentPath = $rootPath
    foreach ($segment in $segments) {
        $currentPath = Join-Path $currentPath $segment
        if (-not (Test-Path -LiteralPath $currentPath)) {
            break
        }

        $item = Get-Item -LiteralPath $currentPath -Force
        if (($item.Attributes -band
                [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw (
                "$Description traverses a reparse point: " +
                $item.FullName)
        }
    }

    return $candidatePath
}

function Remove-SafeDirectoryTree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $safePath = Assert-SafeDescendantPath `
        -Root $Root `
        -Candidate $Path `
        -Description $Description
    if (-not (Test-Path -LiteralPath $safePath)) {
        return
    }
    if (-not (Test-Path -LiteralPath $safePath -PathType Container)) {
        throw "$Description is not a directory: $safePath"
    }

    Remove-Item -LiteralPath $safePath -Recurse -Force
}

function Copy-SafeRepositoryFileToPackage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot,

        [Parameter(Mandatory)]
        [string] $PackageRoot,

        [Parameter(Mandatory)]
        [string] $Source,

        [string] $DestinationRelativePath,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $sourceCandidate = if ([IO.Path]::IsPathFullyQualified($Source)) {
        $Source
    } else {
        Join-Path $root $Source
    }
    $sourcePath = Assert-SafeDescendantPath `
        -Root $root `
        -Candidate $sourceCandidate `
        -Description $Description
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "$Description is not a file: $sourcePath"
    }
    $package = Assert-SafeDescendantPath `
        -Root $root `
        -Candidate $PackageRoot `
        -Description 'Parity package root'
    if (-not (Test-Path -LiteralPath $package -PathType Container)) {
        throw "Parity package root is not a directory: $package"
    }
    $relativePath = if ([string]::IsNullOrWhiteSpace(
            $DestinationRelativePath)) {
        [IO.Path]::GetRelativePath($root, $sourcePath)
    } else {
        $segments = [string[]] $DestinationRelativePath.Split('/')
        if (-not $DestinationRelativePath.StartsWith(
                'artifacts/',
                [StringComparison]::Ordinal) -or
            $DestinationRelativePath.Contains('\') -or
            @($segments | Where-Object {
                    $_ -in @('', '.', '..')
                }).Count -ne 0) {
            throw (
                "Packaged $Description destination must be a safe " +
                'repository-relative artifacts/ path.')
        }
        $DestinationRelativePath.Replace(
            '/', [IO.Path]::DirectorySeparatorChar)
    }
    $destination = Assert-SafeDescendantPath `
        -Root $package `
        -Candidate (Join-Path $package $relativePath) `
        -Description "Packaged $Description"
    New-Item -ItemType Directory -Force `
        -Path (Split-Path -Parent $destination) |
        Out-Null
    Copy-Item -LiteralPath $sourcePath -Destination $destination
}
