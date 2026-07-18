# UI and UX Instructions

- Design keyboard-first workflows for send, entry, correction, start, and stop.
- Keep focus transitions explicit and testable.
- Keep shortcuts discoverable and conflict-aware.
- Keep view models behind `IMorseRunnerClient`.
- Keep contest validation, scoring, and station decisions in the engine.
- Enable Avalonia compiled bindings and declare `x:DataType`.
- Marshal observable state changes to the Avalonia UI thread.
- Coalesce high-rate snapshots before applying them to controls.
- Unit-test view models without Avalonia when possible.
- Use Avalonia headless tests only for controls, layout, focus, or input.
- Verify Windows, Linux, and macOS scaling and font behavior.
