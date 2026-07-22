---
name: run-quality
description: Select and run the correct MorseRunnerXPlat validation for documentation, .NET, Protobuf, engine, DSP, transport, Avalonia, performance, or cross-platform changes. Use when asked to build, test, validate, prepare a PR, or diagnose CI scope.
---

# Run Quality

1. Inspect the changed paths and repository status.
2. Synchronize the repository-local Python tooling and validate YAML metadata:

   ```powershell
   uv sync --locked
   uv run --locked python tools\agent_scaffolding\validate_yaml.py
   ```

3. Always run:

   ```powershell
   .\.github\hooks\scripts\validate-agent-scaffolding.ps1
   ```

4. When `MorseRunnerXPlat.slnx` exists, run restore, formatting verification,
   Release build, and affected tests. Expand to the full solution before handoff
   for cross-cutting changes.
5. When `buf.yaml` exists and Protobuf changed, run `buf lint` and `buf breaking`.
6. Run seeded scenarios for engine changes, numeric vectors and benchmarks for
   DSP changes, shared client vectors for gRPC changes, and headless or visual
   checks for Avalonia changes.
7. Run affected operating-system jobs for platform-specific changes.
8. Report exact commands, results, skipped gates, and missing prerequisites.

Do not claim a gate passed when its project or tool does not yet exist.
