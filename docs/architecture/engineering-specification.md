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

Oracle source hashes cover the exact checked-out bytes, including line
endings. Repository attributes normally materialize Pascal oracle sources with
CRLF. A version-specific line-ending override is part of that immutable
adapter's source contract when retained certification evidence hashed a
different byte sequence. The v16 source is therefore materialized with LF so
fresh checkouts reproduce its certified descriptor without rewriting retained
content-addressed evidence. New oracle versions use CRLF.

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
10. Before green capture or handoff, run the complete legacy and XPlat suites
    to prevent regression. One complete run may close a coherent batch of
    adjacent newly implemented cases.
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

Red evidence promotion is focused authoring proof, not release certification.
It fresh-builds only the exact selected case batch and the oracle versions that
batch references, executes build integration over that same selection, and
runs the selected cases through both adapters. It does not rebuild unrelated
historical oracle versions or compile the same selected oracle repeatedly for
reproducibility. Duplicate clean-build reproducibility runs are deferred to
green promotion before the final certification checkpoint. Ordinary Development,
pull-request, and release runs remain complete applicable-suite gates. Green
promotion still requires the complete cross-platform and Windows Legacy
artifact closure described below.

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
For ordinary Development, pull-request, and release validation,
`tests\parity\Run-Parity.ps1` executes the build integration and every
certifying acceptance case exactly once. A red evidence promotion executes the
same gates over only its exact selected authoring batch. The runner retains and
validates the target result, run context, TRX, execution envelope, and expected
functional-red process exit when a gap remains.

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
- Duration in whole minutes. The CE-compatible desktop setup range is 1
  through 240 inclusive, with a 30-minute default and no preset-only
  restriction.
- Operator callsign and contest-specific station information.
- WPM and Farnsworth settings.
- Minimum and maximum receive-speed offsets.
- Serial-number range mode and validated custom range.
- HST operator identity.
- Pitch and filter bandwidth.
- Activity, station behavior settings, and the nonnegative station-ID rate.
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

Monitor level is an engine-owned runtime radio control. The adjustment delta
is applied on the session loop, clamped to the CE range from `-60 dB` through
`0 dB`, and converted to the CE monitor gain before the next simulation block.
Snapshots expose the current monitor level. The external transport appends
`RADIO_CONTROL_MESSAGE_MONITOR_LEVEL` and `current_monitor_level_db`; older
clients continue to ignore the additive snapshot field. The desktop monitor
slider remains enabled while a session is running or paused, snaps to its
five-dB ticks, and serializes semantic adjustments through
`IMorseRunnerClient`. Setup-only station controls remain disabled.

QSK is also an engine-owned runtime radio condition. Enable and disable
commands are applied on the session loop before the next simulation block, and
snapshots expose the current QSK state. The external transport appends
`RADIO_CONDITION_MESSAGE_QSK` and `qsk_enabled`; both additions preserve older
clients.

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

The schema-v3 `catalog.contest-definition-metadata-ce-order` acceptance case
uses the live `legacy-oracle-v63` adapter to observe all twelve
`Ini.ContestDefinitions` records in declared `TSimContest` order. The catalog
contract includes the stable ID, INI key, display name, both exchange types,
both exchange captions, exchange editability, default exchange, and validation
message. This closes the static metadata boundary only. Contest validation,
exchange generation, scoring, and run-mode semantics retain their own parity
cases.

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

The schema-v3 `contest.invalid-own-exchange-messages-ce-order` case invokes
each live CE contest validator with an empty own exchange through
`legacy-oracle-v64`. It pins rejection and the exact contest-specific
required-field message for all twelve contests in catalog order. Partial-field
inputs and each field's accepted lexical boundaries remain separate vectors.
The production `ContestQsoRules` validator consumes the same catalog validation
message used by the catalog and transport surfaces, avoiding a second
contest-message table.

The schema-v3 `contest.own-exchange-tokenization-boundaries` case runs 23
manifest-supplied inputs through `legacy-oracle-v65`. It covers the base CE
validator's first-two-token behavior, empty generic and NAQP second-field
rejection, Japanese power-only forms, the W7SST ARRL DX baseline, and the
Sweepstakes override's whole-input separator and section-length rules. Two
valid controls guard the adapter setup. Lowercase UI normalization, maximum
edit lengths, locality-dependent exchange-type selection, and
received-exchange validation remain separate vectors.

The production validator mirrors this split. Ordinary contest validators
consume only the first two non-empty space-delimited tokens. The Sweepstakes
validator consumes the complete input and rejects trailing tokens. The pinned
ARRL DX state/province alphabet is the W7SST baseline and does not replace the
separate locality-dependent exchange-type selection rules.

The schema-v3 `contest.dynamic-exchange-type-locality-matrix` case runs 12
manifest-supplied inputs through `legacy-oracle-v66`. Its first eight rows pin
the complete ARRL DX home-locality, station-kind, and message-direction truth
table. Its final four rows pin NAQP sender locality and the received-message
remote-callsign override. Call-history partitioning and invalid-home-call error
text remain separate boundaries so this decision-table case stays fast and
deterministic.

The engine resolves this dynamic pair from semantic station context. For ARRL
DX it applies CE's home-locality, simulated-station, and received-message XOR.
For NAQP it classifies the sending callsign, using a non-empty remote callsign
for received-message queries. UX clients do not reproduce either contest rule.

The schema-v3 `contest.jarl-call-history-truth-column-mapping` case runs one
seeded call-history selection for ALL JA and ACAG through `legacy-oracle-v67`.
It pins the selected callsign and verifies that exchange field 1 is 599 while
the stored prefecture or city-power value occupies exchange field 2. The case
resets the same seed immediately before each contest's first selection so it
does not mix truth-column mapping with later random formatting behavior.
Production station truth maps the JARL history `Exch1` column to the remote
station's second exchange field and supplies 599 as its first field. It never
uses the generated station serial as a JARL truth fallback.
The station catalog also preserves CE's native `SizeInt` overload behavior:
selection consumes two MT19937 words, clears the combined sign bit, and applies
the list count as a 64-bit modulo. This is distinct from CE's one-word
`Random(LongInt)` multiply-high path.

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
4. Render each active simulated station and apply that station's enabled QSB
   envelope to its mono waveform.
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

QSB is owned by each remote station. It must never be applied to the aggregate
receiver buffer, receiver hiss, QRN, QRM, or local sidetone. XPlat currently
enforces this aggregate-path invariant. Active remote stations consult the
session-loop-owned QSB condition at every render boundary and apply only their
private continuously initialized QSB processor when it is enabled. The
retained runtime-toggle case certifies this positive per-station path for one
station. Independent multi-station evolution remains pending.

Flutter is not an aggregate receiver effect. CE consults it only while
constructing a remote station's QSB processor, where it may select the fast
QSB bandwidth distribution. Enabling flutter with no remote stations must not
change receiver audio or consume effect-specific random draws. XPlat enforces
the station-free and aggregate-path invariant, but positive station
construction and fast per-station QSB remain pending retained acceptance
coverage and implementation.

CE QRM is produced only by probabilistically created interfering CW stations.
Enabling QRM when the block's trigger does not create a station must not add
an aggregate tone or otherwise change that block's receiver output. XPlat
enforces this no-trigger invariant and implements the CE trigger order,
pooled station construction, message families, levels, pitch, speed, retry
state, and bounded lifetime. The retained seed-1843 case certifies the first
positive construction and same-block waveform. Retry distributions, complete
lifetime, overlapping stations, normal-caller interaction, RIT, QSK, and
runtime toggling still require their dedicated live acceptance cases before
the broader QRM obligation can be promoted.

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
per-session playback coordinator has two explicit, idempotent presentation
phases. Physical initialization and device-free parity preparation execute the
four synchronous one-positive-zero-`Single` prefill presentations. The private
fill core shared by the native `OnAudioRead` path and the device-free parity
seam executes the fifth completion-driven presentation when a positive frame
count first arrives. It then drains the five positive-zero samples before
presenting canonical engine audio. A zero-frame fill does not execute the
completion-driven presentation. Diagnostics derive each request's origin from
the completed phase state: a fresh coordinator reports zero requests,
synchronous preparation reports exactly requests 1 through 4 as prefill, and
the first positive fill reports request 5 as completion-driven. They never
infer a completed origin from a planned frame index or from prefix consumption.
These logical frames do not enter the canonical audio-block queue, advance
simulation time, consume random values, or update the last rendered simulation
block. A preallocated one-block staging buffer prevents the five-sample prefix
from holding a canonical queue slot past its normal producer boundary. Queue
diagnostics continue to include any partially consumed staged canonical block,
but never count the five prefix samples. Recovery reuses the same coordinator,
so neither presentation phase nor drained prefix samples replay. A new session
creates a fresh coordinator and executes a fresh synchronous prefill phase. The
logical frames may share a native callback because portable audio backends do
not expose CE's WinMM buffer lifecycle.

Warmup samples must never be inserted into engine-rendered blocks, WAV output,
raw capture, or null sinks. Runtime bandwidth changes are outside this
fixed-vector contract. Stop/restart continuity remains separately pending
because CE resets the block number and repeats warmup requests while preserving
filter histories and roles, modulator phase, AGC memory, and RIT phase. The
normal Run path clears remote stations before restarting.

The fresh-start fixed vector first proves that fresh physical diagnostics are
empty, executes the same synchronous preparation used by physical
initialization, and proves the intermediate four-request state. It then
executes the shared production fill core against a device-free native-shaped
output buffer, proves the fifth request's completion-driven origin, verifies
the five prefix samples and their transition into the first canonical engine
block, and certifies the receiver phase separately. These observations leave
the canonical queue empty and do not change callback, underrun, drop, fault,
or last-rendered-block diagnostics. The vector does not certify a particular
portable backend callback size or callback count. It also does not certify WAV,
raw, or null adapter exclusion; those sinks require dedicated acceptance
vectors before that part of the contract can be promoted.

Local monitor audio enters both complex receiver channels before the moving-
average filters, pitch modulator, and AGC. The default monitor level is the
legacy `0 dB`. A lower monitor level attenuates the local signal used at that
mixing boundary. During local transmission with QSK disabled, the scaled local
block replaces both receiver channels. With QSK enabled, a receiver-gain value
starts at one for each block. Each local sample may immediately reduce it to
`1 - local / local amplitude`; otherwise it recovers as
`receiver gain * 0.997 + 0.003`. The scaled local sample and gain-adjusted
receiver sample are then combined independently in both complex channels.
QSK consumes no random values.

CE derives monitor gain from `TVolumeSlider.Value` as
`10 ^ ((value - 1) * 3)`. Below slider value `0.05`, it multiplies that gain by
`value * 60`, making the `-60 dB` endpoint exactly zero. The authored
`audio.operator-monitor-minus-60db-mute-first-cq-block-seed-12345` case pins
fresh `0 dB` and `-60 dB` first-CQ blocks through the real CE monitor, filter,
modulator, and AGC path. The full block retains hash
`7d925cbba9a0bb2e86a48c5a1777c347cfed68080a559446d8e3ed3c9d6af4ee`.
The muted block has zero magnitude while its raw binary32 bytes retain signed
zero and hash
`b73ce67d7f6a60efbc46929d114471b7e79ddaee5b5a60a350a2c6a0a3ce3e6a`.
Retained red evidence records
`audio-operator-monitor-minus-60db-mute-mismatch` for the former XPlat gain
calculation, which left a nonzero `0.001` endpoint. Production now applies the
CE slider conversion, power curve, and low-level linear rolloff in the same
order. It matches both pinned block hashes and the shared random checkpoint.
Intermediate low-level slider values and other runtime transitions remain
pending.

The authored
`audio.operator-monitor-runtime-mute-second-cq-block-seed-12345` case pins a
live CE monitor change from `0 dB` to `-60 dB` after the first CQ block and
before the second. The first block retains hash
`7d925cbba9a0bb2e86a48c5a1777c347cfed68080a559446d8e3ed3c9d6af4ee`.
With filter, modulator, and AGC state continuous, the fixed-full second block
has hash
`98ed32d957fef5ee62e50a0a04cb063f4c027a9770d4a9e30b27c60dec52e234`,
while the runtime-muted second block has hash
`4d62f0d47d5d84552b1e30ae49c93f6ac69c5ebb9edad4cd655bfa5eec01e3a2`.
Both paths retain the ordinal-2048 random checkpoint `3f53fd06`. Retained red
evidence records the former XPlat gap, where both runs kept the full monitor
level and emitted the fixed-full second block. Production now applies the
semantic monitor-level command on the session loop before the next block. The
runtime-muted path matches the pinned CE second-block hash without changing the
random checkpoint. The desktop exposes the same live command through its
monitor slider.

The authored
`audio.qsk-receiver-ducking-first-cq-block-seed-12345` case pins fresh QSK-off
and QSK-on first-CQ blocks from the real CE `TContest.GetAudio` path, including
12 binary32 probes, peak, RMS, full-block hash, first QSK divergence, and the
shared random checkpoint at ordinal 1024. Its CE v37 adapter observes exact
QSK-off and QSK-on hashes
`7d925cbba9a0bb2e86a48c5a1777c347cfed68080a559446d8e3ed3c9d6af4ee`
and
`a4568db0f89409e3bf3640cd4d3a8e04fe619e20467a98674f6c6dbf5dca85f3`.
Retained red evidence records the pre-implementation
`audio-qsk-receiver-ducking-mismatch` at the QSK-off block, where XPlat added a
separate post-receiver sidetone instead of using the CE receiver mixing
boundary. Production rendering now emits the keyer envelope at CE local-station
amplitude 300000, performs the QSK-off replacement or QSK-on gain calculation
in both complex channels, and then uses the shared receiver filters, modulator,
and AGC. It matches both pinned first-block hashes and the ordinal-1024 random
checkpoint exactly. Later recovery blocks, remote signals beneath local
transmission, other monitor levels, and post-message silence remain pending.

The authored `audio.qsk-runtime-enable-second-cq-block-seed-12345` case pins a
live CE QSK change from disabled to enabled after the first CQ block and before
the second. The common first block retains hash
`7d925cbba9a0bb2e86a48c5a1777c347cfed68080a559446d8e3ed3c9d6af4ee`.
With receiver-filter, modulator, AGC, and QSK receiver-gain state continuous,
the fixed-off second block has hash
`98ed32d957fef5ee62e50a0a04cb063f4c027a9770d4a9e30b27c60dec52e234`,
while the runtime-on second block first diverges at sample 289 and has hash
`d549ce119f5a1813f4c51196439c431c5830c5aec3e5f8b4be51dd49e930f356`.
Both paths retain the ordinal-2048 random checkpoint `3f53fd06`. XPlat has no
runtime QSK command in the retained pre-implementation evidence, so that red
record contains the fixed-off second block for the requested runtime-on path.
Production now applies the semantic QSK condition on the session loop before
the next block and matches the pinned CE runtime-on hash without changing the
random checkpoint. Client transport carries the command and snapshot state;
desktop and terminal runtime mutation remain pending.

The authored
`audio.bandwidth-runtime-narrow-second-cq-block-seed-12345` case pins a live
CE receiver-bandwidth change from 500 Hz to 250 Hz after the first CQ block
and before the second. `TMainForm.SetBw` changes `Points` and `GainDb` on both
moving-average filters. The `Points` setters reset both filter histories while
the modulator and AGC remain continuous. The common first block has hash
`7d925cbba9a0bb2e86a48c5a1777c347cfed68080a559446d8e3ed3c9d6af4ee`.
The fixed-500 Hz second block has hash
`98ed32d957fef5ee62e50a0a04cb063f4c027a9770d4a9e30b27c60dec52e234`.
The runtime-250 Hz block first diverges at sample 173 and has hash
`7c95262971abcf6acf2f0324fd5b7ffc33c46032ba66111709f445f6a9bd8275`.
Both paths retain the ordinal-2048 random checkpoint `3f53fd06`. The retained
pre-implementation XPlat path accepts the semantic bandwidth command and
reports 250 Hz in its snapshot, but renders the fixed-500 Hz hash because its
receiver filters are not reconfigured. Production now replaces both receiver
filters on the session loop at the command boundary, preserves the absolute
filter-swap phase, modulator, and AGC, and calculates filter points and gain in
the same double-precision-then-Single order as CE. The runtime path matches the
pinned 250 Hz hash and ordinal-2048 random checkpoint exactly.

The authored
`audio.rit-runtime-plus-50-second-caller-block-seed-12345` case pins a live
CE RIT change from 0 Hz to +50 Hz after the first scripted remote-station block
and before the second. The v42 oracle invokes the real
`TMainForm.Panel8MouseDown` handler, then `TContest.GetAudio` subtracts the
shared RIT phase and per-sample RIT step from the station BFO. The common first
block has hash
`2ed89f5a0efa340a7546fed86add29ed234bf16925cec59d797c41ca9a217ccb`.
The fixed-0 Hz second block has hash
`690d977c8da212a55f2ac866aec81510dd1ea3164ffd4d5f555782a5db2f9ec0`.
The runtime-+50 Hz block first diverges at sample 152 and has hash
`2b2ce5e6e0c58f1ab1813a1c7727088a50aae43e3f7f69aba07d1a34401bef0c`.
Both paths retain the ordinal-2048 random checkpoint `3f53fd06`. The authored
XPlat target now applies the semantic RIT command to normal callers through the
shared `LegacyStationMixer`. Caller transmission start resets the binary32 BFO
phase, each caller block uses CE's binary32 phase accumulation and binary64
trigonometric evaluation, and the session supplies the shared block-start RIT
phase. The common, fixed-0 Hz, and runtime-+50 Hz hashes, first divergence, and
random checkpoint match the pinned CE values exactly. Negative offsets, the
lower clamp, custom and reversed persisted steps, reset, multiple stations,
transport, and UX mutation remain separate acceptance boundaries.

The authored
`audio.rit-upper-clamp-extra-click-second-caller-block-seed-12345` case
separately pins the default +50 Hz pointer step and positive +500 Hz clamp. One
fresh CE run invokes `TMainForm.Panel8MouseDown` ten times between caller
blocks, and another invokes it eleven times. Both finish at +500 Hz and render
the identical second-block hash
`d22adaeb4130e08c7d2e8adeb9e37779ddd22e0b1967f9b875e66624ead5ffc3`.
Before implementation, the XPlat semantic-command path reaches +550 Hz on the
eleventh step, first differs in its reported state, first differs in the
rendered block at sample 101, and emits hash
`069ecc737e21234023ee2be2fffb51c2c54e7587ad62a5717352169599299393`.
Both paths retain the ordinal-2048 random checkpoint `3f53fd06`. Production
now clamps the authoritative session RIT to -500 through +500 Hz. The extra
positive step leaves both state and the caller waveform at the pinned CE
values. Client-specific step mappings remain separately pending.

The authored `ux.rit-default-up-command-step-50-hz-seed-12345` case pins one
default positive client action independently from the engine range. The v44 CE
oracle verifies `Ini.RitStepIncr = 50` and invokes the real handleless
`TMainForm.Panel8MouseDown` path once, moving RIT from 0 Hz to +50 Hz. Before
implementation, the production Avalonia `RitUpCommand` reaches only +10 Hz
through `IMorseRunnerClient`. Negative actions, HST, custom or reversed
persisted steps, TUI, mouse wheel, reset, and displayed state remain separate
acceptance boundaries. Production Avalonia now sends +50 Hz and -50 Hz for its
default RIT commands; the positive command matches the pinned v44 observation
exactly.

The authored `ux.tui-rit-default-up-command-step-50-hz-seed-12345` case
separately pins the production terminal client's positive action. The v45 CE
oracle selects `rmSingle`, verifies `Ini.RitStepIncr = 50`, and invokes the
real handleless `TMainForm.Panel8MouseDown` path once, moving RIT from 0 Hz to
+50 Hz. Before implementation, `TuiApplication.HandleAsync` sends only +10 Hz
through `IMorseRunnerClient`. The negative TUI action, HST, persisted custom
steps, reset, and displayed state remain separate acceptance boundaries.
Production TUI now sends +50 Hz and -50 Hz for its default RIT actions; the
positive action matches the pinned v45 observation exactly.

The authored `ux.wpm-default-page-up-command-step-2-wpm-seed-12345` case
isolates the default non-HST WPM step from startup defaults. Both targets begin
an `rmSingle` session at 30 WPM. The v46 CE oracle verifies
`Ini.WpmStepRate = 2` and invokes the real `TMainForm.FormKeyDown` PageUp path,
which finishes at 32 WPM. Before implementation, the production Avalonia
`SpeedUpCommand` reaches only 31 WPM through `IMorseRunnerClient`. Negative
steps, HST rounding, persisted custom steps, TUI, bounds, and displayed state
remain separate acceptance boundaries. Production Avalonia now sends +2 WPM
and -2 WPM for its default speed commands; the positive command matches the
pinned v46 observation exactly.

The authored `ux.tui-wpm-default-page-up-command-step-2-wpm-seed-12345`
case applies the same CE PageUp oracle semantics to the production terminal
client. Both targets start `rmSingle` at 30 WPM. Before implementation,
`TuiApplication.HandleAsync` reaches only 31 WPM through
`IMorseRunnerClient`, while the pinned v47 CE observation reaches 32 WPM.
Negative steps, HST rounding, persisted custom steps, bounds, and display
remain separate acceptance boundaries. Production TUI now sends +2 WPM and
-2 WPM for its default speed actions; the positive action matches the pinned
v47 observation exactly.

The authored `ux.wpm-upper-clamp-extra-page-up-from-118-seed-12345` case
pins the CE CW-speed ceiling independently from the default step. The v48 CE
oracle mirrors the form's 10 through 120 WPM range, starts `rmSingle` at 118
WPM, and invokes the real PageUp handler twice. The first action reaches 120
WPM and the extra action remains at 120 WPM. Before implementation, the
production Avalonia view-model setter first clamps the requested 118 WPM to
100 WPM before session creation; its input control and the engine live-command
path share the same incorrect ceiling. The lower bound, HST rounding,
persisted custom steps, TUI setup range, and direct menu choices remain
separate acceptance boundaries. Production Avalonia setup, view-model state,
and live engine speed adjustments now expose and enforce the CE 120 WPM upper
bound; the fixed vector matches the pinned v48 observation exactly.

The authored
`ux.tui-wpm-setup-upper-range-increment-from-100-seed-12345` case applies
the CE numeric-control range to the production terminal advanced-settings
workflow. The v49 CE oracle mirrors the form's 10 through 120 WPM control,
sets WPM to 100 through `TMainForm.SetWpm`, and accepts a one-WPM increase to
101. Before implementation, `TuiApplication.AdjustCurrentSetting` remains at
100 because its setup-only ceiling is 100 WPM. The lower bound, operator-mode
PageUp clamp, HST rounding, persisted custom steps, and direct menu choices
remain separate acceptance boundaries. Production TUI advanced settings now
permit WPM values through CE's 120 WPM upper bound, and the fixed vector
matches the pinned v49 observation exactly.

The authored
`ux.tui-wpm-setup-lower-clamp-decrement-from-10-seed-12345` case pins the
opposite TUI setup boundary. The v50 CE oracle mirrors the same numeric
control, starts at 10 WPM through `TMainForm.SetWpm`, and remains at 10 when
asked to decrement by one. Before implementation,
`TuiApplication.AdjustCurrentSetting` reaches 9 WPM because its setup-only
minimum is 5. Operator-mode PageDown, HST rounding, persisted custom steps,
and direct menu choices remain separate acceptance boundaries. Production TUI
advanced settings now clamp WPM at CE's 10 WPM lower bound, and the fixed
vector matches the pinned v50 observation exactly.

The authored `ux.wpm-hst-page-up-rounds-32-to-35-seed-12345` case isolates
CE's HST-specific speed rounding from the persisted non-HST step. Both targets
start an `rmHst` session at 32 WPM. The v51 CE oracle invokes the real
`TMainForm.FormKeyDown` PageUp path, which rounds upward to the adjacent
five-WPM boundary at 35 WPM. Before implementation, the production Avalonia
`SpeedUpCommand` uses its fixed non-HST two-WPM increment and reaches 34 WPM.
HST PageDown, exact-boundary changes, TUI, persisted custom steps, and direct
menu choices remain separate acceptance boundaries. Production Avalonia now
computes PageUp's delta from the current authoritative WPM in `rmHst`, and the
fixed vector matches the pinned v51 observation exactly.

The authored `ux.tui-wpm-hst-page-up-rounds-32-to-35-seed-12345` case
applies the same CE HST PageUp boundary to the production terminal client.
Both targets start `rmHst` at 32 WPM. The v52 CE oracle reaches 35 WPM through
the real `TMainForm.FormKeyDown` path. Before implementation,
`TuiApplication.HandleAsync` sends its fixed non-HST two-WPM adjustment and
reaches 34 WPM. HST PageDown, exact-boundary changes, persisted custom steps,
and direct menu choices remain separate acceptance boundaries. Production TUI
now computes PageUp's delta from the current authoritative WPM in `rmHst`, and
the fixed vector matches the pinned v52 observation exactly.

The authored `ux.wpm-hst-page-down-rounds-33-to-30-seed-12345` case pins
the negative Avalonia HST rounding boundary. Both targets start `rmHst` at 33
WPM. The v53 CE oracle invokes the real `TMainForm.FormKeyDown` PageDown path
and rounds downward to 30 WPM. Before implementation, the production
Avalonia `SpeedDownCommand` sends its fixed non-HST two-WPM adjustment and
reaches 31 WPM. Exact-boundary changes, TUI PageDown, persisted custom steps,
and direct menu choices remain separate acceptance boundaries. Production
Avalonia now computes PageDown's delta from the current authoritative WPM in
`rmHst`, and the fixed vector matches the pinned v53 observation exactly.

The authored `ux.tui-wpm-hst-page-down-rounds-33-to-30-seed-12345` case
applies the same negative HST boundary to the terminal client. Both targets
start `rmHst` at 33 WPM. The v54 CE oracle reaches 30 WPM through the real
`TMainForm.FormKeyDown` path. Before implementation,
`TuiApplication.HandleAsync` sends its fixed non-HST minus-two adjustment and
reaches 31 WPM. Exact-boundary changes, persisted custom steps, and direct
menu choices remain separate acceptance boundaries. Production TUI now
computes PageDown's delta from the current authoritative WPM in `rmHst`, and
the fixed vector matches the pinned v54 observation exactly.

The authored `ux.wpm-custom-page-up-command-step-7-wpm-seed-12345` case
pins CE's persisted non-HST speed increment. Both targets start an `rmSingle`
session at 30 WPM with `WpmStepRate` configured as 7. The v55 CE oracle
invokes the real `TMainForm.FormKeyDown` PageUp path and reaches 37 WPM.
Before implementation, the production Avalonia settings path ignores
`Settings.WpmStepRate`, so `SpeedUpCommand` applies its fixed two-WPM default
and reaches 32 WPM. PageDown, malformed or out-of-range setting clamps, TUI,
HST precedence, and direct menu choices remain separate acceptance
boundaries. Production Avalonia now loads a valid persisted
`Settings.WpmStepRate`, applies it to non-HST PageUp, and preserves it when
settings are saved. HST continues to use its
five-WPM boundary calculation independently of the persisted step.

The authored `ux.tui-wpm-custom-page-up-command-step-7-wpm-seed-12345`
case applies the same persisted custom step to the production terminal
client. Both targets start `rmSingle` at 30 WPM with `WpmStepRate` configured
as 7. The v56 CE oracle reaches 37 WPM through the real
`TMainForm.FormKeyDown` PageUp path. Before implementation, the production
TUI settings path ignores `Settings.WpmStepRate`, so the SpeedUp action uses
its fixed two-WPM default and reaches 32 WPM. PageDown, malformed or
out-of-range setting clamps, HST precedence, and direct menu choices remain
separate acceptance boundaries. Production TUI now loads a valid persisted
step, applies it to non-HST PageUp, and preserves it on save while leaving HST
five-WPM rounding independent.

The authored `ux.wpm-custom-page-down-command-step-7-wpm-seed-12345` case
pins the negative Avalonia custom-step workflow. Both targets start
`rmSingle` at 30 WPM with `WpmStepRate` configured as 7. The v57 CE oracle
invokes the real `TMainForm.FormKeyDown` PageDown path and reaches 23 WPM.
Before implementation, production Avalonia loads and preserves the custom
step but `SpeedDownCommand` still subtracts its fixed two-WPM default and
reaches 28 WPM. TUI, lower-bound clamping, HST precedence, and direct menu
choices remain separate acceptance boundaries. Production Avalonia now
subtracts the persisted step for non-HST PageDown while retaining the
independent HST five-WPM boundary calculation.

The authored `ux.tui-wpm-custom-page-down-command-step-7-wpm-seed-12345`
case applies the negative persisted-step workflow to the terminal client.
Both targets start `rmSingle` at 30 WPM with `WpmStepRate` configured as 7.
The v58 CE oracle reaches 23 WPM through the real
`TMainForm.FormKeyDown` PageDown path. Before implementation, production TUI
loads and preserves the custom step but its SpeedDown action still subtracts
the fixed two-WPM default and reaches 28 WPM. Lower-bound clamping, HST
precedence, and direct menu choices remain separate acceptance boundaries.
Production TUI now subtracts the persisted step for non-HST PageDown while
retaining the independent HST five-WPM boundary calculation.

The authored `ux.wpm-step-lower-clamp-page-up-from-zero-seed-12345` case
pins CE's lower load clamp for the persisted WPM step. The v59 CE oracle
writes `WpmStepRate=0` beside the executable, invokes the real `Ini.FromIni`
path, verifies an effective step of 1 WPM, and reaches 31 WPM from 30 through
the real `TMainForm.FormKeyDown` PageUp path. Before implementation,
production Avalonia loads the zero value unchanged, so `SpeedUpCommand`
remains at 30 WPM. TUI, the upper load clamp, malformed values, PageDown, HST
precedence, and direct menu choices remain separate acceptance boundaries.
Production Avalonia now applies the CE lower load clamp before using or
preserving the persisted step. Values above CE's upper bound remain unmodified
until their separate acceptance boundary is implemented.

The authored `ux.tui-wpm-step-lower-clamp-page-up-from-zero-seed-12345`
case applies the same persisted lower-clamp boundary through the terminal
client. The v60 CE oracle reaches 31 WPM after loading `WpmStepRate=0` through
the real `Ini.FromIni` path. Before implementation, the production TUI loads
the zero value unchanged and remains at 30 WPM after its SpeedUp action. The
upper load clamp, malformed values, PageDown, HST precedence, and direct menu
choices remain separate acceptance boundaries. Production TUI now applies
the CE lower load clamp before using or preserving the persisted step. Values
above CE's upper bound remain unmodified until their separate acceptance
boundary is implemented.

The authored `ux.wpm-step-upper-clamp-page-up-from-21-seed-12345` case pins
CE's upper load clamp for the persisted WPM step. The v61 CE oracle writes
`WpmStepRate=21` beside the executable, invokes the real `Ini.FromIni` path,
verifies an effective step of 20 WPM, and reaches 50 WPM from 30 through the
real `TMainForm.FormKeyDown` PageUp path. Before implementation, production
Avalonia retains the 21 WPM step and reaches 51 WPM. TUI, malformed values,
PageDown, HST precedence, and direct menu choices remain separate acceptance
boundaries. Production Avalonia now clamps the loaded persisted step to CE's
complete 1 through 20 range before using or preserving it.

The authored `ux.tui-wpm-step-upper-clamp-page-up-from-21-seed-12345` case
applies the persisted upper boundary through the terminal client. The v62 CE
oracle loads `WpmStepRate=21` through the real `Ini.FromIni` path, clamps it to
20, and reaches 50 WPM from 30 through PageUp. Before implementation, the
production TUI retains the 21 WPM step and reaches 51 WPM. Malformed values,
PageDown, HST precedence, and direct menu choices remain separate acceptance
boundaries. Production TUI now clamps the loaded persisted step to CE's
complete 1 through 20 range before using or preserving it.

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
- The CE-compatible mono PCM16 sink must map normalized engine samples to the
  symmetric CE range from -32767 through 32767 and applies round-to-nearest,
  ties-to-even conversion before writing little-endian samples. It does not
  reserve -32768 as a special normalized negative-full-scale value.

The schema-v3 `audio.wav-pcm16-bit-exact` case runs a pinned seven-sample
vector through the real CE `TAlWavFile` path and the production XPlat
`WavAudioSink`. It compares the complete 11025 Hz mono RIFF/WAVE file bytes,
including header lengths, format fields, scaling, rounding, sign, and both
full-scale endpoints. Physical playback, queue backpressure, recording names,
and device lifecycle remain separate acceptance boundaries.

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

`ContestDefinitionMessage` carries the complete shared catalog metadata,
including both exchange captions and the CE validation message. Fields 8, 9,
and 10 add those values without changing the existing field numbers.

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
  preferred audio-device name, and station-ID rate as additive
  session-setting fields. An omitted station-ID rate uses the CE default of
  three QSOs.
- Represent an eligible runtime radio-condition change as the semantic
  `SetRadioCondition` command with an explicit condition and enabled value.
  The initial condition enum contains QSB. The current QSB value is an
  additive session-snapshot field so in-process and hosted clients observe the
  same applied block-boundary state.
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
ownership is now partially covered by the receiver-hiss checkpoint case below.
Cross-feature draw order remains pending under
`audio.single-seeded-random-stream`.

The authored
`audio.receiver-hiss-shared-random-checkpoint-seed-12345` case binds the base
receiver hiss to the one CE process random stream. Its pinned v13 CE adapter
creates an actual CQ WPX `TContest.GetAudio` runtime in `rmStop` with seed
12345, 11025 Hz audio, one 512-sample complete block, 500 Hz bandwidth, 600 Hz
pitch, no remote or operator transmission, and QSB, flutter, QRM, QRN, QSK,
and LIDs disabled. It executes the real handleless `MainForm.SetBw` path,
resets `RandSeed` immediately before audio framing, verifies five actual
one-`Single` zero startup requests, and captures the first complete receiver
block.

CE consumes two consecutive `Random` values for each raw complex hiss sample.
The complete block therefore consumes exactly 1024 values before filtering,
modulation, AGC, and normalization. The oracle then makes one final `Random`
call. It verifies that this zero-based ordinal 1024 came from raw value
`0x2c80d4e4` and records its rounded binary32 bits as `3e320354`. The normalized
receiver block has raw-`Single` SHA-256
`6b468ab13ccc1accb6ec587b8a51d27ca23eb80b20bce034106e547ad3565378`.

The XPlat adapter runs the equivalent production `MorseRunnerEngine` and
`EngineSession` scenario with automatic timing disabled, captures the block
through `IAudioSink`, and validates the final public `SessionSnapshot`. As its
final engine operation, it requests exactly one value from the authoritative
session random source through an internal parity-only session-loop work item.
The request is guarded by the observed revision and simulation block and does
not add a Domain, client, Protobuf, or gRPC contract. The retained
pre-implementation baseline is legacy-green/XPlat-red with divergence code
`audio-receiver-hiss-shared-random-checkpoint-mismatch`: it records the exact
CE block alongside XPlat checkpoint bits `3f6dfb52` from the then-untouched
authoritative session stream. On the certified QRN-disabled path, production
`EngineSession` now uses that one session stream for receiver hiss and returns
CE checkpoint bits `3e320354`; the retained red evidence remains immutable.

This narrow vector proves only ownership and draw order for receiver hiss in
the first complete block. The first-block QRN background transition is covered
by the separate case below. Later blocks and ordering across QRN burst-station
construction, QRM, normal station construction, QSB, flutter, call selection,
and exchanges remain uncovered, so `audio.single-seeded-random-stream`
remains partial.

The authored
`audio.qsb-no-station-noise-invariance-seed-12345` case narrows the first QSB
acceptance boundary to station-free receiver audio. Its pinned CE adapter
creates two fresh `TContest.GetAudio` runtimes with seed 12345, normal 500 Hz
`SetBw` configuration, five verified startup requests, two complete
512-sample blocks, and no remote or operator transmission. The runs differ
only in `Ini.Qsb`. CE reaches `TQsb.ApplyTo` only from
`TDxStation.GetBlock`, so enabling QSB with no remote stations leaves both
normalized blocks and their aggregate raw-`Single` hash bit-for-bit
identical. The XPlat adapter performs the same paired capture through two
fresh production engine sessions and `IAudioSink`.

The retained pre-implementation baseline remains `legacy-green-xplat-red` with
divergence code `audio-qsb-no-station-noise-invariance-mismatch`. Production
`EngineSession` no longer constructs or applies a session-global post-receiver
`QsbProcessor`, so enabling QSB leaves station-free receiver audio unchanged.
The unchanged development case must pass before this correction is considered
a local green regression, while the retained red evidence remains immutable.
Production caller construction now creates a private `QsbProcessor` for every
candidate and consumes the CE constructor and bandwidth draws even when QSB is
disabled. A station applies only its own processor while sending. Exact
positive-envelope behavior, independent multi-station evolution, runtime
toggling, and flutter remain uncertified, so
`audio.qsb-independent-per-station` remains partial.

The authored
`audio.qsb-runtime-toggle-active-station-seed-12345` case narrows the next QSB
boundary to an already-active station. Two fresh CE CQ WPX `rmStop` runtimes
construct the same scripted `K1ABC` `TDxStation` at seed 12345, force 30 WPM,
unit amplitude, zero pitch offset, and send the same eight-call message. The
disabled run leaves `Ini.Qsb` false for four 512-sample raw station-envelope
blocks. The runtime-toggle run leaves it false for blocks zero and one, then
sets it true immediately before `TDxStation.GetBlock` for blocks two and
three. Because `TDxStation` creates and warms its private `TQsb` regardless of
the setting and reads `Ini.Qsb` on every block, the first two block hashes are
identical, the last two differ, and only the enabled blocks advance the shared
random stream through `TQsb.ApplyTo`.

The pinned CE v19 adapter records six binary32 probes and a raw-`Single` hash
for every block, four transition comparisons, aggregate hashes, and terminal
random checkpoints. The retained pre-implementation evidence records
`audio-qsb-runtime-toggle-active-station-mismatch`. XPlat now owns the current
QSB value on the session loop, exposes `SetRadioCondition(Qsb, enabled)` to
both in-process and gRPC clients, includes the applied value in
`SessionSnapshot`, and passes it into every active station render. A station
retains the same private `QsbProcessor` across disabled and enabled blocks, so
enabling QSB advances exactly that station's processor and shared random state
without reconstructing it. The unchanged development case matches all 15 CE
rows bit-for-bit. Independent stations, disabling QSB after prior enabled
evolution, and UI mutation workflow remain separately pending.

The authored
`engine.start-silent-empty-enter-cq-seed-12345` case fixes the CQ WPX session
start boundary. Its pinned CE v20 adapter creates a handleless `TMainForm`
runtime for station `W7SST` at seed 12345. Before any operator input,
`TMyStation` is listening with no selected message, message text, envelope, or
QSO. Driving an empty `VK_RETURN` through `TMainForm.FormKeyDown` then selects
`msgCQ`; `TContest.SendMsg` and `TMyStation.SendText` expand the message to
`CQ W7SST TEST` and create a nonempty local envelope.

The retained pre-implementation evidence records
`engine-start-silent-empty-enter-cq-mismatch`. Production
`StartSessionCommand` now changes session state and enables timing without
loading a local message. The first operator transmission remains an explicit
operator command. For CQ WPX, both `OperatorIntent.Cq` and an empty
`TriggerEnterSendMessageCommand` compose `CQ {stationCall} TEST`, return that
exact semantic message, and load the same text into the production tone
renderer.

The authored `engine.contest-specific-cq-tu-station-id-seed-12345` case fixes
the remaining contest operator-message boundary. Its pinned CE v21 adapter
creates one handleless runtime for each of the 12 contests at station `W7SST`,
seed 12345, station-ID rate three, 25 WPM, 11025 Hz audio, and 512-sample
blocks. It records the initial CQ, TU before the threshold, TU at the
threshold, and TU after the qualifying transmission completes and resets the
counter. CWT uses `CQ CWT {stationCall}`, Field Day uses
`CQ FD {stationCall}`, SST uses `CQ SST {stationCall}`, Sweepstakes uses
`CQ SS {stationCall}`, and every other contest uses
`CQ {stationCall} TEST`. SST always sends `TU {stationCall}`. Other contests
insert the callsign only in pileup or WPX run mode when the completed-QSO
counter has reached `stationIdRate - 1`; HST mode therefore sends plain `TU`.
Logging increments the counter after TU composition. Completion of a CQ, or
completion of a TU after the configured count has been reached, resets it.

The retained pre-implementation evidence records
`engine-contest-specific-cq-tu-station-id-mismatch`. Production owns the
counter on the session loop, detects operator-envelope completion at the audio
block boundary, and applies the same composition and reset rules. The
immutable nonnegative `SessionSettings.StationIdRate` defaults to three and is
mapped additively as optional Protobuf field 24, with omission preserving that
CE default.

The authored `contest.cwt-remote-exchange-format-seed-12345` case begins the
full remote-exchange formatting obligation. Its pinned CE v22 adapter selects
CWT in pileup mode and exposes the protected `TStation.NrAsText` result for a
fixed K1ABC station with name `DAVID` and member number `123`, before reply
prefix selection or CW rendering. CE composes the exact string `DAVID  123`,
including the two spaces between fields. The retained pre-implementation
evidence records `contest-cwt-remote-exchange-format-mismatch`, where XPlat
instead discarded field one and composed `5NN123`. Production simulated
stations now retain their contest ID and use the CE CWT two-field composition
for every remote reply that consumes the station exchange formatter. Numeric
cutting, leading-zero width, other contests, repeat and correction variants,
and LID errors remain within the partial obligation.

The authored
`contest.default-two-field-remote-exchange-format-seed-12345` case extends the
same obligation through the shared CE exchange branch used by ARRL DX, ALL JA,
ACAG, and IARU HF. Its pinned CE v30 adapter fixes nonnumeric second fields and
observes the exact `5NN MA`, `5NN 12H`, `5NN 1234H`, and `5NN ARRL` exchanges.
The JARL fields contain neither zero nor nine, so their probabilistic cut paths
cannot alter this vector. Retained red evidence records
`contest-default-two-field-remote-exchange-format-mismatch`, where XPlat
discarded the separator and produced `5NNMA`, `5NN12H`, `5NN1234H`, and
`5NNARRL`. Production simulated stations for these four contests now preserve
the CE single space between cut RST and exchange field two. CQWW zones, ARRL DX
power, JARL cut variants, missing fields, repeats, correction variants, LID
errors, and random reply prefixes remain within the partial obligation.

The authored
`contest.full-cut-numeric-remote-exchange-format-seed-12345` case extends the
same obligation through CE's retained-`R1` numeric branch. Its pinned CE v31
adapter fixes `R1` at zero and observes CQWW zone `10` as `5NN AT` and ARRL DX
power `100` from remote DX call `JA1ABC` as `5NN ATT`. Retained red evidence
records `contest-full-cut-numeric-remote-exchange-format-mismatch`: XPlat
forced a synthetic three-digit CQ zone and produced `5NNT1T`, while its ARRL DX
row retained uncut `5NN 100`. Production simulated stations now apply CE's
full `1` to `A`, `0` to `T`, and `9` to `N` mapping for CQWW zones and numeric
ARRL DX power when retained `R1` is below 0.70. Higher-`R1` probabilistic power
cuts, JARL cuts, repeats, correction variants, LID errors, and random reply
prefixes remain within the partial obligation.

The authored
`contest.arrldx-high-r1-power-remote-exchange-format-seed-12345` case extends
the numeric branch through retained `R1` at 0.930. Its pinned CE v33 adapter
starts the formatter after the seven raw draws consumed by production
candidate construction. At that checkpoint CQWW zone `10` remains `5NN 10`.
ARRL DX power `100` becomes `5NN 1TT` because CE replaces leading `000` and
`00` groups before using `R1` to decide whether to apply the full `1` to `A`
cut set. Retained red evidence records
`contest-arrldx-high-r1-power-remote-exchange-format-mismatch`, where XPlat
instead produced `5NN 100`. Production ARRL DX formatting now retains CE's
non-HST random RST decision, unconditional leading-zero group replacements,
short-circuit remote zero-cut decisions, and final below-0.70 full-cut branch
in their original order. Other checkpoints and numeric fields, repeated
formatter calls, missing fields, repeats, correction variants, LID errors,
and random reply prefixes remain within the partial obligation.

The authored
`contest.jarl-random-cut-remote-exchange-format-seed-12345` case extends the
same obligation through CE's sequential JARL cut decisions. Its pinned CE v32
adapter resets `RandSeed` immediately before constructing each station and
exposes the formatter after exactly three raw draws. At that checkpoint CE
keeps the ordinary `5NN`, selects the under-0.4 zero branch, applies `00` to
`TT` before converting remaining zeroes to `O`, and then selects the
independent under-0.1 nine branch. The exact ALL JA and ACAG results are
`5NN 1ONH` and `5NN 1TTNH`. Retained red evidence records
`contest-jarl-random-cut-remote-exchange-format-mismatch`, where XPlat
produced uncut `5NN 109H` and `5NN 1009H`. Production simulated stations now
retain the same session-owned `LegacyRandom` instance already supplied to the
station operator and consume no draw merely to retain it. ALL JA and ACAG
formatting outside HST mode consumes the rare RST-error draw, the CE
short-circuit zero draws, and the independent nine draw in their original
order. Other checkpoints, repeated formatter calls, higher-`R1` ARRL DX
power, missing fields, repeats, correction variants, LID errors, and random
reply prefixes remain within the partial obligation.

The authored `contest.rare-rst-error-remote-exchange-format-seed-12345`
case extends the formatter's random boundary through CE's non-HST remote RST
error decision. Its pinned CE v34 adapter starts IARU HF headquarters exchange
formatting after five raw draws. Draw six is below 0.05, so CE replaces `599`
with `ENN` and emits `ENN ARRL`. Retained red evidence records
`contest-rare-rst-error-remote-exchange-format-mismatch`, where XPlat emitted
`5NN ARRL` without consuming the decision draw. Production IARU HF formatting
now retains the station-owned random stream, consumes the RST decision once,
and applies `ENN` before the ordinary `5NN` replacement. IARU numeric
exchanges, other RST checkpoints, repeated formatter calls, missing fields,
repeats, correction variants, LID errors, and random reply prefixes remain
within the partial obligation.

The authored
`contest.lid-serial-correction-remote-exchange-format-seed-16` case extends
the formatter through CE's one-shot LID serial correction. Its pinned CE v35
adapter fixes WPX serial 123 at the station-creation checkpoint after raw draw
nine. Seed 16 arms the LID decision at draw four, and formatter draw ten takes
the increment branch, producing the exact exchange `5NN124EEEEE 123` before
the state clears. Retained red evidence records
`contest-lid-serial-correction-remote-exchange-format-mismatch`, where XPlat
consumed but discarded the creation decision and emitted `5NN123`. Production
candidate stations now retain that decision, corrupt the final eligible serial
digit or the preceding eligible digit by one, append `EEEEE` and the correct
three-digit serial, clear the state on the first formatting attempt, and then
consume the ordinary RST and numeric-cut draws in CE order. Ineligible serial
digits, other creation checkpoints, reply prefixes, and additional LID
operator branches remain within the partial obligation.

The authored
`contest.cqww-random-consumption-remote-exchange-format-seed-12345` case
extends the formatter through CE's suppressed CQ-zone cut decisions. Its
pinned CE v36 adapter constructs the production candidate through raw draw
2465 with retained `R1` at 0.930. Formatting consumes draw 2466 for the rare
remote RST error and draws 2467 and 2468 for the two zero-cut conditions. The
CQ-zone exclusions prevent either zero substitution, so the exchange remains
`5NN 10`, while the next shared value is draw 2469 with binary32 bits
`3f1506e1`. Retained red evidence records
`contest-cqww-random-consumption-remote-exchange-format-mismatch`, where XPlat
produced the same visible exchange without consuming any formatter draws and
returned draw 2466 with bits `3f626e2f`. Production CQ WW formatting now
retains CE's RST decision, leading-zero replacements, both excluded cut
decisions, and below-0.70 full-cut branch in their original order. Other
candidate checkpoints, local-station formatting, repeated formatter calls,
reply prefixes, and LID operator branches remain within the partial
obligation.

The authored `contest.naqp-remote-exchange-format-seed-12345` case extends
that obligation through the nonempty NAQP name and location branch. Its pinned
CE v23 adapter uses the same protected `TStation.NrAsText` observation boundary
for `DAVID` and `CO`, producing the exact string `DAVID CO`. Retained red
evidence records `contest-naqp-remote-exchange-format-mismatch`, where XPlat
discarded the name and composed `5NNCO`. Production simulated stations now
compose NAQP name and location with one separating space, or emit only the
name when the location is empty, matching both CE branches. Numeric cutting,
leading-zero width, other contests, repeat and correction variants, and LID
errors remain within the partial obligation.

The authored `contest.hst-remote-exchange-format-seed-12345` case extends the
same obligation through HST serial formatting. Its pinned CE v24 adapter fixes
the remote serial at seven and observes the exact `5NN007` exchange. HST keeps
the minimum three-digit serial width and skips the ordinary run-mode numeric
cutting that would replace both leading zeroes. Retained red evidence records
`contest-hst-remote-exchange-format-mismatch`, where XPlat instead produced
`5NNTT7`. Production simulated HST stations now use their numeric station
serial, preserve decimal zeroes, and apply a minimum three-digit width while
retaining the CE `5NN` RST form. WPX serial-range modes, other contests,
repeats, correction variants, and LID errors remain within the partial
obligation.

The authored
`contest.wpx-midcontest-remote-exchange-format-seed-12345` case extends the
same obligation through WPX serial-range width. Its pinned CE v25 adapter
selects `snMidContest`, fixes the remote serial at 57, and observes the exact
`5NN57` exchange. Retained red evidence records
`contest-wpx-midcontest-remote-exchange-format-mismatch`, where XPlat forced a
three-digit serial and cut the synthetic leading zero to produce `5NNT57`.
The session now carries its serial-number range into every simulated station,
and production WPX mid-contest formatting applies CE's two-digit minimum before
the existing numeric cutting stage. Start, end, and custom range acceptance,
custom leading-zero intent, probabilistic cut variants, repeats, correction
variants, and LID errors remain within the partial obligation.

The authored `contest.wpx-custom-range-remote-exchange-format-seed-12345`
case binds the WPX/HST station-serial obligation to CE's custom range parser
and formatting path. Its pinned CE v27 adapter parses the literal range
`01-99`, fixes the remote serial at seven and the station width selector below
0.5, and observes the exact cut exchange `5NNT7`. Retained red evidence records
`contest-wpx-custom-range-remote-exchange-format-mismatch`, where XPlat lost
the textual minimum width and forced three digits, producing `5NNTT7`.

`SessionSettings` therefore carries the numeric custom bounds and the digit
width of each textual bound. The CE-compatible defaults are minimum one,
exclusive maximum 99, and two digits for each bound. A custom session requires
each width to be at least the decimal width of its bound and no greater than
four. Protobuf fields 25 and 26 carry the minimum and maximum widths
additively, with omission restoring two. Avalonia and terminal settings expose
both widths, persist the CE-compatible padded `SerialNrCustomRange` text, and
recover its leading-zero intent. The session passes the custom minimum and its
width to each simulated station. WPX formatting selects the configured width
when the station's retained `R1` is below 0.5 and otherwise uses the natural
width of the configured minimum, matching CE's per-station width choice.
Elapsed-time serial generation, arbitrary custom-range parser edge cases,
maximum-width consumers, repeats, correction variants, and LID errors remain
within the partial obligation.

The authored `contest.fieldday-remote-exchange-format-seed-12345` case extends
the same obligation through Field Day's two-field exchange composition. Its
pinned CE v26 adapter fixes the remote class at `3A` and ARRL section at `OR`,
and observes the exact `3A OR` exchange. Retained red evidence records
`contest-fieldday-remote-exchange-format-mismatch`, where XPlat discarded the
class and composed `5NNOR`. Production simulated Field Day stations now compose
the class and section with one separating space. Missing or malformed fields,
other contests, numeric cutting, repeats, correction variants, LID errors, and
random reply prefixes remain within the partial obligation.

The authored `contest.sst-remote-exchange-format-seed-12345` case extends the
same obligation through K1USN Slow Speed Test exchange composition. Its pinned
CE v28 adapter fixes the remote operator name at `BRUCE` and location at `MA`,
and observes the exact `BRUCE MA` exchange. Retained red evidence records
`contest-sst-remote-exchange-format-mismatch`, where XPlat discarded the name
and composed `5NNMA`. Production simulated SST stations now compose the name
and location with one separating space. Missing or malformed fields, other
contest formats, repeats, correction variants, LID errors, and random reply
prefixes remain within the partial obligation.

The authored `contest.sweepstakes-remote-exchange-format-seed-12345` case
extends the same obligation through the remote ARRL Sweepstakes composition
branch. Its pinned CE v29 adapter fixes serial and precedence at `123 A`, the
remote call at `K1ABC`, and check and section at `72 OR`, and observes the exact
`123 A K1ABC 72 OR` exchange. Retained red evidence records
`contest-sweepstakes-remote-exchange-format-mismatch`, where XPlat discarded
the first field and remote call and produced `5NN72 OR`. Production simulated
Sweepstakes stations now place their callsign between the two retained
exchange fields. The local-station serial prefix, malformed fields, repeats,
correction variants, LID errors, and random reply prefixes remain within the
partial obligation.

The authored
`audio.flutter-no-station-noise-invariance-seed-12345` case narrows the first
flutter acceptance boundary to station-free receiver audio. Its pinned CE
adapter creates two fresh `TContest.GetAudio` runtimes with the same seed,
normal `SetBw` configuration, five verified startup requests, two complete
blocks, no stations or operator transmission, and `Ini.Qsb` false. The runs
differ only in `Ini.Flutter`. Because the runtime constructs no `TDxStation`,
CE never reaches the station constructor's probabilistic fast-bandwidth branch,
`TDxStation.GetBlock`, or `TQsb.ApplyTo`; both normalized block sequences and
their aggregate raw-`Single` hash remain bit-for-bit identical. The XPlat
adapter performs the same paired capture through fresh production engine
sessions and `IAudioSink`.

The retained pre-implementation baseline remains `legacy-green-xplat-red` with
divergence code `audio-flutter-no-station-noise-invariance-mismatch`.
Production `EngineSession` no longer applies a session-global post-receiver
flutter multiplier, so enabling flutter leaves station-free receiver audio
unchanged. The unchanged development case must pass before this correction is
considered a local green regression, while the retained red evidence remains
immutable. This correction does not implement or certify positive per-station
ownership, QSB gating, the 30 percent fast-mode selection, bandwidth
distribution, draw order, independent envelopes, or runtime toggling. The
`audio.flutter-fast-per-station-qsb` obligation therefore remains partial.

The authored
`audio.qrm-no-trigger-invariance-seed-12345` case narrows the first QRM
acceptance boundary to a station-free block in which no interferer is created.
Its pinned v12 CE adapter creates two fresh actual `TContest.GetAudio` runtimes
for CQ WPX in `rmStop` with seed 12345, 11025 Hz audio, 512-sample blocks,
500 Hz bandwidth, 600 Hz pitch, no normal DX stations or operator
transmission, and QSB, flutter, QRN, QSK, and LIDs disabled. The runs differ
only in `Ini.Qrm`. Each run executes the real handleless `MainForm.SetBw` path,
resets `RandSeed` immediately before audio framing, verifies five real
one-`Single` zero startup requests, and requests exactly one complete block.

With QRN disabled, that complete block consumes 1024 shared random values for
its complex hiss before QRM evaluates random ordinal 1024. For seed 12345 the
trigger value is approximately 0.17384081427007914, which is greater than the
0.0002 construction threshold. The adapter requires the actual production
`Tst.Stations` collection to remain empty in both runs and records the actual
counts. The clean and QRM-enabled normalized binary32 blocks, probe bits, and
raw-`Single` hashes must be bit-for-bit identical. The case deliberately does
not compare a second block because the enabled run consumed the otherwise
absent trigger draw and therefore begins its next block at a different
shared-stream position.

The XPlat adapter performs the paired capture through two fresh actual
`MorseRunnerEngine` and `EngineSession` sessions and the production
`IAudioSink` port. It starts and immediately aborts each `rmStop` session,
proves no audio was written before one explicit block advance, and requires
zero active stations. The retained pre-implementation baseline remains
`legacy-green-xplat-red` with first divergence at the `qrm-block[0]` row and
code `audio-qrm-no-trigger-invariance-mismatch`. Production `EngineSession` no
longer applies an unconditional post-receiver QRM sine, so the unchanged
development case now preserves the CE no-trigger block exactly while the
retained red evidence remains immutable.

This case does not certify positive QRM construction, construction probability
and draw ownership, a shared-random sentinel, message selection, levels,
pitch, WPM, same-block audibility, retries, or station lifetime. Those require
separate live cases, so `audio.qrm-interfering-cw-stations` remains partial.

The authored `audio.qrm-first-triggered-station-seed-1843` case adds the first
positive QRM construction boundary. Its pinned v16 CE adapter creates fresh
clean and QRM-enabled CQ WPX `rmStop` runtimes with seed 1843, 11025 Hz audio,
512-sample blocks, 500 Hz bandwidth, 600 Hz pitch, station call `W7SST`, no
normal DX stations or operator transmission, and every other optional effect
disabled. Both runtimes execute the real handleless `MainForm.SetBw` path,
reset `RandSeed` immediately before audio framing, verify five actual
one-`Single` startup requests, and capture one complete receiver block.

That block consumes 1024 complex-hiss draws before the QRM trigger at ordinal
1024. Its binary32 bits are `38e1bf40`, so it satisfies CE's 0.0002
construction threshold. The eager `TQrmStation` constructor then consumes R1
ordinal 1025, patience ordinal 1026, call ordinal 1027, amplitude ordinal
1028, Gaussian pitch ordinals 1029 and 1030, WPM ordinal 1031, and message
ordinal 1032. The next shared-stream value is ordinal 1033 with bits
`3f519e01`. A content-bound production `LegacyRandom` and
`LegacyRandomEffects` replay pins the complete ten-draw binary32 sequence and
the bounded-integer decisions without substituting those decisions for the
engine execution.

The catalog input is the pinned 1,239,476-byte `MASTER.DTA` with SHA-256
`acf37090e7c9c0f2146a2b08608295cb243c8bfe649a421d1c528a59656097aa`.
It contains 46,039 calls, and ordinal 1027 selects index 23,903, `LU5MT`.
The live station has patience 2, R1 bits `3f03301e`, amplitude
19583.306640625 with bits `4698fe9d`, pitch offset -124 Hz, and sending and
character speeds of 31 WPM. Message choice 2 selects `[msgQrl2]` and text
`QRL?   QRL?`. The eager message encoder produces 53,248 samples, or 104
complete blocks. The trigger block consumes the first 512 samples and leaves
the station `stSending` at send position 512 with 103 blocks remaining.

The clean normalized receiver block hash is
`3ba44162f2959aeeaa6599059e97033d942258e3a2e0dc33cbb07defbb12a4c5`.
The QRM block hash is
`72f7618e7e055db7fefd472c47f0488046087905d5810b1e1aa97b88187f643d`,
and its first final-output divergence from clean is sample 310. These hashes
cover same-block QRM mixing through receiver filtering, modulation, AGC,
normalization, and clamping.

The XPlat adapter executes independent clean and QRM-enabled production
`MorseRunnerEngine` sessions with automatic timing disabled. It starts and
immediately aborts each session, advances one block, captures the actual
`IAudioSink` output, and verifies public `ActiveStations`, `LastCaller`, and
caller events remain empty. QRM is internal audio interference, not a contest
caller. The internal parity-only
`EngineSession.ObserveQrmStationForParityAsync` seam is therefore guarded by
session ID at the engine wrapper plus exact revision and simulation block. It
rejects automatic timing and stale boundaries, runs on the session worker,
does not advance simulation or consume random values, and is absent from
`IMorseRunnerClient`, snapshots, gRPC, and UX contracts. It returns the first
active QRM station in chronological receiver-source order, or an explicit
empty observation when no QRM station exists.

Production evaluates the QRM trigger after complex hiss and all enabled QRN
work. A successful trigger eagerly consumes the CE constructor sequence,
appends a pooled QRM source to the shared chronological receiver-source list,
and mixes the new source in that same block. `StationReferenceCatalog` owns
the mutable contest call catalog used by both normal and QRM callers. It
preserves WPX/HST empty-list fallback and HST deletion, ARRL DX side
partitioning and invalid-DXCC retry deletion, and NAQP deferred validation and
retry deletion.

QRM messages use fixed string segments rather than render-time concatenation.
`LegacyMorseEnvelopeCursor` treats those segments as one logical message,
applies the standard keyer for every contest except SST, streams the exact CE
ramp and spacing samples, and includes zero padding through the complete final
512-sample block. One immutable keying profile owns the shared ramp pair.
Pooled stations retain only cursor and oscillator state, and the session
reuses one 512-sample envelope scratch block. Activation, block rendering,
timeout processing, retry preparation, and release allocate no memory on the
session render path.

`LegacyStationMixer` resets binary32 BFO phase for every transmission,
advances it with the CE positive-only wrap, preserves the binary32
`BFO - RitPhase` intermediate, evaluates the per-sample RIT term and separate
cosine and sine contributions in binary64, and casts each receiver
accumulation once to binary32. QRM and normal caller sources use the same
block-start RIT phase and mixer arithmetic. The session advances and wraps RIT
once after chronological source mixing and before local-monitor and receiver
processing. Applying that shared RIT phase to QRN sources and certifying
multi-source interaction remain broader receiver-mixing parity items.

The QRM pool is sized before rendering from the active catalog and actual
operator callsign. If `BLong` is the maximum 30-WPM padded long-CQ block count
and `BInitial` is the maximum padded initial-message block count, the exact
structural bound is:

`BInitial + 4 * (129 + BLong)`

There can be at most one new QRM source per block. The bound covers five
transmissions, four maximum 129-block retry timeouts, the active contest's
longest catalog call, the operator-call QSY message, and the `P29SX`
MASTER.DTA empty-list fallback. Caller additions reserve their own slot plus
all QRN and QRM slots, so an active trigger never grows the chronological
source list.

After audio reaches the sink, all remote receiver sources tick in reverse
chronological order. Normal caller rendering is separated from its timeout and
state-transition tick so shared random draws remain ordered with QRM retry
draws. A completed QRM transmission clears its message text,
decrements patience, and either releases immediately or draws the CE limited
Gaussian retry timeout. Each silent block decrements that timeout. Reaching
zero prepares the contest long CQ at the tick boundary, and its first sample
is rendered in the following block. QRM sources remain excluded from public
caller snapshots, counts, operator-input matching, scoring, and events.

The retained immutable evidence remains XPlat-red at row three, `station`,
with code `audio-qrm-first-triggered-station-mismatch`, recording the required
preimplementation failure. The unchanged production target now passes all ten
fixture rows exactly. It observes one internal station, consumes constructor
ordinals 1025 through 1032, reproduces the QRM receiver hash and sample-310
divergence, and reaches terminal bits `3f519e01`.

This case certifies one successful first-block trigger, eager constructor draw
order, one catalog selection, one level, pitch, speed, message, envelope
length, same-block audibility, and terminal checkpoint. It does not certify
trigger-rate statistics, other messages or distributions, retry and timeout
behavior, complete lifetime, overlapping QRM stations, normal-station
interaction (including active-QRM callsign collision suppression), RIT, or
runtime toggling. The
`audio.qrm-interfering-cw-stations` obligation remains partial.

The incremental QRM hot-path gate uses a preallocated eight-station harness
with the production QRM station, catalog, mixer, envelope cursor, retry, and
release paths. After 256 warmup blocks, it measures 4096 blocks while keeping
eight sources active. The Release gate requires zero measured managed
allocation, p99 below 11.6 ms, and the p99.9 nearest-rank normal maximum below
23.2 ms. It retains the raw maximum for audit and records activation, retry,
and release counts. This scoped gate measures the QRM increment only. It does
not replace the release-wide all-effects and 30-minute underrun gates in
Section 21.

The first retained Windows x64 capture is
`tests/MorseRunner.Engine.Tests/PerformanceEvidence/audio-qrm-station-hot-path.windows-x64.v1.json`.
It measures clean revision `7c4fee8e8db71f198954995df4941f7db8f850df`
on .NET 10.0.8 and an Intel Core i9-10980XE. Across 4096 blocks it records
zero managed allocation, 0.2692 ms p99, 0.2975 ms p99.9 normal maximum, and
a 0.4120 ms raw maximum. The run includes 66 activations, 105 retries, and 58
releases. This is development evidence for one Windows machine, not
cross-platform release certification.

The authored
`audio.qrm-caller-collision-retry-limit-seed-24680` case isolates the CE
caller retry boundary while one `K1ABC` QRM station is active. The pinned v18
oracle scripts ten `K1ABC` caller rows. `TStations.AddCaller` constructs a
complete `TDxStation` before each collision check, discards attempts one
through nine, and accepts attempt ten without another check because the
retry counter has reached zero. Each discarded candidate still constructs
its operator and QSB state, consumes the receive-speed, amplitude, and pitch
draws, and loads its exchange. The tenth caller retains `EX10`, `ID10`, and
catalog metadata, while the terminal random checkpoint is ordinal 25373
with bits `3f7bb16d`.

The v18 oracle source has SHA-256
`230c0cf302280ae280e8c00c5d22ec95bff83cc18be6b06fdad8765e778a8e69`.
Its build recipe has SHA-256
`451543aa96992127762563a90f0ece92bd6235f731414725736b0d702db429b5`,
and the reviewed executable has SHA-256
`f06ea390595447a4216c4f45196d7901884a857eee3efed14ab905d5b9941722`.
All sixteen fixture rows are exact and the oracle self-check rejects any
change to them.

The internal
`EngineSession.ObserveCallerCollisionForParityAsync` probe is available only
through the engine assembly's parity test friendship. It requires manual
timing, exact revision and simulation block, QRM enabled, and one fresh
execution per session. The work runs on the session worker, creates the live
QRM source, invokes production `AddCaller` with a scripted identity selector,
and reports every candidate construction plus the terminal random value. It
does not appear in `IMorseRunnerClient`, snapshots, gRPC, or UX contracts.

The retained preimplementation boundary is
`legacy-green-xplat-red` with code
`audio-qrm-caller-collision-retry-limit-mismatch`. The first divergence is
the zero-based `catalog` row at ordinal two. At that checkpoint, production
excluded QRM sources from caller collision checks and constructed a caller
only after an identity survived selection, so the retained red evidence
records the first duplicate being accepted and the nine discarded
full-candidate constructions being skipped.

The unchanged production-backed target now passes all sixteen fixture rows.
`SimulatedStation.CreateCandidate` performs CE construction order: station R1,
identity and exchange selection, `SimulatedOperator` creation in
`NeedPreviousEnd`, receive-speed selection, QSB initialization and bandwidth
refill, amplitude, and Gaussian pitch. `EngineSession.AddCaller` constructs
and observes a candidate before testing its callsign against both normal
callers and active QRM sources. Attempts one through nine may be discarded,
while attempt ten is accepted without another collision check. Only the
accepted ordinary runtime caller transitions to `NeedQso` and its send-delay
state. The parity probe suppresses that later arrival transition so it
observes the same immediate post-`TStations.AddCaller` boundary as CE.

The authored
`audio.qrn-background-sparse-impulses-seed-12345` case narrows the first QRN
boundary to one station-free complete receiver block. Its pinned v14 CE
adapter creates two fresh actual `TContest.GetAudio` runtimes for CQ WPX in
`rmStop` with seed 12345, 11025 Hz audio, a 512-sample block, 500 Hz
bandwidth, 600 Hz pitch, no normal DX stations or operator transmission, and
QSB, flutter, QRM, QSK, and LIDs disabled. The runs differ only in `Ini.Qrn`.
Each executes the real handleless `MainForm.SetBw` path, resets `RandSeed`
immediately before audio framing, verifies five actual one-`Single` zero
startup requests, and captures the first complete block through the live
method.

The content-bound source-order replay consumes CE's 1024 complex-hiss draws,
then one QRN trigger for each of the 512 raw samples. Seed 12345 selects six
real-component replacement indexes: 92, 248, 323, 482, 488, and 507. Their
trigger ordinals are 1116, 1273, 1349, 1509, 1516, and 1536, and their
replacement-value ordinals are 1117, 1274, 1350, 1510, 1517, and 1537. These
raw indexes and decision values are replay-derived from the content-bound
`Contest.pas` source order. They are not presented as direct pre-filter
instrumentation. The actual live `TContest.GetAudio` block, station count,
final-output probes, and terminal sentinel corroborate the complete
observable path.

The replay-derived burst trigger is ordinal 1542 with value 0.486590252, so
the actual CE station collection remains empty. The clean block raw-`Single`
SHA-256 is
`6b468ab13ccc1accb6ec587b8a51d27ca23eb80b20bce034106e547ad3565378`.
The QRN block hash is
`16375b39a2a153bc44f33449bad640084f8dbed67c6b8bb9ccb3dec094be5435`,
and its first live normalized output divergence from clean is sample 310
after both receiver filters, modulation, AGC, normalization, and clamping.
An actual final `Random` call after each live block records clean next ordinal
1024 as binary32 bits `3e320354` and QRN next ordinal 1543 as bits `3f43412e`.

The XPlat adapter performs the paired capture through two fresh actual
`MorseRunnerEngine` and `EngineSession` sessions and the production
`IAudioSink` port. After validating each final public snapshot, it requests
one guarded terminal value from the authoritative session random source. The
retained pre-implementation baseline is legacy-green/XPlat-red with first row
divergence at `qrn-block[0]` and code
`audio-qrn-background-sparse-impulses-mismatch`. It records the former
effect-specific QRN stream and continuous post-receiver noise path, including
first final-output divergence at sample 0 and an untouched authoritative
session checkpoint.

Production now injects the authoritative session `LegacyRandom` into
`LegacyReceiverNoiseGenerator`. For every complete block it generates all
complex hiss first. With QRN enabled it then takes one binary64 trigger per
sample, replaces only the selected real components with CE's binary64
360000-scale expression followed by one binary32 cast, and takes the burst
trigger after the background loop. The prepared complex block enters both
receiver filters before modulation and AGC. The old effect-specific random
source and normalized post-AGC continuous QRN addition have been removed.
This path allocates no memory per block and preserves the clean ordinal 1024
and QRN ordinal 1543 terminal checkpoints. A true burst trigger now eagerly
constructs an internal pooled QRN station from the same authoritative random
stream before any receiver source is mixed.

This case covers first-block background trigger probability, replacement
semantics, source-order draw ownership, final receiver output, the no-burst
station count, and terminal sentinels. The separate triggered vector below
certifies one two-block burst. Other seeds, duration boundaries, overlapping
bursts, interactions with normal stations, and runtime toggling remain
uncovered.

The authored `audio.qrn-burst-station-lifecycle-seed-1903` case binds the
first successful QRN burst trigger to the pinned v15 CE oracle. Two fresh live
CQ WPX `rmStop` runtimes use seed 1903, 11025 Hz audio, 512-sample blocks,
500 Hz bandwidth, 600 Hz pitch, QRN enabled, no normal DX stations or
operator transmission, and every other optional effect disabled. Both
execute the real handleless `MainForm.SetBw` path and five actual one-`Single`
startup requests. One runtime stops after its first complete block for an
uncontaminated shared-random checkpoint. The other advances continuously
through two complete blocks.

The content-bound source-order replay records eight block-one background
replacements before the successful burst trigger at ordinal 1544. The actual
`TQrnStation` eager constructor then consumes duration ordinal 1545, whose
binary32 bits are `3dc788c6`, and amplitude ordinal 1546. It creates a
two-block, 1024-sample envelope with amplitude bits `48e976cf`. Direct
inspection of the live CE object and the replay agree that its only nonzero
envelope indexes are 359, 411, 848, 907, and 990, with respective binary32
bits `47c46069`, `4849d6e3`, `c7e5b614`, `476e2c1c`, and `c75e86f2`.
Every other envelope sample is positive zero.

The live CE station collection contains exactly one `TQrnStation` in
`stSending` after block one, with the full 1024-sample envelope still
retained after its first `GetBlock` call. During block two, `GetBlock`
consumes the remaining half, clears the envelope, and `Tick` routes
`evMsgSent` to `TQrnStation.ProcessEvent`, which frees the station. The
collection is empty after block two. The normalized block hashes are
`41096894caafa0890ea1f5d18545aa14b644ed1e9f20aff31f9f8fcec75d960e`
and
`44ae49f68a99e7688200685231b9cbf6c84414ae8a6b1b4736b78cce1d7d4637`;
their aggregate hash is
`bec466358e35bf3720c074c9fee0ea8e7ef8f656164b6dd48d5758da66c41f61`.
The fresh one-block next value at ordinal 2576 has bits `3f58ce2d`. The
continuous two-block next value at ordinal 4117 has bits `3e9fed0d`.

QRN burst stations are internal audio interference, not contest callers.
They must never appear in public `SessionSnapshot.ActiveStations`, change
`LastCaller`, participate in best-call matching, or emit caller and station
events. The parity adapter therefore requires public active-station snapshots
to remain empty at both block boundaries. The internal parity-only
`EngineSession.ObserveQrnBurstForParityAsync` seam reports burst count,
sending state, and retained envelope length. It accepts session ID at the
engine wrapper plus expected revision and expected simulation block, rejects
automatic timing and stale or mismatched boundaries, and neither advances
simulation nor consumes random values. It is not part of
`IMorseRunnerClient`, snapshots, gRPC, or any production UX contract.

The retained preimplementation evidence records an explicit empty internal
observation and first divergence at row three, `station-lifecycle`, with code
`audio-qrn-burst-station-lifecycle-mismatch`. Production now owns 22
preallocated `QrnBurstStation` instances. The bound follows from one possible
creation per block and the maximum
`Round(11025 / 512 * Single(Random)) = 22` block duration. Every activation
consumes the binary32 duration argument, the binary64 amplitude expression
with one final binary32 cast, then all envelope trigger and replacement draws
eagerly. Reused active envelope samples are cleared before decisions are
applied, so pooled storage has the same zero initialization as CE
`SetLength`.

The session keeps one chronological internal receiver-source order for
callers and QRN bursts. Caller creation reserves capacity for all 22 possible
bursts, so a burst trigger, mix, and release do not allocate. A new burst is
appended only after its eager constructor completes, mixes into the real
complex component in its trigger block with zero intrinsic BFO, and advances
one complete compatibility block even when local non-QSK audio later hides
the receiver. The full envelope length and sending state remain observable
through the final mix. The post-render reverse cleanup releases the pooled
station only after its completed block reaches the audio sink, matching CE's
`GetBlock` followed by `Tick` boundary. Public caller snapshots and caller
events continue to use only the normal station collection.

The production target now matches all nine pinned CE rows: the internal count
and retained envelope after block one, removal after block two, both exact
receiver hashes, the aggregate hash, and terminal ordinals 2576 and 4117.
The retained red artifacts remain immutable evidence of the former missing
behavior.

The incremental QRN hot-path gate uses a preallocated 22-station test harness
with the production burst primitive and stable `List.RemoveAt` ordering. After
256 warmup blocks, 4096 measured blocks each begin with 21 maximum-duration
bursts, activate the twenty-second from seed 1989, mix all 22 into one
512-sample complex block, and release the completed oldest burst. The Release
gate requires zero measured allocation, p99 below 11.6 ms, and the p99.9
nearest-rank normal maximum below 23.2 ms. It retains the raw maximum for
audit without treating an operating-system deschedule as render work.
Opt-in evidence capture records the clean source revision and tree, hardware,
OS, runtime, complete sorted tick distribution, thresholds, and result after
the measured region. This scoped gate measures the QRN increment only. It
does not replace the pending release-wide all-effects and 30-minute underrun
gates in Section 21.

The first retained Windows x64 capture is
`tests/MorseRunner.Engine.Tests/PerformanceEvidence/audio-qrn-burst-hot-path.windows-x64.v1.json`.
It measures clean revision `223eceb7cd47732f093c244cd8b0b4eb0701b545`
on .NET 10.0.8 and an Intel Core i9-10980XE. Across 4096 blocks it records
zero managed allocation, 0.3532 ms p99, 0.3832 ms p99.9 normal maximum, and
a 5.7338 ms raw maximum. This is development evidence for one Windows
machine, not cross-platform release certification.

This vector certifies one successful eager construction, exact sparse
envelope, same-block audibility, two-block lifetime, destruction, and
continuous shared-stream order. It does not cover other seeds, the full
duration and amplitude distributions, zero- or long-duration boundaries,
overlapping QRN bursts, interactions with normal or QRM stations, or runtime
QRN toggling. The `audio.qrn-impulses-and-burst-stations` obligation remains
partial.

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
- QRN sparse background impulses and the pinned seed-1903 two-block burst now
  use the CE shared-stream draw order, real-component replacement semantics,
  pre-filter receiver stage, eager burst construction, same-block mixing, and
  post-render lifetime boundary. Other burst durations, overlapping bursts,
  caller and QRM interaction, runtime QRN toggling, and RIT rotation remain
  uncertified. The retained first-CQ QSK red vector records the former
  post-receiver sidetone divergence. Production now matches its pre-filter
  complex-channel mixing, first-block QSK response, and shared-random
  checkpoint exactly. Later QSK recovery remains uncertified. LID paths still use
  deterministic XPlat behavior rather than certified CE behavior. Production
  callers now own private CE-style QSB
  processors and construction draws. The session loop owns the mutable QSB
  flag, applies it at station render boundaries, and publishes it through
  in-process and gRPC snapshots. The retained seed-12345 runtime-toggle case
  certifies the first positive per-station envelope transition. Independent
  multi-station evolution, the disable-after-enable path, UI mutation, and
  flutter remain incomplete. Positive QRM now
  uses pooled CE-style station construction and
  same-block receiver mixing for the first-trigger boundary described above,
  while retry/lifetime, overlap, normal-caller interaction, RIT, QSK, and
  runtime toggling remain partial or uncertified. The audit found further
  differences in audio ordering, signal models, remaining random-source
  ownership, and cross-feature draw ordering, so these broader paths are not
  CE-equivalent yet.
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
- The Avalonia duration editor accepts and persists every whole-minute CE value
  from 1 through 240. Values loaded or assigned outside that range clamp at the
  corresponding CE boundary before session creation.
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
