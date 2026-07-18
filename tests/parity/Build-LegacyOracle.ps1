[CmdletBinding()]
param(
    [string] $LegacyRoot = (Join-Path $PSScriptRoot '..\..\..\MorseRunner'),

    [string] $LazarusRoot = 'C:\lazarus'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$legacyRootPath = (Resolve-Path $LegacyRoot).Path
$sourcePath = Join-Path $PSScriptRoot 'legacy-oracle\LegacyOracle.lpr'
$outputRoot = Join-Path $repositoryRoot 'artifacts\legacy-oracle'
$unitRoot = Join-Path $outputRoot 'units'
$compiler = Join-Path $LazarusRoot 'fpc\3.2.2\bin\x86_64-win64\fpc.exe'

if (-not (Test-Path $compiler -PathType Leaf)) {
    throw "Pinned FPC compiler not found: $compiler"
}

New-Item -ItemType Directory -Force -Path $outputRoot, $unitRoot | Out-Null

$arguments = @(
    '-Twin64'
    '-Px86_64'
    '-MDelphi'
    '-O2'
    "-Fu$legacyRootPath"
    "-Fu$(Join-Path $legacyRootPath 'Lazarus')"
    "-Fu$(Join-Path $legacyRootPath 'VCL')"
    "-Fu$(Join-Path $legacyRootPath 'Util')"
    "-Fu$(Join-Path $LazarusRoot 'lcl\units\x86_64-win64\win32')"
    "-Fu$(Join-Path $LazarusRoot 'lcl\units\x86_64-win64')"
    "-Fu$(Join-Path $LazarusRoot 'components\freetype\lib\x86_64-win64')"
    "-Fu$(Join-Path $LazarusRoot 'components\lazutils\lib\x86_64-win64')"
    "-Fu$(Join-Path $LazarusRoot 'packager\units\x86_64-win64')"
    "-FU$unitRoot"
    "-FE$outputRoot"
    "-o$(Join-Path $outputRoot 'LegacyOracle.exe')"
    $sourcePath
)

& $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Legacy oracle compilation failed with exit code $LASTEXITCODE."
}

$revision = (& git -C $legacyRootPath rev-parse --verify HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'Could not read the legacy revision.'
}
if ($revision -ne '55bbd019c29d8cf693184ea420a17a253f16fe1e') {
    throw "Legacy revision mismatch: $revision"
}

Write-Host "Built pinned legacy oracle: $(Join-Path $outputRoot 'LegacyOracle.exe')"
