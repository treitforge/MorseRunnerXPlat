# MorseRunnerXPlat parity report

Generated from validated manifest, fixture, and evidence records. Do not edit by hand.

## Evidence policy

- Active evidence schema: `2`
- Active case totals below are derived from evidence whose fixture and observed-value hashes were recomputed.
- `observedValuesSha256` hashes a compact UTF-8 JSON string array with no BOM, whitespace, or trailing newline.
- `firstGreenCommit` is the first XPlat code revision demonstrated green by the retained run. It must equal the run-context revision, be reachable from HEAD, and have the retained run-context tree.
- Cases may overlap when independent behavior vectors exercise the same CE surface. Overlap counts remain visible for review.
- Complete capabilities require both-green coverage for every mapped legacy surface on every declared capability platform.
- Native GUI, physical-audio, performance, and experienced-user obligations remain structurally non-completable until typed, content-addressed artifact and sign-off evidence is implemented.
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
- Partially authored capabilities: 1
- Not-authored capabilities: 23
- Behavioral obligations: 118
- Source-bound obligations: 1
- Pending source bindings: 117
- Complete obligations: 0
- Partially authored obligations: 1
- Not-authored obligations: 117
- Rich-artifact evidence blockers: 0
- Active acceptance cases: 1
- Evidence-certified both-green cases: 0
- Legacy-green/XPlat-red cases: 1
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
| `contest.legacy-implementations` | Legacy contest-specific implementations | `partial` | 1 | 215 | 0 | `ACAG.pas`<br>`ALLJA.pas`<br>`ArrlDx.pas`<br>`ArrlFd.pas`<br>`ArrlSS.pas`<br>`CqWpx.pas`<br>`CqWW.pas`<br>`CWOPS.pas`<br>`CWSST.pas`<br>`DualExchContest.pas`<br>`IaruHf.pas`<br>`NaQp.pas` |
| `data.legacy-parsers` | Legacy call, prefix, exchange, and serial data parsers | `not-authored` | 0 | 477 | 0 | `CallLst.pas`<br>`DXCC.pas`<br>`ExchFields.pas`<br>`SerNRGen.pas`<br>`Util/ArrlSections.pas`<br>`Util/CallsignUtils.pas`<br>`Util/Lexer.pas`<br>`Util/SSExchParser.pas`<br>`ACAG.pas`<br>`ALLJA.pas`<br>`ArrlDx.pas`<br>`ArrlFd.pas`<br>`ArrlSS.pas`<br>`CqWW.pas`<br>`CWOPS.pas`<br>`CWSST.pas`<br>`IaruHf.pas`<br>`Main.pas`<br>`NaQp.pas`<br>`Test/SSLexerTest.pas` |
| `simulation.legacy-effects` | Legacy QSB and random effects | `not-authored` | 0 | 15 | 0 | `Qsb.pas`<br>`RndFunc.pas` |
| `ux.score-dialog` | Legacy score dialog | `not-authored` | 0 | 5 | 0 | `ScoreDlg.pas` |
| `data.files-and-operational-paths` | Legacy data files, recording, export, and failure paths | `not-authored` | 0 | 164 | 0 | `Ini.pas`<br>`Main.pas`<br>`Log.pas`<br>`Station.pas`<br>`VCL`<br>`contest and data support units`<br>`bundled data and application resource files`<br>`Lazarus/build.ps1`<br>`tools/make-install.sh`<br>`ACAG.pas`<br>`ALLJA.pas`<br>`ArrlDx.pas`<br>`ArrlFd.pas`<br>`ArrlSS.pas`<br>`CallLst.pas`<br>`CqWW.pas`<br>`CWOPS.pas`<br>`CWSST.pas`<br>`DXCC.pas`<br>`IaruHf.pas`<br>`NaQp.pas` |
| `ux.legacy-form-definitions` | Legacy Lazarus and Delphi form definitions | `not-authored` | 0 | 410 | 0 | `Main.lfm`<br>`Main.pas`<br>`ScoreDlg.dfm`<br>`ScoreDlg.lfm`<br>`ScoreDlg.pas` |
| `build.legacy-project-metadata` | Legacy project, package, and deployment metadata | `not-authored` | 0 | 201 | 0 | `Lazarus/tests/RegexSmokeTest.lpr`<br>`MRCE.groupproj`<br>`MorseRunner.deployproj`<br>`MorseRunner.dpr`<br>`MorseRunner.dproj`<br>`MorseRunner.lpi`<br>`MorseRunner.lpr`<br>`Test/UnitTests.dpr`<br>`Test/UnitTests.dproj`<br>`VCL/MorseRunnerVcl.dpk`<br>`VCL/MorseRunnerVcl.dproj` |
| `quality.legacy-tests-and-smoke` | Legacy unit and smoke test contracts | `not-authored` | 0 | 1077 | 0 | `Lazarus/tests/RegexSmokeTest.lpr`<br>`Test/CallsignUtilsTest.pas`<br>`Test/DxOperTest.pas`<br>`Test/DxccListTest.pas`<br>`Test/LexerTest.pas`<br>`Test/MySSExchTest.pas`<br>`Test/SSExchParserTest.pas`<br>`Test/SSLexerTest.pas` |
| `lifecycle.legacy-unit-hooks` | Legacy Pascal unit lifecycle hooks | `not-authored` | 0 | 12 | 0 | `ArrlFd.pas`<br>`Log.pas`<br>`Test/CallsignUtilsTest.pas`<br>`Test/DxOperTest.pas`<br>`Test/DxccListTest.pas`<br>`Test/LexerTest.pas`<br>`Test/MySSExchTest.pas`<br>`Test/SSExchParserTest.pas`<br>`Test/SSLexerTest.pas`<br>`Util/CallsignUtils.pas` |

## Behavioral obligations

| Obligation ID | Capability | Binding | Status | Cases | Platforms | Required behavior |
|---|---|---|---|---:|---|---|
| `catalog.contest-identifiers` | `catalog.contest-enumeration` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Every CE contest identifier is present in declaration order and selects the same contest family. |
| `catalog.contest-metadata-and-defaults` | `catalog.contest-definitions` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Every contest exposes CE-equivalent names, exchange types, defaults, and feature flags. |
| `session.run-mode-identifiers` | `session.run-mode-enumeration` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Every CE run mode is present and retains its observable selection semantics. |
| `quality.live-ce-oracle-required` | `quality.legacy-tests-and-smoke` | `pending` | `not-authored` | 0 | `windows` | Certified parity always executes the pinned CE oracle and never silently substitutes a committed fixture. |
| `quality.xplat-acceptance-target-executes` | `quality.legacy-tests-and-smoke` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | The dedicated XPlat parity target executes every applicable acceptance case exactly once. |
| `quality.ci-builds-pinned-ce` | `build.legacy-project-metadata` | `pending` | `not-authored` | 0 | `windows` | CI checks out the pinned clean CE revision and builds the hash-verified oracle before certification. |
| `quality.complete-replay-inputs-and-first-green` | `quality.legacy-tests-and-smoke` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Every case retains complete replay inputs, red evidence, and a reachable first-green commit. |
| `quality.manifest-evidence-reconciliation` | `quality.legacy-tests-and-smoke` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Manifest status, inputs, outputs, evidence classification, hashes, and Git provenance are internally consistent. |
| `quality.complete-legacy-inventory` | `quality.legacy-tests-and-smoke` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | The inventory covers CE forms, projects, resources, generated files, tests, commands, data, and failure paths. |
| `contest.oracle-active-contest-selection` | `contest.legacy-implementations` | `pending` | `not-authored` | 0 | `windows` | The CE oracle selects each ActiveContest before observing contest-dependent metadata or behavior. |
| `quality.behavioral-not-structural-evidence` | `quality.legacy-tests-and-smoke` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | UX, settings, data, adapters, and runtime features are certified by executed behavior rather than source tokens or filenames. |
| `audio.operator-sidetone-pipeline` | `audio-dsp.legacy-processing` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Local sidetone is mixed at the CE pipeline stage and level before filtering, modulation, and AGC. |
| `audio.non-farnsworth-symbols-timing-and-ramps` | `audio-dsp.legacy-processing` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Non-Farnsworth Morse symbols, cut numbers, unit timing, five millisecond ramps, and mark-space transitions match CE exactly. |
| `audio.moving-average-receiver-filter-numerics` | `audio-dsp.legacy-processing` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Every CE moving-average receiver stage, reset, cascade order, and filter-swap numeric vector matches exactly. |
| `audio.carrier-quantization-and-modulator-numerics` | `audio-dsp.legacy-processing` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Carrier frequency quantization, phase advance, quadrature modulation signs, and modulator numeric vectors match CE. |
| `audio.agc-equations-and-state-numerics` | `audio-dsp.legacy-processing` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | AGC attack, release, gain equations, state transitions, reset behavior, and output numeric vectors match CE. |
| `audio.complex-station-mixing-signs-order-and-normalization` | `audio-dsp.legacy-processing` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Complex remote-station BFO mixing uses CE-equivalent signs, channel order, phase application, accumulation order, and output normalization. |
| `audio.default-compatibility-output-profile` | `audio.legacy-adapters` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | The default CE compatibility output contract is 11025 Hz mono, 512 samples per block, and identical sample totals. |
| `audio.physical-queue-depth-and-order` | `audio.legacy-adapters` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | The physical audio queue preserves CE-equivalent default depth, FIFO block order, startup fill order, and exact sample continuity. |
| `audio.deterministic-random-primitives` | `simulation.legacy-effects` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | MT19937 output and every CE random distribution primitive produce deterministic numeric vectors for reviewed seeds. |
| `audio.qsk-receiver-ducking-and-recovery` | `audio-dsp.legacy-processing` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | QSK applies CE-equivalent receiver ducking and recovery around local transmissions. |
| `audio.rit-affects-rendered-stations` | `audio-dsp.legacy-processing` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | RIT changes each station's audible offset using CE range, steps, and block-boundary timing. |
| `audio.runtime-bandwidth-updates-filters` | `audio-dsp.legacy-processing` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Runtime bandwidth changes replace or update receiver filters at the CE-equivalent block boundary. |
| `audio.qrm-interfering-cw-stations` | `simulation.legacy-effects` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | QRM uses CE-equivalent randomized interfering CW stations, messages, levels, pitches, speeds, retries, and lifetimes. |
| `audio.qrn-impulses-and-burst-stations` | `simulation.legacy-effects` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | QRN produces CE sparse impulses and burst stations before receiver filtering and AGC. |
| `audio.qsb-independent-per-station` | `simulation.legacy-effects` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Every remote station has an independent CE-distributed QSB process without fading the receiver noise floor. |
| `audio.flutter-fast-per-station-qsb` | `simulation.legacy-effects` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Flutter is the CE probabilistic fast per-station QSB mode rather than a global multiplier. |
| `audio.station-level-and-pitch-distributions` | `audio-dsp.legacy-processing` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Remote station amplitude and pitch use CE distributions and deterministic draw ordering. |
| `audio.bfo-phase-state-and-reset` | `audio-dsp.legacy-processing` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Station BFO phase advances and resets at the same CE transmission boundaries. |
| `audio.sst-farnsworth-timing` | `audio-dsp.legacy-processing` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | SST uses independent character and spacing speeds with CE Farnsworth timing. |
| `audio.single-seeded-random-stream` | `simulation.legacy-effects` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | One seeded session random stream reproduces CE ownership and cross-feature draw order. |
| `audio.legacy-block-size-configurations` | `audio-dsp.legacy-processing` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | CE block sizes from 128 through 2048 are supported or proven import-equivalent with matching timing. |
| `audio.startup-warmup-and-filter-timing` | `audio-dsp.legacy-processing` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Startup warmup requests, block numbering, prefill, and alternating-filter swap timing match CE. |
| `audio.realistic-hiss-and-noise-floor` | `audio-dsp.legacy-processing` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Base complex hiss and receiver noise floor match CE level, spectrum, random draws, and processing order. |
| `audio.wav-pcm-bit-exact` | `audio.legacy-adapters` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | WAV headers, PCM scaling, rounding, clipping, negative full scale, and sample bytes match CE vectors. |
| `audio.recording-failure-and-backpressure-isolation` | `audio.legacy-adapters` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Recording failure and queue backpressure stop recording without faulting or allocating in the contest render path. |
| `audio.sink-preconversion-block-equivalence` | `audio.legacy-adapters` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Raw capture, WAV, null, and physical playback receive identical ordered pre-conversion audio blocks. |
| `audio.physical-device-lifecycle` | `audio.legacy-adapters` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Physical callbacks, underruns, drops, device loss, recovery, stale callbacks, shutdown, and disposal are bounded and proven natively. |
| `audio.all-effects-performance` | `audio.legacy-adapters` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | All-effects rendering retains allocation, latency, queue-depth, underrun, drop, and GC evidence representative of sustained play. |
| `engine.operator-message-completion-timing` | `simulation.runtime-routines` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Operator transmission completion and remote interpretation occur only after the rendered CW envelope completes. |
| `engine.event-driven-poisson-caller-arrivals` | `simulation.runtime-routines` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Caller creation follows completed CQ, TU, and MyCall events with CE Poisson and no-stop rules. |
| `engine.start-silent-empty-enter-cq` | `simulation.runtime-routines` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Start is silent and an empty Enter initiates the appropriate CE contest CQ. |
| `engine.contest-specific-cq-tu-and-station-id` | `contest.legacy-implementations` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | CQ, TU, operator call insertion, and periodic station-ID messages match each CE contest. |
| `contest.full-remote-exchange-formatting` | `contest.legacy-implementations` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Remote exchanges retain and format every CE contest identity field, cut number, repeat, and correction variant. |
| `contest.required-exchange-fields-all-contests` | `contest.legacy-implementations` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | CWT, SST, Field Day, NAQP, Sweepstakes, WPX, HST, and all other contests include every required CE exchange field. |
| `contest.allja-acag-truth-column-mapping` | `contest.legacy-implementations` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | ALLJA and ACAG station truth use the correct CE data columns without serial fallback. |
| `contest.sweepstakes-complete-truth-model` | `contest.legacy-implementations` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Sweepstakes models serial, weighted precedence, own call, check, section, copied values, truth, and errors. |
| `contest.arrldx-naqp-home-filtering-and-location` | `contest.legacy-implementations` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | ARRL DX and NAQP apply CE home-call filtering and dynamic location or exchange derivation. |
| `contest.wpx-hst-station-serial-generation` | `contest.legacy-implementations` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | WPX and HST station serials use CE elapsed-time, skill, and random behavior. |
| `session.hst-wpx-start-constraints` | `session.run-mode-enumeration` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | HST and WPX start constraints, forced settings, activity, bandwidth, duration, and valid serial rules match CE. |
| `engine.station-delay-lifetime-and-call-pool-rules` | `simulation.state-models` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Single-call delay, caller lifetime, patience, retry, Gaussian WPM, calls-from-keyer, station-ID rate, and HST call-pool removal match CE. |
| `engine.midmessage-append-correction-and-abort` | `simulation.runtime-routines` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Mid-message append, unsent callsign correction, and abort completion preserve CE timing and station notification. |
| `engine.confidence-lid-repeat-and-f12-branches` | `simulation.runtime-routines` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Confidence, LID, Sweepstakes correction, call-and-number repeat, and F12 probability branches match CE. |
| `engine.reset-and-restart-state` | `simulation.state-models` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Reset and restart clear and recreate the same authoritative state at the same ordered boundaries as CE. |
| `contest.exchange-shapes-and-constructor-metadata` | `contest.legacy-implementations` | `bound` | `partial` | 1 | `windows`, `linux`, `macos` | All 12 contest constructors expose the same exchange enum pair, default own exchange acceptance, and Farnsworth flag as CE. |
| `logging.nil-without-live-station-truth` | `logging.qso-model` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Logging without a completed matching live station produces CE-compatible NIL truth rather than fabricated truth. |
| `logging.worked-call-after-verified-success` | `logging.qso-model` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | A call enters worked-call state only when CE would accept verified truth, so an initial error does not poison correction. |
| `logging.duplicate-requires-prior-correct-qso` | `logging.qso-model` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Duplicate classification requires the same prior verified-correct QSO conditions as CE. |
| `logging.complete-copied-true-error-model` | `logging.qso-model` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | QSO records preserve true WPM, copied and true fields, per-field errors, corrections, and contest column flags. |
| `logging.deliberate-wrong-incomplete-nil-b4-corrected` | `logging.qso-model` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Wrong, incomplete, NIL, B4, and corrected training QSOs are committed with CE message and logging order. |
| `logging.raw-verified-points-multipliers-rate-corrections` | `logging.scoring-rate-and-results` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Raw and verified points, multipliers, score, rate, and correction totals match CE after every QSO. |
| `logging.score-history-and-competition-results` | `logging.scoring-rate-and-results` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Automatic completion, WPX history, HST append, historical score browsing, and submission workflows match CE. |
| `logging.complete-contest-cabrillo-export` | `logging.scoring-rate-and-results` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Cabrillo export includes complete interoperable QSO fields for every supported contest. |
| `ux.persisted-per-contest-own-exchange` | `configuration.persisted-settings` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Operators can validate, persist, reload, and use a distinct own exchange for every CE contest. |
| `ux.f2-and-esm-send-own-exchange` | `ux.main-form-events` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | F2 and ESM transmit the configured operator exchange rather than received-entry values. |
| `ux.contest-aware-entry-layout-and-focus` | `ux.main-form-objects` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Entry labels, visibility, widths, limits, filters, and focus order adapt to each contest as CE does. |
| `ux.input-transforms-paste-and-ime` | `ux.keyboard-workflows` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Contest-specific input transforms preserve valid text and apply equivalently to keys, paste, and IME input. |
| `ux.wipe-resets-authoritative-qso-state` | `ux.main-form-events` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Wipe clears local fields and authoritative engine ESM or active-QSO state at one semantic boundary. |
| `ux.abort-resets-authoritative-send-state` | `ux.keyboard-workflows` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Escape or Abort resets active send and ESM state and notifies stations with CE completion semantics. |
| `ux.punctuation-and-modified-enter-logging` | `ux.keyboard-workflows` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Punctuation and modified Enter preserve CE TU, log-only, wrong-QSO, and field-clearing workflows. |
| `ux.f9-explicit-pileup-action` | `ux.main-menu-commands` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | F9 and Run Pile-Up explicitly start Pile-Up regardless of the selected run-mode control. |
| `ux.live-settings-send-semantic-commands` | `ux.main-menu-commands` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Every live QSK, effect, activity, speed, pitch, bandwidth, and monitor change reaches the engine through the client. |
| `ux.rit-wpm-bandwidth-ranges-and-steps` | `ux.keyboard-workflows` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | RIT, WPM, and bandwidth ranges, increments, clamping, and displayed engine state match CE. |
| `ux.shortcut-labels-and-score-wipe-commands` | `ux.main-menu-commands` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Visible shortcut labels and commands consistently distinguish F11 Wipe from Ctrl+F11 Score. |
| `ux.plus-equals-keypad-and-layouts` | `ux.keyboard-workflows` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Physical plus, equals, keypad Add, and international layouts invoke only the CE-equivalent completion actions. |
| `ux.keyboard-and-pointer-tuning-workflows` | `ux.keyboard-workflows` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | All CE key-down, key-up, shortcut, mouse-wheel, tuning-reset, and monitor-reset workflows are available. |
| `ux.log-truth-correction-and-error-presentation` | `ux.main-form-objects` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Log rows expose copied and true values, corrections, NIL, B4, duplicate, error type, and chronological status accessibly. |
| `ux.start-stop-reset-fields-focus-and-pointer` | `ux.main-form-events` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Start, stop, and reset clear fields, restore focus, and transition pointer affordances exactly as CE. |
| `ux.high-frequency-tab-order` | `ux.legacy-form-definitions` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Tab and Shift+Tab remain in the CE high-frequency entry workflow for every contest and state. |
| `ux.numeric-controls-and-live-status-accessibility` | `ux.legacy-form-definitions` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Numeric child controls, validation, status, and failure feedback expose names, values, actions, focus, and live announcements. |
| `ux.native-scaling-contrast-and-layout` | `ux.legacy-form-definitions` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | The native GUI remains usable at minimum size, 100, 150, and 200 percent scaling, high contrast, and platform fonts. |
| `ux.score-dialog-and-history-workflow` | `ux.score-dialog` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Score dialogs, automatic completion, history navigation, actions, focus, and accessible content match CE. |
| `ux.legacy-component-runtime-behavior` | `ux.legacy-vcl-components` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Every legacy hint, volume, and UI component behavior remains functionally available through the cross-platform UX. |
| `settings.production-legacy-import` | `configuration.persisted-settings` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Production startup invokes CE settings migration when appropriate. |
| `settings.ce-encoding-translation` | `configuration.persisted-settings` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Migration translates CE indexes, ordinals, zero-or-one booleans, IDs, Hz values, duration, block size, and contest exchanges. |
| `settings.preserve-unknown-and-unconsumed-values` | `configuration.persisted-settings` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Migration and save preserve unknown or unconsumed CE values and are idempotent. |
| `settings.clean-profile-ce-defaults` | `configuration.persisted-settings` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Clean-profile call, WPM, activity, duration, pitch, bandwidth, and related defaults match CE. |
| `settings.full-duration-range` | `configuration.persisted-settings` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Every CE duration from 1 through 240 minutes is accepted and persists without hidden clamping. |
| `data.replaceable-reference-root-and-fallback` | `data.files-and-operational-paths` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Users can replace CE reference data through an explicit root with packaged fallback and equivalent discovery. |
| `data.malformed-and-missing-file-reporting` | `data.legacy-parsers` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Every parser rejects malformed rows and reports missing or invalid replacement data without fabricating values. |
| `data.legacy-parser-output-equivalence` | `data.legacy-parsers` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Call, prefix, exchange, serial, section, lexer, and contest data parsers produce CE-equivalent values and failures. |
| `recording.ce-filename-overwrite-and-discovery` | `data.files-and-operational-paths` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | A CE-compatible fixed WAV naming, overwrite, and discovery workflow remains available. |
| `release.version-package-and-about-provenance` | `build.legacy-project-metadata` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | About, engine, assembly, CLI, archive, icon, package, and evidence version provenance agree. |
| `release.real-native-gui-window-evidence` | `quality.legacy-tests-and-smoke` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Release evidence launches and discovers a real native Avalonia main window rather than setup-only execution. |
| `release.physical-audio-evidence-required` | `quality.legacy-tests-and-smoke` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Release publication requires physical-audio startup, sustained playback, effects, device change, recovery, and shutdown evidence. |
| `transport.completed-state-is-immutable` | `simulation.state-models` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | In-process and remote clients cannot mutate authoritative completed QSO or result state. |
| `transport.single-active-interactive-session` | `simulation.state-models` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | The shared application service enforces the documented single active interactive session contract. |
| `transport.commands-apply-at-every-block-boundary` | `simulation.runtime-routines` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Commands apply at every documented block boundary, including during long simulation advances. |
| `transport.bounded-idempotent-mutations` | `simulation.runtime-routines` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Mutating request IDs replay safely and command idempotency retention is bounded. |
| `transport.lossless-events-coalesced-snapshots` | `simulation.runtime-routines` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Lossless events and coalescible snapshots use independent bounded delivery so slow observers cannot delay or silently lose authority. |
| `transport.resync-watermark-ordering` | `simulation.state-models` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Snapshots carry a sequence watermark and reconnect never delivers older events after newer state. |
| `transport.required-event-groups` | `simulation.state-models` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Fault, operator-message, score, rate, recording, result, and control-lease events expose every authoritative transition. |
| `transport.control-lease-semantics` | `simulation.state-models` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Lease acquire, renew, release, takeover, identity, ordering, and transport-neutral control behavior are complete. |
| `transport.inprocess-grpc-full-session-equivalence` | `quality.legacy-tests-and-smoke` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Identical full-session vectors produce identical commands, events, snapshots, results, and failures through in-process and gRPC clients. |
| `release.native-gui-input-accessibility-evidence` | `quality.legacy-tests-and-smoke` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Native GUI, focus, routed-key, pointer, scaling, accessibility, and shutdown evidence exists on every supported platform. |
| `release.all-client-and-artifact-path-evidence` | `quality.legacy-tests-and-smoke` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | TUI, CLI, in-process, hosted gRPC, WAV, data override, migration, and archive behavior are captured on clean systems. |
| `release.platform-complete-publication-gate` | `build.legacy-project-metadata` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Every supported platform must be complete before release promotion or artifact publication. |
| `release.zero-skips-live-final-certification` | `quality.legacy-tests-and-smoke` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Final certification executes every obligation live with zero skipped, waived, quarantined, expected-failure, or fixture-only cases. |
| `release.experienced-user-ab-listening` | `quality.legacy-tests-and-smoke` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Structured CE and XPlat A/B sessions cover contests, audio conditions, corrections, logging, history, and migration with experienced users. |
| `ux.enter-esm-partial-call-message-selection` | `ux.keyboard-workflows` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Enter with an empty, partial, corrected, or complete call selects and sends the same ESM message and state transition as CE. |
| `ux.log-selection-updates-callsign-information` | `ux.main-form-events` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Selecting a historical QSO updates the callsign-information region to the selected call as CE does. |
| `ux.semantic-duration-not-simulation-blocks` | `ux.main-form-events` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | The UX sends semantic duration and never owns sample-rate or simulation-block conversion policy. |
| `ux.help-about-readme-community-actions` | `ux.main-menu-commands` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Help, About, readme, website, and community actions match CE availability, targets, errors, keyboard access, and focus return. |
| `ux.score-service-browse-submit-and-failures` | `ux.score-dialog` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Score browsing, submission, disabled or unavailable service behavior, failures, focus, and history match CE. |
| `transport.session-lifecycle-transition-validity` | `simulation.state-models` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Session lifecycle transitions reject invalid starts such as rmStop, define close-while-running behavior, and emit the same ordered terminal state through every client. |
| `settings.all-supported-ce-keys-consumed-or-preserved` | `configuration.persisted-settings` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Every supported CE key is consumed with equivalent semantics or preserved losslessly across import, save, and restart. |
| `release.cli-help-version-exit-codes` | `build.legacy-project-metadata` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | CLI help, version, validation failures, runtime failures, and successful commands expose stable documented output and exit codes in packaged builds. |
| `lifecycle.unit-initialization-finalization-order` | `lifecycle.legacy-unit-hooks` | `pending` | `not-authored` | 0 | `windows`, `linux`, `macos` | Legacy initialization and finalization hooks execute with equivalent order, registration, cleanup, state, and failures. |

## Rich-artifact evidence blockers

- None.

## Active acceptance cases

| Case ID | Capability | Obligations | Status | Failure code | Legacy | XPlat |
|---|---|---|---|---|---|---|
| `contest.exchange-shapes` | `contest.legacy-implementations` | `contest.exchange-shapes-and-constructor-metadata` | `legacy-green-xplat-red` | `contest-exchange-shape-mismatch` | `pass` | `fail` |

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
