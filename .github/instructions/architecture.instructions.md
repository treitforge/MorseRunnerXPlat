# Architecture Instructions

## Direction

MorseRunnerXPlat has one .NET engine and multiple possible UX clients.

```text
Avalonia / CLI / TUI
        |
 IMorseRunnerClient
    /          \
in-process    gRPC
    \          /
  Engine application service
            |
  single-owner session loop
            |
   renderer and audio sink
```

## Rules

- Keep the normal Avalonia path in process.
- Keep gRPC optional and transport-only.
- Keep audio rendering and playback in the engine-hosting process.
- Keep mutable simulation state on one session loop.
- Apply commands at documented block boundaries.
- Publish immutable events and coalescible snapshots.
- Allow many observers but only one control lease.
- Keep Protobuf types out of domain, DSP, and engine projects.
- Keep UX state distinct from operator intent and engine state.
- Update `docs/architecture/engineering-specification.md` with behavioral or
  contract changes.
