# Station-response comparison harness

This experimental harness compares one completed operator transmission against
the legacy CE `DxStn`/`DxOper` lifecycle and the XPlat `SimulatedOperator`.
It reports parity for the resulting operator state and normalized response kind:
`call`, `call-correction`, `exchange`, `exchange-request`, or `none`.

It intentionally does not compare audio, station selection, send delays, or
the exact random text variant. Those concerns have separate tests.

Build both probes once:

```powershell
dotnet build tools\MorseRunner.ParityProbe\MorseRunner.ParityProbe.csproj --configuration Release -warnaserror
..\MorseRunner\Lazarus\build.ps1 -BuildMode Debug
```

Run the scenario matrix:

```powershell
.\tools\parity\Compare-StationResponses.ps1
```

The script passes the CE repository directory as CE's first positional
argument. CE uses that historical convention to locate `DXCC.LIST`; users
should not invoke the experimental CE probe without that argument.

To rebuild CE within the run, use `-BuildLegacy`. Use `-KeepTraces` to retain
the per-scenario CE JSON output in a temporary directory.

To manually inspect the XPlat probe:

```powershell
dotnet run --project tools\MorseRunner.ParityProbe --configuration Release -- `
  --call WA2WDT --initial need-qso `
  --copied-call WA2W --messages his-call,number --seed 24680
```
