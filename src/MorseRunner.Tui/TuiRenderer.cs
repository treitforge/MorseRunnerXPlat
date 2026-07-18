using System.Globalization;
using System.Text;
using MorseRunner.Domain;

namespace MorseRunner.Tui;

public static class TuiRenderer
{
    public static string Render(TuiState state, int width, int height)
    {
        width = Math.Max(width, 40);
        height = Math.Max(height, 18);
        if (state.ShowHelp)
        {
            return RenderHelp(width, height);
        }

        var output = new StringBuilder(width * height);
        string title = state.IsHosted
            ? " MORSE RUNNER XPLAT  |  HOSTED TUI "
            : " MORSE RUNNER XPLAT  |  LOCAL TUI ";
        Line(output, Center(title, width, '='));

        SessionSnapshot? snapshot = state.Snapshot;
        string session = snapshot is null
            ? "READY"
            : snapshot.State.ToString().ToUpperInvariant();
        string elapsed = snapshot?.ElapsedSimulationTime.ToString(
            @"mm\:ss\.fff",
            CultureInfo.InvariantCulture) ?? "00:00.000";
        Line(
            output,
            Fit(
                $" {state.Contest.DisplayName}  |  {RunModeName(state.RunMode)}"
                + $"  |  {session}  |  {elapsed}  |  SCORE {snapshot?.Score ?? 0}",
                width));
        Line(
            output,
            Fit(
                $" Setup: Ctrl+Left/Right contest  Alt+Left/Right mode"
                + $"  Ctrl+D duration ({DurationName(state.DurationMinutes)})",
                width));
        Line(
            output,
            Fit(
                " Conditions Ctrl+1..6: "
                + $"QSK {OnOff(state.Qsk)}  QSB {OnOff(state.Qsb)}  "
                + $"QRM {OnOff(state.Qrm)}  QRN {OnOff(state.Qrn)}  "
                + $"Flutter {OnOff(state.Flutter)}  LIDs {OnOff(state.Lids)}",
                width));
        Line(output, new string('-', width));

        string[] labels = ["CALL", "RST", "EXCHANGE 1", "EXCHANGE 2"];
        int fieldWidth = Math.Max(8, (width - 13) / 4);
        for (int index = 0; index < labels.Length; index++)
        {
            string marker = state.ActiveField == index ? ">" : " ";
            output.Append(marker);
            output.Append(Fit($" {labels[index]} ", fieldWidth, '-'));
            if (index == labels.Length - 1)
            {
                output.AppendLine();
            }
            else
            {
                output.Append(" |");
            }
        }

        string[] values = state.Fields;
        for (int index = 0; index < values.Length; index++)
        {
            string marker = state.ActiveField == index ? ">" : " ";
            output.Append(marker);
            output.Append(Fit($" {values[index]}", fieldWidth));
            if (index == values.Length - 1)
            {
                output.AppendLine();
            }
            else
            {
                output.Append(" |");
            }
        }

        Line(output, new string('-', width));
        Line(
            output,
            Fit(
                $" F1 CQ  F2 EXCH  F3 TU  F4 MY CALL  F5 HIS CALL"
                + "  F6 B4  F7 ?  F8 NIL  F12 NR?",
                width));
        Line(
            output,
            Fit(
                " F9 START  SHIFT+F9 SINGLE  CTRL+F9 HST  F10 STOP"
                + "  ENTER LOG  F11 WIPE",
                width));
        Line(output, new string('-', width));

        int logRows = Math.Max(2, height - 15);
        Line(output, Fit(" TIME      CALL         RST  EXCHANGE                 PTS", width));
        IReadOnlyList<Qso> qsos = state.Qsos;
        int start = Math.Max(0, qsos.Count - logRows);
        for (int index = start; index < qsos.Count; index++)
        {
            Qso qso = qsos[index];
            Line(
                output,
                Fit(
                    $" {qso.Timestamp:HH:mm:ss}  {qso.Call,-12} {qso.Rst,-4}"
                    + $" {JoinExchange(qso),-24} {qso.Points,3}",
                    width));
        }

        for (int index = qsos.Count - start; index < logRows; index++)
        {
            Line(output, string.Empty);
        }

        Line(output, new string('-', width));
        Line(
            output,
            Fit(
                $" {state.Status}  |  RIT {snapshot?.RitOffsetHz ?? 0} Hz"
                + $"  BW {snapshot?.CurrentBandwidthHz ?? 500} Hz"
                + $"  WPM {snapshot?.CurrentWordsPerMinute ?? 30}",
                width));
        Line(
            output,
            Fit(
                " Tab fields  Up/Down RIT  Ctrl+Up/Down BW  PgUp/PgDn WPM"
                + "  ? help  Ctrl+Q quit",
                width));
        return output.ToString();
    }

    private static string RenderHelp(int width, int height)
    {
        string[] lines =
        [
            "KEYBOARD REFERENCE",
            string.Empty,
            "F1 CQ       F2 Exchange   F3 TU       F4 My call",
            "F5 His call F6 QSO B4     F7 ?        F8 NIL",
            "F9 Pile-Up  Shift+F9 Single calls    Ctrl+F9 HST",
            "F10 Stop    F11 Wipe       F12 NR?",
            string.Empty,
            "Enter logs. Insert or semicolon sends his call and exchange.",
            "Period, comma, plus, or left bracket sends TU and logs.",
            "Tab and Shift+Tab move through entry fields.",
            "Up/Down changes RIT. Ctrl+Up/Down changes bandwidth.",
            "Page Up/Page Down changes speed. Escape aborts.",
            "Ctrl+Left/Right changes contest before a session.",
            "Alt+Left/Right changes run mode. Ctrl+D changes duration.",
            "Ctrl+1..6 toggles QSK, QSB, QRM, QRN, Flutter, and LIDs.",
            "Ctrl+P pauses, Ctrl+R resumes, Ctrl+W wipes, Ctrl+Q quits.",
            string.Empty,
            "Press ? to return.",
        ];
        var output = new StringBuilder(width * height);
        Line(output, Center(" MORSE RUNNER XPLAT HELP ", width, '='));
        foreach (string line in lines)
        {
            Line(output, Fit($" {line}", width));
        }

        while (output.ToString().Count(character => character == '\n') < height)
        {
            Line(output, string.Empty);
        }

        return output.ToString();
    }

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

    private static string OnOff(bool value) => value ? "ON" : "off";

    private static string JoinExchange(Qso qso) =>
        string.Join(
            ' ',
            new[] { qso.Exchange1, qso.Exchange2 }
                .Where(value => !String.IsNullOrWhiteSpace(value)));

    private static string Center(string value, int width, char fill)
    {
        value = value.Length > width ? value[..width] : value;
        int left = Math.Max(0, (width - value.Length) / 2);
        int right = Math.Max(0, width - value.Length - left);
        return new string(fill, left) + value + new string(fill, right);
    }

    private static string Fit(string value, int width, char fill = ' ')
    {
        if (value.Length > width)
        {
            return width > 1 ? value[..(width - 1)] + "…" : value[..width];
        }

        return value.PadRight(width, fill);
    }

    private static void Line(StringBuilder output, string value)
    {
        output.AppendLine(value);
    }
}
