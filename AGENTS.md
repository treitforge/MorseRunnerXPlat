# AGENTS.md

## Project context

MorseRunnerXPlat is a cross-platform .NET Morse code contest simulator and training application. Preserve a fast, deterministic simulation, one engine shared by all clients, and a keyboard-first operator experience on Windows, Linux, and macOS.

`docs/architecture/engineering-specification.md` is the authoritative technical specification. Update it with changes to architecture, engine behavior, commands, events, snapshots, transport contracts, timing, platform support, or acceptance criteria.

## Development

- Use PowerShell and `rg` on this Windows-oriented repository.
- Runtime code must support Windows, Linux, and macOS. Do not add platform-specific APIs to portable projects.
- Use `apply_patch` for source edits. Preserve unrelated work in a dirty worktree. Never use destructive reset or checkout commands.
- Python is tooling only and is managed with uv. Use `uv sync --locked` and `uv run --locked`.
- Do not use emojis or em dashes in code or documentation. Use nullable reference types and address warnings.

## Architecture

1. There is one production engine implementation in .NET.
2. UX projects do not own contest rules, scoring, station behavior, simulation time, or DSP policy.
3. Domain, DSP, and Engine do not reference Avalonia, gRPC, Protobuf-generated types, or concrete audio backends.
4. Every UX depends on `IMorseRunnerClient`.
5. Avalonia uses the in-process client by default. gRPC maps to the same service and is not a second engine.
6. The engine-host process owns audio rendering, playback, and recording. Never stream PCM through gRPC.
7. One session loop owns mutable simulation state. Commands apply at documented block boundaries.
8. Domain updates are immutable events and coalescible snapshots. Slow observers never delay audio or simulation.

## Real-time and UX

- Keep render callbacks free from blocking waits, file I/O, logging, UI dispatch, RPC, allocations, LINQ, boxing, reflection, and exceptions.
- Use bounded queues for commands, rendered blocks, and subscribers.
- Keep engine state mutations on the session loop and use one seeded random source per session.
- Preserve keyboard-first operation. Keep focus, shortcuts, and editing in the UX, and send semantic intent to the engine.
- Avalonia uses compiled bindings with `x:DataType`; view models marshal observable updates to the UI thread.

## Testing and validation

Add focused deterministic tests with every behavior change. Test the engine directly unless transport is the subject. Use numeric vectors and benchmarks for DSP changes, shared client vectors for gRPC changes, and headless or visual checks for UX changes.

Run the applicable commands before handoff:

```powershell
uv sync --locked
uv run --locked python tools\agent_scaffolding\validate_yaml.py
.\.github\hooks\scripts\validate-agent-scaffolding.ps1
dotnet restore MorseRunnerXPlat.slnx
dotnet format MorseRunnerXPlat.slnx --verify-no-changes
dotnet build MorseRunnerXPlat.slnx --no-restore --configuration Release -warnaserror
dotnet test --solution MorseRunnerXPlat.slnx --no-build --configuration Release -- --filter-not-trait Category=Performance --fail-warns on
```

When Protobuf changes, also run `buf lint` and `buf breaking --against '.git#branch=main'`.

## Security

Never print or commit secrets. Bind the optional host to loopback by default. Remote access requires explicit TLS and authentication design. Treat resets, destructive result changes, and forced lease takeover as explicit operations.
