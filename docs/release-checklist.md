# MorseRunnerXPlat release checklist

## Automated gates

- Restore, formatting, Release build, and the complete solution test suite pass.
- Buf lint and breaking checks pass when transport contracts change.
- Vulnerability and dependency review is complete.

## Product validation

- Launch Avalonia on Windows, Linux, and macOS.
- Exercise a physical audio device, recovery, and WAV recording.
- Complete the keyboard-only operator workflow in both desktop and terminal clients.
- Save settings and results, restart, and verify retention.
- Launch the standalone host, run `host-info`, and run `hosted-scenario`.
- Confirm discovery is loopback-only and removed after graceful shutdown.
- Run a sustained session without unbounded memory, handle, queue, or subscriber growth.

## Distribution review

- Verify `LICENSE`, `README.md`, and `THIRD-PARTY-NOTICES.md` are in every product directory.
- Verify packaged data, user-data locations, and uninstall behavior.
- Complete signing, notarization, and Linux packaging decisions before public release.
