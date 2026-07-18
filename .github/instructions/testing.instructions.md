# Testing Instructions

- Write the shared legacy/XPlat acceptance case before production porting.
- Prove the case passes the pinned legacy target.
- Prove the unchanged case fails the unimplemented XPlat behavior.
- Implement until XPlat passes without weakening the case.
- Require 100 percent of the parity manifest with zero skipped, waived,
  quarantined, disabled, expected-failure, or unimplemented cases for release.
- Use explicit seeds and fake clocks for simulation tests.
- Do not use `Thread.Sleep` or arbitrary `Task.Delay` for synchronization.
- Test engine behavior directly unless transport is under test.
- Test in-process and gRPC clients with shared scenario vectors.
- Preserve legacy comparisons as golden fixtures with provenance.
- Separate exact integer and state assertions from tolerance-based floating
  point assertions.
- Test cancellation, disposal, slow subscribers, queue saturation, reconnect,
  resync, and control lease expiry.
- Add regression coverage with every defect fix.
- Keep test output diagnostic enough to reproduce the seed and first divergent
  block.
