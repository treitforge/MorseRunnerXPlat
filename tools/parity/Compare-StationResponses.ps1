[CmdletBinding()]
param(
  [string]$LegacyRepository,
  [string]$LegacyExecutable,
  [switch]$BuildLegacy,
  [switch]$KeepTraces
)

$ErrorActionPreference = 'Stop'

$xplatRepository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($LegacyRepository)) {
  $LegacyRepository = Join-Path (Split-Path -Parent $xplatRepository) 'MorseRunner'
}

$xplatRepository = (Resolve-Path -LiteralPath $xplatRepository).Path
$LegacyRepository = (Resolve-Path -LiteralPath $LegacyRepository).Path
if ([string]::IsNullOrWhiteSpace($LegacyExecutable)) {
  $LegacyExecutable = Join-Path $LegacyRepository 'build\Debug\MorseRunner.exe'
}

if ($BuildLegacy) {
  & (Join-Path $LegacyRepository 'Lazarus\build.ps1') -BuildMode Debug
  if ($LASTEXITCODE -ne 0) {
    throw 'Legacy MorseRunner build failed.'
  }
}

if (-not (Test-Path -LiteralPath $LegacyExecutable -PathType Leaf)) {
  throw "Legacy executable was not found: $LegacyExecutable. Run with -BuildLegacy or build CE first."
}

$probeProject = Join-Path $xplatRepository 'tools\MorseRunner.ParityProbe\MorseRunner.ParityProbe.csproj'
$scenarios = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'station-response-scenarios.json') |
  ConvertFrom-Json
$traceDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("MorseRunnerParity-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $traceDirectory | Out-Null

function Invoke-XPlatProbe([pscustomobject]$Scenario) {
  $output = & dotnet run --no-build --project $probeProject --configuration Release -- `
    --call $Scenario.call `
    --initial $Scenario.initial `
    --copied-call $Scenario.copiedCall `
    --messages $Scenario.messages `
    --seed $Scenario.seed
  if ($LASTEXITCODE -ne 0) {
    throw "XPlat probe failed for '$($Scenario.name)'."
  }

  return ($output | ConvertFrom-Json)
}

function Invoke-CeProbe([pscustomobject]$Scenario, [string]$OutputPath) {
  $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = $LegacyExecutable
  $startInfo.UseShellExecute = $false
  $legacyDataDirectory = $LegacyRepository.TrimEnd('\') + '\'
  foreach ($argument in @(
      $legacyDataDirectory,
      '--station-response-probe',
      '--call', $Scenario.call,
      '--initial', $Scenario.initial,
      '--copied-call', $Scenario.copiedCall,
      '--messages', $Scenario.messages,
      '--seed', [string]$Scenario.seed,
      '--output', $OutputPath)) {
    [void]$startInfo.ArgumentList.Add($argument)
  }

  $process = [System.Diagnostics.Process]::Start($startInfo)
  $process.WaitForExit()
  if ($process.ExitCode -ne 0) {
    throw "CE probe failed for '$($Scenario.name)' with exit code $($process.ExitCode)."
  }
  if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
    throw "CE probe did not write its trace for '$($Scenario.name)'."
  }

  $result = Get-Content -Raw -LiteralPath $OutputPath | ConvertFrom-Json
  if ($null -ne $result.error) {
    throw "CE probe failed for '$($Scenario.name)': $($result.error)"
  }
  return $result
}

try {
  $results = foreach ($scenario in $scenarios) {
    $xplat = Invoke-XPlatProbe $scenario
    $ceOutput = Join-Path $traceDirectory ("$($scenario.name)-ce.json")
    $ce = Invoke-CeProbe $scenario $ceOutput
    [pscustomobject]@{
      Scenario = $scenario.name
      XPlatState = $xplat.State
      CeState = $ce.state
      XPlatReply = $xplat.ReplyKind
      CeReply = $ce.replyKind
      Matched = $xplat.State -eq $ce.state -and $xplat.ReplyKind -eq $ce.replyKind
      XPlatRawReply = $xplat.RawReply
      CeRawReply = $ce.rawReply
    }
  }

  $results | Format-Table Scenario, XPlatState, CeState, XPlatReply, CeReply, Matched -AutoSize
  $mismatches = @($results | Where-Object { -not $_.Matched })
  if ($mismatches.Count -gt 0) {
    throw "$($mismatches.Count) station-response scenario(s) diverged."
  }
}
finally {
  if ($KeepTraces) {
    Write-Host "Retained CE traces: $traceDirectory"
  }
  else {
    Remove-Item -LiteralPath $traceDirectory -Recurse -Force -ErrorAction SilentlyContinue
  }
}
