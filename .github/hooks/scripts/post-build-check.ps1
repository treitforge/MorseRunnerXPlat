[CmdletBinding()]
param(
    [string]$Command = $env:COPILOT_TOOL_INPUT
)

$ErrorActionPreference = 'Stop'

if (-not $Command) {
    exit 0
}

if ($Command -match '(?i)dotnet\s+(build|test|format)') {
    $validator = Join-Path $PSScriptRoot 'validate-agent-scaffolding.ps1'
    if (Test-Path -LiteralPath $validator) {
        & $validator
        exit $LASTEXITCODE
    }
}

exit 0
