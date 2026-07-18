---
name: feature-implementation
description: Implements a scoped MorseRunnerXPlat feature as a tested vertical slice.
---

# Feature Implementation

1. Read `AGENTS.md` and the relevant engineering specification sections.
2. Define observable acceptance criteria and legacy compatibility expectations.
3. Write the cross-implementation acceptance case and prove legacy green and
   unimplemented XPlat red.
4. Identify the owning layer and required adapter changes.
5. Implement the smallest complete vertical slice until XPlat is green without
   weakening the acceptance case.
6. Add deterministic tests at the lowest useful layer.
7. Validate affected platforms and transport parity where applicable.
8. Update the manifest and specification.
