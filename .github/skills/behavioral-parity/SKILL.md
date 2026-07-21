---
name: behavioral-parity
description: Compare MorseRunnerXPlat behavior with the legacy Pascal MorseRunner implementation. Use when porting contests, scoring, station state machines, CW generation, noise and propagation effects, configuration, logs, timing, or when a compatibility regression is suspected.
---

# Behavioral Parity

1. Select one narrow manifest capability and locate its Pascal implementation
   under `..\MorseRunner`.
2. Write one acceptance case that can run through both the legacy and XPlat
   adapters.
3. Record source revision, settings, seed or captured random decisions,
   commands, and expected output.
4. Capture block-numbered events, state, score, QSO records, and numeric or
   audio output as relevant.
5. Prove the case passes against legacy.
6. Before implementation, prove it fails against XPlat for the expected missing
   behavior.
7. Implement without changing the acceptance expectation until XPlat passes.
8. Normalize only paths, wall-clock timestamps, engine epoch, and transport
   metadata that cannot affect functional behavior.
9. Find the first divergence rather than comparing only the final result.
10. Preserve the vector and provenance as a regression fixture and update the
    manifest.

Author related narrow cases as one coherent batch when they can share an
immutable oracle version. Promote the batch's red evidence atomically so the
fresh Legacy build and build-integration gate cover only those selected cases.
After implementing the batch, run the complete applicable Legacy and XPlat
suites before green capture or handoff. Do not treat focused red promotion as
release certification.

Treat Pascal as the behavioral oracle, not the target architecture. Do not copy
its global state, form coupling, or Windows audio APIs.

Full parity is 100 percent of the manifest with no skipped, waived,
quarantined, expected-failure, or unimplemented cases.
