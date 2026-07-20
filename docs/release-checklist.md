# MorseRunnerXPlat release checklist

## Current release status

- Release is blocked. The 24 broad schema-v3 capabilities are not yet complete.
- `contest.exchange-shapes` is currently an authored
  `legacy-green-xplat-red` case pending certifying evidence and implementation.
- The former 1,501-surface and 20/20 report is invalidated historical
  scaffolding. Its 25 schema-v1 records are noncertifying and cannot satisfy a
  checklist row.
- Packaging, a passing unit-test suite, or an offline fixture run does not
  establish a complete CE port.

## Automated gates

- Restore, formatting, Release build, and all tests pass.
- Buf lint passes. Buf breaking passes when a prior contract exists on `main`.
- `Test-ParityCompleteness.ps1` validates schema version 3, exact capability and
  case bindings, and monotonic promotion from the merge-base manifest.
- The pinned inventory classifies all 143 tracked CE files exactly once. It
  inventories the 131 functional, build, resource, integration, data, and test
  inputs and permits only the 12 reviewed narrow nonfunctional exclusions.
- All 3,668 discovered legacy surfaces map to exactly one of the 24 current
  capabilities, with no stale, duplicate, omitted, or invented surface.
- Every capability is `complete`, every active behavioral case is
  `both-green`, and every required surface-platform assignment has retained
  both-green coverage.
- `Run-Parity.ps1 -Target Both -Mode Release` executes clean live Legacy and
  XPlat targets in separate Windows test processes and reports zero functional
  divergences, not-runnable outcomes, gaps, skips, waivers, quarantines,
  disables, expected failures, and unimplemented cases.
- Each result records platform, process architecture, runtime and framework,
  clean XPlat revision and tree, and, for Legacy, the clean pinned CE revision
  and tree plus fresh oracle and provenance hashes.
- Every target run retains a correlated TRX and execution envelope. The
  envelope binds the exact test identities, selected case IDs, raw result and
  TRX hashes, process exit code, platform, process architecture, runtime
  identifier, revision, tree, and cleanliness.
- The live Legacy provenance and content-addressed build registry verify the
  reference definition, bundle, versioned oracle source and recipe, executable,
  build script, complete 14,553-file consumed-toolchain fingerprint, exact
  ordered compiler invocation, and every supported scenario observation.
- Case definitions, fixtures, canonical observed values, run documents, and
  provenance hashes recompute and match the manifest evidence.
- Every both-green promotion retains the original red proof and green XPlat
  proof from the same clean first-green commit on Windows, Linux, and macOS.
  Each platform also retains a complete XPlat Development run, and Windows
  retains a complete `Both` run at that revision, before selected-case capture.
  Candidate manifest, history, evidence, and report validation completes before
  one rollback-safe, no-overwrite transaction.
- No fixture-backed observation is counted as a live legacy execution.
- No retained schema-v1 observation is counted as release evidence.
- Vulnerable-package audit reports no known vulnerable packages.
- Runtime packages publish for Windows x64, Linux x64, macOS x64, and macOS
  arm64.

## Platform validation

- Run `Native Release Evidence` for `win-x64`, `linux-x64`, `osx-x64`, and
  `osx-arm64`; retain every `native-evidence-*` artifact with the release.
- Retain native XPlat parity evidence for every platform declared by each
  capability. The live Windows `Both` run establishes the CE comparison;
  native XPlat evidence establishes Windows, Linux, and macOS coverage and
  cannot replace that live comparison.
- Confirm each `evidence-manifest.json` identifies the expected native
  architecture and sets `platformComplete` to `true`.
- Launch the Avalonia application on a clean machine.
- Enumerate and play through a physical audio device.
- Record and inspect a WAV session.
- Complete the keyboard-only operator workflow.
- Save settings and results, restart, and verify both are retained.
- Upgrade a schema-version 1 settings file and verify values are preserved.
- Launch the standalone host, run `host-info`, and run `hosted-scenario`.
- Confirm host discovery is loopback-only and removed after graceful shutdown.
- Run a two-hour session without unbounded memory, handle, or subscriber growth.

The reproducible commands, artifact contents, and incomplete-hardware handling
are documented in `docs/release/native-evidence.md`. Publish success alone
does not satisfy any platform row. Missing hardware or a not-runnable adapter
keeps the row incomplete. It is not a waiver and must not be reclassified as a
functional divergence.

## Distribution review

- Verify `LICENSE`, `README.md`, and `THIRD-PARTY-NOTICES.md` are in every
  product directory.
- Verify canonical packaged-data filenames and hashes.
- Verify uninstalling application files does not remove user settings,
  recordings, diagnostics, or results.
- Complete Windows signing and macOS signing/notarization decisions before a
  public release.
- Complete the Linux packaging-format decision before a public release.
