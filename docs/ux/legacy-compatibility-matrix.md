# Legacy MorseRunner compatibility matrix

## Verdict

MorseRunnerXPlat now has working Avalonia and terminal operator clients over
the same semantic engine boundary. The primary start, send, entry, logging,
radio-control, pause, resume, stop, recording, and help workflows operate
end to end.

Full 1:1 legacy compatibility is not yet achieved. Release remains blocked by
the partial and missing rows below, most importantly contest-specific scoring
and exchanges beyond the completed CQ WPX slice, realistic station state
machines, advanced legacy settings, and cross-platform visual verification.

## Evidence used

- Legacy revision `55bbd019c29d8cf693184ea420a17a253f16fe1e`.
- `Main.dfm`, `Main.lfm`, and the keyboard and handler branches in `Main.pas`.
- The 1,501-entry extracted surface inventory and committed legacy fixtures.
- A live launch of `build/Release/MorseRunner.exe` on Windows. The automation
  provider could identify the legacy process and title but could not capture
  its VCL window, so structural UI comparison uses the form resources.
- Live Windows launch and interaction with the Avalonia executable.
- Avalonia headless tests, TUI renderer and key-router tests, cross-UX workflow
  tests, engine/DSP tests, and in-process versus gRPC scenario tests.

The existing 20/20 parity report proves fixture and structural adapter
coverage. It must not be interpreted as live behavioral or visual proof for
the partial rows below.

## Workflow comparison

| Workflow | Legacy | Avalonia | TUI | Status and evidence |
|---|---|---|---|---|
| Launch and initial callsign focus | Main form focuses the operator entry path | Window launches, loads settings, and focuses Call | Starts at Call field | Implemented. Avalonia headless window-open/focus test and live Windows launch. |
| Contest selection | 12 contests | All 12 selectable | All 12 cycle from the keyboard | Implemented. Shared catalog and cross-UX tests. |
| Run modes | Stop, Pile-Up, Single Calls, WPX, HST | Four active modes plus Stop | Four active modes plus Stop | Implemented. F9, Shift+F9, Ctrl+F9, and F10 are tested. |
| Real-time session clock | Audio/block-driven | Engine-owned automatic timing for physical sessions | Automatic timing for physical and null-audio interactive modes | Implemented. Session-loop timing test and live UI observation. |
| F1 through F8 and F12 messages | Semantic send menu and function keys | Semantic client commands | Same semantic client commands | Implemented. GUI bindings and TUI key-router tests. |
| Insert and semicolon | Send caller call plus exchange | Implemented | Implemented | Implemented. |
| Enter and punctuation logging | Enter and `.`, `,`, `+`, `[` complete the QSO workflow | Implemented | Implemented | Implemented. Cross-UX QSO outcome test. |
| Entry formatting | Uppercase and legacy A/E/N/O/T substitutions | Text filters and focus selection | Field-aware filters and replacement | Implemented for the inventoried transformations. |
| RIT, bandwidth, and speed | Arrow and page keys with modifiers | Live semantic radio commands and snapshot values | Same commands and values | Implemented. |
| Pause, resume, stop, restart | Stop and run lifecycle | Pause/resume extensions plus Stop and clean restart | Same | Implemented. |
| Live QSO log and score | Main log, score, and result views | Bound log with time, call, RST, exchange, result, and duplicate status | Responsive terminal log with the same outcome | CQ WPX one-point QSOs, unique-prefix multipliers, verified score, and `DUP` behavior are legacy-golden and cross-UX tested. Other contests remain simplified. |
| Settings persistence | Legacy INI | Atomic versioned settings store with legacy-compatible keys | Session setup is not yet persisted | Partial. |
| Activity and band conditions | Activity, QSK, QSB, QRM, QRN, flutter, LIDs | Passed through session settings; deterministic QSB/QRM/QRN/flutter DSP and activity/LIDs behavior | Same settings through Ctrl+1 through Ctrl+6 | Partial. QSK is carried but its complete legacy keying semantics still need an acceptance vector. |
| Monitor level | Persistent self-monitor level | Applied as engine output gain | Fixed default | Partial. |
| Audio recording and playback | Optional WAV and playback command | Bounded WAV recording beside physical output; completed file opens from File menu | Not exposed | Partial. |
| Help, first-time setup, readme, community link | Help menu | Working dialogs and packaged readme | Built-in keyboard help | Implemented, except TUI does not open the long packaged readme. |
| Responsive/scaled layout | Fixed VCL form | Scroll-safe desktop layout, compiled bindings, accessible names | 80-column and wide-terminal render coverage | Implemented on Windows. Linux and macOS visual review remains. |
| Hosted operation | Not applicable | In-process default | Local or authenticated loopback gRPC | XPlat extension implemented. Cross-client transport tests pass. |

## Release-blocking functional gaps

| Area | Current gap | Required proof |
|---|---|---|
| Contest scoring and multipliers | CQ WPX scoring and duplicate behavior are integrated. The other 11 contests still use a simplified point result. | Identical legacy/XPlat golden QSO sequences and final score for all 12 contests. |
| Exchange validation and correction | CQ WPX applies callsign, RST, and serial presence validation plus duplicate detection. True-exchange correction, NIL handling, and the other contest-specific transitions are not integrated. | Dual-run acceptance vectors for valid, invalid, corrected, duplicate, and NIL QSOs. |
| Station simulation | The live loop emits simplified seeded callers and does not yet integrate the full operator and station-collection state machines. | Seeded event, reply timing, station-state, and audio vectors against legacy. |
| Callsign and reference data | Packaged data and parsers exist, but live calls are not yet sourced and annotated through the complete legacy workflow. | Callsign, DXCC, prefix, and contest-file scenario vectors. |
| Advanced settings | Min/max RX speed, serial-number range, HST operator configuration, and callsign-info presentation are not exposed end to end. | Persisted setting, engine behavior, Avalonia, and TUI workflow tests. |
| Rate and result experience | Full rate calculations, detailed result breakdown, export, high-score browsing, and submission are incomplete. | Legacy comparison plus offline/error behavior for external services. |
| Audio device UX | Device enumeration and recovery exist below the UX, but device selection and recovery dialogs are incomplete. | Physical-device interaction test on each supported OS. |
| TUI persistence and recording | The TUI exposes session setup and conditions but does not persist them or control WAV recording. | Restart and recording workflows through the TUI. |
| Visual platforms | Windows live Avalonia interaction is verified. Linux and macOS captures are not yet recorded. | CI or release-machine screenshots and keyboard/focus checks on all three platforms. |

## Next acceptance slices

1. Extend the CQ WPX live slice with true-exchange correction and NIL vectors,
   then port the remaining 11 contests in increasing rule complexity.
2. Replace placeholder caller generation with the existing seeded operator and
   station state machines, then capture reply and event timing.
3. Complete advanced settings once their engine semantics are acceptance-tested.
4. Add audio-device recovery UX and TUI persistence/recording.
5. Record Linux and macOS visual evidence and close every remaining row before
   changing the verdict to full compatibility.
