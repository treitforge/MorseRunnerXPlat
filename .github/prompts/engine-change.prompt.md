---
name: engine-change
description: Changes simulation, contest, station, scoring, command, event, or snapshot behavior safely.
---

# Engine Change

1. Identify the authoritative engine invariant being changed.
2. Capture legacy or existing .NET behavior before editing.
3. Define command ordering and snapshot or event effects.
4. Keep all mutable state on the session loop.
5. Add seeded scenario coverage.
6. Verify in-process behavior first.
7. Verify gRPC mapping only if the public client contract changed.
