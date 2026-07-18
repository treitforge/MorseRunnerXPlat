[CmdletBinding()]
param(
    [string] $LegacyRoot
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

& uv run --locked python -m unittest discover -s tools\parity -p 'test_*.py'
if ($LASTEXITCODE -ne 0) {
    throw "Parity tooling tests failed with exit code $LASTEXITCODE."
}

$arguments = @(
    'run'
    '--locked'
    'python'
    'tools\parity\validate_parity.py'
    '--mode'
    'completeness'
)

if ($LegacyRoot) {
    $arguments += @('--legacy-root', (Resolve-Path $LegacyRoot).Path)
}

& uv @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Parity completeness validation failed with exit code $LASTEXITCODE."
}
