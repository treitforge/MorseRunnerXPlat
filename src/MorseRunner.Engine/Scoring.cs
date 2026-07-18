using System.Collections.ObjectModel;
using System.Globalization;
using MorseRunner.Domain;

namespace MorseRunner.Engine;

public static class ScoreFormatter
{
    public static string Format(int score) =>
        score.ToString(CultureInfo.InvariantCulture).PadLeft(6);
}

public sealed class MultiplierSet
{
    private readonly SortedSet<string> _values = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Values =>
        new ReadOnlyCollection<string>([.. _values]);

    public void Apply(string multipliers)
    {
        ArgumentNullException.ThrowIfNull(multipliers);
        foreach (string value in multipliers.Split(';'))
        {
            _values.Add(value);
        }
    }
}

public static class QsoRateCalculator
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public static int Calculate(
        IReadOnlyList<Qso> completedQsos,
        TimeSpan elapsedSimulationTime)
    {
        ArgumentNullException.ThrowIfNull(completedQsos);
        if (elapsedSimulationTime <= TimeSpan.Zero)
        {
            return 0;
        }

        TimeSpan duration = elapsedSimulationTime < Window
            ? elapsedSimulationTime
            : Window;
        DateTimeOffset cutoff = DateTimeOffset.UnixEpoch
            + elapsedSimulationTime
            - duration;
        int count = 0;
        for (int index = completedQsos.Count - 1; index >= 0; index--)
        {
            if (completedQsos[index].Timestamp <= cutoff)
            {
                break;
            }

            count++;
        }

        return (int)Math.Round(
            count * 3600d / duration.TotalSeconds,
            MidpointRounding.ToEven);
    }
}

public static class ContestResultCalculator
{
    public static int CalculateScore(
        ContestId contestId,
        IReadOnlyList<Qso> completedQsos)
    {
        ArgumentNullException.ThrowIfNull(completedQsos);
        Qso[] verified =
        [
            .. completedQsos.Where(
                qso => !qso.IsDuplicate
                    && qso.ExchangeError == LogError.None
                    && qso.Exchange1Error == LogError.None
                    && qso.Exchange1SecondaryError == LogError.None
                    && qso.Exchange2Error == LogError.None
                    && qso.Exchange2SecondaryError == LogError.None),
        ];
        int points = verified.Sum(qso => qso.Points);
        if (contestId.Value == "scHst")
        {
            return points;
        }

        int multipliers = verified
            .SelectMany(qso => qso.Multiplier.Split(';'))
            .Distinct(StringComparer.Ordinal)
            .Count();
        return points * multipliers;
    }
}
