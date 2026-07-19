[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string] $RuntimeIdentifier,

    [Parameter(Mandatory)]
    [string] $ArchivePath,

    [string] $EvidenceRoot = (
        "artifacts\evidence\$RuntimeIdentifier"
    ),

    [switch] $AttemptPhysicalAudio,

    [switch] $RequirePhysicalAudio,

    [double] $PhysicalAudioSeconds = 30,

    [string] $InitialAudioDevice,

    [string] $RecoveryAudioDevice
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath).Path
$resolvedEvidenceRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot $EvidenceRoot)
)
$requiredEvidencePrefix = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\evidence')
)
if (-not $resolvedEvidenceRoot.StartsWith(
    $requiredEvidencePrefix,
    [System.StringComparison]::OrdinalIgnoreCase
)) {
    throw "Evidence output must remain under '$requiredEvidencePrefix'."
}

$expectedPlatform = switch -Wildcard ($RuntimeIdentifier) {
    'win-*' { 'Windows' }
    'linux-*' { 'Linux' }
    'osx-*' { 'macOS' }
}
$actualPlatform = if ($IsWindows) {
    'Windows'
} elseif ($IsLinux) {
    'Linux'
} elseif ($IsMacOS) {
    'macOS'
} else {
    'Unknown'
}
$expectedArchitecture = if ($RuntimeIdentifier.EndsWith(
    '-arm64',
    [System.StringComparison]::Ordinal
)) {
    'Arm64'
} else {
    'X64'
}
$actualArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
if ($actualPlatform -ne $expectedPlatform) {
    throw "Archive '$RuntimeIdentifier' must run on $expectedPlatform, not $actualPlatform."
}

if ($actualArchitecture.ToString() -ne $expectedArchitecture) {
    throw "Archive '$RuntimeIdentifier' requires $expectedArchitecture, not $actualArchitecture."
}

if (Test-Path -LiteralPath $resolvedEvidenceRoot) {
    $resolvedExisting = (Resolve-Path -LiteralPath $resolvedEvidenceRoot).Path
    if (-not $resolvedExisting.StartsWith(
        $requiredEvidencePrefix,
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Refusing to remove unexpected path '$resolvedExisting'."
    }

    Remove-Item -LiteralPath $resolvedExisting -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $resolvedEvidenceRoot | Out-Null
$installRoot = Join-Path $resolvedEvidenceRoot 'install'
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
if ($resolvedArchive.EndsWith(
    '.zip',
    [System.StringComparison]::OrdinalIgnoreCase
)) {
    Expand-Archive -LiteralPath $resolvedArchive -DestinationPath $installRoot
} elseif ($resolvedArchive.EndsWith(
    '.tar.gz',
    [System.StringComparison]::OrdinalIgnoreCase
)) {
    & tar -xzf $resolvedArchive -C $installRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Archive extraction failed with exit code $LASTEXITCODE."
    }
} else {
    throw "Archive must use the .zip or .tar.gz extension."
}

$extension = if ($IsWindows) { '.exe' } else { '' }
$cli = Join-Path $installRoot "cli\MorseRunner.Cli$extension"
$app = Join-Path $installRoot "app\MorseRunner.App$extension"
$tui = Join-Path $installRoot "tui\MorseRunner.Tui$extension"
$hostExecutable = Join-Path (
    $installRoot
) "engine-host\MorseRunner.EngineHost$extension"
foreach ($executable in @($cli, $app, $tui, $hostExecutable)) {
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Release executable not found: $executable"
    }
}

$scenarioPath = Join-Path $resolvedEvidenceRoot 'in-process-scenario.json'
& $cli scenario | Out-File -LiteralPath $scenarioPath -Encoding utf8
if ($LASTEXITCODE -ne 0) {
    throw "The in-process archive scenario failed with exit code $LASTEXITCODE."
}
$inProcessScenario = Get-Content -LiteralPath $scenarioPath -Raw |
    ConvertFrom-Json

$recordingPath = Join-Path $resolvedEvidenceRoot 'recording.wav'
$recordingReportPath = Join-Path $resolvedEvidenceRoot 'recording.json'
& $cli recording-probe --output $recordingPath |
    Out-File -LiteralPath $recordingReportPath -Encoding utf8
if ($LASTEXITCODE -ne 0) {
    throw "The buffered WAV archive probe failed with exit code $LASTEXITCODE."
}

$avaloniaStartupPath = Join-Path $resolvedEvidenceRoot 'avalonia-startup.json'
& $app --startup-smoke $avaloniaStartupPath
if ($LASTEXITCODE -ne 0) {
    throw "The Avalonia archive startup smoke failed with exit code $LASTEXITCODE."
}

$tuiRoot = Join-Path $resolvedEvidenceRoot 'tui'
New-Item -ItemType Directory -Force -Path $tuiRoot | Out-Null
foreach ($view in @('operator', 'settings', 'results', 'diagnostics', 'help')) {
    $snapshotPath = Join-Path $tuiRoot "$view.txt"
    & $tui --snapshot --no-audio --no-color --snapshot-view $view |
        Out-File -LiteralPath $snapshotPath -Encoding utf8
    if ($LASTEXITCODE -ne 0) {
        throw "The TUI '$view' archive smoke failed with exit code $LASTEXITCODE."
    }
}

$dataRoot = Join-Path $resolvedEvidenceRoot 'data'
$hostOutputPath = Join-Path $resolvedEvidenceRoot 'engine-host.stdout.txt'
$hostErrorPath = Join-Path $resolvedEvidenceRoot 'engine-host.stderr.txt'
$startArguments = @(
    '--data-root',
    $dataRoot,
    '--shutdown-after-seconds',
    '20'
)
$startParameters = @{
    FilePath = $hostExecutable
    ArgumentList = $startArguments
    PassThru = $true
    RedirectStandardOutput = $hostOutputPath
    RedirectStandardError = $hostErrorPath
}
if ($IsWindows) {
    $startParameters.WindowStyle = 'Hidden'
}

$hostProcess = Start-Process @startParameters
$discoveryPath = Join-Path $dataRoot 'runtime\engine-host.json'
$deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
while (-not (Test-Path -LiteralPath $discoveryPath)) {
    if ($hostProcess.HasExited) {
        throw "The archive engine host exited before publishing discovery."
    }

    if ([DateTimeOffset]::UtcNow -ge $deadline) {
        Stop-Process -Id $hostProcess.Id -Force
        throw "The archive engine host did not publish discovery within 15 seconds."
    }

    Start-Sleep -Milliseconds 100
}

$previousDataRoot = $env:MORSE_RUNNER_DATA_ROOT
$env:MORSE_RUNNER_DATA_ROOT = $dataRoot
try {
    $hostInfoPath = Join-Path $resolvedEvidenceRoot 'host-info.json'
    & $cli host-info | Out-File -LiteralPath $hostInfoPath -Encoding utf8
    if ($LASTEXITCODE -ne 0) {
        throw "The archive host-info command failed with exit code $LASTEXITCODE."
    }

    $hostedScenarioPath = Join-Path $resolvedEvidenceRoot 'hosted-scenario.json'
    & $cli hosted-scenario |
        Out-File -LiteralPath $hostedScenarioPath -Encoding utf8
    if ($LASTEXITCODE -ne 0) {
        throw "The archive hosted scenario failed with exit code $LASTEXITCODE."
    }

    $hostedScenario = Get-Content -LiteralPath $hostedScenarioPath -Raw |
        ConvertFrom-Json
    foreach ($snapshot in @($inProcessScenario, $hostedScenario)) {
        $snapshot.PSObject.Properties.Remove('EngineEpoch')
        $snapshot.PSObject.Properties.Remove('SessionId')
    }

    $inProcessComparable = $inProcessScenario |
        ConvertTo-Json -Depth 10 -Compress
    $hostedComparable = $hostedScenario |
        ConvertTo-Json -Depth 10 -Compress
    if ($inProcessComparable -cne $hostedComparable) {
        throw "In-process and authenticated hosted scenario outcomes differ."
    }
}
finally {
    $env:MORSE_RUNNER_DATA_ROOT = $previousDataRoot
}

$hostProcess.WaitForExit(30000) | Out-Null
if (-not $hostProcess.HasExited) {
    Stop-Process -Id $hostProcess.Id -Force
    throw "The archive engine host did not stop at its evidence deadline."
}

if (Test-Path -LiteralPath $discoveryPath) {
    throw "The archive engine host did not remove its discovery record."
}

$audioDevicesPath = Join-Path $resolvedEvidenceRoot 'audio-devices.json'
$physicalAudioStatus = 'not-attempted'
$physicalAudioReason = 'Run on labeled hardware with -AttemptPhysicalAudio.'
$physicalAudioReport = $null
$nonPhysicalDeviceNames = @(
    'Apple Virtual Sound Device',
    'Discard all samples (playback) or generate zero samples (capture)',
    'Null Audio Device'
)
try {
    & $cli audio-devices |
        Out-File -LiteralPath $audioDevicesPath -Encoding utf8
    if ($LASTEXITCODE -ne 0) {
        throw "audio-devices exited with code $LASTEXITCODE."
    }

    $devices = @(Get-Content -LiteralPath $audioDevicesPath -Raw |
        ConvertFrom-Json)
    $physicalDevices = @(
        $devices |
            Where-Object Name -NotIn $nonPhysicalDeviceNames
    )
    if ($AttemptPhysicalAudio) {
        if ($physicalDevices.Count -eq 0) {
            $physicalAudioStatus = 'hardware-unavailable'
            $physicalAudioReason = if ($devices.Count -eq 0) {
                'The runner enumerated no playback devices.'
            } else {
                'The runner enumerated only null or virtual playback devices.'
            }
        } else {
            $initial = if ([string]::IsNullOrWhiteSpace($InitialAudioDevice)) {
                (
                    $physicalDevices |
                        Where-Object IsDefault |
                        Select-Object -First 1
                ).Name
            } else {
                $InitialAudioDevice
            }
            if ([string]::IsNullOrWhiteSpace($initial)) {
                $initial = $physicalDevices[0].Name
            }

            $recovery = if ([string]::IsNullOrWhiteSpace($RecoveryAudioDevice)) {
                ($physicalDevices |
                    Where-Object Name -ne $initial |
                    Select-Object -First 1).Name
            } else {
                $RecoveryAudioDevice
            }
            if ([string]::IsNullOrWhiteSpace($recovery)) {
                $recovery = $initial
            }

            $physicalRecordingPath = Join-Path (
                $resolvedEvidenceRoot
            ) 'physical-recording.wav'
            $physicalReportPath = Join-Path (
                $resolvedEvidenceRoot
            ) 'physical-audio.json'
            & $cli audio-probe `
                --seconds $PhysicalAudioSeconds `
                --device $initial `
                --recover-device $recovery `
                --record $physicalRecordingPath |
                Out-File -LiteralPath $physicalReportPath -Encoding utf8
            $physicalProbeExitCode = $LASTEXITCODE
            $physicalAudioReport = Get-Content `
                -LiteralPath $physicalReportPath `
                -Raw |
                ConvertFrom-Json
            $physicalAudioStatus = if (
                $physicalProbeExitCode -eq 0 -and
                $physicalAudioReport.Passed
            ) {
                'passed'
            } else {
                'failed'
            }
            $physicalAudioReason = if ($physicalAudioStatus -eq 'failed') {
                "audio-probe exited with code $physicalProbeExitCode."
            } elseif (
                $physicalAudioReport.DeviceChanged
            ) {
                'Sustained playback, recovery, device change, and recording passed.'
            } else {
                'Playback and recovery passed, but only one device was available.'
            }
        }
    }
}
catch {
    $physicalAudioStatus = 'failed'
    $physicalAudioReason = $_.Exception.Message
}

$recordingReport = Get-Content -LiteralPath $recordingReportPath -Raw |
    ConvertFrom-Json
$avaloniaStartup = Get-Content -LiteralPath $avaloniaStartupPath -Raw |
    ConvertFrom-Json
$archiveHash = (Get-FileHash -LiteralPath $resolvedArchive -Algorithm SHA256).Hash
$evidence = [ordered]@{
    schemaVersion = 1
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    runtimeIdentifier = $RuntimeIdentifier
    platform = $actualPlatform
    architecture = $actualArchitecture.ToString()
    archive = [ordered]@{
        path = $resolvedArchive
        sha256 = $archiveHash
        length = (Get-Item -LiteralPath $resolvedArchive).Length
    }
    checks = [ordered]@{
        avaloniaStartup = $avaloniaStartup
        tuiViews = @('operator', 'settings', 'results', 'diagnostics', 'help')
        bufferedWav = $recordingReport
        inProcessScenario = 'passed'
        authenticatedHostedScenario = 'passed'
        gracefulHostDiscoveryCleanup = 'passed'
        physicalAudio = [ordered]@{
            status = $physicalAudioStatus
            reason = $physicalAudioReason
            report = $physicalAudioReport
        }
    }
    platformComplete = (
        $avaloniaStartup.Started -and
        $recordingReport.Valid -and
        $physicalAudioStatus -eq 'passed' -and
        $physicalAudioReport.DeviceChanged
    )
}
$manifestPath = Join-Path $resolvedEvidenceRoot 'evidence-manifest.json'
$evidence |
    ConvertTo-Json -Depth 10 |
    Set-Content -LiteralPath $manifestPath -Encoding utf8
Remove-Item -LiteralPath $installRoot -Recurse -Force

if ($physicalAudioStatus -eq 'failed') {
    throw "Physical release evidence failed: $physicalAudioReason"
}

if ($RequirePhysicalAudio -and -not $evidence.platformComplete) {
    throw "Physical release evidence is incomplete: $physicalAudioReason"
}

Write-Host "Release evidence written to '$manifestPath'."
Write-Output $manifestPath
$global:LASTEXITCODE = 0
