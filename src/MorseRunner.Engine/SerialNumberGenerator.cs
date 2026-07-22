using MorseRunner.Dsp;

namespace MorseRunner.Engine;

public sealed class SerialNumberGenerator
{
    private readonly DeterministicRandom _random;
    private readonly int _minimum;
    private readonly int _width;

    public SerialNumberGenerator(
        DeterministicRandom random,
        int minimum,
        int exclusiveMaximum)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimum, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            exclusiveMaximum,
            minimum);
        _random = random;
        _minimum = minimum;
        _width = exclusiveMaximum - minimum;
    }

    public int Next()
    {
        // Bin selection consumes a random value even for one bin.
        _random.Next(1);
        return _minimum + _random.Next(_width);
    }
}
