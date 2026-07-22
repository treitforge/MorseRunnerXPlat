namespace MorseRunner.Dsp;

public sealed class LegacyReceiverNoiseGenerator
{
    private const double HissAmplitude = 18_000d;
    private const double QrnImpulseAmplitude = 360_000d;
    private const double QrnTriggerProbability = 0.01d;
    private readonly LegacyRandom _random;

    public LegacyReceiverNoiseGenerator(LegacyRandom random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public bool PrepareInput(
        Span<float> real,
        Span<float> imaginary,
        bool qrnEnabled)
    {
        if (real.Length != imaginary.Length)
        {
            throw new ArgumentException(
                "Receiver real and imaginary buffers must have equal "
                + "lengths.");
        }

        for (int index = 0; index < real.Length; index++)
        {
            real[index] = (float)(
                HissAmplitude * (_random.NextDouble() - 0.5d));
            imaginary[index] = (float)(
                HissAmplitude * (_random.NextDouble() - 0.5d));
        }

        if (!qrnEnabled)
        {
            return false;
        }

        for (int index = 0; index < real.Length; index++)
        {
            if (_random.NextDouble() < QrnTriggerProbability)
            {
                real[index] = (float)(
                    QrnImpulseAmplitude
                    * (_random.NextDouble() - 0.5d));
            }
        }

        return _random.NextDouble() < QrnTriggerProbability;
    }
}
