# MorseRunnerXPlat engineering specification

Status: Active

## Purpose

MorseRunnerXPlat is a cross-platform, real-time Morse contest simulator. This document is the authoritative specification for its architecture, observable behavior boundaries, and quality requirements.

## Architecture

- `MorseRunner.Domain` contains immutable values, commands, events, and snapshots.
- `MorseRunner.Dsp` contains managed, allocation-conscious signal processing.
- `MorseRunner.Engine` owns simulation time, mutable session state, contest logic, and the `IAudioSink` port.
- `MorseRunner.Audio` implements physical, WAV, and null audio adapters.
- `MorseRunner.Client` exposes the transport-neutral application service used by every UX.
- Avalonia uses the in-process client by default. gRPC is an optional adapter and never a second engine.

The Domain, DSP, and Engine projects must not reference Avalonia, gRPC, generated Protobuf types, or concrete audio adapters. The process hosting the engine owns audio rendering and playback. Real-time PCM never crosses gRPC.

## Session and concurrency

- One session loop owns mutable simulation state.
- Commands are ordered and applied at documented simulation block boundaries.
- Events and snapshots are immutable. Subscriber queues are bounded, and slow observers cannot delay simulation.
- A session owns its seeded random source. Simulation behavior uses simulation time; wall-clock time is diagnostic only.
- UI and gRPC handlers send semantic commands and never mutate engine state directly.
- A station starts copying when an operator transmission begins, then evaluates the complete operator message only after that transmission finishes. Its reply delay begins at the completion boundary.
- QSO logging associates truth data only with a completed station that is an exact match or the highest-confidence acceptable near-match for the entered callsign. Ties choose the newest caller. An unrelated completed station remains active and cannot turn a new log entry into a callsign error.
- A newly logged QSO with an eligible live station is provisionally awaiting station confirmation, including when the operator logs before sending `TU`. The desktop log leaves its error result blank during this state. After the matching station finishes copying a later `TU` and reaches `Done`, the engine updates that same QSO with its truth data, final error, score, and station removal. A QSO with no eligible station remains `NIL`.

## Real-time audio

- Render callbacks perform no blocking work, file I/O, logging, RPC, UI dispatch, or asynchronous work.
- Avoid per-sample allocation, LINQ, boxing, reflection, and exceptions in DSP hot paths.
- Audio queues are bounded. Physical output telemetry includes queue depth, underruns, dropped blocks, and health.
- Recording is best-effort: recorder backpressure may drop recording blocks but must not interrupt a live session.

## UX

- Desktop and terminal clients preserve keyboard-first operation.
- Focus movement, shortcuts, accessibility, and transient editing belong to the UX.
- View models depend only on `IMorseRunnerClient` and marshal observable changes onto the UI thread.
- Avalonia uses compiled bindings and declares `x:DataType`.
- Callsign input and engine matching are case-insensitive. Clients display canonical uppercase callsigns.
- The QSO entry presents only contest-applicable received fields. ARRL Field Day records class and section, without a signal report; its QSO log and validation never render or compare RST.
- Once station confirmation provides truth data, a QSO preserves every callsign and exchange-field mismatch. The QSO log displays the CE-style ordered correction values, including every incorrect received field, rather than a single summary error category. Normal desktop status does not automatically select or identify an active caller as operator-entered station information.
- Desktop and terminal QSO logs use contest-specific headings and a dedicated Corrections column. In particular, Field Day presents Class, Sect, and Corrections, without an empty RST column.
- Each normal interactive desktop or terminal session receives a fresh random seed, matching CE's varied normal runs. The selected seed remains in the session snapshot, results, and available diagnostics so a scenario can be replayed with an explicit seed through automated clients.
- Desktop and terminal clients persist changed operator and contest preferences before a normal application shutdown completes. The main desktop window defers closing until that write finishes.
- A successfully started session clears the prior QSO entry and focuses the callsign field.
- Debug desktop builds provide a keyboard-only session trace. It reports caller and reply events, exact operator messages, pending QSO confirmation, and final QSO truth-data comparisons without changing release behavior. The trace is copied as JSON on demand and persisted atomically under the results directory while the Debug session runs.

## Transport

- Protobuf is the source of truth for the optional external transport only.
- Mutating commands have a request ID and session ID.
- Updates include engine epoch, session ID, sequence, revision, and simulation block where applicable.
- Multiple clients may observe a session, but only one holds the control lease.

## Quality requirements

- Add focused deterministic tests for behavior changes and error paths.
- Test engine behavior directly unless transport is under test.
- Use numeric vectors and benchmarks for DSP changes, shared client vectors for gRPC changes, and headless or visual tests for UX changes.
- Release validation requires restore, format verification, Release build, complete solution tests, and platform product checks described in the release checklist.

## Definition of done

A feature is complete when its observable acceptance criteria and focused tests pass, dependency direction is preserved, real-time constraints are maintained, affected clients and transport mappings are validated, and this specification is updated for architectural or behavioral changes.
