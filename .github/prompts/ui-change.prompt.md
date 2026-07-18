---
name: ui-change
description: Implements an Avalonia or terminal UX change without moving domain behavior into the UX.
---

# UX Change

1. Define the keyboard and pointer interaction.
2. Identify the semantic command or snapshot field involved.
3. Keep text editing, focus, and presentation state in the UX.
4. Keep contest and scoring decisions in the engine.
5. Use compiled Avalonia bindings and accessible labels.
6. Add view-model tests and headless control tests where appropriate.
7. Capture and inspect the affected state when tooling exists.
