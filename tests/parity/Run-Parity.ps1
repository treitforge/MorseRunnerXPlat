[CmdletBinding()]
param(
    [ValidateSet('Legacy', 'XPlat', 'Both')]
    [string] $Target = 'Both',

    [ValidateSet('Baseline', 'PullRequest', 'Development', 'Release')]
    [string] $Mode = 'Development',

    [string] $LegacyRoot
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$testProject = Join-Path $repositoryRoot 'tests\MorseRunner.LegacyParity.Tests\MorseRunner.LegacyParity.Tests.csproj'

if ($LegacyRoot) {
    $env:MORSE_RUNNER_LEGACY_ROOT = (Resolve-Path $LegacyRoot).Path
}

if ($Target -in @('Legacy', 'Both')) {
    & dotnet test --project $testProject --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "Parity target execution failed with exit code $LASTEXITCODE."
    }
}

$arguments = @(
    'run'
    '--locked'
    'python'
    'tools\parity\validate_parity.py'
    '--mode'
    $Mode
)
if ($LegacyRoot) {
    $arguments += @('--legacy-root', $env:MORSE_RUNNER_LEGACY_ROOT)
}

& uv @arguments
if ($LASTEXITCODE -ne 0) {
    throw "$Mode parity validation failed with exit code $LASTEXITCODE."
}
