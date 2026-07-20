# MorseRunnerXPlat engineering specification

Status: Draft for implementation

Version: 0.2
Date: 2026-07-19

## 1. Purpose and authority

This document is the authoritative engineering specification for
MorseRunnerXPlat.

It defines:

- Product and compatibility scope.
- System boundaries and dependency direction.
- Engine, session, audio, client, and optional transport behavior.
- Cross-platform and real-time constraints.
- Required test evidence and performance gates.
- Delivery sequence and completion criteria.

Changes to observable engine behavior, architecture, commands, events,
snapshots, timing, transport contracts, supported platforms, or acceptance
criteria must update this document in the same change.

The terms **must**, **must not**, **should**, and **may** are normative:

- **Must** and **must not** describe required conformance.
- **Should** describes the default expectation. A deviation requires a recorded
  reason and test evidence.
- **May** describes an allowed option.

## 2. Functional reference

The adjacent `..\MorseRunner` repository is the functional reference
implementation.

Developers and agents must inspect it when establishing:

- Contest selection and run-mode behavior.
- Contest exchanges, validation, multipliers, points, scoring, and results.
- Operator and simulated-station state machines.
- Message composition and CW timing.
- Station activity, mistakes, confidence, and reply timing.
- QRN, QRM, QSB, flutter, RIT, QSK, filtering, modulation, and AGC.
- Logging, corrections, rate, score, and result presentation.
- Settings, data files, import behavior, and persisted output.
- High-frequency keyboard workflows.

Important reference files include:

| Concern | Legacy source |
|---|---|
| Main interaction and lifecycle | `..\MorseRunner\Main.pas` |
| Simulation and audio-block loop | `..\MorseRunner\Contest.pas` |
| Station waveform and state | `..\MorseRunner\Station.pas` |
| Simulated operator behavior | `..\MorseRunner\DxOper.pas` |
| Simulated station behavior | `..\MorseRunner\DxStn.pas` |
| Station collection | `..\MorseRunner\StnColl.pas` |
| Logging and scoring | `..\MorseRunner\Log.pas` |
| Settings and contest catalog | `..\MorseRunner\Ini.pas` |
| Contest implementations | `..\MorseRunner\*.pas` contest units |
| DSP and Windows audio | `..\MorseRunner\VCL\*.pas` |
| Existing tests | `..\MorseRunner\Test\` |

The legacy source is not the desired architecture. The .NET implementation
must not copy:

- Process-global engine, configuration, log, or form state.
- Engine calls into form controls.
- UI-thread simulation advancement.
- WinMM or other Windows-only audio assumptions.
- Test-only copies of production algorithms.
- Implicit reliance on current working directory or case-insensitive paths.

Every golden compatibility fixture must record:

- Legacy Git revision.
- Legacy Git tree.
- Legacy source location or scenario description.
- Pinned reference-definition hash.
- Oracle source, executable, and provenance hashes.
- Compiler identity, executable hashes, and the complete consumed-toolchain
  fingerprint.
- Exact compiler invocation, including ordered options, unit, tool, and library
  search paths, output paths, source, and executable.
- Settings and input sequence.
- Seed or captured random decisions.
- Expected state, events, score, QSO records, or audio data.
- Any normalization applied during comparison.
- A content hash for the canonical observed-value sequence.

Golden fixtures are offline caches. They must identify themselves as fixture
observations and must never satisfy a requirement to execute the live legacy
target.

### 2.1 Full-parity requirement

MorseRunnerXPlat must reach full 1:1 functional parity with the pinned legacy
MorseRunner reference before any production release.

Full parity means:

- Every observable legacy feature exists in XPlat.
- Every legacy workflow has an XPlat equivalent.
- Every contest, run mode, setting, command, condition, result, file behavior,
  and failure behavior is represented in the parity manifest.
- The shared acceptance suite passes 100 percent against legacy MorseRunner.
- The same suite passes 100 percent against XPlat.
- The functional gap count is zero.
- No required case is skipped, waived, quarantined, disabled, marked
  expected-failure, or marked unimplemented.

Visual styling and layout may be modernized, but visual change must not remove
information, commands, keyboard workflows, validation, or functional outcomes.

An accepted-difference document must not be used to waive missing functionality.
Enhancements may be added after or alongside parity, but the legacy capability
must remain available and tested.

### 2.2 Exhaustive feature inventory

Before broad production implementation, the team must build a complete,
machine-readable parity manifest at:

```text
tests/parity/parity-manifest.json
```

The inventory must be derived from code and runtime behavior, not from memory or
the user manual alone. At minimum, inspect:

- Every main-form control and event handler in `Main.pas`, `Main.lfm`, and
  `Main.dfm`.
- Every menu item, shortcut, function-key action, dialog, and result view.
- Every `TSimContest` value and contest unit.
- Every run mode and state transition.
- Every persisted setting read or written by `Ini.pas`.
- Every logging, scoring, correction, rate, and result path in `Log.pas`.
- Every station, operator, and collection state path.
- Every DSP stage and effect under `VCL\`.
- Every bundled data file and parser.
- Every recording, export, high-score, and failure path.
- Existing unit tests and known regression cases.

Manifest schema version 3 separates inventory grouping from executable proof:

- A capability groups related legacy surfaces. It contains a stable capability
  ID, category, feature, user-visible behavior, legacy source references,
  stable legacy-surface selectors, required platforms, acceptance status, and
  its case IDs.
- A case is one narrow executable behavior vector. It contains a stable case
  ID and owning capability, behavior, legacy source references, platforms,
  preconditions, input, target adapters, assertions and tolerances, live Legacy
  and XPlat statuses, functional status and failure code, fixture and evidence
  paths, and the first green XPlat commit when applicable.

Capability acceptance status is one of:

- `not-authored`: no release-certifying case coverage exists.
- `partial`: one or more cases exist, but the capability does not yet have
  both-green case coverage for every mapped surface on every declared
  platform.
- `complete`: every mapped surface and declared platform assignment is covered
  by retained both-green case evidence.

A broad case pass must never promote its entire owning capability by inference.
Completeness is computed over the Cartesian product of mapped legacy surfaces
and required capability platforms. Case overlap is visible in the report and
does not compensate for an uncovered surface-platform assignment.

A human-readable parity report must be generated from the manifest. The
generated report is not the source of truth.

Completeness tooling may also commit deterministic extracted inventories, such
as `tests/parity/legacy-surface-inventory.json`, as audit inputs. Every
discovered surface has a stable ID and source reference and must map to exactly
one manifest capability. Extracted inventories are regenerated from the pinned
legacy revision and checked for staleness. Extraction, parsing, byte lengths,
and content hashes use exact Git blob bytes from the pinned revision, never
checkout bytes that can vary with line-ending conversion across operating
systems. The manifest remains the source of truth for capability grouping,
acceptance status, fixtures, and evidence.

The current pinned inventory classifies all 143 tracked CE files. It inventories
131 application, data, resource, project, integration, and test inputs and
records 12 narrow exclusions for nonfunctional repository, legal, community, or
developer documentation. It extracts 3,668 stable surfaces, all mapped to 24
broad capabilities. These counts establish inventory coverage only. They do not
establish behavioral parity.

### 2.3 Dual-runner acceptance harness

The parity suite must execute one logical acceptance case against two target
adapters:

```csharp
public interface IParityTarget
{
    Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken);
}
```

The adapters are:

- `LegacyMorseRunnerTarget`, controlling or invoking the pinned legacy
  implementation.
- `XPlatMorseRunnerTarget`, controlling the .NET implementation through the
  in-process semantic client.

The adapter mechanisms may differ, but the scenario and functional assertions
must be shared.

The legacy adapter may combine:

- Existing Pascal unit-test entry points.
- A purpose-built legacy oracle executable.
- Deterministic instrumentation added to a test build.
- UI automation for workflows that cannot be reached below the UI.
- File and WAV output capture.

The XPlat adapter must prefer the in-process client so transport behavior does
not obscure functional parity. Separate tests cover gRPC equivalence.

A live `Both` run must start separate test processes with explicit target
selection. One process selects `Legacy`; the other selects `XPlat`. A test must
not select a target based on which executable or fixture happens to be
available. The live Legacy adapter has no fixture fallback.

The reference revision and executable or build provenance must be pinned. A
release parity run must not depend on an unknown locally installed binary.

The first Phase 0 legacy adapter builds
`tests/parity/legacy-oracle/v1/LegacyOracle.lpr` with Lazarus 4.6 and Free
Pascal 3.2.2 against legacy revision
`55bbd019c29d8cf693184ea420a17a253f16fe1e` and tree
`a44212bfee5b1eebfd0129459d476736775adf36`. The public base revision and the
additional pinned commits are represented by
`tests/parity/legacy-reference.json` and
`tests/parity/legacy-reference.bundle`.

Every active case binds an immutable legacy adapter descriptor containing its
adapter ID, version ID, source path and hash, build-recipe path and hash. A
successful build writes a content-addressed registry entry that binds that
descriptor to the exact executable and provenance hashes. The Legacy runner
must resolve the case through that registry. A process-wide executable path,
an unregistered binary, or a build from a different adapter version cannot
satisfy the case. A descriptor version ending in `-vN` must bind both source
and recipe under the exact `tests/parity/legacy-oracle/vN/` directory. The
unversioned source retained for historical schema-v1 fixtures is never an
active build or runtime candidate.

Descriptor paths are canonical repository-relative `/`-separated strings.
Absolute paths, backslashes, empty segments, `.`, `..`, aliases, and a
version-directory mismatch are rejected identically by every language layer.
Multiple cases may share one version ID only when their complete adapter
descriptors are identical. The build registry then contains one version entry,
and its provenance binds the exact sorted case IDs executed by that build.
A repeated version ID with any descriptor difference is invalid.

`Prepare-LegacyReference.ps1` constructs a detached, clean worktree at the
exact revision and verifies the complete Git tree. `Build-LegacyOracle.ps1`
performs a full rebuild with the pinned compiler, validates every supported
oracle scenario, and writes machine-readable provenance containing the legacy
revision and tree, oracle source and executable hashes, full toolchain
fingerprint, exact build invocation, XPlat build context, and content-addressed
observations.

Scenario input and output use one cross-language canonical JSON contract. The
hash input is the exact UTF-8 byte sequence, without a byte-order mark or
newline. Object keys use ordinal UTF-16 ordering, strings preserve their
original Unicode scalar sequence, required escapes are deterministic, numbers
are signed 64-bit integers, and floating-point values are rejected. Pascal,
.NET, PowerShell, and Python must pass the same adversarial canonicalization
vectors before a result can certify parity.

The toolchain fingerprint covers all 14,553 files in the consumed Free Pascal
3.2.2 tree, Lazarus LCL and supporting unit roots, and `lazbuild.exe`. Its
canonical record includes the ordered relative roots, per-file paths, lengths,
and hashes, plus aggregate file count, byte count, and SHA-256. The toolchain is
verified before and after the oracle build. Verification must reject a
mismatched installation and must not silently repair or replace an unrelated
user installation.

The compiler invocation is recorded structurally, not accepted through a
substring check. Provenance contains the compiler, ordered compiler options,
ordered unit, tool, and library search paths, unit and executable output paths,
output executable, and source. The build script, reference definition, bundle,
oracle source, oracle executable, provenance document, and scenario
observations are SHA-256 addressed.

Every live result includes platform, process architecture, runtime identifier,
framework, and a run context. The XPlat context records revision, tree, and
cleanliness. A Legacy result additionally records the clean CE revision and
tree plus fresh oracle and provenance hashes supplied by the build that
immediately preceded the run. Release validation requires both worktrees to be
clean and every recorded hash to recompute.

The acceptance-test identity is exactly `parity:<case-id>()`. The result file
and TRX must contain that same name once and only once. Every target process
also writes a content-addressed execution envelope that binds the raw result
file hash, raw TRX hash, target, exact selected case IDs, platform, process
architecture, runtime identifier, revision, tree, cleanliness, exact process
exit code, and wrapper-correlation completion. Legacy success and XPlat green
use exit code 0. A retained XPlat functional-divergence run uses test-process
exit code 2 and the case-specific
`PARITY_FUNCTIONAL_DIVERGENCE|<case-id>|<failure-code>` exception. A command
line error, host failure, missing test, generic assertion, unexpected extra
test, skipped test, or incomplete envelope is infrastructure failure.

A live legacy adapter must reject a dirty tree, an unknown executable, a stale
fresh-build hash anchor, missing provenance, a provenance mismatch, a changed
toolchain, an inexact build invocation, or a fixture substituted for an
executable.

UI automation remains available only for workflows that cannot be exercised
through the Pascal oracle or file outputs.

### 2.4 Mandatory red-green porting sequence

Every ported feature follows this order:

1. Select its owning capability and add one narrow parity-manifest case.
2. Write the shared acceptance case.
3. Run it against the pinned legacy target.
4. Confirm the legacy target passes.
5. Run the unchanged case against XPlat.
6. Confirm XPlat fails for the expected missing or divergent behavior.
7. Record content-addressed red proof in manifest evidence, including the first
   divergent value and the exact functional-divergence code.
8. Implement the feature in production code.
9. Run the unchanged case until XPlat passes.
10. Run the complete legacy and XPlat suites to prevent regression.
11. Capture XPlat green for the unchanged case at one clean first-green commit
    on Windows, Linux, and macOS.
12. Promote only that case to `both-green` after the original red proof and all
    current green proofs are retained and the first-green commit is verified.
    Promotion validates the entire candidate manifest, history, evidence, and
    report before a single rollback-safe transaction. Retained red evidence and
    existing content-addressed artifacts are immutable and must never be
    overwritten.
13. Promote the owning capability only when all of its surface-platform
    assignments have both-green case coverage.

A case that accidentally passes XPlat before implementation is not accepted as
proof. It must be examined for a weak assertion, an already implemented shared
behavior, or an incorrect inventory boundary.

Production implementation must not precede the parity test except for the
minimal testability seams needed to run the XPlat adapter. Those seams must not
implement the feature under test.

Changing an assertion, fixture, case definition, or expected observation
changes its content hash and requires the red-green sequence to be repeated.
Retained schema-v1 observations and other pre-schema-v3 aggregate fixtures are
historical provenance only. They cannot supply the red proof, current green
proof, first-green commit, or capability coverage required by this sequence.

A Baseline capture may select an exact, sorted subset of registered applicable
case IDs so a newly fixed case can produce an exit-0 green run while unrelated
authored cases remain red. The selected IDs must drive discovery, the recorder,
the TRX, and the execution envelope identically. Duplicate, unknown, empty, or
nonapplicable selections fail closed. Development, pull-request, and release
modes reject selection and always execute the complete applicable suite.
A no-mutation green-capture operation may validate selected green artifacts
before promotion, but it cannot update the manifest, evidence, history, or
report.

Selected capture does not replace regression execution. At the same clean
first-green revision, every capture platform must retain a complete XPlat
Development run, and Windows must retain a complete `Both` run. Promotion
verifies those full-suite execution envelopes before accepting the narrower
selected-case green artifacts. A regression in an existing both-green case, an
unexecuted applicable case, or a full-suite revision mismatch blocks promotion.
The Windows full-suite artifact must also retain its content-addressed Legacy
oracle registry and every registry-referenced executable and provenance file at
their repository-relative paths. Green promotion accepts that exact registry
and its declared hash as an external input and validates the complete artifact
closure. A locally rebuilt registry cannot substitute for the registry bound to
the retained Windows Legacy result. The uploaded package retains a
repository-relative `artifacts/...` tree inside its archive. Consumers extract
it below `artifacts/parity-imports/<package-index-sha256>/`, never into the live
parity results root. A content-addressed package index binds every retained file
by package-relative path and raw SHA-256 and supports the same closure check
both before upload and after download.
The package also retains a content-addressed execution envelope for the
dedicated Legacy oracle build-integration test process. That envelope binds the
actual process exit code, exact test identity, TRX hash, registry hash,
registry-provenance case IDs, XPlat revision and tree, and Windows runtime
identity. Re-parsing a passing TRX with an assumed exit code is not sufficient.

### 2.5 Parity metrics and gates

Every parity run reports:

- Total capabilities and capability status counts.
- Total active cases and acceptance tests executed.
- Total required and covered surface-platform assignments.
- Legacy passed, failed, and not runnable.
- XPlat passed, failed, and not runnable.
- Both-green count.
- Missing-feature count.
- Divergent-behavior count.
- Skipped, waived, quarantined, disabled, and expected-failure counts.
- Unmapped legacy features found by completeness audits.

After Phase 0, each authored manifest case is in one of two active states:

- `legacy-green-xplat-red`: the case is executed against both targets, legacy
  passes, and XPlat fails for the recorded functional gap.
- `both-green`: both targets pass the unchanged functional case.

During Phase 0, a discovered capability uses `not-authored` until a
release-certifying shared case exists, then `partial` until all required
surface-platform assignments are covered. These capability states are not
active tests, skips, waivers, quarantines, or expected failures. They block
Phase 0 completion and every release gate.

The red state is not a skipped test, framework expected-failure, quarantine, or
waiver. The runner executes the case, records the divergence, and counts it as a
functional gap. Once a case becomes both-green, any regression fails pull
request validation.

Target execution outcomes are distinct from case status:

- `passed` means the adapter completed and the exact observed-value sequence
  matched the case.
- `functional-divergence` means the adapter completed, produced a different
  functional observation, and returned the case-specific allowed divergence
  code. This is valid red proof for XPlat only when Legacy passed.
- `not-runnable` means the adapter, reference, provenance, environment, or
  recorder could not produce a valid functional observation. It is
  infrastructure failure and can never be counted as red parity evidence.

A mismatch without an observed-value difference, a missing or unexpected
failure code, an incomplete adapter, stale provenance, or a missing result is
`not-runnable`, not a functional gap. Baseline and Development modes may retain
known functional divergence while reporting a failing convergence result.
Release mode rejects both functional divergence and not-runnable outcomes.

During development, the XPlat pass percentage is expected to rise from near zero
to 100 percent. The legacy pass percentage must remain 100 percent once a test
enters the active manifest.

Release gates are:

```text
legacy pass rate           = 100%
XPlat pass rate            = 100%
complete capabilities      = all 24
covered surface-platforms  = every required assignment
functional gap count       = 0
unmapped legacy features   = 0
skipped or waived cases    = 0
not-runnable outcomes      = 0
```

### 2.6 Completeness audits

Tests prove inventoried behavior. They do not prove the inventory is complete.

The inventory audit must obtain the complete tracked-file list from the pinned
CE revision and classify every path exactly once as inventoried or narrowly
excluded. A newly tracked, omitted, multiply classified, or invented path fails
the audit. Runtime, compiler, package, test, data, resource, and external
integration inputs cannot be excluded merely because an extractor does not yet
understand them.

The team must also run recurring audits that compare the manifest against:

- Legacy contest enumeration.
- Form controls and menu actions.
- Shortcut and function-key handlers.
- Settings keys.
- data-file consumers.
- Public result and export paths.
- State-machine events.
- DSP effects and runtime toggles.

Any unmapped legacy surface or uncovered required surface-platform assignment
fails the completeness audit and creates required manifest capability or case
work before related implementation continues.

## 3. Product goals

MorseRunnerXPlat must:

1. Provide a faithful cross-platform MorseRunner experience on Windows, Linux,
   and macOS.
2. Achieve full 1:1 functional parity with the pinned legacy application, with
   zero missing features or functional gaps.
3. Preserve the characteristic real-time simulation and audio behavior.
4. Separate simulation and contest behavior from every UX implementation.
5. Support Avalonia desktop, headless CLI, automation, and an optional TUI
   through one semantic client facade.
6. Permit an optional standalone gRPC host for external clients and
   multi-client observation.
7. Remain deterministic and reproducible under an explicit seed.
8. Be testable without a UI, physical audio device, or network connection.
9. Package cleanly for each supported desktop platform.

## 4. Non-goals

The first production release does not require:

- Multiple engine implementations.
- Runtime selection between alternate engine implementations.
- A mandatory external engine process.
- Remote access over the public network.
- Real-time PCM streaming to UX clients.
- Dynamic loading of third-party contest plug-ins.
- Mobile or browser deployment.
- Pixel-for-pixel reproduction of the legacy Windows UI.

Pixel differences are allowed only when all information, interactions, keyboard
workflows, and outcomes remain functionally equivalent.

Remote access, dynamic plug-ins, mobile, browser, and alternate engines require
separate proposals.

## 5. Technology baseline

The planned baseline is:

| Area | Selection |
|---|---|
| Runtime | .NET 10 LTS |
| Language | C# version associated with the pinned SDK |
| Desktop UX | Avalonia 12.x |
| External IPC | ASP.NET Core gRPC and Protobuf |
| Contract linting | Buf |
| Test runner | Microsoft.Testing.Platform |
| Test framework | xUnit v3 unless the bootstrap spike identifies a blocker |
| Logging | `Microsoft.Extensions.Logging` abstractions |
| Configuration | Project-owned versioned models and explicit serializers |
| Repository Python tooling | uv with a pinned interpreter and committed lockfile |

The root solution must pin the SDK in `global.json` and centralize:

- Build settings in `Directory.Build.props`.
- Analyzer and warning policy in `Directory.Build.targets` when required.
- Package versions in `Directory.Packages.props`.
- Formatting in `.editorconfig`.

Repository projects must enable:

- Nullable reference types.
- Implicit usings.
- Deterministic builds.
- Warnings as errors for repository-authored code.
- Analyzers appropriate to library, UI, and performance-sensitive projects.

The root `global.json` must select `Microsoft.Testing.Platform` as the .NET test
runner. With the .NET 10 command-line interface, solution tests use:

```powershell
dotnet test --solution MorseRunnerXPlat.slnx --no-build -- --filter-not-trait Category=ParityAcceptance --filter-not-trait Category=LegacyOracleBuildIntegration
```

The ordinary solution run excludes `Category=ParityAcceptance` and the
mandatory fresh-build `Category=LegacyOracleBuildIntegration` test.
`tests\parity\Run-Parity.ps1` executes the build integration and every
certifying acceptance case exactly once, then retains and validates the target
result, run context, TRX, execution envelope, and expected functional-red
process exit when a gap remains.

NativeAOT is not a release requirement. Libraries should avoid unnecessary
reflection and dynamic loading so future trimming and AOT experiments remain
possible.

Python may be used for repository validation, fixture generation, and other
development tooling. It must be managed through `.python-version`,
`pyproject.toml`, and committed `uv.lock`. It must not become a dependency of
the shipped application, engine, audio, DSP, client, transport, or UX runtime.

## 6. System architecture

### 6.1 Logical view

```text
 Avalonia App       CLI        TUI       Automation
      |              |          |            |
      +--------------+----------+------------+
                             |
                  IMorseRunnerClient
                      /             \
       InProcessEngineClient     GrpcEngineClient
                      \             /
                 EngineApplicationService
                             |
                       SessionHost
                             |
                 single-owner render loop
                    /                  \
             SimulationSession      update publisher
                    |
              AudioBlockRenderer
                    |
                 IAudioSink
              /       |       \
        physical     WAV      null
```

### 6.2 Deployment modes

#### Embedded desktop mode

- Avalonia and the engine run in one process.
- Avalonia uses `InProcessEngineClient`.
- This is the normal and preferred desktop topology.
- No local port, engine executable discovery, or Protobuf serialization is
  required.

#### Standalone host mode

- `MorseRunner.EngineHost` owns the engine and audio device.
- External UX clients use `GrpcEngineClient`.
- Multiple clients may observe the same session.
- One client at a time may control the session.
- The host is optional and must not be required by embedded desktop mode.

#### Headless mode

- CLI and tests instantiate the engine in process.
- A null sink advances simulation without physical output.
- A WAV sink captures deterministic output.
- Headless scenarios may run faster than wall clock when no physical sink is
  selected.

### 6.3 Fundamental invariants

1. There is one production simulation engine implementation.
2. The session loop is the only owner of mutable simulation state.
3. UX code expresses operator intent and renders state. It does not decide
   contest behavior.
4. Audio remains in the process hosting the engine.
5. The default desktop topology is in process.
6. gRPC is an optional adapter over the same application service.
7. Domain and client models are not generated transport models.
8. Slow UX, transport, persistence, recording, and logging consumers cannot
   block the render loop.

## 7. Solution layout and dependencies

### 7.1 Production projects

```text
src/
  MorseRunner.Domain/
  MorseRunner.Dsp/
  MorseRunner.Engine/
  MorseRunner.Audio/
  MorseRunner.Infrastructure/
  MorseRunner.Client/
  MorseRunner.Contracts/
  MorseRunner.Grpc/
  MorseRunner.EngineHost/
  MorseRunner.App/
  MorseRunner.Cli/
  MorseRunner.Tui/
```

`MorseRunner.Tui` is a first-release client. It supports an in-process engine
with physical or null audio and the optional hosted gRPC topology.

### 7.2 Test projects

```text
tests/
  MorseRunner.Domain.Tests/
  MorseRunner.Dsp.Tests/
  MorseRunner.Engine.Tests/
  MorseRunner.Audio.Tests/
  MorseRunner.Infrastructure.Tests/
  MorseRunner.Client.Tests/
  MorseRunner.Grpc.Tests/
  MorseRunner.Scenarios.Tests/
  MorseRunner.LegacyParity.Tests/
  MorseRunner.App.Tests/
  MorseRunner.Performance.Tests/
```

Projects may be combined during the first vertical slice if separation would
create empty forwarding assemblies. They must converge on the dependency rules
below before broad feature work.

### 7.3 Responsibilities

#### MorseRunner.Domain

Owns:

- IDs and immutable value objects.
- Session settings.
- Contest-independent commands, results, events, and snapshots.
- QSO, exchange, score, result, station, and control models.
- Domain error codes.

Must not reference:

- Avalonia.
- gRPC or Protobuf.
- Concrete audio APIs.
- Filesystem, process, environment, or wall-clock APIs.

#### MorseRunner.Dsp

Owns:

- Morse envelope generation.
- Oscillators and mixers.
- Filtering.
- Modulation and demodulation where required.
- AGC and level control.
- QRN, QRM, QSB, flutter, noise, and RIT processing.
- Audio block rendering primitives.
- Numeric state required across blocks.

Must not reference UX, transport, persistence, or physical device APIs.

#### MorseRunner.Engine

Owns:

- The `IAudioSink` output port consumed by the session loop.
- Session lifecycle.
- Contest strategy selection.
- Operator and station state machines.
- Simulation clock and block number.
- Command ordering and application.
- Seeded randomness.
- QSO log, corrections, scoring, and results.
- Immutable event and snapshot publication.
- Coordination with the renderer and abstract sink.

Must not reference Avalonia, generated Protobuf types, or a concrete device
backend.

#### MorseRunner.Audio

Owns:

- Physical, WAV, and null implementations of the engine-owned `IAudioSink`
  output port.
- Device enumeration and selection.
- Format negotiation or edge resampling.
- Buffer queue and native callback adaptation.
- Native device lifecycle and cross-platform integration.
- Device-loss and recovery behavior.

It may reference a selected native binding, but platform code must remain
inside this adapter boundary.

#### MorseRunner.Infrastructure

Owns:

- Data-file loading.
- Application and user path resolution.
- Versioned settings serialization.
- Legacy INI import.
- Atomic result and recording persistence.
- Packaged reference data.

It must expose project-owned models to the engine and clients.

#### MorseRunner.Client

Owns:

- `IMorseRunnerClient`.
- `InProcessEngineClient`.
- Client-facing semantic models when they do not belong in Domain.
- Subscription and disposal contracts.

It must not expose generated Protobuf messages.

For the embedded path, `MorseRunner.Client` references `MorseRunner.Engine` and
`MorseRunner.Audio` directly and constructs the in-process engine with its
selected sink. This is the intended dependency boundary. A separate
application-service abstractions project is not warranted unless a concrete
second implementation requires it. UX projects continue to reference only
`MorseRunner.Client` and project-owned semantic models.

#### MorseRunner.Contracts

Owns generated external transport types. Generated code must not be edited
manually.

#### MorseRunner.Grpc

Owns:

- Mapping between transport and semantic client models.
- `GrpcEngineClient`.
- gRPC service implementations.
- Transport error mapping.
- Subscription, cancellation, and resync handling.

It must not implement simulation behavior.

#### MorseRunner.EngineHost

Owns:

- ASP.NET Core host bootstrap.
- Local endpoint selection and publication.
- Host authentication.
- Health and lifecycle.
- Dependency injection and structured logging setup.
- Graceful shutdown.

#### MorseRunner.App

Owns:

- Avalonia views, view models, styling, resources, and platform integration.
- Keyboard, pointer, focus, text editing, dialogs, accessibility, and
  presentation state.
- Snapshot coalescing and UI-thread dispatch.

It must not own contest rules, scoring, station behavior, simulation time, DSP,
or generated gRPC clients.

#### MorseRunner.Cli and MorseRunner.Tui

Own presentation and interaction only. Shared CLI scenario execution belongs in
a reusable application-facing library when both surfaces need it.

### 7.4 Allowed project references

| Project | May reference |
|---|---|
| Domain | Base class library only |
| Dsp | Domain where necessary |
| Engine | Domain and Dsp |
| Audio | Engine and Dsp |
| Infrastructure | Domain and engine persistence abstractions |
| Client | Domain and engine application abstractions |
| Contracts | Generated Protobuf support |
| Grpc | Client, Domain, Contracts, engine application abstractions |
| EngineHost | Engine, Audio, Infrastructure, Grpc |
| App | Client, Domain, Infrastructure composition |
| Cli | Client, Domain, Infrastructure composition |
| Tui | Client, Domain, Grpc, Infrastructure composition |

Circular project references are forbidden.

## 8. Engine application service

All UX implementations use a semantic facade with the shape:

```csharp
public interface IMorseRunnerClient : IAsyncDisposable
{
    Task<EngineInfo> GetEngineInfoAsync(CancellationToken cancellationToken);

    Task<SessionHandle> CreateSessionAsync(
        SessionSettings settings,
        CancellationToken cancellationToken);

    Task<CommandResult> ExecuteAsync(
        SessionCommand command,
        CancellationToken cancellationToken);

    Task<SessionSnapshot> GetSnapshotAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);

    IAsyncEnumerable<SessionUpdate> SubscribeAsync(
        SessionSubscription subscription,
        CancellationToken cancellationToken);

    Task CloseSessionAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);
}
```

The final API may use more specific methods or discriminated command types, but
it must preserve these semantics.

### 8.1 Client behavior

- A client instance must be safe for concurrent calls unless a method states
  otherwise.
- Every asynchronous operation must accept cancellation.
- A client must distinguish domain rejection from transport failure.
- Disposal must cancel subscriptions and release owned resources.
- The in-process and gRPC implementations must return equivalent semantic
  results for the same engine scenario.

### 8.2 Engine information

`EngineInfo` must include:

- Engine ID.
- Display name.
- Semantic version.
- Engine epoch.
- Supported contract version range.
- Capability names.
- Build and diagnostic version safe for display.
- Whether the client is connected in process or through transport.

Capability names are lowercase kebab-case. Initial capabilities should include:

- `session`
- `session-events`
- `contest-catalog`
- `audio-output`
- `wav-recording`
- `deterministic-scenarios`
- `results`
- `control-lease` when hosted mode enables multiple clients

## 9. Session model

### 9.1 Session identity

Every session has:

- A stable opaque session ID.
- An engine epoch identifying the host process instance.
- Immutable creation settings.
- A monotonic revision.
- A monotonic event sequence.
- A monotonic simulation block number.
- A seeded random source.

IDs must be treated as opaque values by clients.

### 9.2 Session lifecycle

The normative states are:

```text
Created -> Ready -> Running <-> Paused -> Stopping -> Completed
    |        |          |          |          |
    +--------+----------+----------+----------+-> Faulted

Ready / Completed / Faulted -> Closed
```

Rules:

- `Created` exists only while immutable resources are being prepared.
- `Ready` has validated settings and initialized simulation state.
- `Running` advances simulation and produces audio.
- `Paused` does not advance simulation time.
- `Stopping` drains or terminates audio according to the stop reason.
- `Completed` is terminal for the run but allows result queries and export.
- `Faulted` records a stable error code and diagnostic summary.
- `Closed` releases resources and is not queryable except through retained
  result storage.

The initial host supports one active interactive session. A second
`CreateSession` must fail with `active-session-exists` unless the prior session
is completed, faulted, or closed. This may be expanded only after resource and
control semantics are specified.

### 9.3 Immutable settings

At minimum, `SessionSettings` includes:

- Contest ID.
- Run mode.
- Duration.
- Operator callsign and contest-specific station information.
- WPM and Farnsworth settings.
- Minimum and maximum receive-speed offsets.
- Serial-number range mode and validated custom range.
- HST operator identity.
- Pitch and filter bandwidth.
- Activity and station behavior settings.
- QRN, QRM, QSB, flutter, LID, QSK, and other condition toggles.
- Audio sink selection.
- Recording settings.
- Random seed.
- Compatibility profile.

Settings that would invalidate deterministic behavior or DSP state are
immutable after `Ready`. Runtime controls are changed through commands.

## 10. Commands and ordering

### 10.1 Command envelope

Every command includes:

- Request ID.
- Session ID.
- Client ID.
- Expected session revision when stale application is unsafe.
- Control lease token when required.
- Command payload.

Request IDs provide idempotency for a bounded retention window. Repeating a
request ID with the same payload returns the original result. Reusing it with a
different payload fails.

### 10.2 Initial command categories

#### Lifecycle

- Start.
- Pause.
- Resume.
- Stop.
- Reset while in an allowed state.

#### Operator action

- Send message or invoke a semantic function-key action.
- Submit current QSO entry.
- Clear or wipe the active entry context.
- Correct callsign or exchange.
- Trigger Enter Sends Message behavior.

Enter Sends Message is one ordered session command carrying an immutable QSO
entry snapshot (call, RST, exchange field one, and exchange field two). The
session loop owns the active call-sent and operator-exchange-sent state. It
evaluates Enter in this order:

1. An empty call sends CQ.
2. A new, changed, partial, or uncertain call sends the entered call exactly.
3. A complete call whose operator exchange has not been sent sends the
   contest-specific operator exchange.
4. Missing received exchange data after the operator exchange was sent sends
   `?`.
5. A complete received exchange is validated before any `TU` is sent.
6. Successful validation sends `TU` and commits exactly one QSO as one command
   application.
7. Failed validation sends no `TU`, commits no QSO, and retains the active
   entry.

The command result includes a semantic outcome, the ordered messages sent,
the preferred entry field, whether the UX should select an uncertain-call
question mark, and whether successful completion permits clearing the entry.
Caret placement, selection, and focus remain UX responsibilities. Explicit
function-key messages such as F3 `TU` and F7 `?` do not invoke ESM or log a
QSO.

The desktop entry starts and resets with a blank displayed RST. Empty Enter
leaves it blank. The engine evaluates a nonempty Enter snapshot before the
desktop applies CE's post-send field advance. After a message is accepted for
transmission in a contest whose first received exchange type is RST, that
advance fills a still-blank displayed RST with the CE literal `599`. It must
not make an incomplete pre-send snapshot appear complete to the engine.

In the certified CQ WPX route, a nonempty call containing `?` returns focus to
Call and selects the first question mark. Any other nonempty CQ WPX call,
including a too-short call that sends only the entered call, advances to the
serial-number exchange field. Broader contest-specific focus and partially
populated-field behavior require their own CE-first vectors.

The live `ux.enter-esm-partial-call-message-selection-live` case drives the
handleless CE `MainForm.FormKeyDown(VK_RETURN)` path and drains the send queue
through `GetBlock`. Across reset and continuation actions, it compares message
selection, normalized semantic message sequence, focus, question-mark
selection, entry fields, and QSO count. The XPlat side drives the real
headless Avalonia `MainWindow` Enter
route, observes actual control focus and selection, and validates only the
semantic messages synchronously accepted into the engine queue. It does not
advance simulation blocks. Its retained baseline is
`legacy-green-xplat-red`. This narrow case does not certify renderer or
envelope completion, emitted PCM, completion callbacks, caller notification,
or broader native UX behavior.

#### Runtime control

- Set RIT.
- Set monitor level.
- Set WPM within the allowed mode.
- Set filter bandwidth if supported at runtime.
- Toggle eligible radio-condition effects.
- Start or stop recording.

#### Hosted control

- Acquire control.
- Renew control.
- Release control.
- Request explicit takeover.

Text caret, selection, focus, menu expansion, and uncommitted field editing are
not engine commands. When a semantic action depends on entered text, the UX
sends an immutable entry snapshot with that action.

### 10.3 Block-boundary application

- UX and gRPC threads enqueue commands into a bounded channel.
- The session loop assigns an arrival order.
- Eligible commands are drained at the start of a render block.
- Commands are applied in arrival order unless an explicit priority rule is
  documented.
- A command result records the applied revision and block number.
- A command accepted while paused applies at a synthetic control boundary
  without advancing simulation time.
- A command must not partially apply.

Lifecycle stop may have an emergency path for device or process shutdown. The
result and event must identify whether audio was drained or aborted.

### 10.4 Rejection

Domain rejection uses stable codes, including:

- `session-not-found`
- `invalid-session-state`
- `stale-revision`
- `duplicate-request-conflict`
- `control-required`
- `control-lease-expired`
- `invalid-setting`
- `invalid-entry`
- `command-queue-full`
- `unsupported-capability`

Human-readable messages are diagnostic and are not the programmatic contract.

## 11. Events, snapshots, and subscriptions

### 11.1 Ordered events

Events represent facts that happened and must not be silently coalesced.

Initial event groups include:

- Session created, ready, started, paused, resumed, stopping, completed,
  faulted, and closed.
- Command applied or rejected when subscription detail requests it.
- Message started and completed.
- Simulated caller joined, started, completed, or departed.
- QSO entry accepted, logged, corrected, or rejected.
- Score, multiplier, rate, or result changed.
- Recording started, stopped, or failed.
- Audio device opened, lost, recovered, underrun, or failed.
- Control acquired, renewed, released, expired, or taken over.
- Resync required.

Every event includes:

- Engine epoch.
- Session ID.
- Event sequence.
- Session revision.
- Simulation block number.
- Event payload.

Wall-clock timestamps may be included for diagnostics. Simulation behavior must
not depend on them.

### 11.2 Snapshots

Snapshots describe current state and may be coalesced.

`SessionSnapshot` includes:

- Identity, state, revision, and simulation block.
- Elapsed and remaining simulation time.
- Contest and run-mode identity.
- Operator and active QSO context, including the current simulated operator
  state when a caller is active.
- Active station summaries.
- Current score, multipliers, QSO count, and rate.
- Runtime controls.
- Audio and recording status.
- Control lease summary without secret tokens.
- Last stable error when applicable.

Snapshots must be immutable after publication.

### 11.3 Publication rate

- The session may produce internal changes every block.
- External snapshots should be published between 10 and 20 Hz while running.
- Discrete events publish promptly after the block commits.
- A UX may coalesce snapshots further.
- Physical in-process and physical hosted clients enable engine-owned automatic
  block timing. Deterministic tests, CLI scenarios, and parity vectors retain
  explicit block advancement unless they opt into automatic timing.
- Completion, fault, device loss, QSO logged, and control changes must not wait
  for the next periodic snapshot.

### 11.4 Backpressure

- Each external subscriber has a bounded queue.
- Event publication never waits for a subscriber.
- Coalescible snapshots replace older pending snapshots.
- Ordered events use a bounded retained history.
- If a subscriber falls behind retained history, the engine emits or returns
  `resync-required` and closes that subscription cleanly.
- Reconnect uses `GetSnapshot` followed by `Subscribe` from a known sequence.

In-process subscriptions must obey the same semantic behavior as gRPC
subscriptions.

## 12. Simulation time and determinism

### 12.1 Time

Simulation time is derived from rendered blocks:

```text
simulation seconds = rendered samples / canonical sample rate
```

Wall-clock time must not drive station replies, contest duration, scoring, or
random events.

### 12.2 Randomness

- A session owns one explicit `IRandomSource`.
- The seed is part of immutable session settings.
- Random decisions occur only on the session loop.
- Tests record the seed.
- Failures report the seed and first divergent block.
- Parallel tasks must not draw from the session random source.

The .NET random-number algorithm does not have to be the Pascal algorithm.
However, parity tests must still produce identical observable decisions. They
may inject a captured decision stream, replay legacy draws, or use another
deterministic compatibility adapter. Distribution-only comparison is
insufficient for a functional acceptance case that can be made exact.

### 12.3 Replay trace

The diagnostic trace format should be able to record:

- Settings and seed.
- Accepted command envelopes without secret lease tokens.
- Applied block and revision.
- Ordered events.
- Final result.
- Optional audio hash checkpoints.

A trace must be replayable through a headless in-process engine.

## 13. Contest architecture

### 13.1 Contest catalog

The first parity release covers the legacy contest catalog:

- CQ WPX.
- CWT.
- ARRL Field Day.
- NAQP.
- HST.
- CQ WW.
- ARRL DX.
- K1USN SST.
- JARL ALL JA.
- JARL ACAG.
- IARU HF.
- ARRL Sweepstakes.

HST may reuse WPX behavior where the legacy application does, but the catalog
must expose HST as a distinct selectable mode.

### 13.2 Contest interfaces

A contest implementation owns:

- Stable contest ID and display metadata.
- Supported run modes.
- Sent and received exchange definitions.
- Entry validation.
- Simulated exchange generation.
- QSO points.
- Multiplier extraction.
- Duplicate rules.
- Score calculation.
- Contest-specific message composition.
- Completion and result interpretation.

A contest must not access UI controls, filesystem paths, audio devices, gRPC,
or process-global configuration.

### 13.3 Exchange model

Exchange values must use typed project-owned models rather than anonymous
string arrays. Raw operator text may be retained for display and diagnostics,
but validation and scoring use normalized values.

The first schema-v3 contest metadata baseline uses operator call `W7SST` and
remote call `F6ABC`. For that exact scenario, the authoritative sent and
received exchange metadata is:

| Contest | Exchange field one | Exchange field two | Farnsworth |
|---|---|---|---|
| CQ WPX | RST | Serial number | No |
| CWT | Operator name | Generic field | No |
| ARRL Field Day | Field Day class | ARRL section | No |
| NAQP | Operator name | NAQP second field | No |
| HST | RST | Serial number | No |
| CQ WW | RST | CQ zone | No |
| ARRL DX | RST | State/province | No |
| K1USN SST | Operator name | Generic field | Yes |
| JARL ALL JA | RST | Japan prefecture | No |
| JARL ACAG | RST | Japan city | No |
| IARU HF | RST | Generic field | No |
| ARRL Sweepstakes | Number/precedence | Check/section | No |

Each contest uses the same exchange pair for sent and received metadata in this
baseline scenario. These rows are not universal static exchange rules. ARRL DX
selects its state/province or power exchange from operator locality, station
kind, and send/receive direction. NAQP selects its local or non-North-American
second field from station locality. Those dynamic paths remain governed by the
separate `contest.arrldx-naqp-home-filtering-and-location` obligation and
require their own live CE vectors before implementation.

A contest's configured default own exchange must pass that contest's normal
own-exchange validator. The catalog must not bypass contest-specific validation
with a blanket default-valid flag.

### 13.4 Extensibility

New built-in contests register through a catalog. Runtime plug-in loading is
out of scope. The registration mechanism must be testable without reflection
when practical.

### 13.5 Live contest scoring

All 12 catalog contests use one live rule evaluator in the authoritative
session loop. The evaluator accepts one normalized contact input and returns
validation, parsed exchange values, points, and the multiplier string. It does
not own mutable score or log state.

The implemented legacy score policies are:

- CQ WPX: one point and each distinct WPX prefix.
- CWT: one point and each distinct full callsign.
- ARRL Field Day: two points and the constant multiplier `1`.
- NAQP: one point and state or province multipliers for North American and
  Hawaii contacts. The legacy empty DX multiplier remains observable.
- HST: the additive legacy Morse-element value of each distinct callsign.
- CQ WW: zone and DXCC entity multipliers with same-entity, same-continent,
  North American, different-continent, Alaska, Hawaii, and maritime-mobile
  point rules.
- ARRL DX: three points and either DXCC entity or state/province multipliers
  based on the home station.
- K1USN SST: one point and state, province, or resolved DXCC entity.
- JARL ALL JA and ACAG: one point and the received prefecture or
  city/gun/ku code without its power suffix.
- IARU HF: one, three, or five points based on zone, society, and continent,
  with the received zone or society as multiplier.
- ARRL Sweepstakes: two points and the parsed ARRL/RAC section.

Exact-call duplicates are logged with `IsDuplicate`,
`LogError.Duplicate`, and `DUP`, but do not change verified points,
multipliers, or score. A rejected exchange does not mutate the QSO log or
score, so a corrected command with a new request ID can be retried safely.

DXCC geography is resolved once through an immutable engine lookup over the
canonical packaged `DXCC.LIST` data. Sweepstakes parsing is a pure Domain
operation. There is no contest-service interface layered over the catalog or
rule evaluator.

The worked-call set, verified point total, multiplier set, QSO log, and score
are mutable session state. Only the session loop may change them.

### 13.6 Rate calculation

The displayed QSO rate matches legacy `ShowRate` behavior:

- Before simulation time advances, the rate is zero.
- Up to five elapsed minutes, all completed QSOs strictly after time zero are
  counted.
- After five minutes, only QSOs strictly newer than the five-minute cutoff are
  counted.
- The hourly result is rounded to the nearest integer using midpoint-to-even
  rounding.

The same rate is included in immutable snapshots and completed results, and is
mapped additively through the external Protobuf contract.

## 14. Audio and DSP

### 14.1 Compatibility render format

The initial compatibility profile uses:

- Canonical render rate: 11,025 Hz.
- Block size: 512 samples.
- Block duration: approximately 46.44 ms.
- Initial target queue: four rendered blocks, approximately 185.8 ms.
- Mono floating-point internal samples.

These values preserve legacy timing during parity work. The physical audio
adapter may resample at the edge when a device does not accept the canonical
rate.

Changing the canonical rate or block size requires:

- Updated timing and DSP golden vectors.
- Contest and station state-machine parity evidence.
- Audio quality comparison.
- Performance and latency measurements.
- A specification revision.

### 14.2 Render pipeline

The compatibility pipeline is conceptually:

1. Generate complex background noise.
2. Add QRN background and bursts.
3. Add QRM stations.
4. Render active simulated stations.
5. Apply per-station BFO and RIT phase.
6. Render operator sidetone and QSK behavior.
7. Apply filtering.
8. Modulate to pitch.
9. Apply AGC and output limiting.
10. Publish the completed block to sinks.
11. Tick operator and station state.
12. Finalize QSOs, score, callers, and completion.
13. Publish events and snapshot changes.

The exact order must be verified against `TContest.GetAudio` and preserved.
An internal refactoring may differ only when shared parity tests demonstrate
identical functional and audio outcomes.

### 14.3 Renderer ownership

- One renderer instance belongs to one session.
- Renderer state is not shared between sessions.
- A block render is synchronous from the session loop's perspective.
- The renderer writes into owned or pooled memory.
- Published blocks become immutable until the sink returns them.

### 14.4 Physical audio adapter

The physical adapter must:

- Enumerate devices with stable display and opaque IDs.
- Open the default or selected device.
- Maintain a bounded ready-block queue.
- Keep native callbacks minimal.
- Handle device loss and default-device change.
- Report underruns and recovery.
- Dispose native resources deterministically.
- Support Windows, Linux, and macOS packaging.

The first implementation spike must compare at least:

- A minimal miniaudio binding.
- OpenAL Soft through an appropriate .NET binding.

The selection criteria are:

- Reliable device enumeration and hot-plug behavior.
- Callback and queue control.
- Cross-platform packaging.
- License and redistribution obligations.
- Maintenance health.
- Measured latency and underrun behavior.

The public `IAudioSink` contract must prevent the backend decision from leaking
into the engine or UX.

`IAudioSink` is defined in `MorseRunner.Engine`, which consumes the output port.
`MorseRunner.Audio` references Engine and implements that contract. Engine must
not reference Audio. No separate audio-abstractions project is used. The
interface itself is the abstraction, and a new project would add indirection
without creating a useful boundary.

The initial physical backend selection is `MiniAudioEx.NET` 2.0.2, using its
bundled miniaudio 0.11.22 native runtimes. The package provides the procedural
float callback, device enumeration, default-device selection, and Windows,
Linux, and macOS runtime assets required by the initial adapter.

The spike also evaluated OpenAL Soft through `OpenTK.Audio.OpenAL` 4.9.4.
OpenAL Soft has mature device enumeration, disconnect, system-event, and device
reopen extensions. The OpenTK package is a low-level binding and does not bundle
the OpenAL Soft native runtime, however, so it would require a second native
packaging and streaming-queue integration for the same initial result.
MiniAudioEx was selected for the smaller packaged implementation. Its global
native context is intentionally contained inside `PhysicalAudioSink`, which
permits one physical device owner per process. The engine-facing contract and
tests do not depend on the selected native library.

The physical sink uses a preallocated single-producer, single-consumer queue of
four 512-sample blocks. The native callback performs only bounded queue reads,
sample copies, zero fill, and lock-free metric updates. Playback begins only
after two blocks are queued (or one block when the configured queue capacity is
one). Automatic sessions use periodic wakeups governed by an absolute
sample-count deadline. A late wakeup renders at most two catch-up blocks per
turn, so scheduler latency does not permanently change the simulation rate or
allow unbounded catch-up work. Callback staleness or failure marks the output
unhealthy. The session pauses at the next block boundary, publishes
`AudioDeviceFailed`, and accepts `RecoverAudioCommand` for device reselection
before resume.

Receiver audio uses the legacy complex-mix topology: stateful alternating
three-pass moving-average filters, lookup-table up-conversion with legacy
carrier quantization, and look-ahead AGC normalized to floating-point device
output. A requested 600 Hz carrier therefore produces the legacy effective
612.5 Hz carrier at 11,025 Hz. Numeric parity vectors compare receiver samples,
AGC block peaks, active RMS, and effective-versus-requested carrier
correlations with a `1e-6` cross-runtime tolerance while retaining the exact
legacy fixture values.

The CE physical startup contract has four synchronous output-buffer prefill
requests followed by one completion-driven refill. On a fresh run,
`TContest.GetAudio` returns one zero `Single` for absolute requests 1 through 5;
request 6 is the first 512-sample receiver block. The receiver swaps its
parallel moving-average filter roles after absolute blocks 10 and 20, so logical
DSP phase retains the five-request offset by constructing the production
receiver with an initial absolute request count of five. Physical startup and
prefill framing belong only to `PhysicalAudioSink` and its device contract. A
per-session playback coordinator presents five one-sample zero logical frames
before presenting canonical engine audio. The first four are marked as
synchronous prefill and the fifth as completion-driven. Diagnostics expose only
logical frames that the coordinator has actually consumed. They never report
the planned prefix as completed before the playback path executes. These
logical frames do not enter the canonical audio-block queue, advance simulation
time, consume random values, or update the last rendered simulation block. A
preallocated one-block staging buffer prevents the five-sample prefix from
holding a canonical queue slot past its normal producer boundary. Queue
diagnostics continue to include any partially consumed staged canonical block,
but never count the five prefix samples. The logical frames may share a native
callback because portable audio backends do not expose CE's WinMM buffer
lifecycle.

Warmup samples must never be inserted into engine-rendered blocks, WAV output,
raw capture, or null sinks. Runtime bandwidth changes are outside this
fixed-vector contract. Stop/restart continuity remains separately pending
because CE resets the block number and repeats warmup requests while preserving
filter histories and roles, modulator phase, AGC memory, and RIT phase. The
normal Run path clears remote stations before restarting.

The fresh-start fixed vector executes the production playback coordinator
against a device-free native-shaped output buffer, verifies the five prefix
samples and their transition into the first canonical engine block, and
certifies the receiver phase separately. It does not certify a particular
portable backend callback size or callback count. It also does not certify WAV,
raw, or null adapter exclusion; those sinks require dedicated acceptance
vectors before that part of the contract can be promoted.

Local sidetone remains separate from receiver audio until the final monitor
boundary. The default monitor level is the legacy `0 dB`. A lower monitor level
attenuates only local sidetone and never changes remote stations, QRM, QRN, or
the receiver noise floor. With QSK disabled, local transmission mutes the
receiver path. With QSK enabled, the monitored local signal and receiver output
are combined.

### 14.5 Device failure

On unrecoverable physical-device failure:

1. Emit an immediate audio-device-failed event.
2. Stop accepting new blocks into the failed sink.
3. Pause simulation at a block boundary unless the user selected a documented
   fallback policy.
4. Preserve session state and pending operator context.
5. Allow device reselection and resume.

The UX must not silently continue a contest whose audio the operator cannot
hear. Physical device recovery preserves cumulative diagnostic counters while
starting a new health generation, so faults from an earlier device generation
do not immediately pause a successful recovery. Recovery preserves consumed
fresh-run startup-prefix state and does not replay the five logical frames.

### 14.6 WAV and null sinks

- WAV writing must happen outside the render callback through a bounded queue.
- A slow disk must not block rendering. Recording failure stops recording and
  emits an event without faulting the session unless explicitly configured.
- The null sink supports real-time and accelerated modes.
- WAV and null sinks must consume the same rendered blocks as the physical sink.

### 14.7 Hot-path restrictions

Steady-state block rendering must avoid:

- Managed allocation.
- LINQ.
- Boxing.
- Reflection.
- Contended locks.
- Blocking waits.
- Exceptions for control flow.
- File or network I/O.
- UI dispatch.
- RPC.
- Synchronous logging sinks.

## 15. Optional gRPC service layer

### 15.1 Purpose

The external service layer exists for:

- TUI and other independently deployed UX clients.
- Automation and scenario control.
- Remote debugging over a local machine boundary.
- Multiple simultaneous observers.
- Process isolation experiments.

It is not the default Avalonia execution path.

### 15.2 Initial services

#### EngineService

- `GetEngineInfo`
- `GetHealth`

#### CatalogService

- `ListContests`
- `GetContestDefinition`
- `GetDefaultSettings`
- `GetDataStatus`

#### SessionService

- `CreateSession`
- `GetSession`
- `ExecuteCommand`
- `SubscribeSession`
- `CloseSession`
- `AcquireControl`
- `RenewControl`
- `ReleaseControl`
- `TakeControl`

#### ResultsService

- `ListCompletedQsos`
- `GetResult`
- `ExportResult`

The initial contract should remain close to this surface. New services require
a concrete client use case.

### 15.3 Protobuf conventions

- Use one package namespace for the versioned API.
- Use cohesive files by service or tightly related model group.
- Use unique request and response envelopes for RPCs.
- Represent Enter Sends Message as an additive command payload. Its result is
  an additive command-result message so in-process and gRPC clients receive
  identical outcome and focus guidance.
- Represent receive-speed bounds, serial-number ranges, HST operator identity,
  and preferred audio-device name as additive session-setting fields.
- Use explicit `oneof` payloads for commands and events.
- Keep zero enum values unspecified.
- Prefer additive evolution.
- Never change an existing field's meaning.
- Never reuse field numbers or enum values.
- Reserve removed names and numbers.
- Use `buf lint` and `buf breaking`.

MorseRunnerXPlat intentionally does not require one file for every message.

### 15.4 Handler behavior

- gRPC handlers validate and map transport inputs.
- Mutations enqueue semantic commands.
- Handlers never lock and mutate simulation state directly.
- Server streaming respects cancellation.
- Writes are awaited and never overlap on one response stream.
- Deadlines apply to unary control calls.
- Long-lived subscriptions rely on cancellation and heartbeat status.
- Transport status codes map to stable semantic error codes.

### 15.5 Local endpoint and discovery

The standalone host must:

- Bind to loopback by default.
- Prefer an ephemeral port over a fixed global port.
- Publish endpoint, process ID, engine epoch, contract version, and a per-launch
  token through a user-private runtime file or parent-child handshake.
- Report readiness through `GetEngineInfo`, not merely an open socket.
- Remove stale discovery state safely.
- Never bind publicly because a host name or configuration value is malformed.

Named pipes on Windows and Unix domain sockets on Unix may be evaluated after
the loopback transport is proven. Their use must not change client semantics.

### 15.6 Remote access

Remote access is disabled in the first release. Enabling it requires:

- Explicit configuration.
- TLS.
- Authentication and authorization.
- Threat modeling.
- Session and message limits.
- Audit events for control changes.
- A separate release and operations plan.

## 16. Multiple clients and control lease

### 16.1 Roles

- Any authenticated client may observe an allowed session.
- Exactly one client may hold the control lease.
- Mutating session commands require the lease in hosted mode.
- Embedded in-process mode receives an implicit local lease.

### 16.2 Lease

A lease contains:

- Opaque token.
- Owning client ID.
- Issued and expiry wall-clock times.
- Lease revision.

The secret token is returned only to its owner and is never included in
snapshots or logs.

### 16.3 Heartbeat and disconnect

- The controller renews before expiry.
- A brief disconnect does not immediately transfer control.
- After expiry, the session pauses at a block boundary if it is still running.
- The engine emits a control-expired event.
- Observers do not gain control automatically.

The initial hosted defaults are a 10-second lease and a 2-second grace period.
Both are host configuration values. A bounded hosted coordinator detects
expiry, enqueues an internal semantic expiry command, pauses a running session
at its next command boundary, and publishes an ordered `control-expired`
event. Simulation behavior does not read wall-clock time directly.

### 16.4 Takeover

- Takeover is explicit.
- Normal takeover is allowed only after expiry or owner release.
- Forced takeover requires user confirmation and a supported capability.
- A takeover invalidates the prior token and emits an ordered event.

## 17. Persistence and data

### 17.1 Paths

Runtime code must use platform application-data APIs and project-owned path
abstractions. It must not assume the current working directory.

The path service distinguishes:

- Packaged read-only data.
- User settings.
- User results and logs.
- Recordings.
- Cache.
- Temporary and runtime-discovery files.

### 17.2 Settings

- Settings use a versioned project-owned schema.
- Writes are atomic through write-to-temp and replace.
- Invalid settings produce a diagnostic and a safe recovery path.
- Secrets are not stored in ordinary settings.
- Legacy INI import is one-way and idempotent.
- Unknown future fields should be preserved when practical.

### 17.3 Reference data

- File names use canonical casing.
- Tests run on a case-sensitive filesystem.
- Parsers use explicit encodings and invariant culture.
- Packaged data has a version or content hash.
- Missing or malformed required data prevents session creation with a stable
  error instead of failing during rendering.

### 17.4 Results

A completed result contains:

- Session settings and compatibility profile.
- Seed.
- Contest and run mode.
- Start, end, and simulated duration.
- QSO log with entered and verified values.
- Corrections and error categories.
- Score components and final score.
- Rate data.
- Engine version.

Result writes are atomic. Export formatting is an infrastructure concern over
project-owned result models.
JSON and Cabrillo exports use one formatter for in-process and hosted
workflows. Per-contest personal high scores are stored atomically beside
result exports.

## 18. Avalonia application

### 18.1 View-model boundaries

Planned view models include:

- Run and session lifecycle.
- Entry and validation presentation.
- Radio controls.
- Conditions.
- Log.
- Score and rate.
- Settings.
- Audio device and recording.
- Results.

View models may:

- Hold text and focus state.
- Validate local formatting needed for immediate presentation.
- Build semantic command inputs.
- Coalesce snapshots.
- Present engine errors.

View models must not:

- Calculate contest score.
- Decide exchanges or multipliers.
- Advance simulation time.
- Generate station behavior.
- Render DSP.
- Use generated Protobuf clients or messages.

### 18.2 Bindings

- Enable compiled bindings by default.
- Set `x:DataType` on views and data templates.
- Treat binding compilation and runtime binding warnings as defects.
- Avoid broad reflection-based binding fallbacks.

### 18.3 Threading

- Background subscriptions do not update observable UI state directly.
- Apply state through the Avalonia UI dispatcher.
- Coalesce snapshots before dispatch.
- Do not block the UI thread waiting for engine, audio, file, or transport work.
- Use cancellation tied to view and application lifetime.

### 18.4 Interaction

- High-frequency actions have discoverable shortcuts.
- Focus behavior is deterministic.
- Pointer interaction complements keyboard interaction.
- Enter Sends Message behavior is engine-defined but UX-triggered.
- Shortcut maps are shared as semantic commands where practical.
- Destructive actions require explicit confirmation.

### 18.5 Testing

- Test view models as ordinary .NET classes.
- Use Avalonia headless tests for layout, focus, input, or control behavior.
- Use screenshot or automation evidence for meaningful visual changes once the
  capture harness exists.
- Test scaling and fonts on all supported desktop platforms.

## 19. Error and recovery model

Errors have:

- Stable code.
- Category.
- Retryability.
- Safe display message.
- Optional diagnostic details not exposed to untrusted clients.

Categories include:

- Validation.
- Session state.
- Data.
- Audio device.
- Recording or persistence.
- Transport.
- Authorization or control.
- Internal invariant failure.

Rules:

- External transport failure does not corrupt engine state.
- Recording failure does not normally fault a session.
- Required data failure prevents session creation.
- An internal invariant failure faults the session and preserves a diagnostic
  trace.
- Cancellation is not logged as an error when expected.
- The UX always receives a stable state after a failed command.

## 20. Observability

Structured diagnostics must include:

- Engine and session identity.
- Seed.
- Block number and revision for state-related diagnostics.
- Command request ID.
- Audio queue depth and underrun count.
- Block render duration.
- Subscriber drops and resync count.
- Device lifecycle.
- Recording and persistence failures.

The render loop may increment lock-free counters or write compact records to a
bounded nonblocking diagnostic queue. It must not call a logging provider
directly.

Diagnostic export must redact:

- Lease tokens.
- Local authentication tokens.
- User-private paths when not required.
- Environment values.

## 21. Performance requirements

Performance gates are measured in Release builds on documented reference
hardware.

### 21.1 Render loop

For a representative high-activity scenario with all major effects enabled:

- Steady-state managed allocation per rendered block: zero.
- p99 render duration: less than 25 percent of block duration.
- Maximum normal render duration: less than 50 percent of block duration.
- A 30-minute run: zero underruns on reference hardware.

For the 11,025 Hz and 512-sample compatibility profile, those duration targets
are approximately 11.6 ms p99 and 23.2 ms maximum.

### 21.2 Commands and updates

- An accepted local interactive command applies within two block boundaries
  under normal load.
- Snapshot publication is 10 to 20 Hz while running.
- A slow subscriber has no measurable effect on block render latency.
- In-process command overhead is not dominated by serialization or reflection.

### 21.3 UX

- Keystroke handling and local text editing remain responsive while engine work
  is active.
- Snapshot application is coalesced and does not build an unbounded dispatcher
  backlog.
- Initial desktop window becomes usable within a target established by the
  first Avalonia vertical slice and tracked in CI thereafter.

### 21.4 Measurement

Benchmarks must:

- Use fixed scenarios and seeds.
- Report hardware, OS, runtime, configuration, and commit.
- Include allocation and duration distributions.
- Avoid debug builds and attached debuggers.
- Retain baseline results for material hot-path changes.

## 22. Test strategy

### 22.0 Tests before production ports

Cross-implementation acceptance tests are written before the corresponding
production feature.

The active development loop is:

```text
inventory -> shared test -> legacy green -> XPlat red
          -> production implementation -> XPlat green
          -> full regression -> manifest update
```

Unit tests, DSP vectors, scenario tests, and UI tests support this acceptance
layer. They do not replace the requirement that the same functional case run
against both complete implementations.

### 22.1 Unit tests

Cover:

- Value objects and validation.
- Contest rules and scoring.
- Station and operator transitions.
- Command state rules.
- Parsers and persistence mappings.
- Client and transport mappings.

### 22.2 Numeric DSP tests

Use fixed input arrays and state to verify:

- Morse envelopes.
- Filters.
- Oscillators and mixers.
- RIT phase.
- Modulation.
- AGC.
- Noise and propagation stages.

Use exact assertions where representation is exact and documented tolerances
where floating-point behavior may differ.

### 22.3 Seeded scenario tests

A scenario includes settings, seed, scheduled commands, expected events,
snapshots, QSO records, score, and optional audio hashes.

Scenario failures report:

- Seed.
- First divergent block.
- Expected and actual event or state.
- Relevant command history.

### 22.4 Legacy parity tests

Legacy parity tests are shared acceptance cases executed through both target
adapters.

Every active case must:

1. Pass the pinned legacy target.
2. While red, execute XPlat and retain the case-specific
   `functional-divergence` observation.
3. Before promotion to both-green, retain that historical red evidence and
   pass the unchanged case against the current XPlat target.
4. Bind the case definition, fixture, canonical observed values, run results,
   and live provenance by recomputable SHA-256.
5. Cover explicit legacy surfaces and platforms without promoting unrelated
   capability scope.

The suite may use a purpose-built Pascal oracle for logic below the UI. It must
also use legacy UI automation for functional workflows that cannot be proven
through the oracle alone.

Golden fixtures are caches of legacy evidence, not substitutes for validating
the reference implementation. The full release run executes the pinned legacy
target as well as XPlat.

Normal cross-platform XPlat-only CI may consume pinned golden evidence for
speed, but it must select the XPlat target explicitly. Fixture-backed tests
must select an offline-fixture adapter explicitly. The live legacy adapter has
no fallback path.

Each target executes in a separate test process. A `Both` run means one
process selected as `Legacy` and one process selected as `XPlat`; it does not
mean that a test silently chooses whichever adapter is available. A required
Windows parity or release workflow must run both implementations and fail if
legacy behavior, provenance, recorded fixture provenance, or XPlat behavior
diverges.

The result recorder distinguishes `functional-divergence` from
`not-runnable`. Only an executed XPlat value mismatch with the case-specific
allowed failure code is red functional evidence. Missing prerequisites,
process failure without a complete recorded observation, stale hashes, dirty
or unknown source, recorder failure, and adapter failure are not runnable and
fail the run.

No required test may be skipped because the legacy harness is inconvenient.
The harness must be extended until the behavior is observable.

### 22.5 Client and transport parity

Run the same semantic vector through:

- `InProcessEngineClient`.
- `GrpcEngineClient` against a local host.

Normalize engine epoch, transport status, endpoint, and wall-clock metadata.
Compare command results, event order, snapshots, QSO log, score, and audio hash
checkpoints.

### 22.6 Concurrency and resilience tests

Cover:

- Queue saturation.
- Duplicate request IDs.
- Cancellation before and after enqueue.
- Slow and disconnected subscribers.
- Resync.
- Control expiry and takeover.
- Stop during rendering.
- Device loss and recovery.
- Recording backpressure.
- Disposal races.

Use deterministic synchronization primitives rather than sleeps.

### 22.7 UI tests

Cover:

- View-model command mapping.
- Focus movement.
- Keyboard shortcuts.
- Entry validation presentation.
- Snapshot coalescing.
- Cancellation and disposal.
- Headless layout and input where valuable.

### 22.8 Cross-platform CI

Build and test the solution on:

- Windows.
- Ubuntu Linux.
- macOS.

Platform audio integration tests may require labeled hardware or manual release
gates. Null and WAV sink tests run everywhere.

The live CE oracle is a pinned Win64 build, so the release parity workflow runs
the live Legacy and XPlat processes on Windows. That Windows result does not
claim Linux or macOS case coverage by implication. Each required
surface-platform assignment must also be supported by native XPlat evidence on
Windows, Linux, and macOS as declared by its capability. Platform-neutral
numeric evidence may be reused only when the case definition explicitly
permits it and the native jobs verify the relevant XPlat adapter on their own
platform.

### 22.9 Parity CI

Parity CI has four modes:

The canonical local commands are:

```powershell
.\tests\parity\Test-ParityCompleteness.ps1
.\tests\parity\Run-Parity.ps1 -Target Both -Mode Development
```

Runner modes are:

- `Baseline`: establish and record Phase 0 legacy-green/XPlat-red evidence.
  Successful evidence validation records the gap; it does not mean parity or
  release readiness passed.
- `PullRequest`: fail legacy regressions, both-green regressions, invalid
  manifest transitions, new unmapped features, uncovered regression scope, and
  any divergence not already represented by the base manifest.
- `Development`: execute the complete suite and report convergence without
  treating already-recorded red gaps as a successful parity result.
- `Release`: require the full zero-gap production gate and return failure for
  any red or otherwise non-green item.

#### Fast pull-request mode

- Validate manifest schema and completeness mappings.
- Compare the manifest with the merge-base version and allow only monotonic
  evidence-backed case and capability promotion.
- Run ordinary cross-platform tests with the XPlat target selected explicitly.
- Use pinned fixture observations only in tests named as offline fixture tests.
- Run the live `Both` suite in the Windows parity-quality workflow.
- Prevent a both-green case from regressing.

#### Scheduled full mode

- Prepare a clean legacy worktree and build the pinned legacy target.
- Verify both Git run contexts, the oracle source and executable hashes, the
  full 14,553-file toolchain fingerprint, the exact build invocation, fresh
  oracle and provenance hashes, and content-addressed observations before
  executing acceptance cases.
- Run every active case against legacy and XPlat.
- Publish the complete parity metrics and first divergences.
- Fail on Legacy failure, not-runnable outcome, unexpected XPlat divergence,
  missing evidence, or unmapped feature. A recorded expected XPlat functional
  divergence remains a reported gap and never counts as release readiness.

#### Release mode

- Prepare and rebuild the full suite on the pinned reference revision.
- Require `-Target Both`; an XPlat-only run cannot satisfy the release gate.
- Require clean Legacy and XPlat run contexts from separate processes.
- Require every active case both-green and every manifest capability complete.
- Require every mapped surface-platform assignment covered by both-green
  evidence.
- Require zero skip, waiver, quarantine, disable, expected-failure, and
  unimplemented counts, plus zero not-runnable outcomes.
- Require the completeness audit to find zero unmapped legacy features.
- Archive the manifest, fixtures, run documents, content hashes, exact build
  provenance, toolchain fingerprint, and reference revisions as release
  evidence.
- Require native XPlat evidence on every declared platform after the live
  Windows `Both` gate. Native XPlat evidence supplements the live Legacy run;
  it never replaces it.

## 23. Security requirements

- The embedded engine has no listening endpoint by default.
- The standalone host binds only to loopback by default.
- Discovery files are user-private.
- Per-launch tokens are never logged.
- Remote access is disabled.
- Contract message sizes and session counts are bounded.
- Paths supplied through clients are validated and constrained.
- Errors do not expose stack traces to ordinary clients.
- Destructive and forced-control actions are explicit.

## 24. Packaging and release

Release targets are:

- Windows x64.
- Linux x64.
- macOS x64.
- macOS arm64.

Additional architectures may be added after CI and audio validation.

Packaging must include:

- Managed application files.
- Required native audio libraries for the runtime identifier.
- Packaged reference data with canonical casing.
- Licenses and notices for redistributed dependencies.
- Default configuration schema or resources.

Release validation includes:

- Clean-machine startup.
- Device enumeration and playback.
- Recording.
- Settings and results persistence.
- Keyboard workflow.
- Upgrade from a prior settings version.
- Uninstall or removal without deleting user results unexpectedly.

Code signing, notarization, and Linux packaging formats are release-plan items
that must be resolved before public distribution.

## 25. Delivery plan

No production feature port begins until Phase 0 has established the complete
test-first parity baseline. Later phases implement against that already-authored
acceptance suite.

### Phase 0: complete parity suite before implementation

Deliver:

- Solution-level build policy.
- Minimal project and adapter skeleton needed to execute tests.
- Agent and CI scaffold.
- Exhaustive legacy feature inventory.
- Machine-readable parity manifest.
- Completeness-audit tooling.
- Pinned legacy build or executable provenance.
- Legacy and XPlat target adapters.
- Shared acceptance scenarios sufficient to cover every capability
  surface-platform assignment.
- Compatibility fixture format.
- Baseline parity dashboard.

Exit criteria:

- Every observable legacy feature is mapped to at least one acceptance case.
- Every acceptance case passes the pinned legacy target.
- Every not-yet-implemented behavior fails XPlat for the expected reason.
- Red evidence is retained for every authored case whose XPlat behavior remains
  unimplemented.
- Accidental XPlat passes have been audited for weak assertions or pre-existing
  shared behavior.
- The completeness audit finds zero unmapped legacy features.
- No production implementation exists beyond testability seams.
- All minimal projects build and dependency tests enforce forbidden references.

Current Phase 0 trust baseline:

- The pinned CE revision has 143 tracked files. All are classified exactly
  once: 131 are inventoried and 12 narrowly excluded nonfunctional repository
  or documentation files have explicit rationales.
- The deterministic inventory contains 3,668 stable legacy surfaces mapped to
  24 broad capabilities with zero unmapped surfaces.
- Manifest schema version 3 separates broad capabilities from narrow
  executable cases. Capability completeness is calculated over mapped surface
  and required platform assignments.
- The 24 broad capabilities were reset to `not-authored` or `partial`. None is
  release-certified merely by a structural fixture or aggregate observation.
- Twenty-five schema-v1 records are retained as noncertifying historical
  provenance. They cannot count as active cases, surface-platform coverage,
  red proof, green proof, or release evidence.
- `contest.exchange-shapes` is the first authored schema-v3 case. Its current
  status is `legacy-green-xplat-red`, pending retained certifying evidence and
  the production correction. It must not be described as green before the
  unchanged case passes XPlat.
- The clean live runner prepares the exact CE revision, fingerprints all 14,553
  consumed toolchain files, records the exact compiler invocation, and executes
  Legacy and XPlat in separate processes with content-addressed run contexts
  and evidence.

Invalidated historical baseline:

- The former inventory reported 1,501 surfaces and the former manifest reported
  20 broad cases as both-green.
- That report omitted tracked forms, projects, resources, integrations, data
  paths, and legacy tests, used aggregate structural assertions and fixture
  fallback, and did not provide the trust chain required by schema version 3.
- The 1,501 and 20/20 figures are retained only to explain prior project
  history. They are not current progress metrics and cannot support any release
  claim.

### Phase 1: architectural vertical slice

Deliver:

- Domain command, event, and snapshot models.
- Session loop with seeded time and randomness.
- One minimal contest path.
- Null and WAV sinks.
- `IMorseRunnerClient` and in-process client.
- Minimal Avalonia run screen.

Exit criteria:

- Relevant pre-authored parity cases turn green without changing their
  functional assertions.
- One seeded scenario runs headlessly and through Avalonia.
- UI does not reference engine implementation.
- No physical audio or gRPC is required.

The Phase 1 deterministic runner includes an `AdvanceSimulationCommand` as an
explicit development and automation seam. It advances a requested count of
canonical 512-sample blocks only while the session is running. Production
physical audio later drives the same block loop. The command does not create a
second clock or simulation implementation.

Current Phase 1 implementation inventory, not parity certification:

- The session loop is the sole owner of mutable session state and applies
  bounded-channel commands at exact block boundaries.
- Domain commands, immutable snapshots and updates, seeded randomness, null
  and PCM16 WAV sinks, the in-process client, and a minimal Avalonia run screen
  are implemented.
- Avalonia references Client and Domain only. Client references Engine
  directly for the embedded path.
- The seeded headless and Avalonia view-model scenario exercises
  start, advance, pause, resume, and stop through `IMorseRunnerClient`.
- Pre-schema-v3 catalog observations exist, but they are among the retained
  noncertifying records. Catalog behavior remains unverified until narrow live
  cases complete the required red-green sequence.

### Phase 2: DSP and physical audio proof

Deliver:

- Compatibility render format.
- Morse generation and essential DSP pipeline.
- Audio backend selection spike.
- Physical sink on Windows, Linux, and macOS.
- Device loss and queue metrics.

Exit criteria:

- Pre-authored CW and audio parity cases turn green.
- Golden numeric vectors pass.
- Reference scenario produces acceptable audio.
- Performance gates pass on reference hardware.
- Backend selection is recorded.

Current Phase 2 implementation inventory, not parity certification:

- Legacy Morse envelope, down-mixer, cascaded quick-average, and mono PCM16 WAV
  vectors exist as pre-schema-v3 observations. They are noncertifying and must
  be replaced by narrow live cases with content-addressed evidence before they
  contribute to audio parity.
- The deterministic 600 Hz keyed tone renderer preserves state across blocks
  and has a retained SHA-256 reference vector.
- The Release renderer gate measures zero steady-state managed allocation and
  p99 below 11.6 ms for 512-sample blocks. Single-sample maximum latency is
  measured only on controlled reference hardware because shared CI scheduler
  pauses are not renderer execution time.
- The MiniAudioEx physical probe enumerates devices, opens the Windows default
  device, consumes a four-block queue with zero dropped blocks, reports
  callback health and underruns, and tears down the native context
  deterministically.
- Linux and macOS native assets are packaged by the selected dependency;
  physical-device execution remains a platform release gate.
- The parity audit found unresolved release-blocking differences in sidetone and
  QSK ordering, RIT and bandwidth effects, QRM and QRN generation, QSB and
  flutter ownership, Farnsworth session wiring, random-source ownership,
  pileup audio, block and startup behavior, WAV conversion, recording
  backpressure, device recovery, and real-time allocation coverage. Existing
  lower-level tests do not certify these behaviors as CE-equivalent.

The first manifest schema-v3 audio case is
`audio.sst-farnsworth-envelope-timing`. It executes the pinned CE `TCWSST`
send path with `TFarnsKeyer` at 11025 Hz, 512 samples per block, 15 WPM sending
speed, 25 WPM character speed, and amplitude 300000. For `PARIS TEST` and
`K1ABC 599 123`, it compares the true sample count, padded sample count, and
SHA-256 of the raw `Single` envelope bytes. Its retained pre-implementation
baseline remains `legacy-green-xplat-red`; its first divergence is the true
sample count for `PARIS TEST`. The production `MorseKeyer` now applies the CE
`TFarnsKeyer` marker and spacing rules when a character speed is configured,
including CE `Single` delay arithmetic, effective-speed fallback, true sample
count, default ramp bytes, and block padding. Static `MorseKeyer.Encode`
continues to expose the non-Farnsworth representation used by existing scoring
and DSP consumers; the envelope path translates its contextual separator runs
without changing that shared representation. The instance `EncodeText` path
emits CE markers directly and preserves leading, trailing, repeated, and
whitespace-only message pieces without the ambiguity of the shared static
encoding. `MorseToneRenderer` uses that instance path. The unchanged XPlat
development case must pass before this implementation is considered a local
green regression, while the retained red evidence remains immutable.

This case covers only SST Farnsworth encoding, spacing, default ramp numerics,
and block padding. It does not certify station mixing, effects, filtering, AGC,
PCM conversion, physical playback, or the complete session audio path.
`MorseToneRenderer` and `EngineSession` do not yet carry the separate SST
character rate, so direct keyer parity must not be treated as end-to-end
operator availability. The separate
`audio.sst-farnsworth-session-wiring` obligation remains pending until a live
session case proves that complete path.

The `audio.realistic-hiss-noise-floor` case executes the pinned CE
`TContest.GetAudio` pure receiver path and the XPlat production engine at
11025 Hz with 512-sample blocks, seed 12345, 500 Hz bandwidth, and 600 Hz
pitch. After contest construction, the CE adapter executes the real
`MainForm.SetBw` path with handleless controls, matching normal CE startup and
configuring both moving-average filters before audio begins. It then verifies
and discards its first five one-`Single` startup requests only to align the
first complete receiver block, resets the seed immediately before capture,
and records 12 consecutive blocks. The XPlat adapter starts and immediately
aborts a session, proves that no audio is written before explicit advancement,
and captures the same number of blocks through `IAudioSink`. The exact
comparison covers normalized binary32 probe bits, per-block raw-`Single`
hashes, aggregate peak and RMS values, and the aggregate raw-`Single` hash
after the production receiver filter, modulator, and AGC path.

The XPlat receiver/effects MT19937 stream is seeded directly from the session
seed for this path. Base hiss consumes two binary64 uniform values per complex
sample and casts to binary32 only after applying CE's 18000 scale and offset.
The AGC preserves CE `Single` rounding for the compression ratio before `Ln`
and for the exponent argument before `Exp`.

This case certifies only the fixed-vector base receiver hiss and noise-floor
path. The discarded CE startup requests are capture alignment and do not
certify startup framing, block warmup behavior, or filter-swap phase in
isolation. It also does not certify standalone filter, modulator, or AGC
components; station and operator mixing; QRM, QRN, QSB, flutter, QSK, or RIT;
runtime bandwidth changes; PCM or WAV conversion; recording; physical
devices; or audio-sink failure behavior. Those obligations remain pending
until separate live cases prove them. Session-wide shared random-stream
ownership and cross-feature draw order also remain pending under
`audio.single-seeded-random-stream`.

The `audio.deterministic-random-primitives-seed-12345` case executes the pinned CE
`RndFunc.pas` routines and the XPlat production `LegacyRandom` and
`LegacyRandomEffects` primitives with seed 12345. Each primitive group starts
from a fresh seed. The case records the first eight exact binary32 results, a
raw-byte SHA-256 over 4096 results for each floating distribution, an exact
4096-value integer sequence hash for `RndPoisson`, and the next raw
Random-to-`Single` value after each group to expose draw consumption. It covers
raw Random-to-`Single` conversion, `RndUniform`, `RndUShaped`, `RndNormal`,
`RndGaussLim`, `RndRayleigh`, and `RndPoisson`. The vector uses non-binary
Gaussian, Rayleigh, and Poisson parameters so intermediate rounding remains
observable.

The same case includes the FPC `System.Random(LongInt)` overload with bounds
0, 1, 2, 3, 10, 1000, 65536, and 2147483647. FPC consumes one MT19937 draw and
returns zero for `Random(0)`. XPlat `LegacyRandom.Next(0)` has the same result
and consumes the same one draw. Negative bounds also follow FPC's pre-increment
rule and consume one draw. `System.Random(LongInt)` is owned by the pinned FPC
toolchain rather than a CE repository unit, so it has no `RndFunc.pas`
inventory selector. Its behavior is bound by the exact compiler and toolchain
fingerprint in the versioned oracle recipe; the distribution routines remain
bound to their exact `RndFunc` inventory selectors.

This primitive case does not certify session-wide stream ownership, reset
timing, cross-feature draw order, or QSB, QRM, QRN, flutter, station, and
operator consumers. Those require their separate live obligations.

### Phase 3: core simulation and data

Deliver:

- Station and operator state machines.
- Call and exchange data loading.
- Conditions and radio controls.
- QSO log, corrections, score, and rate.
- Settings and path services.

Exit criteria:

- Pre-authored engine, data, logging, and condition parity cases turn green.
- Seeded scenario suite covers core pileup behavior.
- Required data is case-safe and validated at session creation.

Current Phase 3 implementation inventory, not parity certification:

- The infrastructure project embeds all 13 canonical legacy data files with
  exact casing and verified SHA-256 hashes.
- Pure callsign, WPX-prefix, and Sweepstakes-exchange parsing is owned by
  Domain. General DXCC and legacy INI parsing remain in Infrastructure. The
  engine links the same canonical packaged DXCC data into a private immutable
  scoring lookup rather than referencing Infrastructure or duplicating a
  service boundary. Pre-schema-v3 Pascal fixtures exist for portions of this
  behavior but do not certify parser, data, or failure-path completeness.
- MT19937, distribution helpers, serial-number selection, QSB processing, and
  operator transitions are deterministic for a fixed seed in XPlat. Exact CE
  behavior remains subject to narrow live cases.
- The session loop directly owns the active `SimulatedStation` collection.
  Each station owns its operator, reply timeout, CW renderer, callsign, and true
  exchange. Listening, copying, preparation, and sending transitions use
  simulation blocks. The implementation has paths for full calls, partial
  calls, repeats, corrections, ghosting, completion, and pileup confidence
  selection. Their timing and functional outcomes are not yet certified
  against CE. No station repository or station-service abstraction sits
  between the session and this state.
- Live station calls and contest exchanges are selected deterministically from
  the same 12 packaged call-history sources used by the data adapter, including
  `MASTER.DTA` for CQ WPX and HST. The engine links those immutable resources
  privately so its portable dependency boundary remains Domain plus DSP.
- Active operator state and active station summaries are additive immutable
  snapshot fields. The station summary includes callsign, station and operator
  states, patience, repeat count, WPM, pitch offset, true exchange, and last
  reply. The in-process client and explicit gRPC mapping carry the same values.
  Avalonia and the TUI expose the active pileup count.
- Station reply start and completion, caller departure, and QSO logging are
  ordered session events with revision and simulation-block metadata. Seeded
  tests verify caller sets, station event traces, true-exchange logging, NIL
  outcomes, and deterministic audio hashes.
- QSK, QSB, QRM, QRN, flutter, and LID paths exist and use deterministic XPlat
  state. The audit found differences in audio ordering, signal models, and
  random-source ownership, so these paths are not CE-equivalent yet.
- Immutable QSO records, score and multiplier behavior, radio controls,
  versioned settings, one-way INI import, atomic persistence, and
  platform-specific application paths are implemented.
- The retained event history and every external subscriber queue are bounded.
  A subscriber outside retained history receives `resync-required`.
- These implementation claims are subject to narrow schema-v3 acceptance
  coverage. The audit found unresolved station-state, operator-message,
  logging, correction, condition, persistence, and results behavior. Retained
  schema-v1 fixtures do not certify the session paths as CE-equivalent.

### Phase 4: contest parity

Deliver every legacy contest and run mode with:

- Exchange validation.
- Message behavior.
- Multipliers and scoring.
- Results.
- Legacy fixtures.

Exit criteria:

- Every contest and run-mode capability is complete and every owning case is
  both-green.
- All contest acceptance cases pass unchanged against legacy and XPlat.
- Contest functional gap count is zero.

Current Phase 4 status:

- XPlat contains catalog and rule structures for all 12 legacy contests and all
  five run-mode values. Existing unit and pre-schema-v3 fixture vectors are
  implementation inventory, not live parity certification.
- The audit found incorrect contest exchange shapes and default validation
  behavior. `contest.exchange-shapes` is the first narrow schema-v3 acceptance
  case and is currently `legacy-green-xplat-red`. Its recorded divergence is a
  release blocker until the unchanged case passes XPlat.
- Contest validation, message composition, multipliers, points, duplicates,
  correction paths, live station truth, score, rate, and results still require
  decomposed live Legacy and XPlat cases across their mapped surfaces and
  platforms.
- No contest capability is complete, and no zero-gap contest claim is valid at
  this stage.

The former 20/20 both-green and 1,501/1,501 mapped statements are invalidated
historical scaffold results. They are not current evidence. The generated
schema-v3 parity report is the machine-derived status source. The behavioral
and UX audit in `docs/ux/legacy-compatibility-matrix.md` records workflows and
gaps that still require executable cases.

### Phase 5: Avalonia product UX

Deliver:

- Complete operator workflow.
- Settings, audio, recording, log, score, rate, and result views.
- Keyboard map.
- Accessibility and scaling.
- Headless and visual test harness.

Exit criteria:

- Every legacy workflow and keyboard-function capability is complete and every
  owning case is both-green.
- Primary legacy workflows are usable without a pointer.
- Cross-platform visual and interaction review passes.

Current Phase 5 implementation inventory, not parity certification:

- The Avalonia application provides the keyboard-first operator dashboard,
  station and radio controls, band conditions, QSO entry and a live QSO log,
  score, contest and run-mode selection, duration, message keys, and score
  dialog.
- The desktop exposes F1 through F12, modifier, entry-field, abort, wipe,
  complete-QSO, RIT, bandwidth, and speed command paths. The audit found that
  several mappings, focus transitions, partial-call Enter behavior, exchange
  edits, correction paths, validation outcomes, and shortcut semantics do not
  yet match CE.
- Physical sessions advance in real time on the engine session loop. Avalonia
  consumes bounded live updates through `IMorseRunnerClient`.
- The operator status row exposes both the selected caller state and the active
  pileup count. The terminal status line exposes the same count and physical
  audio health.
- The TUI renders a fixed-size cell canvas in the alternate screen with no
  trailing terminal newline. ANSI-capable terminals receive cyan panel borders,
  highlighted entry fields, colored state, and keycaps. After the first frame,
  only changed rows are written, and snapshot-driven refreshes are limited to
  ten per second while key input repaints immediately. A Windows ConPTY and
  xterm capture verified typing and resize behavior at 120 by 34 and 100 by 28
  without scrolling, line accumulation, or overlapping status content.
- The TUI honors `NO_COLOR` and `--no-color` independently from ANSI cursor
  support. Dumb terminals use the plain repaint path. Operator, compact,
  settings, results, diagnostics, and help views render within their requested
  cell bounds and expose equivalent textual state without relying on color.
- A live Windows Avalonia launch verified a three-caller pileup with readable
  layout and keyboard focus retained in the callsign field.
- Settings persist atomically. Optional recording uses a bounded WAV writer
  beside physical playback, and completed recordings can be opened from the
  File menu.
- Avalonia exposes persisted receive-speed bounds, legacy serial-number range
  choices, a validated custom serial range, HST operator identity, and optional
  live callsign context. These values remain immutable after session creation.
- Mid-contest and end-of-contest serial selection uses the pinned legacy WPX
  weighted bins. Custom ranges retain the legacy exclusive upper bound.
- Avalonia enumerates playback devices through `IMorseRunnerClient`, selects a
  preferred device before start, and can pause, recover to another device, and
  resume without mutating the audio adapter from the UI thread.
- The score view presents the engine-owned five-minute rate and per-contest
  personal high score. JSON and Cabrillo exports are written atomically to the
  platform results directory, and the gRPC Results service reuses the same
  formatter.
- The TUI persists the same XPlat station, contest, run-mode, duration,
  condition, receive-speed, serial-range, HST operator, monitor, and recording
  keys as Avalonia. Whether their defaults, ranges, mutation rules, and effects
  match CE remains unverified. Its advanced settings view changes immutable
  session inputs before creation and sends no widget state into the engine.
- Completed TUI sessions show engine-owned score, QSO count, five-minute rate,
  elapsed time, and the shared per-contest personal high score. JSON and
  Cabrillo exports use the same infrastructure formatter and atomic save path
  as Avalonia and gRPC.
- Local physical TUI sessions can enable the bounded WAV sink for the next
  session, discover the latest completed recording, and launch it through the
  operating system. Hosted TUI sessions state that recording belongs to the
  engine-host process, preserving the rule that real-time PCM never crosses
  gRPC.
- The TUI diagnostics view exposes authenticated-host connection state, engine
  version and capabilities, session revision and block, audio health, queue
  depth, underruns, drops, and the latest operator status. The gRPC client
  performs its bounded reconnect and snapshot resync below this presentation
  layer; terminal command and subscription failures become explicit textual
  recovery states.
- QSB, QRM, QRN, flutter, activity, LIDs, monitor level, and QSK settings cross
  the semantic and gRPC session contract. Contract carriage does not establish
  that the associated station or DSP behavior matches CE.
- Compiled bindings and `x:DataType` are enabled. View-model tests, a headless
  window-open and focus test, and live Windows visual and interaction checks
  cover the primary path.
- Native Linux and macOS visual and physical-audio review, plus optional
  external score services, remain release checklist items in
  `docs/ux/legacy-compatibility-matrix.md`.
- No operator-workflow capability is complete. Keyboard, accessibility,
  lifecycle, settings, score, high-score, recording, and result claims require
  narrow live cases and native platform evidence before release.

### Phase 6: optional gRPC host

Deliver:

- Initial Protobuf contract.
- Contract lint and compatibility gates.
- gRPC adapter and standalone host.
- Discovery and local authentication.
- Subscription, reconnect, and resync.
- Control lease.

Exit criteria:

- Shared client scenario vectors pass.
- Slow observers cannot affect audio.
- Hosted mode has a real TUI, automation, or debugging consumer.
- Embedded mode remains fully supported.

Current Phase 6 implementation inventory, not parity certification:

- `morserunner.v1` defines cohesive Engine, Catalog, Session, and Results
  services with unique request and response envelopes and explicit command and
  update `oneof` payloads.
- Generated Protobuf types remain in `MorseRunner.Contracts`. Explicit
  mappings and all gRPC behavior remain in `MorseRunner.Grpc`.
- The standalone ASP.NET Core host binds cleartext HTTP/2 to an ephemeral IPv4
  loopback port, publishes a user-private discovery record, requires a
  per-launch bearer token, and removes owned discovery state on graceful
  shutdown.
- The host uses null audio by default for automation and can opt into the
  physical sink. Audio always remains in the engine-host process.
- Leases use opaque 256-bit tokens, a 10-second duration, and a 2-second grace
  period. Expiry pauses a running session through the session loop. Forced
  takeover is disabled unless the host explicitly enables it.
- The gRPC client automatically acquires and renews its lease, distinguishes
  transport exceptions from domain rejections, reconnects one interrupted
  subscription, and supports snapshot-plus-sequence resync.
- Shared in-process and gRPC scenario vectors produce equivalent commands,
  snapshots, QSO logs, and results. Authentication, cancellation, slow
  observer, and control-lease behaviors are covered by transport tests.
- `MorseRunner.Tui` is a real hosted consumer with responsive terminal
  rendering, legacy keyboard actions, all contests and run modes, band
  conditions, QSO logging, help, and a local in-process mode.
- The CLI `host-info` and `hosted-scenario` commands are real external
  debugging and automation consumers. A process-boundary Windows smoke test
  passed through discovery and authentication.
- Transport tests establish adapter behavior only. They do not promote any CE
  capability unless the same semantic case is bound to live schema-v3 parity
  evidence.

### Phase 7: packaging and release hardening

Deliver:

- Runtime-specific packages.
- Dependency notices.
- Upgrade behavior.
- Crash diagnostics.
- Clean-machine and long-run validation.

Exit criteria:

- Release checklist passes on every supported target.
- Full dual-runner parity suite passes 100 percent.
- Functional gap and unmapped-feature counts are zero.
- Skip, waiver, quarantine, disable, expected-failure, and unimplemented counts
  are zero.

Current Phase 7 implementation inventory, not release certification:

- `Publish-Release.ps1` creates self-contained Avalonia, CLI, and engine-host
  payloads and archives for `win-x64`, `linux-x64`, `osx-x64`, and
  `osx-arm64`.
- Every product payload receives the project license, README, and third-party
  dependency notices.
- Settings schema version 2 upgrades version 1 documents without losing
  values. Malformed settings recover to a safe default with a diagnostic.
- Unhandled desktop startup failures write local JSON crash diagnostics with
  runtime and platform context.
- Cross-platform .NET quality, contract lint, release parity, vulnerability
  audit, package publication, and artifact upload are encoded in GitHub
  workflows.
- `Native Release Evidence` publishes each runtime archive on its target
  operating system and architecture, extracts it into a clean evidence root,
  and launches the archived Avalonia, TUI, CLI, and engine-host products.
  `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64` use explicit native runner
  labels.
- The archive evidence harness initializes Avalonia platform services, captures
  all terminal views, validates and hashes a buffered WAV, compares normalized
  in-process and authenticated hosted snapshots, and requires graceful host
  discovery cleanup. The archived product binaries, not build-tree binaries,
  perform these checks.
- Physical evidence records device count, selected and recovery endpoints,
  whether the endpoint changed, sustained playback duration, queue depth,
  health, underruns, drops, and simultaneous WAV length. A platform is complete
  only when physical playback, recovery, and a distinct device change pass.
  Missing runner hardware remains an explicit incomplete result.
- The release checklist records clean-machine, physical audio, recording,
  keyboard, persistence, long-run, signing, notarization, and packaging-format
  verification. Missing hardware evidence is an incomplete release result, not
  proof of a runtime defect and not a waiver. Signing, notarization, and
  packaging decisions remain unresolved release operations.
- Packaging workflows and published artifacts do not establish a releasable
  port. Release remains blocked while any capability is not-authored or
  partial, any active case is red or not runnable, any surface-platform
  assignment is uncovered, or any native platform evidence is incomplete.

## 26. Definition of done

A feature is complete only when:

1. Observable acceptance criteria are met.
2. Its owning manifest capability and narrow shared acceptance case were
   authored before production implementation.
3. The acceptance case passes the pinned legacy target.
4. Historical evidence shows the unimplemented XPlat target failed the same
   case for the expected reason.
5. The unchanged case now passes XPlat.
6. The owning project is correct and dependency direction is preserved.
7. Deterministic lower-level tests cover new behavior and error paths.
8. Hot-path changes include appropriate measurements.
9. In-process behavior passes before transport-specific validation.
10. gRPC and Protobuf mappings are tested when affected.
11. Cross-platform impact is validated.
12. Avalonia changes include interaction and visual evidence when tooling
    exists.
13. Documentation, manifest evidence, and this specification are updated.
14. Every claimed surface-platform assignment is covered by the case evidence.
15. No known quality gate is left failing.

### 26.1 Parity-release definition of done

A parity release is complete only when:

- The exhaustive manifest has no unmapped legacy surface.
- Every tracked CE file is classified exactly once as inventoried or narrowly
  excluded.
- The pinned legacy target passes every acceptance case.
- XPlat passes every acceptance case.
- Every active case is both-green.
- Every manifest capability is complete.
- Every required surface-platform assignment is covered by both-green evidence.
- Functional gap count is zero.
- Missing-feature count is zero.
- Divergent-behavior count is zero.
- Skip count is zero.
- Waiver count is zero.
- Quarantine count is zero.
- Disabled count is zero.
- Expected-failure count is zero.
- Unimplemented count is zero.
- Not-runnable count is zero.
- Release evidence records clean Legacy and XPlat revisions and trees, native
  platforms, the complete toolchain fingerprint, exact build invocation,
  manifest, fixtures, runs, provenance, and recomputable content hashes.
- A live Windows `Both` run and native XPlat evidence for every required
  platform are retained.

## 27. Initial architecture acceptance tests

Before broad feature porting, the team must demonstrate:

1. The complete shared acceptance suite passes the pinned legacy target.
2. Every missing XPlat capability has a failing acceptance case and retained red
   evidence.
3. The parity manifest and completeness audit account for every discovered
   legacy capability.
4. Avalonia starts a session through `IMorseRunnerClient` without referencing
   engine implementation types.
5. A seeded headless session and Avalonia session produce the same events,
   state, score, and audio hash.
6. Commands applied during running record the exact applied block.
7. Pausing stops simulation time without corrupting audio state.
8. A slow snapshot consumer does not increase render time.
9. WAV recording and null output consume identical rendered blocks.
10. The physical sink can be removed without changing engine code.
11. The same engine application service can later be hosted behind gRPC.
12. `MorseRunner.Domain`, `MorseRunner.Dsp`, and `MorseRunner.Engine` contain no
   Avalonia or generated Protobuf references.
13. Windows, Linux, and macOS CI build and run all device-independent tests.

## 28. Deferred decisions

The following decisions are intentionally deferred to measured spikes:

| Decision | Required evidence |
|---|---|
| Exact default audio queue depth | Underrun and interaction-latency measurements |
| OS IPC after loopback gRPC | A measured reliability or security benefit over the proven local TCP host |
| Long-term settings format details | Migration and forward-compatibility prototype |
| Dynamic contest plug-ins | Concrete third-party extension requirement |
| Remote engine access | Product requirement and threat model |

Deferred decisions must not weaken current boundaries. In particular, the audio
backend must remain behind `IAudioSink`, and hosted transport must remain behind
`IMorseRunnerClient`.

## 29. One-sentence direction

Build one deterministic, audio-owning .NET simulation engine, keep every UX
behind a semantic client service, run Avalonia in process by default, and expose
the same engine over gRPC only when an external client benefits from it.
