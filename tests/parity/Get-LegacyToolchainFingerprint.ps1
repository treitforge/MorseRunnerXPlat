[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $LazarusRoot,

    [Parameter(Mandatory)]
    [string[]] $RelativeRoots,

    [switch] $IncludeFiles
)

$ErrorActionPreference = 'Stop'

function Get-NormalizedRelativePath {
    param(
        [Parameter(Mandatory)]
        [string] $BasePath,

        [Parameter(Mandatory)]
        [string] $Path
    )

    $relativePath = [IO.Path]::GetRelativePath($BasePath, $Path)
    if ([IO.Path]::IsPathRooted($relativePath) -or
        $relativePath -eq '..' -or
        $relativePath.StartsWith(
            '..' + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::Ordinal) -or
        $relativePath.StartsWith(
            '..' + [IO.Path]::AltDirectorySeparatorChar,
            [StringComparison]::Ordinal)) {
        throw "Toolchain path escapes the Lazarus root: $Path"
    }

    return $relativePath.Replace('\', '/').
        Normalize([Text.NormalizationForm]::FormC).
        ToLowerInvariant()
}

function Test-PathContainedBy {
    param(
        [Parameter(Mandatory)]
        [string] $Parent,

        [Parameter(Mandatory)]
        [string] $Candidate
    )

    $parentPath = [IO.Path]::GetFullPath($Parent)
    $candidatePath = [IO.Path]::GetFullPath($Candidate)
    $parentPrefix = $parentPath.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    ) + [IO.Path]::DirectorySeparatorChar

    return $candidatePath.StartsWith(
        $parentPrefix,
        [StringComparison]::OrdinalIgnoreCase)
}

function Add-CanonicalRecord {
    param(
        [Parameter(Mandatory)]
        [Security.Cryptography.IncrementalHash] $Hash,

        [Parameter(Mandatory)]
        [string] $Record
    )

    $bytes = [Text.Encoding]::UTF8.GetBytes($Record)
    $Hash.AppendData($bytes)
}

$rootPath = (Resolve-Path -LiteralPath $LazarusRoot).Path
if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) {
    throw "Lazarus root is not a directory: $rootPath"
}
$rootItem = Get-Item -LiteralPath $rootPath -Force
if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Lazarus root cannot be a reparse point: $rootPath"
}

$normalizedRoots = [Collections.Generic.List[string]]::new()
$rootLookup = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($relativeRoot in $RelativeRoots) {
    if ([String]::IsNullOrWhiteSpace($relativeRoot) -or
        [IO.Path]::IsPathRooted($relativeRoot)) {
        throw "Toolchain fingerprint root must be a non-empty relative path: $relativeRoot"
    }

    $candidatePath = [IO.Path]::GetFullPath(
        (Join-Path $rootPath $relativeRoot))
    if (-not (Test-PathContainedBy -Parent $rootPath -Candidate $candidatePath)) {
        throw "Toolchain fingerprint root escapes the Lazarus root: $relativeRoot"
    }

    $normalizedRoot = Get-NormalizedRelativePath `
        -BasePath $rootPath `
        -Path $candidatePath
    if (-not $rootLookup.Add($normalizedRoot)) {
        throw "Duplicate toolchain fingerprint root: $relativeRoot"
    }
    if (-not (Test-Path -LiteralPath $candidatePath)) {
        throw "Toolchain fingerprint root not found: $candidatePath"
    }

    $normalizedRoots.Add($normalizedRoot)
}

$rootNames = $normalizedRoots.ToArray()
[Array]::Sort($rootNames, [StringComparer]::Ordinal)

$fileLookup = @{}
foreach ($normalizedRoot in $rootNames) {
    $candidatePath = [IO.Path]::GetFullPath(
        (Join-Path $rootPath $normalizedRoot.Replace(
            '/',
            [IO.Path]::DirectorySeparatorChar)))
    $rootItem = Get-Item -LiteralPath $candidatePath -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Toolchain fingerprint root cannot be a reparse point: $candidatePath"
    }
    $files = if ($rootItem.PSIsContainer) {
        $descendants = @(
            Get-ChildItem -LiteralPath $candidatePath -Force -Recurse)
        $reparsePoint = $descendants |
            Where-Object {
                ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
            } |
            Select-Object -First 1
        if ($null -ne $reparsePoint) {
            throw "Toolchain fingerprint tree contains a reparse point: $($reparsePoint.FullName)"
        }
        @($descendants | Where-Object { -not $_.PSIsContainer })
    } else {
        @($rootItem)
    }

    foreach ($file in $files) {
        $filePath = [IO.Path]::GetFullPath($file.FullName)
        if (-not (Test-PathContainedBy -Parent $rootPath -Candidate $filePath)) {
            throw "Toolchain file escapes the Lazarus root: $filePath"
        }

        $normalizedPath = Get-NormalizedRelativePath `
            -BasePath $rootPath `
            -Path $filePath
        if ($fileLookup.ContainsKey($normalizedPath)) {
            throw "Toolchain fingerprint roots overlap at: $normalizedPath"
        }

        $fileLookup[$normalizedPath] = $file
    }
}

$fileNames = [string[]] @($fileLookup.Keys)
[Array]::Sort($fileNames, [StringComparer]::Ordinal)

$filePaths = [string[]] @(
    foreach ($normalizedPath in $fileNames) {
        $fileLookup[$normalizedPath].FullName
    }
)
$hashLookup = [Collections.Generic.Dictionary[string, string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($hashResult in @(
        Get-FileHash -LiteralPath $filePaths -Algorithm SHA256)) {
    if (-not $hashLookup.TryAdd(
            [IO.Path]::GetFullPath($hashResult.Path),
            $hashResult.Hash)) {
        throw "Duplicate hashed toolchain file: $($hashResult.Path)"
    }
}
if ($hashLookup.Count -ne $fileNames.Count) {
    throw "Expected $($fileNames.Count) toolchain file hashes, got $($hashLookup.Count)."
}

$aggregate = [Security.Cryptography.IncrementalHash]::CreateHash(
    [Security.Cryptography.HashAlgorithmName]::SHA256)
try {
    Add-CanonicalRecord `
        -Hash $aggregate `
        -Record "MORSE_RUNNER_LEGACY_TOOLCHAIN_V1`n"
    foreach ($normalizedRoot in $rootNames) {
        Add-CanonicalRecord `
            -Hash $aggregate `
            -Record "ROOT`0$normalizedRoot`n"
    }

    $byteCount = [long] 0
    $fileRecords = [Collections.Generic.List[object]]::new()
    foreach ($normalizedPath in $fileNames) {
        $file = $fileLookup[$normalizedPath]
        $length = [long] $file.Length
        $fileHash = $hashLookup[[IO.Path]::GetFullPath($file.FullName)]
        Add-CanonicalRecord `
            -Hash $aggregate `
            -Record "FILE`0$normalizedPath`0$length`0$fileHash`n"
        $byteCount += $length

        if ($IncludeFiles) {
            $fileRecords.Add([ordered]@{
                path = $normalizedPath
                length = $length
                sha256 = $fileHash
            })
        }
    }

    $aggregateHash = [Convert]::ToHexString(
        $aggregate.GetHashAndReset()).ToLowerInvariant()
} finally {
    $aggregate.Dispose()
}

$result = [ordered]@{
    schemaVersion = 1
    canonicalization = 'utf8-lf-nul-lowercase-relative-path-v1'
    roots = $rootNames
    aggregateSha256 = $aggregateHash
    fileCount = $fileNames.Count
    byteCount = $byteCount
}
if ($IncludeFiles) {
    $result.files = $fileRecords.ToArray()
}

$result | ConvertTo-Json -Depth 5 -Compress
