using System.Collections.ObjectModel;
using System.Globalization;

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
