---
name: avalonia-ux-change
description: Implement or review MorseRunnerXPlat Avalonia views, view models, keyboard interactions, focus, accessibility, styling, layout, or headless UI tests. Use for MorseRunner.App work and for shared UX behavior consumed through IMorseRunnerClient.
---

# Avalonia UX Change

1. Define the keyboard, pointer, focus, validation, and accessibility behavior.
2. Separate transient editing and focus state from semantic engine commands.
3. Consume `IMorseRunnerClient`. Do not call engine internals or generated gRPC
   clients from a view model.
4. Keep contest validation, scoring, station behavior, and timing in the engine.
5. Enable compiled bindings, declare `x:DataType`, and treat binding warnings as
   defects.
6. Marshal observable changes to the Avalonia UI thread and coalesce high-rate
   snapshots before updating controls.
7. Unit-test view models without Avalonia. Use headless tests for controls,
   layout, focus, or input.
8. Capture and inspect important states when repository capture tooling exists.
9. Check scaling, fonts, shortcuts, and focus on Windows, Linux, and macOS.
