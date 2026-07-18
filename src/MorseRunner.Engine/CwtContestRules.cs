using MorseRunner.Domain;

namespace MorseRunner.Engine;

public static class CwtContestRules
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
        string operatorName,
        string memberNumberOrQth)
    {
        ArgumentNullException.ThrowIfNull(operatorName);
        ArgumentNullException.ThrowIfNull(memberNumberOrQth);
        if (NormalizeCall(call).Length < 3)
        {
            return new(false, "Invalid callsign");
        }

        if (operatorName.Length <= 1)
        {
            return new(false, "Missing/Invalid Name");
        }

        return memberNumberOrQth.Length == 0
            ? new(false, "Missing/Invalid QTH")
            : new(true, string.Empty);
    }

    public static bool IsMemberExchange(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length > 0
            && value.All(character => character is >= '0' and <= '9');
    }
}
