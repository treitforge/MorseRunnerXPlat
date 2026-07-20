# Legacy MorseRunner compatibility matrix

## Verdict

MorseRunnerXPlat has working Avalonia and terminal clients over the same
semantic engine boundary, but the July 2026 live parity audit found material
behavioral gaps in high-frequency operator workflows, sent exchanges,
contest-aware entry, logging errors and corrections, runtime controls,
results, persistence, focus, accessibility, and native platform evidence.

Full 1:1 legacy compatibility is not yet achieved. Every aggregate UX claim
remains red until a routed-input acceptance case first passes live CE, retains
the observed XPlat failure, and then passes unchanged against XPlat.

## Evidence used

- Legacy revision `55bbd019c29d8cf693184ea420a17a253f16fe1e`.
- `Main.dfm`, `Main.lfm`, and the keyboard and handler branches in `Main.pas`.
- The current 3,668-surface inventory across all 143 tracked CE files, mapped
  to 24 audited capabilities and 118 behavioral obligations.
- A live launch of `build/Release/MorseRunner.exe` on Windows. The automation
  provider could identify the legacy process and title but could not capture
  its VCL window, so structural UI comparison uses the form resources.
- Live Windows launch and interaction with the Avalonia executable.
- Historical schema-v1 fixtures as noncertifying provenance only.
- Avalonia headless tests, TUI renderer and key-router tests, Windows ConPTY
  and xterm captures, cross-UX workflow tests, engine/DSP tests, and in-process
  versus gRPC scenario tests.
- Target-native archive evidence defined in
  `docs/release/native-evidence.md`, including native Avalonia initialization,
  five terminal views, WAV integrity, normalized in-process and authenticated
  hosted outcomes, and physical-device status.

The earlier 20/20 report used fixture fallback and permissive structural
assertions. It is historical scaffold evidence only and proves neither live
behavioral nor visual parity.

## Workflow comparison

| Workflow | Legacy | Avalonia | TUI | Status and evidence |
|---|---|---|---|---|
| Launch and initial callsign focus | Main form focuses the operator entry path | Window launches and focuses Call on the audited Windows build | Starts at Call field | Partial. Initial Windows focus passed, but restart field clearing, focus restoration, and Linux/macOS native traces are not certified. |
| Contest selection | 12 contests with contest-specific entry fields and sent exchanges | All 12 are selectable, but entry fields remain static and sent exchange is not configurable | All 12 can be selected | Gap. Enumeration exists, but contest selection does not produce the CE editing and sent-exchange workflow. |
| Run modes | Stop, Pile-Up, Single Calls, WPX, HST | Four modes plus Stop | Four modes plus Stop | Mismatch. F9 follows the selected combo instead of always starting Pile-Up, and HST can start under configurations CE rejects. |
| Real-time session clock | Audio/block-driven with CE startup block behavior | Engine-owned timing | Engine-owned timing | Partial. The session clock exists, but startup request sizes, first filter swap, and early block sequence differ from CE. |
| F1 through F8 and F12 messages | Contest-aware operator messages and active-call correction | Semantic commands exist | Same command surface | Mismatch. F2 sends copied received fields instead of the configured operator exchange, and in-flight call correction is missing. |
| Insert and semicolon | Send caller call plus the operator exchange as one workflow | Two commands are issued | Same shared commands | Partial. Payload, ordering, cancellation, and ESM state equivalence are not certified. |
| Enter and punctuation logging | ESM plus deliberate wrong-copy logging for Enter, `.`, `,`, `+`, and `[` | ESM and completion handlers exist | Shared client intents exist | Critical mismatch. XPlat can send TU and then reject the QSO instead of retaining the training error. The shifted `+` route is also incorrect. |
| Entry formatting | Contest-specific field visibility, length, case, allowed characters, spaces, and substitutions | Four static fields with global filters | Shared field model | Critical mismatch. ARRL sections can be corrupted, Sweepstakes spacing is unsupported, and paste/IME input bypasses filtering. |
| RIT, bandwidth, and speed | CE steps and bounds with audible receiver changes | Snapshot commands exist | Same commands | Critical mismatch. Steps and bounds differ, while RIT and bandwidth do not affect rendered audio. |
| Pause, resume, stop, restart | Stop/run lifecycle clears entry and contest state | Pause/resume extensions and Stop exist | Same shared lifecycle | Mismatch. Wipe, abort, stop, and restart do not reset all authoritative ESM and entry state. |
| Live QSO log and score | Contest-specific columns, copied and true values, errors, corrections, history, and automatic results | Current rows and score dialog | Current log and results views | Critical gap. Error truth, corrections, chronological CE presentation, score history, and automatic competition results are missing. |
| Settings persistence | Legacy defaults plus per-contest exchanges and operational settings | Versioned store | Same store | Critical gap. Clean defaults differ, arbitrary 1 to 240 minute durations are unavailable, and per-contest sent exchanges are absent. |
| Activity and band conditions | Event-driven callers plus independent QSB/flutter, CW QRM, impulse/burst QRN, QSK, and LIDs | Simplified seeded effects | Same engine effects | Critical audio mismatch. QRM is a sine, QRN is continuous low noise, QSB/flutter are global, QSK is ordered differently, and caller arrivals use a periodic cadence. |
| Live callers and corrections | CE random ownership, station distributions, repeats, correction, ghosting, and completion | Session-owned station collection | Same engine collection | Partial. Basic station lifecycle exists, but seeded draw order, amplitude/pitch distributions, arrival timing, BFO reset, and active-message correction differ. |
| Station truth and NIL | Logs copied and true values, correction flags, NIL, duplicate, and score outcomes | Domain records retain some truth fields | Same records | Partial. The UI discards important truth/error fields and the completion path can reject deliberate errors instead of logging them. |
| Monitor level | Local sidetone enters the CE receiver pipeline with QSK ducking and a true -60 dB mute | Post-AGC normalized tone/gain | Same rendered output | Critical audio mismatch. Level, AGC interaction, receiver ducking, clipping, and -60 dB behavior differ. |
| Audio recording and playback | CE PCM conversion and synchronous completed receiver result | Background WAV recording and physical playback | Recording controls and playback launch | Critical reliability and fidelity gap. PCM conversion is not bit-exact, recording backpressure can fault the session, and sink parity is unproved. |
| Help, first-time setup, readme, community link | Help menu and first-use flow | Dialogs and packaged readme | Built-in keyboard help | Partial. Structural commands exist, but first-use, failure, focus-return, and native accessibility workflows are not certified. |
| Responsive/scaled layout | Fixed VCL form | Scrollable Avalonia layout | Responsive terminal layouts | Partial. Compiled bindings exist, but minimum-size density, focus order, numeric accessibility, high contrast, and 150/200 percent scaling lack native three-platform proof. |
| Hosted operation | Not applicable | In-process default | Optional loopback gRPC | XPlat extension. Transport tests exist, but every parity scenario must still produce the same normalized outcome through in-process and hosted clients before release. |

## Release-blocking functional gaps

| Area | Current gap | Required proof |
|---|---|---|
| Contest and exchanges | Sent exchange, field shape, validation, scoring, and HST constraints are incomplete or mismatched. | Decomposed live CE-first cases for every contest, field, default, invalid input, point, multiplier, and completion outcome. |
| Keyboard and ESM | F2, F9, Enter, punctuation, wipe, abort, correction, key-up, focus, and modified shortcuts differ. | Routed keyboard/focus traces with exact commands, emitted CW, state, QSO rows, and score on both targets. |
| Audio and propagation | Sidetone/QSK, RIT, bandwidth, QSB, flutter, QRM, QRN, Farnsworth, RNG ownership, startup, and WAV conversion differ. | Seeded per-block CE/XPlat observations and audio hashes for every effect alone and in combination. |
| Logging, results, and persistence | Deliberate errors, truth/correction display, score history, competition completion, defaults, duration, and sent exchanges are incomplete. | Restart, completion, export, history, and error-record acceptance cases against clean temporary stores. |
| Audio device UX | Avalonia enumerates, persists, selects before start, and recovers playback devices through the semantic client. Native interaction evidence is currently Windows-only. | Physical-device interaction test on each supported OS. |
| Visual platforms | Windows live Avalonia interaction is verified. Linux and macOS captures are not yet recorded. | CI or release-machine screenshots and keyboard/focus checks on all three platforms. |

## Next acceptance slices

1. Finish the fail-closed live CE evidence harness and decompose every audited
   capability into executable behavioral cases.
2. Correct contest exchange and validation behavior, then operator entry,
   logging, and lifecycle workflows.
3. Port the integrated CE audio pipeline and effects with seeded block-level
   evidence before tuning physical backends.
4. Close persistence, results, hosted-client, native audio, visual, focus, and
   accessibility evidence on every supported platform.
