# MorseRunnerXPlat

Cross-platform .NET reimplementation of MorseRunner.

The project is designed around one deterministic simulation engine with
multiple possible UX clients. Avalonia uses the engine in process by default;
an optional gRPC host supports external TUI, CLI, automation, and observer
clients without moving audio out of the engine process.

Full 1:1 functional parity with the adjacent legacy MorseRunner is a mandatory
release gate. The project uses a test-first dual-runner acceptance suite: every
case must pass legacy MorseRunner, initially fail the missing XPlat behavior,
and then converge to a 100 percent XPlat pass rate with zero functional gaps.

## Project status

The initial cross-platform architecture, deterministic session service, audio
adapters, Avalonia operator workflow, TUI, CLI, and optional gRPC host are
implemented. The pinned legacy inventory contains 1,501 structurally mapped
surfaces and the fixture-level acceptance capabilities are green.

That structural result is not yet proof of full behavioral parity. The
end-to-end UX audit found remaining work in contest-specific scoring and
exchange behavior, realistic station simulation, several advanced settings,
score submission, and cross-platform visual verification. These are
release-blocking gaps, tracked in the
[legacy compatibility matrix](docs/ux/legacy-compatibility-matrix.md).

## Engineering guidance

- [Engineering specification](docs/architecture/engineering-specification.md)
- [Legacy UX compatibility matrix](docs/ux/legacy-compatibility-matrix.md)
- [Generated parity report](tests/parity/PARITY_REPORT.md)
- [Agent instructions](AGENTS.md)

## Repository tooling

Python-based repository tooling is managed exclusively with uv:

```powershell
uv sync --locked
uv run --locked python tools\agent_scaffolding\validate_yaml.py
```

The `.venv` directory is local and ignored. Python is not an application
runtime dependency.

During compatibility work, the adjacent `..\MorseRunner` repository is the
functional reference implementation.

## Initial .NET validation

```powershell
dotnet restore MorseRunnerXPlat.slnx
dotnet format MorseRunnerXPlat.slnx --verify-no-changes
dotnet build MorseRunnerXPlat.slnx --no-restore --configuration Release
dotnet test --solution MorseRunnerXPlat.slnx --no-build --configuration Release
.\tests\parity\Test-ParityCompleteness.ps1 -LegacyRoot ..\MorseRunner
.\tests\parity\Run-Parity.ps1 -Target Both -Mode Development -LegacyRoot ..\MorseRunner
```

## Run

Launch the desktop application:

```powershell
dotnet run --project src\MorseRunner.App
```

Launch the local terminal application:

```powershell
dotnet run --project src\MorseRunner.Tui
dotnet run --project src\MorseRunner.Tui -- --no-audio
dotnet run --project src\MorseRunner.Tui -- --snapshot
```

Launch the optional local engine host and inspect it from another terminal:

```powershell
dotnet run --project src\MorseRunner.EngineHost
dotnet run --project src\MorseRunner.Cli -- host-info
dotnet run --project src\MorseRunner.Cli -- hosted-scenario
dotnet run --project src\MorseRunner.Tui -- --hosted
```

Set `MORSE_RUNNER_DATA_ROOT` to isolate settings, results, discovery, and
diagnostics. The engine host uses null audio by default for automation. Pass
`--physical-audio true` to select the physical output adapter.

Publish all supported runtime packages:

```powershell
.\tools\release\Publish-Release.ps1
```

Regenerate the committed legacy inventory and parity report after changing an
audited extractor or manifest mapping:

```powershell
uv run --locked python tools\parity\inventory_legacy.py --legacy-root ..\MorseRunner
uv run --locked python tools\parity\validate_parity.py --mode completeness --legacy-root ..\MorseRunner --write-report
```
