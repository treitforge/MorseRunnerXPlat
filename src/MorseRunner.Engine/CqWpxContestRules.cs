using MorseRunner.Domain;

namespace MorseRunner.Engine;

public static class CqWpxContestRules
{
    public const int PointsPerQso = 1;

    public static string NormalizeCall(string call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return call
            .Replace("?", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
    }

    public static ContestValidation ValidateReceivedQso(
        string call,
        string rst,
        string serialNumber)
    {
        ArgumentNullException.ThrowIfNull(rst);
        ArgumentNullException.ThrowIfNull(serialNumber);
        string normalizedCall = NormalizeCall(call);
        if (normalizedCall.Length < 3)
        {
            return new(false, "Invalid callsign");
        }

        if (rst.Length != 3)
        {
            return new(false, "Missing/Invalid RST");
        }

        return serialNumber.Length == 0
            ? new(false, "Missing/Invalid Nr.")
            : new(true, string.Empty);
    }

    public static int ParseRst(string rst)
    {
        ArgumentNullException.ThrowIfNull(rst);
        string numeric = rst
            .Replace("A", "1", StringComparison.OrdinalIgnoreCase)
            .Replace("E", "5", StringComparison.OrdinalIgnoreCase)
            .Replace("N", "9", StringComparison.OrdinalIgnoreCase)
            .Replace("O", "0", StringComparison.OrdinalIgnoreCase)
            .Replace("T", "0", StringComparison.OrdinalIgnoreCase);
        return Int32.TryParse(numeric, out int value) ? value : 0;
    }

    public static int ParseSerialNumber(string serialNumber)
    {
        ArgumentNullException.ThrowIfNull(serialNumber);
        return Int32.TryParse(serialNumber, out int value) ? value : 0;
    }
}
