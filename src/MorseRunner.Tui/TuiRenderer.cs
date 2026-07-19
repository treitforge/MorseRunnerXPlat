using System.Globalization;
using System.Text;
using MorseRunner.Domain;

namespace MorseRunner.Tui;

public static class TuiRenderer
{
    private const int StandardMinimumWidth = 70;
    private const int StandardMinimumHeight = 22;

    public static string Render(
        TuiState state,
        int width,
        int height,
        bool useColor = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var canvas = new TerminalCanvas(width, height);
        if (state.ShowHelp)
        {
            RenderHelp(canvas);
        }
        else if (width < StandardMinimumWidth
            || height < StandardMinimumHeight)
        {
            RenderCompact(state, canvas);
        }
        else
        {
            RenderStandard(state, canvas);
        }

        return canvas.Render(useColor);
    }

    private static void RenderStandard(
        TuiState state,
        TerminalCanvas canvas)
    {
        SessionSnapshot? snapshot = state.Snapshot;
        RenderHeader(state, snapshot, canvas);
        RenderSessionSetup(state, canvas);
        RenderEntry(state, canvas);
        RenderActions(canvas);

        int statusTop = canvas.Height - 4;
        RenderLog(state, canvas, top: 16, bottom: statusTop - 1);
        RenderStatus(state, snapshot, canvas, statusTop);
    }

    private static void RenderHeader(
        TuiState state,
        SessionSnapshot? snapshot,
        TerminalCanvas canvas)
    {
        canvas.DrawBox(0, 0, canvas.Width - 1, 2, string.Empty);
        canvas.Write(1, 2, "MORSE RUNNER XPLAT", CellStyle.Title);
        canvas.Write(
            1,
            23,
            state.IsHosted ? "HOSTED" : "LOCAL",
            CellStyle.Muted);

        string session = snapshot is null
            ? "READY"
            : snapshot.State.ToString().ToUpperInvariant();
        CellStyle sessionStyle = snapshot?.State switch
        {
            SessionState.Running => CellStyle.Good,
            SessionState.Paused => CellStyle.Warning,
            SessionState.Faulted => CellStyle.Error,
            _ => CellStyle.Value,
        };
        string elapsed = snapshot?.ElapsedSimulationTime.ToString(
            @"mm\:ss\.fff",
            CultureInfo.InvariantCulture) ?? "00:00.000";
        string context =
            $"{state.Contest.DisplayName}  {RunModeName(state.RunMode)}";
        canvas.Write(1, 34, context, CellStyle.Accent);
        canvas.WriteRight(
            1,
            2,
            $"SCORE {snapshot?.Score ?? 0}",
            CellStyle.Value);
        canvas.WriteRight(1, 16, elapsed, CellStyle.Value);
        canvas.WriteRight(1, 29, session, sessionStyle);
    }

    private static void RenderSessionSetup(
        TuiState state,
        TerminalCanvas canvas)
    {
        canvas.DrawBox(0, 3, canvas.Width - 1, 6, " SESSION ");
        canvas.Write(4, 2, "CONTEST", CellStyle.Border);
        canvas.Write(4, 11, state.Contest.DisplayName, CellStyle.Value);
        canvas.Write(4, 29, "MODE", CellStyle.Border);
        canvas.Write(4, 35, RunModeName(state.RunMode), CellStyle.Value);
        canvas.Write(4, 56, "DURATION", CellStyle.Border);
        canvas.Write(
            4,
            66,
            DurationName(state.DurationMinutes),
            CellStyle.Value);

        canvas.Write(5, 2, "CONDITIONS", CellStyle.Border);
        int column = 14;
        column = WriteToggle(canvas, 5, column, "QSK", state.Qsk);
        column = WriteToggle(canvas, 5, column, "QSB", state.Qsb);
        column = WriteToggle(canvas, 5, column, "QRM", state.Qrm);
        column = WriteToggle(canvas, 5, column, "QRN", state.Qrn);
        column = WriteToggle(canvas, 5, column, "FLUTTER", state.Flutter);
        WriteToggle(canvas, 5, column, "LIDS", state.Lids);
        canvas.WriteRight(
            5,
            2,
            "Ctrl+1..6 toggle",
            CellStyle.Muted);
    }

    private static int WriteToggle(
        TerminalCanvas canvas,
        int row,
        int column,
        string label,
        bool enabled)
    {
        canvas.Write(
            row,
            column,
            $"{label} {(enabled ? "ON" : "off")}",
            enabled ? CellStyle.Good : CellStyle.Muted);
        return column + label.Length + 7;
    }

    private static void RenderEntry(TuiState state, TerminalCanvas canvas)
    {
        canvas.DrawBox(0, 7, canvas.Width - 1, 11, " QSO ENTRY ");
        int innerWidth = canvas.Width - 2;
        string[] labels = ["CALLSIGN", "RST", "EXCHANGE 1", "EXCHANGE 2"];
        string[] values = state.Fields;
        for (int index = 0; index < labels.Length; index++)
        {
            int left = 1 + (index * innerWidth / labels.Length);
            int right = 1 + ((index + 1) * innerWidth / labels.Length);
            int valueWidth = Math.Max(1, right - left - 2);
            bool active = state.ActiveField == index;
            canvas.Write(
                8,
                left + 1,
                labels[index],
                active ? CellStyle.Accent : CellStyle.Border);
            canvas.Write(
                9,
                left + 1,
                Fit(values[index], valueWidth),
                active ? CellStyle.Active : CellStyle.Value);
            if (index > 0)
            {
                canvas.Write(8, left, "│", CellStyle.Border);
                canvas.Write(9, left, "│", CellStyle.Border);
                canvas.Write(10, left, "│", CellStyle.Border);
            }
        }

        canvas.Write(
            10,
            2,
            "Tab / Shift+Tab move fields",
            CellStyle.Muted);
        canvas.WriteRight(
            10,
            2,
            "Enter ESM  F11 wipes",
            CellStyle.Muted);
    }

    private static void RenderActions(TerminalCanvas canvas)
    {
        canvas.DrawBox(0, 12, canvas.Width - 1, 15, " KEYER ");
        WriteKeyHints(
            canvas,
            13,
            [
                ("F1", "CQ"),
                ("F2", "EXCH"),
                ("F3", "TU"),
                ("F4", "MY CALL"),
                ("F5", "HIS CALL"),
                ("F6", "B4"),
                ("F7", "?"),
                ("F8", "NIL"),
                ("F12", "NR?"),
            ]);
        WriteKeyHints(
            canvas,
            14,
            [
                ("F9", "START"),
                ("Shift+F9", "SINGLE"),
                ("Ctrl+F9", "HST"),
                ("F10", "STOP"),
                ("Enter", "ESM"),
                ("F11", "WIPE"),
            ]);
    }

    private static void WriteKeyHints(
        TerminalCanvas canvas,
        int row,
        IReadOnlyList<(string Key, string Label)> hints)
    {
        int column = 2;
        foreach ((string key, string label) in hints)
        {
            if (column >= canvas.Width - 3)
            {
                break;
            }

            canvas.Write(row, column, $" {key} ", CellStyle.Key);
            column += key.Length + 2;
            canvas.Write(row, column, $" {label}", CellStyle.Muted);
            column += label.Length + 3;
        }
    }

    private static void RenderLog(
        TuiState state,
        TerminalCanvas canvas,
        int top,
        int bottom)
    {
        if (bottom <= top)
        {
            return;
        }

        canvas.DrawBox(0, top, canvas.Width - 1, bottom, " QSO LOG ");
        int headerRow = top + 1;
        canvas.Write(headerRow, 2, "TIME", CellStyle.Border);
        canvas.Write(headerRow, 13, "CALL", CellStyle.Border);
        canvas.Write(headerRow, 28, "RST", CellStyle.Border);
        canvas.Write(headerRow, 35, "EXCHANGE", CellStyle.Border);
        canvas.WriteRight(headerRow, 2, "RESULT", CellStyle.Border);

        int availableRows = Math.Max(0, bottom - top - 2);
        IReadOnlyList<Qso> qsos = state.Qsos;
        int start = Math.Max(0, qsos.Count - availableRows);
        int row = top + 2;
        for (int index = start; index < qsos.Count && row < bottom; index++)
        {
            Qso qso = qsos[index];
            canvas.Write(
                row,
                2,
                qso.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                CellStyle.Muted);
            canvas.Write(row, 13, Fit(qso.Call, 13), CellStyle.Call);
            canvas.Write(
                row,
                28,
                qso.Rst.ToString(CultureInfo.InvariantCulture),
                CellStyle.Value);
            canvas.Write(
                row,
                35,
                Fit(JoinExchange(qso), Math.Max(1, canvas.Width - 51)),
                CellStyle.Value);
            canvas.WriteRight(
                row,
                2,
                QsoResult(qso),
                qso.IsDuplicate ? CellStyle.Warning : CellStyle.Good);
            row++;
        }
    }

    private static void RenderStatus(
        TuiState state,
        SessionSnapshot? snapshot,
        TerminalCanvas canvas,
        int top)
    {
        canvas.DrawBox(0, top, canvas.Width - 1, canvas.Height - 1, " STATUS ");

        string caller = CallerState(snapshot?.ActiveOperatorState);
        int pileup = snapshot?.ActiveStations?.Count ?? 0;
        string audio = AudioState(snapshot);
        CellStyle audioStyle = snapshot switch
        {
            { AudioOutputHealthy: false } => CellStyle.Error,
            { AudioDroppedBlockCount: > 0 } => CellStyle.Warning,
            { AudioUnderrunCount: > 0 } => CellStyle.Warning,
            _ => CellStyle.Good,
        };
        string telemetry =
            canvas.Width >= 120
                ? $"CALLER {caller}  |  PILEUP {pileup}  |  {audio}"
                    + $"  |  RIT {snapshot?.RitOffsetHz ?? 0} Hz"
                    + $"  BW {snapshot?.CurrentBandwidthHz ?? 500} Hz"
                    + $"  WPM {snapshot?.CurrentWordsPerMinute ?? 30}"
                : canvas.Width >= 95
                    ? $"CALLER {caller}  |  PILEUP {pileup}  |  {audio}"
                    : $"PILEUP {pileup}  |  {audio}";
        int statusWidth = Math.Max(
            1,
            canvas.Width - telemetry.Length - 6);
        canvas.Write(
            top + 1,
            2,
            Fit(state.Status, statusWidth),
            StatusStyle(state.Status));
        canvas.WriteRight(top + 1, 2, telemetry, audioStyle);

        canvas.Write(
            top + 2,
            2,
            "Tab fields  ↑/↓ RIT  Ctrl+↑/↓ BW  PgUp/PgDn WPM",
            CellStyle.Muted);
        canvas.WriteRight(
            top + 2,
            2,
            "? help  Ctrl+Q quit",
            CellStyle.Muted);
    }

    private static CellStyle StatusStyle(string status)
    {
        if (status.Contains("rejected", StringComparison.OrdinalIgnoreCase)
            || status.Contains("error", StringComparison.OrdinalIgnoreCase)
            || status.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return CellStyle.Error;
        }

        if (status.Contains("logged", StringComparison.OrdinalIgnoreCase)
            || status.Contains("running", StringComparison.OrdinalIgnoreCase))
        {
            return CellStyle.Good;
        }

        return CellStyle.Value;
    }

    private static string AudioState(SessionSnapshot? snapshot) =>
        snapshot switch
        {
            null => "AUDIO READY",
            { AudioOutputHealthy: false } => "AUDIO FAULT",
            { AudioDroppedBlockCount: > 0 } =>
                $"AUDIO DROP {snapshot.AudioDroppedBlockCount}",
            { AudioUnderrunCount: > 0 } =>
                $"AUDIO UNDERRUN {snapshot.AudioUnderrunCount}",
            _ => "AUDIO OK",
        };

    private static void RenderCompact(TuiState state, TerminalCanvas canvas)
    {
        SessionSnapshot? snapshot = state.Snapshot;
        canvas.DrawBox(0, 0, canvas.Width - 1, Math.Min(2, canvas.Height - 1), "");
        canvas.Write(1, 2, "MORSE RUNNER XPLAT", CellStyle.Title);
        canvas.WriteRight(
            1,
            2,
            snapshot?.State.ToString().ToUpperInvariant() ?? "READY",
            CellStyle.Accent);

        if (canvas.Height < 8)
        {
            return;
        }

        canvas.Write(3, 1, "CALL", CellStyle.Border);
        canvas.Write(4, 1, Fit(state.Call, canvas.Width - 2), CellStyle.Active);
        canvas.Write(
            5,
            1,
            $"RST {state.Rst}  EXCH {state.Exchange1} {state.Exchange2}",
            CellStyle.Value);
        canvas.Write(
            7,
            1,
            $"F9 START  F10 STOP  ENTER LOG  F11 WIPE",
            CellStyle.Muted);
        if (canvas.Height > 10)
        {
            canvas.Write(
                9,
                1,
                $"SCORE {snapshot?.Score ?? 0}  PILEUP "
                + $"{snapshot?.ActiveStations?.Count ?? 0}",
                CellStyle.Accent);
        }

        canvas.Write(
            canvas.Height - 2,
            1,
            state.Status,
            StatusStyle(state.Status));
        canvas.Write(
            canvas.Height - 1,
            1,
            "Terminal is compact. Resize to at least 70 x 22.",
            CellStyle.Warning);
    }

    private static void RenderHelp(TerminalCanvas canvas)
    {
        canvas.DrawBox(
            0,
            0,
            canvas.Width - 1,
            canvas.Height - 1,
            " MORSE RUNNER XPLAT  |  KEYBOARD REFERENCE ");
        string[] lines =
        [
            "F1 CQ        F2 Exchange     F3 TU          F4 My call",
            "F5 His call  F6 QSO B4       F7 ?           F8 NIL",
            "F9 Pile-Up   Shift+F9 Single Ctrl+F9 HST    F10 Stop",
            "F11 Wipe     F12 NR?          Enter ESM",
            "",
            "Insert or semicolon sends his call and exchange.",
            "Period, comma, plus, or left bracket sends TU and logs.",
            "Tab and Shift+Tab move through entry fields.",
            "Up/Down changes RIT. Ctrl+Up/Down changes bandwidth.",
            "Page Up/Page Down changes speed. Escape aborts.",
            "Ctrl+Left/Right changes contest before a session.",
            "Alt+Left/Right changes run mode. Ctrl+D changes duration.",
            "Ctrl+1..6 toggles QSK, QSB, QRM, QRN, Flutter, and LIDs.",
            "Ctrl+P pauses, Ctrl+R resumes, Ctrl+W wipes, Ctrl+Q quits.",
            "",
            "Press ? to return.",
        ];

        int row = 2;
        foreach (string line in lines)
        {
            if (row >= canvas.Height - 1)
            {
                break;
            }

            canvas.Write(
                row,
                3,
                line,
                line.Length == 0 ? CellStyle.Default : CellStyle.Value);
            row++;
        }
    }

    private static string CallerState(OperatorState? state) =>
        state switch
        {
            null => "IDLE",
            OperatorState.NeedPreviousEnd => "WAITING",
            OperatorState.NeedQso => "CALLING",
            OperatorState.NeedNumber => "NEED EXCHANGE",
            OperatorState.NeedCall => "CORRECT CALL",
            OperatorState.NeedCallAndNumber => "CORRECT CALL+EXCHANGE",
            OperatorState.NeedEnd => "NEED TU",
            OperatorState.Done => "COMPLETE",
            OperatorState.Failed => "GONE",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

    private static string RunModeName(RunModeId runMode) =>
        runMode.Value switch
        {
            "rmPileup" => "Pile-Up",
            "rmSingle" => "Single Calls",
            "rmWpx" => "WPX Competition",
            "rmHst" => "HST Competition",
            _ => runMode.Value,
        };

    private static string DurationName(int minutes) =>
        minutes == 0 ? "unlimited" : $"{minutes} min";

    private static string JoinExchange(Qso qso)
    {
        if (String.IsNullOrWhiteSpace(qso.Exchange1))
        {
            return qso.Exchange2;
        }

        return String.IsNullOrWhiteSpace(qso.Exchange2)
            ? qso.Exchange1
            : qso.Exchange1 + " " + qso.Exchange2;
    }

    private static string QsoResult(Qso qso) =>
        qso.IsDuplicate
            ? "DUP"
            : qso.Points.ToString(CultureInfo.InvariantCulture);

    private static string Fit(string value, int width)
    {
        width = Math.Max(0, width);
        if (value.Length > width)
        {
            return width > 1 ? value[..(width - 1)] + "…" : value[..width];
        }

        return value.PadRight(width);
    }

    private enum CellStyle
    {
        Default,
        Border,
        Title,
        Accent,
        Active,
        Muted,
        Good,
        Warning,
        Error,
        Value,
        Call,
        Key,
    }

    private sealed class TerminalCanvas
    {
        private readonly char[] _characters;
        private readonly CellStyle[] _styles;

        public TerminalCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            _characters = new char[checked(width * height)];
            Array.Fill(_characters, ' ');
            _styles = new CellStyle[_characters.Length];
        }

        public int Width { get; }

        public int Height { get; }

        public void DrawBox(
            int left,
            int top,
            int right,
            int bottom,
            string title)
        {
            left = Math.Clamp(left, 0, Width - 1);
            right = Math.Clamp(right, left, Width - 1);
            top = Math.Clamp(top, 0, Height - 1);
            bottom = Math.Clamp(bottom, top, Height - 1);
            for (int column = left + 1; column < right; column++)
            {
                Set(top, column, '─', CellStyle.Border);
                Set(bottom, column, '─', CellStyle.Border);
            }

            for (int row = top + 1; row < bottom; row++)
            {
                Set(row, left, '│', CellStyle.Border);
                Set(row, right, '│', CellStyle.Border);
            }

            Set(top, left, '┌', CellStyle.Border);
            Set(top, right, '┐', CellStyle.Border);
            Set(bottom, left, '└', CellStyle.Border);
            Set(bottom, right, '┘', CellStyle.Border);
            if (!String.IsNullOrEmpty(title))
            {
                Write(top, left + 2, title, CellStyle.Title);
            }
        }

        public void Write(
            int row,
            int column,
            string value,
            CellStyle style)
        {
            if (row < 0 || row >= Height || String.IsNullOrEmpty(value))
            {
                return;
            }

            int sourceIndex = 0;
            if (column < 0)
            {
                sourceIndex = -column;
                column = 0;
            }

            while (sourceIndex < value.Length && column < Width)
            {
                Set(row, column, value[sourceIndex], style);
                sourceIndex++;
                column++;
            }
        }

        public void WriteRight(
            int row,
            int rightPadding,
            string value,
            CellStyle style)
        {
            int column = Math.Max(0, Width - rightPadding - value.Length);
            Write(row, column, value, style);
        }

        public string Render(bool useColor)
        {
            var output = new StringBuilder(
                checked(Width * Height + Height * Environment.NewLine.Length));
            for (int row = 0; row < Height; row++)
            {
                CellStyle currentStyle = CellStyle.Default;
                for (int column = 0; column < Width; column++)
                {
                    int index = checked((row * Width) + column);
                    CellStyle style = _styles[index];
                    if (useColor && style != currentStyle)
                    {
                        output.Append(Ansi(style));
                        currentStyle = style;
                    }

                    output.Append(_characters[index]);
                }

                if (useColor && currentStyle != CellStyle.Default)
                {
                    output.Append("\u001b[0m");
                }

                if (row < Height - 1)
                {
                    output.Append(Environment.NewLine);
                }
            }

            return output.ToString();
        }

        private void Set(
            int row,
            int column,
            char character,
            CellStyle style)
        {
            if (row < 0 || row >= Height || column < 0 || column >= Width)
            {
                return;
            }

            int index = checked((row * Width) + column);
            _characters[index] = character;
            _styles[index] = style;
        }

        private static string Ansi(CellStyle style) =>
            style switch
            {
                CellStyle.Default => "\u001b[0m",
                CellStyle.Border => "\u001b[0;36m",
                CellStyle.Title => "\u001b[1;36m",
                CellStyle.Accent => "\u001b[1;33m",
                CellStyle.Active => "\u001b[1;30;46m",
                CellStyle.Muted => "\u001b[0;90m",
                CellStyle.Good => "\u001b[1;32m",
                CellStyle.Warning => "\u001b[1;33m",
                CellStyle.Error => "\u001b[1;31m",
                CellStyle.Value => "\u001b[1;37m",
                CellStyle.Call => "\u001b[1;33m",
                CellStyle.Key => "\u001b[1;30;46m",
                _ => "\u001b[0m",
            };
    }
}
