[CmdletBinding()]
param(
    [string]$File = $env:COPILOT_FILE
)

$ErrorActionPreference = 'Stop'

if (-not $File -or -not (Test-Path -LiteralPath $File -PathType Leaf)) {
    exit 0
}

$content = Get-Content -LiteralPath $File -Raw
$secretPattern = '(?i)(api[_-]?key|client[_-]?secret|password|access[_-]?token|connection[_-]?string)\s*[:=]\s*[''"][^''"]+[''"]'

if ($content -match $secretPattern) {
    Write-Error "Potential hardcoded secret detected in $File. Use environment variables or a secure provider."
    exit 1
}

$portableProjectPattern = '(?i)[\\/](MorseRunner\.(Domain|Dsp|Engine))[\\/]'
if ($File -match $portableProjectPattern) {
    $platformPattern = '(?i)(Microsoft\.Win32|System\.Management|System\.Windows|DllImport\s*\(\s*[''"](winmm|kernel32|user32))'
    if ($content -match $platformPattern) {
        Write-Warning "Portable core file references a Windows-specific API: $File"
    }
}

exit 0
