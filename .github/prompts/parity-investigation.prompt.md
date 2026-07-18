---
name: parity-investigation
description: Compares the .NET implementation against legacy MorseRunner and isolates functional gaps that must be closed.
---

# Behavioral Parity Investigation

1. Select a narrow capability from the parity manifest.
2. Write an unchanged acceptance case for both implementation adapters.
3. Capture Pascal inputs, seed or decisions, timing, and outputs.
4. Prove legacy green.
5. Prove unimplemented or divergent XPlat red.
6. Normalize only nonfunctional platform and transport metadata.
7. Locate the first divergent block, command, event, score, QSO, or sample.
8. Preserve the case as a regression vector.
9. Do not waive, skip, or quarantine the case.
