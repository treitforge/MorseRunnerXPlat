# Legacy MorseRunner compatibility matrix

## Verdict

MorseRunnerXPlat now has working Avalonia and terminal operator clients over
the same semantic engine boundary. The primary start, send, entry, logging,
radio-control, pause, resume, stop, recording, and help workflows operate
end to end.

Full 1:1 legacy compatibility is not yet achieved. Avalonia and the TUI now
expose the advanced legacy settings, result export, local high-score, and
recording workflows through the shared engine and persistence layers. Release
remains blocked by cross-platform native visual and physical-audio evidence
below.

## Evidence used

- Legacy revision `55bbd019c29d8cf693184ea420a17a253f16fe1e`.
- `Main.dfm`, `Main.lfm`, and the keyboard and handler branches in `Main.pas`.
- The 1,501-entry extracted surface inventory and committed legacy fixtures.
- A live launch of `build/Release/MorseRunner.exe` on Windows. The automation
  provider could identify the legacy process and title but could not capture
  its VCL window, so structural UI comparison uses the form resources.
- Live Windows launch and interaction with the Avalonia executable.
- Avalonia headless tests, TUI renderer and key-router tests, Windows ConPTY
  and xterm captures, cross-UX workflow tests, engine/DSP tests, and in-process
  versus gRPC scenario tests.
- Target-native archive evidence defined in
  `docs/release/native-evidence.md`, including native Avalonia initialization,
  five terminal views, WAV integrity, normalized in-process and authenticated
  hosted outcomes, and physical-device status.

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
| Live QSO log and score | Main log, score, rate, and result views | Bound log plus score dialog with five-minute rate and per-contest personal high score | Responsive log plus results view with the same score, rate, high score, and exports | Implemented. Avalonia, TUI, and hosted Results reuse the shared JSON and Cabrillo formatter. Cross-UX result tests pin the same engine-owned outcome. |
| Settings persistence | Legacy INI | Atomic versioned settings store with legacy-compatible keys, including RX bounds, serial range, HST operator, and callsign-info visibility | Uses the same atomic store and legacy-compatible keys for session setup and advanced settings | Implemented. Restart coverage verifies TUI settings survive a new application instance. |
| Activity and band conditions | Activity, QSK, QSB, QRM, QRN, flutter, LIDs | Seeded active-station audio, pileup activity, QSK receive-during-send, and deterministic QSB/QRM/QRN/flutter/LID behavior | Same settings through Ctrl+1 through Ctrl+6 | Implemented for the live station path. Legacy audio golden expansion remains part of the broader DSP release gate. |
| Live callers and corrections | Station collection, best partial-call confidence, repeats, reply timing, correction, ghosting, and completion | Session-owned active station collection with block-timed state and CW replies | Same engine behavior and active pileup count | Implemented. The pinned live-station vector and seeded engine traces cover `NR?`, corrected number, partial calls, completion, and caller events. |
| Station truth and NIL | Completed station supplies true callsign and exchange for log verification | True callsign and contest exchange populate immutable QSO records; unmatched logs are `NIL` and do not score | Same result through the shared client | Implemented with corrected and NIL engine workflows. |
| Monitor level | Persistent self-monitor level, defaulting to `0 dB` | Applied as engine output gain with the legacy default | Persisted and adjustable from the advanced settings view | Implemented. Physical startup is prebuffered and the automated probe reports zero underruns and drops. |
| Audio recording and playback | Optional WAV and playback command | Bounded WAV recording beside physical output; completed file opens from File menu | Local recording toggle, latest-WAV discovery, and operating-system launch; hosted mode identifies recording as host-owned | Implemented at the UX boundary. Native physical recording evidence remains a release gate. |
| Help, first-time setup, readme, community link | Help menu | Working dialogs and packaged readme | Built-in keyboard help | Implemented, except TUI does not open the long packaged readme. |
| Responsive/scaled layout | Fixed VCL form | Scroll-safe desktop layout, compiled bindings, accessible names | Bounded operator, compact, settings, results, diagnostics, and help views; ANSI and color capability detection; incremental row repaint | Automated compact and wide renderer tests pass. Windows ConPTY captures at 120 by 34 and 100 by 28 prove typing and resizing without scrolling or line accumulation. Target-native archive captures are produced by `Native Release Evidence`; Linux and macOS physical review remains. |
| Hosted operation | Not applicable | In-process default | Local or authenticated loopback gRPC with explicit connected, reconnect-failed, and host-owned recording text | XPlat extension implemented. Cross-client transport tests and TUI diagnostic-state coverage pass. |

## Release-blocking functional gaps

| Area | Current gap | Required proof |
|---|---|---|
| Audio device UX | Avalonia enumerates, persists, selects before start, and recovers playback devices through the semantic client. Native interaction evidence is currently Windows-only. | Physical-device interaction test on each supported OS. |
| Visual platforms | Windows live Avalonia interaction is verified. Linux and macOS captures are not yet recorded. | CI or release-machine screenshots and keyboard/focus checks on all three platforms. |

## Next acceptance slices

1. Capture native audio-device recovery and visual evidence on Linux and
   macOS.
2. Expand contest-specific live exchange and audio golden traces as the
   remaining DSP and result slices land.
3. Record Linux and macOS visual evidence and close every remaining row before
   changing the verdict to full compatibility.
