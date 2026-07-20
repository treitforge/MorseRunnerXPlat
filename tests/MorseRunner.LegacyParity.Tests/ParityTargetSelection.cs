namespace MorseRunner.LegacyParity.Tests;

public enum ParityTargetKind
{
    Legacy,
    XPlat,
}

internal static class ParityTargetSelection
{
    public const string EnvironmentVariableName = "MORSE_RUNNER_PARITY_TARGET";

    public static ParityTargetKind Current =>
        Parse(Environment.GetEnvironmentVariable(EnvironmentVariableName));

    internal static ParityTargetKind Parse(string? value)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return ParityTargetKind.XPlat;
        }

        if (Enum.TryParse(value.Trim(), ignoreCase: true, out ParityTargetKind target)
            && Enum.IsDefined(target))
        {
            return target;
        }

        throw new InvalidOperationException(
            $"{EnvironmentVariableName} must be Legacy or XPlat, but was '{value}'.");
    }
}

internal static class ParityTargetFactory
{
    public static IParityTarget CreateSelected(
        Func<IParityTarget> createLegacy,
        Func<IParityTarget> createXPlat)
    {
        return Create(ParityTargetSelection.Current, createLegacy, createXPlat);
    }

    internal static IParityTarget Create(
        ParityTargetKind target,
        Func<IParityTarget> createLegacy,
        Func<IParityTarget> createXPlat)
    {
        ArgumentNullException.ThrowIfNull(createLegacy);
        ArgumentNullException.ThrowIfNull(createXPlat);

        return target switch
        {
            ParityTargetKind.Legacy => createLegacy(),
            ParityTargetKind.XPlat => createXPlat(),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };
    }
}
