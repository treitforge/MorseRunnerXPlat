[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$errors = [System.Collections.Generic.List[string]]::new()

function Add-ValidationError {
    param([string]$Message)
    $errors.Add($Message)
}

function Test-RequiredFile {
    param([string]$RelativePath)
    $path = Join-Path $root $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Add-ValidationError "Missing required file: $RelativePath"
    }
}

function Test-Frontmatter {
    param(
        [string]$Path,
        [string]$ExpectedName = ''
    )

    $relative = [System.IO.Path]::GetRelativePath($root, $Path)
    $lines = Get-Content -LiteralPath $Path
    if ($lines.Count -lt 5 -or $lines[0] -ne '---') {
        Add-ValidationError "Missing frontmatter at line 1: $relative"
        return
    }

    $closing = [Array]::IndexOf($lines, '---', 1)
    if ($closing -lt 3) {
        Add-ValidationError "Malformed frontmatter: $relative"
        return
    }

    $frontmatter = $lines[1..($closing - 1)] -join "`n"
    $nameMatch = [regex]::Match($frontmatter, '(?m)^name:\s*([a-z0-9-]+)\s*$')
    $descriptionMatch = [regex]::Match($frontmatter, '(?m)^description:\s*(.+)$')

    if (-not $nameMatch.Success) {
        Add-ValidationError "Missing lowercase hyphenated name: $relative"
    } elseif ($ExpectedName -and $nameMatch.Groups[1].Value -ne $ExpectedName) {
        Add-ValidationError "Name does not match containing folder in $relative"
    }

    if (-not $descriptionMatch.Success) {
        Add-ValidationError "Missing description: $relative"
    }

    $placeholderPattern = '\bTO' + 'DO\b'
    if (($lines -join "`n") -match $placeholderPattern) {
        Add-ValidationError "Unresolved work-item placeholder: $relative"
    }
}

@(
    'AGENTS.md'
    '.python-version'
    'pyproject.toml'
    'uv.lock'
    '.codex\config.toml'
    '.codex\hooks.json'
    '.github\copilot-instructions.md'
    '.github\hooks\copilot-policy.json'
    'docs\architecture\engineering-specification.md'
    'tools\agent_scaffolding\validate_yaml.py'
) | ForEach-Object { Test-RequiredFile $_ }

foreach ($jsonFile in @('.codex\hooks.json', '.github\hooks\copilot-policy.json')) {
    $path = Join-Path $root $jsonFile
    if (Test-Path -LiteralPath $path) {
        try {
            Get-Content -LiteralPath $path -Raw | ConvertFrom-Json | Out-Null
        } catch {
            Add-ValidationError "Invalid JSON in $jsonFile`: $($_.Exception.Message)"
        }
    }
}

$sharedSkills = Join-Path $root '.agents\skills'
$copilotSkills = Join-Path $root '.github\skills'
if (Test-Path -LiteralPath $sharedSkills) {
    Get-ChildItem -LiteralPath $sharedSkills -Directory | ForEach-Object {
        $skillName = $_.Name
        $sharedSkill = Join-Path $_.FullName 'SKILL.md'
        $copilotSkill = Join-Path $copilotSkills "$skillName\SKILL.md"
        if (-not (Test-Path -LiteralPath $sharedSkill)) {
            Add-ValidationError "Missing shared SKILL.md for $skillName"
            return
        }

        Test-Frontmatter -Path $sharedSkill -ExpectedName $skillName

        if (-not (Test-Path -LiteralPath $copilotSkill)) {
            Add-ValidationError "Missing Copilot skill mirror for $skillName"
        } else {
            $sharedHash = (Get-FileHash -LiteralPath $sharedSkill -Algorithm SHA256).Hash
            $copilotHash = (Get-FileHash -LiteralPath $copilotSkill -Algorithm SHA256).Hash
            if ($sharedHash -ne $copilotHash) {
                Add-ValidationError "Skill mirror differs for $skillName"
            }
        }
    }
}

foreach ($pattern in @('.github\agents\*.agent.md', '.github\prompts\*.prompt.md')) {
    Get-ChildItem -Path (Join-Path $root $pattern) -File | ForEach-Object {
        Test-Frontmatter -Path $_.FullName
    }
}

$codexAgents = Get-ChildItem -Path (Join-Path $root '.codex\agents\*.toml') -File
foreach ($agent in $codexAgents) {
    $content = Get-Content -LiteralPath $agent.FullName -Raw
    if ($content -notmatch '(?m)^name\s*=\s*"[a-z0-9-]+"\s*$') {
        Add-ValidationError "Missing valid name in $([System.IO.Path]::GetRelativePath($root, $agent.FullName))"
    }
    if ($content -notmatch '(?m)^description\s*=\s*".+"\s*$') {
        Add-ValidationError "Missing description in $([System.IO.Path]::GetRelativePath($root, $agent.FullName))"
    }
    if ($content -notmatch '(?m)^developer_instructions\s*=\s*"""') {
        Add-ValidationError "Missing developer instructions in $([System.IO.Path]::GetRelativePath($root, $agent.FullName))"
    }
}

$uv = Get-Command uv -ErrorAction SilentlyContinue
if (-not $uv) {
    Add-ValidationError 'uv is required to validate YAML and skill metadata.'
} elseif (Test-Path -LiteralPath (Join-Path $root 'uv.lock')) {
    Push-Location $root
    try {
        & uv run --locked python tools\agent_scaffolding\validate_yaml.py
        if ($LASTEXITCODE -ne 0) {
            Add-ValidationError 'uv-managed YAML and skill validation failed.'
        }
    } catch {
        Add-ValidationError "Unable to run uv-managed YAML validation: $($_.Exception.Message)"
    } finally {
        Pop-Location
    }
}

if ($errors.Count -gt 0) {
    Write-Error ("Agent scaffolding validation failed:`n - " + ($errors -join "`n - "))
    exit 1
}

Write-Host "Agent scaffolding validation passed."
exit 0
