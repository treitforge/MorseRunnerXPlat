# Human evaluation

Use this guide to evaluate the everyday operator experience.

## Launch

```powershell
dotnet run --project src\MorseRunner.App\MorseRunner.App.csproj -c Release
dotnet run --project src\MorseRunner.Tui\MorseRunner.Tui.csproj -c Release
```

## Normal operator loop

1. Choose a contest, station call, sent exchange, run mode, and duration.
2. Start Pile-Up, Single Calls, WPX, and HST where applicable.
3. Exercise F1 through F8 and F12, Insert or semicolon, Enter, and QSO completion punctuation.
4. Confirm valid QSOs, NILs, and duplicates appear in the log and score view.
5. Clear entry state, abort an operator message, and verify focus and follow-on entry behavior.
6. Restart each client and confirm normal settings and selected contest exchange persist.
7. Verify audio remains responsive while recording and that degraded audio status is visible if output blocks drop.

Record any workflow that blocks normal operation as a human-evaluation finding.
