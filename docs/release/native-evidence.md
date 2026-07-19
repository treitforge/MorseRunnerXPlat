# Native release evidence

## Purpose

Release evidence is collected from the self-contained archive on its target
operating system and architecture. A successful cross-publish is not evidence
that the archive launches, renders, records, or communicates correctly.
Windows is packaged as ZIP. Linux and macOS use `tar.gz` so their executable
permission bits survive installation.

The `Native Release Evidence` workflow runs this matrix:

| Runtime | Native runner |
|---|---|
| `win-x64` | `windows-latest` |
| `linux-x64` | `ubuntu-latest` |
| `osx-x64` | `macos-15-intel` |
| `osx-arm64` | `macos-15` |

GitHub documents `macos-15-intel` as an Intel runner and `macos-15` as an
arm64 runner. The workflow uses explicit labels so an alias change cannot
silently change the tested architecture.

## Reproducible commands

Build one archive on the same target that will run it:

```powershell
.\tools\release\Publish-Release.ps1 `
  -RuntimeIdentifiers @('win-x64')
```

Install the archive into a temporary evidence directory and execute every
device-independent check:

```powershell
.\tools\release\Test-ReleaseArchive.ps1 `
  -RuntimeIdentifier win-x64 `
  -ArchivePath artifacts\release\MorseRunnerXPlat-win-x64.zip
```

On a machine with two playback endpoints, require the physical audio gate:

```powershell
.\tools\release\Test-ReleaseArchive.ps1 `
  -RuntimeIdentifier win-x64 `
  -ArchivePath artifacts\release\MorseRunnerXPlat-win-x64.zip `
  -AttemptPhysicalAudio `
  -RequirePhysicalAudio `
  -PhysicalAudioSeconds 30 `
  -InitialAudioDevice '<initial device name>' `
  -RecoveryAudioDevice '<recovery device name>'
```

Omit the device names to select the default endpoint and the first distinct
endpoint. A one-device machine can prove playback and same-device recovery,
but it cannot satisfy the device-change gate.

## Captured artifacts

Each native job uploads an `evidence-manifest.json` plus:

- The tested archive SHA-256 and length.
- Avalonia native-platform initialization metadata.
- Rendered Avalonia main-window and score-window PNGs from the interaction
  suite.
- Operator, settings, results, diagnostics, and help TUI frames.
- Deterministic in-process and authenticated hosted snapshots. The evidence
  script removes engine and session identities, then requires the remaining
  snapshots to match exactly.
- Host startup output and proof that graceful shutdown removed discovery.
- A buffered PCM16 mono WAV, structural metadata, and SHA-256.
- Audio-device enumeration.
- When requested, sustained physical playback, pause-boundary recovery,
  device-change status, queue depth, underruns, drops, health, and a
  simultaneously buffered WAV.

The manifest sets `platformComplete` only when the archive, UX, WAV, transport,
physical playback, recovery, and distinct-device change checks all pass.
Missing hardware is recorded as `hardware-unavailable`; it is never converted
to a passing result.

## Current evidence

GitHub Actions run
[`29676157899`](https://github.com/treitforge/MorseRunnerXPlat/actions/runs/29676157899)
captured the target-native archive results below. Its `native-evidence-*`
artifacts retain the manifests, UX captures, WAV reports, scenarios, device
enumeration, and host diagnostics for 30 days.

| Runtime | Archive, UX, WAV, and hosted checks | Physical audio | Complete |
|---|---|---|---|
| `win-x64` | Passed on native x64 CI and locally from the extracted ZIP | Hosted runner reported no playback devices. Local hardware passed a 30-second run with 14 enumerated endpoints, a distinct endpoint change, healthy output, zero underruns, zero drops, and buffered WAV capture | Yes |
| `linux-x64` | Passed from the extracted native `tar.gz` | Hosted runner enumerated only ALSA's null/discard endpoint and recorded `hardware-unavailable` | No |
| `osx-x64` | Passed from the extracted native `tar.gz` | Hosted runner enumerated only the null endpoint and recorded `hardware-unavailable` | No |
| `osx-arm64` | Passed from the extracted native `tar.gz` | Hosted runner enumerated only Apple/Null virtual endpoints and recorded `hardware-unavailable` | No |

The Windows evidence was captured on 2026-07-18 Pacific time with .NET 10.0.8
on Windows x64. Release artifacts from the final candidate must be retained
with the release rather than relying on this development capture.
