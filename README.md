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
adapters, Avalonia operator workflow, TUI, CLI, and optional gRPC host exist.
They are not yet certified as a complete CE port. The parity audit found
release-blocking gaps in contest and exchange behavior, station simulation,
audio effects and pipeline ordering, operator workflows, persistence, results,
and cross-platform native verification.

The current pinned-reference audit classifies all 143 tracked CE files. It
inventories 131 runtime, build, resource, data, integration, and test inputs
and records 12 narrow nonfunctional exclusions with individual rationales.
Those inputs produce 3,668 stable legacy surfaces mapped to 24 broad
capabilities. The broad capabilities were reset to `not-authored` or `partial`
until narrow executable cases cover every mapped surface on every required
platform.

The earlier 1,501-surface and 20-case report is invalidated history. Its 25
schema-v1 observation records are retained for provenance only and cannot
certify a capability, an acceptance case, or a release. The first schema-v3
case, `contest.exchange-shapes`, is currently
`legacy-green-xplat-red`. It remains a recorded functional gap until the
unchanged case passes XPlat with retained red and green evidence. Do not infer a
green release status from the amount of implemented code.

Current progress and known gaps are tracked in the
[generated parity report](tests/parity/PARITY_REPORT.md) and the
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

The live parity runner does not execute an arbitrary adjacent binary. It
prepares the exact pinned Git revision in a clean artifact worktree, imports
the committed reference bundle, verifies the complete tree, builds the Pascal
oracle version required by each case with the pinned Lazarus and Free Pascal
toolchain, and validates its content-addressed build registry and provenance
before running either target. The toolchain attestation covers 14,553 files
across every consumed compiler and Lazarus unit root. The build record captures
the exact compiler options, search paths, output paths, source, and executable.

Legacy and XPlat execute in separate test processes. Each result records the
clean XPlat revision and tree, platform and runtime identity, and, for a live
Legacy run, the clean CE revision and tree plus fresh oracle and provenance
hashes. Fixtures, case definitions, observed values, run results, and
provenance are content-addressed so stale or substituted evidence fails closed.
The exact xUnit identity, TRX, process exit code, selected case IDs, and result
hash are bound by a per-target execution envelope. A `not-runnable` result is
infrastructure failure, not a functional divergence.

Release validation requires a live `Both` run on Windows and explicit native
XPlat evidence on Windows, Linux, and macOS. Offline fixtures and retained
schema-v1 observations never satisfy the live Legacy release gate.

## Initial .NET validation

```powershell
dotnet restore MorseRunnerXPlat.slnx
dotnet format MorseRunnerXPlat.slnx --verify-no-changes
dotnet build MorseRunnerXPlat.slnx --no-restore --configuration Release
dotnet test --solution MorseRunnerXPlat.slnx --no-build --configuration Release -- --filter-not-trait Category=ParityAcceptance --filter-not-trait Category=LegacyOracleBuildIntegration
.\tests\parity\Test-ParityCompleteness.ps1 -LegacyRoot ..\MorseRunner
.\tests\parity\Run-Parity.ps1 -Target Both -Mode Development -LegacyRoot ..\MorseRunner
```

Certifying `ParityAcceptance` tests and the mandatory fresh-build integration
test run only through `Run-Parity.ps1`. A recorded XPlat red is a deliberately
failing product test, so invoking those tests through the ordinary solution
test command would make an honest development baseline indistinguishable from
an unrelated test failure.

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
