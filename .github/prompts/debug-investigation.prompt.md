---
name: debug-investigation
description: Investigates a MorseRunnerXPlat defect with a root-cause-first workflow.
---

# Debug Investigation

1. Record expected and actual behavior.
2. Reproduce with a seed and command trace when simulation is involved.
3. Locate the fault domain: engine, DSP, device, persistence, transport, or UX.
4. Compare with the legacy Pascal implementation when relevant.
5. Identify root cause with concrete evidence.
6. Apply the smallest correct fix only when implementation is requested.
7. Add regression coverage and report remaining risk.
