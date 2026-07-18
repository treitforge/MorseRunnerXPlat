# MorseRunnerXPlat parity report

Generated from `parity-manifest.json` and `legacy-surface-inventory.json`. Do not edit by hand.

## Inventory

- Inventory status: `complete`
- Pinned legacy revision: `55bbd019c29d8cf693184ea420a17a253f16fe1e`
- Discovered legacy surfaces: 1501
- Mapped legacy surfaces: 1501
- Unmapped legacy surfaces: 0
- Pending audit surfaces: 0

| Category | Discovered surfaces |
|---|---:|
| `contest-definition` | 12 |
| `contest-enumeration` | 12 |
| `data-file-reference` | 16 |
| `keyboard-branch` | 41 |
| `keyboard-shortcut` | 12 |
| `log-error-code` | 17 |
| `log-routine` | 30 |
| `main-event-binding` | 162 |
| `main-event-handler` | 67 |
| `main-form-object` | 79 |
| `main-menu-item` | 129 |
| `operational-path` | 49 |
| `persisted-setting` | 60 |
| `qso-field` | 30 |
| `run-mode` | 5 |
| `score-column` | 25 |
| `simulation-enum` | 48 |
| `simulation-routine` | 93 |
| `state-transition` | 18 |
| `support-contest-property` | 1 |
| `support-contest-routine` | 189 |
| `support-contest-type` | 25 |
| `support-data-constant` | 3 |
| `support-data-enum` | 28 |
| `support-data-property` | 3 |
| `support-data-routine` | 52 |
| `support-data-type` | 23 |
| `support-effect-property` | 1 |
| `support-effect-routine` | 13 |
| `support-effect-type` | 1 |
| `support-ui-routine` | 4 |
| `support-ui-type` | 1 |
| `vcl-adapter-property` | 26 |
| `vcl-adapter-routine` | 53 |
| `vcl-adapter-type` | 6 |
| `vcl-dsp-constant` | 6 |
| `vcl-dsp-property` | 21 |
| `vcl-dsp-routine` | 72 |
| `vcl-dsp-type` | 32 |
| `vcl-ui-property` | 11 |
| `vcl-ui-routine` | 23 |
| `vcl-ui-type` | 2 |

## Acceptance progress

- Manifest capabilities: 20
- Acceptance cases authored: 20
- Inventory-only capabilities: 0
- Both-green: 20
- Legacy-green/XPlat-red: 0
- Skipped, waived, quarantined, disabled, or expected-failure: 0

| Parity ID | Feature | Status | Legacy | XPlat | Mapped surfaces | Legacy source |
|---|---|---|---|---|---:|---|
| `catalog.contest-enumeration` | Legacy contest enumeration | `both-green` | `pass` | `pass` | 12 | `Ini.pas:28-30` |
| `session.run-mode-enumeration` | Legacy run mode enumeration | `both-green` | `pass` | `pass` | 5 | `Ini.pas:31` |
| `catalog.contest-definitions` | Legacy contest definitions | `both-green` | `pass` | `pass` | 12 | `Ini.pas:99-222` |
| `configuration.persisted-settings` | Legacy persisted settings | `both-green` | `pass` | `pass` | 60 | `Ini.pas:345-548` |
| `ux.main-form-objects` | Legacy main-form objects | `both-green` | `pass` | `pass` | 79 | `Main.dfm:1-1988` |
| `ux.main-menu-commands` | Legacy main-menu commands | `both-green` | `pass` | `pass` | 129 | `Main.dfm:964-1579` |
| `ux.main-form-events` | Legacy main-form event bindings and handlers | `both-green` | `pass` | `pass` | 229 | `Main.dfm:1-1988`<br>`Main.pas:452-2867` |
| `ux.keyboard-workflows` | Legacy shortcuts and keyboard branches | `both-green` | `pass` | `pass` | 53 | `Main.dfm:1000-1579`<br>`Main.pas:629-947` |
| `logging.qso-model` | Legacy QSO record and error model | `both-green` | `pass` | `pass` | 47 | `Log.pas:48-82` |
| `logging.scoring-rate-and-results` | Legacy logging, scoring, rate, correction, and result paths | `both-green` | `pass` | `pass` | 55 | `Log.pas:147-1137` |
| `simulation.state-models` | Legacy simulation state models and transitions | `both-green` | `pass` | `pass` | 66 | `Contest.pas`<br>`Station.pas`<br>`DxOper.pas`<br>`DxStn.pas`<br>`StnColl.pas`<br>`MyStn.pas`<br>`QrmStn.pas`<br>`QrnStn.pas` |
| `simulation.runtime-routines` | Legacy contest, station, and operator routines | `both-green` | `pass` | `pass` | 93 | `Contest.pas`<br>`Station.pas`<br>`DxOper.pas`<br>`DxStn.pas`<br>`StnColl.pas`<br>`MyStn.pas`<br>`QrmStn.pas`<br>`QrnStn.pas` |
| `audio-dsp.legacy-processing` | Legacy portable keying and DSP processing | `both-green` | `pass` | `pass` | 131 | `VCL/Crc32.pas`<br>`VCL/FarnsKeyer.pas`<br>`VCL/Mixers.pas`<br>`VCL/MorseKey.pas`<br>`VCL/MorseTbl.pas`<br>`VCL/MovAvg.pas`<br>`VCL/QuickAvg.pas`<br>`VCL/SndTypes.pas`<br>`VCL/VolumCtl.pas` |
| `audio.legacy-adapters` | Legacy sound output, buffering, and WAV adapters | `both-green` | `pass` | `pass` | 85 | `VCL/BaseComp.pas`<br>`VCL/SndCustm.pas`<br>`VCL/SndOut.pas`<br>`VCL/WavFile.pas` |
| `ux.legacy-vcl-components` | Legacy VCL-only hint and volume controls | `both-green` | `pass` | `pass` | 36 | `VCL/PermHint.pas`<br>`VCL/VolmSldr.pas` |
| `contest.legacy-implementations` | Legacy contest-specific implementations | `both-green` | `pass` | `pass` | 215 | `ACAG.pas`<br>`ALLJA.pas`<br>`ArrlDx.pas`<br>`ArrlFd.pas`<br>`ArrlSS.pas`<br>`CqWpx.pas`<br>`CqWW.pas`<br>`CWOPS.pas`<br>`CWSST.pas`<br>`DualExchContest.pas`<br>`IaruHf.pas`<br>`NaQp.pas` |
| `data.legacy-parsers` | Legacy call, prefix, exchange, and serial data parsers | `both-green` | `pass` | `pass` | 109 | `CallLst.pas`<br>`DXCC.pas`<br>`ExchFields.pas`<br>`SerNRGen.pas`<br>`Util/ArrlSections.pas`<br>`Util/CallsignUtils.pas`<br>`Util/Lexer.pas`<br>`Util/SSExchParser.pas` |
| `simulation.legacy-effects` | Legacy QSB and random effects | `both-green` | `pass` | `pass` | 15 | `Qsb.pas`<br>`RndFunc.pas` |
| `ux.score-dialog` | Legacy score dialog | `both-green` | `pass` | `pass` | 5 | `ScoreDlg.pas` |
| `data.files-and-operational-paths` | Legacy data files, recording, export, and failure paths | `both-green` | `pass` | `pass` | 65 | `Ini.pas`<br>`Main.pas`<br>`Log.pas`<br>`Station.pas`<br>`VCL`<br>`contest and data support units` |

## Pending completeness audits

