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
