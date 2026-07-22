# MorseRunnerXPlat

MorseRunnerXPlat is a cross-platform .NET Morse contest simulator and training application. It provides one deterministic simulation engine with Avalonia, terminal, CLI, and optional gRPC clients.

## Run

```powershell
dotnet restore MorseRunnerXPlat.slnx
dotnet build MorseRunnerXPlat.slnx --no-restore --configuration Release
dotnet run --project src\MorseRunner.App --configuration Release
```

The keyboard-first terminal client is available with `dotnet run --project src\MorseRunner.Tui --configuration Release`.

## Quality checks

```powershell
uv sync --locked
uv run --locked python tools\agent_scaffolding\validate_yaml.py
.\.github\hooks\scripts\validate-agent-scaffolding.ps1
dotnet format MorseRunnerXPlat.slnx --verify-no-changes
dotnet test --solution MorseRunnerXPlat.slnx --no-build --configuration Release
```

See the [engineering specification](docs/architecture/engineering-specification.md), [release checklist](docs/release-checklist.md), and [human evaluation guide](docs/release/human-evaluation.md).
