# MorseRunnerXPlat release checklist

## Automated gates

- Restore, formatting, Release build, and all tests pass.
- Buf lint passes. Buf breaking passes when a prior contract exists on `main`.
- The complete dual-runner parity suite reports 20/20 both-green and zero gaps.
- The legacy inventory reports 1,501/1,501 mapped surfaces.
- Vulnerable-package audit reports no known vulnerable packages.
- Runtime packages publish for Windows x64, Linux x64, macOS x64, and macOS
  arm64.

## Platform validation

- Launch the Avalonia application on a clean machine.
- Enumerate and play through a physical audio device.
- Record and inspect a WAV session.
- Complete the keyboard-only operator workflow.
- Save settings and results, restart, and verify both are retained.
- Upgrade a schema-version 1 settings file and verify values are preserved.
- Launch the standalone host, run `host-info`, and run `hosted-scenario`.
- Confirm host discovery is loopback-only and removed after graceful shutdown.
- Run a two-hour session without unbounded memory, handle, or subscriber growth.

## Distribution review

- Verify `LICENSE`, `README.md`, and `THIRD-PARTY-NOTICES.md` are in every
  product directory.
- Verify canonical packaged-data filenames and hashes.
- Verify uninstalling application files does not remove user settings,
  recordings, diagnostics, or results.
- Complete Windows signing and macOS signing/notarization decisions before a
  public release.
- Complete the Linux packaging-format decision before a public release.
