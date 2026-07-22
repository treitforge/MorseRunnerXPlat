---
name: engine-change
description: Implement or review MorseRunnerXPlat simulation, contest, station, scoring, session command, event, and snapshot changes. Use for work in MorseRunner.Domain or MorseRunner.Engine and whenever observable contest behavior or engine state transitions change.
---

# Engine Change

1. Read `AGENTS.md` and the relevant sections of
   `docs/architecture/engineering-specification.md`.
2. State the invariant and observable behavior being changed.
3. Inspect the affected simulation and client behavior.
4. Write focused deterministic acceptance tests before changing observable
   engine behavior.
5. Define command input, block-boundary ordering, state transition, emitted
   events, and snapshot effects before editing.
6. Keep mutable state on the session loop. Do not mutate it from UI, gRPC, or
   timer callbacks.
7. Add seeded unit or scenario tests that report the seed and first divergent
   block on failure.
8. Run direct engine tests first. Run shared transport vectors if the semantic
   client surface changed.
9. Update the engineering specification when behavior, contracts, or invariants
   changed.

Do not place contest rules, scoring, station decisions, or simulation time in a
UX project. Do not introduce generated Protobuf types into the domain or engine.
Do not skip required quality checks.
