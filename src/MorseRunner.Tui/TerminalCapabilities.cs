namespace MorseRunner.Tui;

public sealed record TerminalCapabilities(bool UseAnsi, bool UseColor)
{
    public static TerminalCapabilities Detect(
        string? term,
        string? noColor,
        bool forceNoColor)
    {
        bool useAnsi = !String.Equals(
            term,
            "dumb",
            StringComparison.OrdinalIgnoreCase);
        bool useColor = useAnsi
            && !forceNoColor
            && String.IsNullOrEmpty(noColor);
        return new(useAnsi, useColor);
    }

    public static TerminalCapabilities Detect(bool forceNoColor = false) =>
        Detect(
            Environment.GetEnvironmentVariable("TERM"),
            Environment.GetEnvironmentVariable("NO_COLOR"),
            forceNoColor);
}
