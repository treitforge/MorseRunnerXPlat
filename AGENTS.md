# AGENTS.md

## Project context

MorseRunnerXPlat is a cross-platform .NET reimplementation of MorseRunner, a
real-time Morse code contest simulator and training application.

Primary goals:

- Preserve the behavior and feel of the legacy MorseRunner simulation.
- Reach full 1:1 functional parity with the adjacent legacy MorseRunner, with
  zero missing features or functional gaps.
- Run on Windows, Linux, and macOS from one .NET codebase.
- Keep the simulation, contest rules, scoring, and DSP independent of any UX.
- Support Avalonia, CLI, TUI, automation, and future UX clients through one
  application-facing service layer.
- Keep the normal desktop path in process while allowing an optional gRPC host.

## Canonical specification

`docs/architecture/engineering-specification.md` is the authoritative technical
specification.

When a change alters architecture, engine behavior, commands, events,
snapshots, transport contracts, timing, deterministic behavior, platform
support, or acceptance criteria, update the specification in the same change.

## Style rules

- Never use emojis in code, documentation, comments, or generated copy unless
  the user explicitly requests them.
- Never use em dashes. Use hyphens, commas, parentheses, or separate sentences.
- Keep comments focused on invariants and reasons, not line-by-line narration.
- Use nullable reference types and address warnings rather than suppressing them
  broadly.
- Prefer explicit names over abbreviations except for established domain terms
  such as CW, DSP, QSO, RIT, AGC, QRN, QRM, QSB, PCM, and WPM.

## Development environment

- The repository is commonly developed from Windows.
- Use PowerShell (`pwsh`) for repository scripts and Windows examples.
- Use `rg` for text and file search.
- Runtime code must support Windows, Linux, and macOS.
- Do not introduce platform-specific APIs into portable projects.
- Python is allowed for repository tooling only. It is not an application
  runtime dependency.
- Manage all repository Python versions and dependencies with uv. Do not use
  `pip install`, Poetry, Pipenv, or an unmanaged virtual environment.
- Do not assume Node, Bash, a browser, or a native compiler unless the task
  explicitly requires it.

## Expected commands

The repository is currently in specification and scaffolding phase. Validate
the agent scaffold with:

```powershell
uv sync --locked
uv run --locked python tools\agent_scaffolding\validate_yaml.py
.\.github\hooks\scripts\validate-agent-scaffolding.ps1
```

Once the .NET solution exists, use:

```powershell
dotnet restore MorseRunnerXPlat.slnx
dotnet format MorseRunnerXPlat.slnx --verify-no-changes
dotnet build MorseRunnerXPlat.slnx --no-restore
dotnet test --solution MorseRunnerXPlat.slnx --no-build -- --filter-not-trait Category=ParityAcceptance --filter-not-trait Category=LegacyOracleBuildIntegration
.\tests\parity\Run-Parity.ps1 -Target Both -Mode Development
```

Once the Phase 0 parity harness exists, use:

```powershell
.\tests\parity\Test-ParityCompleteness.ps1
.\tests\parity\Run-Parity.ps1 -Target Both -Mode Development
```

Certifying `ParityAcceptance` tests and the mandatory fresh-oracle build
integration test run only through `Run-Parity.ps1`. Outstanding XPlat gaps
deliberately fail those product tests while the parity runner validates and
retains the exact functional-red result.

When `proto\` and `buf.yaml` exist, also use:

```powershell
buf lint
buf breaking --against '.git#branch=main'
```

Do not invent passing results for commands that cannot run because a planned
project or tool has not been added yet. Report the missing prerequisite.

## Planned repository structure

- `src\MorseRunner.Domain\`: immutable domain values, commands, events, and
  snapshots.
- `src\MorseRunner.Dsp\`: allocation-conscious managed DSP and audio rendering.
- `src\MorseRunner.Engine\`: authoritative simulation and session runtime,
  including the `IAudioSink` output port.
- `src\MorseRunner.Audio\`: physical, WAV, and null implementations of the
  engine-owned audio sink port.
- `src\MorseRunner.Client\`: transport-neutral client facade and in-process
  implementation.
- `src\MorseRunner.Contracts\`: generated Protobuf and gRPC transport types.
- `src\MorseRunner.Grpc\`: transport mapping, client, and service adapters.
- `src\MorseRunner.EngineHost\`: optional standalone gRPC host.
- `src\MorseRunner.App\`: Avalonia desktop UX.
- `src\MorseRunner.Cli\`: headless scenarios, diagnostics, and export.
- `src\MorseRunner.Tui\`: optional terminal UX.
- `tests\`: unit, scenario, transport, UI, performance, and legacy parity tests.
- `proto\`: external service contracts only.
- `docs\architecture\`: authoritative architecture and engineering documents.
- `.agents\skills\`: reusable project workflows shared by agent surfaces.
- `.codex\`: Codex runtime settings and custom agents.
- `.github\`: GitHub Actions and Copilot-specific adapters.
- `tools\agent_scaffolding\`: uv-managed Python validation tooling.

## Architectural invariants

1. There is one production engine implementation, written in .NET.
2. UX projects never own contest rules, scoring, station behavior, simulation
   time, or DSP policy.
3. `MorseRunner.Domain`, `MorseRunner.Dsp`, and `MorseRunner.Engine` do not
   reference Avalonia, gRPC, Protobuf-generated types, or a concrete audio
   backend.
4. Every UX depends on `IMorseRunnerClient`, not directly on engine internals.
5. Avalonia uses the in-process client by default.
6. The optional gRPC host exposes the same engine application service. It is an
   adapter, not a second engine.
7. The process hosting the engine owns audio rendering, playback, and recording.
   Never stream real-time PCM through gRPC.
8. One session loop owns all mutable simulation state.
9. Commands are ordered and applied at documented simulation block boundaries.
10. Engine updates are immutable events and coalescible snapshots.
11. Multiple clients may observe a hosted session, but only one client may hold
    the control lease.
12. Domain types remain independent from transport types. Map explicitly at the
    gRPC edge.
13. `MorseRunner.Engine` owns `IAudioSink` and does not reference
    `MorseRunner.Audio`. Audio adapters reference Engine and implement the port.

## Real-time and concurrency rules

- Keep the render callback free from RPC, file I/O, logging sinks, UI dispatch,
  blocking waits, and asynchronous work.
- Avoid per-sample allocation, LINQ, boxing, reflection, and exceptions in DSP
  hot paths.
- Use bounded queues for commands, rendered blocks, and external update
  subscribers.
- A slow observer must never delay the simulation or audio queue.
- Use one explicit seeded random source owned by the session.
- Use simulation time for domain behavior and wall-clock time only for
  diagnostics, leases, and external timestamps.
- Do not mutate engine state from gRPC handlers or UI threads.

## UX rules

- Preserve keyboard-first operation for high-frequency actions.
- Keep focus movement, shortcut handling, and text editing in the UX.
- Send semantic operator intent to the engine rather than widget state.
- Keep Avalonia view models free of contest and scoring decisions.
- Enable compiled bindings and declare `x:DataType`.
- Marshal observable UI changes onto the Avalonia UI thread.
- Visually verify meaningful layout or interaction changes when capture tooling
  is available.

## Contract rules

- Protobuf is the source of truth for the optional external transport only.
- Never expose generated Protobuf types from `IMorseRunnerClient`.
- Prefer additive schema evolution.
- Never reuse removed field numbers or enum values.
- Reserve removed field numbers and names.
- Give every mutating command a request ID and session ID.
- Include engine epoch, session ID, sequence, revision, and simulation block
  number in streamed updates where applicable.
- Keep the first service surface cohesive and small. Do not copy QSORipper's
  file count or dual-engine conventions.
- Update the engineering specification with contract behavior changes.

## Legacy parity

The legacy repository is expected at `..\MorseRunner` during parity work.

- Treat the Pascal implementation as a behavioral oracle, not as the desired
  .NET architecture.
- Full functional parity is a release-blocking requirement.
- Inventory every observable legacy feature in the parity manifest.
- Write acceptance tests before implementing the corresponding XPlat feature.
- Every new parity test must first pass against legacy MorseRunner.
- Before its feature is implemented, the same test must demonstrably fail
  against XPlat for the expected missing behavior.
- The red case is executed and recorded as a functional gap. It is not skipped
  or marked expected-failure in the test framework.
- Drive the XPlat pass rate to 100 percent. A parity release permits no skipped,
  quarantined, waived, expected-failure, or unimplemented manifest entries.
- Capture golden results before changing semantics.
- Record seeds, commands, block numbers, events, score, QSO log, and audio
  hashes for reproducible scenarios.
- Do not copy UI coupling, process-global state, or Windows audio APIs.
- Do not use an "intentional difference" record to waive missing behavior.
- Visual design may differ, but every legacy workflow and functional outcome
  must remain available and acceptance-tested.

## Testing and quality

- Add focused tests with every behavior change or new error path.
- For ported legacy behavior, add the cross-implementation acceptance test
  before production code.
- Prefer deterministic tests with explicit seeds and fake clocks.
- Test engine behavior directly without starting gRPC unless transport is the
  subject of the test.
- Run identical scenario vectors through in-process and gRPC clients for
  transport parity.
- Do not use sleeps to synchronize tests.
- Performance changes require measurements representative of the affected hot
  path.
- Cross-platform-sensitive changes must be exercised on Windows, Linux, and
  macOS CI where practical.

## Security and secrets

- Never commit, print, or log secrets, tokens, private keys, credentials,
  connection strings, or local `.env` values.
- Bind the optional engine host to loopback by default.
- Remote access is a separate feature requiring TLS and authentication.
- Use a per-launch local token when an external host can be reached by unrelated
  local processes.
- Treat session reset, result deletion, overwrite, and forced lease takeover as
  explicit destructive operations.

## Agent setup

- This file is the canonical shared guidance.
- Codex runtime settings and custom agents belong in `.codex\`.
- Reusable workflows belong in `.agents\skills\`.
- Copilot-specific adapters belong in `.github\`.
- Do not duplicate durable repository rules in provider-specific files.
- Keep personal credentials, MCP configuration, and private defaults outside
  the repository.

## Python tooling

- `.python-version` pins the repository tooling interpreter.
- `pyproject.toml` declares tooling dependencies.
- `uv.lock` is committed and must remain current.
- `.venv\` is local and ignored.
- Use `uv sync --locked` to create or verify the environment.
- Use `uv run --locked <command>` to run Python tooling.
- Add or remove dependencies with `uv add` or `uv remove`, then commit the
  resulting `pyproject.toml` and `uv.lock` changes.
- Keep Python under `tools\`, tests, or build automation. Do not add it to
  `src\` or any shipped runtime path.
