# Basic parity human evaluation

This build is ready for a focused human evaluation of the everyday MorseRunner
operator workflow. It is a basic-parity candidate, not a claim of full
byte-for-byte or release-certifying CE parity.

## Launch

Build and launch the Avalonia client from the repository root:

```powershell
dotnet run --project src\MorseRunner.App\MorseRunner.App.csproj -c Release
```

For the keyboard-first terminal client:

```powershell
dotnet run --project src\MorseRunner.Tui\MorseRunner.Tui.csproj -c Release
```

## Evaluate the normal operator loop

1. Choose a contest, station call, sent exchange, run mode, and duration.
2. Start Pile-Up, Single Calls, WPX, and HST where applicable.
3. Exercise F1 through F8 and F12, Insert or semicolon, Enter, and the QSO
   completion punctuation (`.`, `,`, `+`, `[`).
4. Confirm valid QSOs, NILs, and duplicates appear in the log and score view.
5. Use F11 or Ctrl+W to clear entry state and return to the callsign field.
6. Use Escape to abort an operator message, then confirm Enter sends the call
   and exchange again.
7. Restart each client and confirm the selected contest's sent exchange and
   normal settings persist.

## Deliberately deferred from this handoff

- Full legacy-oracle certification, cross-platform native captures, and exact
  audio/DSP hash parity.
- CE's contest-specific received-entry layouts and all input substitution,
  spacing, and paste behavior.
- Detailed correction/truth presentation, score history, and automatic
  competition-result flows.
- Exact audio behavior for RIT, bandwidth, QSK, QSB, QRM, QRN, recording, and
  physical-device edge cases.

Record any visible workflow that blocks normal operation as a human-evaluation
finding. The full gap inventory remains in
`docs/ux/legacy-compatibility-matrix.md`.
