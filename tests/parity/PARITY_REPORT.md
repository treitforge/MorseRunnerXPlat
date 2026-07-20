# MorseRunnerXPlat parity report

Generated from validated manifest, fixture, and evidence records. Do not edit by hand.

## Evidence policy

- Active evidence schema: `2`
- Active case totals below are derived from evidence whose fixture and observed-value hashes were recomputed.
- `observedValuesSha256` hashes a compact UTF-8 JSON string array with no BOM, whitespace, or trailing newline.
- `firstGreenCommit` is the first XPlat code revision demonstrated green by the retained run. It must equal the run-context revision, be reachable from HEAD, and have the retained run-context tree.
- Cases may overlap when independent behavior vectors exercise the same CE surface. Overlap counts remain visible for review.
- Complete capabilities require both-green coverage for every mapped legacy surface on every declared capability platform.
- Retained legacy-v1 noncertifying observations: 25
- Retained legacy-v1 observations are provenance only. They do not count toward release parity.

## Inventory

- Inventory status: `complete`
- Pinned legacy revision: `55bbd019c29d8cf693184ea420a17a253f16fe1e`
- Discovered legacy surfaces: 3668
- Mapped legacy surfaces: 3668
- Unmapped legacy surfaces: 0
- Pending audit surfaces: 0
- Overlapping case surface/platform assignments: 0

| Category | Discovered surfaces |
|---|---:|
| `bundled-application-resource` | 14 |
| `bundled-data-file` | 14 |
| `bundled-documentation` | 2 |
| `contest-definition` | 12 |
| `contest-enumeration` | 12 |
| `data-file-reference` | 16 |
| `data-parser-path` | 16 |
| `distribution-file` | 15 |
| `external-integration` | 29 |
| `form-event-binding` | 170 |
| `form-keyboard-shortcut` | 12 |
| `form-object` | 224 |
| `form-resource-reference` | 4 |
| `keyboard-branch` | 41 |
| `keyboard-shortcut` | 12 |
| `legacy-smoke-test` | 3 |
| `legacy-test-case` | 897 |
| `legacy-test-commented-case` | 27 |
| `legacy-test-declaration` | 20 |
| `legacy-test-disabled-case` | 64 |
| `legacy-test-disabled-declaration` | 4 |
| `legacy-test-disabled-method` | 4 |
| `legacy-test-disabled-registration` | 1 |
| `legacy-test-fixture` | 10 |
| `legacy-test-lifecycle` | 19 |
| `legacy-test-method` | 19 |
| `legacy-test-registration` | 9 |
| `log-error-code` | 17 |
| `log-routine` | 30 |
| `main-event-binding` | 162 |
| `main-event-handler` | 67 |
| `main-form-object` | 79 |
| `main-menu-item` | 129 |
| `operational-path` | 49 |
| `persisted-setting` | 60 |
| `project-configuration` | 14 |
| `project-definition` | 11 |
| `project-lifecycle` | 14 |
| `project-member-reference` | 2 |
| `project-resource-reference` | 14 |
| `project-unit-reference` | 146 |
| `qso-field` | 30 |
| `repository-build-input` | 25 |
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
| `third-party-regex-constant` | 87 |
| `third-party-regex-property` | 54 |
| `third-party-regex-routine` | 163 |
| `third-party-regex-type` | 48 |
| `unit-lifecycle` | 12 |
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

- Manifest capabilities: 24
- Complete capabilities: 0
- Partially authored capabilities: 0
- Not-authored capabilities: 24
- Active acceptance cases: 0
- Evidence-certified both-green cases: 0
- Legacy-green/XPlat-red cases: 0
- Skipped, waived, quarantined, disabled, or expected-failure: 0

| Capability ID | Feature | Acceptance status | Cases | Mapped surfaces | Overlap assignments | Legacy source |
|---|---|---|---:|---:|---:|---|
| `catalog.contest-enumeration` | Legacy contest enumeration | `not-authored` | 0 | 12 | 0 | `Ini.pas:28-30` |
| `session.run-mode-enumeration` | Legacy run mode enumeration | `not-authored` | 0 | 5 | 0 | `Ini.pas:31` |
| `catalog.contest-definitions` | Legacy contest definitions | `not-authored` | 0 | 12 | 0 | `Ini.pas:99-222` |
| `configuration.persisted-settings` | Legacy persisted settings | `not-authored` | 0 | 60 | 0 | `Ini.pas:345-548` |
| `ux.main-form-objects` | Legacy main-form objects | `not-authored` | 0 | 79 | 0 | `Main.dfm:1-1988` |
| `ux.main-menu-commands` | Legacy main-menu commands | `not-authored` | 0 | 129 | 0 | `Main.dfm:964-1579` |
| `ux.main-form-events` | Legacy main-form event bindings and handlers | `not-authored` | 0 | 229 | 0 | `Main.dfm:1-1988`<br>`Main.pas:452-2867` |
| `ux.keyboard-workflows` | Legacy shortcuts and keyboard branches | `not-authored` | 0 | 53 | 0 | `Main.dfm:1000-1579`<br>`Main.pas:629-947` |
| `logging.qso-model` | Legacy QSO record and error model | `not-authored` | 0 | 47 | 0 | `Log.pas:48-82` |
| `logging.scoring-rate-and-results` | Legacy logging, scoring, rate, correction, and result paths | `not-authored` | 0 | 55 | 0 | `Log.pas:147-1137` |
| `simulation.state-models` | Legacy simulation state models and transitions | `not-authored` | 0 | 66 | 0 | `Contest.pas`<br>`Station.pas`<br>`DxOper.pas`<br>`DxStn.pas`<br>`StnColl.pas`<br>`MyStn.pas`<br>`QrmStn.pas`<br>`QrnStn.pas` |
| `simulation.runtime-routines` | Legacy contest, station, and operator routines | `not-authored` | 0 | 93 | 0 | `Contest.pas`<br>`Station.pas`<br>`DxOper.pas`<br>`DxStn.pas`<br>`StnColl.pas`<br>`MyStn.pas`<br>`QrmStn.pas`<br>`QrnStn.pas` |
| `audio-dsp.legacy-processing` | Legacy portable keying and DSP processing | `not-authored` | 0 | 131 | 0 | `VCL/Crc32.pas`<br>`VCL/FarnsKeyer.pas`<br>`VCL/Mixers.pas`<br>`VCL/MorseKey.pas`<br>`VCL/MorseTbl.pas`<br>`VCL/MovAvg.pas`<br>`VCL/QuickAvg.pas`<br>`VCL/SndTypes.pas`<br>`VCL/VolumCtl.pas` |
| `audio.legacy-adapters` | Legacy sound output, buffering, and WAV adapters | `not-authored` | 0 | 85 | 0 | `VCL/BaseComp.pas`<br>`VCL/SndCustm.pas`<br>`VCL/SndOut.pas`<br>`VCL/WavFile.pas` |
| `ux.legacy-vcl-components` | Legacy VCL-only hint and volume controls | `not-authored` | 0 | 36 | 0 | `VCL/PermHint.pas`<br>`VCL/VolmSldr.pas` |
| `contest.legacy-implementations` | Legacy contest-specific implementations | `not-authored` | 0 | 215 | 0 | `ACAG.pas`<br>`ALLJA.pas`<br>`ArrlDx.pas`<br>`ArrlFd.pas`<br>`ArrlSS.pas`<br>`CqWpx.pas`<br>`CqWW.pas`<br>`CWOPS.pas`<br>`CWSST.pas`<br>`DualExchContest.pas`<br>`IaruHf.pas`<br>`NaQp.pas` |
| `data.legacy-parsers` | Legacy call, prefix, exchange, and serial data parsers | `not-authored` | 0 | 477 | 0 | `CallLst.pas`<br>`DXCC.pas`<br>`ExchFields.pas`<br>`SerNRGen.pas`<br>`Util/ArrlSections.pas`<br>`Util/CallsignUtils.pas`<br>`Util/Lexer.pas`<br>`Util/SSExchParser.pas`<br>`ACAG.pas`<br>`ALLJA.pas`<br>`ArrlDx.pas`<br>`ArrlFd.pas`<br>`ArrlSS.pas`<br>`CqWW.pas`<br>`CWOPS.pas`<br>`CWSST.pas`<br>`IaruHf.pas`<br>`Main.pas`<br>`NaQp.pas`<br>`Test/SSLexerTest.pas` |
| `simulation.legacy-effects` | Legacy QSB and random effects | `not-authored` | 0 | 15 | 0 | `Qsb.pas`<br>`RndFunc.pas` |
| `ux.score-dialog` | Legacy score dialog | `not-authored` | 0 | 5 | 0 | `ScoreDlg.pas` |
| `data.files-and-operational-paths` | Legacy data files, recording, export, and failure paths | `not-authored` | 0 | 164 | 0 | `Ini.pas`<br>`Main.pas`<br>`Log.pas`<br>`Station.pas`<br>`VCL`<br>`contest and data support units`<br>`bundled data and application resource files`<br>`Lazarus/build.ps1`<br>`tools/make-install.sh`<br>`ACAG.pas`<br>`ALLJA.pas`<br>`ArrlDx.pas`<br>`ArrlFd.pas`<br>`ArrlSS.pas`<br>`CallLst.pas`<br>`CqWW.pas`<br>`CWOPS.pas`<br>`CWSST.pas`<br>`DXCC.pas`<br>`IaruHf.pas`<br>`NaQp.pas` |
| `ux.legacy-form-definitions` | Legacy Lazarus and Delphi form definitions | `not-authored` | 0 | 410 | 0 | `Main.lfm`<br>`Main.pas`<br>`ScoreDlg.dfm`<br>`ScoreDlg.lfm`<br>`ScoreDlg.pas` |
| `build.legacy-project-metadata` | Legacy project, package, and deployment metadata | `not-authored` | 0 | 201 | 0 | `Lazarus/tests/RegexSmokeTest.lpr`<br>`MRCE.groupproj`<br>`MorseRunner.deployproj`<br>`MorseRunner.dpr`<br>`MorseRunner.dproj`<br>`MorseRunner.lpi`<br>`MorseRunner.lpr`<br>`Test/UnitTests.dpr`<br>`Test/UnitTests.dproj`<br>`VCL/MorseRunnerVcl.dpk`<br>`VCL/MorseRunnerVcl.dproj` |
| `quality.legacy-tests-and-smoke` | Legacy unit and smoke test contracts | `not-authored` | 0 | 1077 | 0 | `Lazarus/tests/RegexSmokeTest.lpr`<br>`Test/CallsignUtilsTest.pas`<br>`Test/DxOperTest.pas`<br>`Test/DxccListTest.pas`<br>`Test/LexerTest.pas`<br>`Test/MySSExchTest.pas`<br>`Test/SSExchParserTest.pas`<br>`Test/SSLexerTest.pas` |
| `lifecycle.legacy-unit-hooks` | Legacy Pascal unit lifecycle hooks | `not-authored` | 0 | 12 | 0 | `ArrlFd.pas`<br>`Log.pas`<br>`Test/CallsignUtilsTest.pas`<br>`Test/DxOperTest.pas`<br>`Test/DxccListTest.pas`<br>`Test/LexerTest.pas`<br>`Test/MySSExchTest.pas`<br>`Test/SSExchParserTest.pas`<br>`Test/SSLexerTest.pas`<br>`Util/CallsignUtils.pas` |

## Active acceptance cases

- None.

## Retained noncertifying observations

- `audio-dsp.legacy-processing`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `audio.legacy-adapters`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `catalog.contest-definitions`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `catalog.contest-enumeration`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `configuration.persisted-settings`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `contest.cq-wpx-scoring`: A narrow pre-audit scoring vector retained for provenance; it is not mapped to a release-certifying manifest capability and does not establish full CQ WPX behavior.
- `contest.cwt-scoring`: A narrow pre-audit scoring vector retained for provenance; it is not mapped to a release-certifying manifest capability and does not establish full CWT behavior.
- `contest.legacy-implementations`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `contest.remaining-scoring`: A narrow pre-audit scoring vector retained for provenance; it is not mapped to a release-certifying manifest capability and does not establish complete behavior for the remaining contests.
- `data.files-and-operational-paths`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `data.legacy-parsers`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `logging.qso-model`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `logging.scoring-rate-and-results`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `session.run-mode-enumeration`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `simulation.legacy-effects`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `simulation.live-operator-session`: A narrow pre-audit operator vector retained for provenance; it is not mapped to a release-certifying manifest capability and does not establish full message timing or operator workflow parity.
- `simulation.live-station-session`: A narrow pre-audit station vector retained for provenance; it is not mapped to a release-certifying manifest capability and does not establish full caller timing, exchange, or station lifecycle parity.
- `simulation.runtime-routines`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `simulation.state-models`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `ux.keyboard-workflows`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `ux.legacy-vcl-components`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `ux.main-form-events`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `ux.main-form-objects`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `ux.main-menu-commands`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.
- `ux.score-dialog`: A pre-schema-v3 observation retained for provenance only; it did not execute or certify the full inventory capability.

## Pending completeness audits

- None.
