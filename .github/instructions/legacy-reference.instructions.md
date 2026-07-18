# Legacy MorseRunner Reference Instructions

The adjacent `..\MorseRunner` repository is the functional reference
implementation for MorseRunnerXPlat.

Full 1:1 functional parity is mandatory. A parity release has zero missing
features and zero functional gaps.

## Use it to establish

- Contest selection, rules, exchanges, multipliers, scoring, and duration.
- Station and operator state transitions.
- CW message composition, timing, speed, and Farnsworth behavior.
- DSP stages, noise, QRN, QRM, QSB, flutter, filtering, RIT, QSK, and AGC.
- Logging, correction, rate, score, results, configuration, and data-file
  semantics.
- Keyboard workflows and other observable operator behavior.

## Do not copy

- Process-global `Tst`, `Ini`, `QsoList`, or form instances.
- Engine calls into UI controls.
- Simulation advancement from a UI-thread audio callback.
- WinMM, `waveOut`, or other Windows-only device assumptions.
- Nondeterministic tests or duplicated test-only algorithms.

## Parity evidence

- Inventory every observable legacy capability in the machine-readable parity
  manifest.
- Write cross-implementation acceptance tests before porting production code.
- Prove each new case passes against legacy MorseRunner.
- Prove the same case fails against XPlat while the feature is still missing.
- Implement until the XPlat runner passes the unchanged case.
- Require 100 percent XPlat pass rate with no skipped, waived, quarantined,
  expected-failure, or unimplemented cases.
- Record the legacy revision and source location for every golden fixture.
- Prefer seeded or captured-decision headless scenarios.
- Compare the first divergent block or state transition.
- Visual treatment may change, but functional workflows and outcomes may not be
  removed or waived.
